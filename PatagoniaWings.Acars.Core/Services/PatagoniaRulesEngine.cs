using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    /// <summary>
    /// Carga el archivo maestro de reglas y entrega siempre la definicion activa.
    /// </summary>
    public sealed class PatagoniaRulesProvider
    {
        private const string RulesRelativePath = @"Assets\Rules\patagonia-rules.json";
        private static readonly object SyncRoot = new object();
        private static PatagoniaRulesetDefinition _cachedRuleset = new PatagoniaRulesetDefinition();
        private static string _cachedPath = string.Empty;
        private static DateTime _cachedWriteUtc = DateTime.MinValue;

        public PatagoniaRulesetDefinition Load(string explicitPath = "")
        {
            var path = ResolveRulesPath(explicitPath);
            var writeUtc = File.GetLastWriteTimeUtc(path);

            lock (SyncRoot)
            {
                if (string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase) &&
                    _cachedWriteUtc == writeUtc)
                {
                    return _cachedRuleset;
                }

                var serializer = new JavaScriptSerializer
                {
                    MaxJsonLength = int.MaxValue,
                    RecursionLimit = 256
                };

                var json = File.ReadAllText(path);
                var ruleset = serializer.Deserialize<PatagoniaRulesetDefinition>(json) ?? new PatagoniaRulesetDefinition();
                Normalize(ruleset);

                _cachedPath = path;
                _cachedWriteUtc = writeUtc;
                _cachedRuleset = ruleset;
                return _cachedRuleset;
            }
        }

        public string ResolveRulesPath(string explicitPath = "")
        {
            var candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                candidates.Add(explicitPath);
            }

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            var currentDirectory = Environment.CurrentDirectory ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(baseDirectory))
            {
                candidates.Add(Path.Combine(baseDirectory, RulesRelativePath));
            }

            if (!string.IsNullOrWhiteSpace(currentDirectory))
            {
                candidates.Add(Path.Combine(currentDirectory, RulesRelativePath));
            }

            foreach (var root in EnumerateAncestorDirectories(baseDirectory).Concat(EnumerateAncestorDirectories(currentDirectory)))
            {
                candidates.Add(Path.Combine(root, RulesRelativePath));
                candidates.Add(Path.Combine(root, "PatagoniaWings.Acars.Master", RulesRelativePath));
            }

            foreach (var candidate in candidates.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("No encontre el archivo maestro de reglaje Patagonia Wings.", RulesRelativePath);
        }

        private static IEnumerable<string> EnumerateAncestorDirectories(string startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                yield break;
            }

            DirectoryInfo current;
            try
            {
                current = new DirectoryInfo(startPath);
            }
            catch
            {
                yield break;
            }

            while (current != null)
            {
                yield return current.FullName;
                current = current.Parent;
            }
        }

        private static void Normalize(PatagoniaRulesetDefinition ruleset)
        {
            ruleset.Scoring = ruleset.Scoring ?? new PatagoniaScoringDefaults();
            ruleset.OutputContract = ruleset.OutputContract ?? new PatagoniaOutputContractDefinition();
            ruleset.GlobalTolerances = ruleset.GlobalTolerances ?? new PatagoniaGlobalToleranceDefinition();
            ruleset.FlightPhases = ruleset.FlightPhases ?? new List<string>();
            ruleset.Categories = ruleset.Categories ?? new List<string>();
            ruleset.OutputContract.WebSections = ruleset.OutputContract.WebSections ?? new List<string>();
            ruleset.Rules = ruleset.Rules ?? new List<PatagoniaRuleDefinition>();

            foreach (var rule in ruleset.Rules)
            {
                rule.Metadata = rule.Metadata ?? new PatagoniaRuleMetadata();
                rule.Metadata.Scopes = rule.Metadata.Scopes ?? new List<string>();
                rule.Metadata.Tags = rule.Metadata.Tags ?? new List<string>();
                rule.Evaluation = rule.Evaluation ?? new PatagoniaRuleEvaluationDefinition();
                rule.Evaluation.FlightTypes = rule.Evaluation.FlightTypes ?? new List<string>();
                rule.Evaluation.ApplicableAircraft = rule.Evaluation.ApplicableAircraft ?? new List<string>();
                rule.Evaluation.RequiredCertifications = rule.Evaluation.RequiredCertifications ?? new List<string>();
                rule.Evaluation.RequiredTelemetry = rule.Evaluation.RequiredTelemetry ?? new List<string>();
                rule.Evaluation.DispatchDependency = rule.Evaluation.DispatchDependency ?? new PatagoniaDependencyDefinition();
                rule.Evaluation.WeatherDependency = rule.Evaluation.WeatherDependency ?? new PatagoniaDependencyDefinition();
                rule.Evaluation.Tolerances = rule.Evaluation.Tolerances ?? new PatagoniaRuleToleranceDefinition();
                rule.Evaluation.Tolerances.Values = rule.Evaluation.Tolerances.Values ?? new Dictionary<string, object>();
                rule.Evaluation.Condition = rule.Evaluation.Condition ?? new PatagoniaRuleConditionDefinition();
                rule.Evaluation.Condition.All = rule.Evaluation.Condition.All ?? new List<PatagoniaRuleConditionDefinition>();
                rule.Evaluation.Condition.Any = rule.Evaluation.Condition.Any ?? new List<PatagoniaRuleConditionDefinition>();
                rule.Evaluation.Condition.Values = rule.Evaluation.Condition.Values ?? new List<object>();
                rule.Effect = rule.Effect ?? new PatagoniaRuleEffectDefinition();
                rule.Effect.ScoreDelta = rule.Effect.ScoreDelta ?? new PatagoniaScoreDeltaDefinition();
            }
        }
    }

    public sealed class PatagoniaEvaluationInput
    {
        public Flight Flight { get; set; } = new Flight();
        public PreparedDispatch Dispatch { get; set; } = new PreparedDispatch();
        public FlightReport Report { get; set; } = new FlightReport();
        public IReadOnlyList<SimData> TelemetryLog { get; set; } = Array.Empty<SimData>();
        public SimData CurrentTelemetry { get; set; } = new SimData();
        public string PilotQualifications { get; set; } = string.Empty;
        public string PilotCertifications { get; set; } = string.Empty;
    }

    /// <summary>
    /// Motor principal de evaluacion Patagonia Wings.
    /// Lee reglas desde JSON, registra auditoria por regla y proyecta salidas legacy.
    /// </summary>
    public sealed class PatagoniaEvaluationService
    {
        private const string FinalEvaluationScope = "final_evaluation";
        private const string PreflightGateScope = "preflight_gate";

        private readonly PatagoniaRulesProvider _rulesProvider;

        public PatagoniaEvaluationService(PatagoniaRulesProvider? rulesProvider = null)
        {
            _rulesProvider = rulesProvider ?? new PatagoniaRulesProvider();
        }

        public PatagoniaEvaluationReport Evaluate(PatagoniaEvaluationInput input, string explicitRulesPath = "")
        {
            return EvaluateInternal(input, explicitRulesPath, FinalEvaluationScope);
        }

        public PatagoniaStartFlightGateResult EvaluateStartFlightGate(PatagoniaEvaluationInput input, string explicitRulesPath = "")
        {
            var evaluation = EvaluateInternal(input, explicitRulesPath, PreflightGateScope);
            var failures = evaluation.GateFailures
                .Select(item => string.IsNullOrWhiteSpace(item.Message) ? item.RuleId : item.Message)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new PatagoniaStartFlightGateResult
            {
                CanStart = failures.Count == 0,
                Summary = failures.Count == 0
                    ? "Start gate OK"
                    : "Start gate bloqueado: " + string.Join(" | ", failures),
                BlockingReasons = failures,
                Evaluation = evaluation
            };
        }

        private PatagoniaEvaluationReport EvaluateInternal(PatagoniaEvaluationInput input, string explicitRulesPath, string scope)
        {
            if (input == null) throw new ArgumentNullException("input");

            var ruleset = _rulesProvider.Load(explicitRulesPath);
            var rules = SelectRulesForScope(ruleset, scope);
            var profile = AircraftTelemetryProfileService.ResolveProfile(input);
            var metrics = BuildMetrics(input, profile);
            var providedCertifications = ParseTokens(input.PilotQualifications, input.PilotCertifications);
            var telemetrySummary = BuildTelemetrySummary(input);
            var aircraftValidation = BuildAircraftValidation(input, rules, profile);
            var dispatchValidation = BuildDispatchValidation(input);
            var weightsValidation = BuildWeightsValidation(input, profile);
            var weatherValidation = BuildWeatherValidation(input);

            var phaseResults = BuildPhaseResults(ruleset.FlightPhases);
            var auditLog = new List<PatagoniaRuleAuditEntry>();
            var incidents = new List<PatagoniaIncidentRecord>();
            var invalidReasons = new List<string>();
            var certificationChecks = new List<PatagoniaCertificationCheckResult>();

            var procedureScore = ruleset.Scoring.ProcedureBase;
            var performanceScore = ruleset.Scoring.PerformanceBase;
            var patagoniaScore = ruleset.Scoring.PatagoniaBase;

            foreach (var rule in rules)
            {
                var audit = EvaluateRule(rule, input, profile, metrics, providedCertifications, ruleset.GlobalTolerances, scope);
                auditLog.Add(audit);

                if (audit.Phase.Length == 0)
                {
                    audit.Phase = "GEN";
                }

                if (!phaseResults.ContainsKey(audit.Phase))
                {
                    phaseResults[audit.Phase] = new PatagoniaPhaseResult { Phase = audit.Phase };
                }

                var phaseResult = phaseResults[audit.Phase];
                phaseResult.ScoreDelta += audit.ScoreDelta;
                IncrementAuditCounters(phaseResult, audit.Result);

                var certificationCheck = BuildCertificationCheck(rule, providedCertifications);
                if (certificationCheck != null)
                {
                    certificationChecks.Add(certificationCheck);
                }

                if (!audit.Triggered)
                {
                    continue;
                }

                var triggered = MapTriggeredRule(audit);
                phaseResult.TriggeredRuleIds.Add(triggered.RuleId);

                ApplyScoreDelta(rule, audit.ScoreDelta, ref procedureScore, ref performanceScore, ref patagoniaScore);

                switch ((audit.RuleType ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "bonus":
                        phaseResult.BonusCount++;
                        break;
                    case "gate":
                        phaseResult.GateFailureCount++;
                        break;
                    case "penalty":
                        phaseResult.PenaltyCount++;
                        break;
                }

                if (audit.FlightInvalidated && !invalidReasons.Contains(audit.Reason))
                {
                    invalidReasons.Add(audit.Reason);
                }

                if (!string.IsNullOrWhiteSpace(audit.IncidentCode))
                {
                    incidents.Add(new PatagoniaIncidentRecord
                    {
                        Code = audit.IncidentCode,
                        Phase = audit.Phase,
                        Severity = audit.Severity,
                        Message = audit.Reason,
                        Source = audit.Source,
                        ScoreDelta = audit.ScoreDelta,
                        TimestampUtc = audit.TimestampUtc
                    });
                }
            }

            procedureScore = ClampScore(procedureScore, ruleset.Scoring.MinScore, ruleset.Scoring.MaxScore);
            performanceScore = ClampScore(performanceScore, ruleset.Scoring.MinScore, ruleset.Scoring.MaxScore);
            patagoniaScore = ClampScore(
                (int)Math.Round(
                    ruleset.Scoring.PatagoniaBase
                    + ((procedureScore - ruleset.Scoring.ProcedureBase) * ruleset.Scoring.PatagoniaProcedureWeight)
                    + (performanceScore * ruleset.Scoring.PatagoniaPerformanceWeight)
                    + (patagoniaScore - ruleset.Scoring.PatagoniaBase),
                    MidpointRounding.AwayFromZero),
                ruleset.Scoring.MinScore,
                ruleset.Scoring.MaxScore);

            var triggeredRules = auditLog
                .Where(item => item.Triggered)
                .Select(MapTriggeredRule)
                .ToList();
            var bonuses = triggeredRules
                .Where(item => string.Equals(item.RuleType, "bonus", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var penalties = triggeredRules
                .Where(item => string.Equals(item.RuleType, "penalty", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var gateFailures = triggeredRules
                .Where(item => string.Equals(item.RuleType, "gate", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var report = new PatagoniaEvaluationReport
            {
                ContractVersion = ruleset.OutputContract.ContractVersion,
                RulesetVersion = ruleset.SchemaVersion,
                VisibleScoreName = ruleset.VisibleScoreName,
                RulesFilePath = _rulesProvider.ResolveRulesPath(explicitRulesPath),
                PatagoniaScore = patagoniaScore,
                ProcedureScore = procedureScore,
                PerformanceScore = performanceScore,
                PatagoniaGrade = ToGrade(patagoniaScore),
                ProcedureGrade = ToGrade(procedureScore),
                PerformanceGrade = ToGrade(performanceScore),
                FlightValid = invalidReasons.Count == 0,
                InvalidReasons = invalidReasons,
                Incidents = incidents.OrderBy(item => item.TimestampUtc).ToList(),
                RuleAuditLog = auditLog.OrderBy(item => item.TimestampUtc).ThenBy(item => item.RuleId).ToList(),
                TriggeredRules = triggeredRules,
                PhaseResults = phaseResults.Values.OrderBy(item => PhaseOrder(ruleset, item.Phase)).ToList(),
                Bonuses = bonuses,
                Penalties = penalties,
                GateFailures = gateFailures,
                TelemetrySummary = telemetrySummary,
                EventLog = BuildEventLog(input, auditLog),
                CertificationsChecks = certificationChecks,
                AircraftValidation = aircraftValidation,
                DispatchValidation = dispatchValidation,
                WeightsValidation = weightsValidation,
                WeatherValidation = weatherValidation
            };

            report.Summary = BuildSummary(report, scope);
            return report;
        }

        private static List<PatagoniaRuleDefinition> SelectRulesForScope(PatagoniaRulesetDefinition ruleset, string scope)
        {
            return ruleset.Rules
                .Where(rule =>
                {
                    var scopes = rule.Metadata == null ? null : rule.Metadata.Scopes;
                    if (scopes == null || scopes.Count == 0)
                    {
                        return string.Equals(scope, FinalEvaluationScope, StringComparison.OrdinalIgnoreCase);
                    }

                    return scopes.Any(item => string.Equals(item, scope, StringComparison.OrdinalIgnoreCase));
                })
                .ToList();
        }

        private static Dictionary<string, PatagoniaPhaseResult> BuildPhaseResults(IEnumerable<string> phases)
        {
            var map = new Dictionary<string, PatagoniaPhaseResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var phase in phases ?? Array.Empty<string>())
            {
                var code = NormalizePhase(phase);
                if (!map.ContainsKey(code))
                {
                    map[code] = new PatagoniaPhaseResult { Phase = code };
                }
            }
            return map;
        }

        private PatagoniaRuleAuditEntry EvaluateRule(
            PatagoniaRuleDefinition rule,
            PatagoniaEvaluationInput input,
            AircraftProfile profile,
            Dictionary<string, object> metrics,
            HashSet<string> providedCertifications,
            PatagoniaGlobalToleranceDefinition globalTolerances,
            string scope)
        {
            var metadata = rule.Metadata ?? new PatagoniaRuleMetadata();
            var evaluation = rule.Evaluation ?? new PatagoniaRuleEvaluationDefinition();
            var effect = rule.Effect ?? new PatagoniaRuleEffectDefinition();

            var audit = new PatagoniaRuleAuditEntry
            {
                RuleId = metadata.Id ?? string.Empty,
                Phase = NormalizePhase(metadata.Phase),
                TimestampUtc = ResolveRuleTimestamp(input, NormalizePhase(metadata.Phase)),
                EvaluationState = "evaluated",
                AppliedTolerance = DescribeTolerance(evaluation.Tolerances, globalTolerances),
                Source = ResolveDataSource(metadata.DataSource, evaluation.Condition),
                Name = metadata.Name ?? string.Empty,
                Description = metadata.Description ?? string.Empty,
                Category = metadata.Category ?? string.Empty,
                RuleType = NormalizeRuleType(effect.RuleType),
                Severity = metadata.Severity ?? string.Empty,
                ScoreTarget = NormalizeScoreTarget(effect.ScoreTarget),
                UiMessage = metadata.UiMessage ?? string.Empty,
                LogMessage = metadata.LogMessage ?? string.Empty,
                Result = PatagoniaAuditResults.NotApplicable,
                Context = DescribeConditionContext(evaluation.Condition)
            };

            SetObservedAndExpected(audit, evaluation.Condition, metrics);

            if (!metadata.Enabled)
            {
                audit.Reason = "Rule disabled in ruleset";
                return audit;
            }

            if (!IsFlightTypeApplicable(evaluation, input))
            {
                audit.Reason = "Flight type not applicable";
                return audit;
            }

            if (!DispatchDependencySatisfied(evaluation, metrics))
            {
                audit.Reason = "Dispatch dependency not satisfied";
                return audit;
            }

            if (!WeatherDependencySatisfied(evaluation, metrics))
            {
                audit.Reason = "Weather dependency not satisfied";
                return audit;
            }

            if (!HasRequiredTelemetry(evaluation, metrics))
            {
                var unsupportedReason = AircraftTelemetryProfileService.DescribeUnsupportedMetrics(profile, evaluation.RequiredTelemetry);
                audit.Reason = string.IsNullOrWhiteSpace(unsupportedReason)
                    ? "Required telemetry missing"
                    : unsupportedReason;
                return audit;
            }

            if (!IsAircraftApplicable(evaluation, input))
            {
                audit.Reason = "Aircraft not applicable";
                return audit;
            }

            if (!HasRequiredCertifications(evaluation, providedCertifications))
            {
                audit.Result = PatagoniaAuditResults.Fail;
                audit.ScoreDelta = ResolveScoreDelta(rule);
                audit.Reason = "Required certification missing";
                audit.Context = "certifications";
                audit.Triggered = ShouldTriggerVisibleEvent(audit.RuleType);
                audit.FlightInvalidated = effect.InvalidatesFlight;
                audit.IncidentCode = effect.IncidentCode ?? string.Empty;
                return audit;
            }

            var conditionMatched = EvaluateCondition(evaluation.Condition, metrics);
            var outcomeMode = NormalizeOutcomeMode(evaluation.OutcomeMode);
            ApplyOutcome(rule, audit, conditionMatched, outcomeMode, scope);
            return audit;
        }

        private static void ApplyOutcome(
            PatagoniaRuleDefinition rule,
            PatagoniaRuleAuditEntry audit,
            bool conditionMatched,
            string outcomeMode,
            string scope)
        {
            var effect = rule.Effect ?? new PatagoniaRuleEffectDefinition();
            var normalizedType = NormalizeRuleType(effect.RuleType);

            if (string.Equals(outcomeMode, "success_on_match", StringComparison.OrdinalIgnoreCase))
            {
                if (conditionMatched)
                {
                    audit.Result = "PASS";
                    audit.Reason = BuildPositiveReason(audit, normalizedType);

                    if (normalizedType == "bonus" || normalizedType == "info")
                    {
                        audit.ScoreDelta = ResolveScoreDelta(rule);
                        audit.Triggered = true;
                    }
                    else
                    {
                        audit.ScoreDelta = 0;
                        audit.Triggered = normalizedType == "info";
                    }
                }
                else
                {
                    if (normalizedType == "gate")
                    {
                        audit.Result = PatagoniaAuditResults.Fail;
                        audit.ScoreDelta = ResolveScoreDelta(rule);
                        audit.Reason = FirstNonEmpty(audit.UiMessage, audit.LogMessage, audit.Name, "Required gate not satisfied");
                        audit.Triggered = true;
                        audit.FlightInvalidated = effect.InvalidatesFlight;
                        audit.IncidentCode = effect.IncidentCode ?? string.Empty;
                    }
                    else if (normalizedType == "bonus")
                    {
                        audit.Result = PatagoniaAuditResults.NotApplicable;
                        audit.Reason = "Bonus condition not achieved";
                    }
                    else
                    {
                        audit.Result = PatagoniaAuditResults.Warn;
                        audit.Reason = "Expected procedure not satisfied";
                    }
                }

                return;
            }

            if (string.Equals(outcomeMode, "inform_on_match", StringComparison.OrdinalIgnoreCase))
            {
                if (conditionMatched)
                {
                    audit.Result = PatagoniaAuditResults.Pass;
                    audit.ScoreDelta = ResolveScoreDelta(rule);
                    audit.Reason = BuildPositiveReason(audit, normalizedType);
                    audit.Triggered = true;
                }
                else
                {
                    audit.Result = PatagoniaAuditResults.NotApplicable;
                    audit.Reason = "Info condition not observed";
                }
                return;
            }

            if (conditionMatched)
            {
                audit.Result = PatagoniaAuditResults.Fail;
                audit.ScoreDelta = ResolveScoreDelta(rule);
                audit.Reason = FirstNonEmpty(audit.UiMessage, audit.LogMessage, audit.Name, "Violation detected");
                audit.Triggered = ShouldTriggerVisibleEvent(normalizedType);
                audit.FlightInvalidated = effect.InvalidatesFlight;
                audit.IncidentCode = effect.IncidentCode ?? string.Empty;
                return;
            }

            audit.Result = PatagoniaAuditResults.Pass;
            audit.ScoreDelta = 0;
            audit.Reason = "Condition within tolerance";
        }

        private static string BuildPositiveReason(PatagoniaRuleAuditEntry audit, string ruleType)
        {
            if (!string.IsNullOrWhiteSpace(audit.UiMessage))
            {
                return audit.UiMessage;
            }

            if (!string.IsNullOrWhiteSpace(audit.LogMessage))
            {
                return audit.LogMessage;
            }

            if (ruleType == "bonus")
            {
                return "Bonus achieved";
            }

            if (ruleType == "info")
            {
                return "Informational rule observed";
            }

            return "Rule satisfied";
        }

        private static void IncrementAuditCounters(PatagoniaPhaseResult phaseResult, string result)
        {
            switch ((result ?? string.Empty).Trim().ToUpperInvariant())
            {
                case PatagoniaAuditResults.Pass:
                    phaseResult.AuditPassCount++;
                    break;
                case PatagoniaAuditResults.Warn:
                    phaseResult.AuditWarnCount++;
                    break;
                case PatagoniaAuditResults.NotApplicable:
                    phaseResult.AuditNaCount++;
                    break;
            }
        }

        private static PatagoniaTriggeredRuleResult MapTriggeredRule(PatagoniaRuleAuditEntry audit)
        {
            return new PatagoniaTriggeredRuleResult
            {
                RuleId = audit.RuleId,
                Name = audit.Name,
                Description = audit.Description,
                Phase = audit.Phase,
                Category = audit.Category,
                RuleType = audit.RuleType,
                Severity = audit.Severity,
                ScoreTarget = audit.ScoreTarget,
                ScoreDelta = audit.ScoreDelta,
                Message = FirstNonEmpty(audit.UiMessage, audit.LogMessage, audit.Reason, audit.Name),
                AuditResult = audit.Result
            };
        }

        private static PatagoniaCertificationCheckResult BuildCertificationCheck(PatagoniaRuleDefinition rule, HashSet<string> providedCertifications)
        {
            var required = (rule.Evaluation == null ? null : rule.Evaluation.RequiredCertifications) ?? new List<string>();
            required = required
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (required.Count == 0)
            {
                return null;
            }

            var provided = providedCertifications.ToList();
            var passed = required.All(item => providedCertifications.Contains(item));
            return new PatagoniaCertificationCheckResult
            {
                RuleId = rule.Metadata == null ? string.Empty : (rule.Metadata.Id ?? string.Empty),
                Required = required,
                Provided = provided,
                Passed = passed,
                Status = passed ? "passed" : "missing_required_certification"
            };
        }

        private static Dictionary<string, object> BuildMetrics(PatagoniaEvaluationInput input, AircraftProfile profile)
        {
            var flight = input.Flight ?? new Flight();
            var dispatch = input.Dispatch ?? new PreparedDispatch();
            var report = input.Report ?? new FlightReport();
            var telemetry = (input.TelemetryLog ?? Array.Empty<SimData>()).Where(sample => sample != null).ToList();
            var first = telemetry.Count > 0 ? telemetry[0] : (input.CurrentTelemetry ?? new SimData());
            var last = telemetry.Count > 0 ? telemetry[telemetry.Count - 1] : (input.CurrentTelemetry ?? new SimData());
            var airborne = telemetry.Where(sample => !sample.OnGround).ToList();
            var onGround = telemetry.Where(sample => sample.OnGround).ToList();

            var metrics = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["telemetry.samples.count"] = telemetry.Count,
                ["telemetry.airborne_samples.count"] = airborne.Count,
                ["telemetry.on_ground_samples.count"] = onGround.Count,
                ["telemetry.max_ias_kts"] = telemetry.Count == 0 ? 0d : telemetry.Max(sample => sample.IndicatedAirspeed),
                ["telemetry.max_gs_kts"] = telemetry.Count == 0 ? 0d : telemetry.Max(sample => sample.GroundSpeed),
                ["telemetry.max_altitude_ft"] = telemetry.Count == 0 ? 0d : telemetry.Max(sample => sample.AltitudeFeet),
                ["telemetry.max_bank_deg"] = telemetry.Count == 0 ? 0d : telemetry.Max(sample => Math.Abs(sample.Bank)),
                ["telemetry.max_pitch_deg"] = telemetry.Count == 0 ? 0d : telemetry.Max(sample => Math.Abs(sample.Pitch)),
                ["telemetry.max_vs_fpm"] = telemetry.Count == 0 ? 0d : telemetry.Max(sample => sample.VerticalSpeed),
                ["telemetry.min_vs_fpm"] = telemetry.Count == 0 ? 0d : telemetry.Min(sample => sample.VerticalSpeed),
                ["telemetry.last.latitude"] = last == null ? 0d : last.Latitude,
                ["telemetry.last.longitude"] = last == null ? 0d : last.Longitude,
                ["telemetry.last.altitude_ft"] = last == null ? 0d : last.AltitudeFeet,
                ["telemetry.last.altitude_agl_ft"] = last == null ? 0d : last.AltitudeAGL,
                ["telemetry.last.ias_kts"] = last == null ? 0d : last.IndicatedAirspeed,
                ["telemetry.last.gs_kts"] = last == null ? 0d : last.GroundSpeed,
                ["telemetry.last.vs_fpm"] = last == null ? 0d : last.VerticalSpeed,
                ["telemetry.last.on_ground"] = last != null && last.OnGround,
                ["telemetry.last.pause"] = last != null && last.Pause,
                ["telemetry.last.reverser_active"] = last != null && last.ReverserActive,
                ["telemetry.last.engine1_n1"] = last == null ? 0d : last.Engine1N1,
                ["telemetry.last.engine2_n1"] = last == null ? 0d : last.Engine2N1,
                ["telemetry.last.engine3_n1"] = last == null ? 0d : last.Engine3N1,
                ["telemetry.last.engine4_n1"] = last == null ? 0d : last.Engine4N1,
                ["telemetry.last.autopilot_on"] = last != null && last.AutopilotActive,
                ["telemetry.last.raining"] = last != null && last.IsRaining,
                ["telemetry.last.wind_speed"] = last == null ? 0d : last.WindSpeed,
                ["telemetry.last.wind_direction"] = last == null ? 0d : last.WindDirection,
                ["telemetry.last.profile_code"] = last == null ? string.Empty : (last.DetectedProfileCode ?? string.Empty),
                ["telemetry.first.raining"] = first != null && first.IsRaining,
                ["telemetry.first.wind_speed"] = first == null ? 0d : first.WindSpeed,
                ["telemetry.first.wind_direction"] = first == null ? 0d : first.WindDirection,
                ["telemetry.touchdown.detected"] = Math.Abs(report.LandingVS) > 0.01d || Math.Abs(report.LandingG) > 0.01d,
                ["telemetry.touchdown_vs_fpm"] = report.LandingVS,
                ["telemetry.touchdown_g"] = report.LandingG,
                ["dispatch.present"] = !string.IsNullOrWhiteSpace(dispatch.ReservationId),
                ["dispatch.flight_number_set"] = !string.IsNullOrWhiteSpace(report.FlightNumber),
                ["dispatch.origin_set"] = !string.IsNullOrWhiteSpace(report.DepartureIcao),
                ["dispatch.destination_set"] = !string.IsNullOrWhiteSpace(report.ArrivalIcao),
                ["dispatch.route_set"] = !string.IsNullOrWhiteSpace(flight.Route),
                ["dispatch.current_airport_matches_departure"] =
                    string.IsNullOrWhiteSpace(dispatch.CurrentAirportCode)
                    || string.Equals((dispatch.CurrentAirportCode ?? string.Empty).Trim(), (dispatch.DepartureIcao ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase),
                ["dispatch.flight_matches"] = ValidateCommercialFlightNumber(dispatch, report.FlightNumber),
                ["dispatch.origin_matches"] = MatchToken(dispatch.DepartureIcao, report.DepartureIcao),
                ["dispatch.destination_matches"] = MatchToken(dispatch.ArrivalIcao, report.ArrivalIcao),
                ["dispatch.aircraft_matches"] = MatchToken(NormalizeAircraftCode(dispatch.AircraftIcao), NormalizeAircraftCode(report.AircraftIcao)),
                ["flight.present"] = !string.IsNullOrWhiteSpace(flight.FlightNumber) || !string.IsNullOrWhiteSpace(report.FlightNumber),
                ["flight.duration_minutes"] = Math.Max(0d, report.Duration.TotalMinutes),
                ["flight.distance_nm"] = report.Distance,
                ["flight.fuel_used_kg"] = report.FuelUsed * 0.45359237d,
                ["flight.departure_time_utc"] = report.DepartureTime == default(DateTime) ? string.Empty : report.DepartureTime.ToString("o", CultureInfo.InvariantCulture),
                ["flight.arrival_time_utc"] = report.ArrivalTime == default(DateTime) ? string.Empty : report.ArrivalTime.ToString("o", CultureInfo.InvariantCulture),
                ["flight.mode"] = (flight.FlightModeCode ?? dispatch.FlightMode ?? string.Empty).Trim(),
                ["aircraft.icao"] = NormalizeAircraftCode(report.AircraftIcao),
                ["aircraft.type_code"] = NormalizeAircraftCode(flight.AircraftTypeCode),
                ["aircraft.display_name"] = FirstNonEmpty(flight.AircraftDisplayName, flight.AircraftName, dispatch.AircraftDisplayName),
                ["aircraft.profile_code"] = last == null ? string.Empty : (last.DetectedProfileCode ?? string.Empty),
                ["preflight.cold_and_dark"] = AircraftTelemetryProfileService.IsColdAndDark(last, profile),
                ["preflight.engines_off"] = AircraftTelemetryProfileService.EnginesStopped(last, profile)
            };

            SetMetricIfSupported(metrics, profile, "telemetry.last.nav_on", last != null && last.NavLightsOn);
            SetMetricIfSupported(metrics, profile, "telemetry.last.beacon_on", last != null && last.BeaconLightsOn);
            SetMetricIfSupported(metrics, profile, "telemetry.last.strobe_on", last != null && last.StrobeLightsOn);
            SetMetricIfSupported(metrics, profile, "telemetry.last.landing_on", last != null && last.LandingLightsOn);
            SetMetricIfSupported(metrics, profile, "telemetry.last.taxi_on", last != null && last.TaxiLightsOn);
            SetMetricIfSupported(metrics, profile, "telemetry.last.parking_brake_on", last != null && last.ParkingBrake);
            SetMetricIfSupported(metrics, profile, "preflight.parking_brake_on", last != null && last.ParkingBrake);
            SetMetricIfSupported(metrics, profile, "telemetry.last.apu_on", last != null && last.ApuRunning);
            SetMetricIfSupported(metrics, profile, "telemetry.last.bleed_on", last != null && last.BleedAirOn);
            SetMetricIfSupported(metrics, profile, "telemetry.last.seatbelt_on", last != null && last.SeatBeltSign);
            SetMetricIfSupported(metrics, profile, "telemetry.last.no_smoking_on", last != null && last.NoSmokingSign);
            SetMetricIfSupported(metrics, profile, "telemetry.last.flaps_percent", last == null ? 0d : last.FlapsPercent);
            SetMetricIfSupported(metrics, profile, "telemetry.last.flaps_deployed", last != null && (last.FlapsDeployed || last.FlapsPercent > 0d));
            SetMetricIfSupported(metrics, profile, "telemetry.last.gear_down", last != null && last.GearDown);
            SetMetricIfSupported(metrics, profile, "telemetry.last.transponder_charlie", last != null && last.TransponderCharlieMode);
            SetMetricIfSupported(metrics, profile, "telemetry.last.transponder_code", last == null ? 0 : last.TransponderCode);
            SetMetricIfSupported(metrics, profile, "telemetry.last.battery_on", last != null && last.BatteryMasterOn);
            SetMetricIfSupported(metrics, profile, "telemetry.last.avionics_on", last != null && last.AvionicsMasterOn);
            SetMetricIfSupported(metrics, profile, "telemetry.last.door_open", last != null && last.DoorOpen);
            SetMetricIfSupported(metrics, profile, "telemetry.last.fuel_kg", last == null ? 0d : last.FuelKg);
            SetMetricIfSupported(metrics, profile, "telemetry.first.fuel_kg", first == null ? 0d : first.FuelKg);
            SetMetricIfSupported(metrics, profile, "telemetry.last.zero_fuel_weight_kg", last == null ? 0d : last.ZeroFuelWeightKg);
            SetMetricIfSupported(metrics, profile, "telemetry.last.payload_kg", last == null ? 0d : last.PayloadKg);
            SetMetricIfSupported(metrics, profile, "telemetry.last.total_weight_kg", last == null ? 0d : last.TotalWeightKg);
            SetMetricIfSupported(metrics, profile, "telemetry.last.empty_weight_kg", last == null ? 0d : last.EmptyWeightKg);
            SetMetricIfSupported(metrics, profile, "telemetry.last.qnh_hpa", last == null ? 0d : last.QNH);
            SetMetricIfSupported(metrics, profile, "telemetry.last.inertial_separator_on", last != null && last.InertialSeparatorOn);

            return metrics;
        }

        private static PatagoniaTelemetrySummary BuildTelemetrySummary(PatagoniaEvaluationInput input)
        {
            var telemetry = input.TelemetryLog ?? Array.Empty<SimData>();
            var first = telemetry.Count > 0 ? telemetry[0] : input.CurrentTelemetry;
            var last = telemetry.Count > 0 ? telemetry[telemetry.Count - 1] : input.CurrentTelemetry;

            return new PatagoniaTelemetrySummary
            {
                SamplesCount = telemetry.Count,
                AirborneSamplesCount = telemetry.Count(sample => !sample.OnGround),
                OnGroundSamplesCount = telemetry.Count(sample => sample.OnGround),
                DistanceNm = input.Report == null ? 0d : input.Report.Distance,
                FuelUsedKg = input.Report == null ? 0d : input.Report.FuelUsed * 0.45359237d,
                MaxAltitudeFt = telemetry.Count == 0 ? input.Report.MaxAltitudeFeet : telemetry.Max(sample => sample.AltitudeFeet),
                MaxSpeedKts = telemetry.Count == 0 ? input.Report.MaxSpeedKts : telemetry.Max(sample => Math.Max(sample.IndicatedAirspeed, sample.GroundSpeed)),
                MaxBankDeg = telemetry.Count == 0 ? 0d : telemetry.Max(sample => Math.Abs(sample.Bank)),
                MaxPitchDeg = telemetry.Count == 0 ? 0d : telemetry.Max(sample => Math.Abs(sample.Pitch)),
                LandingVsFpm = input.Report == null ? 0d : input.Report.LandingVS,
                LandingG = input.Report == null ? 0d : input.Report.LandingG,
                FirstSampleUtc = first == null ? default(DateTime) : first.CapturedAtUtc,
                LastSampleUtc = last == null ? default(DateTime) : last.CapturedAtUtc,
                LastLatitude = last == null ? 0d : last.Latitude,
                LastLongitude = last == null ? 0d : last.Longitude,
                AircraftProfileCode = last == null ? string.Empty : (last.DetectedProfileCode ?? string.Empty)
            };
        }

        private static void SetMetricIfSupported(Dictionary<string, object> metrics, AircraftProfile profile, string metricKey, object value)
        {
            if (AircraftTelemetryProfileService.SupportsMetric(profile, metricKey))
            {
                metrics[metricKey] = value;
            }
        }

        private static PatagoniaAircraftValidationResult BuildAircraftValidation(PatagoniaEvaluationInput input, IEnumerable<PatagoniaRuleDefinition> rules, AircraftProfile profile)
        {
            var result = AircraftTelemetryProfileService.BuildValidation(profile);
            result.AircraftIcao = NormalizeAircraftCode(input.Report.AircraftIcao);
            result.AircraftTypeCode = NormalizeAircraftCode(input.Flight.AircraftTypeCode);
            result.AircraftDisplayName = FirstNonEmpty(input.Flight.AircraftDisplayName, input.Flight.AircraftName, input.Dispatch.AircraftDisplayName);
            result.DetectedProfileCode = input.CurrentTelemetry == null ? result.DetectedProfileCode : (input.CurrentTelemetry.DetectedProfileCode ?? result.DetectedProfileCode);
            result.AircraftMatchesDispatch = MatchToken(NormalizeAircraftCode(input.Dispatch.AircraftIcao), NormalizeAircraftCode(input.Report.AircraftIcao));

            foreach (var rule in rules)
            {
                if (IsAircraftApplicable(rule.Evaluation ?? new PatagoniaRuleEvaluationDefinition(), input))
                {
                    result.MatchedRules.Add(rule.Metadata == null ? string.Empty : (rule.Metadata.Id ?? string.Empty));
                }
            }

            if (!result.AircraftMatchesDispatch && !string.IsNullOrWhiteSpace(input.Dispatch.AircraftIcao))
            {
                result.Issues.Add("Aircraft mismatch against dispatch");
            }

            result.HasApplicableRules = result.MatchedRules.Count > 0;
            return result;
        }

        private static PatagoniaDispatchValidationResult BuildDispatchValidation(PatagoniaEvaluationInput input)
        {
            var result = new PatagoniaDispatchValidationResult
            {
                DispatchPresent = !string.IsNullOrWhiteSpace(input.Dispatch.ReservationId),
                FlightNumberPresent = !string.IsNullOrWhiteSpace(input.Report.FlightNumber),
                OriginPresent = !string.IsNullOrWhiteSpace(input.Report.DepartureIcao),
                DestinationPresent = !string.IsNullOrWhiteSpace(input.Report.ArrivalIcao),
                RoutePresent = !string.IsNullOrWhiteSpace(input.Flight.Route)
            };

            if (!result.DispatchPresent) result.Issues.Add("Dispatch missing");
            if (!result.FlightNumberPresent) result.Issues.Add("Flight number missing");
            if (!result.OriginPresent) result.Issues.Add("Origin missing");
            if (!result.DestinationPresent) result.Issues.Add("Destination missing");
            if (!result.RoutePresent) result.Issues.Add("Route missing");
            if (!ValidateCommercialFlightNumber(input.Dispatch, input.Report.FlightNumber)) result.Issues.Add("Flight number mismatch");
            if (!MatchToken(input.Dispatch.DepartureIcao, input.Report.DepartureIcao)) result.Issues.Add("Origin mismatch");
            if (!MatchToken(input.Dispatch.ArrivalIcao, input.Report.ArrivalIcao)) result.Issues.Add("Destination mismatch");

            result.Passed = result.Issues.Count == 0;
            return result;
        }

        private static PatagoniaWeightsValidationResult BuildWeightsValidation(PatagoniaEvaluationInput input, AircraftProfile profile)
        {
            var first = input.TelemetryLog != null && input.TelemetryLog.Count > 0 ? input.TelemetryLog[0] : input.CurrentTelemetry;
            var last = input.TelemetryLog != null && input.TelemetryLog.Count > 0 ? input.TelemetryLog[input.TelemetryLog.Count - 1] : input.CurrentTelemetry;
            var result = new PatagoniaWeightsValidationResult
            {
                Evaluated = input.Dispatch != null || input.Flight != null || last != null,
                PlannedFuelKg = input.Dispatch == null ? input.Flight.BlockFuel : input.Dispatch.FuelPlannedKg,
                ActualFuelStartKg = first == null ? 0d : first.FuelKg,
                ActualFuelEndKg = last == null ? 0d : last.FuelKg,
                PlannedPayloadKg = input.Dispatch == null ? 0d : input.Dispatch.PayloadKg,
                PlannedZeroFuelWeightKg = input.Dispatch == null ? input.Flight.ZeroFuelWeight : input.Dispatch.ZeroFuelWeightKg,
                ActualZeroFuelWeightKg = last == null ? 0d : last.ZeroFuelWeightKg
            };

            if (result.PlannedFuelKg > 0 && result.ActualFuelStartKg > 0)
            {
                var diff = PercentageDifference(result.PlannedFuelKg, result.ActualFuelStartKg);
                if (diff > 10d)
                {
                    result.Issues.Add("Fuel start out of tolerance (>10%)");
                }
            }

            if (result.PlannedZeroFuelWeightKg > 0 && result.ActualZeroFuelWeightKg > 0)
            {
                var diff = PercentageDifference(result.PlannedZeroFuelWeightKg, result.ActualZeroFuelWeightKg);
                if (diff > 10d)
                {
                    result.Issues.Add("Zero fuel weight out of tolerance (>10%)");
                }
            }
            else if (result.PlannedZeroFuelWeightKg > 0 && result.ActualZeroFuelWeightKg <= 0 && profile.SupportsZfwReadback)
            {
                result.Issues.Add("Zero fuel weight telemetry unavailable");
            }

            result.Passed = result.Issues.Count == 0;
            return result;
        }

        private static PatagoniaWeatherValidationResult BuildWeatherValidation(PatagoniaEvaluationInput input)
        {
            var first = input.TelemetryLog != null && input.TelemetryLog.Count > 0 ? input.TelemetryLog[0] : input.CurrentTelemetry;
            var last = input.TelemetryLog != null && input.TelemetryLog.Count > 0 ? input.TelemetryLog[input.TelemetryLog.Count - 1] : input.CurrentTelemetry;

            return new PatagoniaWeatherValidationResult
            {
                Evaluated = first != null || last != null,
                Passed = true,
                DepartureRaining = first != null && first.IsRaining,
                ArrivalRaining = last != null && last.IsRaining,
                DepartureWindSpeed = first == null ? 0d : first.WindSpeed,
                ArrivalWindSpeed = last == null ? 0d : last.WindSpeed,
                DepartureWindDirection = first == null ? 0d : first.WindDirection,
                ArrivalWindDirection = last == null ? 0d : last.WindDirection
            };
        }

        private static List<PatagoniaEventLogEntry> BuildEventLog(PatagoniaEvaluationInput input, IReadOnlyList<PatagoniaRuleAuditEntry> auditLog)
        {
            var result = new List<PatagoniaEventLogEntry>();

            if (input.Report != null)
            {
                if (input.Report.BlockOutTimeUtc != default(DateTime))
                {
                    result.Add(new PatagoniaEventLogEntry
                    {
                        TimestampUtc = input.Report.BlockOutTimeUtc,
                        Phase = "PRE",
                        Source = "flight",
                        Severity = "info",
                        Message = "Block out registrado"
                    });
                }

                if (input.Report.TakeoffTimeUtc != default(DateTime))
                {
                    result.Add(new PatagoniaEventLogEntry
                    {
                        TimestampUtc = input.Report.TakeoffTimeUtc,
                        Phase = "TO",
                        Source = "flight",
                        Severity = "info",
                        Message = "Takeoff registrado"
                    });
                }

                if (input.Report.TouchdownTimeUtc != default(DateTime))
                {
                    result.Add(new PatagoniaEventLogEntry
                    {
                        TimestampUtc = input.Report.TouchdownTimeUtc,
                        Phase = "LDG",
                        Source = "flight",
                        Severity = Math.Abs(input.Report.LandingVS) > 500 ? "warning" : "info",
                        Message = "Touchdown registrado"
                    });
                }

                if (input.Report.ArrivalTime != default(DateTime))
                {
                    result.Add(new PatagoniaEventLogEntry
                    {
                        TimestampUtc = input.Report.ArrivalTime,
                        Phase = "PAR",
                        Source = "flight",
                        Severity = "info",
                        Message = "Cierre de vuelo solicitado"
                    });
                }
            }

            foreach (var audit in auditLog)
            {
                result.Add(new PatagoniaEventLogEntry
                {
                    TimestampUtc = audit.TimestampUtc,
                    Phase = audit.Phase,
                    Source = audit.Source,
                    Severity = string.IsNullOrWhiteSpace(audit.Severity) ? audit.Result.ToLowerInvariant() : audit.Severity,
                    Message = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} [{1}] {2} | observado={3} | esperado={4} | tolerancia={5}",
                        audit.RuleId,
                        audit.Result,
                        FirstNonEmpty(audit.Reason, audit.Name, "rule evaluated"),
                        audit.ObservedValue,
                        audit.ExpectedValue,
                        audit.AppliedTolerance)
                });
            }

            return result.OrderBy(item => item.TimestampUtc).ToList();
        }

        private static bool IsFlightTypeApplicable(PatagoniaRuleEvaluationDefinition evaluation, PatagoniaEvaluationInput input)
        {
            var flightTypes = evaluation == null ? null : evaluation.FlightTypes;
            if (flightTypes == null || flightTypes.Count == 0)
            {
                return true;
            }

            var flightMode = FirstNonEmpty(input.Flight.FlightModeCode, input.Dispatch.FlightMode);
            return flightTypes.Any(item => MatchToken(item, flightMode));
        }

        private static bool DispatchDependencySatisfied(PatagoniaRuleEvaluationDefinition evaluation, Dictionary<string, object> metrics)
        {
            if (evaluation == null || evaluation.DispatchDependency == null || !evaluation.DispatchDependency.Required)
            {
                return true;
            }

            return ToBoolean(GetMetric(metrics, "dispatch.present"));
        }

        private static bool WeatherDependencySatisfied(PatagoniaRuleEvaluationDefinition evaluation, Dictionary<string, object> metrics)
        {
            if (evaluation == null || evaluation.WeatherDependency == null || !evaluation.WeatherDependency.Required)
            {
                return true;
            }

            var mode = (evaluation.WeatherDependency.Mode ?? string.Empty).Trim().ToLowerInvariant();
            if (mode == "wet")
            {
                return ToBoolean(GetMetric(metrics, "telemetry.first.raining")) || ToBoolean(GetMetric(metrics, "telemetry.last.raining"));
            }

            return true;
        }

        private static bool HasRequiredTelemetry(PatagoniaRuleEvaluationDefinition evaluation, Dictionary<string, object> metrics)
        {
            var requiredTelemetry = evaluation == null ? null : evaluation.RequiredTelemetry;
            if (requiredTelemetry == null || requiredTelemetry.Count == 0)
            {
                return true;
            }

            return requiredTelemetry
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .All(item => metrics.ContainsKey(item.Trim()));
        }

        private static bool IsAircraftApplicable(PatagoniaRuleEvaluationDefinition evaluation, PatagoniaEvaluationInput input)
        {
            var tokens = evaluation == null ? null : evaluation.ApplicableAircraft;
            if (tokens == null || tokens.Count == 0)
            {
                return true;
            }

            var values = new[]
            {
                NormalizeAircraftCode(input.Report.AircraftIcao),
                NormalizeAircraftCode(input.Flight.AircraftTypeCode),
                NormalizeAircraftCode(input.Dispatch.AircraftIcao),
                FirstNonEmpty(input.Flight.AircraftDisplayName, input.Flight.AircraftName, input.Dispatch.AircraftDisplayName),
                input.CurrentTelemetry == null ? string.Empty : (input.CurrentTelemetry.DetectedProfileCode ?? string.Empty)
            };

            return tokens.Any(token => values.Any(value => ContainsToken(value, token)));
        }

        private static bool HasRequiredCertifications(PatagoniaRuleEvaluationDefinition evaluation, HashSet<string> providedCertifications)
        {
            var required = evaluation == null ? null : evaluation.RequiredCertifications;
            if (required == null || required.Count == 0)
            {
                return true;
            }

            return required
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .All(item => providedCertifications.Contains(item.Trim()));
        }

        private static bool EvaluateCondition(PatagoniaRuleConditionDefinition condition, Dictionary<string, object> metrics)
        {
            if (condition == null)
            {
                return false;
            }

            if (condition.All != null && condition.All.Count > 0)
            {
                return condition.All.All(item => EvaluateCondition(item, metrics));
            }

            if (condition.Any != null && condition.Any.Count > 0)
            {
                return condition.Any.Any(item => EvaluateCondition(item, metrics));
            }

            if (string.IsNullOrWhiteSpace(condition.Metric))
            {
                return false;
            }

            var left = GetMetric(metrics, condition.Metric.Trim());
            var op = (condition.Operator ?? string.Empty).Trim().ToLowerInvariant();
            var expected = !IsEmpty(condition.ExpectedValue) ? condition.ExpectedValue : condition.Value;

            switch (op)
            {
                case "exists":
                    return !IsEmpty(left);
                case "not_empty":
                    return !string.IsNullOrWhiteSpace(Convert.ToString(left, CultureInfo.InvariantCulture));
                case "is_true":
                    return ToBoolean(left);
                case "is_false":
                    return !ToBoolean(left);
                case "eq":
                    return CompareEquals(left, expected);
                case "neq":
                    return !CompareEquals(left, expected);
                case "gt":
                    return ToDouble(left) > ToDouble(expected);
                case "gte":
                    return ToDouble(left) >= ToDouble(expected);
                case "lt":
                    return ToDouble(left) < ToDouble(expected);
                case "lte":
                    return ToDouble(left) <= ToDouble(expected);
                case "between":
                    return ToDouble(left) >= ToDouble(condition.Min) && ToDouble(left) <= ToDouble(condition.Max);
                case "contains":
                    return Convert.ToString(left, CultureInfo.InvariantCulture).IndexOf(
                        Convert.ToString(expected, CultureInfo.InvariantCulture),
                        StringComparison.OrdinalIgnoreCase) >= 0;
                case "one_of":
                    return (condition.Values ?? new List<object>()).Any(item => CompareEquals(left, item));
                case "starts_with":
                    return Convert.ToString(left, CultureInfo.InvariantCulture).StartsWith(
                        Convert.ToString(expected, CultureInfo.InvariantCulture),
                        StringComparison.OrdinalIgnoreCase);
                case "ends_with":
                    return Convert.ToString(left, CultureInfo.InvariantCulture).EndsWith(
                        Convert.ToString(expected, CultureInfo.InvariantCulture),
                        StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

        private static void SetObservedAndExpected(PatagoniaRuleAuditEntry audit, PatagoniaRuleConditionDefinition condition, Dictionary<string, object> metrics)
        {
            if (condition == null || string.IsNullOrWhiteSpace(condition.Metric))
            {
                audit.ObservedValue = string.Empty;
                audit.ExpectedValue = string.Empty;
                return;
            }

            audit.ObservedValue = DescribeValue(GetMetric(metrics, condition.Metric.Trim()));
            if (!IsEmpty(condition.ExpectedValue))
            {
                audit.ExpectedValue = DescribeValue(condition.ExpectedValue);
            }
            else if (!IsEmpty(condition.Value))
            {
                audit.ExpectedValue = DescribeValue(condition.Value);
            }
            else if (!IsEmpty(condition.Min) || !IsEmpty(condition.Max))
            {
                audit.ExpectedValue = DescribeValue(condition.Min) + " - " + DescribeValue(condition.Max);
            }
            else if (condition.Values != null && condition.Values.Count > 0)
            {
                audit.ExpectedValue = string.Join(", ", condition.Values.Select(DescribeValue));
            }

            audit.Context = condition.Metric ?? string.Empty;
        }

        private static string DescribeConditionContext(PatagoniaRuleConditionDefinition condition)
        {
            if (condition == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(condition.Metric))
            {
                return condition.Metric.Trim();
            }

            if (condition.All != null && condition.All.Count > 0)
            {
                return "all(" + string.Join(",", condition.All
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Metric))
                    .Select(item => item.Metric.Trim())) + ")";
            }

            if (condition.Any != null && condition.Any.Count > 0)
            {
                return "any(" + string.Join(",", condition.Any
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Metric))
                    .Select(item => item.Metric.Trim())) + ")";
            }

            return string.Empty;
        }

        private static string DescribeTolerance(PatagoniaRuleToleranceDefinition tolerances, PatagoniaGlobalToleranceDefinition globalTolerances)
        {
            if (tolerances == null)
            {
                return "none";
            }

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(tolerances.TimeProfile))
            {
                parts.Add("time_profile=" + tolerances.TimeProfile);
            }

            foreach (var pair in tolerances.Values ?? new Dictionary<string, object>())
            {
                parts.Add(pair.Key + "=" + DescribeValue(pair.Value));
            }

            if (parts.Count == 0 && globalTolerances != null)
            {
                parts.Add("short=" + globalTolerances.ShortTimeSeconds + "s");
                parts.Add("medium=" + globalTolerances.MediumTimeSeconds + "s");
            }

            return string.Join(" | ", parts);
        }

        private static DateTime ResolveRuleTimestamp(PatagoniaEvaluationInput input, string phase)
        {
            var report = input.Report ?? new FlightReport();
            switch ((phase ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "PRE":
                case "IGN":
                case "TAX":
                    if (report.BlockOutTimeUtc != default(DateTime)) return report.BlockOutTimeUtc;
                    break;
                case "TO":
                case "ASC":
                    if (report.TakeoffTimeUtc != default(DateTime)) return report.TakeoffTimeUtc;
                    break;
                case "DES":
                case "LDG":
                case "TAG":
                    if (report.TouchdownTimeUtc != default(DateTime)) return report.TouchdownTimeUtc;
                    break;
                case "PAR":
                    if (report.ArrivalTime != default(DateTime)) return report.ArrivalTime;
                    break;
            }

            if (input.CurrentTelemetry != null && input.CurrentTelemetry.CapturedAtUtc != default(DateTime))
            {
                return input.CurrentTelemetry.CapturedAtUtc;
            }

            if (input.TelemetryLog != null && input.TelemetryLog.Count > 0)
            {
                var first = input.TelemetryLog[0];
                if (first != null && first.CapturedAtUtc != default(DateTime))
                {
                    return first.CapturedAtUtc;
                }
            }

            return DateTime.UtcNow;
        }

        private static bool ShouldTriggerVisibleEvent(string ruleType)
        {
            switch ((ruleType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "gate":
                case "penalty":
                case "bonus":
                case "info":
                    return true;
                default:
                    return false;
            }
        }

        private static int ResolveScoreDelta(PatagoniaRuleDefinition rule)
        {
            var effect = rule.Effect ?? new PatagoniaRuleEffectDefinition();
            var delta = effect.ScoreDelta ?? new PatagoniaScoreDeltaDefinition();
            if (delta.Value != 0)
            {
                return delta.Value;
            }

            if (delta.Procedure != 0) return delta.Procedure;
            if (delta.Performance != 0) return delta.Performance;
            if (delta.Patagonia != 0) return delta.Patagonia;
            return 0;
        }

        private static void ApplyScoreDelta(
            PatagoniaRuleDefinition rule,
            int scoreDelta,
            ref int procedureScore,
            ref int performanceScore,
            ref int patagoniaScore)
        {
            var effect = rule.Effect ?? new PatagoniaRuleEffectDefinition();
            var target = NormalizeScoreTarget(effect.ScoreTarget);
            var delta = effect.ScoreDelta ?? new PatagoniaScoreDeltaDefinition();

            if (delta.Procedure != 0 || delta.Performance != 0 || delta.Patagonia != 0)
            {
                procedureScore += delta.Procedure;
                performanceScore += delta.Performance;
                patagoniaScore += delta.Patagonia;
                return;
            }

            switch (target)
            {
                case "performance":
                    performanceScore += scoreDelta;
                    break;
                case "patagonia":
                    patagoniaScore += scoreDelta;
                    break;
                case "all":
                    procedureScore += scoreDelta;
                    performanceScore += scoreDelta;
                    patagoniaScore += scoreDelta;
                    break;
                default:
                    procedureScore += scoreDelta;
                    break;
            }
        }

        private static string BuildSummary(PatagoniaEvaluationReport report, string scope)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} | proc {2} | perf {3} | valid={4} | audit={5} | triggers={6} | scope={7}",
                report.VisibleScoreName,
                report.PatagoniaScore,
                report.ProcedureScore,
                report.PerformanceScore,
                report.FlightValid ? "yes" : "no",
                report.RuleAuditLog == null ? 0 : report.RuleAuditLog.Count,
                report.TriggeredRules == null ? 0 : report.TriggeredRules.Count,
                scope);
        }

        private static string ToGrade(int score)
        {
            if (score >= 95) return "Excelente";
            if (score >= 80) return "Satisfactorio";
            if (score >= 60) return "Marginal";
            return "Insatisfactorio";
        }

        private static int PhaseOrder(PatagoniaRulesetDefinition ruleset, string phase)
        {
            var index = (ruleset.FlightPhases ?? new List<string>())
                .FindIndex(item => string.Equals(item, phase, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? int.MaxValue : index;
        }

        private static int ClampScore(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static string NormalizePhase(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "GEN" : value.Trim().ToUpperInvariant();
        }

        private static string NormalizeRuleType(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "info" : value.Trim().ToLowerInvariant();
        }

        private static string NormalizeScoreTarget(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "procedure" : value.Trim().ToLowerInvariant();
        }

        private static string NormalizeOutcomeMode(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "violation_on_match" : value.Trim().ToLowerInvariant();
        }

        private static HashSet<string> ParseTokens(params string[] rawValues)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in rawValues.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                foreach (var chunk in raw
                    .Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0))
                {
                    set.Add(chunk);
                }
            }

            return set;
        }

        private static string ResolveDataSource(string dataSource, PatagoniaRuleConditionDefinition condition)
        {
            if (!string.IsNullOrWhiteSpace(dataSource))
            {
                return dataSource.Trim();
            }

            var metric = condition == null ? string.Empty : (condition.Metric ?? string.Empty);
            if (metric.StartsWith("dispatch.", StringComparison.OrdinalIgnoreCase)) return "dispatch";
            if (metric.StartsWith("aircraft.", StringComparison.OrdinalIgnoreCase)) return "aircraft_profile";
            if (metric.StartsWith("weather.", StringComparison.OrdinalIgnoreCase)) return "weather";
            if (metric.StartsWith("certification.", StringComparison.OrdinalIgnoreCase)) return "certification";
            return "telemetry";
        }

        private static object GetMetric(Dictionary<string, object> metrics, string key)
        {
            object value;
            return metrics.TryGetValue(key, out value) ? value : null;
        }

        private static bool CompareEquals(object left, object right)
        {
            if (left == null && right == null) return true;
            if (left == null || right == null) return false;

            double leftNumber;
            double rightNumber;
            if (double.TryParse(Convert.ToString(left, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out leftNumber) &&
                double.TryParse(Convert.ToString(right, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out rightNumber))
            {
                return Math.Abs(leftNumber - rightNumber) < 0.0001d;
            }

            return string.Equals(
                Convert.ToString(left, CultureInfo.InvariantCulture)?.Trim(),
                Convert.ToString(right, CultureInfo.InvariantCulture)?.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool ToBoolean(object value)
        {
            if (value == null) return false;

            bool parsed;
            if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out parsed))
            {
                return parsed;
            }

            return Math.Abs(ToDouble(value)) > 0.0001d;
        }

        private static double ToDouble(object value)
        {
            if (value == null) return 0d;

            double parsed;
            if (double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out parsed))
            {
                return parsed;
            }

            return 0d;
        }

        private static string DescribeValue(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is bool)
            {
                return ((bool)value) ? "true" : "false";
            }

            if (value is DateTime)
            {
                return ((DateTime)value).ToString("o", CultureInfo.InvariantCulture);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static bool IsEmpty(object value)
        {
            return value == null || string.IsNullOrWhiteSpace(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static bool ContainsToken(string source, string token)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            return source.IndexOf(token.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool MatchToken(string expected, string actual)
        {
            var left = (expected ?? string.Empty).Trim().ToUpperInvariant();
            var right = (actual ?? string.Empty).Trim().ToUpperInvariant();
            if (left.Length == 0 || right.Length == 0)
            {
                return false;
            }

            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeAircraftCode(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (normalized.StartsWith("ATR72") || normalized.StartsWith("AT76")) return "AT76";
            if (normalized.StartsWith("B736") || normalized == "B737-700") return "B737";
            if (normalized.StartsWith("B738") || normalized == "B737-800") return "B738";
            if (normalized.StartsWith("B739") || normalized == "B737-900") return "B739";
            if (normalized.StartsWith("B38M")) return "B38M";
            if (normalized.StartsWith("A319")) return "A319";
            if (normalized.StartsWith("A320")) return "A320";
            if (normalized.StartsWith("A20N")) return "A20N";
            if (normalized.StartsWith("A321")) return "A321";
            if (normalized.StartsWith("A21N")) return "A21N";
            if (normalized.StartsWith("A339")) return "A339";
            if (normalized.StartsWith("A359")) return "A359";
            if (normalized.StartsWith("B77W")) return "B77W";
            if (normalized.StartsWith("B772")) return "B772";
            if (normalized.StartsWith("B789")) return "B789";
            if (normalized.StartsWith("B78X")) return "B78X";
            if (normalized.StartsWith("B350")) return "B350";
            if (normalized.StartsWith("BE58")) return "BE58";
            if (normalized.StartsWith("C208")) return "C208";
            if (normalized.StartsWith("E175")) return "E175";
            if (normalized.StartsWith("E190")) return "E190";
            if (normalized.StartsWith("E195")) return "E195";
            if (normalized.StartsWith("MD82")) return "MD82";
            if (normalized.StartsWith("MD83")) return "MD83";
            if (normalized.StartsWith("MD88")) return "MD88";
            return normalized;
        }

        private static bool ValidateCommercialFlightNumber(PreparedDispatch dispatch, string actualFlightNumber)
        {
            var expected = NormalizeCommercialFlightNumber(dispatch == null ? string.Empty : dispatch.FlightDesignator, dispatch == null ? string.Empty : dispatch.FlightNumber);
            var actual = NormalizeCommercialFlightNumber(actualFlightNumber, actualFlightNumber);
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
            {
                return false;
            }

            return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeCommercialFlightNumber(string preferred, string fallback)
        {
            var candidate = !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;
            var normalized = (candidate ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty);
            if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

            if (normalized.Contains("-"))
            {
                var digits = ExtractDigits(normalized);
                return string.IsNullOrWhiteSpace(digits) ? normalized : "PWG" + digits;
            }

            if (normalized.StartsWith("PWG"))
            {
                var digits = ExtractDigits(normalized);
                return string.IsNullOrWhiteSpace(digits) ? normalized : "PWG" + digits;
            }

            var suffixDigits = ExtractDigits(normalized);
            return string.IsNullOrWhiteSpace(suffixDigits) ? normalized : "PWG" + suffixDigits;
        }

        private static string ExtractDigits(string value)
        {
            var result = string.Empty;
            foreach (var character in value ?? string.Empty)
            {
                if (char.IsDigit(character))
                {
                    result += character;
                }
            }

            return result;
        }

        private static bool IsColdAndDark(SimData sample)
        {
            if (sample == null)
            {
                return false;
            }

            return EnginesStopped(sample)
                   && !sample.NavLightsOn
                   && !sample.BeaconLightsOn
                   && !sample.StrobeLightsOn
                   && !sample.LandingLightsOn
                   && !sample.TaxiLightsOn
                   && !sample.ApuRunning
                   && !sample.BleedAirOn
                   && !sample.BatteryMasterOn
                   && !sample.AvionicsMasterOn;
        }

        private static bool EnginesStopped(SimData sample)
        {
            if (sample == null)
            {
                return true;
            }

            return sample.Engine1N1 < 5
                   && sample.Engine2N1 < 5
                   && sample.Engine3N1 < 5
                   && sample.Engine4N1 < 5
                   && !sample.EngineOneRunning
                   && !sample.EngineTwoRunning
                   && !sample.EngineThreeRunning
                   && !sample.EngineFourRunning;
        }

        private static double PercentageDifference(double expected, double actual)
        {
            if (expected <= 0d || actual <= 0d)
            {
                return 0d;
            }

            return Math.Abs(actual - expected) / expected * 100d;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item)) ?? string.Empty;
        }
    }
}
