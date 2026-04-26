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
                    BuildVuelo(telemetry, report, generatedAtUtc),
                    BuildResumen(dispatch, report, telemetry, firstSample, lastSample, firstAirborne, touchdown, blockMinutes, flightMinutes, distanceNm, fuelStartKg, fuelEndKg, fuelUsedKg, fuelPerHour, fuelPer100Nm),
                    BuildIndicadores(telemetry, report, blockMinutes),
                    BuildAeropuertos(dispatch, report, firstSample, touchdown, lastSample),
                    BuildEvaluacionPendiente(),
                    BuildProcedimientosPendiente(),
                    BuildPerformancePendiente(),
                    BuildMeteorologia(firstSample, lastSample),
                    BuildAvion(dispatch, activeFlight, lastSample, fuelEndKg),
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
            var maxG = telemetry.Count == 0 ? report.LandingG : telemetry.Select(sample => sample.LandingG).DefaultIfEmpty(report.LandingG).Max();
            var minG = telemetry.Count == 0 ? 0d : telemetry.Select(sample => sample.LandingG).DefaultIfEmpty(0d).Min();

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
                Element("TouchdownGForce", FormatDecimal(report.LandingG, 3)),
                Element("TouchdownVS", ToIntString(report.LandingVS)),
                Element("MaxGForce", FormatDecimal(maxG, 3)),
                Element("MinGForce", FormatDecimal(minG, 3)),
                Element("PICsFailed", "0"),
                Element("CantidadPICs", "0"),
                Element("Networkd", "OFFLINE")
            );
        }

        private static XElement BuildAeropuertos(PreparedDispatch dispatch, FlightReport report, SimData? firstSample, SimData? touchdown, SimData? lastSample)
        {
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
                    Element("TouchdownGForce", FormatDecimal(report.LandingG, 3)),
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

        private static XElement BuildPerformancePendiente()
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
                    Element("PuntosPorPIC", "0"),
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
            var engineHealth = EstimateEngineHealth(lastSample);
            return new XElement("Avion",
                Element("Com1", "0.00"),
                Element("Com2", FormatFrequency(lastSample == null ? 0d : lastSample.Com2FrequencyMhz)),
                Element("Nav1", "0.00"),
                Element("Nav2", "0.00"),
                Element("Nav1OBS", "0"),
                Element("Transpondedor", lastSample == null ? "2000" : lastSample.TransponderCode.ToString(CultureInfo.InvariantCulture)),
                Element("Combustible", ToIntString(finalFuelKg)),
                Element("ParkingBreak", Bool(lastSample != null && lastSample.ParkingBrake)),
                Element("EstadoTren", FormatDecimal(lastSample == null ? 0d : (lastSample.GearDown ? 100d : 0d), 3)),
                Element("EstadoMotores", FormatDecimal(engineHealth, 3)),
                Element("EstadoFuselaje", "100.000"),
                Element("APUInstalada", "True"),
                Element("PacksInstalados", "True"),
                Element("ATInstalado", "True"),
                Element("TieneCrew", "True"),
                Element("TieneCopiloto", "True"),
                Element("AbrePuertas", "True"),
                Element("DetectaFuego", "True"),
                Element("TieneEngineMode", "True"),
                Element("MTOW", ToIntString(lastSample == null ? 0d : lastSample.TotalWeightKg)),
                Element("MLW", "0"),
                new XElement("Mantenimiento",
                    Element("EnviarMantenimiento", "No"),
                    new XElement("Fallas")
                )
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

            if (Math.Abs(previous.Com2FrequencyMhz - current.Com2FrequencyMhz) >= 0.01d && current.Com2FrequencyMhz > 0d)
            {
                changes.Add("COM2 to " + FormatFrequency(current.Com2FrequencyMhz));
            }

            if (previous.TransponderCode != current.TransponderCode && current.TransponderCode > 0)
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
            var lat1 = ToRad(a.Latitude);
            var lat2 = ToRad(b.Latitude);
            var dLat = ToRad(b.Latitude - a.Latitude);
            var dLon = ToRad(b.Longitude - a.Longitude);
            var h = Math.Sin(dLat / 2d) * Math.Sin(dLat / 2d) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon / 2d) * Math.Sin(dLon / 2d);
            var c = 2d * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1d - h));
            return 3440.065d * c;
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
