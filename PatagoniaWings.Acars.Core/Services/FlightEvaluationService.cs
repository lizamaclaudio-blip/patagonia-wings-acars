using System;
using System.Collections.Generic;
using System.Linq;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    /// <summary>
    /// Implementa el sistema de evaluación de vuelo SUR Air:
    ///   • Score de Procedimientos  — parte en 100, solo descuenta (penalizaciones).
    ///   • Score de Performance     — parte en 60, sube o baja (bonificaciones + penalizaciones).
    /// Calificaciones:
    ///   ★★★ Excelente   — Proc ≥90 / Perf ≥80
    ///   ★★  Satisfactorio — Proc ≥75 / Perf ≥65
    ///   ★   Marginal    — Proc ≥60 / Perf ≥50
    ///       Insatisfactorio — por debajo de los anteriores
    /// </summary>
    public sealed class FlightEvaluationService
    {
        // ── Resultado ──────────────────────────────────────────────────────────
        public sealed class EvaluationResult
        {
            public int ProcedureScore { get; set; }
            public int PerformanceScore { get; set; }
            public string ProcedureGrade { get; set; } = string.Empty;
            public string PerformanceGrade { get; set; } = string.Empty;
            public List<ScoreEvent> Violations { get; set; } = new List<ScoreEvent>();
            public List<ScoreEvent> Bonuses { get; set; } = new List<ScoreEvent>();
            public string Summary { get; set; } = string.Empty;

            // Legacy compatibility — deltas por fase para Supabase
            public int LandingPenalty { get; set; }
            public int TaxiPenalty { get; set; }
            public int AirbornePenalty { get; set; }
            public int ApproachPenalty { get; set; }
            public int CabinPenalty { get; set; }
        }

        // ── Contexto de vuelo ──────────────────────────────────────────────────
        private readonly IReadOnlyList<SimData> _log;
        private readonly double _landingVS;
        private readonly double _landingG;
        private readonly string _aircraftIcao;
        private readonly string _aircraftName;
        private readonly bool _isPressurized;

        public FlightEvaluationService(
            IReadOnlyList<SimData> telemetryLog,
            double landingVS,
            double landingG,
            string aircraftIcao,
            string aircraftName,
            bool isPressurized)
        {
            _log = telemetryLog ?? Array.Empty<SimData>();
            _landingVS = landingVS;
            _landingG = landingG;
            _aircraftIcao = (aircraftIcao ?? string.Empty).Trim().ToUpperInvariant();
            _aircraftName = (aircraftName ?? string.Empty).Trim().ToUpperInvariant();
            _isPressurized = isPressurized;
        }

        // ── Punto de entrada ───────────────────────────────────────────────────
        public EvaluationResult Evaluate()
        {
            var violations = new List<ScoreEvent>();
            var bonuses    = new List<ScoreEvent>();

            // ── PROCEDIMIENTOS ────────────────────────────────────────────────
            int proc = 100;

            // legacy deltas
            int beforeTax  = proc; EvalTax(ref proc, violations);  int taxDelta  = proc - beforeTax;
            int beforeTo   = proc; EvalTo (ref proc, violations);  int toDelta   = proc - beforeTo;
            int beforeAsc  = proc; EvalAsc(ref proc, violations);  int ascDelta  = proc - beforeAsc;
            int beforeCru  = proc; EvalCru(ref proc, violations);  int cruDelta  = proc - beforeCru;
            int beforeDes  = proc; EvalDes(ref proc, violations);  // descent → cabin → cabin penalty
            int desDelta   = proc - beforeDes;
            int beforeApp  = proc; EvalApp(ref proc, violations);  int appDelta  = proc - beforeApp;
            int beforeLdg  = proc; EvalLdg(ref proc, violations);  int ldgDelta  = proc - beforeLdg;
            int beforeTag  = proc; EvalTag(ref proc, violations);
            int beforePar  = proc; EvalPar(ref proc, violations);

            proc = Math.Max(0, Math.Min(100, proc));

            // ── PERFORMANCE ───────────────────────────────────────────────────
            int perf = 60;
            EvalPerfGeneral(ref perf, violations, bonuses);
            EvalPerfTakeoff(ref perf, bonuses);
            EvalPerfLanding(ref perf, violations, bonuses);
            EvalPerfCruise (ref perf, bonuses);

            perf = Math.Max(0, Math.Min(100, perf));

            // ── Resumen textual de infracciones ───────────────────────────────
            var parts = violations.Select(v => $"{v.Code} {v.Description} ({v.Points})").ToList();
            var summary = parts.Count == 0 ? "Procedimiento limpio de punta a punta." : string.Join(" · ", parts);

            // legacy mapping: taxi, airborne (TO+ASC+CRU), approach, landing, cabin
            var airborneDelta = toDelta + ascDelta + cruDelta + desDelta;
            var cabinDelta = violations
                .Where(v => v.Phase == "DES" || v.Code.StartsWith("CRU-04") || v.Code.StartsWith("CRU-05"))
                .Sum(v => v.Points);

            return new EvaluationResult
            {
                ProcedureScore  = proc,
                PerformanceScore = perf,
                ProcedureGrade  = ProcGrade(proc),
                PerformanceGrade = PerfGrade(perf),
                Violations = violations,
                Bonuses    = bonuses,
                Summary    = summary,
                LandingPenalty  = ldgDelta,
                TaxiPenalty     = taxDelta,
                AirbornePenalty = airborneDelta,
                ApproachPenalty = appDelta,
                CabinPenalty    = cabinDelta
            };
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PROCEDIMIENTOS
        // ══════════════════════════════════════════════════════════════════════

        // ── TAX — Rodaje de salida ─────────────────────────────────────────────
        private void EvalTax(ref int score, List<ScoreEvent> violations)
        {
            var samples = _log.Where(s => s.OnGround && s.GroundSpeed >= 3 && s.GroundSpeed <= 45).ToList();
            if (samples.Count == 0) return;

            // TAX-01: Beacon encendido durante taxi [requerido]
            if (samples.Count(s => !s.BeaconLightsOn) > samples.Count / 3)
                Penalize(violations, ref score, "TAX-01", "TAX", "Beacon apagado en taxi", -5);

            // TAX-02: Luces de taxi encendidas [requerido]
            if (samples.Count(s => !s.TaxiLightsOn) > samples.Count / 2)
                Penalize(violations, ref score, "TAX-02", "TAX", "Taxi lights apagadas", -4);

            // TAX-03: Landing lights EN taxi (inadecuado por encandilamiento)
            if (samples.Count(s => s.LandingLightsOn) > samples.Count / 2)
                Penalize(violations, ref score, "TAX-03", "TAX", "Landing lights encendidas en taxi", -3);

            // TAX-04: Velocidad de taxi excesiva (>30 kts GS)
            if (samples.Any(s => s.GroundSpeed > 30))
                Penalize(violations, ref score, "TAX-04", "TAX", "Velocidad de taxi >30 kts", -5);

            // TAX-05: Strobes encendidas en taxi (deslumbrante en plataforma)
            if (samples.Count(s => s.StrobeLightsOn) > samples.Count / 2)
                Penalize(violations, ref score, "TAX-05", "TAX", "Strobes encendidas en taxi", -3);
        }

        // ── TO — Despegue ─────────────────────────────────────────────────────
        private void EvalTo(ref int score, List<ScoreEvent> violations)
        {
            // Muestras justo antes o durante el despegue (IAS 50-130 kts en tierra o AGL<400)
            var rollSamples = _log
                .Where(s => (s.OnGround && s.GroundSpeed > 30) || (!s.OnGround && s.AltitudeAGL < 400 && s.IndicatedAirspeed > 50))
                .ToList();
            if (rollSamples.Count == 0) return;

            // TO-01: Strobes ON en despegue [requerido]
            if (rollSamples.Count(s => !s.StrobeLightsOn) > rollSamples.Count / 3)
                Penalize(violations, ref score, "TO-01", "TO", "Strobes apagadas al despegar", -8);

            // TO-02: Landing lights ON al despegar [requerido]
            if (rollSamples.Count(s => !s.LandingLightsOn) > rollSamples.Count / 3)
                Penalize(violations, ref score, "TO-02", "TO", "Landing lights apagadas al despegar", -5);

            // TO-03: Transponder Modo C/S [requerido]
            if (rollSamples.Count(s => !s.TransponderCharlieMode) > rollSamples.Count / 3)
                Penalize(violations, ref score, "TO-03", "TO", "Transponder no en Modo C/S al despegar", -5);

            // TO-04: Seat belt sign [requerido]
            if (rollSamples.Count(s => !s.SeatBeltSign) > rollSamples.Count / 3)
                Penalize(violations, ref score, "TO-04", "TO", "Seat belt sign apagado al despegar", -4);
        }

        // ── ASC — Ascenso ─────────────────────────────────────────────────────
        private void EvalAsc(ref int score, List<ScoreEvent> violations)
        {
            var climbSamples = _log
                .Where(s => !s.OnGround && s.VerticalSpeed > 200 && s.AltitudeFeet > 500 && s.AltitudeFeet < 40000)
                .ToList();
            if (climbSamples.Count == 0) return;

            // ASC-01: Landing lights apagadas sobre FL100 (10000 ft)
            var above10k = climbSamples.Where(s => s.AltitudeFeet > 10000).ToList();
            if (above10k.Count > 0 && above10k.Count(s => s.LandingLightsOn) > above10k.Count / 2)
                Penalize(violations, ref score, "ASC-01", "ASC", "Landing lights encendidas sobre 10000 ft", -4);

            // ASC-02: Seat belt sign encendido en ascenso
            if (climbSamples.Count(s => !s.SeatBeltSign) > climbSamples.Count / 2)
                Penalize(violations, ref score, "ASC-02", "ASC", "Seat belt sign apagado en ascenso", -3);

            // ASC-03: No smoking sign en ascenso (presurizado)
            if (_isPressurized && climbSamples.Count(s => !s.NoSmokingSign) > climbSamples.Count / 2)
                Penalize(violations, ref score, "ASC-03", "ASC", "No smoking sign apagado en ascenso", -3);

            // ASC-04: Beacon encendido en ascenso
            if (climbSamples.Count(s => !s.BeaconLightsOn) > climbSamples.Count / 3)
                Penalize(violations, ref score, "ASC-04", "ASC", "Beacon apagado en ascenso", -5);
        }

        // ── CRU — Crucero ─────────────────────────────────────────────────────
        private void EvalCru(ref int score, List<ScoreEvent> violations)
        {
            var cruSamples = _log
                .Where(s => !s.OnGround && Math.Abs(s.VerticalSpeed) < 500 && s.AltitudeFeet > 8000)
                .ToList();
            if (cruSamples.Count == 0) return;

            // CRU-01: Pausa detectada en vuelo
            if (_log.Any(s => s.Pause))
                Penalize(violations, ref score, "CRU-01", "CRU", "Pausa activa durante el vuelo", -10);

            // CRU-02: Transponder Modo C/S en crucero
            if (cruSamples.Count(s => !s.TransponderCharlieMode) > cruSamples.Count / 3)
                Penalize(violations, ref score, "CRU-02", "CRU", "Transponder no en Modo C/S en crucero", -6);

            // CRU-03: Beacon encendido en crucero
            if (cruSamples.Count(s => !s.BeaconLightsOn) > cruSamples.Count / 3)
                Penalize(violations, ref score, "CRU-03", "CRU", "Beacon apagado en crucero", -4);

            // CRU-04: Strobes encendidas en crucero
            if (cruSamples.Count(s => !s.StrobeLightsOn) > cruSamples.Count / 3)
                Penalize(violations, ref score, "CRU-04", "CRU", "Strobes apagadas en crucero", -4);

            // CRU-05: Banco excesivo en ruta (>45°) — turbulencia o maniobra brusca
            if (cruSamples.Any(s => Math.Abs(s.Bank) > 45))
                Penalize(violations, ref score, "CRU-05", "CRU", "Banco >45° en crucero", -5);
        }

        // ── DES — Descenso / Cabina ────────────────────────────────────────────
        private void EvalDes(ref int score, List<ScoreEvent> violations)
        {
            if (!_isPressurized) return;

            var cruiseSamples = _log.Where(s => !s.OnGround && s.AltitudeFeet > 10000).ToList();
            if (cruiseSamples.Count == 0) return;

            // DES-01: Altitud de cabina crítica (>10000 ft cabina en vuelo)
            var highCabin = cruiseSamples.Count(s => s.CabinAltitudeFeet > 10000);
            var modCabin  = cruiseSamples.Count(s => s.CabinAltitudeFeet > 8500 && s.CabinAltitudeFeet <= 10000);

            if (highCabin > cruiseSamples.Count / 4)
                Penalize(violations, ref score, "DES-01", "DES", "Presión de cabina crítica (>10000 ft cabina)", -15);
            else if (modCabin > cruiseSamples.Count / 3)
                Penalize(violations, ref score, "DES-02", "DES", "Altitud de cabina elevada (>8500 ft)", -8);
        }

        // ── APP — Aproximación ────────────────────────────────────────────────
        private void EvalApp(ref int score, List<ScoreEvent> violations)
        {
            var appSamples = _log
                .Where(s => !s.OnGround && s.AltitudeAGL > 0 && s.AltitudeAGL < 3000 && s.IndicatedAirspeed < 230)
                .ToList();
            if (appSamples.Count == 0) return;

            // APP-01: Tren de aterrizaje extendido en aproximación
            if (appSamples.Count(s => !s.GearDown) > appSamples.Count / 2)
                Penalize(violations, ref score, "APP-01", "LDG", "Tren no extendido en aproximación", -10);

            // APP-02: Flaps desplegados en aproximación
            if (appSamples.Count(s => !s.FlapsDeployed && s.IndicatedAirspeed < 190) > appSamples.Count / 3)
                Penalize(violations, ref score, "APP-02", "LDG", "Sin flaps en aproximación final", -8);

            // APP-03: Landing lights en aproximación
            if (appSamples.Count(s => !s.LandingLightsOn) > appSamples.Count / 2)
                Penalize(violations, ref score, "APP-03", "LDG", "Landing lights apagadas en aproximación", -5);

            // APP-04: Seat belt sign en aproximación
            if (appSamples.Count(s => !s.SeatBeltSign) > appSamples.Count / 2)
                Penalize(violations, ref score, "APP-04", "LDG", "Seat belt sign apagado en aproximación", -4);

            // APP-05: No smoking sign en aproximación (presurizado)
            if (_isPressurized && appSamples.Count(s => !s.NoSmokingSign) > appSamples.Count / 2)
                Penalize(violations, ref score, "APP-05", "LDG", "No smoking sign apagado en aproximación", -4);
        }

        // ── LDG — Aterrizaje ──────────────────────────────────────────────────
        private void EvalLdg(ref int score, List<ScoreEvent> violations)
        {
            var vs = Math.Abs(_landingVS);

            // LDG-01: Velocidad vertical de aterrizaje
            if (vs > 700)
                Penalize(violations, ref score, "LDG-01", "LDG", $"VS de aterrizaje muy duro ({vs:F0} fpm)", -25);
            else if (vs > 400)
                Penalize(violations, ref score, "LDG-01", "LDG", $"VS de aterrizaje firme ({vs:F0} fpm)", -15);
            else if (vs > 200)
                Penalize(violations, ref score, "LDG-01", "LDG", $"VS de aterrizaje normal ({vs:F0} fpm)", -5);

            // LDG-02: Factor G de aterrizaje
            if (_landingG > 2.0)
                Penalize(violations, ref score, "LDG-02", "LDG", $"Factor G alto en aterrizaje ({_landingG:F2} G)", -15);
            else if (_landingG > 1.5)
                Penalize(violations, ref score, "LDG-02", "LDG", $"Factor G elevado en aterrizaje ({_landingG:F2} G)", -8);
        }

        // ── TAG — Rodaje de llegada ────────────────────────────────────────────
        private void EvalTag(ref int score, List<ScoreEvent> violations)
        {
            // Muestras después del toque (baja velocidad, en tierra)
            var rolloutSamples = _log
                .Where(s => s.OnGround && s.GroundSpeed >= 2 && s.GroundSpeed < 40)
                .OrderBy(s => s.CapturedAtUtc)
                .ToList();
            if (rolloutSamples.Count == 0) return;

            // TAG-01: Strobes apagadas al salir de pista
            if (rolloutSamples.Count(s => s.StrobeLightsOn) > rolloutSamples.Count / 2)
                Penalize(violations, ref score, "TAG-01", "TAG", "Strobes encendidas en rodaje de llegada", -3);

            // TAG-02: Beacon apagado en rodaje de llegada
            if (rolloutSamples.Count(s => !s.BeaconLightsOn) > rolloutSamples.Count / 3)
                Penalize(violations, ref score, "TAG-02", "TAG", "Beacon apagado en rodaje de llegada", -3);
        }

        // ── PAR — Estacionamiento ─────────────────────────────────────────────
        private void EvalPar(ref int score, List<ScoreEvent> violations)
        {
            var parkSamples = _log
                .Where(s => s.OnGround && s.GroundSpeed < 2 && s.ParkingBrake)
                .ToList();
            if (parkSamples.Count == 0) return;

            // PAR-01: Beacon apagado al estacionar
            if (parkSamples.Count(s => s.BeaconLightsOn) > parkSamples.Count / 2)
                Penalize(violations, ref score, "PAR-01", "PAR", "Beacon encendido al estacionar", -3);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PERFORMANCE
        // ══════════════════════════════════════════════════════════════════════

        // ── GEN — General ─────────────────────────────────────────────────────
        private void EvalPerfGeneral(ref int perf, List<ScoreEvent> violations, List<ScoreEvent> bonuses)
        {
            // GEN-01: Pausa en vuelo
            if (_log.Any(s => s.Pause))
                PenalizePerf(violations, ref perf, "GEN-01", "GEN", "Pausa activa durante el vuelo", -10);

            // GEN-02: Banco excesivo en ruta
            var airborneSamples = _log.Where(s => !s.OnGround && s.AltitudeFeet > 500).ToList();
            if (airborneSamples.Any(s => Math.Abs(s.Bank) > 45))
                PenalizePerf(violations, ref perf, "GEN-02", "GEN", "Banco >45° en vuelo", -8);

            // GEN-03: Vuelo completado (bonus base)
            if (_log.Count > 20)
                BonusPerf(bonuses, ref perf, "GEN-03", "GEN", "Vuelo completado satisfactoriamente", +15);
        }

        // ── TO-perf — Performance de despegue ────────────────────────────────
        private void EvalPerfTakeoff(ref int perf, List<ScoreEvent> bonuses)
        {
            var climbSamples = _log
                .Where(s => !s.OnGround && s.AltitudeFeet > 200 && s.AltitudeFeet < 5000 && s.VerticalSpeed > 0)
                .ToList();
            if (climbSamples.Count == 0) return;

            // TO-01: Ascenso inicial robusto (VS >800 fpm)
            var avgClimbVS = climbSamples.Average(s => s.VerticalSpeed);
            if (avgClimbVS > 800)
                BonusPerf(bonuses, ref perf, "TO-01", "TO", $"Ascenso inicial enérgico ({avgClimbVS:F0} fpm)", +5);
        }

        // ── LDG-perf — Performance de aterrizaje ─────────────────────────────
        private void EvalPerfLanding(ref int perf, List<ScoreEvent> violations, List<ScoreEvent> bonuses)
        {
            var vs = Math.Abs(_landingVS);

            if (vs < 100)
                BonusPerf(bonuses, ref perf, "LDG-01", "LDG", $"Aterrizaje suave ({vs:F0} fpm)", +10);
            else if (vs < 200)
                BonusPerf(bonuses, ref perf, "LDG-01", "LDG", $"Aterrizaje normal ({vs:F0} fpm)", +5);
            else if (vs > 700)
                PenalizePerf(violations, ref perf, "LDG-02", "LDG", $"Aterrizaje muy duro ({vs:F0} fpm)", -15);
            else if (vs > 400)
                PenalizePerf(violations, ref perf, "LDG-02", "LDG", $"Aterrizaje firme ({vs:F0} fpm)", -8);

            // Factor G
            if (_landingG > 0 && _landingG < 1.3)
                BonusPerf(bonuses, ref perf, "LDG-03", "LDG", $"Factor G excelente ({_landingG:F2} G)", +5);
            else if (_landingG > 2.0)
                PenalizePerf(violations, ref perf, "LDG-03", "LDG", $"Factor G alto ({_landingG:F2} G)", -10);
        }

        // ── CRU-perf — Performance de crucero ─────────────────────────────────
        private void EvalPerfCruise(ref int perf, List<ScoreEvent> bonuses)
        {
            var cruSamples = _log
                .Where(s => !s.OnGround && Math.Abs(s.VerticalSpeed) < 500 && s.AltitudeFeet > 8000)
                .ToList();
            if (cruSamples.Count < 5) return;

            // CRU-01: Uso de autopiloto en crucero (bonus por disciplina)
            var apSamples = cruSamples.Count(s => s.AutopilotActive);
            if (apSamples > cruSamples.Count / 2)
                BonusPerf(bonuses, ref perf, "CRU-01", "CRU", "Uso de autopiloto en crucero", +3);

            // CRU-02: Altitud constante (VS < 100 fpm la mayor parte del crucero)
            var stableSamples = cruSamples.Count(s => Math.Abs(s.VerticalSpeed) < 100);
            if (stableSamples > cruSamples.Count * 2 / 3)
                BonusPerf(bonuses, ref perf, "CRU-02", "CRU", "Altitud de crucero estable", +3);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════════════════════════

        private static void Penalize(List<ScoreEvent> violations, ref int score, string code, string phase, string desc, int pts)
        {
            score += pts; // pts es negativo
            violations.Add(new ScoreEvent { Code = code, Phase = phase, Description = desc, Points = pts });
        }

        private static void PenalizePerf(List<ScoreEvent> violations, ref int perf, string code, string phase, string desc, int pts)
        {
            perf += pts;
            violations.Add(new ScoreEvent { Code = code, Phase = phase, Description = desc, Points = pts });
        }

        private static void BonusPerf(List<ScoreEvent> bonuses, ref int perf, string code, string phase, string desc, int pts)
        {
            perf += pts;
            bonuses.Add(new ScoreEvent { Code = code, Phase = phase, Description = desc, Points = pts });
        }

        private static string ProcGrade(int score)
        {
            if (score >= 90) return "★★★ Excelente";
            if (score >= 75) return "★★ Satisfactorio";
            if (score >= 60) return "★ Marginal";
            return "Insatisfactorio";
        }

        private static string PerfGrade(int score)
        {
            if (score >= 80) return "★★★ Excelente";
            if (score >= 65) return "★★ Satisfactorio";
            if (score >= 50) return "★ Marginal";
            return "Insatisfactorio";
        }
    }
}
