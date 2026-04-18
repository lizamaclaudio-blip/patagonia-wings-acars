using System;
using System.Collections.Generic;

namespace PatagoniaWings.Acars.Core.Models
{
    public class AircraftDamageEvent
    {
        public string AircraftId { get; set; } = string.Empty;
        public string ReservationId { get; set; } = string.Empty;
        public string EventCode { get; set; } = string.Empty;
        public string Phase { get; set; } = "unknown";
        public string Severity { get; set; } = "medium";
        public Dictionary<string, object> Details { get; set; } = new Dictionary<string, object>();
        public DateTime CapturedAtUtc { get; set; } = DateTime.UtcNow;

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(AircraftId) &&
            !string.IsNullOrWhiteSpace(EventCode) &&
            !string.IsNullOrWhiteSpace(Phase);
    }

    public class AircraftDamageProfileSettings
    {
        public string ProfileCode { get; set; } = "GENERIC";
        public double MaxTaxiSpeedKts { get; set; } = 25;
        public double MaxTurnTaxiSpeedKts { get; set; } = 15;
        public double HardLandingFpm { get; set; } = -400;
        public double SevereHardLandingFpm { get; set; } = -700;
        public double MinorEngineN1Pct { get; set; } = 102;
        public double MajorEngineN1Pct { get; set; } = 105;
        public double MinorOverspeedKts { get; set; } = 280;
        public double MajorOverspeedKts { get; set; } = 320;
    }

    public class AircraftDamageSubmissionResult
    {
        public int BaseWearCalls { get; set; }
        public int DamageEventsSubmitted { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; } = string.Empty;
        public List<string> Errors { get; set; } = new List<string>();
    }
}
