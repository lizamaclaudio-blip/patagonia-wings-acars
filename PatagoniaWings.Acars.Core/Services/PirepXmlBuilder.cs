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
            var fuelStartSample = firstAirborne ?? firstSample;
            var gateStopSample = telemetry.LastOrDefault(sample => sample.OnGround && sample.ParkingBrake && sample.GroundSpeed <= 3d);
            var fuelStartKg = ResolveFuelKg(fuelStartSample, dispatch.FuelPlannedKg);
            var fuelEndKg = ResolveFuelKg(gateStopSample ?? lastSample, 0d);
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
                    BuildAltitudeEvidence(telemetry, firstSample, lastSample),
                    BuildFlightPhaseSummary(telemetry, report),
                    BuildPhaseOperationalChecklist(telemetry),
                    BuildPhaseAuditReport(telemetry),
                    BuildPhasePrevalidationPackage(telemetry),
                    BuildPhaseAcceptanceMatrix(telemetry),
                    BuildRunwayTdzAuditReport(telemetry),
                    BuildFacilityBridgeAuditReport(telemetry),
                    BuildPhaseTestRunManifest(telemetry, dispatch, report),
                    BuildEventTimeline(telemetry, dispatch, activeFlight, report),
                    BuildVuelo(telemetry, report, generatedAtUtc),
                    BuildResumen(dispatch, report, telemetry, firstSample, lastSample, firstAirborne, touchdown, blockMinutes, flightMinutes, distanceNm, fuelStartKg, fuelEndKg, fuelUsedKg, fuelPerHour, fuelPer100Nm),
                    BuildIndicadores(telemetry, report, blockMinutes),
                    BuildAeropuertos(dispatch, report, telemetry, firstSample, touchdown, lastSample),
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
            var lastLoggedAtUtc = DateTime.MinValue;

            for (var index = 0; index < telemetry.Count; index++)
            {
                var sample = telemetry[index];
                if (index > 0)
                {
                    cumulativeDistance += DistanceNm(telemetry[index - 1], sample);
                }

                var eventText = ResolveEventText(previous, sample, index, report);
                var phaseChanged = previous != null && !string.Equals(previous.OperationalPhaseCode, sample.OperationalPhaseCode, StringComparison.OrdinalIgnoreCase);
                var periodic = lastLoggedAtUtc == DateTime.MinValue || (sample.CapturedAtUtc - lastLoggedAtUtc).TotalSeconds >= 30d;
                var major = IsMajorVueloEvent(eventText);
                var boundary = index == 0 || index == telemetry.Count - 1;

                // C15: keep the legacy <Vuelo>/<Log> format, but stop writing every
                // telemetry sample. The raw black-box samples remain available in ACARS
                // memory/export; the PIREP XML should carry operational evidence.
                if (boundary || phaseChanged || major || periodic)
                {
                    rows.Add(Element("Log", FormatLogLine(sample.CapturedAtUtc, eventText, sample, cumulativeDistance, first.CapturedAtUtc)));
                    lastLoggedAtUtc = sample.CapturedAtUtc;
                }

                previous = sample;
            }

            rows.Add(Element("Log", FormatLogLine(telemetry[telemetry.Count - 1].CapturedAtUtc, "STOP", telemetry[telemetry.Count - 1], cumulativeDistance, first.CapturedAtUtc)));
            return new XElement("Vuelo", rows);
        }

        private static bool IsMajorVueloEvent(string eventText)
        {
            var text = (eventText ?? string.Empty).ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.Contains("START")
                || text.Contains("STOP")
                || text.Contains("AIRBORNE")
                || text.Contains("TOUCHDOWN")
                || text.Contains("TAKEOFF")
                || text.Contains("LANDING")
                || text.Contains("PHASE")
                || text.Contains("RUNWAY")
                || text.Contains("GATE")
                || text.Contains("PIC")
                || text.Contains("XPDR");
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
            var touchdownG = ResolveTouchdownGForce(telemetry, touchdown, report.LandingG);
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

        private static XElement BuildAeropuertos(PreparedDispatch dispatch, FlightReport report, IReadOnlyList<SimData> telemetry, SimData? firstSample, SimData? touchdown, SimData? lastSample)
        {
            var touchdownG = ResolveTouchdownGForce(telemetry, touchdown, report.LandingG);
            var depRunway = ResolveRunwayIdent(firstSample);
            var arrRunway = ResolveRunwayIdent(touchdown ?? lastSample);
            var depGate = ResolveGateLabel(firstSample);
            var arrGate = ResolveGateLabel(lastSample);
            var depRunwayLen = ResolveRunwayLengthMeters(firstSample);
            var arrRunwayLen = ResolveRunwayLengthMeters(touchdown ?? lastSample);
            return new XElement("Aeropuertos",
                new XElement("Despegue",
                    Element("ICAO", FirstNonEmpty(dispatch.DepartureIcao, report.DepartureIcao)),
                    Element("Pista", depRunway),
                    Element("Gate", depGate),
                    Element("Altura", ToIntString(firstSample == null ? 0d : firstSample.AltitudeFeet)),
                    Element("LargoPistaMts", ToIntString(depRunwayLen))
                ),
                new XElement("Aterrizaje",
                    Element("ICAO", FirstNonEmpty(dispatch.ArrivalIcao, report.ArrivalIcao)),
                    Element("Pista", arrRunway),
                    Element("Gate", arrGate),
                    Element("Altura", ToIntString((touchdown ?? lastSample) == null ? 0d : (touchdown ?? lastSample).AltitudeFeet)),
                    Element("LargoPistaMts", ToIntString(arrRunwayLen)),
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
                Element("TouchdownG", FormatDecimal(ResolveTouchdownGForce(telemetry, touchdown, report.LandingG), 3)),
                Element("DistanceNm", FormatDecimal(ResolveDistanceNm(report, telemetry), 1)),
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
                Element("SchemaVersion", "PIREP_PERFECT_A2_C1"),
                Element("PhaseResolver", "C1_STATE_MACHINE"),
                Element("MeasurementPolicy", "phase_based_raw_evidence_no_client_score"),
                Element("Samples", list.Count),
                Element("TakeoffDetected", Bool(takeoff != null)),
                Element("TouchdownDetected", Bool(touchdown != null)),
                Element("ManualGateCloseoutRequired", "True"),
                Element("AutoCloseoutAllowed", "False"));

            var hasOperationalPhases = list.Any(sample => !string.IsNullOrWhiteSpace(sample.OperationalPhaseCode));
            if (hasOperationalPhases)
            {
                phases.Add(PhaseNode("Preflight", list.Where(sample => IsOperationalPhase(sample, "PRE", "BRD")).ToList()));
                phases.Add(PhaseNode("TaxiOut", list.Where(sample => IsOperationalPhase(sample, "TAX_OUT")).ToList()));
                phases.Add(PhaseNode("Takeoff", list.Where(sample => IsOperationalPhase(sample, "TO")).ToList()));
                phases.Add(PhaseNode("Climb", list.Where(sample => IsOperationalPhase(sample, "CLB")).ToList()));
                phases.Add(PhaseNode("Cruise", list.Where(sample => IsOperationalPhase(sample, "CRZ")).ToList()));
                phases.Add(PhaseNode("Descent", list.Where(sample => IsOperationalPhase(sample, "DES")).ToList()));
                phases.Add(PhaseNode("Approach", list.Where(sample => IsOperationalPhase(sample, "APP")).ToList()));
                phases.Add(PhaseNode("Landing", list.Where(sample => IsOperationalPhase(sample, "LDG")).ToList()));
                phases.Add(PhaseNode("TaxiInGate", list.Where(sample => IsOperationalPhase(sample, "TAX_IN", "GATE", "DEB")).ToList()));
            }
            else
            {
                phases.Add(PhaseNode("Preflight", list.Where(sample => first != null && (blockOff == null || sample.CapturedAtUtc <= blockOff.CapturedAtUtc)).ToList()));
                phases.Add(PhaseNode("TaxiOut", list.Where(sample => blockOff != null && takeoff != null && sample.CapturedAtUtc >= blockOff.CapturedAtUtc && sample.CapturedAtUtc <= takeoff.CapturedAtUtc).ToList()));
                phases.Add(PhaseNode("Takeoff", list.Where(sample => takeoff != null && sample.CapturedAtUtc >= takeoff.CapturedAtUtc.AddSeconds(-30) && sample.CapturedAtUtc <= takeoff.CapturedAtUtc.AddMinutes(2)).ToList()));
                phases.Add(PhaseNode("Climb", list.Where(sample => takeoff != null && sample.CapturedAtUtc > takeoff.CapturedAtUtc && (touchdown == null || sample.CapturedAtUtc < touchdown.CapturedAtUtc) && sample.VerticalSpeed > 300d).ToList()));
                phases.Add(PhaseNode("Cruise", list.Where(sample => takeoff != null && !sample.OnGround && Math.Abs(sample.VerticalSpeed) <= 300d && ResolveAgl(sample) > 1500d).ToList()));
                phases.Add(PhaseNode("DescentApproach", list.Where(sample => takeoff != null && sample.CapturedAtUtc > takeoff.CapturedAtUtc && (touchdown == null || sample.CapturedAtUtc < touchdown.CapturedAtUtc) && (sample.VerticalSpeed < -300d || ResolveAgl(sample) <= 1500d)).ToList()));
                phases.Add(PhaseNode("Landing", touchdown == null ? new List<SimData>() : list.Where(sample => sample.CapturedAtUtc >= touchdown.CapturedAtUtc.AddSeconds(-20) && sample.CapturedAtUtc <= touchdown.CapturedAtUtc.AddSeconds(30)).ToList()));
                phases.Add(PhaseNode("TaxiInGate", list.Where(sample => touchdown != null && sample.CapturedAtUtc >= touchdown.CapturedAtUtc).ToList()));
            }

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

        private static bool IsOperationalPhase(SimData sample, params string[] codes)
        {
            if (sample == null || codes == null || codes.Length == 0) return false;
            var code = (sample.OperationalPhaseCode ?? string.Empty).Trim().ToUpperInvariant();
            return codes.Any(expected => string.Equals(code, expected, StringComparison.OrdinalIgnoreCase));
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
                Element("MaxAltitude", ToIntString(list.Count == 0 ? 0d : list.Max(sample => ResolveMsl(sample)))),
                Element("MaxAltitudeMslFt", ToIntString(list.Count == 0 ? 0d : list.Max(sample => ResolveMsl(sample)))),
                Element("MaxAglFt", ToIntString(list.Count == 0 ? 0d : list.Max(sample => ResolveAgl(sample)))),
                Element("MinAGL", ToIntString(list.Count == 0 ? 0d : list.Min(sample => ResolveAgl(sample)))),
                Element("MaxVS", ToIntString(list.Count == 0 ? 0d : list.Max(sample => sample.VerticalSpeed))),
                Element("MinVS", ToIntString(list.Count == 0 ? 0d : list.Min(sample => sample.VerticalSpeed))),
                Element("MaxBank", ToIntString(list.Count == 0 ? 0d : list.Select(sample => Math.Abs(sample.Bank)).DefaultIfEmpty(0d).Max())),
                Element("MaxG", FormatDecimal(list.Count == 0 ? 0d : list.Select(sample => ResolveGForce(sample, 0d)).DefaultIfEmpty(0d).Max(), 3)),
                Element("MinG", FormatDecimal(list.Count == 0 ? 0d : list.Select(sample => ResolveGForce(sample, 0d)).DefaultIfEmpty(0d).Min(), 3)),
                Element("FuelStartKg", ToIntString(first == null ? 0d : ResolveFuelKg(first, 0d))),
                Element("FuelEndKg", ToIntString(last == null ? 0d : ResolveFuelKg(last, 0d))),
                Element("DistanceNm", FormatDecimal(ResolveDistanceNm(new FlightReport(), list), 1)),
                Element("PhaseExpectedActions", first == null ? string.Empty : first.PhaseExpectedActions),
                Element("PhaseMeasuredMetrics", first == null ? string.Empty : first.PhaseMeasuredMetrics),
                Element("PhaseScoringHints", first == null ? string.Empty : first.PhaseScoringHints),
                Element("PhaseReviewQuestion", first == null ? string.Empty : first.PhaseReviewQuestion),
                Element("PhaseReviewVersion", first == null ? string.Empty : first.PhaseReviewVersion));
        }

        private static XElement InstantNode(string name, SimData? sample)
        {
            return new XElement("Instant",
                new XAttribute("name", name),
                Element("Detected", Bool(sample != null)),
                Element("TimeUtc", sample == null ? string.Empty : FormatClock(sample.CapturedAtUtc)),
                Element("OperationalPhaseCode", sample == null ? string.Empty : sample.OperationalPhaseCode),
                Element("OperationalPhaseName", sample == null ? string.Empty : sample.OperationalPhaseName),
                Element("OperationalPhaseReason", sample == null ? string.Empty : sample.OperationalPhaseReason),
                Element("PhaseChecklistStatus", sample == null ? string.Empty : sample.PhaseChecklistStatus),
                Element("PhaseChecklistMissing", sample == null ? string.Empty : sample.PhaseChecklistMissing),
                Element("PhaseTransitionFromCode", sample == null ? string.Empty : sample.PhaseTransitionFromCode),
                Element("PhaseTransitionToCode", sample == null ? string.Empty : sample.PhaseTransitionToCode),
                Element("PhaseTransitionChanged", Bool(sample != null && sample.PhaseTransitionChanged)),
                Element("PhaseTransitionReason", sample == null ? string.Empty : sample.PhaseTransitionReason),
                Element("PhaseTransitionIndex", sample == null ? "0" : sample.PhaseTransitionIndex.ToString(CultureInfo.InvariantCulture)),
                Element("PhaseStabilitySamples", sample == null ? "0" : sample.PhaseStabilitySamples.ToString(CultureInfo.InvariantCulture)),
                Element("PhaseCandidateSamples", sample == null ? "0" : sample.PhaseCandidateSamples.ToString(CultureInfo.InvariantCulture)),
                Element("PhaseDwellSeconds", sample == null ? "0" : sample.PhaseDwellSeconds.ToString(CultureInfo.InvariantCulture)),
                Element("PhaseDecisionConfidence", sample == null ? string.Empty : sample.PhaseDecisionConfidence),
                Element("PhaseMatrixVersion", sample == null ? string.Empty : sample.PhaseMatrixVersion),
                Element("PhaseAuditStatus", sample == null ? string.Empty : sample.PhaseAuditStatus),
                Element("PhaseAuditSummary", sample == null ? string.Empty : sample.PhaseAuditSummary),
                Element("PhaseAuditFlags", sample == null ? string.Empty : sample.PhaseAuditFlags),
                Element("PhaseAuditVersion", sample == null ? string.Empty : sample.PhaseAuditVersion),
                Element("PhaseExpectedActions", sample == null ? string.Empty : sample.PhaseExpectedActions),
                Element("PhaseMeasuredMetrics", sample == null ? string.Empty : sample.PhaseMeasuredMetrics),
                Element("PhaseScoringHints", sample == null ? string.Empty : sample.PhaseScoringHints),
                Element("PhaseReviewQuestion", sample == null ? string.Empty : sample.PhaseReviewQuestion),
                Element("PhaseReviewVersion", sample == null ? string.Empty : sample.PhaseReviewVersion),
                Element("PhasePrevalidationStatus", sample == null ? string.Empty : sample.PhasePrevalidationStatus),
                Element("PhasePrevalidationSummary", sample == null ? string.Empty : sample.PhasePrevalidationSummary),
                Element("PhasePrevalidationFlags", sample == null ? string.Empty : sample.PhasePrevalidationFlags),
                Element("PhasePrevalidationVersion", sample == null ? string.Empty : sample.PhasePrevalidationVersion),
                Element("SurfaceContextCode", sample == null ? string.Empty : sample.SurfaceContextCode),
                Element("SurfaceContextName", sample == null ? string.Empty : sample.SurfaceContextName),
                Element("SurfaceContextReason", sample == null ? string.Empty : sample.SurfaceContextReason),
                Element("RunwayCandidate", Bool(sample != null && sample.RunwayCandidate)),
                Element("TaxiwayCandidate", Bool(sample != null && sample.TaxiwayCandidate)),
                Element("GateAreaCandidate", Bool(sample != null && sample.GateAreaCandidate)),
                Element("SurfaceContextReliable", Bool(sample != null && sample.SurfaceContextReliable)),
                Element("SurfaceContextVersion", sample == null ? string.Empty : sample.SurfaceContextVersion),
                Element("RunwayContextCode", sample == null ? string.Empty : sample.RunwayContextCode),
                Element("RunwayContextName", sample == null ? string.Empty : sample.RunwayContextName),
                Element("RunwayContextReason", sample == null ? string.Empty : sample.RunwayContextReason),
                Element("EstimatedRunwayIdent", sample == null ? string.Empty : sample.EstimatedRunwayIdent),
                Element("EstimatedRunwayReciprocalIdent", sample == null ? string.Empty : sample.EstimatedRunwayReciprocalIdent),
                Element("EstimatedRunwayHeadingDeg", FormatDecimal(sample == null ? 0d : sample.EstimatedRunwayHeadingDeg, 1)),
                Element("RunwayHeadingDeltaDeg", FormatDecimal(sample == null ? 0d : sample.RunwayHeadingDeltaDeg, 1)),
                Element("RunwayAlignedCandidate", Bool(sample != null && sample.RunwayAlignedCandidate)),
                Element("RunwayEntryCandidate", Bool(sample != null && sample.RunwayEntryCandidate)),
                Element("RunwayExitCandidate", Bool(sample != null && sample.RunwayExitCandidate)),
                Element("TakeoffRollCandidate", Bool(sample != null && sample.TakeoffRollCandidate)),
                Element("LandingRollCandidate", Bool(sample != null && sample.LandingRollCandidate)),
                Element("TouchdownZoneCandidate", Bool(sample != null && sample.TouchdownZoneCandidate)),
                Element("TaxiwayProbable", Bool(sample != null && sample.TaxiwayProbable)),
                Element("RunwayGeometryAvailable", Bool(sample != null && sample.RunwayGeometryAvailable)),
                Element("RunwayContextReliable", Bool(sample != null && sample.RunwayContextReliable)),
                Element("RunwayContextVersion", sample == null ? string.Empty : sample.RunwayContextVersion),
                Element("FacilityRunwayGeometryAvailable", Bool(sample != null && sample.FacilityRunwayGeometryAvailable)),
                Element("FacilityRunwayGeometryStatus", sample == null ? string.Empty : sample.FacilityRunwayGeometryStatus),
                Element("FacilityNearestRunwayAirportIcao", sample == null ? string.Empty : sample.FacilityNearestRunwayAirportIcao),
                Element("FacilityNearestRunwayIdent", sample == null ? string.Empty : sample.FacilityNearestRunwayIdent),
                Element("FacilityNearestRunwayReciprocalIdent", sample == null ? string.Empty : sample.FacilityNearestRunwayReciprocalIdent),
                Element("FacilityNearestRunwayHeadingDeg", FormatDecimal(sample == null ? 0d : sample.FacilityNearestRunwayHeadingDeg, 1)),
                Element("FacilityNearestRunwayLengthMeters", FormatDecimal(sample == null ? 0d : sample.FacilityNearestRunwayLengthMeters, 0)),
                Element("FacilityNearestRunwayWidthMeters", FormatDecimal(sample == null ? 0d : sample.FacilityNearestRunwayWidthMeters, 0)),
                Element("FacilityNearestRunwayDistanceMeters", FormatDecimal(sample == null ? 0d : sample.FacilityNearestRunwayDistanceMeters, 0)),
                Element("FacilityRunwayLateralOffsetMeters", FormatDecimal(sample == null ? 0d : sample.FacilityRunwayLateralOffsetMeters, 0)),
                Element("FacilityRunwayLongitudinalOffsetMeters", FormatDecimal(sample == null ? 0d : sample.FacilityRunwayLongitudinalOffsetMeters, 0)),
                Element("FacilityRunwayHeadingErrorDeg", FormatDecimal(sample == null ? 0d : sample.FacilityRunwayHeadingErrorDeg, 1)),
                Element("FacilityRunwayDistanceFromThresholdMeters", FormatDecimal(sample == null ? 0d : sample.FacilityRunwayDistanceFromThresholdMeters, 0)),
                Element("FacilityOnRunwayCandidate", Bool(sample != null && sample.FacilityOnRunwayCandidate)),
                Element("FacilityRunwayAlignedCandidate", Bool(sample != null && sample.FacilityRunwayAlignedCandidate)),
                Element("FacilityTouchdownZoneCandidate", Bool(sample != null && sample.FacilityTouchdownZoneCandidate)),
                Element("FacilityRunwayGeometrySummary", sample == null ? string.Empty : sample.FacilityRunwayGeometrySummary),
                Element("FacilityRunwayGeometryCount", sample == null ? "0" : sample.FacilityRunwayGeometryCount.ToString(CultureInfo.InvariantCulture)),
                Element("FacilityRunwayGeometryVersion", sample == null ? string.Empty : sample.FacilityRunwayGeometryVersion),
                Element("Lat", FormatDecimal(sample == null ? 0d : sample.Latitude, 5)),
                Element("Lon", FormatDecimal(sample == null ? 0d : sample.Longitude, 5)),
                Element("AGL", ToIntString(sample == null ? 0d : ResolveAgl(sample))),
                Element("Altitude", ToIntString(sample == null ? 0d : ResolveMsl(sample))),
                Element("AltitudeMslFt", ToIntString(sample == null ? 0d : ResolveMsl(sample))),
                Element("AltitudeAglFt", ToIntString(sample == null ? 0d : ResolveAgl(sample))),
                Element("IAS", ToIntString(sample == null ? 0d : sample.IndicatedAirspeed)),
                Element("GS", ToIntString(sample == null ? 0d : sample.GroundSpeed)));
        }

        private static XElement BuildPhaseOperationalChecklist(IReadOnlyList<SimData> telemetry)
        {
            var list = telemetry == null ? new List<SimData>() : telemetry.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var root = new XElement("PhaseOperationalChecklist",
                Element("SchemaVersion", "PIREP_PERFECT_C3"),
                Element("Policy", "raw_phase_checklist_no_client_score"),
                Element("Samples", list.Count));

            root.Add(ExpectedPhaseNode("PRE", "Preflight", "OnGround, GS<=3, parking brake ON; registra avión, matrícula, origen, combustible inicial y QNH."));
            root.Add(ExpectedPhaseNode("TAX_OUT", "Taxi out", "OnGround, GS 3-35 kt, parking brake OFF; registra taxi speed, luces y configuración previa al despegue."));
            root.Add(ExpectedPhaseNode("TO", "Takeoff", "Takeoff roll y airborne; registra velocidad, flaps, luces, combustible al despegue y transición OnGround false."));
            root.Add(ExpectedPhaseNode("CLB", "Climb", "Airborne con VS positiva o baja altura AGL; registra ascenso, velocidad y combustible."));
            root.Add(ExpectedPhaseNode("CRZ", "Cruise", "Airborne estable; registra altitud MSL/FL, velocidad, combustible, pausas y PIC checks."));
            root.Add(ExpectedPhaseNode("DES", "Descent", "Airborne con descenso sostenido; registra VS, velocidad y perfil vertical."));
            root.Add(ExpectedPhaseNode("APP", "Approach", "AGL < 3000 ft y descenso/establecido; registra luces, flaps, gear si aplica y aproximación estabilizada."));
            root.Add(ExpectedPhaseNode("LDG", "Landing", "Transición aire a tierra; registra touchdown VS/G/IAS/bank/pitch."));
            root.Add(ExpectedPhaseNode("TAX_IN", "Taxi in", "OnGround después de touchdown, GS 3-40 kt; registra salida de pista y taxi al gate."));
            root.Add(ExpectedPhaseNode("GATE", "Gate ready", "OnGround, GS<=3, parking brake ON y cierre manual; registra combustible final y estado de motores/cold-dark."));

            var groups = list
                .GroupBy(sample => string.IsNullOrWhiteSpace(sample.OperationalPhaseCode) ? "UNKNOWN" : sample.OperationalPhaseCode.Trim().ToUpperInvariant())
                .OrderBy(group => PhaseOrder(group.Key))
                .ThenBy(group => group.Key);

            foreach (var group in groups)
            {
                var samples = group.OrderBy(sample => sample.CapturedAtUtc).ToList();
                var first = samples.Count == 0 ? null : samples[0];
                var last = samples.Count == 0 ? null : samples[samples.Count - 1];
                var missing = samples.SelectMany(sample => SplitChecklist(sample.PhaseChecklistMissing)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                var warnings = samples.SelectMany(sample => SplitChecklist(sample.PhaseChecklistWarnings)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                var satisfied = samples.SelectMany(sample => SplitChecklist(sample.PhaseChecklistSatisfied)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                var status = missing.Count == 0 ? (warnings.Count == 0 ? "OK" : "WARN") : "INCOMPLETE";

                root.Add(new XElement("ObservedPhase",
                    new XAttribute("code", group.Key),
                    new XAttribute("name", first == null ? group.Key : FirstNonEmpty(first.OperationalPhaseName, group.Key)),
                    new XAttribute("status", status),
                    Element("Samples", samples.Count),
                    Element("StartUtc", first == null ? string.Empty : FormatClock(first.CapturedAtUtc)),
                    Element("EndUtc", last == null ? string.Empty : FormatClock(last.CapturedAtUtc)),
                    Element("FirstReason", first == null ? string.Empty : first.OperationalPhaseReason),
                    Element("LastReason", last == null ? string.Empty : last.OperationalPhaseReason),
                    Element("TransitionCount", samples.Count(sample => sample.PhaseTransitionChanged).ToString(CultureInfo.InvariantCulture)),
                    Element("FirstTransitionIndex", first == null ? "0" : first.PhaseTransitionIndex.ToString(CultureInfo.InvariantCulture)),
                    Element("LastTransitionIndex", last == null ? "0" : last.PhaseTransitionIndex.ToString(CultureInfo.InvariantCulture)),
                    Element("LastDecisionConfidence", last == null ? string.Empty : last.PhaseDecisionConfidence),
                    Element("LastDwellSeconds", last == null ? "0" : last.PhaseDwellSeconds.ToString(CultureInfo.InvariantCulture)),
                    Element("Required", first == null ? string.Empty : first.PhaseChecklistRequired),
                    Element("Satisfied", string.Join(",", satisfied)),
                    Element("Missing", string.Join(",", missing)),
                    Element("Warnings", string.Join(",", warnings)),
                    Element("AuditStatus", ResolveAuditStatus(samples)),
                    Element("AuditFlags", ResolveAuditFlags(samples)),
                    Element("MaxMslFt", ToIntString(samples.Count == 0 ? 0d : samples.Max(sample => ResolveMsl(sample)))),
                    Element("MaxAglFt", ToIntString(samples.Count == 0 ? 0d : samples.Max(sample => ResolveAgl(sample)))),
                    Element("MaxGS", ToIntString(samples.Count == 0 ? 0d : samples.Max(sample => sample.GroundSpeed))),
                    Element("MinVS", ToIntString(samples.Count == 0 ? 0d : samples.Min(sample => sample.VerticalSpeed))),
                    Element("MaxVS", ToIntString(samples.Count == 0 ? 0d : samples.Max(sample => sample.VerticalSpeed)))));
            }

            return root;
        }

        private static XElement BuildPhaseAuditReport(IReadOnlyList<SimData> telemetry)
        {
            var list = telemetry == null ? new List<SimData>() : telemetry.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var root = new XElement("PhaseAuditReport",
                Element("SchemaVersion", "PIREP_PERFECT_C4"),
                Element("Policy", "audit_only_no_client_score"),
                Element("Samples", list.Count.ToString(CultureInfo.InvariantCulture)));

            if (list.Count == 0)
            {
                root.Add(new XElement("Overall",
                    new XAttribute("status", "ERROR"),
                    Element("Summary", "No hay muestras de telemetria para auditar fases.")));
                return root;
            }

            var sequence = list
                .Select(sample => string.IsNullOrWhiteSpace(sample.OperationalPhaseCode) ? "UNKNOWN" : sample.OperationalPhaseCode.Trim().ToUpperInvariant())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Aggregate(new List<string>(), (acc, code) => { if (acc.Count == 0 || acc[acc.Count - 1] != code) acc.Add(code); return acc; });

            var transitions = list.Where(sample => sample.PhaseTransitionChanged).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var flags = list.SelectMany(sample => SplitChecklist(sample.PhaseAuditFlags)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
            var errors = list.Count(sample => string.Equals(sample.PhaseAuditStatus, "ERROR", StringComparison.OrdinalIgnoreCase));
            var warnings = list.Count(sample => string.Equals(sample.PhaseAuditStatus, "WARN", StringComparison.OrdinalIgnoreCase));
            var ok = list.Count(sample => string.Equals(sample.PhaseAuditStatus, "OK", StringComparison.OrdinalIgnoreCase));
            var touchdownCount = list.Count(sample => sample.TouchdownDetected);
            var gateCount = list.Count(sample => string.Equals(sample.OperationalPhaseCode, "GATE", StringComparison.OrdinalIgnoreCase));
            var onGroundAirborneContradictions = list.Count(sample => sample.OnGround && IsAirborneOperationalCode(sample.OperationalPhaseCode));
            var airborneGroundContradictions = list.Count(sample => !sample.OnGround && IsGroundOperationalCode(sample.OperationalPhaseCode));

            var overallStatus = errors > 0 || onGroundAirborneContradictions > 0 || airborneGroundContradictions > 0
                ? "ERROR"
                : warnings > 0 || flags.Count > 0 ? "WARN" : "OK";

            root.Add(new XElement("Overall",
                new XAttribute("status", overallStatus),
                Element("Summary", overallStatus == "OK" ? "Secuencia de fases coherente para auditoria." : "Secuencia de fases requiere revision."),
                Element("ObservedSequence", string.Join(" > ", sequence)),
                Element("TransitionCount", transitions.Count.ToString(CultureInfo.InvariantCulture)),
                Element("OkSamples", ok.ToString(CultureInfo.InvariantCulture)),
                Element("WarnSamples", warnings.ToString(CultureInfo.InvariantCulture)),
                Element("ErrorSamples", errors.ToString(CultureInfo.InvariantCulture)),
                Element("TouchdownSamples", touchdownCount.ToString(CultureInfo.InvariantCulture)),
                Element("GateSamples", gateCount.ToString(CultureInfo.InvariantCulture)),
                Element("OnGroundAirbornePhaseContradictions", onGroundAirborneContradictions.ToString(CultureInfo.InvariantCulture)),
                Element("AirborneGroundPhaseContradictions", airborneGroundContradictions.ToString(CultureInfo.InvariantCulture)),
                Element("Flags", string.Join(",", flags))));

            var transitionsNode = new XElement("Transitions");
            foreach (var sample in transitions)
            {
                transitionsNode.Add(new XElement("Transition",
                    new XAttribute("index", sample.PhaseTransitionIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("timeUtc", FormatClock(sample.CapturedAtUtc)),
                    new XAttribute("from", string.IsNullOrWhiteSpace(sample.PhaseTransitionFromCode) ? "UNKNOWN" : sample.PhaseTransitionFromCode),
                    new XAttribute("to", string.IsNullOrWhiteSpace(sample.OperationalPhaseCode) ? sample.PhaseTransitionToCode : sample.OperationalPhaseCode),
                    new XAttribute("confidence", string.IsNullOrWhiteSpace(sample.PhaseDecisionConfidence) ? "confirmed" : sample.PhaseDecisionConfidence),
                    Element("Reason", sample.PhaseTransitionReason),
                    Element("AuditStatus", sample.PhaseAuditStatus),
                    Element("AuditFlags", sample.PhaseAuditFlags),
                    Element("AltitudeMslFt", ToIntString(ResolveMsl(sample))),
                    Element("AltitudeAglFt", ToIntString(ResolveAgl(sample))),
                    Element("GroundSpeedKt", ToIntString(sample.GroundSpeed)),
                    Element("VerticalSpeedFpm", ToIntString(sample.VerticalSpeed)),
                    Element("OnGround", Bool(sample.OnGround))));
            }
            root.Add(transitionsNode);

            var phaseContracts = new XElement("PhaseReviewContracts");
            foreach (var group in list
                .Where(sample => !string.IsNullOrWhiteSpace(sample.OperationalPhaseCode))
                .GroupBy(sample => sample.OperationalPhaseCode.Trim().ToUpperInvariant())
                .OrderBy(group => group.Key))
            {
                var sample = group.FirstOrDefault();
                phaseContracts.Add(new XElement("PhaseContract",
                    new XAttribute("code", group.Key),
                    Element("Samples", group.Count().ToString(CultureInfo.InvariantCulture)),
                    Element("ExpectedActions", sample == null ? string.Empty : sample.PhaseExpectedActions),
                    Element("MeasuredMetrics", sample == null ? string.Empty : sample.PhaseMeasuredMetrics),
                    Element("ScoringHints", sample == null ? string.Empty : sample.PhaseScoringHints),
                    Element("ReviewQuestion", sample == null ? string.Empty : sample.PhaseReviewQuestion),
                    Element("Version", sample == null ? string.Empty : sample.PhaseReviewVersion)));
            }
            root.Add(phaseContracts);

            var questions = new XElement("ValidationQuestions",
                Element("Question", "¿PRE aparece antes de TAX_OUT/TO?"),
                Element("Question", "¿TO aparece antes de CLB/CRZ/DES/APP?"),
                Element("Question", "¿LDG aparece por transicion aire→tierra y no solo por AGL=0?"),
                Element("Question", "¿TAX_IN/GATE aparecen despues de LDG?"),
                Element("Question", "¿No hay CLB/CRZ/DES/APP con OnGround=true?"),
                Element("Question", "¿AGL=0 en tierra y MSL conserva elevacion real?"));
            root.Add(questions);

            return root;
        }

        private static XElement BuildPhasePrevalidationPackage(IReadOnlyList<SimData> telemetry)
        {
            var list = telemetry == null ? new List<SimData>() : telemetry.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var root = new XElement("PhasePrevalidationPackage",
                Element("SchemaVersion", "PIREP_PERFECT_C6"),
                Element("Policy", "preflight_phase_review_only_no_client_score"),
                Element("Samples", list.Count.ToString(CultureInfo.InvariantCulture)),
                Element("OfficialScoringAuthority", "WEB_SUPABASE"));

            if (list.Count == 0)
            {
                root.Add(new XElement("Overall",
                    new XAttribute("status", "BLOCK"),
                    Element("Summary", "Sin telemetria para prevalidar fases."),
                    Element("Flags", "NoTelemetry")));
                return root;
            }

            var sequence = BuildObservedPhaseSequence(list);
            var required = new[] { "PRE", "TAX_OUT", "TO", "CLB", "LDG", "TAX_IN", "GATE" };
            var observed = new HashSet<string>(list.Select(sample => (sample.OperationalPhaseCode ?? string.Empty).Trim().ToUpperInvariant()).Where(code => !string.IsNullOrWhiteSpace(code)));
            var missingRequired = required.Where(code => !observed.Contains(code)).ToList();
            var statusCounts = list
                .GroupBy(sample => string.IsNullOrWhiteSpace(sample.PhasePrevalidationStatus) ? "PENDING" : sample.PhasePrevalidationStatus.Trim().ToUpperInvariant())
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            var flags = list
                .SelectMany(sample => SplitChecklist(sample.PhasePrevalidationFlags))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(flag => flag)
                .ToList();

            var hasBlock = statusCounts.ContainsKey("BLOCK") || flags.Any(flag => flag.EndsWith("ButAirbornePhase", StringComparison.OrdinalIgnoreCase) || flag.EndsWith("ButGroundPhase", StringComparison.OrdinalIgnoreCase));
            var hasWait = statusCounts.ContainsKey("WAIT");
            var hasWarn = statusCounts.ContainsKey("WARN") || flags.Count > 0;
            var overall = hasBlock ? "BLOCK" : hasWait ? "WAIT" : hasWarn ? "WARN" : "READY";

            root.Add(new XElement("Overall",
                new XAttribute("status", overall),
                Element("Summary", overall == "READY" ? "C6 listo para prueba de vuelo completa." : "C6 requiere revision durante la prueba de vuelo."),
                Element("ObservedSequence", string.Join(" > ", sequence)),
                Element("MissingRecommendedPhases", string.Join(",", missingRequired)),
                Element("ReadySamples", CountStatus(statusCounts, "READY")),
                Element("WarnSamples", CountStatus(statusCounts, "WARN")),
                Element("WaitSamples", CountStatus(statusCounts, "WAIT")),
                Element("BlockSamples", CountStatus(statusCounts, "BLOCK")),
                Element("Flags", string.Join(",", flags)),
                Element("ReadyForWebSupabaseScoringReview", Bool(overall != "BLOCK"))));

            var byPhase = new XElement("PhaseReadinessByPhase");
            foreach (var group in list
                .GroupBy(sample => string.IsNullOrWhiteSpace(sample.OperationalPhaseCode) ? "UNKNOWN" : sample.OperationalPhaseCode.Trim().ToUpperInvariant())
                .OrderBy(group => PhaseOrder(group.Key))
                .ThenBy(group => group.Key))
            {
                var samples = group.ToList();
                var sampleFlags = samples.SelectMany(sample => SplitChecklist(sample.PhasePrevalidationFlags)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(flag => flag).ToList();
                var phaseStatus = samples.Any(sample => string.Equals(sample.PhasePrevalidationStatus, "BLOCK", StringComparison.OrdinalIgnoreCase))
                    ? "BLOCK"
                    : samples.Any(sample => string.Equals(sample.PhasePrevalidationStatus, "WAIT", StringComparison.OrdinalIgnoreCase))
                        ? "WAIT"
                        : samples.Any(sample => string.Equals(sample.PhasePrevalidationStatus, "WARN", StringComparison.OrdinalIgnoreCase))
                            ? "WARN"
                            : "READY";
                var first = samples.OrderBy(sample => sample.CapturedAtUtc).FirstOrDefault();
                var last = samples.OrderBy(sample => sample.CapturedAtUtc).LastOrDefault();
                byPhase.Add(new XElement("PhaseReadiness",
                    new XAttribute("code", group.Key),
                    new XAttribute("status", phaseStatus),
                    Element("Samples", samples.Count.ToString(CultureInfo.InvariantCulture)),
                    Element("StartUtc", first == null ? string.Empty : FormatClock(first.CapturedAtUtc)),
                    Element("EndUtc", last == null ? string.Empty : FormatClock(last.CapturedAtUtc)),
                    Element("Summary", last == null ? string.Empty : last.PhasePrevalidationSummary),
                    Element("Flags", string.Join(",", sampleFlags)),
                    Element("LastChecklist", last == null ? string.Empty : last.PhaseChecklistStatus),
                    Element("LastAudit", last == null ? string.Empty : last.PhaseAuditStatus),
                    Element("LastReviewQuestion", last == null ? string.Empty : last.PhaseReviewQuestion)));
            }
            root.Add(byPhase);

            root.Add(new XElement("FlightTestInstructions",
                Element("Step", "Validar PRE en parking: AGL=0, MSL=elevacion real, freno ON."),
                Element("Step", "Validar TAX_OUT: GS 3-35 kt, freno OFF, OnGround=true."),
                Element("Step", "Validar TO/CLB: transicion OnGround true->false, AGL/MSL suben."),
                Element("Step", "Validar DES/APP: descenso sostenido y AGL < 3000 ft en aproximacion."),
                Element("Step", "Validar LDG: touchdown por aire->tierra, no por AGL=0 en gate."),
                Element("Step", "Validar TAX_IN/GATE: OnGround=true, GS bajo, parking brake ON, cierre manual.")));

            return root;
        }


        private static XElement BuildPhaseAcceptanceMatrix(IReadOnlyList<SimData> telemetry)
        {
            var list = telemetry == null ? new List<SimData>() : telemetry.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var root = new XElement("PhaseAcceptanceMatrix",
                Element("SchemaVersion", "PIREP_PERFECT_C7"),
                Element("Policy", "final_manual_review_pack_no_client_score"),
                Element("OfficialScoringAuthority", "WEB_SUPABASE"),
                Element("Samples", list.Count.ToString(CultureInfo.InvariantCulture)));

            var sequence = BuildObservedPhaseSequence(list);
            var observed = new HashSet<string>(sequence, StringComparer.OrdinalIgnoreCase);
            var expected = new[]
            {
                new { Code = "PRE", Name = "Preflight / Gate", Required = "OnGround=true, AGL=0, GS<=3, parking brake ON, despacho presente", Critical = "AirportMatch, AircraftMatch, ColdDark, ParkingBrake, FuelStart" },
                new { Code = "TAX_OUT", Name = "Taxi out", Required = "OnGround=true, GS 3-35 kt, parking brake OFF, antes de airborne", Critical = "TaxiSpeed, LightsCapability, XPDRCapability, BrakeReleased" },
                new { Code = "TO", Name = "Takeoff roll / Airborne inicial", Required = "OnGround true->false o GS>35 kt en pista y luego AGL>20 ft", Critical = "TakeoffRoll, AirborneTransition, Flaps, Lights" },
                new { Code = "CLB", Name = "Climb", Required = "Airborne=true, AGL>500 ft, VS positiva/promedio, MSL subiendo", Critical = "MSLTrend, AGLTrend, VS, MaxIAS" },
                new { Code = "CRZ", Name = "Cruise", Required = "Airborne=true, VS estabilizada, altitud mantenida o FL sobre transicion", Critical = "MSL, PressureAltitude, FlightLevel, FuelBurn" },
                new { Code = "DES", Name = "Descent", Required = "Airborne=true, VS negativa sostenida, MSL descendiendo", Critical = "DescentStart, VS, SpeedBelow10000" },
                new { Code = "APP", Name = "Approach", Required = "Airborne=true, AGL<3000 ft, distancia destino decreciendo, configuracion landing", Critical = "AGL, LandingLights, Flaps, GearCapability" },
                new { Code = "LDG", Name = "Landing", Required = "Transicion OnGround false->true, touchdown capturado", Critical = "TouchdownVS, TouchdownG, TouchdownIAS, TouchdownAgl" },
                new { Code = "TAX_IN", Name = "Taxi in", Required = "Post-touchdown, OnGround=true, GS 3-35 kt, sin finalizar", Critical = "TaxiSpeed, RunwayVacated, LightsAfterLanding" },
                new { Code = "GATE", Name = "Gate ready", Required = "Post-touchdown, OnGround=true, GS<=3, parking brake ON, motores/cold dark si aplica", Critical = "ParkingBrake, EnginesOff, AGL0, ManualFinalizeReady" }
            };

            root.Add(new XElement("Overall",
                Element("ObservedSequence", string.Join(" > ", sequence)),
                Element("HasPreflight", Bool(observed.Contains("PRE"))),
                Element("HasTakeoff", Bool(observed.Contains("TO") || observed.Contains("CLB"))),
                Element("HasLanding", Bool(observed.Contains("LDG"))),
                Element("HasGate", Bool(observed.Contains("GATE"))),
                Element("ReadyForFinalFlightTest", Bool(list.Count > 0)),
                Element("ManualReviewRequired", "True")));

            var expectedNode = new XElement("ExpectedPhaseAcceptance");
            foreach (var item in expected)
            {
                var samples = list.Where(sample => string.Equals((sample.OperationalPhaseCode ?? string.Empty).Trim(), item.Code, StringComparison.OrdinalIgnoreCase)).OrderBy(sample => sample.CapturedAtUtc).ToList();
                var first = samples.FirstOrDefault();
                var last = samples.LastOrDefault();
                var missing = samples.SelectMany(sample => SplitChecklist(sample.PhaseChecklistMissing)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                var warnings = samples.SelectMany(sample => SplitChecklist(sample.PhaseChecklistWarnings)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                var auditFlags = samples.SelectMany(sample => SplitChecklist(sample.PhaseAuditFlags)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                var prevalidationFlags = samples.SelectMany(sample => SplitChecklist(sample.PhasePrevalidationFlags)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
                var status = samples.Count == 0
                    ? "NOT_OBSERVED"
                    : samples.Any(sample => string.Equals(sample.PhasePrevalidationStatus, "BLOCK", StringComparison.OrdinalIgnoreCase) || string.Equals(sample.PhaseAuditStatus, "ERROR", StringComparison.OrdinalIgnoreCase))
                        ? "BLOCK"
                        : missing.Count > 0 || warnings.Count > 0 || auditFlags.Count > 0 || prevalidationFlags.Count > 0
                            ? "REVIEW"
                            : "OK";

                expectedNode.Add(new XElement("PhaseAcceptance",
                    new XAttribute("code", item.Code),
                    new XAttribute("name", item.Name),
                    new XAttribute("status", status),
                    Element("Samples", samples.Count.ToString(CultureInfo.InvariantCulture)),
                    Element("FirstUtc", first == null ? string.Empty : FormatClock(first.CapturedAtUtc)),
                    Element("LastUtc", last == null ? string.Empty : FormatClock(last.CapturedAtUtc)),
                    Element("RequiredEvidence", item.Required),
                    Element("CriticalSignals", item.Critical),
                    Element("FirstReason", first == null ? string.Empty : first.OperationalPhaseReason),
                    Element("LastReason", last == null ? string.Empty : last.OperationalPhaseReason),
                    Element("MaxMslFt", samples.Count == 0 ? "0" : ToIntString(samples.Max(sample => ResolveMsl(sample)))),
                    Element("MaxAglFt", samples.Count == 0 ? "0" : ToIntString(samples.Max(sample => ResolveAgl(sample)))),
                    Element("MaxGroundSpeedKt", samples.Count == 0 ? "0" : ToIntString(samples.Max(sample => sample.GroundSpeed))),
                    Element("MinVerticalSpeedFpm", samples.Count == 0 ? "0" : ToIntString(samples.Min(sample => sample.VerticalSpeed))),
                    Element("MaxVerticalSpeedFpm", samples.Count == 0 ? "0" : ToIntString(samples.Max(sample => sample.VerticalSpeed))),
                    Element("MissingChecklist", string.Join(",", missing)),
                    Element("Warnings", string.Join(",", warnings)),
                    Element("AuditFlags", string.Join(",", auditFlags)),
                    Element("PrevalidationFlags", string.Join(",", prevalidationFlags)),
                    Element("ReviewQuestion", BuildAcceptanceQuestion(item.Code))));
            }
            root.Add(expectedNode);

            root.Add(new XElement("FinalFlightTestProtocol",
                Element("Instruction", "No publicar scoring nuevo hasta revisar este bloque contra capturas y XML real."),
                Element("Instruction", "Confirmar que ALT MSL conserva elevacion real y AGL queda 0 en tierra."),
                Element("Instruction", "Confirmar que CLB/CRZ/DES/APP nunca permanecen activos con OnGround=true."),
                Element("Instruction", "Confirmar LDG por transicion aire-tierra y GATE por GS<=3 + parking brake."),
                Element("Instruction", "Confirmar que XPDR/Doors/Gear unsupported quedan N/D y sin penalizacion.")));

            return root;
        }


        private static XElement BuildRunwayTdzAuditReport(IReadOnlyList<SimData> telemetry)
        {
            var list = telemetry == null ? new List<SimData>() : telemetry.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var root = new XElement("RunwayTdzAuditReport",
                Element("SchemaVersion", "PIREP_PERFECT_C11D4"),
                Element("Policy", "runway_taxi_tdz_alignment_are_raw_evidence_no_client_score"),
                Element("GeometryAvailable", Bool(list.Any(sample => sample.RunwayGeometryAvailable || sample.FacilityRunwayGeometryAvailable))),
                Element("GeometryPolicy", "C11C may use MSFS SimConnect Facilities runway geometry; Web/Supabase remains official scoring authority."),
                Element("FacilityRunwayGeometrySamples", list.Count(sample => sample.FacilityRunwayGeometryAvailable)),
                Element("FacilityOnRunwaySamples", list.Count(sample => sample.FacilityOnRunwayCandidate)),
                Element("FacilityTdzSamples", list.Count(sample => sample.FacilityTouchdownZoneCandidate)),
                Element("Samples", list.Count),
                Element("RunwayEntrySamples", list.Count(sample => sample.RunwayEntryCandidate)),
                Element("RunwayAlignedSamples", list.Count(sample => sample.RunwayAlignedCandidate)),
                Element("TakeoffRollSamples", list.Count(sample => sample.TakeoffRollCandidate)),
                Element("TouchdownZoneCandidateSamples", list.Count(sample => sample.TouchdownZoneCandidate)),
                Element("LandingRollSamples", list.Count(sample => sample.LandingRollCandidate)),
                Element("RunwayExitSamples", list.Count(sample => sample.RunwayExitCandidate)),
                Element("TaxiwayProbableSamples", list.Count(sample => sample.TaxiwayProbable)));

            var groups = list
                .Where(sample => !string.IsNullOrWhiteSpace(sample.RunwayContextCode))
                .GroupBy(sample => sample.RunwayContextCode.Trim().ToUpperInvariant())
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key);

            foreach (var group in groups)
            {
                var first = group.First();
                var last = group.Last();
                root.Add(new XElement("Context",
                    new XAttribute("code", group.Key),
                    Element("Count", group.Count()),
                    Element("Name", first.RunwayContextName),
                    Element("FirstUtc", FormatClock(first.CapturedAtUtc)),
                    Element("LastUtc", FormatClock(last.CapturedAtUtc)),
                    Element("EstimatedRunwayIdent", first.EstimatedRunwayIdent),
                    Element("EstimatedRunwayReciprocalIdent", first.EstimatedRunwayReciprocalIdent),
                    Element("AvgHeadingDeltaDeg", FormatDecimal(group.Select(sample => sample.RunwayHeadingDeltaDeg).DefaultIfEmpty(0d).Average(), 1)),
                    Element("FacilityRunwayIdent", first.FacilityNearestRunwayIdent),
                    Element("FacilityRunwayAirportIcao", first.FacilityNearestRunwayAirportIcao),
                    Element("FacilityRunwayGeometry", first.FacilityRunwayGeometrySummary),
                    Element("AvgFacilityDistanceMeters", FormatDecimal(group.Where(sample => sample.FacilityRunwayGeometryAvailable).Select(sample => sample.FacilityNearestRunwayDistanceMeters).DefaultIfEmpty(0d).Average(), 0)),
                    Element("AvgFacilityLateralMeters", FormatDecimal(group.Where(sample => sample.FacilityRunwayGeometryAvailable).Select(sample => Math.Abs(sample.FacilityRunwayLateralOffsetMeters)).DefaultIfEmpty(0d).Average(), 0)),
                    Element("MaxGS", ToIntString(group.Select(sample => sample.GroundSpeed).DefaultIfEmpty(0d).Max())),
                    Element("Reason", first.RunwayContextReason)));
            }

            var firstRunwayEntry = list.FirstOrDefault(sample => sample.RunwayEntryCandidate);
            var firstTakeoffRoll = list.FirstOrDefault(sample => sample.TakeoffRollCandidate);
            var touchdown = list.FirstOrDefault(sample => sample.TouchdownZoneCandidate);
            var runwayExit = list.FirstOrDefault(sample => sample.RunwayExitCandidate);

            root.Add(new XElement("KeyRunwayInstants",
                InstantNode("runway_entry_candidate", firstRunwayEntry),
                InstantNode("takeoff_roll_candidate", firstTakeoffRoll),
                InstantNode("tdz_candidate", touchdown),
                InstantNode("runway_exit_candidate", runwayExit)));

            return root;
        }


        private static XElement BuildFacilityBridgeAuditReport(IReadOnlyList<SimData> telemetry)
        {
            var list = telemetry == null ? new List<SimData>() : telemetry.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var samplesWithBridge = list.Where(sample => sample.FacilityBridgeAvailable || sample.FacilityDataReceived || !string.IsNullOrWhiteSpace(sample.FacilityBridgeStatus)).ToList();
            var last = samplesWithBridge.LastOrDefault() ?? list.LastOrDefault();

            var root = new XElement("FacilityBridgeAuditReport",
                Element("SchemaVersion", "PIREP_PERFECT_C11D2"),
                Element("Policy", "simconnect_facilities_bridge_runway_taxi_parking_raw_evidence_no_client_score"),
                Element("Source", last == null ? string.Empty : last.FacilityDataSource),
                Element("BridgeAvailable", Bool(samplesWithBridge.Any(sample => sample.FacilityBridgeAvailable))),
                Element("Subscribed", Bool(samplesWithBridge.Any(sample => sample.FacilityBridgeSubscribed))),
                Element("DataReceived", Bool(samplesWithBridge.Any(sample => sample.FacilityDataReceived))),
                Element("Samples", list.Count),
                Element("BridgeSamples", samplesWithBridge.Count),
                Element("AirportCountMax", samplesWithBridge.Count == 0 ? "0" : samplesWithBridge.Max(sample => sample.FacilityBridgeAirportCount).ToString(CultureInfo.InvariantCulture)),
                Element("RecordsReceivedMax", samplesWithBridge.Count == 0 ? "0" : samplesWithBridge.Max(sample => sample.FacilityBridgeRecordsReceived).ToString(CultureInfo.InvariantCulture)),
                Element("DirectRequestsSentMax", samplesWithBridge.Count == 0 ? "0" : samplesWithBridge.Max(sample => sample.FacilityBridgeDirectRequestsSent).ToString(CultureInfo.InvariantCulture)),
                Element("DataEndCountMax", samplesWithBridge.Count == 0 ? "0" : samplesWithBridge.Max(sample => sample.FacilityBridgeDataEndCount).ToString(CultureInfo.InvariantCulture)),
                Element("ExceptionCountMax", samplesWithBridge.Count == 0 ? "0" : samplesWithBridge.Max(sample => sample.FacilityBridgeExceptionCount).ToString(CultureInfo.InvariantCulture)),
                Element("NearestAirports", last == null ? string.Empty : last.FacilityBridgeNearestAirports),
                Element("RequestedIcaos", last == null ? string.Empty : last.FacilityBridgeRequestedIcaos),
                Element("ReceivedIcaos", last == null ? string.Empty : last.FacilityBridgeReceivedIcaos),
                Element("PendingIcaos", last == null ? string.Empty : last.FacilityBridgePendingIcaos),
                Element("LastStatus", last == null ? string.Empty : last.FacilityBridgeStatus),
                Element("LastDataStatus", last == null ? string.Empty : last.FacilityBridgeLastDataStatus),
                Element("DataTypeHistogram", last == null ? string.Empty : last.FacilityBridgeDataTypeHistogram),
                Element("LastIcao", last == null ? string.Empty : last.FacilityBridgeLastIcao),
                Element("LastRequestMode", last == null ? string.Empty : last.FacilityBridgeLastRequestMode),
                Element("AwaitingResponse", Bool(last != null && last.FacilityBridgeAwaitingResponse)),
                Element("SecondsSinceRequest", last == null ? "0" : last.FacilityBridgeSecondsSinceRequest.ToString("F0", CultureInfo.InvariantCulture)),
                Element("LastException", last == null ? string.Empty : last.FacilityBridgeLastException),
                Element("LastRequestUtc", last != null && last.FacilityBridgeLastRequestUtc.HasValue ? FormatClock(last.FacilityBridgeLastRequestUtc.Value) : string.Empty),
                Element("LastReceivedUtc", last != null && last.FacilityBridgeLastReceivedUtc.HasValue ? FormatClock(last.FacilityBridgeLastReceivedUtc.Value) : string.Empty),
                Element("FacilityRunwayGeometryAvailable", Bool(list.Any(sample => sample.FacilityRunwayGeometryAvailable))),
                Element("FacilityRunwayGeometrySamples", list.Count(sample => sample.FacilityRunwayGeometryAvailable)),
                Element("FacilityRunwayGeometryCountMax", list.Count == 0 ? "0" : list.Max(sample => sample.FacilityRunwayGeometryCount).ToString(CultureInfo.InvariantCulture)),
                Element("LastFacilityRunwayGeometryStatus", last == null ? string.Empty : last.FacilityRunwayGeometryStatus),
                Element("LastFacilityRunwayGeometrySummary", last == null ? string.Empty : last.FacilityRunwayGeometrySummary),
                Element("FacilityTaxiParkingPayloadCountMax", list.Count == 0 ? "0" : list.Max(sample => sample.FacilityTaxiParkingPayloadCount).ToString(CultureInfo.InvariantCulture)),
                Element("FacilityTaxiPointPayloadCountMax", list.Count == 0 ? "0" : list.Max(sample => sample.FacilityTaxiPointPayloadCount).ToString(CultureInfo.InvariantCulture)),
                Element("FacilityTaxiPathPayloadCountMax", list.Count == 0 ? "0" : list.Max(sample => sample.FacilityTaxiPathPayloadCount).ToString(CultureInfo.InvariantCulture)),
                Element("LastFacilityTaxiGeometryStatus", last == null ? string.Empty : last.FacilityTaxiGeometryStatus),
                Element("FacilityTaxiGeometryAvailable", Bool(list.Any(sample => sample.FacilityTaxiGeometryAvailable))),
                Element("FacilityTaxiGeometrySamples", list.Count(sample => sample.FacilityTaxiGeometryAvailable)),
                Element("FacilityTaxiParkingGeometryCountMax", list.Count == 0 ? "0" : list.Max(sample => sample.FacilityTaxiParkingGeometryCount).ToString(CultureInfo.InvariantCulture)),
                Element("FacilityTaxiPointGeometryCountMax", list.Count == 0 ? "0" : list.Max(sample => sample.FacilityTaxiPointGeometryCount).ToString(CultureInfo.InvariantCulture)),
                Element("FacilityTaxiPathGeometryCountMax", list.Count == 0 ? "0" : list.Max(sample => sample.FacilityTaxiPathGeometryCount).ToString(CultureInfo.InvariantCulture)),
                Element("LastFacilityTaxiGeometrySummary", last == null ? string.Empty : last.FacilityTaxiGeometrySummary),
                Element("LastFacilityNearestTaxiAirportIcao", last == null ? string.Empty : last.FacilityNearestTaxiAirportIcao),
                Element("LastFacilityNearestTaxiParkingLabel", last == null ? string.Empty : last.FacilityNearestTaxiParkingLabel),
                Element("LastFacilityNearestTaxiParkingDistanceMeters", last == null ? "0" : last.FacilityNearestTaxiParkingDistanceMeters.ToString("F0", CultureInfo.InvariantCulture)),
                Element("LastFacilityNearestTaxiPathDistanceMeters", last == null ? "0" : last.FacilityNearestTaxiPathDistanceMeters.ToString("F0", CultureInfo.InvariantCulture)),
                Element("LastFacilityGateAreaCandidate", Bool(last != null && last.FacilityGateAreaCandidate)),
                Element("LastFacilityTaxiwayCandidate", Bool(last != null && last.FacilityTaxiwayCandidate)),
                Element("SurfaceProcedureEvidenceSamples", list.Count(sample => !string.IsNullOrWhiteSpace(sample.SurfaceProcedureEvidenceStatus))),
                Element("LastSurfaceProcedurePhaseCode", last == null ? string.Empty : last.SurfaceProcedurePhaseCode),
                Element("LastSurfaceProcedurePhaseName", last == null ? string.Empty : last.SurfaceProcedurePhaseName),
                Element("LastSurfaceProcedureEvidenceStatus", last == null ? string.Empty : last.SurfaceProcedureEvidenceStatus),
                Element("LastSurfaceProcedureEvidenceSummary", last == null ? string.Empty : last.SurfaceProcedureEvidenceSummary),
                Element("LastSurfaceProcedureEvidenceFlags", last == null ? string.Empty : last.SurfaceProcedureEvidenceFlags),
                Element("LastSurfaceProcedureTaxiLightExpected", Bool(last != null && last.SurfaceProcedureTaxiLightExpected)),
                Element("LastSurfaceProcedureStrobeExpected", Bool(last != null && last.SurfaceProcedureStrobeExpected)),
                Element("LastSurfaceProcedureLandingLightExpected", Bool(last != null && last.SurfaceProcedureLandingLightExpected)),
                Element("LastSurfaceProcedureXpdrAltExpected", Bool(last != null && last.SurfaceProcedureXpdrAltExpected)),
                Element("SurfaceProcedureEvidenceVersion", last == null ? "C11F" : last.SurfaceProcedureEvidenceVersion),
                Element("NextBlock", "C11F/C12 phase ladder and procedure evidence ready for Web/Supabase official scoring."));

            foreach (var group in samplesWithBridge
                .Where(sample => !string.IsNullOrWhiteSpace(sample.FacilityBridgeStatus))
                .GroupBy(sample => sample.FacilityBridgeStatus.Trim())
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key))
            {
                root.Add(new XElement("StatusGroup",
                    new XAttribute("status", group.Key),
                    Element("Count", group.Count()),
                    Element("FirstUtc", FormatClock(group.First().CapturedAtUtc)),
                    Element("LastUtc", FormatClock(group.Last().CapturedAtUtc))));
            }

            return root;
        }

        private static XElement BuildPhaseTestRunManifest(IReadOnlyList<SimData> telemetry, PreparedDispatch dispatch, FlightReport report)
        {
            var list = telemetry == null ? new List<SimData>() : telemetry.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var sequence = BuildObservedPhaseSequence(list);
            var observed = new HashSet<string>(sequence, StringComparer.OrdinalIgnoreCase);
            var groundSamples = list.Where(sample => sample.OnGround).ToList();
            var airborneSamples = list.Where(sample => !sample.OnGround).ToList();
            var hasAltitudeResolver = list.Any(sample => !string.IsNullOrWhiteSpace(sample.DisplayAltitudeText)
                || Math.Abs(sample.AltitudeMslFeet) > 0.01d
                || Math.Abs(sample.AltitudeAglFeet) > 0.01d
                || Math.Abs(sample.PressureAltitudeFeet) > 0.01d);
            var hasPhaseStateMachine = list.Any(sample => !string.IsNullOrWhiteSpace(sample.OperationalPhaseCode));
            var hasChecklist = list.Any(sample => !string.IsNullOrWhiteSpace(sample.PhaseChecklistSummary)
                || !string.IsNullOrWhiteSpace(sample.PhaseChecklistRequired)
                || !string.IsNullOrWhiteSpace(sample.PhaseChecklistSatisfied));
            var hasTransitionEvidence = list.Any(sample => !string.IsNullOrWhiteSpace(sample.PhaseTransitionReason)
                || sample.PhaseTransitionChanged
                || sample.PhaseTransitionIndex > 0);
            var hasAuditEvidence = list.Any(sample => !string.IsNullOrWhiteSpace(sample.PhaseAuditSummary)
                || !string.IsNullOrWhiteSpace(sample.PhaseAuditFlags));
            var hasReviewContract = list.Any(sample => !string.IsNullOrWhiteSpace(sample.PhaseExpectedActions)
                || !string.IsNullOrWhiteSpace(sample.PhaseReviewQuestion));
            var hasPrevalidation = list.Any(sample => !string.IsNullOrWhiteSpace(sample.PhasePrevalidationSummary)
                || !string.IsNullOrWhiteSpace(sample.PhasePrevalidationFlags));
            var touchdownSamples = list.Count(sample => sample.TouchdownDetected);
            var gateSamples = list.Count(sample => sample.GateReadyCandidate);
            var groundAglOk = groundSamples.Count == 0 || groundSamples.All(sample => ResolveAgl(sample) <= 25d);
            var airborneMslOk = airborneSamples.Count == 0 || airborneSamples.Any(sample => ResolveMsl(sample) > 100d || ResolveAgl(sample) > 30d);
            var readyForFullTest = hasAltitudeResolver && hasPhaseStateMachine && hasChecklist && hasTransitionEvidence && hasAuditEvidence && hasReviewContract && hasPrevalidation;

            var root = new XElement("PhaseTestRunManifest",
                Element("SchemaVersion", "PIREP_PERFECT_C8"),
                Element("Policy", "final_acars_pretest_manifest_no_client_score"),
                Element("OfficialScoringAuthority", "WEB_SUPABASE"),
                Element("FlightNumber", FirstNonEmpty(dispatch.FlightNumber, report.FlightNumber)),
                Element("Origin", FirstNonEmpty(dispatch.DepartureIcao, report.DepartureIcao)),
                Element("Destination", FirstNonEmpty(dispatch.ArrivalIcao, report.ArrivalIcao)),
                Element("Samples", list.Count.ToString(CultureInfo.InvariantCulture)),
                Element("ObservedSequence", string.Join(" > ", sequence)),
                Element("ReadyForFullSimulatorValidation", Bool(readyForFullTest)));

            root.Add(new XElement("EvidenceInventory",
                Element("AltitudeResolver", Bool(hasAltitudeResolver)),
                Element("PhaseStateMachine", Bool(hasPhaseStateMachine)),
                Element("PhaseOperationalChecklist", Bool(hasChecklist)),
                Element("PhaseTransitionMatrix", Bool(hasTransitionEvidence)),
                Element("PhaseAuditReport", Bool(hasAuditEvidence)),
                Element("PhaseReviewContract", Bool(hasReviewContract)),
                Element("PhasePrevalidationPackage", Bool(hasPrevalidation)),
                Element("PhaseAcceptanceMatrix", "True"),
                Element("TouchdownSamples", touchdownSamples.ToString(CultureInfo.InvariantCulture)),
                Element("GateReadySamples", gateSamples.ToString(CultureInfo.InvariantCulture)),
                Element("GroundAglNormalized", Bool(groundAglOk)),
                Element("AirborneAltitudeEvidence", Bool(airborneMslOk))));

            root.Add(new XElement("PretestBlockingRules",
                Element("Rule", "No publicar instalador publico hasta hacer vuelo completo y revisar XML C8."),
                Element("Rule", "No tocar Web/Supabase scoring hasta validar secuencia PRE>TAX_OUT>TO>CLB>CRZ/DES/APP>LDG>TAX_IN>GATE."),
                Element("Rule", "Si una capability viene unsupported o penaltyEligible=false, Web/Supabase debe saltar penalizacion."),
                Element("Rule", "ACARS sigue siendo caja negra: no calcula score oficial.")));

            var plan = new XElement("ManualCapturePlan");
            AddCaptureStep(plan, "01", "PRE", "Parking antes de iniciar", "ALT MSL real/elevacion, AGL=0, GS=0, parking brake ON", "Captura UI + XML con Phase=PRE y Altitude separado MSL/AGL");
            AddCaptureStep(plan, "02", "TAX_OUT", "Rodaje salida", "OnGround=true, AGL=0, GS 3-35, parking brake OFF", "Captura UI + EventTimeline TAX_OUT");
            AddCaptureStep(plan, "03", "TO", "Carrera/despegue", "GS>35 y luego OnGround true->false", "Captura si es posible + XML con TO/AIRBORNE");
            AddCaptureStep(plan, "04", "CLB", "Ascenso", "Airborne=true, MSL/AGL subiendo, VS positiva", "Captura UI CLB con MSL y AGL correctos");
            AddCaptureStep(plan, "05", "CRZ", "Crucero si aplica", "VS estable y FL si sobre transicion", "Captura UI FL/MSL/AGL o confirmar NOT_OBSERVED si vuelo corto");
            AddCaptureStep(plan, "06", "DES", "Descenso", "VS negativa sostenida, MSL bajando", "Captura UI DES o XML con PhaseTransitionReason");
            AddCaptureStep(plan, "07", "APP", "Aproximacion", "AGL<3000, distancia destino decreciendo", "Captura APP antes del touchdown");
            AddCaptureStep(plan, "08", "LDG", "Touchdown/landing roll", "Transicion OnGround false->true, TouchdownDetected=true", "XML con touchdown_vs/g/ias y LDG");
            AddCaptureStep(plan, "09", "TAX_IN", "Rodaje llegada", "Post-touchdown, OnGround=true, GS 3-35", "Captura TAX_IN");
            AddCaptureStep(plan, "10", "GATE", "Gate listo", "OnGround=true, AGL=0, GS<=3, parking brake ON, cierre manual", "Captura GATE antes de Finalizar en Gate");
            root.Add(plan);

            var gates = new XElement("AcceptanceGates");
            AddAcceptanceGate(gates, "Altitude", groundAglOk && airborneMslOk, "AGL debe ser 0 en tierra y MSL debe conservar altitud real; en vuelo MSL/AGL deben subir separadas.");
            AddAcceptanceGate(gates, "PhaseSequence", observed.Contains("PRE") || observed.Contains("TAX_OUT") || observed.Contains("TO") || observed.Contains("CLB") || observed.Contains("LDG") || observed.Contains("GATE"), "La prueba final debe observar fases principales o marcarlas como NOT_OBSERVED justificadas.");
            AddAcceptanceGate(gates, "Touchdown", touchdownSamples > 0 || !observed.Contains("LDG"), "Si hay aterrizaje real, touchdown debe venir por aire->tierra, no solo por AGL=0.");
            AddAcceptanceGate(gates, "GateReady", gateSamples > 0 || !observed.Contains("GATE"), "GATE debe requerir post-touchdown, OnGround, GS bajo y parking brake.");
            AddAcceptanceGate(gates, "NoClientScore", true, "ACARS no calcula puntaje oficial; Web/Supabase es autoridad.");
            root.Add(gates);

            root.Add(new XElement("CommitReadinessChecklist",
                Element("BuildReleaseRequired", "MSBuild Release x64 0 errores antes de commit."),
                Element("GitAddPolicy", "Agregar solo archivos C0-C8; no usar git add . si hay bin/obj/zip/backups/installer."),
                Element("ExpectedGitStatus", "Solo archivos fuente ACARS modificados y changelogs C0-C8."),
                Element("AfterTestNextBlock", "C9 solo si XML/capturas muestran fase incorrecta; Web/Supabase se toca despues de validar ACARS.")));

            return root;
        }

        private static void AddCaptureStep(XElement parent, string order, string phase, string name, string expectedUi, string evidence)
        {
            parent.Add(new XElement("CaptureStep",
                new XAttribute("order", order),
                new XAttribute("phase", phase),
                Element("Name", name),
                Element("ExpectedUi", expectedUi),
                Element("EvidenceToCollect", evidence)));
        }

        private static void AddAcceptanceGate(XElement parent, string name, bool pass, string criteria)
        {
            parent.Add(new XElement("Gate",
                new XAttribute("name", name),
                new XAttribute("status", pass ? "READY" : "REVIEW"),
                Element("Criteria", criteria)));
        }

        private static string BuildAcceptanceQuestion(string code)
        {
            switch ((code ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "PRE": return "¿PRE aparece en parking con AGL=0, MSL real, freno ON y despacho correcto?";
                case "TAX_OUT": return "¿TAX_OUT aparece al soltar freno y rodar antes de despegar?";
                case "TO": return "¿TO aparece durante carrera/despegue y no se confunde con taxi?";
                case "CLB": return "¿CLB aparece solo airborne con MSL/AGL subiendo y sale al estabilizar o descender?";
                case "CRZ": return "¿CRZ aparece al estabilizar altitud o FL sobre transicion?";
                case "DES": return "¿DES aparece con descenso sostenido y distancia al destino decreciendo?";
                case "APP": return "¿APP aparece bajo 3000 ft AGL o cerca de destino, antes del touchdown?";
                case "LDG": return "¿LDG aparece por transicion aire→tierra y captura VS/G/IAS?";
                case "TAX_IN": return "¿TAX_IN aparece despues de touchdown durante rodaje a gate?";
                case "GATE": return "¿GATE aparece detenido con parking brake y listo para cierre manual?";
                default: return "¿La fase observada es coherente con la telemetria?";
            }
        }

        private static List<string> BuildObservedPhaseSequence(IReadOnlyList<SimData> list)
        {
            return list
                .Select(sample => string.IsNullOrWhiteSpace(sample.OperationalPhaseCode) ? "UNKNOWN" : sample.OperationalPhaseCode.Trim().ToUpperInvariant())
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Aggregate(new List<string>(), (acc, code) => { if (acc.Count == 0 || acc[acc.Count - 1] != code) acc.Add(code); return acc; });
        }

        private static string CountStatus(Dictionary<string, int> counts, string status)
        {
            return counts.TryGetValue(status, out var value) ? value.ToString(CultureInfo.InvariantCulture) : "0";
        }

        private static string ResolveAuditStatus(IReadOnlyList<SimData> samples)
        {
            if (samples == null || samples.Count == 0) return "PENDING";
            if (samples.Any(sample => string.Equals(sample.PhaseAuditStatus, "ERROR", StringComparison.OrdinalIgnoreCase))) return "ERROR";
            if (samples.Any(sample => string.Equals(sample.PhaseAuditStatus, "WARN", StringComparison.OrdinalIgnoreCase))) return "WARN";
            if (samples.Any(sample => string.Equals(sample.PhaseAuditStatus, "OK", StringComparison.OrdinalIgnoreCase))) return "OK";
            return "PENDING";
        }

        private static string ResolveAuditFlags(IReadOnlyList<SimData> samples)
        {
            if (samples == null || samples.Count == 0) return string.Empty;
            return string.Join(",", samples.SelectMany(sample => SplitChecklist(sample.PhaseAuditFlags)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
        }

        private static bool IsAirborneOperationalCode(string code)
        {
            switch ((code ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "TO":
                case "CLB":
                case "CRZ":
                case "DES":
                case "APP":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsGroundOperationalCode(string code)
        {
            switch ((code ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "PRE":
                case "BRD":
                case "TAX_OUT":
                case "TAX_IN":
                case "GATE":
                case "DEB":
                    return true;
                default:
                    return false;
            }
        }

        private static XElement ExpectedPhaseNode(string code, string name, string measures)
        {
            return new XElement("ExpectedPhase",
                new XAttribute("code", code),
                new XAttribute("name", name),
                Element("Measures", measures),
                Element("ScoringAuthority", "WEB_SUPABASE"));
        }

        private static IEnumerable<string> SplitChecklist(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Enumerable.Empty<string>();
            return value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0);
        }

        private static int PhaseOrder(string code)
        {
            switch ((code ?? string.Empty).ToUpperInvariant())
            {
                case "PRE": return 10;
                case "BRD": return 15;
                case "TAX_OUT": return 20;
                case "TO": return 30;
                case "CLB": return 40;
                case "CRZ": return 50;
                case "DES": return 60;
                case "APP": return 70;
                case "LDG": return 80;
                case "TAX_IN": return 90;
                case "GATE": return 100;
                case "DEB": return 110;
                default: return 999;
            }
        }

        private static XElement BuildEventTimeline(IReadOnlyList<SimData> telemetry, PreparedDispatch dispatch, Flight? activeFlight, FlightReport report)
        {
            var list = telemetry == null ? new List<SimData>() : telemetry.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var root = new XElement("EventTimeline",
                Element("SchemaVersion", "C15_EVENT_SLIM"),
                Element("Policy", "operational_events_only_raw_evidence_web_supabase_scores"),
                Element("RawTelemetryPolicy", "raw_samples_not_repeated_as_events"));

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
            var runwayEntryLogged = false;
            var takeoffRollLogged = false;
            var runwayExitLogged = false;
            var tdzLogged = false;
            var approachGateLogged = false;
            var airborneLogged = false;
            var touchdownLogged = false;

            for (var index = 0; index < list.Count; index++)
            {
                var current = list[index];
                var phase = ResolveOperationalPhase(current, list, report);
                if (previous == null)
                {
                    previous = current;
                    continue;
                }

                if (!string.Equals(previous.OperationalPhaseCode, current.OperationalPhaseCode, StringComparison.OrdinalIgnoreCase))
                {
                    root.Add(EventNode(phase, current.CapturedAtUtc, "PHASE_CHANGED", "Phase " + previous.OperationalPhaseCode + " → " + current.OperationalPhaseCode, current, "phase_engine", true, "confirmed"));
                }

                // C15: record material operational changes, not continuous radio/sensor polling.
                AddBooleanEvent(root, phase, previous.ParkingBrake, current.ParkingBrake, current, "PARKING_BRAKE_ON", "PARKING_BRAKE_OFF", "SimConnect", true, "confirmed");
                AddBooleanEvent(root, phase, previous.BeaconLightsOn, current.BeaconLightsOn, current, "BEACON_ON", "BEACON_OFF", lightsSupported ? "SimConnect" : "unsupported", lightsSupported, lightsSupported ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.TaxiLightsOn, current.TaxiLightsOn, current, "TAXI_LIGHTS_ON", "TAXI_LIGHTS_OFF", lightsSupported ? "SimConnect" : "unsupported", lightsSupported, lightsSupported ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.StrobeLightsOn, current.StrobeLightsOn, current, "STROBE_ON", "STROBE_OFF", lightsSupported ? "SimConnect" : "unsupported", lightsSupported, lightsSupported ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.LandingLightsOn, current.LandingLightsOn, current, "LANDING_LIGHTS_ON", "LANDING_LIGHTS_OFF", lightsSupported ? "SimConnect" : "unsupported", lightsSupported, lightsSupported ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.DoorOpen, current.DoorOpen, current, "DOORS_OPEN", "DOORS_CLOSED", doorsSupported ? FirstNonEmpty(profile.DoorSource, "native") : "unsupported", doorsSupported, doorsSupported ? "confirmed" : "unsupported");
                AddBooleanEvent(root, phase, previous.AutopilotActive, current.AutopilotActive, current, "AUTOPILOT_MASTER_ON", "AUTOPILOT_MASTER_OFF", "SimConnect", true, "confirmed");

                if (!airborneLogged && previous.OnGround && !current.OnGround)
                {
                    airborneLogged = true;
                    root.Add(EventNode("TAKEOFF", current.CapturedAtUtc, "AIRBORNE", "Aircraft became airborne", current, "OnGroundTransition", true, "confirmed"));
                }
                if (!touchdownLogged && !previous.OnGround && current.OnGround)
                {
                    touchdownLogged = true;
                    root.Add(EventNode("LANDING", current.CapturedAtUtc, "TOUCHDOWN", "Touchdown detected from airborne-to-ground transition", current, "OnGroundTransition", true, "confirmed"));
                }
                if (!takeoffRollLogged && previous.OnGround && current.OnGround && previous.GroundSpeed < 30d && current.GroundSpeed >= 30d)
                {
                    takeoffRollLogged = true;
                    root.Add(EventNode("TAKEOFF", current.CapturedAtUtc, "TAKEOFF_ROLL", "Ground speed crossed 30 kt while on ground", current, "computed", true, "inferred"));
                }
                if (!runwayEntryLogged && !previous.RunwayEntryCandidate && current.RunwayEntryCandidate)
                {
                    runwayEntryLogged = true;
                    root.Add(EventNode(phase, current.CapturedAtUtc, "RUNWAY_ENTRY_CANDIDATE", "C10/C11 detected probable runway entry or lineup", current, "C11", false, "geometry_evidence"));
                }
                if (!takeoffRollLogged && !previous.TakeoffRollCandidate && current.TakeoffRollCandidate)
                {
                    takeoffRollLogged = true;
                    root.Add(EventNode("TAKEOFF", current.CapturedAtUtc, "RUNWAY_TAKEOFF_ROLL_CANDIDATE", "C10/C11 detected probable takeoff roll on runway axis", current, "C11", false, "geometry_evidence"));
                }
                if (!tdzLogged && !previous.TouchdownZoneCandidate && current.TouchdownZoneCandidate)
                {
                    tdzLogged = true;
                    root.Add(EventNode("LANDING", current.CapturedAtUtc, "TDZ_CANDIDATE", "C11 detected touchdown zone candidate", current, "C11", false, "geometry_evidence"));
                }
                if (!runwayExitLogged && !previous.RunwayExitCandidate && current.RunwayExitCandidate)
                {
                    runwayExitLogged = true;
                    root.Add(EventNode("TAXI_IN", current.CapturedAtUtc, "RUNWAY_EXIT_CANDIDATE", "C10/C11 detected probable runway exit toward taxiway", current, "C11", false, "geometry_evidence"));
                }
                if (!approachGateLogged && !previous.OnGround && current.AltitudeAGL < 1500d && previous.AltitudeAGL >= 1500d)
                {
                    approachGateLogged = true;
                    root.Add(EventNode("APPROACH", current.CapturedAtUtc, "APPROACH_GATE_1500_AGL", "Aircraft descended below 1500 ft AGL", current, "computed", true, "inferred"));
                }
                if (xpdrSupported && (previous.TransponderCode != current.TransponderCode || previous.TransponderStateRaw != current.TransponderStateRaw))
                {
                    root.Add(EventNode(phase, current.CapturedAtUtc, "XPDR_CHANGED", "XPDR code " + current.TransponderCode.ToString(CultureInfo.InvariantCulture) + " state " + current.TransponderStateRaw.ToString(CultureInfo.InvariantCulture), current, FirstNonEmpty(profile.TransponderStateSource, "native"), true, "confirmed"));
                }

                previous = current;
            }

            if (report.PicChecksTotal > 0)
            {
                var picStatus = report.PicChecksFailed > 0 ? "PIC_CHECK_FAILED" : (report.PicChecksSucceeded > 0 ? "PIC_CHECK_PASSED" : "PIC_CHECK_SCHEDULED");
                root.Add(EventNode("CRUISE", list[list.Count - 1].CapturedAtUtc, picStatus, "PIC COM2 checks " + report.PicChecksSucceeded.ToString(CultureInfo.InvariantCulture) + "/" + report.PicChecksTotal.ToString(CultureInfo.InvariantCulture), list[list.Count - 1], "PIC_COM2", report.PicChecksFailed > 0, report.PicChecksFailed > 0 ? "failed" : "confirmed"));
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
                Element("OperationalPhaseCode", sample == null ? string.Empty : sample.OperationalPhaseCode),
                Element("OperationalPhaseName", sample == null ? string.Empty : sample.OperationalPhaseName),
                Element("OperationalPhaseReason", sample == null ? string.Empty : sample.OperationalPhaseReason),
                Element("PhaseChecklistStatus", sample == null ? string.Empty : sample.PhaseChecklistStatus),
                Element("PhaseChecklistMissing", sample == null ? string.Empty : sample.PhaseChecklistMissing),
                Element("PhaseTransitionFromCode", sample == null ? string.Empty : sample.PhaseTransitionFromCode),
                Element("PhaseTransitionToCode", sample == null ? string.Empty : sample.PhaseTransitionToCode),
                Element("PhaseTransitionChanged", Bool(sample != null && sample.PhaseTransitionChanged)),
                Element("PhaseTransitionReason", sample == null ? string.Empty : sample.PhaseTransitionReason),
                Element("PhaseTransitionIndex", sample == null ? "0" : sample.PhaseTransitionIndex.ToString(CultureInfo.InvariantCulture)),
                Element("PhaseStabilitySamples", sample == null ? "0" : sample.PhaseStabilitySamples.ToString(CultureInfo.InvariantCulture)),
                Element("PhaseCandidateSamples", sample == null ? "0" : sample.PhaseCandidateSamples.ToString(CultureInfo.InvariantCulture)),
                Element("PhaseDwellSeconds", sample == null ? "0" : sample.PhaseDwellSeconds.ToString(CultureInfo.InvariantCulture)),
                Element("PhaseDecisionConfidence", sample == null ? string.Empty : sample.PhaseDecisionConfidence),
                Element("PhaseMatrixVersion", sample == null ? string.Empty : sample.PhaseMatrixVersion),
                Element("PhaseAuditStatus", sample == null ? string.Empty : sample.PhaseAuditStatus),
                Element("PhaseAuditSummary", sample == null ? string.Empty : sample.PhaseAuditSummary),
                Element("PhaseAuditFlags", sample == null ? string.Empty : sample.PhaseAuditFlags),
                Element("PhaseAuditVersion", sample == null ? string.Empty : sample.PhaseAuditVersion),
                Element("PhaseExpectedActions", sample == null ? string.Empty : sample.PhaseExpectedActions),
                Element("PhaseMeasuredMetrics", sample == null ? string.Empty : sample.PhaseMeasuredMetrics),
                Element("PhaseScoringHints", sample == null ? string.Empty : sample.PhaseScoringHints),
                Element("PhaseReviewQuestion", sample == null ? string.Empty : sample.PhaseReviewQuestion),
                Element("PhaseReviewVersion", sample == null ? string.Empty : sample.PhaseReviewVersion),
                Element("PhasePrevalidationStatus", sample == null ? string.Empty : sample.PhasePrevalidationStatus),
                Element("PhasePrevalidationSummary", sample == null ? string.Empty : sample.PhasePrevalidationSummary),
                Element("PhasePrevalidationFlags", sample == null ? string.Empty : sample.PhasePrevalidationFlags),
                Element("PhasePrevalidationVersion", sample == null ? string.Empty : sample.PhasePrevalidationVersion),
                Element("SurfaceContextCode", sample == null ? string.Empty : sample.SurfaceContextCode),
                Element("SurfaceContextName", sample == null ? string.Empty : sample.SurfaceContextName),
                Element("SurfaceContextReason", sample == null ? string.Empty : sample.SurfaceContextReason),
                Element("RunwayCandidate", Bool(sample != null && sample.RunwayCandidate)),
                Element("TaxiwayCandidate", Bool(sample != null && sample.TaxiwayCandidate)),
                Element("GateAreaCandidate", Bool(sample != null && sample.GateAreaCandidate)),
                Element("SurfaceContextReliable", Bool(sample != null && sample.SurfaceContextReliable)),
                Element("SurfaceContextVersion", sample == null ? string.Empty : sample.SurfaceContextVersion),
                Element("RunwayContextCode", sample == null ? string.Empty : sample.RunwayContextCode),
                Element("RunwayContextName", sample == null ? string.Empty : sample.RunwayContextName),
                Element("RunwayContextReason", sample == null ? string.Empty : sample.RunwayContextReason),
                Element("EstimatedRunwayIdent", sample == null ? string.Empty : sample.EstimatedRunwayIdent),
                Element("EstimatedRunwayReciprocalIdent", sample == null ? string.Empty : sample.EstimatedRunwayReciprocalIdent),
                Element("EstimatedRunwayHeadingDeg", FormatDecimal(sample == null ? 0d : sample.EstimatedRunwayHeadingDeg, 1)),
                Element("RunwayHeadingDeltaDeg", FormatDecimal(sample == null ? 0d : sample.RunwayHeadingDeltaDeg, 1)),
                Element("RunwayAlignedCandidate", Bool(sample != null && sample.RunwayAlignedCandidate)),
                Element("RunwayEntryCandidate", Bool(sample != null && sample.RunwayEntryCandidate)),
                Element("RunwayExitCandidate", Bool(sample != null && sample.RunwayExitCandidate)),
                Element("TakeoffRollCandidate", Bool(sample != null && sample.TakeoffRollCandidate)),
                Element("LandingRollCandidate", Bool(sample != null && sample.LandingRollCandidate)),
                Element("TouchdownZoneCandidate", Bool(sample != null && sample.TouchdownZoneCandidate)),
                Element("TaxiwayProbable", Bool(sample != null && sample.TaxiwayProbable)),
                Element("RunwayGeometryAvailable", Bool(sample != null && sample.RunwayGeometryAvailable)),
                Element("RunwayContextReliable", Bool(sample != null && sample.RunwayContextReliable)),
                Element("RunwayContextVersion", sample == null ? string.Empty : sample.RunwayContextVersion),
                Element("FacilityRunwayGeometryAvailable", Bool(sample != null && sample.FacilityRunwayGeometryAvailable)),
                Element("FacilityRunwayGeometryStatus", sample == null ? string.Empty : sample.FacilityRunwayGeometryStatus),
                Element("FacilityNearestRunwayAirportIcao", sample == null ? string.Empty : sample.FacilityNearestRunwayAirportIcao),
                Element("FacilityNearestRunwayIdent", sample == null ? string.Empty : sample.FacilityNearestRunwayIdent),
                Element("FacilityNearestRunwayReciprocalIdent", sample == null ? string.Empty : sample.FacilityNearestRunwayReciprocalIdent),
                Element("FacilityNearestRunwayHeadingDeg", FormatDecimal(sample == null ? 0d : sample.FacilityNearestRunwayHeadingDeg, 1)),
                Element("FacilityNearestRunwayLengthMeters", FormatDecimal(sample == null ? 0d : sample.FacilityNearestRunwayLengthMeters, 0)),
                Element("FacilityNearestRunwayWidthMeters", FormatDecimal(sample == null ? 0d : sample.FacilityNearestRunwayWidthMeters, 0)),
                Element("FacilityNearestRunwayDistanceMeters", FormatDecimal(sample == null ? 0d : sample.FacilityNearestRunwayDistanceMeters, 0)),
                Element("FacilityRunwayLateralOffsetMeters", FormatDecimal(sample == null ? 0d : sample.FacilityRunwayLateralOffsetMeters, 0)),
                Element("FacilityRunwayLongitudinalOffsetMeters", FormatDecimal(sample == null ? 0d : sample.FacilityRunwayLongitudinalOffsetMeters, 0)),
                Element("FacilityRunwayHeadingErrorDeg", FormatDecimal(sample == null ? 0d : sample.FacilityRunwayHeadingErrorDeg, 1)),
                Element("FacilityRunwayDistanceFromThresholdMeters", FormatDecimal(sample == null ? 0d : sample.FacilityRunwayDistanceFromThresholdMeters, 0)),
                Element("FacilityOnRunwayCandidate", Bool(sample != null && sample.FacilityOnRunwayCandidate)),
                Element("FacilityRunwayAlignedCandidate", Bool(sample != null && sample.FacilityRunwayAlignedCandidate)),
                Element("FacilityTouchdownZoneCandidate", Bool(sample != null && sample.FacilityTouchdownZoneCandidate)),
                Element("FacilityRunwayGeometrySummary", sample == null ? string.Empty : sample.FacilityRunwayGeometrySummary),
                Element("FacilityRunwayGeometryCount", sample == null ? "0" : sample.FacilityRunwayGeometryCount.ToString(CultureInfo.InvariantCulture)),
                Element("FacilityRunwayGeometryVersion", sample == null ? string.Empty : sample.FacilityRunwayGeometryVersion),
                Element("Lat", FormatDecimal(sample == null ? 0d : sample.Latitude, 5)),
                Element("Lon", FormatDecimal(sample == null ? 0d : sample.Longitude, 5)),
                Element("Altitude", ToIntString(sample == null ? 0d : ResolveMsl(sample))),
                Element("AltitudeMslFt", ToIntString(sample == null ? 0d : ResolveMsl(sample))),
                Element("AGL", ToIntString(sample == null ? 0d : ResolveAgl(sample))),
                Element("AltitudeAglFt", ToIntString(sample == null ? 0d : ResolveAgl(sample))),
                Element("PressureAltitudeFt", ToIntString(sample == null ? 0d : sample.PressureAltitudeFeet)),
                Element("FlightLevel", sample == null ? string.Empty : sample.FlightLevel),
                Element("IAS", ToIntString(sample == null ? 0d : sample.IndicatedAirspeed)),
                Element("GS", ToIntString(sample == null ? 0d : sample.GroundSpeed)),
                Element("VS", ToIntString(sample == null ? 0d : sample.VerticalSpeed)),
                Element("FuelKg", ToIntString(sample == null ? 0d : ResolveFuelKg(sample, 0d))));
        }

        private static string ResolveOperationalPhase(SimData sample, IReadOnlyList<SimData> telemetry, FlightReport report)
        {
            if (sample == null) return "UNKNOWN";

            if (!string.IsNullOrWhiteSpace(sample.OperationalPhaseCode))
            {
                switch (sample.OperationalPhaseCode.Trim().ToUpperInvariant())
                {
                    case "PRE": return "PREFLIGHT";
                    case "BRD": return "BOARDING";
                    case "TAX_OUT": return "TAXI_OUT";
                    case "TO": return "TAKEOFF";
                    case "CLB": return "CLIMB";
                    case "CRZ": return "CRUISE";
                    case "DES": return "DESCENT";
                    case "APP": return "APPROACH";
                    case "LDG": return "LANDING";
                    case "TAX_IN": return "TAXI_IN";
                    case "GATE": return "GATE";
                    case "DEB": return "DEBOARDING";
                }
            }

            var list = telemetry == null ? new List<SimData>() : telemetry.Where(s => s != null).OrderBy(s => s.CapturedAtUtc).ToList();
            var takeoff = list.FirstOrDefault(s => !s.OnGround && ResolveAgl(s) > 30d);
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
            if (!sample.OnGround && ResolveAgl(sample) < 3000d && sample.VerticalSpeed < -200d) return "APPROACH";
            if (!sample.OnGround && ResolveAgl(sample) < 1000d && sample.VerticalSpeed >= -200d) return "TAKEOFF";
            if (sample.VerticalSpeed > 300d) return "CLIMB";
            if (sample.VerticalSpeed < -300d) return "DESCENT";
            return "CRUISE";
        }

        private static XElement BuildAltitudeEvidence(IReadOnlyList<SimData> telemetry, SimData? firstSample, SimData? lastSample)
        {
            var list = telemetry == null
                ? new List<SimData>()
                : telemetry.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();

            var maxMsl = list.Count == 0 ? 0d : list.Max(sample => ResolveMsl(sample));
            var maxAgl = list.Count == 0 ? 0d : list.Max(sample => ResolveAgl(sample));
            var minAgl = list.Count == 0 ? 0d : list.Min(sample => ResolveAgl(sample));
            var maxPressure = list.Count == 0 ? 0d : list.Max(sample => sample.PressureAltitudeFeet);
            var transition = list.Count == 0 ? 10000d : list.Select(sample => sample.TransitionAltitudeFeet).DefaultIfEmpty(10000d).FirstOrDefault(value => value > 0d);
            if (transition <= 0d) transition = 10000d;

            return new XElement("Altitude",
                Element("Schema", "PWG_ALTITUDE_RESOLVER_C0"),
                Element("TransitionAltitudeFt", ToIntString(transition)),
                Element("SampleCount", list.Count.ToString(CultureInfo.InvariantCulture)),
                Element("MaxAltitudeMslFt", ToIntString(maxMsl)),
                Element("MaxAglFt", ToIntString(maxAgl)),
                Element("MinAglFt", ToIntString(minAgl)),
                Element("MaxPressureAltitudeFt", ToIntString(maxPressure)),
                Element("FirstAltitudeMslFt", ToIntString(firstSample == null ? 0d : ResolveMsl(firstSample))),
                Element("FirstAltitudeAglFt", ToIntString(firstSample == null ? 0d : ResolveAgl(firstSample))),
                Element("FirstGroundElevationFt", ToIntString(firstSample == null ? 0d : firstSample.GroundElevationFeet)),
                Element("LastAltitudeMslFt", ToIntString(lastSample == null ? 0d : ResolveMsl(lastSample))),
                Element("LastAltitudeAglFt", ToIntString(lastSample == null ? 0d : ResolveAgl(lastSample))),
                Element("LastGroundElevationFt", ToIntString(lastSample == null ? 0d : lastSample.GroundElevationFeet)),
                Element("LastPressureAltitudeFt", ToIntString(lastSample == null ? 0d : lastSample.PressureAltitudeFeet)),
                Element("LastFlightLevel", lastSample == null ? string.Empty : lastSample.FlightLevel),
                Element("LastDisplayMode", lastSample == null ? string.Empty : lastSample.DisplayAltitudeMode),
                Element("LastDisplayText", lastSample == null ? string.Empty : lastSample.DisplayAltitudeText),
                Element("AltitudeSource", lastSample == null ? string.Empty : lastSample.AltitudeSource),
                Element("IsReliable", Bool(lastSample != null && lastSample.IsAltitudeReliable))
            );
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
            if (telemetry == null || telemetry.Count == 0)
            {
                return null;
            }

            var list = telemetry.Where(sample => sample != null).OrderBy(sample => sample.CapturedAtUtc).ToList();
            var explicitTouchdown = list.FirstOrDefault(sample => sample.TouchdownDetected || string.Equals(sample.OperationalPhaseCode, "LDG", StringComparison.OrdinalIgnoreCase));
            if (explicitTouchdown != null)
            {
                return explicitTouchdown;
            }

            SimData? previous = null;
            foreach (var sample in list)
            {
                if (previous != null && !previous.OnGround && sample.OnGround)
                {
                    return sample;
                }

                previous = sample;
            }

            return list.FirstOrDefault(sample => sample.OnGround && ResolveAgl(sample) <= 15d && sample.GroundSpeed <= 80d && list.Any(s => !s.OnGround));
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

        private static double ResolveTouchdownGForce(IReadOnlyList<SimData> telemetry, SimData? touchdown, double fallback)
        {
            var value = ResolveGForce(touchdown, fallback);
            if (Math.Abs(value) > 0.01d) return value;

            if (touchdown != null && telemetry != null && telemetry.Count > 0)
            {
                var start = touchdown.CapturedAtUtc.AddSeconds(-6);
                var end = touchdown.CapturedAtUtc.AddSeconds(6);
                var around = telemetry
                    .Where(sample => sample != null && sample.CapturedAtUtc >= start && sample.CapturedAtUtc <= end)
                    .Select(sample => ResolveGForce(sample, fallback))
                    .Where(g => Math.Abs(g) > 0.01d)
                    .ToList();
                if (around.Count > 0)
                {
                    return around.OrderByDescending(Math.Abs).First();
                }

                // Si hay touchdown confirmado pero sin dato de G valido, usar baseline operativo.
                return 1.0d;
            }

            return value;
        }

        private static string ResolveRunwayIdent(SimData? sample)
        {
            return FirstNonEmpty(
                sample == null ? string.Empty : sample.FacilityNearestRunwayIdent,
                sample == null ? string.Empty : sample.EstimatedRunwayIdent);
        }

        private static string ResolveGateLabel(SimData? sample)
        {
            var label = FirstNonEmpty(sample == null ? string.Empty : sample.FacilityNearestTaxiParkingLabel);
            return string.IsNullOrWhiteSpace(label) ? "0" : label;
        }

        private static double ResolveRunwayLengthMeters(SimData? sample)
        {
            if (sample == null) return 0d;
            return sample.FacilityNearestRunwayLengthMeters > 0d ? sample.FacilityNearestRunwayLengthMeters : 0d;
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

        private static double ResolveMsl(SimData? sample)
        {
            if (sample == null) return 0d;
            return sample.AltitudeMslFeet > 0d ? sample.AltitudeMslFeet : sample.AltitudeFeet;
        }

        private static double ResolveAgl(SimData? sample)
        {
            if (sample == null) return 0d;
            if (sample.OnGround) return 0d;
            return sample.AltitudeAglFeet >= 0d ? sample.AltitudeAglFeet : sample.AltitudeAGL;
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
