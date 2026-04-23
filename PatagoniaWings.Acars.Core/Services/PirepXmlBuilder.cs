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
    public sealed class PirepXmlBuilder
    {
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
            var telemetry = telemetryLog ?? Array.Empty<SimData>();
            var lastSample = telemetry.Count > 0 ? telemetry[telemetry.Count - 1] : null;

            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement("PatagoniaWingsPirep",
                    new XAttribute("schemaVersion", "2.0"),
                    new XAttribute("visibility", "hidden"),
                    new XAttribute("generatedAfterCompletion", "true"),
                    new XElement("Header",
                        Element("GeneratedAtUtc", FormatUtc(generatedAtUtc)),
                        Element("ReservationId", dispatch.ReservationId),
                        Element("DispatchId", dispatch.DispatchId),
                        Element("DispatchToken", dispatch.DispatchToken),
                        Element("PilotCallsign", pilot.CallSign),
                        Element("PilotName", pilot.FullName),
                        Element("PilotRank", pilot.RankName)
                    ),
                    new XElement("Lifecycle",
                        Element("ReservationStatusTarget", "completed"),
                        Element("PirepVisibility", "hidden"),
                        Element("ValidationStatus", "pending"),
                        Element("ScoringStatus", "pending"),
                        Element("XmlStatus", "generated"),
                        Element("ScoringAuthority", "web_supabase")
                    ),
                    new XElement("DispatchWeb",
                        Element("FlightNumber", dispatch.FlightDesignator),
                        Element("RouteCode", dispatch.RouteCode),
                        Element("FlightModeCode", dispatch.FlightMode),
                        Element("OriginIcao", dispatch.DepartureIcao),
                        Element("DestinationIcao", dispatch.ArrivalIcao),
                        Element("AlternateIcao", dispatch.AlternateIcao),
                        Element("AircraftId", dispatch.AircraftId),
                        Element("AirframeIcao", dispatch.AircraftIcao),
                        Element("AircraftDisplayName", dispatch.AircraftDisplayName),
                        Element("AircraftRegistration", dispatch.AircraftRegistration),
                        Element("RouteText", dispatch.RouteText),
                        Element("CruiseLevel", dispatch.CruiseLevel),
                        Element("PassengerCount", dispatch.PassengerCount),
                        Element("CargoKg", FormatDouble(dispatch.CargoKg)),
                        Element("FuelPlannedKg", FormatDouble(dispatch.FuelPlannedKg)),
                        Element("PayloadKg", FormatDouble(dispatch.PayloadKg)),
                        Element("ZeroFuelWeightKg", FormatDouble(dispatch.ZeroFuelWeightKg))
                    ),
                    new XElement("FlightSummary",
                        Element("DepartureIcao", report.DepartureIcao),
                        Element("ArrivalIcao", report.ArrivalIcao),
                        Element("AirframeIcao", report.AircraftIcao),
                        Element("DepartureTimeUtc", FormatUtc(report.DepartureTime)),
                        Element("ArrivalTimeUtc", FormatUtc(report.ArrivalTime)),
                        Element("BlockMinutes", ((int)Math.Max(1, Math.Round(report.Duration.TotalMinutes))).ToString(CultureInfo.InvariantCulture)),
                        Element("DistanceNm", FormatDouble(report.Distance)),
                        Element("FuelUsedKg", FormatDouble(report.FuelUsed * 0.45359237d)),
                        Element("LandingVS", FormatDouble(report.LandingVS)),
                        Element("LandingG", FormatDouble(report.LandingG)),
                        Element("Simulator", report.Simulator.ToString()),
                        Element("Remarks", report.Remarks)
                    ),
                    new XElement("AircraftState",
                        Element("PlannedAltitude", activeFlight == null ? string.Empty : activeFlight.PlannedAltitude.ToString(CultureInfo.InvariantCulture)),
                        Element("PlannedSpeed", activeFlight == null ? string.Empty : activeFlight.PlannedSpeed.ToString(CultureInfo.InvariantCulture)),
                        Element("BlockFuelKg", activeFlight == null ? string.Empty : FormatDouble(activeFlight.BlockFuel)),
                        Element("ZeroFuelWeightKg", activeFlight == null ? string.Empty : FormatDouble(activeFlight.ZeroFuelWeight)),
                        Element("LastLatitude", lastSample == null ? string.Empty : FormatDouble(lastSample.Latitude)),
                        Element("LastLongitude", lastSample == null ? string.Empty : FormatDouble(lastSample.Longitude)),
                        Element("LastAltitudeFeet", lastSample == null ? string.Empty : FormatDouble(lastSample.AltitudeFeet)),
                        Element("LastGroundSpeedKnots", lastSample == null ? string.Empty : FormatDouble(lastSample.GroundSpeed)),
                        Element("LastHeadingDegrees", lastSample == null ? string.Empty : FormatDouble(lastSample.Heading))
                    ),
                    new XElement("Scoring",
                        Element("Authority", "acars_client_v3"),
                        Element("PatagoniaScore", report.PatagoniaScore.ToString(CultureInfo.InvariantCulture)),
                        Element("PatagoniaGrade", report.PatagoniaGrade),
                        Element("ProcedureScore", report.ProcedureScore.ToString(CultureInfo.InvariantCulture)),
                        Element("ProcedureGrade", report.ProcedureGrade),
                        Element("PerformanceScore", report.PerformanceScore.ToString(CultureInfo.InvariantCulture)),
                        Element("PerformanceGrade", report.PerformanceGrade),
                        new XElement("Violations",
                            new XAttribute("count", report.Violations?.Count ?? 0),
                            (report.Violations ?? new System.Collections.Generic.List<PatagoniaWings.Acars.Core.Models.ScoreEvent>())
                                .Select(v => new XElement("Item",
                                    new XAttribute("code", v.Code),
                                    new XAttribute("phase", v.Phase),
                                    new XAttribute("pts", v.Points),
                                    v.Description))
                        ),
                        new XElement("Bonuses",
                            new XAttribute("count", report.Bonuses?.Count ?? 0),
                            (report.Bonuses ?? new System.Collections.Generic.List<PatagoniaWings.Acars.Core.Models.ScoreEvent>())
                                .Select(b => new XElement("Item",
                                    new XAttribute("code", b.Code),
                                    new XAttribute("phase", b.Phase),
                                    new XAttribute("pts", b.Points),
                                    b.Description))
                        ),
                        new XElement("EvaluationContract",
                            Element("ContractVersion", report.Evaluation == null ? string.Empty : report.Evaluation.ContractVersion),
                            Element("RulesetVersion", report.Evaluation == null ? string.Empty : report.Evaluation.RulesetVersion),
                            Element("RulesFilePath", report.Evaluation == null ? string.Empty : report.Evaluation.RulesFilePath),
                            Element("FlightValid", report.Evaluation == null ? string.Empty : (report.Evaluation.FlightValid ? "true" : "false")),
                            new XElement("InvalidReasons",
                                (report.Evaluation == null ? new List<string>() : report.Evaluation.InvalidReasons)
                                    .Select(item => new XElement("Reason", item))
                            ),
                            new XElement("Incidents",
                                (report.Evaluation == null ? new List<PatagoniaIncidentRecord>() : report.Evaluation.Incidents)
                                    .Select(item => new XElement("Incident",
                                        new XAttribute("code", item.Code),
                                        new XAttribute("phase", item.Phase),
                                        new XAttribute("severity", item.Severity),
                                        new XAttribute("source", item.Source),
                                        new XAttribute("scoreDelta", item.ScoreDelta),
                                        new XAttribute("timestampUtc", item.TimestampUtc == default(DateTime) ? string.Empty : FormatUtc(item.TimestampUtc)),
                                        item.Message))
                            ),
                            new XElement("GateFailures",
                                new XAttribute("count", report.Evaluation == null || report.Evaluation.GateFailures == null ? 0 : report.Evaluation.GateFailures.Count),
                                (report.Evaluation == null ? new List<PatagoniaTriggeredRuleResult>() : report.Evaluation.GateFailures)
                                    .Select(item => new XElement("Item",
                                        new XAttribute("id", item.RuleId),
                                        new XAttribute("phase", item.Phase),
                                        new XAttribute("target", item.ScoreTarget),
                                        new XAttribute("delta", item.ScoreDelta),
                                        item.Message))
                            ),
                            new XElement("PhaseResults",
                                (report.Evaluation == null ? new List<PatagoniaPhaseResult>() : report.Evaluation.PhaseResults)
                                    .Select(item => new XElement("Phase",
                                        new XAttribute("code", item.Phase),
                                        new XAttribute("delta", item.ScoreDelta),
                                        new XAttribute("bonuses", item.BonusCount),
                                        new XAttribute("penalties", item.PenaltyCount),
                                        new XAttribute("gateFailures", item.GateFailureCount),
                                        new XElement("TriggeredRules",
                                            item.TriggeredRuleIds.Select(ruleId => new XElement("RuleId", ruleId))))))
                            ,
                            new XElement("TelemetrySummary",
                                Element("SamplesCount", report.Evaluation == null ? string.Empty : report.Evaluation.TelemetrySummary.SamplesCount.ToString(CultureInfo.InvariantCulture)),
                                Element("AirborneSamplesCount", report.Evaluation == null ? string.Empty : report.Evaluation.TelemetrySummary.AirborneSamplesCount.ToString(CultureInfo.InvariantCulture)),
                                Element("OnGroundSamplesCount", report.Evaluation == null ? string.Empty : report.Evaluation.TelemetrySummary.OnGroundSamplesCount.ToString(CultureInfo.InvariantCulture)),
                                Element("DistanceNm", report.Evaluation == null ? string.Empty : FormatDouble(report.Evaluation.TelemetrySummary.DistanceNm)),
                                Element("FuelUsedKg", report.Evaluation == null ? string.Empty : FormatDouble(report.Evaluation.TelemetrySummary.FuelUsedKg)),
                                Element("MaxAltitudeFt", report.Evaluation == null ? string.Empty : FormatDouble(report.Evaluation.TelemetrySummary.MaxAltitudeFt)),
                                Element("MaxSpeedKts", report.Evaluation == null ? string.Empty : FormatDouble(report.Evaluation.TelemetrySummary.MaxSpeedKts))
                            ),
                            new XElement("RuleAuditLog",
                                (report.Evaluation == null ? new List<PatagoniaRuleAuditEntry>() : report.Evaluation.RuleAuditLog)
                                    .Select(item => new XElement("Rule",
                                        new XAttribute("id", item.RuleId),
                                        new XAttribute("phase", item.Phase),
                                        new XAttribute("result", item.Result),
                                        new XAttribute("scoreDelta", item.ScoreDelta),
                                        new XAttribute("source", item.Source),
                                        new XAttribute("timestampUtc", item.TimestampUtc == default(DateTime) ? string.Empty : FormatUtc(item.TimestampUtc)),
                                        Element("ObservedValue", item.ObservedValue),
                                        Element("ExpectedValue", item.ExpectedValue),
                                        Element("AppliedTolerance", item.AppliedTolerance),
                                        Element("Reason", item.Reason),
                                        Element("Context", item.Context)))
                            )
                        )
                    ),
                    new XElement("TelemetryLog",
                        telemetry.Select((sample, index) =>
                            new XElement("Sample",
                                new XAttribute("sequence", index + 1),
                                Element("CapturedAtUtc", FormatUtc(sample.CapturedAtUtc)),
                                Element("Latitude", FormatDouble(sample.Latitude)),
                                Element("Longitude", FormatDouble(sample.Longitude)),
                                Element("AltitudeFeet", FormatDouble(sample.AltitudeFeet)),
                                Element("IasKnots", FormatDouble(sample.IndicatedAirspeed)),
                                Element("GroundSpeedKnots", FormatDouble(sample.GroundSpeed)),
                                Element("VerticalSpeedFpm", FormatDouble(sample.VerticalSpeed)),
                                Element("HeadingDegrees", FormatDouble(sample.Heading)),
                                Element("FuelLbs", FormatDouble(sample.FuelTotalLbs)),
                                Element("OnGround", sample.OnGround ? "true" : "false")
                            ))
                    )
                )
            );

            var xmlContent = document.ToString();
            return new BuildResult
            {
                GeneratedAtUtc = generatedAtUtc,
                XmlContent = xmlContent,
                ChecksumSha256 = ComputeSha256(xmlContent),
                FileName = BuildFileName(dispatch, generatedAtUtc)
            };
        }

        private static XElement Element(string name, object? value)
        {
            return new XElement(name, value ?? string.Empty);
        }

        private static string FormatUtc(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        private static string FormatUtc(DateTime? value)
        {
            return value.HasValue ? FormatUtc(value.Value) : string.Empty;
        }

        private static string FormatDouble(double value)
        {
            return Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string BuildFileName(PreparedDispatch dispatch, DateTime generatedAtUtc)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "PWG_PIREP_{0}_{1}_{2}_{3:yyyyMMdd_HHmmss}.xml",
                Sanitize(dispatch.FlightDesignator),
                Sanitize(dispatch.DepartureIcao),
                Sanitize(dispatch.ArrivalIcao),
                generatedAtUtc);
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "NA";
            }

            var builder = new StringBuilder();
            foreach (var character in value.Trim())
            {
                if (char.IsLetterOrDigit(character) || character == '-' || character == '_')
                {
                    builder.Append(character);
                }
            }

            return builder.Length == 0 ? "NA" : builder.ToString();
        }

        private static string ComputeSha256(string payload)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(payload ?? string.Empty);
                var hash = sha256.ComputeHash(bytes);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (var current in hash)
                {
                    builder.Append(current.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }
    }
}
