using System;
using System.Collections.Generic;

namespace PatagoniaWings.Acars.Core.Models
{
    public static class PatagoniaAuditResults
    {
        public const string Pass = "PASS";
        public const string Fail = "FAIL";
        public const string Warn = "WARN";
        public const string NotApplicable = "N_A";
    }

    public sealed class PatagoniaEvaluationReport
    {
        public string ContractVersion { get; set; } = "patagonia-eval.v2";
        public string RulesetVersion { get; set; } = string.Empty;
        public string VisibleScoreName { get; set; } = "Patagonia Score";
        public string RulesFilePath { get; set; } = string.Empty;
        public int PatagoniaScore { get; set; }
        public int ProcedureScore { get; set; }
        public int PerformanceScore { get; set; }
        public string PatagoniaGrade { get; set; } = string.Empty;
        public string ProcedureGrade { get; set; } = string.Empty;
        public string PerformanceGrade { get; set; } = string.Empty;
        public bool FlightValid { get; set; } = true;
        public string Summary { get; set; } = string.Empty;
        public List<string> InvalidReasons { get; set; } = new List<string>();
        public List<PatagoniaIncidentRecord> Incidents { get; set; } = new List<PatagoniaIncidentRecord>();
        public List<PatagoniaRuleAuditEntry> RuleAuditLog { get; set; } = new List<PatagoniaRuleAuditEntry>();
        public List<PatagoniaTriggeredRuleResult> TriggeredRules { get; set; } = new List<PatagoniaTriggeredRuleResult>();
        public List<PatagoniaPhaseResult> PhaseResults { get; set; } = new List<PatagoniaPhaseResult>();
        public List<PatagoniaTriggeredRuleResult> Bonuses { get; set; } = new List<PatagoniaTriggeredRuleResult>();
        public List<PatagoniaTriggeredRuleResult> Penalties { get; set; } = new List<PatagoniaTriggeredRuleResult>();
        public List<PatagoniaTriggeredRuleResult> GateFailures { get; set; } = new List<PatagoniaTriggeredRuleResult>();
        public PatagoniaTelemetrySummary TelemetrySummary { get; set; } = new PatagoniaTelemetrySummary();
        public List<PatagoniaEventLogEntry> EventLog { get; set; } = new List<PatagoniaEventLogEntry>();
        public List<PatagoniaCertificationCheckResult> CertificationsChecks { get; set; } = new List<PatagoniaCertificationCheckResult>();
        public PatagoniaAircraftValidationResult AircraftValidation { get; set; } = new PatagoniaAircraftValidationResult();
        public PatagoniaDispatchValidationResult DispatchValidation { get; set; } = new PatagoniaDispatchValidationResult();
        public PatagoniaWeightsValidationResult WeightsValidation { get; set; } = new PatagoniaWeightsValidationResult();
        public PatagoniaWeatherValidationResult WeatherValidation { get; set; } = new PatagoniaWeatherValidationResult();
    }

