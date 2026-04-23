using System.Collections.Generic;

namespace PatagoniaWings.Acars.Core.Models
{
    /// <summary>
    /// Fuente unica de verdad del reglaje Patagonia Wings cargada desde JSON.
    /// Toda regla, gate y scoring debe salir de este contrato.
    /// </summary>
    public sealed class PatagoniaRulesetDefinition
    {
        public string SchemaVersion { get; set; } = "2.0";
        public string ProgramCode { get; set; } = "PATAGONIA_WINGS";
        public string VisibleScoreName { get; set; } = "Patagonia Score";
        public PatagoniaScoringDefaults Scoring { get; set; } = new PatagoniaScoringDefaults();
        public PatagoniaOutputContractDefinition OutputContract { get; set; } = new PatagoniaOutputContractDefinition();
        public PatagoniaGlobalToleranceDefinition GlobalTolerances { get; set; } = new PatagoniaGlobalToleranceDefinition();
        public List<string> FlightPhases { get; set; } = new List<string>();
        public List<string> Categories { get; set; } = new List<string>();
        public List<PatagoniaRuleDefinition> Rules { get; set; } = new List<PatagoniaRuleDefinition>();
    }

    public sealed class PatagoniaScoringDefaults
    {
        public int ProcedureBase { get; set; } = 100;
        public int PerformanceBase { get; set; } = 0;
        public int PatagoniaBase { get; set; } = 100;
        public int MinScore { get; set; } = -1000;
        public int MaxScore { get; set; } = 9999;
        public double PatagoniaProcedureWeight { get; set; } = 1.0;
        public double PatagoniaPerformanceWeight { get; set; } = 1.0;
    }

    public sealed class PatagoniaOutputContractDefinition
    {
        public string ContractVersion { get; set; } = "patagonia-eval.v2";
        public List<string> WebSections { get; set; } = new List<string>();
    }

    public sealed class PatagoniaGlobalToleranceDefinition
    {
        public int ShortTimeSeconds { get; set; } = 5;
        public int MediumTimeSeconds { get; set; } = 10;
        public int SustainedEventSeconds { get; set; } = 30;
        public double TriggerAltitudeFeet { get; set; } = 500;
        public double TriggerSpeedKnots { get; set; } = 15;
        public double TriggerVerticalSpeedFpm { get; set; } = 500;
    }

    public sealed class PatagoniaRuleDefinition
    {
        public PatagoniaRuleMetadata Metadata { get; set; } = new PatagoniaRuleMetadata();
        public PatagoniaRuleEvaluationDefinition Evaluation { get; set; } = new PatagoniaRuleEvaluationDefinition();
        public PatagoniaRuleEffectDefinition Effect { get; set; } = new PatagoniaRuleEffectDefinition();
    }

    public sealed class PatagoniaRuleMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Phase { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string Severity { get; set; } = string.Empty;
        public string DataSource { get; set; } = string.Empty;
        public string LogMessage { get; set; } = string.Empty;
        public string UiMessage { get; set; } = string.Empty;
        public List<string> Scopes { get; set; } = new List<string>();
        public List<string> Tags { get; set; } = new List<string>();
    }

    public sealed class PatagoniaRuleEvaluationDefinition
    {
        /// <summary>
        /// Modos soportados:
        /// success_on_match: la condicion expresa cumplimiento.
        /// violation_on_match: la condicion expresa infraccion.
        /// inform_on_match: la condicion solo registra informacion visible.
        /// </summary>
        public string OutcomeMode { get; set; } = "violation_on_match";
        public List<string> FlightTypes { get; set; } = new List<string>();
        public List<string> ApplicableAircraft { get; set; } = new List<string>();
        public List<string> RequiredCertifications { get; set; } = new List<string>();
        public List<string> RequiredTelemetry { get; set; } = new List<string>();
        public PatagoniaDependencyDefinition DispatchDependency { get; set; } = new PatagoniaDependencyDefinition();
        public PatagoniaDependencyDefinition WeatherDependency { get; set; } = new PatagoniaDependencyDefinition();
        public PatagoniaRuleToleranceDefinition Tolerances { get; set; } = new PatagoniaRuleToleranceDefinition();
        public PatagoniaRuleConditionDefinition Condition { get; set; } = new PatagoniaRuleConditionDefinition();
    }

    public sealed class PatagoniaRuleEffectDefinition
    {
        public string RuleType { get; set; } = "info";
        public string ScoreTarget { get; set; } = "procedure";
        public PatagoniaScoreDeltaDefinition ScoreDelta { get; set; } = new PatagoniaScoreDeltaDefinition();
        public bool InvalidatesFlight { get; set; }
        public string IncidentCode { get; set; } = string.Empty;
    }

    public sealed class PatagoniaScoreDeltaDefinition
    {
        public int Value { get; set; }
        public int Procedure { get; set; }
        public int Performance { get; set; }
        public int Patagonia { get; set; }
    }

    public sealed class PatagoniaDependencyDefinition
    {
        public bool Required { get; set; }
        public string Mode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public sealed class PatagoniaRuleToleranceDefinition
    {
        public string TimeProfile { get; set; } = string.Empty;
        public Dictionary<string, object> Values { get; set; } = new Dictionary<string, object>();
    }

    public sealed class PatagoniaRuleConditionDefinition
    {
        public string Metric { get; set; } = string.Empty;
        public string Operator { get; set; } = string.Empty;
        public object ExpectedValue { get; set; } = string.Empty;
        public object Value { get; set; } = string.Empty;
        public object Min { get; set; } = string.Empty;
        public object Max { get; set; } = string.Empty;
        public List<object> Values { get; set; } = new List<object>();
        public List<PatagoniaRuleConditionDefinition> All { get; set; } = new List<PatagoniaRuleConditionDefinition>();
        public List<PatagoniaRuleConditionDefinition> Any { get; set; } = new List<PatagoniaRuleConditionDefinition>();
    }
}
