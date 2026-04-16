using System;

namespace PatagoniaWings.Acars.Core.Models
{
    public sealed class AcarsReadyFlight
    {
        public string ReservationId { get; set; } = string.Empty;
        public string DispatchPackageId { get; set; } = string.Empty;
        public string PilotCallsign { get; set; } = string.Empty;
        public string PilotUserId { get; set; } = string.Empty;
        public string RankCode { get; set; } = string.Empty;
        public string CareerRankCode { get; set; } = string.Empty;
        public string BaseHubCode { get; set; } = string.Empty;
        public string CurrentAirportCode { get; set; } = string.Empty;
        public string FlightModeCode { get; set; } = string.Empty;
        public string RouteCode { get; set; } = string.Empty;
        public string FlightNumber { get; set; } = string.Empty;
        public string FlightDesignator { get; set; } = string.Empty;
        public string OriginIdent { get; set; } = string.Empty;
        public string DestinationIdent { get; set; } = string.Empty;
        public string AircraftId { get; set; } = string.Empty;
        public string AircraftRegistration { get; set; } = string.Empty;
        public string AircraftTypeCode { get; set; } = string.Empty;
        public string AircraftDisplayName { get; set; } = string.Empty;
        public string AircraftVariantCode { get; set; } = string.Empty;
        public string AddonProvider { get; set; } = string.Empty;
        public string RouteText { get; set; } = string.Empty;
        public int? PlannedAltitude { get; set; }
        public int? PlannedSpeed { get; set; }
        public string CruiseLevel { get; set; } = string.Empty;
        public string AlternateIcao { get; set; } = string.Empty;
        public string DispatchToken { get; set; } = string.Empty;
        public string SimbriefUsername { get; set; } = string.Empty;
        public string Remarks { get; set; } = string.Empty;
        public DateTime? ScheduledDepartureUtc { get; set; }
        public bool ReadyForAcars { get; set; }
        public string SimbriefStatus { get; set; } = string.Empty;
        public string ReservationStatus { get; set; } = string.Empty;
        public string DispatchStatus { get; set; } = string.Empty;
        public int PassengerCount { get; set; }
        public double CargoKg { get; set; }
        public double FuelPlannedKg { get; set; }
        public double PayloadKg { get; set; }
        public double ZeroFuelWeightKg { get; set; }
        public int ScheduledBlockMinutes { get; set; }
        public int ExpectedBlockP50Minutes { get; set; }
        public int ExpectedBlockP80Minutes { get; set; }

        public PreparedDispatch ToPreparedDispatch()
        {
            var flightDesignator = string.IsNullOrWhiteSpace(FlightDesignator)
                ? FlightNumber
                : FlightDesignator;

            return new PreparedDispatch
            {
                ReservationId = ReservationId,
                DispatchId = DispatchPackageId,
                DispatchToken = DispatchToken,
                PilotUserId = PilotUserId,
                RankCode = RankCode,
                CareerRankCode = CareerRankCode,
                BaseHubCode = BaseHubCode,
                CurrentAirportCode = CurrentAirportCode,
                FlightNumber = string.IsNullOrWhiteSpace(FlightNumber) ? flightDesignator : FlightNumber,
                FlightDesignator = flightDesignator,
                RouteCode = RouteCode,
                DepartureIcao = OriginIdent,
                ArrivalIcao = DestinationIdent,
                AlternateIcao = AlternateIcao,
                AircraftId = AircraftId,
                AircraftIcao = AircraftTypeCode,
                AircraftRegistration = AircraftRegistration,
                AircraftDisplayName = AircraftDisplayName,
                AircraftVariantCode = AircraftVariantCode,
                AddonProvider = AddonProvider,
                RouteText = RouteText,
                FlightMode = FlightModeCode,
                ReservationStatus = ReservationStatus,
                DispatchPackageStatus = DispatchStatus,
                SimbriefStatus = SimbriefStatus,
                SimbriefUsername = SimbriefUsername,
                CruiseLevel = CruiseLevel,
                Remarks = Remarks,
                PassengerCount = PassengerCount,
                CargoKg = CargoKg,
                FuelPlannedKg = FuelPlannedKg,
                PayloadKg = PayloadKg,
                ZeroFuelWeightKg = ZeroFuelWeightKg,
                ScheduledBlockMinutes = ScheduledBlockMinutes,
                ExpectedBlockP50Minutes = ExpectedBlockP50Minutes,
                ExpectedBlockP80Minutes = ExpectedBlockP80Minutes,
                ScheduledDepartureUtc = ScheduledDepartureUtc
            };
        }
    }
}
