using System;
using System.Collections.Generic;
using PatagoniaWings.Acars.Core.Enums;

namespace PatagoniaWings.Acars.Core.Models
{
    /// <summary>
    /// Evento de scoring individual: penalización o bonificación detectada por el evaluador.
    /// </summary>
    public class ScoreEvent
    {
        /// <summary>Código de referencia, p.ej. "LDG-06", "TAX-02", "GEN-01".</summary>
        public string Code { get; set; } = string.Empty;
        /// <summary>Fase del vuelo: PRE, IGN, TAX, TO, ASC, CRU, DES, LDG, TAG, PAR, GEN, PLN, TIE.</summary>
        public string Phase { get; set; } = string.Empty;
        /// <summary>Descripción legible del evento.</summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>Puntos aplicados: negativo = penalización, positivo = bonificación.</summary>
        public int Points { get; set; }
    }

    public class FlightReport
    {
        public int Id { get; set; }
        public string ReservationId { get; set; } = string.Empty;
        public string ResultUrl { get; set; } = string.Empty;
        public string ResultStatus { get; set; } = string.Empty;
        public string FlightNumber { get; set; } = string.Empty;
        public string PilotCallSign { get; set; } = string.Empty;
        public string DepartureIcao { get; set; } = string.Empty;
        public string ArrivalIcao { get; set; } = string.Empty;
        public string AircraftIcao { get; set; } = string.Empty;
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public DateTime BlockOutTimeUtc { get; set; }
        public DateTime TakeoffTimeUtc { get; set; }
        public DateTime TouchdownTimeUtc { get; set; }
        public TimeSpan Duration => ArrivalTime - DepartureTime;
        public double Distance { get; set; }
        public double FuelUsed { get; set; }
        public double LandingVS { get; set; }
        public double LandingG { get; set; }

        /// <summary>Proyeccion legacy transicional del score canonico. No es fuente de verdad.</summary>
        public int Score { get; set; }
        /// <summary>Proyeccion legacy transicional de la nota canonica. No es fuente de verdad.</summary>
        public string Grade { get; set; } = string.Empty;
        public string ProceduralSummary { get; set; } = string.Empty;
        public SimulatorType Simulator { get; set; }
        public string Remarks { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public FlightStatus Status { get; set; }
        /// <summary>Alias legacy mantenido solo por compatibilidad descendente.</summary>
        public int PointsEarned { get; set; }

        // Estadísticas de vuelo extendidas
        public double MaxAltitudeFeet { get; set; }
        public double MaxSpeedKts { get; set; }
        public double ApproachQnhHpa { get; set; }

        // Desglose legado por fase (para compatibilidad con Supabase)
        public int LandingPenalty { get; set; }
        public int TaxiPenalty { get; set; }
        public int AirbornePenalty { get; set; }
        public int ApproachPenalty { get; set; }
        public int CabinPenalty { get; set; }

        // ── SUR Air Dual Scoring ─────────────────────────────────────────────
        /// <summary>Score de procedimientos: 0-100 (empieza en 100, solo descuenta).</summary>
        public int ProcedureScore { get; set; }
        /// <summary>Score de performance: 0-100 (empieza en base 60, sube o baja).</summary>
        public int PerformanceScore { get; set; }
        /// <summary>Alias legacy de PatagoniaScore mantenido mientras exista consumo antiguo.</summary>
        public int MissionScore { get; set; }
        /// <summary>Calificación de procedimientos: ★★★ Excelente / ★★ Satisfactorio / ★ Marginal / INSATISFACTORIO.</summary>
        public string ProcedureGrade { get; set; } = string.Empty;
        /// <summary>Calificación de performance: ★★★ Excelente / ★★ Satisfactorio / ★ Marginal / INSATISFACTORIO.</summary>
        public string PerformanceGrade { get; set; } = string.Empty;
        /// <summary>Lista de infracciones detectadas con código, fase, descripción y puntos.</summary>
        public List<ScoreEvent> Violations { get; set; } = new List<ScoreEvent>();
        /// <summary>Lista de bonificaciones otorgadas con código, fase, descripción y puntos.</summary>
        public List<ScoreEvent> Bonuses { get; set; } = new List<ScoreEvent>();

        // Perfil del piloto (para mostrar en PostFlight)
        public string PilotQualifications { get; set; } = string.Empty;
        public string PilotCertifications { get; set; } = string.Empty;

        // Contrato nuevo ACARS -> web
        public int PatagoniaScore { get; set; }
        public string PatagoniaGrade { get; set; } = string.Empty;
        public PatagoniaEvaluationReport Evaluation { get; set; } = new PatagoniaEvaluationReport();

        /// <summary>
        /// Proyecta el contrato canonico actual a los campos legacy que aun consumen integraciones antiguas.
        /// No debe usarse al reves.
        /// </summary>
        public void ApplyLegacyScoreProjection()
        {
            Score = PatagoniaScore;
            Grade = PatagoniaGrade;
            MissionScore = PatagoniaScore;
            PointsEarned = ProcedureScore;
        }
    }

    public enum FlightStatus
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2
    }
}
