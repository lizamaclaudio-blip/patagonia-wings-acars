using System;
using PatagoniaWings.Acars.Core.Enums;

namespace PatagoniaWings.Acars.Core.Models
{
    public class FlightReport
    {
        public int Id { get; set; }
        public string FlightNumber { get; set; } = string.Empty;
        public string PilotCallSign { get; set; } = string.Empty;
        public string DepartureIcao { get; set; } = string.Empty;
        public string ArrivalIcao { get; set; } = string.Empty;
        public string AircraftIcao { get; set; } = string.Empty;
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public TimeSpan Duration => ArrivalTime - DepartureTime;
        public double Distance { get; set; }
        public double FuelUsed { get; set; }
        public double LandingVS { get; set; }
        public double LandingG { get; set; }
        public int Score { get; set; }
        public string Grade { get; set; } = string.Empty;
        public string ProceduralSummary { get; set; } = string.Empty;
        public SimulatorType Simulator { get; set; }
        public string Remarks { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public FlightStatus Status { get; set; }
        public int PointsEarned { get; set; }

        // Estadísticas de vuelo extendidas
        public double MaxAltitudeFeet { get; set; }
        public double MaxSpeedKts { get; set; }
        public double ApproachQnhHpa { get; set; }

        // Desglose de penalizaciones por fase (valores negativos o cero)
        public int LandingPenalty { get; set; }
        public int TaxiPenalty { get; set; }
        public int AirbornePenalty { get; set; }
        public int ApproachPenalty { get; set; }
        public int CabinPenalty { get; set; }

        // Perfil del piloto (para mostrar en PostFlight)
        public string PilotQualifications { get; set; } = string.Empty;
        public string PilotCertifications { get; set; } = string.Empty;
    }

    public enum FlightStatus
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2
    }
}
