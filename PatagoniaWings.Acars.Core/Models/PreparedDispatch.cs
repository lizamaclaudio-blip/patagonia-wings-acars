namespace PatagoniaWings.Acars.Core.Models
{
    public class PreparedDispatch
    {
        public string ReservationId { get; set; } = string.Empty;
        public string DispatchId { get; set; } = string.Empty;
        public string DispatchToken { get; set; } = string.Empty;
        public string PilotUserId { get; set; } = string.Empty;
        public string RankCode { get; set; } = string.Empty;
        public string CareerRankCode { get; set; } = string.Empty;
        public string BaseHubCode { get; set; } = string.Empty;
        public string CurrentAirportCode { get; set; } = string.Empty;
        public string FlightNumber { get; set; } = string.Empty;
        public string FlightDesignator { get; set; } = string.Empty;
        public string RouteCode { get; set; } = string.Empty;
        public string DepartureIcao { get; set; } = string.Empty;
        public string ArrivalIcao { get; set; } = string.Empty;
        public string AlternateIcao { get; set; } = string.Empty;
        public string AircraftId { get; set; } = string.Empty;
        public string AircraftIcao { get; set; } = string.Empty;
        public string AircraftRegistration { get; set; } = string.Empty;
        public string AircraftDisplayName { get; set; } = string.Empty;
        public string AircraftVariantCode { get; set; } = string.Empty;
        public string AddonProvider { get; set; } = string.Empty;
        public string RouteText { get; set; } = string.Empty;
        public string FlightMode { get; set; } = string.Empty;
        public string ReservationStatus { get; set; } = string.Empty;
        public string DispatchPackageStatus { get; set; } = string.Empty;
        public string SimbriefStatus { get; set; } = string.Empty;
        public string SimbriefUsername { get; set; } = string.Empty;
        public string CruiseLevel { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
        public System.DateTime? ScheduledDepartureUtc { get; set; }
        public int PassengerCount { get; set; }
        public double CargoKg { get; set; }
        public double FuelPlannedKg { get; set; }
        public double PayloadKg { get; set; }
        public double ZeroFuelWeightKg { get; set; }
        public int ScheduledBlockMinutes { get; set; }
        public int ExpectedBlockP50Minutes { get; set; }
        public int ExpectedBlockP80Minutes { get; set; }

        public string FlightNumberDisplay
        {
            get
            {
                return !string.IsNullOrWhiteSpace(FlightDesignator)
                    ? FlightDesignator
                    : FlightNumber;
            }
        }

        public bool HasAssignedAircraft
        {
            get
            {
                return !string.IsNullOrWhiteSpace(AircraftId)
                    || !string.IsNullOrWhiteSpace(AircraftRegistration)
                    || !string.IsNullOrWhiteSpace(AircraftIcao);
            }
        }

        public bool IsDispatchReady
        {
            get
            {
                var reservationStatus = (ReservationStatus ?? string.Empty).Trim().ToLowerInvariant();
                var dispatchStatus = (DispatchPackageStatus ?? string.Empty).Trim().ToLowerInvariant();

                var reservationReady =
                    // Oficiales
                    reservationStatus == "dispatched"
                    || reservationStatus == "in_progress"

                    // Legacy, solo para no romper reservas antiguas
                    || reservationStatus == "dispatch_ready"
                    || reservationStatus == "in_flight"

                    // Alias tolerantes históricos
                    || reservationStatus == "despacho"
                    || reservationStatus == "despacho_ready"
                    || reservationStatus == "active"
                    || reservationStatus == "confirmed"
                    || reservationStatus == "booked"
                    || reservationStatus == "ready";

                var packageReady =
                    string.IsNullOrWhiteSpace(dispatchStatus)
                    || dispatchStatus == "prepared"
                    || dispatchStatus == "dispatched"
                    || dispatchStatus == "released"
                    || dispatchStatus == "ready"
                    || dispatchStatus == "validated";

                return reservationReady && packageReady && HasAssignedAircraft;
            }
        }
    }
}
