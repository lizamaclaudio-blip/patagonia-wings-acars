using System;
using System.Collections.Generic;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    public static class DamageRuleMapper
    {
        public static AircraftDamageProfileSettings CreateProfile(string aircraftTypeCode, string aircraftTitle = "")
        {
            var code = (aircraftTypeCode ?? string.Empty).Trim().ToUpperInvariant();
            var title = (aircraftTitle ?? string.Empty).Trim().ToUpperInvariant();

            if (code.Contains("C208") || title.Contains("CARAVAN") || title.Contains("C208"))
            {
                return new AircraftDamageProfileSettings
                {
                    ProfileCode = "C208",
                    MaxTaxiSpeedKts = 20,
                    MaxTurnTaxiSpeedKts = 12,
                    HardLandingFpm = -350,
                    SevereHardLandingFpm = -600,
                    MinorEngineN1Pct = 101,
                    MajorEngineN1Pct = 104,
                    MinorOverspeedKts = 190,
                    MajorOverspeedKts = 205
                };
            }

            if (code.Contains("B350") || code.Contains("BE58") || title.Contains("KING AIR") || title.Contains("BARON"))
            {
                return new AircraftDamageProfileSettings
                {
                    ProfileCode = "BEECH",
                    MaxTaxiSpeedKts = 20,
                    MaxTurnTaxiSpeedKts = 12,
                    HardLandingFpm = -320,
                    SevereHardLandingFpm = -520,
                    MinorEngineN1Pct = 102,
                    MajorEngineN1Pct = 105,
                    MinorOverspeedKts = 240,
                    MajorOverspeedKts = 260
                };
            }

            if (code.Contains("A319") || code.Contains("A320") || code.Contains("A321") ||
                code.Contains("B737") || code.Contains("B738") || code.Contains("B739") ||
                title.Contains("AIRBUS A319") || title.Contains("AIRBUS A320") || title.Contains("AIRBUS A321") ||
                title.Contains("BOEING 737"))
            {
                return new AircraftDamageProfileSettings
                {
                    ProfileCode = "AIRLINER_NB",
                    MaxTaxiSpeedKts = 25,
                    MaxTurnTaxiSpeedKts = 15,
                    HardLandingFpm = -400,
                    SevereHardLandingFpm = -700,
                    MinorEngineN1Pct = 103,
                    MajorEngineN1Pct = 106,
                    MinorOverspeedKts = 300,
                    MajorOverspeedKts = 335
                };
            }

            return new AircraftDamageProfileSettings();
        }

        public static AircraftDamageEvent? MapTaxiOverspeed(
            string aircraftId,
            string reservationId,
            AircraftDamageProfileSettings profile,
            double observedTaxiSpeedKts)
        {
            if (observedTaxiSpeedKts <= profile.MaxTaxiSpeedKts) return null;

            return new AircraftDamageEvent
            {
                AircraftId = aircraftId,
                ReservationId = reservationId,
                EventCode = "TAXI_OVERSPEED",
                Phase = "taxi",
                Severity = observedTaxiSpeedKts > profile.MaxTaxiSpeedKts + 10 ? "medium" : "low",
                Details = new Dictionary<string, object>
                {
                    ["observed_taxi_speed_kts"] = Math.Round(observedTaxiSpeedKts, 1),
                    ["max_taxi_speed_kts"] = profile.MaxTaxiSpeedKts,
                    ["profile_code"] = profile.ProfileCode
                }
            };
        }

        public static List<AircraftDamageEvent> MapLandingDamage(
            string aircraftId,
            string reservationId,
            AircraftDamageProfileSettings profile,
            double touchdownFpm)
        {
            var list = new List<AircraftDamageEvent>();
            if (touchdownFpm >= 0) return list;

            if (touchdownFpm <= profile.SevereHardLandingFpm)
            {
                list.Add(Build(aircraftId, reservationId, "SEVERE_HARD_LANDING_GEAR", "landing", "critical", touchdownFpm, profile));
                list.Add(Build(aircraftId, reservationId, "SEVERE_HARD_LANDING_FUSELAGE", "landing", "critical", touchdownFpm, profile));
                return list;
            }

            if (touchdownFpm <= profile.HardLandingFpm)
            {
                list.Add(Build(aircraftId, reservationId, "HARD_LANDING_GEAR", "landing", "high", touchdownFpm, profile));
                list.Add(Build(aircraftId, reservationId, "HARD_LANDING_FUSELAGE", "landing", "high", touchdownFpm, profile));
                return list;
            }

            // opcional: aterrizaje normal/suave también puede registrarse si quieres trazabilidad
            return list;
        }

        public static AircraftDamageEvent? MapMinorEngineExceedance(
            string aircraftId,
            string reservationId,
            AircraftDamageProfileSettings profile,
            double peakN1)
        {
            if (peakN1 < profile.MinorEngineN1Pct || peakN1 >= profile.MajorEngineN1Pct) return null;
            return new AircraftDamageEvent
            {
                AircraftId = aircraftId,
                ReservationId = reservationId,
                EventCode = "ENGINE_EXCEEDANCE_MINOR",
                Phase = "flight",
                Severity = "medium",
                Details = new Dictionary<string, object>
                {
                    ["peak_n1_pct"] = Math.Round(peakN1, 2),
                    ["minor_limit_pct"] = profile.MinorEngineN1Pct,
                    ["major_limit_pct"] = profile.MajorEngineN1Pct,
                    ["profile_code"] = profile.ProfileCode
                }
            };
        }

        public static AircraftDamageEvent? MapMajorEngineExceedance(
            string aircraftId,
            string reservationId,
            AircraftDamageProfileSettings profile,
            double peakN1)
        {
            if (peakN1 < profile.MajorEngineN1Pct) return null;
            return new AircraftDamageEvent
            {
                AircraftId = aircraftId,
                ReservationId = reservationId,
                EventCode = "ENGINE_EXCEEDANCE_MAJOR",
                Phase = "flight",
                Severity = "high",
                Details = new Dictionary<string, object>
                {
                    ["peak_n1_pct"] = Math.Round(peakN1, 2),
                    ["major_limit_pct"] = profile.MajorEngineN1Pct,
                    ["profile_code"] = profile.ProfileCode
                }
            };
        }

        public static List<AircraftDamageEvent> MapCrash(
            string aircraftId,
            string reservationId,
            string phase,
            AircraftDamageProfileSettings profile)
        {
            return new List<AircraftDamageEvent>
            {
                new AircraftDamageEvent
                {
                    AircraftId = aircraftId,
                    ReservationId = reservationId,
                    EventCode = "CRASH_ENGINE",
                    Phase = string.IsNullOrWhiteSpace(phase) ? "impact" : phase,
                    Severity = "critical",
                    Details = new Dictionary<string, object> { ["profile_code"] = profile.ProfileCode }
                },
                new AircraftDamageEvent
                {
                    AircraftId = aircraftId,
                    ReservationId = reservationId,
                    EventCode = "CRASH_FUSELAGE",
                    Phase = string.IsNullOrWhiteSpace(phase) ? "impact" : phase,
                    Severity = "critical",
                    Details = new Dictionary<string, object> { ["profile_code"] = profile.ProfileCode }
                },
                new AircraftDamageEvent
                {
                    AircraftId = aircraftId,
                    ReservationId = reservationId,
                    EventCode = "CRASH_GEAR",
                    Phase = string.IsNullOrWhiteSpace(phase) ? "impact" : phase,
                    Severity = "critical",
                    Details = new Dictionary<string, object> { ["profile_code"] = profile.ProfileCode }
                }
            };
        }

        public static List<AircraftDamageEvent> MapRunwayExcursion(
            string aircraftId,
            string reservationId,
            string phase,
            AircraftDamageProfileSettings profile,
            string surface)
        {
            return new List<AircraftDamageEvent>
            {
                new AircraftDamageEvent
                {
                    AircraftId = aircraftId,
                    ReservationId = reservationId,
                    EventCode = "RUNWAY_EXCURSION_GEAR",
                    Phase = string.IsNullOrWhiteSpace(phase) ? "landing" : phase,
                    Severity = "high",
                    Details = new Dictionary<string, object> { ["surface"] = surface ?? string.Empty, ["profile_code"] = profile.ProfileCode }
                },
                new AircraftDamageEvent
                {
                    AircraftId = aircraftId,
                    ReservationId = reservationId,
                    EventCode = "RUNWAY_EXCURSION_FUSELAGE",
                    Phase = string.IsNullOrWhiteSpace(phase) ? "landing" : phase,
                    Severity = "high",
                    Details = new Dictionary<string, object> { ["surface"] = surface ?? string.Empty, ["profile_code"] = profile.ProfileCode }
                }
            };
        }

        private static AircraftDamageEvent Build(
            string aircraftId,
            string reservationId,
            string eventCode,
            string phase,
            string severity,
            double touchdownFpm,
            AircraftDamageProfileSettings profile)
        {
            return new AircraftDamageEvent
            {
                AircraftId = aircraftId,
                ReservationId = reservationId,
                EventCode = eventCode,
                Phase = phase,
                Severity = severity,
                Details = new Dictionary<string, object>
                {
                    ["touchdown_fpm"] = Math.Round(touchdownFpm, 0),
                    ["hard_landing_limit_fpm"] = profile.HardLandingFpm,
                    ["severe_hard_landing_limit_fpm"] = profile.SevereHardLandingFpm,
                    ["profile_code"] = profile.ProfileCode
                }
            };
        }
    }
}
