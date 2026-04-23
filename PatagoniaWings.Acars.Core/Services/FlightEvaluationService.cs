using System;
using System.Collections.Generic;
using System.Linq;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    /// <summary>
    /// Adaptador transicional: mantiene la firma histórica pero delega toda la evaluación
    /// al motor basado en JSON. No debe volver a contener reglas hardcodeadas.
    /// </summary>
    public sealed class FlightEvaluationService
    {
        public sealed class EvaluationResult
        {
            public int PatagoniaScore { get; set; }
            public int ProcedureScore { get; set; }
            public int PerformanceScore { get; set; }
            public string PatagoniaGrade { get; set; } = string.Empty;
            public string ProcedureGrade { get; set; } = string.Empty;
            public string PerformanceGrade { get; set; } = string.Empty;
            public List<ScoreEvent> Violations { get; set; } = new List<ScoreEvent>();
            public List<ScoreEvent> Bonuses { get; set; } = new List<ScoreEvent>();
            public string Summary { get; set; } = string.Empty;
            public int LandingPenalty { get; set; }
            public int TaxiPenalty { get; set; }
            public int AirbornePenalty { get; set; }
            public int ApproachPenalty { get; set; }
            public int CabinPenalty { get; set; }
            public PatagoniaEvaluationReport PatagoniaEvaluation { get; set; } = new PatagoniaEvaluationReport();
        }

        private readonly IReadOnlyList<SimData> _log;
        private readonly double _landingVS;
        private readonly double _landingG;
        private readonly string _aircraftIcao;
        private readonly string _aircraftName;

        public FlightEvaluationService(
            IReadOnlyList<SimData> telemetryLog,
            double landingVS,
            double landingG,
            string aircraftIcao,
            string aircraftName,
            bool isPressurized,
            double departureElevationFt = 0,
            double arrivalElevationFt = 0,
            bool cabinSystemsReliable = true)
        {
            _log = telemetryLog ?? Array.Empty<SimData>();
            _landingVS = landingVS;
            _landingG = landingG;
            _aircraftIcao = aircraftIcao ?? string.Empty;
            _aircraftName = aircraftName ?? string.Empty;
        }

        public EvaluationResult Evaluate()
        {
            var report = new FlightReport
            {
                AircraftIcao = _aircraftIcao,
                LandingVS = _landingVS,
                LandingG = _landingG,
                MaxAltitudeFeet = _log.Count == 0 ? 0 : _log.Max(sample => sample.AltitudeFeet),
                MaxSpeedKts = _log.Count == 0 ? 0 : _log.Max(sample => sample.IndicatedAirspeed),
                Distance = 0,
                FuelUsed = 0,
                DepartureTime = _log.Count == 0 ? DateTime.UtcNow : _log[0].CapturedAtUtc,
                ArrivalTime = _log.Count == 0 ? DateTime.UtcNow : _log[_log.Count - 1].CapturedAtUtc
            };

            var input = new PatagoniaEvaluationInput
            {
                Flight = new Flight
                {
                    AircraftIcao = _aircraftIcao,
                    AircraftName = _aircraftName,
                    AircraftDisplayName = _aircraftName,
                    AircraftTypeCode = _aircraftIcao
                },
                Report = report,
                TelemetryLog = _log
            };

            var evaluation = new PatagoniaEvaluationService().Evaluate(input);

            var violations = evaluation.Penalties
                .Select(MapScoreEvent)
                .ToList();

            var bonuses = evaluation.Bonuses
                .Select(MapScoreEvent)
                .ToList();

            return new EvaluationResult
            {
                PatagoniaScore = evaluation.PatagoniaScore,
                ProcedureScore = evaluation.ProcedureScore,
                PerformanceScore = evaluation.PerformanceScore,
                PatagoniaGrade = evaluation.PatagoniaGrade,
                ProcedureGrade = evaluation.ProcedureGrade,
                PerformanceGrade = evaluation.PerformanceGrade,
                Violations = violations,
                Bonuses = bonuses,
                Summary = evaluation.Summary,
                LandingPenalty = SumPhasePenalty(evaluation, "LDG"),
                TaxiPenalty = SumPhasePenalty(evaluation, "TAX") + SumPhasePenalty(evaluation, "TAG"),
                AirbornePenalty = SumPhasePenalty(evaluation, "TO") + SumPhasePenalty(evaluation, "ASC") + SumPhasePenalty(evaluation, "CRU"),
                ApproachPenalty = SumPhasePenalty(evaluation, "DES") + SumPhasePenalty(evaluation, "LDG"),
                CabinPenalty = SumCategoryPenalty(evaluation, "cabin"),
                PatagoniaEvaluation = evaluation
            };
        }

        private static ScoreEvent MapScoreEvent(PatagoniaTriggeredRuleResult rule)
        {
            return new ScoreEvent
            {
                Code = rule.RuleId,
                Phase = rule.Phase,
                Description = string.IsNullOrWhiteSpace(rule.Description) ? rule.Message : rule.Description,
                Points = rule.ScoreDelta
            };
        }

        private static int SumPhasePenalty(PatagoniaEvaluationReport evaluation, string phase)
        {
            return evaluation.Penalties
                .Where(rule => string.Equals(rule.Phase, phase, StringComparison.OrdinalIgnoreCase))
                .Sum(rule => rule.ScoreDelta);
        }

        private static int SumCategoryPenalty(PatagoniaEvaluationReport evaluation, string category)
        {
            return evaluation.Penalties
                .Where(rule => string.Equals(rule.Category, category, StringComparison.OrdinalIgnoreCase))
                .Sum(rule => rule.ScoreDelta);
        }
    }
}
