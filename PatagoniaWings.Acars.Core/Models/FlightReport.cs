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
        public SimulatorType Simulator { get; set; }
        public string Remarks { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public FlightStatus Status { get; set; }
        public int PointsEarned { get; set; }
    }

    public enum FlightStatus
    {
        Pending = 0,
        Approved = 1,
        Rejected = 2
    }
}
