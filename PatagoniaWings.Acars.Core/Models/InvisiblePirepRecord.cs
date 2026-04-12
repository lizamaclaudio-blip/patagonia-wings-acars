using System;
using System.Collections.Generic;

namespace PatagoniaWings.Acars.Core.Models
{
    public class InvisiblePirepRecord
    {
        public string Id { get; set; } = string.Empty;
        public string ReservationId { get; set; } = string.Empty;
        public string PilotCallsign { get; set; } = string.Empty;
        public string FlightNumber { get; set; } = string.Empty;
        public string DepartureIcao { get; set; } = string.Empty;
        public string ArrivalIcao { get; set; } = string.Empty;
        public string AircraftIcao { get; set; } = string.Empty;
        public string AircraftRegistration { get; set; } = string.Empty;
        public string Simulator { get; set; } = string.Empty;
        public string RouteText { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
        public string Visibility { get; set; } = "hidden";
        public DateTime CreatedAtUtc { get; set; }
        public DateTime CompletedAtUtc { get; set; }
        public FlightReport Report { get; set; } = new FlightReport();
        public List<SimData> TelemetrySamples { get; set; } = new List<SimData>();
    }
}
