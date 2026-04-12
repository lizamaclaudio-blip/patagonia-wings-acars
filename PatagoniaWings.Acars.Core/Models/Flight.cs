using System;
using PatagoniaWings.Acars.Core.Enums;

namespace PatagoniaWings.Acars.Core.Models
{
    public class Flight
    {
        public string ReservationId { get; set; } = string.Empty;
        public string DispatchPackageId { get; set; } = string.Empty;
        public string AircraftId { get; set; } = string.Empty;
        public string FlightNumber { get; set; } = string.Empty;
        public string DepartureIcao { get; set; } = string.Empty;
        public string ArrivalIcao { get; set; } = string.Empty;
        public string AircraftIcao { get; set; } = string.Empty;
        public string AircraftTypeCode { get; set; } = string.Empty;
        public string AircraftName { get; set; } = string.Empty;
        public string AircraftDisplayName { get; set; } = string.Empty;
        public string AircraftVariantCode { get; set; } = string.Empty;
        public string AddonProvider { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public string FlightModeCode { get; set; } = string.Empty;
        public string RouteCode { get; set; } = string.Empty;
        public int PlannedAltitude { get; set; }
        public int PlannedSpeed { get; set; }
        public FlightPhase Phase { get; set; }
        public SimulatorType Simulator { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double BlockFuel { get; set; }
        public double ZeroFuelWeight { get; set; }
        public double MaxLandingWeight { get; set; }
        public string Remarks { get; set; } = string.Empty;
        public bool IsCharter { get; set; }
    }
}