    public sealed class PatagoniaRuleAuditEntry
    {
        public string RuleId { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }
        public string EvaluationState { get; set; } = string.Empty;
        public string ObservedValue { get; set; } = string.Empty;
        public string ExpectedValue { get; set; } = string.Empty;
        public string AppliedTolerance { get; set; } = string.Empty;
        public string Result { get; set; } = PatagoniaAuditResults.NotApplicable;
        public int ScoreDelta { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string RuleType { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string ScoreTarget { get; set; } = string.Empty;
        public string UiMessage { get; set; } = string.Empty;
        public string LogMessage { get; set; } = string.Empty;
        public bool Triggered { get; set; }
        public bool FlightInvalidated { get; set; }
        public string IncidentCode { get; set; } = string.Empty;
    }

    public sealed class PatagoniaTriggeredRuleResult
    {
        public string RuleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string RuleType { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string ScoreTarget { get; set; } = string.Empty;
        public int ScoreDelta { get; set; }
        public string Message { get; set; } = string.Empty;
        public string AuditResult { get; set; } = string.Empty;
    }

    public sealed class PatagoniaPhaseResult
    {
        public string Phase { get; set; } = string.Empty;
        public int ScoreDelta { get; set; }
        public int BonusCount { get; set; }
        public int PenaltyCount { get; set; }
        public int GateFailureCount { get; set; }
        public int AuditPassCount { get; set; }
        public int AuditWarnCount { get; set; }
        public int AuditNaCount { get; set; }
        public List<string> TriggeredRuleIds { get; set; } = new List<string>();
    }

    public sealed class PatagoniaTelemetrySummary
    {
        public int SamplesCount { get; set; }
        public int AirborneSamplesCount { get; set; }
        public int OnGroundSamplesCount { get; set; }
        public double DistanceNm { get; set; }
        public double FuelUsedKg { get; set; }
        public double MaxAltitudeFt { get; set; }
        public double MaxSpeedKts { get; set; }
        public double MaxBankDeg { get; set; }
        public double MaxPitchDeg { get; set; }
        public double LandingVsFpm { get; set; }
        public double LandingG { get; set; }
        public DateTime FirstSampleUtc { get; set; }
        public DateTime LastSampleUtc { get; set; }
        public double LastLatitude { get; set; }
        public double LastLongitude { get; set; }
        public string AircraftProfileCode { get; set; } = string.Empty;
    }

    public sealed class PatagoniaEventLogEntry
    {
        public DateTime TimestampUtc { get; set; }
        public string Phase { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public sealed class PatagoniaCertificationCheckResult
    {
        public string RuleId { get; set; } = string.Empty;
        public List<string> Required { get; set; } = new List<string>();
        public List<string> Provided { get; set; } = new List<string>();
        public bool Passed { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public sealed class PatagoniaAircraftValidationResult
    {
        public string AircraftIcao { get; set; } = string.Empty;
        public string AircraftTypeCode { get; set; } = string.Empty;
        public string AircraftDisplayName { get; set; } = string.Empty;
        public string DetectedProfileCode { get; set; } = string.Empty;
        public string FamilyGroup { get; set; } = string.Empty;
        public string AddonProvider { get; set; } = string.Empty;
        public string PrimaryTelemetrySource { get; set; } = string.Empty;
        public string CapabilityAuditState { get; set; } = string.Empty;
        public bool HasApplicableRules { get; set; }
        public bool AircraftMatchesDispatch { get; set; }
        public List<string> TelemetrySources { get; set; } = new List<string>();
        public List<string> SupportedSystems { get; set; } = new List<string>();
        public List<string> PartialSystems { get; set; } = new List<string>();
        public List<string> UnsupportedSystems { get; set; } = new List<string>();
        public List<string> NotApplicableSystems { get; set; } = new List<string>();
        public List<string> ExcludedByProfileSystems { get; set; } = new List<string>();
        public List<string> ExtraSourceSystems { get; set; } = new List<string>();
        public List<string> Issues { get; set; } = new List<string>();
        public List<string> MatchedRules { get; set; } = new List<string>();
        public List<PatagoniaAircraftCapabilityMatrixEntry> CapabilityMatrix { get; set; } = new List<PatagoniaAircraftCapabilityMatrixEntry>();
    }

    public sealed class PatagoniaAircraftCapabilityMatrixEntry
    {
        public string CapabilityCode { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ExpectedSource { get; set; } = string.Empty;
        public string ImplementedSource { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public bool Evaluate { get; set; }
        public string Observation { get; set; } = string.Empty;
    }

    public sealed class PatagoniaDispatchValidationResult
    {
        public bool DispatchPresent { get; set; }
        public bool FlightNumberPresent { get; set; }
        public bool OriginPresent { get; set; }
        public bool DestinationPresent { get; set; }
        public bool RoutePresent { get; set; }
        public bool Passed { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
    }

    public sealed class PatagoniaWeightsValidationResult
    {
        public bool Evaluated { get; set; }
        public bool Passed { get; set; }
        public double PlannedFuelKg { get; set; }
        public double ActualFuelStartKg { get; set; }
        public double ActualFuelEndKg { get; set; }
        public double PlannedPayloadKg { get; set; }
        public double PlannedZeroFuelWeightKg { get; set; }
        public double ActualZeroFuelWeightKg { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
    }

    public sealed class PatagoniaWeatherValidationResult
    {
        public bool Evaluated { get; set; }
        public bool Passed { get; set; } = true;
        public bool DepartureRaining { get; set; }
        public bool ArrivalRaining { get; set; }
        public double DepartureWindSpeed { get; set; }
        public double ArrivalWindSpeed { get; set; }
        public double DepartureWindDirection { get; set; }
        public double ArrivalWindDirection { get; set; }
        public List<string> Issues { get; set; } = new List<string>();
    }

    public sealed class PatagoniaIncidentRecord
    {
        public string Code { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public int ScoreDelta { get; set; }
        public DateTime TimestampUtc { get; set; }
    }

    public sealed class PatagoniaStartFlightGateResult
    {
        public bool CanStart { get; set; }
        public string Summary { get; set; } = string.Empty;
        public List<string> BlockingReasons { get; set; } = new List<string>();
        public PatagoniaEvaluationReport Evaluation { get; set; } = new PatagoniaEvaluationReport();
    }

    public sealed class PatagoniaFlightCloseoutPayload
    {
        public string ContractVersion { get; set; } = "patagonia-closeout.v1";
        public DateTime GeneratedAtUtc { get; set; }
        public string ReservationId { get; set; } = string.Empty;
        public string ResultUrl { get; set; } = string.Empty;
        public PatagoniaFlightCloseoutHeader Header { get; set; } = new PatagoniaFlightCloseoutHeader();
        public PatagoniaFlightCloseoutScores Scores { get; set; } = new PatagoniaFlightCloseoutScores();
        public PatagoniaEvaluationReport Evaluation { get; set; } = new PatagoniaEvaluationReport();
        public string PirepFileName { get; set; } = string.Empty;
        public string PirepChecksumSha256 { get; set; } = string.Empty;
        public string PirepXmlContent { get; set; } = string.Empty;
    }

    public sealed class PatagoniaFlightCloseoutHeader
    {
        public string PilotCallsign { get; set; } = string.Empty;
        public string FlightNumber { get; set; } = string.Empty;
        public string OriginIcao { get; set; } = string.Empty;
        public string DestinationIcao { get; set; } = string.Empty;
        public string AircraftIcao { get; set; } = string.Empty;
        public string AircraftRegistration { get; set; } = string.Empty;
        public string FlightMode { get; set; } = string.Empty;
        public double DurationMinutes { get; set; }
        public string RouteCode { get; set; } = string.Empty;
    }

    public sealed class PatagoniaFlightCloseoutScores
    {
        public int PatagoniaScore { get; set; }
        public int ProcedureScore { get; set; }
        public int PerformanceScore { get; set; }
        public bool FlightValid { get; set; } = true;
    }

    public sealed class PatagoniaPendingCloseoutEnvelope
    {
        public string Id { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime LastAttemptUtc { get; set; }
        public int RetryCount { get; set; }
        public string ReservationId { get; set; } = string.Empty;
        public Pilot Pilot { get; set; } = new Pilot();
        public PreparedDispatch Dispatch { get; set; } = new PreparedDispatch();
        public FlightReport Report { get; set; } = new FlightReport();
        public Flight ActiveFlight { get; set; } = new Flight();
        public List<SimData> TelemetryLog { get; set; } = new List<SimData>();
        public SimData LastSimData { get; set; } = new SimData();
        public List<AircraftDamageEvent> DamageEvents { get; set; } = new List<AircraftDamageEvent>();
        public PatagoniaFlightCloseoutPayload Payload { get; set; } = new PatagoniaFlightCloseoutPayload();
        public string LastError { get; set; } = string.Empty;
    }
}
