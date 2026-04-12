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

        public bool IsDispatchReady
        {
            get
            {
                var reservationReady =
                    ReservationStatus == "dispatch_ready"
                    || ReservationStatus == "dispatched"
                    || ReservationStatus == "in_progress"
                    || ReservationStatus == "in_flight";
                var packageReady =
                    string.IsNullOrWhiteSpace(DispatchPackageStatus)
                    || DispatchPackageStatus == "prepared"
                    || DispatchPackageStatus == "dispatched"
                    || DispatchPackageStatus == "released"
                    || DispatchPackageStatus == "ready"
                    || DispatchPackageStatus == "validated";
                return reservationReady && packageReady;
            }
        }
    }
}
