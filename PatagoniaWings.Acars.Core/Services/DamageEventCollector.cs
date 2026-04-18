using System;
using System.Collections.Generic;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    public class DamageEventCollector
    {
        private readonly AircraftDamageProfileSettings _profile;
        private readonly string _aircraftId;
        private readonly string _reservationId;

        private double _maxTaxiSpeedKts;
        private double _peakN1;
        private bool _hadCrash;
        private bool _hadRunwayExcursion;
        private string _runwayExcursionSurface = string.Empty;
        private string _lastPhase = "unknown";

        public DamageEventCollector(string aircraftId, string reservationId, AircraftDamageProfileSettings profile)
        {
            _aircraftId = aircraftId ?? string.Empty;
            _reservationId = reservationId ?? string.Empty;
            _profile = profile ?? new AircraftDamageProfileSettings();
        }

        public void RecordSample(SimData data)
        {
            if (data == null) return;

            _lastPhase = InferPhase(data);

            if (data.OnGround && data.GroundSpeed > 2)
            {
                if (data.GroundSpeed > _maxTaxiSpeedKts)
                    _maxTaxiSpeedKts = data.GroundSpeed;
            }

            var localPeakN1 = Math.Max(data.Engine1N1, data.Engine2N1);
            if (localPeakN1 > _peakN1)
                _peakN1 = localPeakN1;
        }

        public void MarkCrash()
        {
            _hadCrash = true;
        }

        public void MarkRunwayExcursion(string surface = "")
        {
            _hadRunwayExcursion = true;
            _runwayExcursionSurface = surface ?? string.Empty;
        }

        public List<AircraftDamageEvent> BuildDamageEvents(FlightService flightService)
        {
            var events = new List<AircraftDamageEvent>();

            var taxiEvent = DamageRuleMapper.MapTaxiOverspeed(_aircraftId, _reservationId, _profile, _maxTaxiSpeedKts);
            if (taxiEvent != null) events.Add(taxiEvent);

            var landingEvents = DamageRuleMapper.MapLandingDamage(
                _aircraftId,
                _reservationId,
                _profile,
                flightService == null ? 0 : flightService.LastLandingVS);

            if (landingEvents.Count > 0) events.AddRange(landingEvents);

            var majorEngine = DamageRuleMapper.MapMajorEngineExceedance(_aircraftId, _reservationId, _profile, _peakN1);
            if (majorEngine != null)
            {
                events.Add(majorEngine);
            }
            else
            {
                var minorEngine = DamageRuleMapper.MapMinorEngineExceedance(_aircraftId, _reservationId, _profile, _peakN1);
                if (minorEngine != null) events.Add(minorEngine);
            }

            if (_hadRunwayExcursion)
                events.AddRange(DamageRuleMapper.MapRunwayExcursion(_aircraftId, _reservationId, _lastPhase, _profile, _runwayExcursionSurface));

            if (_hadCrash)
                events.AddRange(DamageRuleMapper.MapCrash(_aircraftId, _reservationId, _lastPhase, _profile));

            return events;
        }

        public void Reset()
        {
            _maxTaxiSpeedKts = 0;
            _peakN1 = 0;
            _hadCrash = false;
            _hadRunwayExcursion = false;
            _runwayExcursionSurface = string.Empty;
            _lastPhase = "unknown";
        }

        private static string InferPhase(SimData data)
        {
            if (data == null) return "unknown";
            if (data.OnGround && data.GroundSpeed < 2) return "parked";
            if (data.OnGround && data.GroundSpeed < 60) return "taxi";
            if (!data.OnGround && data.AltitudeAGL < 1500) return "approach";
            if (!data.OnGround) return "flight";
            return "unknown";
        }
    }
}
