using System;

namespace PatagoniaWings.Acars.Core.Models
{
    public sealed class CharterDispatchContext
    {
        public string FlightModeCode { get; set; }
        public string PilotCallsign { get; set; }
        public string ReservationId { get; set; }
        public string AircraftId { get; set; }
        public string AircraftRegistration { get; set; }
        public string AircraftTypeCode { get; set; }
        public string OriginIcao { get; set; }
        public string DestinationIcao { get; set; }
        public DateTime? ScheduledDepartureUtc { get; set; }
        public bool RealWeatherRequired { get; set; }
        public bool MovePilotOnClose { get; set; }
        public bool MoveAircraftOnClose { get; set; }

        public CharterDispatchContext()
        {
            FlightModeCode = "CHARTER";
            PilotCallsign = string.Empty;
            ReservationId = string.Empty;
            AircraftId = string.Empty;
            AircraftRegistration = string.Empty;
            AircraftTypeCode = string.Empty;
            OriginIcao = string.Empty;
            DestinationIcao = string.Empty;
            RealWeatherRequired = true;
            MovePilotOnClose = true;
            MoveAircraftOnClose = true;
        }
    }
}
