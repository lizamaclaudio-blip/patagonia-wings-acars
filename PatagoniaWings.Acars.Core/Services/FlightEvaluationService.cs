using System;
using System.Collections.Generic;
using System.Linq;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    /// <summary>
    /// Implementa el sistema de evaluación de vuelo SUR Air v5.0:
    ///   • Procedimientos — parte en 100, solo descuenta penalizaciones.
    ///   • Performance    — parte en 60, sube o baja con bonos y penalizaciones (sin tope máximo).
    ///
    /// Calificaciones según Guía SUR Air v5.0:
    ///   Procedimientos:   ★★★ Excelente = 100 | ★★ Satisfactorio = 90-99 | ★ Marginal = 80-89 | Insatisfactorio < 80
    ///   Performance:      ★★★ Excelente ≥ 100 | ★★ Satisfactorio = 60-99 | ★ Marginal = 30-59 | Insatisfactorio < 30
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

            // Deltas por fase (legacy Supabase)
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

        // Contexto extendido (opcional, se infiere del log cuando es posible)
        private readonly double _departureElevationFt;
        private readonly double _arrivalElevationFt;
        /// <summary>
        /// true = el perfil del avión lee sistemas de cabina de forma confiable
        /// (seatbelt, nosmoking, transponder).
        /// false = esos sistemas NO se usan como penalidad dura (Rule 14 matriz Patagonia Wings).
        /// </summary>
        private readonly bool _cabinSystemsReliable;

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
            _aircraftIcao = (aircraftIcao ?? string.Empty).Trim().ToUpperInvariant();
            _aircraftName = (aircraftName ?? string.Empty).Trim().ToUpperInvariant();
            _isPressurized = isPressurized;
            _departureElevationFt = departureElevationFt;
            _arrivalElevationFt = arrivalElevationFt;
            _cabinSystemsReliable = cabinSystemsReliable;
        }

        // ── Punto de entrada ───────────────────────────────────────────────────
        public EvaluationResult Evaluate()
        {
            var violations = new List<ScoreEvent>();
            var bonuses    = new List<ScoreEvent>();

            // ── PROCEDIMIENTOS ────────────────────────────────────────────────
            int proc = 100;

            int beforeTax = proc; EvalPre(ref proc, violations);
            int beforeIgn = proc; EvalIgn(ref proc, violations);
            int beforeTaxi = proc; EvalTax(ref proc, violations);  int taxDelta  = proc - beforeTaxi;
            int beforeTo  = proc; EvalTo (ref proc, violations);   int toDelta   = proc - beforeTo;
            int beforeAsc = proc; EvalAsc(ref proc, violations);   int ascDelta  = proc - beforeAsc;
            int beforeCru = proc; EvalCru(ref proc, violations);   int cruDelta  = proc - beforeCru;
            int beforeDes = proc; EvalDes(ref proc, violations);   int desDelta  = proc - beforeDes;
            int beforeApp = proc; EvalApp(ref proc, violations);   int appDelta  = proc - beforeApp;
            int beforeLdg = proc; EvalLdg(ref proc, violations);   int ldgDelta  = proc - beforeLdg;
            int beforeTag = proc; EvalTag(ref proc, violations);
            int beforePar = proc; EvalPar(ref proc, violations);

            proc = Math.Max(0, Math.Min(100, proc));

            // ── PERFORMANCE ───────────────────────────────────────────────────
            int perf = 60; // base SUR Air per PDF: satisfactorio = 60-99
            EvalPerfGeneral(ref perf, violations, bonuses);
            EvalPerfTakeoff(ref perf, violations, bonuses);
            EvalPerfLanding(ref perf, violations, bonuses);
            EvalPerfCruise (ref perf, bonuses);
            EvalPerfPlan   (ref perf, violations, bonuses);

            // Performance SIN tope superior (puede superar 100 con muchas bonificaciones)
            perf = Math.Max(0, perf);

            // ── Resumen textual ───────────────────────────────────────────────
            var parts = violations.Select(v => $"[{v.Code}] {v.Description} ({v.Points:+#;-#;0})").ToList();
            var summary = parts.Count == 0
                ? "Procedimiento limpio de punta a punta. ¡Excelente vuelo!"
                : string.Join(" · ", parts);

            var airborneDelta = toDelta + ascDelta + cruDelta + desDelta;
            var cabinDelta = violations
                .Where(v => v.Phase == "DES" || v.Phase == "CRU")
                .Sum(v => v.Points);

            return new EvaluationResult
            {
                ProcedureScore   = proc,
                PerformanceScore = perf,
                ProcedureGrade   = ProcGrade(proc),
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
        //  PARTE 1: PROCEDIMIENTOS
        // ══════════════════════════════════════════════════════════════════════

        // ── PRE — Pre-embarque ────────────────────────────────────────────────
        private void EvalPre(ref int score, List<ScoreEvent> violations)
        {
            if (_log.Count == 0) return;

            // PRE-01: Luces de navegación desde inicio (primeros 5 min)
            var early = _log.Take(Math.Min(30, _log.Count)).ToList();
            if (early.Count > 0 && early.Count(s => !s.NavLightsOn) > early.Count / 2)
                Penalize(violations, ref score, "PRE-01", "PRE", "Luces NAV no encendidas al inicio", -4);

            // PRE-04: Freno de estacionamiento al comenzar
            var firstFew = _log.Take(10).ToList();
            if (firstFew.Count > 0 && firstFew.Count(s => !s.ParkingBrake) > firstFew.Count / 2)
                Penalize(violations, ref score, "PRE-04", "PRE", "Sin freno de estacionamiento al iniciar", -5);

            // PRE-11: ACARS iniciado con motores encendidos (N1 > 5% en primeras muestras)
            if (firstFew.Count > 0 && firstFew.Any(s => s.Engine1N1 > 5 || s.Engine2N1 > 5))
                Penalize(violations, ref score, "PRE-11", "PRE", "ACARS iniciado con motores encendidos", -8);
        }

        // ── IGN — Encendido de motores ────────────────────────────────────────
        private void EvalIgn(ref int score, List<ScoreEvent> violations)
        {
            // Detectar el momento de encendido de motores
            var engineOnIdx = -1;
            for (int i = 1; i < _log.Count; i++)
            {
                if ((_log[i].Engine1N1 > 5 || _log[i].Engine2N1 > 5) &&
                    (_log[i - 1].Engine1N1 <= 5 && _log[i - 1].Engine2N1 <= 5))
                {
                    engineOnIdx = i;
                    break;
                }
            }

            if (engineOnIdx < 0) return;

            var ignSample = _log[engineOnIdx];

            // IGN-02: Beacon encendido antes/durante el arranque
            var preIgnSamples = _log.Take(engineOnIdx + 5).ToList();
            if (preIgnSamples.Count > 0 && preIgnSamples.Count(s => s.Engine1N1 > 5 && !s.BeaconLightsOn) > 2)
                Penalize(violations, ref score, "IGN-02", "IGN", "Encendido de motor sin luces BEACON", -8);

            // IGN-06: Freno de estacionamiento durante encendido
            if (!ignSample.ParkingBrake)
                Penalize(violations, ref score, "IGN-06", "IGN", "Encendido de motor sin freno de estacionamiento", -5);

            // IGN-04: Combustible incorrecto — detectar si fuel cambió bruscamente (>20%) después del encendido
            var postIgn = _log.Skip(engineOnIdx).Take(20).ToList();
            if (postIgn.Count > 5)
            {
                var fuelAtIgn = postIgn.First().FuelKg;
                var fuelVariance = postIgn.Max(s => s.FuelKg) - postIgn.Min(s => s.FuelKg);
                if (fuelAtIgn > 0 && fuelVariance > fuelAtIgn * 0.15)
                    Penalize(violations, ref score, "IGN-04", "IGN", "Cantidad de combustible inconsistente al encender", -5);
            }
        }

        // ── TAX — Rodaje de salida ─────────────────────────────────────────────
        private void EvalTax(ref int score, List<ScoreEvent> violations)
        {
            // Muestras de taxi de salida: antes del despegue, en tierra, moviéndose
            var takeoffIdx = FindTakeoffIndex();
            var taxiSamples = _log
                .Take(takeoffIdx > 0 ? takeoffIdx : _log.Count)
                .Where(s => s.OnGround && s.GroundSpeed >= 3 && s.GroundSpeed <= 50)
                .ToList();
            if (taxiSamples.Count == 0) return;

            // TAX-01: Velocidad máxima 25 kts (SUR Air PDF)
            if (taxiSamples.Any(s => s.GroundSpeed > 25))
                Penalize(violations, ref score, "TAX-01", "TAX", "Velocidad de taxi superior a 25 kts", -6);

            // TAX-02: Luces TAXI encendidas
            if (taxiSamples.Count(s => !s.TaxiLightsOn) > taxiSamples.Count / 3)
                Penalize(violations, ref score, "TAX-02", "TAX", "Rodaje sin luces TAXI", -5);

            // TAX-03: Luces NAV encendidas en taxi
            if (taxiSamples.Count(s => !s.NavLightsOn) > taxiSamples.Count / 3)
                Penalize(violations, ref score, "TAX-03", "TAX", "Rodaje sin luces NAV", -4);

            // TAX-04: Luces BEACON encendidas en taxi (motores en marcha)
            if (taxiSamples.Count(s => !s.BeaconLightsOn) > taxiSamples.Count / 3)
                Penalize(violations, ref score, "TAX-04", "TAX", "Rodaje sin luces BEACON", -5);

            // TAX-05: Uso de reversores en plataforma
            if (taxiSamples.Any(s => s.ReverserActive))
                Penalize(violations, ref score, "TAX-05", "TAX", "Uso de reversores en plataforma/taxi", -8);

            // TAX-07: Strobes antes de entrar a pista (al menos en la última parte del taxi)
            var lateT = taxiSamples.Skip(taxiSamples.Count * 3 / 4).ToList();
            if (lateT.Count > 0 && lateT.Count(s => !s.StrobeLightsOn) > lateT.Count / 2)
                Penalize(violations, ref score, "TAX-07", "TAX", "Sin luces STROBE al aproximarse a pista", -5);
        }

        // ── TO — Despegue ─────────────────────────────────────────────────────
        private void EvalTo(ref int score, List<ScoreEvent> violations)
        {
            var rollSamples = _log
                .Where(s => (s.OnGround && s.GroundSpeed > 30) || (!s.OnGround && s.AltitudeAGL < 500 && s.IndicatedAirspeed > 60))
                .ToList();
            if (rollSamples.Count == 0) return;

            // TO-01: Flaps en despegue (política SUR Air)
            if (rollSamples.Count(s => !s.FlapsDeployed) > rollSamples.Count / 2)
                Penalize(violations, ref score, "TO-01", "TO", "Despegue sin flaps", -8);

            // TO-02: Luces LANDING en despegue
            if (rollSamples.Count(s => !s.LandingLightsOn) > rollSamples.Count / 3)
                Penalize(violations, ref score, "TO-02", "TO", "Despegue sin luces LANDING", -6);

            // TO-03: Luces STROBE en despegue
            if (rollSamples.Count(s => !s.StrobeLightsOn) > rollSamples.Count / 3)
                Penalize(violations, ref score, "TO-03", "TO", "Despegue sin luces STROBE", -6);

            // TO-04: Luces NAV en despegue
            if (rollSamples.Count(s => !s.NavLightsOn) > rollSamples.Count / 3)
                Penalize(violations, ref score, "TO-04", "TO", "Despegue sin luces NAV", -5);

            // TO-05: Luces BEACON en despegue
            if (rollSamples.Count(s => !s.BeaconLightsOn) > rollSamples.Count / 3)
                Penalize(violations, ref score, "TO-05", "TO", "Despegue sin luces BEACON", -5);

            // TO-06: Transponder Modo Charlie — solo si el perfil lo lee de forma confiable
            // (Transponder no es núcleo del reglaje final — Rule 15 matriz)
            if (_cabinSystemsReliable && rollSamples.Count(s => !s.TransponderCharlieMode) > rollSamples.Count / 3)
                Penalize(violations, ref score, "TO-06", "TO", "Transponder no en Modo C al despegar", -4);

            // TO-08: Señal de cinturones — solo si perfil confiable
            if (_cabinSystemsReliable && rollSamples.Count(s => !s.SeatBeltSign) > rollSamples.Count / 3)
                Penalize(violations, ref score, "TO-08", "TO", "Señal de cinturones apagada al despegar", -4);

            // TO-10: Retracción del tren — detectar si el tren tardó más de 30 seg en subirse
            var airSamples = _log.Where(s => !s.OnGround && s.AltitudeAGL > 50 && s.AltitudeAGL < 2000).ToList();
            if (airSamples.Count > 0)
            {
                var gearDown = airSamples.Count(s => s.GearDown);
                if (gearDown > airSamples.Count / 2)
                    Penalize(violations, ref score, "TO-10", "TO", "Tren no retraído oportunamente", -4);
            }
        }

        // ── ASC — Ascenso ─────────────────────────────────────────────────────
        private void EvalAsc(ref int score, List<ScoreEvent> violations)
        {
            var climbSamples = _log
                .Where(s => !s.OnGround && s.VerticalSpeed > 100 && s.AltitudeFeet > 500 && s.AltitudeFeet < 40000)
                .ToList();
            if (climbSamples.Count == 0) return;

            // ASC-01: Mantener LANDING abajo de 10,000 ft
            var below10k = climbSamples.Where(s => s.AltitudeFeet < 10000).ToList();
            if (below10k.Count > 0 && below10k.Count(s => !s.LandingLightsOn) > below10k.Count / 2)
                Penalize(violations, ref score, "ASC-01", "ASC", "Luces LANDING apagadas bajo 10,000 ft en ascenso", -5);

            // ASC-02: Apagar LANDING arriba de 10,000 ft
            var above10k = climbSamples.Where(s => s.AltitudeFeet > 10000).ToList();
            if (above10k.Count > 0 && above10k.Count(s => s.LandingLightsOn) > above10k.Count / 2)
                Penalize(violations, ref score, "ASC-02", "ASC", "Luces LANDING encendidas sobre 10,000 ft", -4);

            // ASC-03: Límite de velocidad 250 kts bajo FL100
            var below10kSpeed = climbSamples.Where(s => s.AltitudeFeet < 10000).ToList();
            if (below10kSpeed.Any(s => s.IndicatedAirspeed > 252))
                Penalize(violations, ref score, "ASC-03", "ASC", "Velocidad superior a 250 kts bajo 10,000 ft", -8);

            // ASC-04: Velocidad vertical excesiva (>4500 fpm es agresivo)
            if (climbSamples.Any(s => s.VerticalSpeed > 4500))
                Penalize(violations, ref score, "ASC-04", "ASC", "Velocidad vertical excesiva en ascenso", -4);

            // ASC (Beacon y Strobes en ascenso)
            if (climbSamples.Count(s => !s.BeaconLightsOn) > climbSamples.Count / 3)
                Penalize(violations, ref score, "ASC-05", "ASC", "Luces BEACON apagadas en ascenso", -4);
            if (climbSamples.Count(s => !s.StrobeLightsOn) > climbSamples.Count / 3)
                Penalize(violations, ref score, "ASC-06", "ASC", "Luces STROBE apagadas en ascenso", -4);
        }

        // ── CRU — Crucero ─────────────────────────────────────────────────────
        private void EvalCru(ref int score, List<ScoreEvent> violations)
        {
            var cruSamples = _log
                .Where(s => !s.OnGround && Math.Abs(s.VerticalSpeed) < 500 && s.AltitudeFeet > 8000)
                .ToList();
            if (cruSamples.Count == 0) return;

            // CRU-01: Altímetro estándar en crucero (QNH ~ 1013 hPa cuando aplica)
            var highCru = cruSamples.Where(s => s.AltitudeFeet > 18000).ToList();
            if (highCru.Count > 5 && highCru.Count(s => Math.Abs(s.QNH - 1013.25) > 2) > highCru.Count / 2)
                Penalize(violations, ref score, "CRU-01", "CRU", "Altímetro no en 1013 hPa en crucero alto", -6);

            // CRU-02: Pausa activa
            if (_log.Any(s => s.Pause))
                Penalize(violations, ref score, "CRU-02", "CRU", "Pausa activa durante el vuelo", -10);

            // CRU-03: Transponder — solo informativo si perfil no es confiable (Rule 15 matriz)
            if (_cabinSystemsReliable && cruSamples.Count(s => !s.TransponderCharlieMode) > cruSamples.Count / 3)
                Penalize(violations, ref score, "CRU-03", "CRU", "Transponder no en Modo C en crucero", -4);

            // CRU-04: Beacon encendido en crucero
            if (cruSamples.Count(s => !s.BeaconLightsOn) > cruSamples.Count / 3)
                Penalize(violations, ref score, "CRU-04", "CRU", "Luces BEACON apagadas en crucero", -5);

            // CRU-05: Strobes en crucero
            if (cruSamples.Count(s => !s.StrobeLightsOn) > cruSamples.Count / 3)
                Penalize(violations, ref score, "CRU-05", "CRU", "Luces STROBE apagadas en crucero", -4);

            // CRU-06: Banco excesivo en ruta (>45°)
            if (cruSamples.Any(s => Math.Abs(s.Bank) > 45))
                Penalize(violations, ref score, "CRU-06", "CRU", "Banco >45° en crucero", -5);

            // CRU-07: Señal de cinturones en turbulencia — solo si perfil confiable
            if (_cabinSystemsReliable && cruSamples.Count(s => Math.Abs(s.Bank) > 20 && !s.SeatBeltSign) > 5)
                Penalize(violations, ref score, "CRU-07", "CRU", "Sin señal de cinturones en maniobra/turbulencia", -3);
        }

        // ── DES — Descenso ────────────────────────────────────────────────────
        private void EvalDes(ref int score, List<ScoreEvent> violations)
        {
            var desSamples = _log
                .Where(s => !s.OnGround && s.VerticalSpeed < -100 && s.AltitudeFeet > 500)
                .ToList();
            if (desSamples.Count == 0) return;

            // DES-01: Encender LANDING abajo de 10,000 ft en descenso
            var below10k = desSamples.Where(s => s.AltitudeFeet < 10000).ToList();
            if (below10k.Count > 0 && below10k.Count(s => !s.LandingLightsOn) > below10k.Count / 2)
                Penalize(violations, ref score, "DES-01", "DES", "Luces LANDING apagadas bajo 10,000 ft en descenso", -5);

            // DES-02: Límite de velocidad 250 kts bajo FL100
            if (below10k.Any(s => s.IndicatedAirspeed > 252))
                Penalize(violations, ref score, "DES-02", "DES", "Velocidad superior a 250 kts bajo 10,000 ft", -8);

            // DES-03: Velocidad vertical excesiva en descenso (>3500 fpm)
            if (desSamples.Any(s => s.VerticalSpeed < -3500))
                Penalize(violations, ref score, "DES-03", "DES", "Velocidad vertical excesiva en descenso", -4);

            // DES-04: Presión de cabina crítica (si presurizado)
            if (_isPressurized)
            {
                var highCruise = _log.Where(s => !s.OnGround && s.AltitudeFeet > 10000).ToList();
                if (highCruise.Count > 0)
                {
                    var highCabin = highCruise.Count(s => s.CabinAltitudeFeet > 10000);
                    if (highCabin > highCruise.Count / 4)
                        Penalize(violations, ref score, "DES-04", "DES", "Presión de cabina crítica (>10,000 ft cabina)", -15);
                    else if (highCruise.Count(s => s.CabinAltitudeFeet > 8500) > highCruise.Count / 3)
                        Penalize(violations, ref score, "DES-05", "DES", "Altitud de cabina elevada (>8,500 ft)", -8);
                }
            }
        }

        // ── APP — Aproximación ────────────────────────────────────────────────
        private void EvalApp(ref int score, List<ScoreEvent> violations)
        {
            var appSamples = _log
                .Where(s => !s.OnGround && s.AltitudeAGL > 0 && s.AltitudeAGL < 3000 && s.IndicatedAirspeed < 240)
                .ToList();
            if (appSamples.Count == 0) return;

            // APP-01: Tren extendido en aproximación (bajo 1000 ft AGL)
            var lowApp = appSamples.Where(s => s.AltitudeAGL < 1000).ToList();
            if (lowApp.Count > 0 && lowApp.Count(s => !s.GearDown) > lowApp.Count / 2)
                Penalize(violations, ref score, "APP-01", "LDG", "Tren no extendido en aproximación final", -10);

            // APP-02: Flaps en aproximación final
            if (lowApp.Count > 0 && lowApp.Count(s => !s.FlapsDeployed && s.IndicatedAirspeed < 200) > lowApp.Count / 3)
                Penalize(violations, ref score, "APP-02", "LDG", "Sin flaps en aproximación final", -8);

            // APP-03: Luces LANDING en aproximación
            if (appSamples.Count(s => !s.LandingLightsOn) > appSamples.Count / 2)
                Penalize(violations, ref score, "APP-03", "LDG", "Luces LANDING apagadas en aproximación", -5);

            // APP-04: Señal de cinturones
            if (appSamples.Count(s => !s.SeatBeltSign) > appSamples.Count / 2)
                Penalize(violations, ref score, "APP-04", "LDG", "Señal de cinturones apagada en aproximación", -4);

            // APP-05: Aproximación no estabilizada bajo 1000 ft (VS >1000 fpm a baja altitud)
            if (lowApp.Count > 0 && lowApp.Count(s => s.VerticalSpeed < -1000) > lowApp.Count / 3)
                Penalize(violations, ref score, "APP-05", "LDG", "Aproximación no estabilizada bajo 1,000 ft", -8);
        }

        // ── LDG — Aterrizaje ──────────────────────────────────────────────────
        private void EvalLdg(ref int score, List<ScoreEvent> violations)
        {
            var vs = Math.Abs(_landingVS);

            // LDG luces antes del aterrizaje (app samples ya evaluados; aquí evaluamos el momento del toque)
            var touchSamples = _log
                .Where(s => s.OnGround && s.GroundSpeed > 20)
                .Take(20).ToList();

            if (touchSamples.Count > 0)
            {
                if (touchSamples.Count(s => !s.LandingLightsOn) > touchSamples.Count / 2)
                    Penalize(violations, ref score, "LDG-01", "LDG", "Luces LANDING apagadas al aterrizar", -5);
                if (touchSamples.Count(s => !s.NavLightsOn) > touchSamples.Count / 2)
                    Penalize(violations, ref score, "LDG-02", "LDG", "Luces NAV apagadas al aterrizar", -4);
                if (touchSamples.Count(s => !s.StrobeLightsOn) > touchSamples.Count / 2)
                    Penalize(violations, ref score, "LDG-03", "LDG", "Luces STROBE apagadas al aterrizar", -4);
                if (touchSamples.Count(s => !s.BeaconLightsOn) > touchSamples.Count / 2)
                    Penalize(violations, ref score, "LDG-04", "LDG", "Luces BEACON apagadas al aterrizar", -4);
                if (touchSamples.Count(s => !s.SeatBeltSign) > touchSamples.Count / 2)
                    Penalize(violations, ref score, "LDG-06", "LDG", "Señal de cinturones apagada al aterrizar", -4);
            }

            // TABLA OFICIAL Patagonia Wings (matriz reglaje final):
            //   0  a  -59 fpm → demasiado suave   (-5 proc)
            //  -60 a -180 fpm → perfecto           (bonif en perf, 0 proc)
            // -181 a -250 fpm → bueno              (neutral)
            // -251 a -500 fpm → duro               (-8 proc)
            // -501 a -700 fpm → mantenimiento      (-15 proc)
            //       < -701 fpm → accidentado        (-30 proc)
            if (vs > 700)
                Penalize(violations, ref score, "LDG-15", "LDG", $"Toque accidentado — aeronave a mantenimiento ({vs:F0} fpm)", -30);
            else if (vs > 500)
                Penalize(violations, ref score, "LDG-14", "LDG", $"Toque duro — requiere mantenimiento ({vs:F0} fpm)", -15);
            else if (vs > 250)
                Penalize(violations, ref score, "LDG-12", "LDG", $"Toque duro ({vs:F0} fpm)", -8);
            else if (vs < 60 && vs > 0)
                Penalize(violations, ref score, "LDG-09", "LDG", $"Toque demasiado suave ({vs:F0} fpm)", -5);
            // -60 a -180: perfecto → sin penalidad en procedimientos
            // -181 a -250: bueno → neutral

            // LDG-05: Altímetro — detectar si QNH era correcto (aprox. diferente a 1013 en arr.)
            var arrSamples = _log.Where(s => !s.OnGround && s.AltitudeAGL < 3000 && s.AltitudeFeet < 5000).ToList();
            if (arrSamples.Count > 5)
            {
                var avgQnh = arrSamples.Average(s => s.QNH);
                if (Math.Abs(avgQnh - 1013.25) < 1 && _arrivalElevationFt < 10000)
                {
                    // QNH nunca fue ajustado (sigue en estándar en destino)
                    Penalize(violations, ref score, "LDG-05", "LDG", "Altímetro en QNE (no ajustado para aterrizaje)", -8);
                }
            }
        }

        // ── TAG — Rodaje de llegada ────────────────────────────────────────────
        private void EvalTag(ref int score, List<ScoreEvent> violations)
        {
            // Muestras post-aterrizaje (después del toque, en tierra)
            var landingIdx = FindLandingIndex();
            var tagSamples = _log
                .Skip(landingIdx > 0 ? landingIdx : 0)
                .Where(s => s.OnGround && s.GroundSpeed >= 2 && s.GroundSpeed < 50)
                .ToList();
            if (tagSamples.Count == 0) return;

            // TAG-01: Apagar luces LANDING al salir de pista
            var slowTaxi = tagSamples.Where(s => s.GroundSpeed < 20).ToList();
            if (slowTaxi.Count > 0 && slowTaxi.Count(s => s.LandingLightsOn) > slowTaxi.Count / 2)
                Penalize(violations, ref score, "TAG-01", "TAG", "Luces LANDING encendidas en rodaje de llegada", -3);

            // TAG-02: Encender luces TAXI al rodar a gate
            if (tagSamples.Count(s => !s.TaxiLightsOn) > tagSamples.Count / 2)
                Penalize(violations, ref score, "TAG-02", "TAG", "Sin luces TAXI en rodaje de llegada", -4);

            // TAG-03: Flaps retractados al rodar
            if (slowTaxi.Count > 0 && slowTaxi.Count(s => s.FlapsDeployed) > slowTaxi.Count / 2)
                Penalize(violations, ref score, "TAG-03", "TAG", "Flaps no retractados en rodaje de llegada", -3);

            // TAG-04: Apagar luces STROBE al salir de pista
            if (slowTaxi.Count > 0 && slowTaxi.Count(s => s.StrobeLightsOn) > slowTaxi.Count / 2)
                Penalize(violations, ref score, "TAG-04", "TAG", "Luces STROBE encendidas en rodaje de llegada", -3);

            // TAG-05: Velocidad máxima 25 kts
            if (tagSamples.Any(s => s.GroundSpeed > 25))
                Penalize(violations, ref score, "TAG-05", "TAG", "Velocidad de rodaje de llegada >25 kts", -5);
        }

        // ── PAR — Parking / Estacionamiento ──────────────────────────────────
        private void EvalPar(ref int score, List<ScoreEvent> violations)
        {
            var parkSamples = _log
                .Where(s => s.OnGround && s.GroundSpeed < 2)
                .OrderBy(s => s.CapturedAtUtc)
                .ToList();
            if (parkSamples.Count == 0) return;

            var lastSamples = parkSamples.Skip(Math.Max(0, parkSamples.Count - 20)).ToList();

            // PAR-01: Transponder en Standby al estacionar (Modo C apagado)
            if (lastSamples.Count > 0 && lastSamples.Count(s => s.TransponderCharlieMode) > lastSamples.Count / 2)
                Penalize(violations, ref score, "PAR-01", "PAR", "Transponder no en Standby al estacionar", -4);

            // PAR-02: Freno de estacionamiento al detenerse
            if (lastSamples.Count > 0 && lastSamples.Count(s => !s.ParkingBrake) > lastSamples.Count / 2)
                Penalize(violations, ref score, "PAR-02", "PAR", "Sin freno de estacionamiento al estacionar", -5);

            // PAR-04: Beacon apagado (motores ya apagados)
            var engOff = lastSamples.Where(s => s.Engine1N1 < 5 && s.Engine2N1 < 5).ToList();
            if (engOff.Count > 0 && engOff.Count(s => s.BeaconLightsOn) > engOff.Count / 3)
                Penalize(violations, ref score, "PAR-04", "PAR", "Luces BEACON encendidas con motores apagados", -3);

            // PAR-12: Señal de cinturones apagada en parking
            if (lastSamples.Count > 0 && lastSamples.Count(s => s.SeatBeltSign) > lastSamples.Count / 2)
                Penalize(violations, ref score, "PAR-12", "PAR", "Señal de cinturones encendida en parking", -2);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  PARTE 2: PERFORMANCE
        // ══════════════════════════════════════════════════════════════════════

        // ── GEN — General ─────────────────────────────────────────────────────
        private void EvalPerfGeneral(ref int perf, List<ScoreEvent> violations, List<ScoreEvent> bonuses)
        {
            // GEN-01: Pausa en vuelo
            if (_log.Any(s => s.Pause))
                PenalizePerf(violations, ref perf, "GEN-01", "GEN", "Pausa activa durante el vuelo", -10);

            // GEN-02: Banco excesivo en vuelo
            var airSamples = _log.Where(s => !s.OnGround && s.AltitudeFeet > 500).ToList();
            if (airSamples.Any(s => Math.Abs(s.Bank) > 45))
                PenalizePerf(violations, ref perf, "GEN-02", "GEN", "Banco >45° en vuelo", -8);

            // GEN-03: Vuelo completado (bono base)
            if (_log.Count > 20)
                BonusPerf(bonuses, ref perf, "GEN-03", "GEN", "Vuelo completado satisfactoriamente", +15);

            // GEN-04: Activación de alarma de pérdida (stall)
            // (Se detecta cuando IndicatedAirspeed cae <60 kts en vuelo a más de 500 ft AGL)
            if (airSamples.Any(s => s.AltitudeAGL > 500 && s.IndicatedAirspeed < 60 && s.VerticalSpeed < -500))
                PenalizePerf(violations, ref perf, "GEN-04", "GEN", "Posible activación de alarma de pérdida (stall)", -15);

            // GEN-05: Exceso de velocidad (overspeed — IAS > 380 kts o MMO proxy)
            if (airSamples.Any(s => s.IndicatedAirspeed > 380))
                PenalizePerf(violations, ref perf, "GEN-05", "GEN", "Exceso de velocidad (OVERSPEED)", -12);
        }

        // ── TO-Perf — Maniobras de despegue ──────────────────────────────────
        private void EvalPerfTakeoff(ref int perf, List<ScoreEvent> violations, List<ScoreEvent> bonuses)
        {
            var rollSamples = _log
                .Where(s => (s.OnGround && s.GroundSpeed > 30) || (!s.OnGround && s.AltitudeFeet < 2000 && s.VerticalSpeed > 0))
                .ToList();
            if (rollSamples.Count == 0) return;

            // TO-Perf-01: Ascenso inicial robusto
            var climbInit = rollSamples.Where(s => !s.OnGround).ToList();
            if (climbInit.Count > 0)
            {
                var avgVs = climbInit.Average(s => s.VerticalSpeed);
                if (avgVs > 800)
                    BonusPerf(bonuses, ref perf, "TOP-01", "TO", $"Ascenso inicial enérgico ({avgVs:F0} fpm)", +5);
            }

            // TO-Perf-02: Ángulo de alabeo excesivo durante despegue (>30°)
            if (rollSamples.Any(s => Math.Abs(s.Bank) > 30))
                PenalizePerf(violations, ref perf, "TOP-02", "TO", "Ángulo de alabeo excesivo en despegue", -8);

            // TO-Perf-03: Ángulo de morro excesivo (pitch >20°)
            if (rollSamples.Any(s => s.Pitch > 20))
                PenalizePerf(violations, ref perf, "TOP-09", "TO", "Ángulo de morro excesivo en despegue", -5);

            // TO-Perf-04: Condiciones lluviosas en despegue (bono)
            var depRain = rollSamples.Where(s => s.OnGround).ToList();
            if (depRain.Count > 0 && depRain.Any(s => s.IsRaining))
                BonusPerf(bonuses, ref perf, "TOP-03", "TO", "Despegue en condiciones de lluvia", +5);

            // TO-Perf-06: Viento de cola en despegue (>7 kts)
            var tailwind = GetTailwindComponent(rollSamples);
            if (tailwind > 7)
                PenalizePerf(violations, ref perf, "TOP-06", "TO", $"Despegue con viento de cola ({tailwind:F0} kts)", -6);
            // TO-Perf-07: Viento cruzado en despegue (bono)
            else if (GetCrosswindComponent(rollSamples) > 5)
                BonusPerf(bonuses, ref perf, "TOP-07", "TO", "Despegue con viento cruzado", +5);

            // TO-Perf-08: Despegue manual (sin autopiloto)
            if (rollSamples.Where(s => !s.OnGround).All(s => !s.AutopilotActive))
                BonusPerf(bonuses, ref perf, "TOP-08", "TO", "Despegue manual sin piloto automático", +8);

            // TIE-01: Aeropuerto de alta altura (>5000 ft)
            if (_departureElevationFt > 5000)
                BonusPerf(bonuses, ref perf, "TIE-01", "TIE", "Operación en aeropuerto de alta elevación (salida)", +10);
        }

        // ── LDG-Perf — Maniobras de aterrizaje ───────────────────────────────
        private void EvalPerfLanding(ref int perf, List<ScoreEvent> violations, List<ScoreEvent> bonuses)
        {
            var vs = Math.Abs(_landingVS);

            // TABLA OFICIAL Patagonia Wings — Performance (mismos rangos que procedimientos):
            //   0  a  -59 fpm → suave    → penal
            //  -60 a -180 fpm → perfecto → bono +12
            // -181 a -250 fpm → bueno   → neutral
            // -251 a -500 fpm → duro    → penal -8
            // -501 a -700 fpm → mant.   → penal -20
            //       < -701 fpm → accident → penal -40
            if (vs > 700)
                PenalizePerf(violations, ref perf, "LDP-15", "LDG", $"Toque accidentado — aeronave AOG ({vs:F0} fpm)", -40);
            else if (vs > 500)
                PenalizePerf(violations, ref perf, "LDP-14", "LDG", $"Toque duro — mantenimiento ({vs:F0} fpm)", -20);
            else if (vs > 250)
                PenalizePerf(violations, ref perf, "LDP-12", "LDG", $"Toque duro ({vs:F0} fpm)", -8);
            else if (vs >= 60 && vs <= 180)
                BonusPerf(bonuses, ref perf, "LDP-10", "LDG", $"Toque perfecto ({vs:F0} fpm)", +12);
            else if (vs < 60 && vs > 0)
                PenalizePerf(violations, ref perf, "LDP-09", "LDG", $"Toque demasiado suave ({vs:F0} fpm)", -4);
            // -181 a -250: bueno → neutral (0 pts)

            // Fuerza G durante toque (SUR Air: 1.0-1.3G ideal, 1.3-1.6G aceptable, >1.6 requiere revisión, >2G peligroso)
            if (_landingG > 0)
            {
                if (_landingG <= 1.3)
                    BonusPerf(bonuses, ref perf, "LDP-18A", "LDG", $"Factor G excelente en toque ({_landingG:F2}G)", +5);
                else if (_landingG > 1.6 && _landingG <= 2.0)
                    PenalizePerf(violations, ref perf, "LDP-18B", "LDG", $"Factor G alto en toque ({_landingG:F2}G)", -8);
                else if (_landingG > 2.0)
                    PenalizePerf(violations, ref perf, "LDP-18C", "LDG", $"Factor G peligroso en toque ({_landingG:F2}G)", -15);
            }

            // Detección de condiciones durante aproximación/aterrizaje
            var appSamples = _log
                .Where(s => !s.OnGround && s.AltitudeFeet < 3000 && s.VerticalSpeed < 0)
                .ToList();

            // LDP-21: Aterrizaje sin piloto automático (< 2000 ft sin AP)
            var below2k = appSamples.Where(s => s.AltitudeAGL < 2000).ToList();
            if (below2k.Count > 5)
            {
                bool noAP = below2k.All(s => !s.AutopilotActive);
                if (noAP)
                    BonusPerf(bonuses, ref perf, "LDP-21", "LDG", "Aterrizaje manual sin piloto automático", +10);
            }

            // LDP-30: Pista mojada / lluvia en aterrizaje
            if (appSamples.Any(s => s.IsRaining))
                BonusPerf(bonuses, ref perf, "LDP-30", "LDG", "Aterrizaje bajo lluvia / pista mojada", +8);

            // LDP-29: Viento cruzado en aterrizaje
            var crossLdg = GetCrosswindComponent(appSamples);
            if (crossLdg > 5)
                BonusPerf(bonuses, ref perf, "LDP-29", "LDG", $"Aterrizaje con viento cruzado ({crossLdg:F0} kts)", +8);

            // LDP-28: Viento de cola en aterrizaje (>7 kts) → penalidad
            var tailLdg = GetTailwindComponent(appSamples);
            if (tailLdg > 7)
                PenalizePerf(violations, ref perf, "LDP-28", "LDG", $"Aterrizaje con viento de cola ({tailLdg:F0} kts)", -6);

            // LDP-02: Ángulo de alabeo excesivo en aproximación
            if (appSamples.Any(s => Math.Abs(s.Bank) > 30))
                PenalizePerf(violations, ref perf, "LDP-02", "LDG", "Ángulo de alabeo excesivo en aproximación", -6);

            // LDP-19: Aproximación no estabilizada (<1000 ft, VS < -1000 fpm sostenida)
            var low = appSamples.Where(s => s.AltitudeAGL < 1000).ToList();
            if (low.Count > 3 && low.Count(s => s.VerticalSpeed < -1000) > low.Count / 3)
                PenalizePerf(violations, ref perf, "LDP-19", "LDG", "Aproximación no estabilizada bajo 1,000 ft", -10);

            // LDP-08: Full flaps sin tren abajo
            if (appSamples.Any(s => s.FlapsPercent > 80 && !s.GearDown))
                PenalizePerf(violations, ref perf, "LDP-08", "LDG", "Full flaps desplegados sin tren de aterrizaje", -6);

            // TIE-01: Aeropuerto de alta altura en llegada
            if (_arrivalElevationFt > 5000)
                BonusPerf(bonuses, ref perf, "TIE-01B", "TIE", "Aterrizaje en aeropuerto de alta elevación", +10);

            // Condiciones IMC: si llegó con nubes bajas/niebla (proxy: IsRaining + VS < -500 sostenida en final)
            if (appSamples.Any(s => s.IsRaining) && low.Count > 3 && low.Average(s => s.IndicatedAirspeed) < 180)
                BonusPerf(bonuses, ref perf, "LDP-03", "LDG", "Aproximación/aterrizaje en condiciones IMC", +6);
        }

        // ── PLN — Planificación ───────────────────────────────────────────────
        private void EvalPerfPlan(ref int perf, List<ScoreEvent> violations, List<ScoreEvent> bonuses)
        {
            if (_log.Count == 0) return;

            // PLN-02: Bono por duración del vuelo
            var flightTime = _log.Last().CapturedAtUtc - _log.First().CapturedAtUtc;
            double hours = flightTime.TotalHours;
            if (hours >= 6)
                BonusPerf(bonuses, ref perf, "PLN-02", "PLN", $"Vuelo largo de {hours:F1}h — gran dedicación", +15);
            else if (hours >= 3)
                BonusPerf(bonuses, ref perf, "PLN-02", "PLN", $"Vuelo de {hours:F1}h — buena duración", +8);
            else if (hours >= 1)
                BonusPerf(bonuses, ref perf, "PLN-02", "PLN", $"Vuelo de {hours:F1}h completado", +4);

            // PLN-04: Consumo eficiente — detectar si el flujo de combustible fue bajo
            var airSamples = _log.Where(s => !s.OnGround && s.FuelFlowLbsHour > 0).ToList();
            if (airSamples.Count > 10)
            {
                var avgFlow = airSamples.Average(s => s.FuelFlowLbsHour);
                if (avgFlow < 500) // flujo muy eficiente (turbohélices)
                    BonusPerf(bonuses, ref perf, "PLN-04", "PLN", "Consumo de combustible menor al promedio", +8);
            }

            // PLN-06: Combustible final bajo mínimo (< 5% de capacidad al aterrizar)
            var lastSamples = _log.Where(s => s.OnGround).OrderByDescending(s => s.CapturedAtUtc).Take(10).ToList();
            if (lastSamples.Count > 0)
            {
                var finalFuel = lastSamples.Average(s => s.FuelKg);
                var capacity  = _log.Max(s => s.FuelKg);
                if (capacity > 0 && finalFuel / capacity < 0.05 && finalFuel < 100)
                    PenalizePerf(violations, ref perf, "PLN-06", "PLN", "Combustible final bajo el mínimo legal", -15);
            }
        }

        // ── CRU-Perf ──────────────────────────────────────────────────────────
        private void EvalPerfCruise(ref int perf, List<ScoreEvent> bonuses)
        {
            var cruSamples = _log
                .Where(s => !s.OnGround && Math.Abs(s.VerticalSpeed) < 500 && s.AltitudeFeet > 8000)
                .ToList();
            if (cruSamples.Count < 5) return;

            // Bono por altitud estable en crucero
            var stableSamples = cruSamples.Count(s => Math.Abs(s.VerticalSpeed) < 100);
            if (stableSamples > cruSamples.Count * 2 / 3)
                BonusPerf(bonuses, ref perf, "CRU-01", "CRU", "Altitud de crucero muy estable", +4);
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Helpers de índice
        // ══════════════════════════════════════════════════════════════════════

        /// <summary>Encuentra el índice del primer sample de despegue (primer sample airborne).</summary>
        private int FindTakeoffIndex()
        {
            for (int i = 1; i < _log.Count; i++)
                if (!_log[i].OnGround && _log[i - 1].OnGround && _log[i].GroundSpeed > 20)
                    return i;
            return -1;
        }

        /// <summary>Encuentra el índice del primer sample de aterrizaje (transición air→ground).</summary>
        private int FindLandingIndex()
        {
            for (int i = _log.Count - 1; i > 0; i--)
                if (_log[i].OnGround && !_log[i - 1].OnGround)
                    return i;
            return -1;
        }

        /// <summary>Componente de viento de cola en kts (>0 = tailwind) para un conjunto de muestras.</summary>
        private static double GetTailwindComponent(IList<SimData> samples)
        {
            if (samples == null || samples.Count == 0) return 0;
            var valid = samples.Where(s => s.WindSpeed > 0).ToList();
            if (valid.Count == 0) return 0;

            double tw = 0;
            foreach (var s in valid)
            {
                double windAngle = (s.WindDirection - s.Heading + 360) % 360;
                double rad = windAngle * Math.PI / 180.0;
                tw += s.WindSpeed * Math.Cos(rad); // positivo = headwind (viento de frente)
            }
            // Invertido: tailwind positivo = viento desde atrás
            return -tw / valid.Count;
        }

        /// <summary>Componente de viento cruzado en kts para un conjunto de muestras.</summary>
        private static double GetCrosswindComponent(IList<SimData> samples)
        {
            if (samples == null || samples.Count == 0) return 0;
            var valid = samples.Where(s => s.WindSpeed > 0).ToList();
            if (valid.Count == 0) return 0;

            double cw = 0;
            foreach (var s in valid)
            {
                double windAngle = (s.WindDirection - s.Heading + 360) % 360;
                double rad = windAngle * Math.PI / 180.0;
                cw += Math.Abs(s.WindSpeed * Math.Sin(rad));
            }
            return cw / valid.Count;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Helpers de puntuación
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

        // ── Calificaciones exactas según Guía SUR Air v5.0 ────────────────────

        private static string ProcGrade(int score)
        {
            if (score >= 100) return "★★★ Excelente";
            if (score >= 90)  return "★★ Satisfactorio";
            if (score >= 80)  return "★ Marginal";
            return "Insatisfactorio";
        }

        private static string PerfGrade(int score)
        {
            if (score >= 100) return "★★★ Excelente";
            if (score >= 60)  return "★★ Satisfactorio";
            if (score >= 30)  return "★ Marginal";
            return "Insatisfactorio";
        }
    }
}
