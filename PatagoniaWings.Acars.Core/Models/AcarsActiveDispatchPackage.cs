using System;

namespace PatagoniaWings.Acars.Core.Models
{
    public sealed class AcarsActiveDispatchPackage
    {
        public bool Ok { get; set; }
        public string Error { get; set; }

        public string ReservationId { get; set; }
        public string ReservationCode { get; set; }
        public string PilotCallsign { get; set; }

        public string FlightModeCode { get; set; }
        public string FlightNumber { get; set; }
        public string RouteCode { get; set; }

        public string OriginIcao { get; set; }
        public string DestinationIcao { get; set; }

        public string AircraftId { get; set; }
        public string AircraftRegistration { get; set; }
        public string AircraftTypeCode { get; set; }

        public DateTime? ScheduledDepartureUtc { get; set; }
        public string Status { get; set; }

        public bool RealWeatherRequired { get; set; }
        public bool MovePilotOnClose { get; set; }
        public bool MoveAircraftOnClose { get; set; }

        public AcarsActiveDispatchPackage()
        {
            Ok = false;
            Error = null;
            ReservationId = string.Empty;
            ReservationCode = string.Empty;
            PilotCallsign = string.Empty;
            FlightModeCode = string.Empty;
            FlightNumber = string.Empty;
            RouteCode = string.Empty;
            OriginIcao = string.Empty;
            DestinationIcao = string.Empty;
            AircraftId = string.Empty;
            AircraftRegistration = string.Empty;
            AircraftTypeCode = string.Empty;
            Status = string.Empty;
            RealWeatherRequired = false;
            MovePilotOnClose = false;
            MoveAircraftOnClose = false;
        }
    }
}
