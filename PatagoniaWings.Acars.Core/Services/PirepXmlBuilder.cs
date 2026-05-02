using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    /// <summary>
    /// Constructor oficial del PIREP RAW generado por ACARS.
    ///
    /// Regla de arquitectura Patagonia Wings:
    /// - ACARS registra evidencia operacional, telemetria y cierre.
    /// - ACARS NO calcula el puntaje oficial.
    /// - Supabase/Web evalua posteriormente el XML/JSON raw contra el reglaje vigente.
    ///
    /// La estructura replica el formato base del PIREP de referencia SUR/simulador:
    /// PIREP > Simulador, Despacho, Vuelo, Resumen, Indicadores, Aeropuertos,
    /// Evaluacion, Procedimientos, Performance, Meteorologia, Avion y Economia.
    /// </summary>
    public sealed class PirepXmlBuilder
    {
        private const double LbsToKg = 0.45359237d;

        public sealed class BuildResult
        {
            public string FileName { get; set; } = string.Empty;
            public string XmlContent { get; set; } = string.Empty;
            public string ChecksumSha256 { get; set; } = string.Empty;
            public DateTime GeneratedAtUtc { get; set; }
        }

        public BuildResult Build(
            PreparedDispatch dispatch,
            Pilot pilot,
            FlightReport report,
            Flight? activeFlight,
            IReadOnlyList<SimData>? telemetryLog)
        {
            if (dispatch == null) throw new ArgumentNullException(nameof(dispatch));
            if (pilot == null) throw new ArgumentNullException(nameof(pilot));
            if (report == null) throw new ArgumentNullException(nameof(report));

            var generatedAtUtc = DateTime.UtcNow;
            var telemetry = (telemetryLog ?? Array.Empty<SimData>())
                .Where(sample => sample != null)
                .OrderBy(sample => sample.CapturedAtUtc)
                .ToList();
            var firstSample = telemetry.Count > 0 ? telemetry[0] : null;
            var lastSample = telemetry.Count > 0 ? telemetry[telemetry.Count - 1] : null;
            var firstAirborne = telemetry.FirstOrDefault(sample => !sample.OnGround && sample.AltitudeAGL > 30d);
            var touchdown = FindTouchdownSample(telemetry);

            var blockMinutes = Math.Max(1, (int)Math.Round(GetDuration(report, telemetry).TotalMinutes));
            var flightMinutes = ComputeAirborneMinutes(telemetry, report);
            var distanceNm = ResolveDistanceNm(report, telemetry);
            var fuelStartKg = ResolveFuelKg(firstSample, dispatch.FuelPlannedKg);
            var fuelEndKg = ResolveFuelKg(lastSample, 0d);
            var fuelUsedKg = ResolveFuelUsedKg(report, fuelStartKg, fuelEndKg);
            var fuelPerHour = blockMinutes > 0 ? fuelUsedKg / Math.Max(1d / 60d, blockMinutes / 60d) : 0d;
            var fuelPer100Nm = distanceNm > 0 ? (fuelUsedKg / distanceNm) * 100d : 0d;

            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("PIREP",
                    Element("AcarsVersion", "PatagoniaWings-RAW-1.0"),
                    Element("FechaPIREP", FormatLocalDateTime(generatedAtUtc)),
                    Element("PilotoNum", ExtractPilotNumber(pilot.CallSign)),
                    Element("Nombre", ResolveFirstName(pilot.FullName, pilot.CallSign)),
                    Element("Apellido", ResolveLastName(pilot.FullName)),
                    BuildSimulador(report, dispatch, firstSample),
                    BuildDespacho(dispatch, report),
                    BuildCapabilities(dispatch, activeFlight, firstSample, lastSample),
                    BuildOperationalChecks(telemetry, dispatch, activeFlight),
                    BuildFlightPhaseSummary(telemetry, report),
                    BuildEventTimeline(telemetry, dispatch, activeFlight, report),
                    BuildVuelo(telemetry, report, generatedAtUtc),
                    BuildResumen(dispatch, report, telemetry, firstSample, lastSample, firstAirborne, touchdown, blockMinutes, flightMinutes, distanceNm, fuelStartKg, fuelEndKg, fuelUsedKg, fuelPerHour, fuelPer100Nm),
                    BuildIndicadores(telemetry, report, blockMinutes),
                    BuildAeropuertos(dispatch, report, firstSample, touchdown, lastSample),
                    BuildEvaluacionPendiente(),
                    BuildProcedimientosPendiente(),
                    BuildPerformancePendiente(report),
                    BuildMeteorologia(firstSample, lastSample),
                    BuildAvion(dispatch, activeFlight, lastSample, fuelEndKg),
                    BuildScoringInput(dispatch, report, activeFlight, telemetry, touchdown),
                    BuildEconomiaPendiente()
                )
            );

            var xml = document.ToString(SaveOptions.None);
            return new BuildResult
            {
                FileName = BuildFileName(dispatch, report, generatedAtUtc),
                XmlContent = xml,
                ChecksumSha256 = Sha256(xml),
                GeneratedAtUtc = generatedAtUtc
            };
        }

        private static XElement BuildSimulador(FlightReport report, PreparedDispatch dispatch, SimData? sample)
        {
            var simVersion = sample == null ? report.Simulator.ToString() : sample.SimulatorType.ToString();
            return new XElement("Simulador",
                Element("SimVersion", string.IsNullOrWhiteSpace(simVersion) ? "MSFS" : simVersion),
                Element("SimAvionTipo", FirstNonEmpty(dispatch.AircraftIcao, report.AircraftIcao)),
                Element("SimCertificado", "True"),
                Element("SimAvionAuthor", FirstNonEmpty(dispatch.AddonProvider, "")),
                Element("SimAvionAuthorRaw", FirstNonEmpty(dispatch.AddonProvider, "")),
                Element("SimAvionRaw", sample == null ? string.Empty : sample.AircraftTitle),
                Element("SimFecha", DateTime.UtcNow.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)),
                Element("SimHora", DateTime.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                Element("SimLiveWeather", "True"),
                Element("SimCrashEnabled", "True")
            );
        }

        private static XElement BuildDespacho(PreparedDispatch dispatch, FlightReport report)
        {
            return new XElement("Despacho",
                Element("NroVuelo", FirstNonEmpty(dispatch.FlightDesignator, report.FlightNumber)),
                Element("IdDespacho", FirstNonEmpty(dispatch.DispatchId, dispatch.DispatchToken, dispatch.ReservationId)),
                Element("AirlineCode", "PWG"),
                Element("TipoVuelo", NormalizeFlightMode(dispatch.FlightMode)),
                Element("Certificacion", FirstNonEmpty(dispatch.AircraftVariantCode, dispatch.AddonProvider)),
                Element("Avion", FirstNonEmpty(dispatch.AircraftIcao, report.AircraftIcao)),
                Element("ModeloCertificado", dispatch.AircraftDisplayName),
                Element("Registracion", dispatch.AircraftRegistration),
                Element("PAX", ToIntString(dispatch.PassengerCount)),
                Element("Carga", ToIntString(dispatch.CargoKg)),
                Element("Origen", FirstNonEmpty(dispatch.DepartureIcao, report.DepartureIcao)),
                Element("Destino", FirstNonEmpty(dispatch.ArrivalIcao, report.ArrivalIcao)),
                Element("Alternativo", dispatch.AlternateIcao),
                Element("Gate", "0"),
                Element("FuelPlan", ToIntString(dispatch.FuelPlannedKg)),
                Element("ETD", FormatTimeOrBlank(dispatch.ScheduledDepartureUtc)),
                Element("ETA", string.Empty),
                Element("ConsumoPromedio", "0"),
                Element("FlightLevel", dispatch.CruiseLevel),
                Element("IdTipoVuelo", FlightModeId(dispatch.FlightMode)),
                Element("Ruta", dispatch.RouteText)
            );
        }

        private static XElement BuildVuelo(IReadOnlyList<SimData> telemetry, FlightReport report, DateTime generatedAtUtc)
        {
            var rows = new List<XElement>
            {
                Element("Log", "TIME      EVENT                 LATITUDE       LONGITUDE   HEAD    ALT   KIAS  F/M VS   FUEL  DI NM  B.DUR  FtAGL   IndAlt  altCfg  G/S   BANK ")
            };

            if (telemetry == null || telemetry.Count == 0)
            {
                rows.Add(Element("Log", FormatLogLine(generatedAtUtc, "START", null, 0d, generatedAtUtc)));
                rows.Add(Element("Log", FormatLogLine(generatedAtUtc, "STOP", null, 0d, generatedAtUtc)));
                return new XElement("Vuelo", rows);
            }

            var first = telemetry[0];
            SimData? previous = null;
            var cumulativeDistance = 0d;

            for (var index = 0; index < telemetry.Count; index++)
            {
                var sample = telemetry[index];
                if (index > 0)
                {
                    cumulativeDistance += DistanceNm(telemetry[index - 1], sample);
                }

                var eventText = ResolveEventText(previous, sample, index, report);
                rows.Add(Element("Log", FormatLogLine(sample.CapturedAtUtc, eventText, sample, cumulativeDistance, first.CapturedAtUtc)));
                previous = sample;
            }

            rows.Add(Element("Log", FormatLogLine(telemetry[telemetry.Count - 1].CapturedAtUtc, "STOP", telemetry[telemetry.Count - 1], cumulativeDistance, first.CapturedAtUtc)));
            return new XElement("Vuelo", rows);
        }

        private static XElement BuildResumen(
            PreparedDispatch dispatch,
            FlightReport report,
            IReadOnlyList<SimData> telemetry,
            SimData? firstSample,
            SimData? lastSample,
            SimData? firstAirborne,
            SimData? touchdown,
            int blockMinutes,
            int flightMinutes,
            double distanceNm,
            double fuelStartKg,
            double fuelEndKg,
            double fuelUsedKg,
            double fuelPerHour,
            double fuelPer100Nm)
        {
            var maxIas = telemetry.Count == 0 ? report.MaxSpeedKts : telemetry.Max(sample => sample.IndicatedAirspeed);
            var maxIasFl100 = telemetry.Count == 0 ? 0d : telemetry.Where(sample => sample.AltitudeFeet < 10000d).Select(sample => sample.IndicatedAirspeed).DefaultIfEmpty(0d).Max();
            var maxAltitude = telemetry.Count == 0 ? report.MaxAltitudeFeet : telemetry.Max(sample => sample.AltitudeFeet);
            var takeoffWeight = firstAirborne == null ? 0d : firstAirborne.TotalWeightKg;
            var landingWeight = touchdown == null ? (lastSample == null ? 0d : lastSample.TotalWeightKg) : touchdown.TotalWeightKg;

            return new XElement("Resumen",
                Element("FlightDuration", FormatDuration(TimeSpan.FromMinutes(Math.Max(0, flightMinutes)))),
                Element("BlockDuration", FormatDuration(TimeSpan.FromMinutes(Math.Max(0, blockMinutes)))),
                Element("MaxIAS", ToIntString(maxIas)),
                Element("MaxIAS-FL100", ToIntString(maxIasFl100)),
                Element("TouchDownVS", ToIntString(report.LandingVS)),
                Element("TakeOffWeight", ToIntString(takeoffWeight)),
                Element("LandingWeight", ToIntString(landingWeight)),
                Element("TakeOffFuel", ToIntString(fuelStartKg)),
                Element("SpentFuel", ToIntString(fuelUsedKg)),
                Element("FinalFuel", ToIntString(fuelEndKg)),
                Element("FuelPerHour", FormatDecimal(fuelPerHour, 1)),
                Element("FuelPer100NM", FormatDecimal(fuelPer100Nm, 1)),
                Element("FlightLenght", ToIntString(distanceNm)),
                Element("CruiceAltitud", ToIntString(maxAltitude)),
                Element("PCTakeoffTime", FormatClock(report.TakeoffTimeUtc)),
                Element("PCLandingTime", FormatClock(report.TouchdownTimeUtc)),
                Element("FSTakeoffTime", FormatClock(report.TakeoffTimeUtc)),
                Element("FSLandingTime", FormatClock(report.TouchdownTimeUtc)),
                Element("BlockStartTime", FormatClock(firstSample == null ? report.DepartureTime : firstSample.CapturedAtUtc)),
                Element("BlockEndTime", FormatClock(lastSample == null ? report.ArrivalTime : lastSample.CapturedAtUtc)),
                Element("IncialLongitude", FormatDecimal(firstSample == null ? 0d : firstSample.Longitude, 5)),
                Element("InicialLatitude", FormatDecimal(firstSample == null ? 0d : firstSample.Latitude, 5)),
                Element("FinalLongitude", FormatDecimal(lastSample == null ? 0d : lastSample.Longitude, 5)),
                Element("FinalLatitude", FormatDecimal(lastSample == null ? 0d : lastSample.Latitude, 5)),
                Element("AjusteMinutosLlegada", "0"),
                Element("AjustePorUsoHorario", "0"),
                Element("Comentarios", report.Remarks)
            );
        }

        private static XElement BuildIndicadores(IReadOnlyList<SimData> telemetry, FlightReport report, int blockMinutes)
        {
            var overspeedSamples = telemetry.Count(sample => sample.IndicatedAirspeed > 265d && sample.AltitudeFeet < 10000d || sample.IndicatedAirspeed > 380d);
            var stallSamples = telemetry.Count(sample => !sample.OnGround && sample.IndicatedAirspeed > 0d && sample.IndicatedAirspeed < 55d);
            var pauseSamples = telemetry.Count(sample => sample.Pause);
            var maxAscent = telemetry.Count == 0 ? 0d : telemetry.Max(sample => sample.VerticalSpeed);
            var maxDescent = telemetry.Count == 0 ? 0d : telemetry.Min(sample => sample.VerticalSpeed);
            var touchdown = FindTouchdownSample(telemetry);
            var touchdownG = ResolveGForce(touchdown, report.LandingG);
            var maxG = telemetry.Count == 0 ? touchdownG : telemetry.Select(sample => ResolveGForce(sample, touchdownG)).DefaultIfEmpty(touchdownG).Max();
            var minG = telemetry.Count == 0 ? touchdownG : telemetry.Select(sample => ResolveGForce(sample, touchdownG)).DefaultIfEmpty(touchdownG).Min();

            return new XElement("Indicadores",
                Element("OverspeedSecs", ToIntString(overspeedSamples)),
                Element("StallSecs", ToIntString(stallSamples)),
                Element("MaxAscentrate", ToIntString(maxAscent)),
                Element("MaxDescentrate", ToIntString(maxDescent)),
                Element("TiempoenPausa", ToIntString(pauseSamples)),
                Element("PctenPausa", blockMinutes <= 0 ? "0 %" : FormatDecimal((pauseSamples / Math.Max(1d, telemetry.Count)) * 100d, 0) + " %"),
                Element("TiempoPreTO", "00:00"),
                Element("TiempoPostParking", "00:00"),
                Element("CantTouchdowns", Math.Abs(report.LandingVS) > 0d ? "1" : "0"),
                Element("TouchdownGForce", FormatDecimal(touchdownG, 3)),
                Element("TouchdownVS", ToIntString(report.LandingVS)),
                Element("MaxGForce", FormatDecimal(maxG, 3)),
                Element("MinGForce", FormatDecimal(minG, 3)),
                Element("PICsFailed", Math.Max(0, report.PicChecksFailed).ToString(CultureInfo.InvariantCulture)),
                Element("CantidadPICs", Math.Max(0, report.PicChecksCompleted).ToString(CultureInfo.InvariantCulture)),
                Element("PICsOK", Math.Max(0, report.PicChecksSucceeded).ToString(CultureInfo.InvariantCulture)),
                Element("PICsTotalProgramados", Math.Max(0, report.PicChecksTotal).ToString(CultureInfo.InvariantCulture)),
                Element("PICRadio", string.IsNullOrWhiteSpace(report.PicRadioSource) ? "COM1_COM2" : report.PicRadioSource),
                Element("PICUltimaFrecuencia", FormatFrequency(report.LastPicRequiredFrequencyMhz)),
                Element("Networkd", "OFFLINE")
            );
        }

        private static XElement BuildAeropuertos(PreparedDispatch dispatch, FlightReport report, SimData? firstSample, SimData? touchdown, SimData? lastSample)
        {
            var touchdownG = ResolveGForce(touchdown, report.LandingG);
            return new XElement("Aeropuertos",
                new XElement("Despegue",
                    Element("ICAO", FirstNonEmpty(dispatch.DepartureIcao, report.DepartureIcao)),
                    Element("Pista", string.Empty),
                    Element("Gate", "0"),
                    Element("Altura", ToIntString(firstSample == null ? 0d : firstSample.AltitudeFeet)),
                    Element("LargoPistaMts", "0")
                ),
                new XElement("Aterrizaje",
                    Element("ICAO", FirstNonEmpty(dispatch.ArrivalIcao, report.ArrivalIcao)),
                    Element("Pista", string.Empty),
                    Element("Gate", "0"),
                    Element("Altura", ToIntString(lastSample == null ? 0d : lastSample.AltitudeFeet)),
                    Element("LargoPistaMts", "0"),
                    Element("CantTouchdowns", Math.Abs(report.LandingVS) > 0d ? "1" : "0"),
                    Element("TouchdownGForce", FormatDecimal(touchdownG, 3)),
                    Element("TouchdownVS", ToIntString(report.LandingVS)),
                    Element("TouchdownDistCentro", "0"),
                    Element("TouchdownDistInicio", "0"),
                    Element("TouchdownTDZ", "PENDING_SERVER_EVALUATION"),
                    Element("TouchdownLongitud", FormatDecimal(touchdown == null ? 0d : touchdown.Longitude, 5)),
                    Element("TouchdownLatitud", FormatDecimal(touchdown == null ? 0d : touchdown.Latitude, 5)),
                    Element("TouchdownSpeed", ToIntString(touchdown == null ? 0d : touchdown.IndicatedAirspeed)),
                    Element("TouchdownBank", ToIntString(touchdown == null ? 0d : touchdown.Bank))
                )
            );
        }

        private static XElement BuildEvaluacionPendiente()
        {
            return new XElement("Evaluacion",
                Element("Estado", "PENDING_SERVER_EVALUATION"),
                Element("PuntosFinales", "0")
            );
        }

        private static XElement BuildProcedimientosPendiente()
        {
            return new XElement("Procedimientos",
                Element("Estado", "PENDING_SERVER_EVALUATION"),
                Element("PuntosFinales", "0"),
                new XElement("Detalle")
            );
        }

        private static XElement BuildPerformancePendiente(FlightReport report)
        {
            return new XElement("Performance",
                Element("Estado", "PENDING_SERVER_EVALUATION"),
                Element("PuntosFinales", "0"),
                Element("PuntosPlanificacion", "0"),
                Element("PuntosTierra", "0"),
                Element("PuntosDespegue", "0"),
                Element("PuntosAterrizaje", "0"),
                Element("PuntosGenerales", "0"),
                new XElement("Detalle"),
                new XElement("PuntosExtra",
                    Element("Totales", "0"),
                    Element("PuntosPorPIC", report.PicChecksFailed > 0 ? (-5 * report.PicChecksFailed).ToString(CultureInfo.InvariantCulture) : (report.PicChecksSucceeded > 0 ? (3 * report.PicChecksSucceeded).ToString(CultureInfo.InvariantCulture) : "0")),
                    Element("PuntosPorStall", "0"),
                    Element("PuntosPorOverspeed", "0"),
                    Element("PuntosPorBonificacion", "0"),
                    Element("PuntosPorBautismo", "0"),
                    Element("PuntosPorTipoVuelo", "0"),
                    Element("PuntosPorComplejidad", "0")
                ),
                new XElement("Banderas",
                    Element("CompletoVuelo", "False"),
                    Element("CentradoTDZ", "False")
                )
            );
        }

        private static XElement BuildMeteorologia(SimData firstSample, SimData lastSample)
        {
            return new XElement("Meteorologia",
                BuildWeatherNode("Despegue", firstSample),
                BuildWeatherNode("Aterrizaje", lastSample, true)
            );
        }

        private static XElement BuildWeatherNode(string name, SimData? sample, bool includeMinimum = false)
        {
            var node = new XElement(name,
                Element("Vientos", sample == null ? "0/0kts" : ToIntString(sample.WindDirection) + "/" + ToIntString(sample.WindSpeed) + "kts"),
                Element(name == "Despegue" ? "VientoSalidaDireccion" : "VientoLlegadaDireccion", ToIntString(sample == null ? 0d : sample.WindDirection)),
                Element(name == "Despegue" ? "VientoSalidaVelocidad" : "VientoLlegadaVelocidad", ToIntString(sample == null ? 0d : sample.WindSpeed)),
                Element("VisibilidadKm", "0"),
                Element("CloudBaseFt", "0"),
                Element("CloudType", string.Empty),
                Element("CloudBase2Ft", "0"),
                Element("CloudType2", string.Empty),
                Element("TemperaturaCelcius", ToIntString(sample == null ? 0d : sample.OutsideTemperature)),
                Element("Presion", ToIntString(sample == null ? 0d : sample.QNH)),
                Element("Raining", sample != null && sample.IsRaining ? "LLUEVE" : "NO_LLUEVE"),
                Element("Superficie", "PENDING_SERVER_EVALUATION"),
                Element("VientoCruzado", "0"),
                Element("VientoCola", "0")
            );
            if (includeMinimum)
            {
                node.Add(Element("AlturaMinimaIMC", "0"));
            }
            return node;
        }

        private static XElement BuildAvion(PreparedDispatch dispatch, Flight? activeFlight, SimData? lastSample, double finalFuelKg)
        {
            var profile = ResolveAircraftProfile(dispatch, activeFlight, lastSample);
            var engineHealth = EstimateEngineHealth(lastSample);
            var xpdrSupported = profile.SupportsTransponderModeSystem || profile.SupportsSquawkSystem;
            var fuelCapacityKg = lastSample == null ? 0d : lastSample.FuelTotalCapacityLbs * LbsToKg;

            return new XElement("Avion",
                Element("PerfilPatagonia", profile.Code),
                Element("NombreRealPatagonia", profile.DisplayName),
                Element("ProveedorAddon", profile.AddonProvider),
                Element("MatriculaPatagonia", FirstNonEmpty(dispatch.AircraftRegistration)),
                Element("TituloSimulador", lastSample == null ? string.Empty : lastSample.AircraftTitle),
                Element("Com1", FormatFrequency(lastSample == null ? 0d : lastSample.Com1FrequencyMhz)),
                Element("Com1Standby", FormatFrequency(lastSample == null ? 0d : lastSample.Com1StandbyFrequencyMhz)),
                Element("Com2", FormatFrequency(lastSample == null ? 0d : lastSample.Com2FrequencyMhz)),
                Element("Com2Standby", FormatFrequency(lastSample == null ? 0d : lastSample.Com2StandbyFrequencyMhz)),
                Element("Nav1", "0.00"),
                Element("Nav2", "0.00"),
                Element("Nav1OBS", "0"),
                Element("Transpondedor", xpdrSupported && lastSample != null ? lastSample.TransponderCode.ToString(CultureInfo.InvariantCulture) : "N/D"),
                Element("TranspondedorEstado", xpdrSupported && lastSample != null ? ToIntString(lastSample.TransponderStateRaw) : "N/D"),
                Element("TranspondedorSoportado", Bool(xpdrSupported)),
                Element("TranspondedorFuente", xpdrSupported ? FirstNonEmpty(profile.TransponderStateSource, "native") : "unsupported"),
                Element("TranspondedorPenaltyEligible", Bool(xpdrSupported)),
                Element("Combustible", ToIntString(finalFuelKg)),
                Element("CombustibleCapacidad", fuelCapacityKg > 10d ? ToIntString(fuelCapacityKg) : "N/D"),
                Element("CombustibleLeft", ToIntString(lastSample == null ? 0d : lastSample.FuelLeftTankLbs * LbsToKg)),
                Element("CombustibleRight", ToIntString(lastSample == null ? 0d : lastSample.FuelRightTankLbs * LbsToKg)),
                Element("CombustibleCenter", ToIntString(lastSample == null ? 0d : lastSample.FuelCenterTankLbs * LbsToKg)),
                Element("ParkingBreak", Bool(lastSample != null && lastSample.ParkingBrake)),
                Element("BatteryMaster", profile.SupportsBatteryRead ? Bool(lastSample != null && lastSample.BatteryMasterOn) : "N/D"),
                Element("AvionicsMaster", profile.SupportsAvionicsRead ? Bool(lastSample != null && lastSample.AvionicsMasterOn) : "N/D"),
                Element("ElectricalMainBusVoltage", FormatDecimal(lastSample == null ? 0d : lastSample.ElectricalMainBusVoltage, 1)),
                Element("DoorOpen", profile.SupportsDoorSystem ? Bool(lastSample != null && lastSample.DoorOpen) : "N/D"),
                Element("NavLights", profile.SupportsLightsRead ? Bool(lastSample != null && lastSample.NavLightsOn) : "N/D"),
                Element("BeaconLights", profile.SupportsLightsRead ? Bool(lastSample != null && lastSample.BeaconLightsOn) : "N/D"),
                Element("StrobeLights", profile.SupportsLightsRead ? Bool(lastSample != null && lastSample.StrobeLightsOn) : "N/D"),
                Element("TaxiLights", profile.SupportsLightsRead ? Bool(lastSample != null && lastSample.TaxiLightsOn) : "N/D"),
                Element("LandingLights", profile.SupportsLightsRead ? Bool(lastSample != null && lastSample.LandingLightsOn) : "N/D"),
                Element("SeatBeltSign", profile.SupportsSeatbeltSystem ? Bool(lastSample != null && lastSample.SeatBeltSign) : "N/D"),
                Element("NoSmokingSign", profile.SupportsNoSmokingSystem ? Bool(lastSample != null && lastSample.NoSmokingSign) : "N/D"),
                Element("Autopilot", Bool(lastSample != null && lastSample.AutopilotActive)),
                Element("APURunning", profile.SupportsApuSystem ? Bool(lastSample != null && lastSample.ApuRunning) : "N/D"),
                Element("BleedAir", profile.SupportsBleedAirSystem ? Bool(lastSample != null && lastSample.BleedAirOn) : "N/D"),
                Element("GForce", FormatDecimal(ResolveGForce(lastSample, 0d), 3)),
                Element("EstadoTren", profile.SupportsGearRead ? FormatDecimal(lastSample == null ? 0d : (lastSample.GearDown ? 100d : 0d), 3) : "N/D"),
                Element("EstadoMotores", FormatDecimal(engineHealth, 3)),
                Element("EstadoFuselaje", "100.000"),
                Element("APUInstalada", Bool(profile.HasApu)),
                Element("PacksInstalados", Bool(profile.IsPressurized)),
                Element("ATInstalado", "True"),
                Element("TieneCrew", "True"),
                Element("TieneCopiloto", "True"),
                Element("AbrePuertas", Bool(profile.SupportsDoorSystem)),
                Element("DetectaFuego", "PENDING_VALIDATION"),
                Element("TieneEngineMode", "PENDING_VALIDATION"),
                Element("MTOW", ToIntString(lastSample == null ? 0d : lastSample.TotalWeightKg)),
                Element("ZFW", ToIntString(lastSample == null ? 0d : lastSample.ZeroFuelWeightKg)),
                Element("PayloadKg", ToIntString(lastSample == null ? 0d : lastSample.PayloadKg)),
                Element("EmptyWeightKg", ToIntString(lastSample == null ? 0d : lastSample.EmptyWeightKg)),
                Element("MLW", "0"),
                new XElement("Mantenimiento",
                    Element("EnviarMantenimiento", "No"),
                    new XElement("Fallas")
                )
            );
        }

        private static XElement BuildCapabilities(PreparedDispatch dispatch, Flight? activeFlight, SimData? firstSample, SimData? lastSample)
        {
            var profile = ResolveAircraftProfile(dispatch, activeFlight, lastSample ?? firstSample);
            var xpdrSupported = profile.SupportsTransponderModeSystem || profile.SupportsSquawkSystem;

            return new XElement("Capabilities",
                Element("AircraftProfile", profile.Code),
                Element("DisplayName", profile.DisplayName),
                Element("AddonProvider", profile.AddonProvider),
                Element("CapabilityAuditState", profile.CapabilityAuditState),
                CapabilityMetric("XPDR", xpdrSupported, xpdrSupported ? FirstNonEmpty(profile.TransponderStateSource, "native") : "unsupported", xpdrSupported, xpdrSupported ? "confirmed_or_profile_enabled" : "not_available_for_aircraft"),
                CapabilityMetric("Squawk", profile.SupportsSquawkSystem, profile.SupportsSquawkSystem ? FirstNonEmpty(profile.TransponderStateSource, "native") : "unsupported", profile.SupportsSquawkSystem, profile.SupportsSquawkSystem ? "confirmed_or_profile_enabled" : "not_available_for_aircraft"),
                CapabilityMetric("Doors", profile.SupportsDoorSystem, profile.SupportsDoorSystem ? FirstNonEmpty(profile.DoorSource, "native") : "unsupported", profile.SupportsDoorSystem, profile.SupportsDoorSystem ? "confirmed_or_profile_enabled" : "not_available_for_aircraft"),
                CapabilityMetric("ParkingBrake", profile.SupportsParkingBrakeRead, "SimConnect", profile.SupportsParkingBrakeRead, "profile"),
                CapabilityMetric("Fuel", profile.SupportsFuelRead, "SimConnect", profile.SupportsFuelRead, "profile"),
                CapabilityMetric("Lights", profile.SupportsLightsRead, "SimConnect", profile.SupportsLightsRead, "profile"),
                CapabilityMetric("Gear", profile.SupportsGearRead, "SimConnect", profile.SupportsGearRead, profile.SupportsGearRead ? "profile" : "fixed_gear_or_unsupported"),
                CapabilityMetric("Battery", profile.SupportsBatteryRead, "SimConnect", profile.SupportsBatteryRead, profile.SupportsBatteryRead ? "profile" : "not_available_for_aircraft"),
                CapabilityMetric("Avionics", profile.SupportsAvionicsRead, "SimConnect", profile.SupportsAvionicsRead, profile.SupportsAvionicsRead ? "profile" : "not_available_for_aircraft"),
                CapabilityMetric("EngineRun", profile.SupportsEngineRunRead, "SimConnect", profile.SupportsEngineRunRead, profile.SupportsEngineRunRead ? "profile" : "n1_fallback"),
                CapabilityMetric("Payload", profile.SupportsPayloadRead, "DispatchOrSimConnect", profile.SupportsPayloadRead, profile.SupportsPayloadRead ? "profile" : "dispatch_only"),
                CapabilityMetric("Touchdown", true, "OnGroundTransition", true, "computed_from_raw_telemetry"),
                CapabilityMetric("GForce", true, "SimConnectSanitized", true, "sanitized_range_minus3_to_8")
            );
        }

        private static XElement CapabilityMetric(string name, bool supported, string source, bool penaltyEligible, string reason)
        {
            return new XElement("Metric",
                new XAttribute("name", name),
                new XAttribute("supported", Bool(supported)),
                new XAttribute("source", string.IsNullOrWhiteSpace(source) ? "unknown" : source),
                new XAttribute("penaltyEligible", Bool(penaltyEligible)),
                new XAttribute("reason", string.IsNullOrWhiteSpace(reason) ? "profile" : reason));
        }

        private static XElement BuildOperationalChecks(IReadOnlyList<SimData> telemetry, PreparedDispatch dispatch, Flight? activeFlight)
        {
            var first = telemetry == null || telemetry.Count == 0 ? null : telemetry[0];
            var last = telemetry == null || telemetry.Count == 0 ? null : telemetry[telemetry.Count - 1];
            var profile = ResolveAircraftProfile(dispatch, activeFlight, last ?? first);
            var takeoff = telemetry == null ? null : telemetry.FirstOrDefault(sample => !sample.OnGround && sample.AltitudeAGL > 30d);
            var touchdown = FindTouchdownSample(telemetry ?? Array.Empty<SimData>());

            return new XElement("OperationalChecks",
                new XElement("Preflight",
                    Element("AircraftProfile", profile.Code),
                    Element("AircraftName", profile.DisplayName),
                    Element("Registration", FirstNonEmpty(dispatch.AircraftRegistration)),
                    Element("Origin", FirstNonEmpty(dispatch.DepartureIcao, activeFlight == null ? string.Empty : activeFlight.DepartureIcao)),
                    Element("Destination", FirstNonEmpty(dispatch.ArrivalIcao, activeFlight == null ? string.Empty : activeFlight.ArrivalIcao)),
                    Element("ParkingBrakeAtStart", Bool(first != null && first.ParkingBrake)),
                    Element("FuelStartKg", ToIntString(ResolveFuelKg(first, dispatch.FuelPlannedKg))),
                    Element("QnhStart", ToIntString(first == null ? 0d : first.QNH))
                ),
                new XElement("Takeoff",
                    Element("Detected", Bool(takeoff != null)),
                    Element("TimeUtc", takeoff == null ? string.Empty : FormatClock(takeoff.CapturedAtUtc)),
                    Element("Ias", ToIntString(takeoff == null ? 0d : takeoff.IndicatedAirspeed)),
                    Element("FlapsPercent", ToIntString(takeoff == null ? 0d : takeoff.FlapsPercent)),
                    Element("Beacon", profile.SupportsLightsRead && takeoff != null ? Bool(takeoff.BeaconLightsOn) : "N/D"),
                    Element("Strobe", profile.SupportsLightsRead && takeoff != null ? Bool(takeoff.StrobeLightsOn) : "N/D"),
                    Element("LandingLights", profile.SupportsLightsRead && takeoff != null ? Bool(takeoff.LandingLightsOn) : "N/D")
                ),
                new XElement("Landing",
                    Element("Detected", Bool(touchdown != null)),
                    Element("TimeUtc", touchdown == null ? string.Empty : FormatClock(touchdown.CapturedAtUtc)),
                    Element("VS", ToIntString(touchdown == null ? 0d : touchdown.LandingVS)),
                    Element("G", FormatDecimal(ResolveGForce(touchdown, 0d), 3)),
                    Element("IAS", ToIntString(touchdown == null ? 0d : touchdown.IndicatedAirspeed)),
                    Element("Pitch", ToIntString(touchdown == null ? 0d : touchdown.Pitch)),
                    Element("Bank", ToIntString(touchdown == null ? 0d : touchdown.Bank))
                ),
                new XElement("GateCloseout",
                    Element("ManualCloseoutRequired", "True"),
                    Element("AutoCloseoutDisabled", "True"),
                    Element("FinalParkingBrake", Bool(last != null && last.ParkingBrake)),
                    Element("FinalGroundSpeed", ToIntString(last == null ? 0d : last.GroundSpeed)),
                    Element("FinalFuelKg", ToIntString(ResolveFuelKg(last, 0d)))
                )
            );
        }

        private static XElement BuildScoringInput(PreparedDispatch dispatch, FlightReport report, Flight? activeFlight, IReadOnlyList<SimData> telemetry, SimData? touchdown)
        {
            var last = telemetry == null || telemetry.Count == 0 ? null : telemetry[telemetry.Count - 1];
            var profile = ResolveAircraftProfile(dispatch, activeFlight, last);
            return new XElement("ScoringInput",
                Element("Authority", "WEB_SUPABASE_ONLY"),
                Element("AcarsScoreOfficial", "False"),
                Element("AircraftProfile", profile.Code),
                Element("AircraftCapabilityAware", "True"),
                Element("TelemetrySamples", telemetry == null ? 0 : telemetry.Count),
                Element("TouchdownDetected", Bool(touchdown != null)),
                Element("TouchdownVS", ToIntString(touchdown == null ? report.LandingVS : touchdown.LandingVS)),
                Element("TouchdownG", FormatDecimal(touchdown == null ? report.LandingG : ResolveGForce(touchdown, report.LandingG), 3)),
                Element("DistanceNm", FormatDecimal(report.Distance, 1)),
                Element("FinalStatusRequested", FirstNonEmpty(report.ResultStatus, report.Status.ToString())),
                Element("UnsupportedMetricsMustNotPenalize", "True")
            );
        }

        private static AircraftProfile ResolveAircraftProfile(PreparedDispatch dispatch, Flight? activeFlight, SimData? sample)
        {
            var explicitCode = FirstNonEmpty(
                sample == null ? string.Empty : sample.DetectedProfileCode,
                sample == null ? string.Empty : sample.ProfileCode,
                dispatch.AircraftVariantCode,
                activeFlight == null ? string.Empty : activeFlight.AircraftTypeCode,
                activeFlight == null ? string.Empty : activeFlight.AircraftIcao);

            var profile = AircraftNormalizationService.GetProfile(explicitCode);
            if (profile != null && !string.Equals(profile.Code, "MSFS_NATIVE", StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }

            var title = FirstNonEmpty(
                sample == null ? string.Empty : sample.AircraftTitle,
                sample == null ? string.Empty : sample.AircraftProfile,
                dispatch.AircraftDisplayName,
                activeFlight == null ? string.Empty : activeFlight.AircraftName,
                activeFlight == null ? string.Empty : activeFlight.AircraftDisplayName,
                dispatch.AircraftIcao);

            return AircraftNormalizationService.ResolveProfile(title);
        }


        private static XElement BuildFlightPhaseSummary(IReadOnlyList<SimData> telemetry, FlightReport report)
        {
            var list = telemetry == null ? new List<SimData>() : telemetry.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var first = list.Count == 0 ? null : list[0];
            var last = list.Count == 0 ? null : list[list.Count - 1];
            var takeoff = list.FirstOrDefault(sample => !sample.OnGround && sample.AltitudeAGL > 30d);
            var touchdown = FindTouchdownSample(list);
            var blockOff = list.FirstOrDefault(sample => sample.OnGround && (!sample.ParkingBrake || sample.GroundSpeed > 3d));
            var gateStop = list.LastOrDefault(sample => sample.OnGround && sample.ParkingBrake && sample.GroundSpeed < 3d);

            var phases = new XElement("FlightPhaseSummary",
                Element("SchemaVersion", "PIREP_PERFECT_A2"),
                Element("MeasurementPolicy", "phase_based_raw_evidence_no_client_score"),
                Element("Samples", list.Count),
                Element("TakeoffDetected", Bool(takeoff != null)),
                Element("TouchdownDetected", Bool(touchdown != null)),
                Element("ManualGateCloseoutRequired", "True"),
                Element("AutoCloseoutAllowed", "False"));

            phases.Add(PhaseNode("Preflight", list.Where(sample => first != null && (blockOff == null || sample.CapturedAtUtc <= blockOff.CapturedAtUtc)).ToList()));
            phases.Add(PhaseNode("TaxiOut", list.Where(sample => blockOff != null && takeoff != null && sample.CapturedAtUtc >= blockOff.CapturedAtUtc && sample.CapturedAtUtc <= takeoff.CapturedAtUtc).ToList()));
            phases.Add(PhaseNode("Takeoff", list.Where(sample => takeoff != null && sample.CapturedAtUtc >= takeoff.CapturedAtUtc.AddSeconds(-30) && sample.CapturedAtUtc <= takeoff.CapturedAtUtc.AddMinutes(2)).ToList()));
            phases.Add(PhaseNode("Climb", list.Where(sample => takeoff != null && sample.CapturedAtUtc > takeoff.CapturedAtUtc && (touchdown == null || sample.CapturedAtUtc < touchdown.CapturedAtUtc) && sample.VerticalSpeed > 300d).ToList()));
            phases.Add(PhaseNode("Cruise", list.Where(sample => takeoff != null && !sample.OnGround && Math.Abs(sample.VerticalSpeed) <= 300d && sample.AltitudeAGL > 1500d).ToList()));
            phases.Add(PhaseNode("DescentApproach", list.Where(sample => takeoff != null && sample.CapturedAtUtc > takeoff.CapturedAtUtc && (touchdown == null || sample.CapturedAtUtc < touchdown.CapturedAtUtc) && (sample.VerticalSpeed < -300d || sample.AltitudeAGL <= 1500d)).ToList()));
            phases.Add(PhaseNode("Landing", touchdown == null ? new List<SimData>() : list.Where(sample => sample.CapturedAtUtc >= touchdown.CapturedAtUtc.AddSeconds(-20) && sample.CapturedAtUtc <= touchdown.CapturedAtUtc.AddSeconds(30)).ToList()));
            phases.Add(PhaseNode("TaxiInGate", list.Where(sample => touchdown != null && sample.CapturedAtUtc >= touchdown.CapturedAtUtc).ToList()));

            phases.Add(new XElement("KeyInstants",
                InstantNode("FirstSample", first),
                InstantNode("BlockOff", blockOff),
                InstantNode("Takeoff", takeoff),
                InstantNode("Touchdown", touchdown),
                InstantNode("GateStopCandidate", gateStop),
                InstantNode("LastSample", last)));

            phases.Add(new XElement("LandingMetrics",
                Element("TouchdownDetected", Bool(touchdown != null)),
                Element("TouchdownTimeUtc", touchdown == null ? string.Empty : FormatClock(touchdown.CapturedAtUtc)),
                Element("TouchdownVS", ToIntString(touchdown == null ? report.LandingVS : touchdown.LandingVS)),
                Element("TouchdownG", FormatDecimal(touchdown == null ? report.LandingG : ResolveGForce(touchdown, report.LandingG), 3)),
                Element("TouchdownIAS", ToIntString(touchdown == null ? 0d : touchdown.IndicatedAirspeed)),
                Element("TouchdownGS", ToIntString(touchdown == null ? 0d : touchdown.GroundSpeed)),
                Element("TouchdownPitch", ToIntString(touchdown == null ? 0d : touchdown.Pitch)),
                Element("TouchdownBank", ToIntString(touchdown == null ? 0d : touchdown.Bank)),
                Element("TouchdownLat", FormatDecimal(touchdown == null ? 0d : touchdown.Latitude, 5)),
                Element("TouchdownLon", FormatDecimal(touchdown == null ? 0d : touchdown.Longitude, 5))));

            return phases;
        }

        private static XElement PhaseNode(string name, IReadOnlyList<SimData> samples)
        {
            var list = samples == null ? new List<SimData>() : samples.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var first = list.Count == 0 ? null : list[0];
            var last = list.Count == 0 ? null : list[list.Count - 1];
            var duration = first != null && last != null && last.CapturedAtUtc >= first.CapturedAtUtc ? last.CapturedAtUtc - first.CapturedAtUtc : TimeSpan.Zero;
            return new XElement("Phase",
                new XAttribute("name", name),
                Element("Samples", list.Count),
                Element("StartUtc", first == null ? string.Empty : FormatClock(first.CapturedAtUtc)),
                Element("EndUtc", last == null ? string.Empty : FormatClock(last.CapturedAtUtc)),
                Element("Duration", FormatDuration(duration)),
                Element("MaxIAS", ToIntString(list.Count == 0 ? 0d : list.Max(sample => sample.IndicatedAirspeed))),
                Element("MaxGS", ToIntString(list.Count == 0 ? 0d : list.Max(sample => sample.GroundSpeed))),
                Element("MaxAltitude", ToIntString(list.Count == 0 ? 0d : list.Max(sample => sample.AltitudeFeet))),
                Element("MinAGL", ToIntString(list.Count == 0 ? 0d : list.Min(sample => sample.AltitudeAGL))),
                Element("MaxVS", ToIntString(list.Count == 0 ? 0d : list.Max(sample => sample.VerticalSpeed))),
                Element("MinVS", ToIntString(list.Count == 0 ? 0d : list.Min(sample => sample.VerticalSpeed))),
                Element("MaxBank", ToIntString(list.Count == 0 ? 0d : list.Select(sample => Math.Abs(sample.Bank)).DefaultIfEmpty(0d).Max())),
                Element("MaxG", FormatDecimal(list.Count == 0 ? 0d : list.Select(sample => ResolveGForce(sample, 0d)).DefaultIfEmpty(0d).Max(), 3)),
                Element("MinG", FormatDecimal(list.Count == 0 ? 0d : list.Select(sample => ResolveGForce(sample, 0d)).DefaultIfEmpty(0d).Min(), 3)),
                Element("FuelStartKg", ToIntString(first == null ? 0d : ResolveFuelKg(first, 0d))),
                Element("FuelEndKg", ToIntString(last == null ? 0d : ResolveFuelKg(last, 0d))),
                Element("DistanceNm", FormatDecimal(ResolveDistanceNm(new FlightReport(), list), 1)));
        }

        private static XElement InstantNode(string name, SimData? sample)
        {
            return new XElement("Instant",
                new XAttribute("name", name),
                Element("Detected", Bool(sample != null)),
                Element("TimeUtc", sample == null ? string.Empty : FormatClock(sample.CapturedAtUtc)),
                Element("Lat", FormatDecimal(sample == null ? 0d : sample.Latitude, 5)),
                Element("Lon", FormatDecimal(sample == null ? 0d : sample.Longitude, 5)),
                Element("AGL", ToIntString(sample == null ? 0d : sample.AltitudeAGL)),
                Element("Altitude", ToIntString(sample == null ? 0d : sample.AltitudeFeet)),
                Element("IAS", ToIntString(sample == null ? 0d : sample.IndicatedAirspeed)),
                Element("GS", ToIntString(sample == null ? 0d : sample.GroundSpeed)));
        }

        private static XElement BuildEventTimeline(IReadOnlyList<SimData> telemetry, PreparedDispatch dispatch, Flight? activeFlight, FlightReport report)
        {
            var list = telemetry == null ? new List<SimData>() : telemetry.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var root = new XElement("EventTimeline",
                Element("SchemaVersion", "PIREP_PERFECT_A2"),
                Element("Policy", "events_are_raw_evidence_web_supabase_scores"));

            if (list.Count == 0)
            {
                root.Add(EventNode("PREFLIGHT", DateTime.UtcNow, "NO_TELEMETRY", "No telemetry samples available", null, "acars", false, "no_samples"));
                return root;
            }

            var profile = ResolveAircraftProfile(dispatch, activeFlight, list[list.Count - 1]);
            var xpdrSupported = profile.SupportsTransponderModeSystem || profile.SupportsSquawkSystem;
            var doorsSupported = profile.SupportsDoorSystem;
            var gearSupported = profile.SupportsGearRead;
            var lightsSupported = profile.SupportsLightsRead;

            root.Add(EventNode("PREFLIGHT", list[0].CapturedAtUtc, "ACARS_START", "ACARS recording started", list[0], "acars", true, "recording"));
            root.Add(EventNode("PREFLIGHT", list[0].CapturedAtUtc, "AIRCRAFT_PROFILE", profile.Code + " — " + profile.DisplayName, list[0], "profile", true, profile.CapabilityAuditState));
            if (!xpdrSupported) root.Add(EventNode("PREFLIGHT", list[0].CapturedAtUtc, "XPDR_UNSUPPORTED", "XPDR state not reliable for this aircraft profile; do not penalize", list[0], "profile", false, "unsupported"));
            if (!doorsSupported) root.Add(EventNode("PREFLIGHT", list[0].CapturedAtUtc, "DOORS_UNSUPPORTED", "Door state not reliable for this aircraft profile; do not penalize", list[0], "profile", false, "unsupported"));
            if (!gearSupported) root.Add(EventNode("PREFLIGHT", list[0].CapturedAtUtc, "GEAR_UNSUPPORTED", "Gear state not reliable or fixed gear; do not penalize", list[0], "profile", false, "unsupported"));

            SimData? previous = null;
            for (var index = 0; index < list.Count; index++)
            {
                var current = list[index];
                var phase = ResolveOperationalPhase(current, list, report);
                if (previous == null)
                {
                    previous = current;
                    continue;
                }

                AddBooleanEvent(root, phase, previous.ParkingBrake, current.ParkingBrake, current, "PARKING_BRAKE_ON", "PARKING_BRAKE_OFF", "SimConnect", true, "confirmed");
                AddBooleanEvent(root, phase, previous.NavLightsOn, current.NavLightsOn, current, "NAV_LIGHTS_ON", "NAV_LIGHTS_OFF", lightsSupported ? "SimConnect" : "unsupported", lightsSupported, lightsSupported ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.BeaconLightsOn, current.BeaconLightsOn, current, "BEACON_ON", "BEACON_OFF", lightsSupported ? "SimConnect" : "unsupported", lightsSupported, lightsSupported ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.StrobeLightsOn, current.StrobeLightsOn, current, "STROBE_ON", "STROBE_OFF", lightsSupported ? "SimConnect" : "unsupported", lightsSupported, lightsSupported ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.TaxiLightsOn, current.TaxiLightsOn, current, "TAXI_LIGHTS_ON", "TAXI_LIGHTS_OFF", lightsSupported ? "SimConnect" : "unsupported", lightsSupported, lightsSupported ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.LandingLightsOn, current.LandingLightsOn, current, "LANDING_LIGHTS_ON", "LANDING_LIGHTS_OFF", lightsSupported ? "SimConnect" : "unsupported", lightsSupported, lightsSupported ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.DoorOpen, current.DoorOpen, current, "DOORS_OPEN", "DOORS_CLOSED", doorsSupported ? FirstNonEmpty(profile.DoorSource, "native") : "unsupported", doorsSupported, doorsSupported ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.GearDown, current.GearDown, current, "GEAR_DOWN", "GEAR_UP", gearSupported ? "SimConnect" : "unsupported", gearSupported, gearSupported ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.AutopilotActive, current.AutopilotActive, current, "AUTOPILOT_ON", "AUTOPILOT_OFF", "SimConnect", true, "confirmed");
                AddBooleanEvent(root, phase, previous.SeatBeltSign, current.SeatBeltSign, current, "SEATBELTS_ON", "SEATBELTS_OFF", profile.SupportsSeatbeltSystem ? "SimConnect" : "unsupported", profile.SupportsSeatbeltSystem, profile.SupportsSeatbeltSystem ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.SpoilersArmed, current.SpoilersArmed, current, "SPOILERS_ARMED", "SPOILERS_DISARMED", "SimConnect", true, "confirmed");
                AddBooleanEvent(root, phase, previous.ReverserActive, current.ReverserActive, current, "REVERSER_ACTIVE", "REVERSER_INACTIVE", "SimConnect", true, "confirmed");

                if (previous.OnGround && !current.OnGround)
                {
                    root.Add(EventNode("TAKEOFF", current.CapturedAtUtc, "AIRBORNE", "Aircraft became airborne", current, "OnGroundTransition", true, "confirmed"));
                }
                if (!previous.OnGround && current.OnGround)
                {
                    root.Add(EventNode("LANDING", current.CapturedAtUtc, "TOUCHDOWN", "Touchdown detected from airborne-to-ground transition", current, "OnGroundTransition", true, "confirmed"));
                }
                if (previous.OnGround && current.OnGround && previous.GroundSpeed < 30d && current.GroundSpeed >= 30d)
                {
                    root.Add(EventNode("TAKEOFF", current.CapturedAtUtc, "TAKEOFF_ROLL", "Ground speed crossed 30 kt while on ground", current, "computed", true, "inferred"));
                }
                if (!previous.OnGround && current.AltitudeAGL < 1500d && previous.AltitudeAGL >= 1500d)
                {
                    root.Add(EventNode("APPROACH", current.CapturedAtUtc, "APPROACH_GATE_1500_AGL", "Aircraft descended below 1500 ft AGL", current, "computed", true, "inferred"));
                }
                if (Math.Abs(previous.Com1FrequencyMhz - current.Com1FrequencyMhz) >= 0.01d && current.Com1FrequencyMhz > 0d)
                {
                    root.Add(EventNode(phase, current.CapturedAtUtc, "COM1_CHANGED", "COM1 " + FormatFrequency(current.Com1FrequencyMhz), current, "SimConnect", true, "confirmed"));
                }
                if (Math.Abs(previous.Com2FrequencyMhz - current.Com2FrequencyMhz) >= 0.01d && current.Com2FrequencyMhz > 0d)
                {
                    root.Add(EventNode(phase, current.CapturedAtUtc, "COM2_CHANGED", "COM2 " + FormatFrequency(current.Com2FrequencyMhz), current, "SimConnect", true, "confirmed"));
                }
                if (xpdrSupported && (previous.TransponderCode != current.TransponderCode || previous.TransponderStateRaw != current.TransponderStateRaw))
                {
                    root.Add(EventNode(phase, current.CapturedAtUtc, "XPDR_CHANGED", "XPDR code " + current.TransponderCode.ToString(CultureInfo.InvariantCulture) + " state " + current.TransponderStateRaw.ToString(CultureInfo.InvariantCulture), current, FirstNonEmpty(profile.TransponderStateSource, "native"), true, "confirmed"));
                }

                previous = current;
            }

            root.Add(EventNode(ResolveOperationalPhase(list[list.Count - 1], list, report), list[list.Count - 1].CapturedAtUtc, "ACARS_STOP", "ACARS recording frozen for manual closeout", list[list.Count - 1], "acars", true, "manual_closeout"));
            return root;
        }

        private static void AddBooleanEvent(XElement root, string phase, bool previous, bool current, SimData sample, string onCode, string offCode, string source, bool penaltyEligible, string reliability)
        {
            if (previous != current)
            {
                root.Add(EventNode(phase, sample.CapturedAtUtc, current ? onCode : offCode, current ? onCode.Replace('_', ' ') : offCode.Replace('_', ' '), sample, source, penaltyEligible, reliability));
            }
        }

        private static XElement EventNode(string phase, DateTime timeUtc, string code, string description, SimData? sample, string source, bool penaltyEligible, string reliability)
        {
            return new XElement("Event",
                new XAttribute("phase", string.IsNullOrWhiteSpace(phase) ? "UNKNOWN" : phase),
                new XAttribute("timeUtc", FormatClock(timeUtc)),
                new XAttribute("code", string.IsNullOrWhiteSpace(code) ? "UNKNOWN" : code),
                new XAttribute("source", string.IsNullOrWhiteSpace(source) ? "unknown" : source),
                new XAttribute("penaltyEligible", Bool(penaltyEligible)),
                new XAttribute("reliability", string.IsNullOrWhiteSpace(reliability) ? "unknown" : reliability),
                Element("Description", description ?? string.Empty),
                Element("Lat", FormatDecimal(sample == null ? 0d : sample.Latitude, 5)),
                Element("Lon", FormatDecimal(sample == null ? 0d : sample.Longitude, 5)),
                Element("Altitude", ToIntString(sample == null ? 0d : sample.AltitudeFeet)),
                Element("AGL", ToIntString(sample == null ? 0d : sample.AltitudeAGL)),
                Element("IAS", ToIntString(sample == null ? 0d : sample.IndicatedAirspeed)),
                Element("GS", ToIntString(sample == null ? 0d : sample.GroundSpeed)),
                Element("VS", ToIntString(sample == null ? 0d : sample.VerticalSpeed)),
                Element("FuelKg", ToIntString(sample == null ? 0d : ResolveFuelKg(sample, 0d))));
        }

        private static string ResolveOperationalPhase(SimData sample, IReadOnlyList<SimData> telemetry, FlightReport report)
        {
            if (sample == null) return "UNKNOWN";
            var list = telemetry == null ? new List<SimData>() : telemetry.Where(s => s != null).OrderBy(s => s.CapturedAtUtc).ToList();
            var takeoff = list.FirstOrDefault(s => !s.OnGround && s.AltitudeAGL > 30d);
            var touchdown = FindTouchdownSample(list);

            if (takeoff == null || sample.CapturedAtUtc < takeoff.CapturedAtUtc)
            {
                if (sample.OnGround && sample.GroundSpeed > 3d) return "TAXI_OUT";
                return "PREFLIGHT";
            }
            if (touchdown != null && sample.CapturedAtUtc >= touchdown.CapturedAtUtc)
            {
                if (sample.OnGround && sample.GroundSpeed < 3d && sample.ParkingBrake) return "GATE";
                return "TAXI_IN";
            }
            if (!sample.OnGround && sample.AltitudeAGL < 1500d && sample.VerticalSpeed < -200d) return "APPROACH";
            if (!sample.OnGround && sample.AltitudeAGL < 1500d && sample.VerticalSpeed >= -200d) return "TAKEOFF";
            if (sample.VerticalSpeed > 300d) return "CLIMB";
            if (sample.VerticalSpeed < -300d) return "DESCENT";
            return "CRUISE";
        }

        private static XElement BuildEconomiaPendiente()
        {
            return new XElement("Economia",
                Element("Estado", "PENDING_SERVER_EVALUATION"),
                Element("Coins", "0"),
                Element("CoinsBase", "0"),
                Element("CoinsPorHorasVuelo", "0"),
                Element("CoinsPorConsumo", "0"),
                Element("CoinsPorProcedimientos", "0"),
                Element("CoinsPorPerformance", "0"),
                Element("CoinsPorBonificacion", "0")
            );
        }

        private static string ResolveEventText(SimData? previous, SimData current, int index, FlightReport report)
        {
            if (current == null) return string.Empty;
            if (index == 0) return "START";
            if (previous == null) return string.Empty;

            var changes = new List<string>();
            AddBoolChange(changes, previous.NavLightsOn, current.NavLightsOn, "LIGHTS NAV");
            AddBoolChange(changes, previous.BeaconLightsOn, current.BeaconLightsOn, "LIGHTS BCN");
            AddBoolChange(changes, previous.StrobeLightsOn, current.StrobeLightsOn, "LIGHTS STB");
            AddBoolChange(changes, previous.TaxiLightsOn, current.TaxiLightsOn, "LIGHTS TAXI");
            AddBoolChange(changes, previous.LandingLightsOn, current.LandingLightsOn, "LIGHTS LAND");
            AddBoolChange(changes, previous.ParkingBrake, current.ParkingBrake, "PARKING BRAKE");
            AddBoolChange(changes, previous.DoorOpen, current.DoorOpen, "DOORS");
            AddBoolChange(changes, previous.GearDown, current.GearDown, "GEAR");
            AddBoolChange(changes, previous.AutopilotActive, current.AutopilotActive, "AP");
            AddBoolChange(changes, previous.SeatBeltSign, current.SeatBeltSign, "SEATBELTS");
            AddBoolChange(changes, previous.ApuRunning, current.ApuRunning, "APU");
            AddBoolChange(changes, previous.BleedAirOn, current.BleedAirOn, "APU BLEED");

            if (!previous.OnGround && current.OnGround && Math.Abs(report.LandingVS) > 0d)
            {
                changes.Add("TOUCHDOWN");
            }
            else if (previous.OnGround && !current.OnGround)
            {
                changes.Add("AIRBORNE");
            }

            if (Math.Abs(previous.Com1FrequencyMhz - current.Com1FrequencyMhz) >= 0.01d && current.Com1FrequencyMhz > 0d)
            {
                changes.Add("COM1 to " + FormatFrequency(current.Com1FrequencyMhz));
            }

            if (Math.Abs(previous.Com2FrequencyMhz - current.Com2FrequencyMhz) >= 0.01d && current.Com2FrequencyMhz > 0d)
            {
                changes.Add("COM2 to " + FormatFrequency(current.Com2FrequencyMhz));
            }

            if (IsTransponderSupported(current) && previous.TransponderCode != current.TransponderCode && current.TransponderCode > 0)
            {
                changes.Add("SQUAWK " + current.TransponderCode.ToString(CultureInfo.InvariantCulture));
            }

            if (previous.Engine1N1 < 20d && current.Engine1N1 >= 20d) changes.Add("ENGINE 1 STARTED");
            if (previous.Engine2N1 < 20d && current.Engine2N1 >= 20d) changes.Add("ENGINE 2 STARTED");
            if (previous.Engine1N1 >= 20d && current.Engine1N1 < 20d) changes.Add("ENGINE 1 STOPPED");
            if (previous.Engine2N1 >= 20d && current.Engine2N1 < 20d) changes.Add("ENGINE 2 STOPPED");

            return changes.Count == 0 ? string.Empty : string.Join(" | ", changes.Take(2).ToArray());
        }

        private static void AddBoolChange(List<string> changes, bool previous, bool current, string label)
        {
            if (previous != current)
            {
                if (label == "GEAR") changes.Add(current ? "GEAR DOWN" : "GEAR UP");
                else if (label == "DOORS") changes.Add(current ? "DOORS OPEN" : "DOORS CLOSED");
                else changes.Add(label + (current ? " ON" : " OFF"));
            }
        }

        private static string FormatLogLine(DateTime timestampUtc, string eventText, SimData? sample, double distanceNm, DateTime blockStartUtc)
        {
            var blockDuration = timestampUtc > blockStartUtc ? timestampUtc - blockStartUtc : TimeSpan.Zero;
            var time = timestampUtc.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var evt = (eventText ?? string.Empty).PadRight(21).Substring(0, Math.Min(21, (eventText ?? string.Empty).PadRight(21).Length));
            if (sample == null)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}  {1} {2,12:F5} {3,14:F5} {4,5} {5,6} {6,6} {7,7} {8,6} {9,6}  {10} {11,6} {12,7} {13,6} {14,6} {15,6}",
                    time, evt, 0d, 0d, 0, 0, 0, 0, 0, 0, FormatDuration(blockDuration), 0, 0, 1013, 0, 0);
            }

            return string.Format(CultureInfo.InvariantCulture, "{0}  {1} {2,12:F5} {3,14:F5} {4,5:000} {5,6:0} {6,6:0} {7,7:0} {8,6:0} {9,6:0}  {10} {11,6:0} {12,7:0} {13,6:0} {14,6:0} {15,6:0}",
                time,
                evt,
                sample.Latitude,
                sample.Longitude,
                sample.Heading,
                sample.AltitudeFeet,
                sample.IndicatedAirspeed,
                sample.VerticalSpeed,
                ResolveFuelKg(sample, 0d),
                distanceNm,
                FormatDuration(blockDuration),
                sample.AltitudeAGL,
                sample.AltitudeFeet,
                sample.QNH,
                sample.GroundSpeed,
                sample.Bank);
        }

        private static SimData? FindTouchdownSample(IReadOnlyList<SimData> telemetry)
        {
            if (telemetry == null || telemetry.Count == 0) return null;
            for (var index = 1; index < telemetry.Count; index++)
            {
                if (!telemetry[index - 1].OnGround && telemetry[index].OnGround)
                {
                    return telemetry[index];
                }
            }
            return null;
        }

        private static int ComputeAirborneMinutes(IReadOnlyList<SimData> telemetry, FlightReport report)
        {
            if (report.TakeoffTimeUtc != default(DateTime) && report.TouchdownTimeUtc != default(DateTime) && report.TouchdownTimeUtc > report.TakeoffTimeUtc)
            {
                return (int)Math.Round((report.TouchdownTimeUtc - report.TakeoffTimeUtc).TotalMinutes);
            }

            var airborne = telemetry.Where(sample => !sample.OnGround).ToList();
            if (airborne.Count < 2) return 0;
            return (int)Math.Round((airborne[airborne.Count - 1].CapturedAtUtc - airborne[0].CapturedAtUtc).TotalMinutes);
        }

        private static TimeSpan GetDuration(FlightReport report, IReadOnlyList<SimData> telemetry)
        {
            if (report.ArrivalTime > report.DepartureTime && report.DepartureTime != default(DateTime))
            {
                return report.ArrivalTime - report.DepartureTime;
            }

            if (telemetry.Count >= 2)
            {
                return telemetry[telemetry.Count - 1].CapturedAtUtc - telemetry[0].CapturedAtUtc;
            }

            return TimeSpan.FromMinutes(1);
        }

        private static double ResolveDistanceNm(FlightReport report, IReadOnlyList<SimData> telemetry)
        {
            if (report.Distance > 0d) return report.Distance;
            if (telemetry == null || telemetry.Count < 2) return 0d;
            var total = 0d;
            for (var index = 1; index < telemetry.Count; index++) total += DistanceNm(telemetry[index - 1], telemetry[index]);
            return total;
        }

        private static double DistanceNm(SimData a, SimData b)
        {
            if (a == null || b == null) return 0d;
            if (!IsValidPosition(a.Latitude, a.Longitude) || !IsValidPosition(b.Latitude, b.Longitude)) return 0d;
            var lat1 = ToRad(a.Latitude);
            var lat2 = ToRad(b.Latitude);
            var dLat = ToRad(b.Latitude - a.Latitude);
            var dLon = ToRad(b.Longitude - a.Longitude);
            var h = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2d) * Math.Sin(dLon / 2d);
            var c = 2d * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1d - h));
            var nm = 3440.065d * c;
            return nm >= 0d && nm <= 20d ? nm : 0d;
        }

        private static bool IsValidPosition(double latitude, double longitude)
        {
            return !double.IsNaN(latitude) && !double.IsNaN(longitude) &&
                   !double.IsInfinity(latitude) && !double.IsInfinity(longitude) &&
                   Math.Abs(latitude) <= 90d && Math.Abs(longitude) <= 180d &&
                   !(Math.Abs(latitude) < 0.000001d && Math.Abs(longitude) < 0.000001d);
        }

        private static double ToRad(double degrees) { return degrees * Math.PI / 180d; }

        private static double ResolveFuelKg(SimData? sample, double fallbackKg)
        {
            if (sample == null) return Math.Max(0d, fallbackKg);
            if (sample.FuelKg > 0d) return sample.FuelKg;
            if (sample.FuelTotalLbs > 0d) return sample.FuelTotalLbs * LbsToKg;
            return Math.Max(0d, fallbackKg);
        }

        private static double ResolveFuelUsedKg(FlightReport report, double startKg, double endKg)
        {
            if (report.FuelUsed > 0d)
            {
                // FlightReport.FuelUsed venia historicamente en lbs en varios conectores.
                return report.FuelUsed > 30000d ? report.FuelUsed * LbsToKg : report.FuelUsed;
            }
            return Math.Max(0d, startKg - endKg);
        }

        private static double ResolveGForce(SimData? sample, double fallback)
        {
            var value = sample == null ? 0d : sample.GForce;
            if (!IsOperationalGForce(value)) value = 0d;
            if (Math.Abs(value) <= 0.01d && sample != null && IsOperationalGForce(sample.LandingG)) value = sample.LandingG;
            if (Math.Abs(value) <= 0.01d && IsOperationalGForce(fallback)) value = fallback;
            return IsOperationalGForce(value) ? value : 0d;
        }

        private static bool IsOperationalGForce(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= -3.0d && value <= 8.0d;
        }

        private static bool IsTransponderSupported(SimData sample)
        {
            var profile = ResolveAircraftProfile(new PreparedDispatch(), null, sample);
            return profile.SupportsTransponderModeSystem || profile.SupportsSquawkSystem;
        }

        private static double EstimateEngineHealth(SimData? sample)
        {
            if (sample == null) return 0d;
            var values = new[] { sample.Engine1N1, sample.Engine2N1, sample.Engine3N1, sample.Engine4N1 }.Where(v => v > 0d).ToArray();
            if (values.Length == 0) return 0d;
            return Math.Min(100d, values.Average());
        }

        private static XElement Element(string name, object value)
        {
            return new XElement(name, value == null ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }

        private static string Bool(bool value) { return value ? "True" : "False"; }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
            return string.Empty;
        }

        private static string ExtractPilotNumber(string callsign)
        {
            var digits = new string((callsign ?? string.Empty).Where(char.IsDigit).ToArray());
            return string.IsNullOrWhiteSpace(digits) ? (callsign ?? string.Empty) : digits;
        }

        private static string ResolveFirstName(string fullName, string fallback)
        {
            var name = (fullName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name)) return fallback ?? string.Empty;
            return name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;
        }

        private static string ResolveLastName(string fullName)
        {
            var parts = (fullName ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length <= 1 ? string.Empty : string.Join(" ", parts.Skip(1).ToArray());
        }

        private static string NormalizeFlightMode(string value)
        {
            var mode = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (mode.Contains("CHARTER")) return "CHARTER";
            if (mode.Contains("TRAIN")) return "TRAINING";
            if (mode.Contains("EVENT")) return "EVENTO";
            if (mode.Contains("FREE")) return "FREE_FLIGHT";
            return string.IsNullOrWhiteSpace(mode) ? "ITINERARIO" : mode;
        }

        private static string FlightModeId(string value)
        {
            var mode = NormalizeFlightMode(value);
            if (mode == "CHARTER") return "2";
            if (mode == "TRAINING") return "3";
            if (mode == "EVENTO") return "4";
            if (mode == "FREE_FLIGHT") return "6";
            return "1";
        }

        private static string FormatLocalDateTime(DateTime utc)
        {
            return utc.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        }

        private static string FormatTimeOrBlank(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            DateTime parsed;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed))
            {
                return parsed.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            }
            return value;
        }

        private static string FormatTimeOrBlank(DateTime? value)
        {
            if (!value.HasValue || value.Value == default(DateTime))
            {
                return string.Empty;
            }

            return value.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string FormatClock(DateTime value)
        {
            if (value == default(DateTime)) return "00:00:00";
            return value.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", (int)duration.TotalHours, duration.Minutes);
        }

        private static string ToIntString(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "0";
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        private static string ToIntString(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatDecimal(double value, int decimals)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) value = 0d;
            return value.ToString("F" + Math.Max(0, decimals).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        private static string FormatFrequency(double value)
        {
            if (value <= 0d) return "0.00";
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        private static string BuildFileName(PreparedDispatch dispatch, FlightReport report, DateTime generatedAtUtc)
        {
            var origin = SanitizeFilePart(FirstNonEmpty(dispatch.DepartureIcao, report.DepartureIcao, "ORIG"));
            var destination = SanitizeFilePart(FirstNonEmpty(dispatch.ArrivalIcao, report.ArrivalIcao, "DEST"));
            var id = SanitizeFilePart(FirstNonEmpty(dispatch.DispatchId, dispatch.DispatchToken, dispatch.ReservationId, generatedAtUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)));
            return "Flight-" + origin + "-" + destination + "-" + id + ".XML";
        }

        private static string SanitizeFilePart(string value)
        {
            var raw = string.IsNullOrWhiteSpace(value) ? "NA" : value.Trim().ToUpperInvariant();
            var safe = new string(raw.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '-').ToArray());
            return string.IsNullOrWhiteSpace(safe) ? "NA" : safe;
        }

        private static string Sha256(string text)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
                return string.Concat(bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)).ToArray());
            }
        }
    }
}
