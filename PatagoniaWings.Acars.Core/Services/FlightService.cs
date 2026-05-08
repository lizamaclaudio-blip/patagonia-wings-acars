using System;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    public class FlightService
    {
        private Flight? _currentFlight;
        private SimData _lastSimData = new SimData();
        private readonly List<SimData> _telemetryLog = new List<SimData>();

        // Estadisticas de vuelo
        private double _maxAltitude;
        private double _maxSpeed;
        private double _totalDistanceNm;
        private double _fuelAtStart;
        private DateTime _blockOutTime;
        private DateTime _takeoffTime;
        private DateTime _touchdownTime;
        private double _lastLandingVS;
        private double _lastLandingG;
        private double _lastLatitude;
        private double _lastLongitude;
        private bool _hasPosition;
        private DamageEventCollector? _damageCollector;
        private readonly List<AircraftDamageEvent> _finalDamageEvents = new List<AircraftDamageEvent>();
        private bool _crashMarked;
        private DateTime _crashMarkedAtUtc = default(DateTime);
        private string _crashReason = string.Empty;

        // C1 Phase Resolver: persistent operational state for a robust state machine.
        // The simulator may momentarily report stale or contradictory altitude/ground data;
        // these counters prevent jumping between phases from a single noisy sample.
        private bool _hasBeenAirborne;
        private bool _takeoffDetected;
        private bool _touchdownDetected;
        private int _airborneConfirmSamples;
        private int _groundConfirmSamples;
        private int _descentConfirmSamples;
        private int _approachConfirmSamples;
        private int _cruiseStableSamples;
        private DateTime _lastPhaseChangeUtc = default(DateTime);

        // C3 Phase transition matrix: debounced candidates, dwell time and trend counters.
        // These values are RAW evidence only; Web/Supabase remains the scoring authority.
        private FlightPhase _candidatePhase = FlightPhase.Disconnected;
        private int _candidatePhaseSamples;
        private int _phaseStabilitySamples;
        private int _phaseTransitionIndex;
        private DateTime _phaseEnteredAtUtc = default(DateTime);
        private double _lastAirborneMsl;
        private int _mslIncreasingSamples;
        private int _mslDecreasingSamples;
        private int _taxiOutConfirmSamples;
        private int _takeoffRollConfirmSamples;
        private int _landingRollConfirmSamples;
        private int _gateReadyConfirmSamples;

        // C10 Runway/Taxiway/TDZ detector state. Runway heading is captured from
        // high-confidence takeoff/landing samples and used as an inferred runway axis.
        // This is geometry-lite evidence only; exact runway/taxiway names require navdata.
        private double _departureRunwayHeadingDeg = double.NaN;
        private double _arrivalRunwayHeadingDeg = double.NaN;
        private bool _wasRunwayCandidate;
        private bool _runwayExitDetected;

        // C11D8 Flight phase stabilization. These are recorder-side milestones only;
        // Web/Supabase remains the official scoring authority. They prevent short
        // flights or traffic pattern tests from jumping PRE/TAKEOFF directly to
        // APPROACH without exposing climb/cruise/descent evidence.
        private bool _climbObserved;
        private bool _cruiseObserved;
        private bool _descentObserved;
        private DateTime _airborneAtUtc = default(DateTime);
        private bool _originAreaExited;
        private bool _destinationAreaObserved;
        private int _airborneSamplesSinceTakeoff;
        private bool _arrivalGateLatched;

        // Patagonia Wings 7.0.14 hotfix:
        // Un vuelo activo no debe resetearse ni quedar en Disconnected por cortes
        // breves de SimConnect/FSUIPC o por triggers legacy. El reset solo se
        // permite cuando el piloto confirma cierre/cancelacion, o en auto-closeout
        // critico autorizado.
        private bool _closeoutResetAuthorized;

        public FlightPhase CurrentPhase { get; private set; } = FlightPhase.Disconnected;
        public bool IsFlightActive => _currentFlight != null;
        public Flight? CurrentFlight => _currentFlight;
        public SimData LastSimData => _lastSimData;
        public DateTime BlockOutTimeUtc => _blockOutTime;
        public DateTime TakeoffTimeUtc => _takeoffTime;
        public DateTime TouchdownTimeUtc => _touchdownTime;
        public double FuelAtStartLbs => _fuelAtStart;
        public bool IsFlightLocked { get; private set; }

        // Estadísticas en tiempo real (para el log de PIREP en vivo)
        public double MaxAltitudeFeet   => _maxAltitude;
        public double MaxSpeedKts       => _maxSpeed;
        public double TotalDistanceNm   => _totalDistanceNm;
        public double LastLandingVS     => _lastLandingVS;

        public event Action<FlightPhase>? PhaseChanged;
        public event Action<SimData>? TelemetryUpdated;
        public event Action<bool>? FlightLockChanged;
        public event Action<string>? CrashDetected;
        public event Action? FlightOfficiallyStarted;

        public void StartFlight(Flight flight, double initialFuel)
        {
            _currentFlight = flight;
            _fuelAtStart = initialFuel;
            _blockOutTime = DateTime.UtcNow;
            _takeoffTime = default(DateTime);
            _touchdownTime = default(DateTime);
            _maxAltitude = 0;
            _maxSpeed = 0;
            _totalDistanceNm = 0;
            _lastLandingVS = 0;
            _lastLandingG = 0;
            _telemetryLog.Clear();
            _lastSimData = new SimData();
            _hasPosition = false;
            _crashMarked = false;
            _hasBeenAirborne = false;
            _takeoffDetected = false;
            _touchdownDetected = false;
            _airborneConfirmSamples = 0;
            _groundConfirmSamples = 0;
            _descentConfirmSamples = 0;
            _approachConfirmSamples = 0;
            _cruiseStableSamples = 0;
            _lastPhaseChangeUtc = default(DateTime);
            _candidatePhase = FlightPhase.Disconnected;
            _candidatePhaseSamples = 0;
            _phaseStabilitySamples = 0;
            _phaseTransitionIndex = 0;
            _phaseEnteredAtUtc = default(DateTime);
            _lastAirborneMsl = 0d;
            _mslIncreasingSamples = 0;
            _mslDecreasingSamples = 0;
            _taxiOutConfirmSamples = 0;
            _takeoffRollConfirmSamples = 0;
            _landingRollConfirmSamples = 0;
            _gateReadyConfirmSamples = 0;
            _departureRunwayHeadingDeg = double.NaN;
            _arrivalRunwayHeadingDeg = double.NaN;
            _wasRunwayCandidate = false;
            _runwayExitDetected = false;
            _climbObserved = false;
            _cruiseObserved = false;
            _descentObserved = false;
            _airborneAtUtc = default(DateTime);
            _originAreaExited = false;
            _destinationAreaObserved = false;
            _airborneSamplesSinceTakeoff = 0;
            _arrivalGateLatched = false;
            _closeoutResetAuthorized = false;
            _finalDamageEvents.Clear();

            var profile = DamageRuleMapper.CreateProfile(
                flight.AircraftTypeCode,
                string.Concat(flight.AircraftName ?? string.Empty, " ", flight.AircraftDisplayName ?? string.Empty));

            _damageCollector = string.IsNullOrWhiteSpace(flight.AircraftId)
                ? null
                : new DamageEventCollector(flight.AircraftId, flight.ReservationId, profile);

            SetFlightLock(true);
            SetPhase(FlightPhase.PreFlight);
            FlightOfficiallyStarted?.Invoke();
        }

        public void UpdateSimData(SimData data)
        {
            if (data == null)
            {
                return;
            }

            Debug.WriteLine($"[FlightService] UpdateSimData - ALT={data.AltitudeFeet:F0} FUEL={data.FuelTotalLbs:F0}");

            if (data.CapturedAtUtc == default(DateTime))
            {
                data.CapturedAtUtc = DateTime.UtcNow;
            }

            // Solo capturar combustible inicial cuando hay vuelo activo
            if (_currentFlight != null && _fuelAtStart <= 0 && data.FuelTotalLbs > 0)
            {
                _fuelAtStart = data.FuelTotalLbs;
            }

            _lastSimData = data;

            // Estadísticas y log de telemetría solo cuando hay vuelo activo
            if (_currentFlight != null)
            {
                if (_damageCollector != null)
                {
                    _damageCollector.RecordSample(data);
                }

                DetectCrashLikeImpact(data);

                if (_hasPosition)
                {
                    var legDistanceNm = CalculateDistanceNm(
                        _lastLatitude, _lastLongitude, data.Latitude, data.Longitude);

                    // PIREP Perfect A1:
                    // No sumar saltos imposibles de GPS/SimConnect. Un salto corrupto
                    // descuadra distancia, combustible por NM y puede contaminar el score web.
                    if (IsValidPosition(_lastLatitude, _lastLongitude) &&
                        IsValidPosition(data.Latitude, data.Longitude) &&
                        legDistanceNm >= 0d && legDistanceNm <= 20d)
                    {
                        _totalDistanceNm += legDistanceNm;
                    }
                }

                if (data.AltitudeMslFeet > _maxAltitude) _maxAltitude = data.AltitudeMslFeet;
            else if (data.AltitudeFeet > _maxAltitude) _maxAltitude = data.AltitudeFeet;
                if (data.IndicatedAirspeed > _maxSpeed) _maxSpeed = data.IndicatedAirspeed;

                SanitizeSampleForPirep(data);
                _telemetryLog.Add(data);
            }

            // Siempre actualizar posición para que al iniciar vuelo tengamos punto inicial correcto
            _lastLatitude = data.Latitude;
            _lastLongitude = data.Longitude;
            _hasPosition = true;

            UpdatePhase(data);
            TelemetryUpdated?.Invoke(data); // Siempre notifica la UI (modo monitoreo)
        }

        public IReadOnlyList<SimData> GetTelemetrySnapshot()
        {
            return _telemetryLog.ToArray();
        }

        public FlightPhase CurrentFlightPhase => CurrentPhase;

        private void UpdatePhase(SimData data)
        {
            if (_currentFlight == null || data == null)
            {
                return;
            }

            var sampleTime = data.CapturedAtUtc == default(DateTime) ? DateTime.UtcNow : data.CapturedAtUtc;
            var previous = _telemetryLog.Count >= 2 ? _telemetryLog[_telemetryLog.Count - 2] : null;

            var agl = ResolveAgl(data);
            var msl = ResolveMsl(data);
            var gs = SafeNumber(data.GroundSpeed);
            var ias = SafeNumber(data.IndicatedAirspeed);
            var vs = SafeNumber(data.VerticalSpeed);
            var plannedCruiseAltitude = _currentFlight.PlannedAltitude > 0 ? _currentFlight.PlannedAltitude : 0d;

            var onGroundRaw = ResolveOperationalOnGround(data, agl, gs, ias, vs);
            var previousOnGround = previous == null ? onGroundRaw : ResolveOperationalOnGround(previous, ResolveAgl(previous), SafeNumber(previous.GroundSpeed), SafeNumber(previous.IndicatedAirspeed), SafeNumber(previous.VerticalSpeed));
            var previousAirborne = previous != null && (previous.IsAirborneSample || (!previousOnGround && (ResolveAgl(previous) > 20d || SafeNumber(previous.GroundSpeed) > 55d || SafeNumber(previous.IndicatedAirspeed) > 55d)));

            var facilityOnRunway = data.FacilityOnRunwayCandidate || data.FacilityRunwayAlignedCandidate || data.FacilityTouchdownZoneCandidate;
            var facilityTaxiway = data.FacilityTaxiwayCandidate || data.TaxiwayProbable;
            var facilityGate = data.FacilityGateAreaCandidate || data.GateAreaCandidate || data.GateReadyCandidate;
            var nearestFacilityIcao = (data.FacilityNearestRunwayAirportIcao ?? string.Empty).Trim().ToUpperInvariant();
            var departureIcao = _currentFlight == null ? string.Empty : (_currentFlight.DepartureIcao ?? string.Empty).Trim().ToUpperInvariant();
            var arrivalIcao = _currentFlight == null ? string.Empty : (_currentFlight.ArrivalIcao ?? string.Empty).Trim().ToUpperInvariant();
            var nearDepartureAirport = !string.IsNullOrWhiteSpace(departureIcao) && string.Equals(nearestFacilityIcao, departureIcao, StringComparison.OrdinalIgnoreCase);
            var nearArrivalAirport = !string.IsNullOrWhiteSpace(arrivalIcao) && string.Equals(nearestFacilityIcao, arrivalIcao, StringComparison.OrdinalIgnoreCase);

            if (_hasBeenAirborne && !nearDepartureAirport && !string.IsNullOrWhiteSpace(nearestFacilityIcao))
            {
                _originAreaExited = true;
            }

            if (_hasBeenAirborne && nearArrivalAirport)
            {
                _destinationAreaObserved = true;
            }

            // C11F: SIM ON GROUND can flicker during initial climb/low pass. Treat high
            // runway energy with real AGL as airborne for the phase ladder so the UI and
            // evidence do not jump to RUNWAY_ARRIVAL immediately after takeoff.
            var airborneEnergy = (agl > 25d || (!onGroundRaw && agl > 8d) || ias >= 55d || gs >= 65d) && !data.ParkingBrake;
            var onGround = onGroundRaw && !(agl > 45d && airborneEnergy && _hasBeenAirborne && !_touchdownDetected);

            UpdateAltitudeTrend(msl, onGround);

            var airborneCandidate = !onGround
                && (agl > 20d || (_lastAirborneMsl > 0d && msl > _lastAirborneMsl + 50d) || airborneEnergy)
                && (gs > 40d || ias > 40d);

            var runwayDepartureCandidate = onGround && !_takeoffDetected
                && (facilityOnRunway || data.RunwayEntryCandidate || data.TakeoffRollCandidate || gs >= 38d || ias >= 35d)
                && !data.ParkingBrake
                && (gs >= 30d || ias >= 30d);

            var readyTaxiOutCandidate = onGround && !_hasBeenAirborne
                && data.TaxiLightsOn
                && (!data.ParkingBrake || gs > 1.5d)
                && gs <= 25d
                && !facilityOnRunway;

            var taxiOutCandidate = onGround && !_hasBeenAirborne
                && !facilityOnRunway
                && !data.ParkingBrake
                && (readyTaxiOutCandidate || (gs > 2.5d && gs <= 35d) || facilityTaxiway);

            if (airborneCandidate)
            {
                _airborneConfirmSamples++;
            }
            else if (onGround && !_hasBeenAirborne)
            {
                _airborneConfirmSamples = 0;
            }

            if (runwayDepartureCandidate)
            {
                _takeoffRollConfirmSamples++;
            }
            else if (!_takeoffDetected)
            {
                _takeoffRollConfirmSamples = 0;
            }

            if (taxiOutCandidate || readyTaxiOutCandidate)
            {
                _taxiOutConfirmSamples++;
            }
            else if (!_hasBeenAirborne)
            {
                _taxiOutConfirmSamples = 0;
            }

            if (onGround && _hasBeenAirborne)
            {
                _groundConfirmSamples++;
            }
            else if (!onGround)
            {
                _groundConfirmSamples = 0;
                _landingRollConfirmSamples = 0;
                _gateReadyConfirmSamples = 0;
            }

            var airborneSince = _airborneAtUtc == default(DateTime)
                ? SecondsSince(_takeoffTime, sampleTime)
                : SecondsSince(_airborneAtUtc, sampleTime);

            if (_hasBeenAirborne && !_touchdownDetected && !onGround)
            {
                _airborneSamplesSinceTakeoff++;
            }

            // C11F: do not accept touchdown/arrival from single noisy ground samples
            // during departure, especially in short-leg runway pattern tests. Approach
            // and touchdown become eligible only after the origin area was exited, the
            // destination/alternate area is observed, or a conservative airborne time/
            // distance threshold is reached. This is evidence-side stabilization only.
            var arrivalContextEligible = _originAreaExited
                || _destinationAreaObserved
                || nearArrivalAirport
                || _totalDistanceNm >= 8d
                || airborneSince >= 180d
                || _airborneSamplesSinceTakeoff >= 30;

            if (_hasBeenAirborne && !_touchdownDetected && !arrivalContextEligible && onGround && (agl > 8d || gs >= 35d || ias >= 35d))
            {
                onGround = false;
                _groundConfirmSamples = 0;
            }

            var touchdownAltitudeOk = agl <= 30d || (data.FacilityTouchdownZoneCandidate && agl <= 80d);
            var touchdownTimeOk = _takeoffTime == default(DateTime) || SecondsSince(_takeoffTime, sampleTime) >= 90d || nearArrivalAirport || _totalDistanceNm >= 8d;
            var touchdownEnergyOk = gs >= 35d || ias >= 35d || Math.Abs(vs) >= 80d || data.LandingG > 1.05d;
            var touchdownCandidate = _hasBeenAirborne && !_touchdownDetected
                && arrivalContextEligible
                && touchdownTimeOk
                && touchdownAltitudeOk
                && touchdownEnergyOk
                && (previousAirborne || !previousOnGround || _groundConfirmSamples >= 2)
                && onGround;

            if (touchdownCandidate)
            {
                MarkTouchdown(data, previous, sampleTime);
                _landingRollConfirmSamples = 1;
                ApplyPhaseDecision(data, FlightPhase.Landing, "touchdown_confirmed_agl_facility_c11e", onGround, false, sampleTime, 1, true);
                return;
            }

            if (!_takeoffDetected && _airborneConfirmSamples >= 2)
            {
                _takeoffDetected = true;
                _hasBeenAirborne = true;
                if (_airborneAtUtc == default(DateTime))
                {
                    _airborneAtUtc = sampleTime;
                }
                if (_takeoffTime == default(DateTime))
                {
                    _takeoffTime = sampleTime;
                }

                ApplyPhaseDecision(data, FlightPhase.Takeoff, "airborne_confirmed_two_samples_c11e", onGround, true, sampleTime, 1, true);
                return;
            }

            if (!_hasBeenAirborne)
            {
                if (_takeoffRollConfirmSamples >= 2)
                {
                    ApplyPhaseDecision(data, FlightPhase.Takeoff, "runway_departure_confirmed_facility_c11e", onGround, false, sampleTime, 2, false);
                    return;
                }

                if (_taxiOutConfirmSamples >= 2 || readyTaxiOutCandidate)
                {
                    ApplyPhaseDecision(data, FlightPhase.PushbackTaxi, readyTaxiOutCandidate ? "ready_taxi_out_taxi_light_brake_release_c11e" : "taxi_out_confirmed_surface_c11e", onGround, false, sampleTime, readyTaxiOutCandidate ? 1 : 2, false);
                    return;
                }

                // Do not regress to an ambiguous PreFlight visual once the aircraft is
                // electrically prepared for taxi. The procedure evidence will still show
                // whether lights/door/parking states are correct.
                if (data.TaxiLightsOn || (!data.ParkingBrake && gs > 0.5d))
                {
                    ApplyPhaseDecision(data, FlightPhase.PushbackTaxi, "taxi_preparation_or_movement_c11f", onGround, false, sampleTime, 1, false);
                    return;
                }

                ApplyPhaseDecision(data, FlightPhase.PreFlight, onGround ? "gate_origin_ground_stationary_c11e" : "preflight_waiting_airborne_confirmation_c11e", onGround, false, sampleTime, 1, false);
                return;
            }

            if (onGround)
            {
                if (_arrivalGateLatched && gs <= 12d)
                {
                    ApplyPhaseDecision(data, FlightPhase.Arrived, "gate_arrival_latched_hold_c11f", onGround, false, sampleTime, 1, false);
                    return;
                }

                var gateArrivalCandidate = _touchdownDetected && gs <= 3d && data.ParkingBrake && (facilityGate || !facilityOnRunway);
                // C18: taxi-in must mean the aircraft has left the runway environment.
                // Taxi light alone on the runway is still landing roll / runway occupancy,
                // not taxi-in.
                var taxiInCandidate = _touchdownDetected
                    && !facilityOnRunway
                    && (facilityTaxiway || data.RunwayExitCandidate || (gs > 3d && gs <= 35d && !data.LandingRollCandidate) || (data.TaxiLightsOn && gs <= 25d && !data.LandingRollCandidate));
                var landingRollCandidate = _touchdownDetected && (facilityOnRunway || data.LandingRollCandidate || gs > 35d);

                if (gateArrivalCandidate)
                {
                    _gateReadyConfirmSamples++;
                }
                else
                {
                    _gateReadyConfirmSamples = 0;
                }

                if (_gateReadyConfirmSamples >= 2)
                {
                    _arrivalGateLatched = true;
                    ApplyPhaseDecision(data, FlightPhase.Arrived, "gate_arrival_confirmed_facility_brake_c11e", onGround, false, sampleTime, 2, false);
                    return;
                }

                if (taxiInCandidate)
                {
                    ApplyPhaseDecision(data, FlightPhase.Taxi, "taxi_in_confirmed_after_runway_exit_c11e", onGround, false, sampleTime, 1, false);
                    return;
                }

                if (landingRollCandidate)
                {
                    _landingRollConfirmSamples++;
                    ApplyPhaseDecision(data, FlightPhase.Landing, "landing_roll_confirmed_c11e", onGround, false, sampleTime, 1, _landingRollConfirmSamples <= 2);
                    return;
                }

                ApplyPhaseDecision(data, _touchdownDetected ? FlightPhase.Taxi : FlightPhase.Landing, "ground_after_touchdown_pending_taxi_gate_c11e", onGround, false, sampleTime, 1, false);
                return;
            }

            // C11E Airborne matrix. It exposes the full operational ladder before
            // approach: TAKEOFF -> CLIMB -> CRUISE/DESCENT -> APPROACH. Approach is
            // only allowed with descent + runway/low-altitude evidence.
            airborneSince = _airborneAtUtc == default(DateTime)
                ? SecondsSince(_takeoffTime, sampleTime)
                : SecondsSince(_airborneAtUtc, sampleTime);

            // C16: phase ladder now uses sustained vertical trends and planned cruise
            // altitude. AGL is relative to airport/terrain elevation, so approach is
            // evaluated by AGL + sustained descent, not by absolute MSL alone.
            var cruiseAltitudeKnown = plannedCruiseAltitude > 0d;
            var cruiseAltitudeReached = cruiseAltitudeKnown && msl >= plannedCruiseAltitude - 1000d;
            var cruiseBandStable = cruiseAltitudeReached && Math.Abs(msl - plannedCruiseAltitude) <= 2000d;
            var sustainedClimb = vs > 250d || _mslIncreasingSamples >= 2;
            var sustainedDescent = vs < -350d || _mslDecreasingSamples >= 3;
            var nearApproachLayer = agl > 0d && agl <= 3000d;
            var runwayDistanceNm = data.FacilityNearestRunwayDistanceMeters > 0d
                ? data.FacilityNearestRunwayDistanceMeters / 1852d
                : double.MaxValue;
            var runwayHeadingErrorDeg = data.FacilityRunwayHeadingErrorDeg > 0d
                ? data.FacilityRunwayHeadingErrorDeg
                : (data.RunwayHeadingDeltaDeg > 0d ? data.RunwayHeadingDeltaDeg : 180d);
            var runwayAlignedForApproach = (data.FacilityRunwayGeometryAvailable && runwayHeadingErrorDeg <= 10d)
                                           || data.FacilityRunwayAlignedCandidate
                                           || data.RunwayAlignedCandidate;
            var approachContext = nearArrivalAirport || _destinationAreaObserved || _totalDistanceNm >= 8d || airborneSince >= 180d;
            var approachCandidate = nearApproachLayer
                && approachContext
                && sustainedDescent
                && runwayDistanceNm <= 7.0d
                && runwayAlignedForApproach
                && (_descentObserved || _cruiseObserved || airborneSince >= 180d)
                && !_touchdownDetected;
            var descentCandidate = agl >= 900d
                && approachContext
                && sustainedDescent
                && (_cruiseObserved || cruiseAltitudeReached || airborneSince >= 240d || _totalDistanceNm >= 12d);
            var cruiseCandidate = gs >= 65d
                && Math.Abs(vs) <= 350d
                && (!descentCandidate || CurrentPhase == FlightPhase.Cruise)
                && (cruiseAltitudeKnown ? cruiseBandStable : (agl >= 3500d && airborneSince >= 90d));
            var climbCandidate = !_descentObserved
                && (!cruiseCandidate || !cruiseAltitudeReached)
                && (sustainedClimb || agl < 2500d || !_climbObserved);

            if (approachCandidate)
            {
                _approachConfirmSamples++;
            }
            else if (CurrentPhase != FlightPhase.Approach)
            {
                _approachConfirmSamples = 0;
            }

            if (descentCandidate)
            {
                _descentConfirmSamples++;
            }
            else if (vs > -150d && _mslDecreasingSamples == 0)
            {
                _descentConfirmSamples = 0;
            }

            if (cruiseCandidate)
            {
                _cruiseStableSamples++;
            }
            else if (Math.Abs(vs) > 650d || approachCandidate)
            {
                _cruiseStableSamples = 0;
            }

            if (CurrentPhase == FlightPhase.Takeoff && (airborneSince < 15d || agl < 250d))
            {
                ApplyPhaseDecision(data, FlightPhase.Takeoff, "initial_liftoff_takeoff_window_c11e", onGround, true, sampleTime, 1, false);
                return;
            }

            if (!_climbObserved || (CurrentPhase == FlightPhase.Takeoff && airborneSince >= 15d && agl >= 180d))
            {
                ApplyPhaseDecision(data, FlightPhase.Climb, "climb_required_before_approach_c11e", onGround, true, sampleTime, 1, false);
                return;
            }

            if (_approachConfirmSamples >= 2)
            {
                ApplyPhaseDecision(data, FlightPhase.Approach, "approach_confirmed_sustained_descent_agl_runway_c16", onGround, true, sampleTime, 3, false);
                return;
            }

            if (_descentConfirmSamples >= 3 && (_cruiseObserved || airborneSince >= 180d || (plannedCruiseAltitude > 0d && msl >= plannedCruiseAltitude - 2500d)))
            {
                ApplyPhaseDecision(data, FlightPhase.Descent, "descent_confirmed_sustained_after_cruise_c16", onGround, true, sampleTime, 3, false);
                return;
            }

            if (_cruiseStableSamples >= 4)
            {
                ApplyPhaseDecision(data, FlightPhase.Cruise, "cruise_stable_at_planned_altitude_c16", onGround, true, sampleTime, 4, false);
                return;
            }

            if (climbCandidate)
            {
                ApplyPhaseDecision(data, FlightPhase.Climb, "climb_continuing_until_planned_cruise_c16", onGround, true, sampleTime, 2, false);
                return;
            }

            var preserved = CurrentPhase == FlightPhase.Disconnected || CurrentPhase == FlightPhase.PreFlight || CurrentPhase == FlightPhase.PushbackTaxi
                ? FlightPhase.Climb
                : CurrentPhase;
            ApplyPhaseDecision(data, preserved, "preserve_airborne_phase_c11e", onGround, true, sampleTime, 1, false);
        }

        private void MarkTouchdown(SimData data, SimData? previous, DateTime sampleTime)
        {
            if (_touchdownDetected)
            {
                return;
            }

            _touchdownDetected = true;
            _touchdownTime = sampleTime;

            var touchdownVs = data.LandingVS;
            if (!IsOperationalVerticalSpeed(touchdownVs) || Math.Abs(touchdownVs) < 1d)
            {
                touchdownVs = previous != null && IsOperationalVerticalSpeed(previous.VerticalSpeed)
                    ? previous.VerticalSpeed
                    : data.VerticalSpeed;
            }

            _lastLandingVS = IsOperationalVerticalSpeed(touchdownVs) ? touchdownVs : 0d;

            var touchdownG = data.LandingG;
            if (!IsOperationalGForce(touchdownG) || Math.Abs(touchdownG) < 0.01d)
            {
                touchdownG = IsOperationalGForce(data.GForce) ? data.GForce : 0d;
            }

            _lastLandingG = IsOperationalGForce(touchdownG) ? touchdownG : 0d;
            data.TouchdownDetected = true;
        }

        private void ApplyPhaseDecision(SimData data, FlightPhase candidatePhase, string reason, bool onGround, bool isAirborneSample, DateTime sampleTime, int requiredSamples, bool force)
        {
            if (data == null)
            {
                return;
            }

            if (requiredSamples < 1) requiredSamples = 1;

            var targetPhase = candidatePhase;
            var confidence = force ? "forced" : "confirmed";
            var fromPhase = CurrentPhase;

            if (!force && candidatePhase != CurrentPhase)
            {
                if (_candidatePhase == candidatePhase)
                {
                    _candidatePhaseSamples++;
                }
                else
                {
                    _candidatePhase = candidatePhase;
                    _candidatePhaseSamples = 1;
                }

                if (_candidatePhaseSamples < requiredSamples)
                {
                    targetPhase = CurrentPhase == FlightPhase.Disconnected ? candidatePhase : CurrentPhase;
                    confidence = "pending";
                    reason = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "pending_{0}_{1}_of_{2}:{3}",
                        ToOperationalPhaseCode(candidatePhase),
                        _candidatePhaseSamples,
                        requiredSamples,
                        reason ?? string.Empty);
                    AnnotatePhase(data, targetPhase, reason, onGround, isAirborneSample, fromPhase, candidatePhase, false, confidence);
                    return;
                }
            }
            else
            {
                _candidatePhase = candidatePhase;
                _candidatePhaseSamples = requiredSamples;
            }

            var changed = targetPhase != CurrentPhase;
            if (changed)
            {
                _lastPhaseChangeUtc = sampleTime;
                _phaseEnteredAtUtc = sampleTime;
                _phaseTransitionIndex++;
                _phaseStabilitySamples = 0;
                _candidatePhaseSamples = 0;
                SetPhase(targetPhase);
            }
            else
            {
                _phaseStabilitySamples++;
                if (_phaseEnteredAtUtc == default(DateTime))
                {
                    _phaseEnteredAtUtc = sampleTime;
                }
            }

            if (targetPhase == FlightPhase.Climb) _climbObserved = true;
            if (targetPhase == FlightPhase.Cruise) _cruiseObserved = true;
            if (targetPhase == FlightPhase.Descent) _descentObserved = true;

            AnnotatePhase(data, targetPhase, reason, onGround, isAirborneSample, fromPhase, candidatePhase, changed, confidence);
        }

        private void AnnotatePhase(SimData data, FlightPhase phase, string reason, bool onGround, bool isAirborneSample, FlightPhase fromPhase, FlightPhase candidatePhase, bool changed, string confidence)
        {
            data.OperationalPhaseCode = ToOperationalPhaseCode(phase);
            data.OperationalPhaseName = ToOperationalPhaseName(phase);
            data.OperationalPhaseReason = reason ?? string.Empty;
            data.HasBeenAirborne = _hasBeenAirborne;
            data.IsAirborneSample = isAirborneSample && !onGround;
            data.TouchdownDetected = _touchdownDetected && _touchdownTime != default(DateTime) && data.CapturedAtUtc >= _touchdownTime;
            data.GateReadyCandidate = phase == FlightPhase.Arrived || (_touchdownDetected && onGround && data.GroundSpeed <= 3d && data.ParkingBrake);
            data.PhaseTransitionFromCode = ToOperationalPhaseCode(fromPhase);
            data.PhaseTransitionToCode = ToOperationalPhaseCode(candidatePhase);
            data.PhaseTransitionReason = reason ?? string.Empty;
            data.PhaseTransitionChanged = changed;
            data.PhaseTransitionIndex = _phaseTransitionIndex;
            data.PhaseStabilitySamples = _phaseStabilitySamples;
            data.PhaseCandidateSamples = _candidatePhaseSamples;
            data.PhaseDwellSeconds = (int)Math.Max(0d, SecondsSince(_phaseEnteredAtUtc, data.CapturedAtUtc == default(DateTime) ? DateTime.UtcNow : data.CapturedAtUtc));
            data.PhaseDecisionConfidence = string.IsNullOrWhiteSpace(confidence) ? "confirmed" : confidence;
            data.PhaseMatrixVersion = "C16";

            var surface = BuildSurfaceContext(data, phase, onGround);
            data.SurfaceContextCode = surface.Code;
            data.SurfaceContextName = surface.Name;
            data.SurfaceContextReason = surface.Reason;
            data.RunwayCandidate = surface.RunwayCandidate;
            data.TaxiwayCandidate = surface.TaxiwayCandidate;
            data.GateAreaCandidate = surface.GateAreaCandidate;
            data.SurfaceContextReliable = surface.Reliable;
            data.SurfaceContextVersion = "C9";

            var runway = BuildRunwayTdzContext(data, phase, onGround, surface);
            data.RunwayContextCode = runway.Code;
            data.RunwayContextName = runway.Name;
            data.RunwayContextReason = runway.Reason;
            data.EstimatedRunwayIdent = runway.EstimatedRunwayIdent;
            data.EstimatedRunwayReciprocalIdent = runway.EstimatedRunwayReciprocalIdent;
            data.EstimatedRunwayHeadingDeg = runway.EstimatedRunwayHeadingDeg;
            data.RunwayHeadingDeltaDeg = runway.HeadingDeltaDeg;
            data.RunwayAlignedCandidate = runway.AlignedCandidate;
            data.RunwayEntryCandidate = runway.EntryCandidate;
            data.RunwayExitCandidate = runway.ExitCandidate;
            data.TakeoffRollCandidate = runway.TakeoffRollCandidate;
            data.LandingRollCandidate = runway.LandingRollCandidate;
            data.TouchdownZoneCandidate = runway.TouchdownZoneCandidate;
            data.TaxiwayProbable = runway.TaxiwayProbable;
            data.RunwayGeometryAvailable = runway.GeometryAvailable;
            data.RunwayContextReliable = runway.Reliable;
            data.RunwayContextVersion = runway.GeometryAvailable ? "C11C" : "C10";

            var procedure = BuildSurfaceProcedureEvidence(data, phase, onGround, surface, runway);
            data.SurfaceProcedurePhaseCode = procedure.Code;
            data.SurfaceProcedurePhaseName = procedure.Name;
            data.SurfaceProcedureEvidenceStatus = procedure.Status;
            data.SurfaceProcedureEvidenceSummary = procedure.Summary;
            data.SurfaceProcedureEvidenceFlags = procedure.Flags;
            data.SurfaceProcedureTaxiLightExpected = procedure.TaxiLightExpected;
            data.SurfaceProcedureStrobeExpected = procedure.StrobeExpected;
            data.SurfaceProcedureLandingLightExpected = procedure.LandingLightExpected;
            data.SurfaceProcedureXpdrAltExpected = procedure.XpdrAltExpected;
            data.SurfaceProcedureBeaconExpected = procedure.BeaconExpected;
            data.SurfaceProcedureNavExpected = procedure.NavExpected;
            data.SurfaceProcedureEvidenceVersion = "C11F";

            var checklist = BuildPhaseChecklist(data, phase, onGround);
            data.PhaseChecklistStatus = checklist.Status;
            data.PhaseChecklistSummary = checklist.Summary;
            data.PhaseChecklistRequired = checklist.Required;
            data.PhaseChecklistSatisfied = checklist.Satisfied;
            data.PhaseChecklistMissing = checklist.Missing;
            data.PhaseChecklistWarnings = checklist.Warnings;

            var audit = BuildPhaseAudit(data, phase, onGround, isAirborneSample, changed, confidence);
            data.PhaseAuditStatus = audit.Status;
            data.PhaseAuditSummary = audit.Summary;
            data.PhaseAuditFlags = audit.Flags;
            data.PhaseAuditVersion = "C4";

            var review = BuildPhaseReviewContract(phase);
            data.PhaseExpectedActions = review.ExpectedActions;
            data.PhaseMeasuredMetrics = review.MeasuredMetrics;
            data.PhaseScoringHints = review.ScoringHints;
            data.PhaseReviewQuestion = review.ReviewQuestion;
            data.PhaseReviewVersion = "C5";

            var prevalidation = BuildPhasePrevalidation(data, phase, onGround, isAirborneSample, changed, confidence);
            data.PhasePrevalidationStatus = prevalidation.Status;
            data.PhasePrevalidationSummary = prevalidation.Summary;
            data.PhasePrevalidationFlags = prevalidation.Flags;
            data.PhasePrevalidationVersion = "C6";
        }

        private sealed class SurfaceContextEvidence
        {
            public string Code { get; set; } = "UNKNOWN";
            public string Name { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public bool RunwayCandidate { get; set; }
            public bool TaxiwayCandidate { get; set; }
            public bool GateAreaCandidate { get; set; }
            public bool Reliable { get; set; }
        }

        private static SurfaceContextEvidence BuildSurfaceContext(SimData data, FlightPhase phase, bool onGround)
        {
            var gs = SafeNumber(data.GroundSpeed);
            var ias = SafeNumber(data.IndicatedAirspeed);
            var agl = ResolveAgl(data);
            var touchdown = data.TouchdownDetected || (data.HasBeenAirborne && (phase == FlightPhase.Landing || phase == FlightPhase.Arrived));

            var evidence = new SurfaceContextEvidence
            {
                Code = "UNKNOWN",
                Name = "Superficie no determinada",
                Reason = "Sin geometria aeroportuaria; inferencia por fase/velocidad/telemetria",
                Reliable = false
            };

            if (!onGround)
            {
                evidence.Code = "AIRBORNE";
                evidence.Name = "En vuelo";
                evidence.Reason = "onGround=false o energia de vuelo; no aplica pista/rodaje";
                evidence.Reliable = true;
                return evidence;
            }

            if (gs <= 3d && data.ParkingBrake)
            {
                evidence.Code = phase == FlightPhase.Arrived || touchdown ? "GATE_AREA" : "PARKING_GATE";
                evidence.Name = phase == FlightPhase.Arrived || touchdown ? "Gate/plataforma llegada" : "Parking/gate salida";
                evidence.Reason = "onGround, GS<=3 y parking brake ON";
                evidence.GateAreaCandidate = true;
                evidence.Reliable = true;
                return evidence;
            }

            if (phase == FlightPhase.Takeoff || (!touchdown && (gs >= 35d || ias >= 35d)))
            {
                evidence.Code = "RUNWAY_TAKEOFF_ROLL";
                evidence.Name = "Pista / carrera de despegue probable";
                evidence.Reason = "fase TO o energia de carrera en tierra; sin mapa de pista exacto";
                evidence.RunwayCandidate = true;
                evidence.Reliable = gs >= 35d || ias >= 35d;
                return evidence;
            }

            if (phase == FlightPhase.Landing || (touchdown && gs > 35d))
            {
                evidence.Code = "RUNWAY_LANDING_ROLL";
                evidence.Name = "Pista / carrera de aterrizaje probable";
                evidence.Reason = "touchdown/landing roll con velocidad alta; sin mapa de pista exacto";
                evidence.RunwayCandidate = true;
                evidence.Reliable = true;
                return evidence;
            }

            if (phase == FlightPhase.PushbackTaxi || phase == FlightPhase.Taxi || (gs > 3d && gs <= 35d))
            {
                evidence.Code = touchdown ? "TAXIWAY_IN" : "TAXIWAY_OUT";
                evidence.Name = touchdown ? "Rodaje llegada probable" : "Rodaje salida probable";
                evidence.Reason = "onGround y GS 3-35 kt; taxiway inferido sin geometria aeroportuaria";
                evidence.TaxiwayCandidate = true;
                evidence.Reliable = true;
                return evidence;
            }

            if (agl <= 15d)
            {
                evidence.Code = "GROUND_STOPPED";
                evidence.Name = "Suelo detenido/intermedio";
                evidence.Reason = "onGround/AGL bajo sin condicion suficiente para runway/taxi/gate";
                evidence.Reliable = true;
            }

            return evidence;
        }

        private sealed class SurfaceProcedureEvidence
        {
            public string Code { get; set; } = "UNKNOWN";
            public string Name { get; set; } = string.Empty;
            public string Status { get; set; } = "OBSERVE";
            public string Summary { get; set; } = string.Empty;
            public string Flags { get; set; } = string.Empty;
            public bool TaxiLightExpected { get; set; }
            public bool StrobeExpected { get; set; }
            public bool LandingLightExpected { get; set; }
            public bool XpdrAltExpected { get; set; }
            public bool BeaconExpected { get; set; }
            public bool NavExpected { get; set; }
        }

        private static SurfaceProcedureEvidence BuildSurfaceProcedureEvidence(SimData data, FlightPhase phase, bool onGround, SurfaceContextEvidence surface, RunwayTdzEvidence runway)
        {
            var evidence = new SurfaceProcedureEvidence
            {
                Code = "UNKNOWN",
                Name = "Procedimiento operacional no clasificado",
                Status = "OBSERVE",
                Summary = "Evidencia C11D5 pendiente",
                Flags = string.Empty
            };

            if (data == null)
            {
                evidence.Flags = "NO_TELEMETRY";
                return evidence;
            }

            var gs = SafeNumber(data.GroundSpeed);
            var onRunway = data.FacilityOnRunwayCandidate || data.RunwayCandidate || data.RunwayEntryCandidate || data.TakeoffRollCandidate || data.LandingRollCandidate;
            var onTaxiway = data.FacilityTaxiwayCandidate || data.TaxiwayProbable || (surface != null && surface.TaxiwayCandidate);
            var atGate = data.FacilityGateAreaCandidate || data.GateAreaCandidate || data.GateReadyCandidate || (surface != null && surface.GateAreaCandidate);
            var afterTouchdown = data.TouchdownDetected || phase == FlightPhase.Landing || phase == FlightPhase.Arrived || (data.HasBeenAirborne && onGround && phase == FlightPhase.Taxi);
            var approachLike = !onGround && (phase == FlightPhase.Approach || data.OperationalPhaseCode == "APP" || ResolveAgl(data) <= 3000d);
            var airborneLike = !onGround && !approachLike;

            if (atGate && !afterTouchdown)
            {
                var readyTaxiOut = data.TaxiLightsOn && (!data.ParkingBrake || gs > 1.5d);
                var startupAtGate = !readyTaxiOut && (data.BeaconLightsOn || data.NavLightsOn || data.EngineOneRunning || data.EngineTwoRunning || data.BatteryMasterOn || data.AvionicsMasterOn);
                if (readyTaxiOut)
                {
                    evidence.Code = "READY_TAXI_OUT";
                    evidence.Name = "Listo para rodaje salida";
                    evidence.Summary = "Puertas cerradas/listo para salir: TAXI encendida y freno liberado o movimiento inicial; comienza rodaje hacia pista.";
                    evidence.TaxiLightExpected = true;
                    evidence.BeaconExpected = true;
                    evidence.NavExpected = true;
                }
                else if (startupAtGate)
                {
                    evidence.Code = "STARTUP_AT_GATE";
                    evidence.Name = "Preparacion/encendido en gate";
                    evidence.Summary = "Evidencia de preparacion en gate: BCN/NAV o energia/motores activos; aun no es listo rodaje hasta encender TAXI y soltar freno/iniciar movimiento.";
                    evidence.BeaconExpected = true;
                    evidence.NavExpected = true;
                }
                else
                {
                    evidence.Code = "GATE_ORIGIN";
                    evidence.Name = "Gate/plataforma salida";
                    evidence.Summary = "Evidencia salida en gate: avion detenido y parking brake ON; TAXI marcara el inicio real de salida a rodaje.";
                }
            }
            else if (atGate && afterTouchdown)
            {
                evidence.Code = "GATE_ARRIVAL";
                evidence.Name = "Gate/plataforma llegada";
                evidence.Summary = "Evidencia llegada a gate: taxi/landing/strobe apagadas y parking brake ON antes de finalizar.";
            }
            else if (afterTouchdown && onGround && (onTaxiway || data.RunwayExitCandidate || (gs > 3d && gs <= 35d && !data.LandingRollCandidate)))
            {
                evidence.Code = "TAXI_IN";
                evidence.Name = "Rodaje llegada";
                evidence.Summary = "Evidencia taxi-in post aterrizaje: TAXI esperada ON; STROBE/LANDING esperadas OFF despues de abandonar pista.";
                evidence.TaxiLightExpected = true;
                evidence.BeaconExpected = true;
                evidence.NavExpected = true;
            }
            else if (onRunway || phase == FlightPhase.Takeoff || phase == FlightPhase.Landing)
            {
                evidence.Code = afterTouchdown ? "RUNWAY_ARRIVAL" : "RUNWAY_DEPARTURE";
                evidence.Name = afterTouchdown ? "Pista aterrizaje/salida" : "Pista entrada/despegue";
                evidence.Summary = "Evidencia de pista real/probable: STROBE y LANDING esperadas ON; XPDR ALT esperado antes de carrera/despegue.";
                evidence.StrobeExpected = true;
                evidence.LandingLightExpected = true;
                evidence.XpdrAltExpected = true;
                evidence.BeaconExpected = true;
                evidence.NavExpected = true;
            }
            else if (onTaxiway || phase == FlightPhase.PushbackTaxi || phase == FlightPhase.Taxi || (onGround && gs > 3d && gs <= 35d))
            {
                evidence.Code = afterTouchdown ? "TAXI_IN" : "TAXI_OUT";
                evidence.Name = afterTouchdown ? "Rodaje llegada" : "Rodaje salida";
                evidence.Summary = afterTouchdown
                    ? "Evidencia taxi-in: TAXI esperada ON; STROBE/LANDING esperadas OFF despues de abandonar pista."
                    : "Evidencia taxi-out: TAXI esperada ON; STROBE/LANDING deben esperar hasta entrada/alineamiento de pista.";
                evidence.TaxiLightExpected = true;
                evidence.BeaconExpected = true;
                evidence.NavExpected = true;
            }
            else if (approachLike)
            {
                evidence.Code = "APPROACH_FINAL";
                evidence.Name = "Aproximacion/final";
                evidence.Summary = "Evidencia aproximacion: LANDING y STROBE esperadas ON antes del aterrizaje; XPDR ALT esperado.";
                evidence.StrobeExpected = true;
                evidence.LandingLightExpected = true;
                evidence.XpdrAltExpected = true;
                evidence.BeaconExpected = true;
                evidence.NavExpected = true;
            }
            else if (airborneLike)
            {
                evidence.Code = "AIRBORNE";
                evidence.Name = "En vuelo";
                evidence.Summary = "Evidencia en vuelo: BCN/NAV y XPDR ALT esperados; LANDING segun altitud/procedimiento.";
                evidence.XpdrAltExpected = true;
                evidence.BeaconExpected = true;
                evidence.NavExpected = true;
            }
            else
            {
                evidence.Code = "GROUND_INTERMEDIATE";
                evidence.Name = "Suelo intermedio";
                evidence.Summary = "Evidencia suelo intermedio sin fase operacional suficiente para reglaje automatico.";
            }

            var flags = new List<string>();

            if (evidence.TaxiLightExpected && !data.TaxiLightsOn) flags.Add("TAXI_EXPECTED_OFF_OR_MISSING");
            if (!evidence.TaxiLightExpected && data.TaxiLightsOn && (evidence.Code == "GATE_ORIGIN" || evidence.Code == "STARTUP_AT_GATE" || evidence.Code == "GATE_ARRIVAL")) flags.Add("TAXI_ON_AT_GATE");

            if (evidence.StrobeExpected && !data.StrobeLightsOn) flags.Add("STROBE_EXPECTED_OFF_OR_MISSING");
            if (!evidence.StrobeExpected && data.StrobeLightsOn && (evidence.Code == "TAXI_OUT" || evidence.Code == "TAXI_IN" || evidence.Code == "GATE_ORIGIN" || evidence.Code == "STARTUP_AT_GATE" || evidence.Code == "GATE_ARRIVAL")) flags.Add("STROBE_ON_OUTSIDE_RUNWAY");

            if (evidence.LandingLightExpected && !data.LandingLightsOn) flags.Add("LANDING_EXPECTED_OFF_OR_MISSING");
            if (!evidence.LandingLightExpected && data.LandingLightsOn && (evidence.Code == "TAXI_OUT" || evidence.Code == "TAXI_IN" || evidence.Code == "GATE_ORIGIN" || evidence.Code == "STARTUP_AT_GATE" || evidence.Code == "GATE_ARRIVAL")) flags.Add("LANDING_ON_OUTSIDE_RUNWAY");

            if (evidence.XpdrAltExpected && !data.TransponderCharlieMode) flags.Add("XPDR_ALT_EXPECTED");
            if (evidence.BeaconExpected && !data.BeaconLightsOn) flags.Add("BEACON_EXPECTED");
            if (evidence.NavExpected && !data.NavLightsOn) flags.Add("NAV_EXPECTED");
            if ((evidence.Code == "GATE_ORIGIN" || evidence.Code == "STARTUP_AT_GATE" || evidence.Code == "GATE_ARRIVAL") && !data.ParkingBrake) flags.Add("PARKING_BRAKE_EXPECTED_AT_GATE");
            if (data.DoorOpen && (evidence.Code == "READY_TAXI_OUT" || evidence.Code == "TAXI_OUT" || evidence.Code == "RUNWAY_DEPARTURE" || evidence.Code == "AIRBORNE" || evidence.Code == "APPROACH_FINAL" || evidence.Code == "RUNWAY_ARRIVAL" || evidence.Code == "TAXI_IN")) flags.Add("DOOR_OPEN_OPERATIONAL_PHASE");

            if (flags.Count == 0)
            {
                evidence.Status = "OK";
                evidence.Flags = string.Empty;
            }
            else
            {
                evidence.Status = "EVIDENCE_REVIEW";
                evidence.Flags = string.Join(",", flags);
            }

            evidence.Summary += " Luces: NAV=" + BoolText(data.NavLightsOn) +
                                " BCN=" + BoolText(data.BeaconLightsOn) +
                                " STB=" + BoolText(data.StrobeLightsOn) +
                                " TAXI=" + BoolText(data.TaxiLightsOn) +
                                " LAND=" + BoolText(data.LandingLightsOn) +
                                " XPDR_ALT=" + BoolText(data.TransponderCharlieMode) +
                                "; MSFS=" + FirstNonEmpty(data.FacilityNearestRunwayAirportIcao, "N/D") +
                                " RWY=" + FirstNonEmpty(data.FacilityNearestRunwayIdent, "N/D") +
                                " lat=" + data.FacilityRunwayLateralOffsetMeters.ToString("F0", CultureInfo.InvariantCulture) +
                                "m hdgErr=" + data.FacilityRunwayHeadingErrorDeg.ToString("F0", CultureInfo.InvariantCulture) + "deg.";
            return evidence;
        }

        private static string BoolText(bool value)
        {
            return value ? "ON" : "OFF";
        }

        private static string FirstNonEmpty(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private sealed class RunwayTdzEvidence
        {
            public string Code { get; set; } = "UNKNOWN";
            public string Name { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public string EstimatedRunwayIdent { get; set; } = string.Empty;
            public string EstimatedRunwayReciprocalIdent { get; set; } = string.Empty;
            public double EstimatedRunwayHeadingDeg { get; set; }
            public double HeadingDeltaDeg { get; set; }
            public bool AlignedCandidate { get; set; }
            public bool EntryCandidate { get; set; }
            public bool ExitCandidate { get; set; }
            public bool TakeoffRollCandidate { get; set; }
            public bool LandingRollCandidate { get; set; }
            public bool TouchdownZoneCandidate { get; set; }
            public bool TaxiwayProbable { get; set; }
            public bool GeometryAvailable { get; set; }
            public bool Reliable { get; set; }
        }

        private RunwayTdzEvidence BuildRunwayTdzContext(SimData data, FlightPhase phase, bool onGround, SurfaceContextEvidence surface)
        {
            var gs = SafeNumber(data.GroundSpeed);
            var ias = SafeNumber(data.IndicatedAirspeed);
            var agl = ResolveAgl(data);
            var heading = NormalizeHeading(data.Heading);
            var touchdown = data.TouchdownDetected;
            var afterTouchdown = _touchdownDetected || data.TouchdownDetected || phase == FlightPhase.Landing || phase == FlightPhase.Arrived || (data.HasBeenAirborne && onGround && phase == FlightPhase.Taxi);
            var runwayEnergy = onGround && (surface.RunwayCandidate || gs >= 35d || ias >= 35d || phase == FlightPhase.Takeoff || phase == FlightPhase.Landing);

            if (onGround && !afterTouchdown && runwayEnergy && double.IsNaN(_departureRunwayHeadingDeg))
            {
                _departureRunwayHeadingDeg = heading;
            }

            if (onGround && afterTouchdown && runwayEnergy && double.IsNaN(_arrivalRunwayHeadingDeg))
            {
                _arrivalRunwayHeadingDeg = heading;
            }

            var hasFacilityRunway = data.FacilityRunwayGeometryAvailable && !string.IsNullOrWhiteSpace(data.FacilityNearestRunwayIdent);
            var axis = hasFacilityRunway ? NormalizeHeading(data.FacilityNearestRunwayHeadingDeg) : ResolveRunwayAxis(phase, afterTouchdown, heading);
            var delta = hasFacilityRunway ? SafeNumber(data.FacilityRunwayHeadingErrorDeg) : SmallestHeadingDifference(heading, axis);
            var aligned = hasFacilityRunway ? data.FacilityRunwayAlignedCandidate : delta <= 12d;
            var runwayIdent = hasFacilityRunway ? data.FacilityNearestRunwayIdent : RunwayIdentFromHeading(axis);
            var reciprocal = hasFacilityRunway ? data.FacilityNearestRunwayReciprocalIdent : RunwayIdentFromHeading(axis + 180d);
            var geometryReason = hasFacilityRunway
                ? "C11C geometria MSFS Facilities: " + data.FacilityRunwayGeometrySummary
                : "C10 sin geometria aeroportuaria exacta; inferencia por rumbo, velocidad, fase y touchdown";

            var evidence = new RunwayTdzEvidence
            {
                Code = "UNKNOWN",
                Name = "Contexto pista/rodaje no determinado",
                Reason = geometryReason,
                EstimatedRunwayHeadingDeg = axis,
                HeadingDeltaDeg = delta,
                EstimatedRunwayIdent = runwayIdent,
                EstimatedRunwayReciprocalIdent = reciprocal,
                GeometryAvailable = hasFacilityRunway,
                Reliable = false
            };

            if (!onGround)
            {
                if (afterTouchdown)
                {
                    evidence.Code = "AIRBORNE_AFTER_TOUCHDOWN_UNEXPECTED";
                    evidence.Name = "En vuelo despues de touchdown detectado";
                    evidence.Reason = "Se detecto energia aérea posterior a touchdown; revisar salto/pausa/sim";
                    evidence.Reliable = false;
                    return evidence;
                }

                if (agl > 0d && agl <= 3000d && gs >= 55d)
                {
                    evidence.Code = phase == FlightPhase.Approach ? "APPROACH_RUNWAY_ALIGNED_CANDIDATE" : "AIRBORNE_RUNWAY_AXIS_CANDIDATE";
                    evidence.Name = phase == FlightPhase.Approach ? "Aproximacion alineada candidata" : "Eje de pista probable en vuelo";
                    evidence.Reason = hasFacilityRunway
                        ? "AGL bajo y GS de vuelo; eje calculado con runway MSFS Facilities"
                        : "AGL bajo y GS de vuelo; eje estimado por heading actual hasta disponer de geometria/navdata";
                    evidence.AlignedCandidate = aligned;
                    evidence.Reliable = hasFacilityRunway ? aligned : (phase == FlightPhase.Approach || agl <= 1500d);
                    return evidence;
                }

                evidence.Code = "AIRBORNE_NO_RUNWAY_CONTEXT";
                evidence.Name = "En vuelo sin contexto de pista";
                evidence.Reason = "Muestra airborne fuera de capa de aproximacion/despegue";
                evidence.Reliable = true;
                return evidence;
            }

            if (surface.GateAreaCandidate || (gs <= 3d && data.ParkingBrake))
            {
                evidence.Code = afterTouchdown ? "GATE_AREA" : "APRON_PARKING";
                evidence.Name = afterTouchdown ? "Gate/plataforma llegada" : "Plataforma/parking salida";
                evidence.Reason = "GS<=3 y parking brake ON; no se considera pista/rodaje";
                evidence.Reliable = true;
                return evidence;
            }

            if (!afterTouchdown)
            {
                if (runwayEnergy || (hasFacilityRunway && data.FacilityOnRunwayCandidate && gs > 3d))
                {
                    evidence.Code = gs < 45d ? "RUNWAY_ENTRY_OR_LINEUP" : "RUNWAY_TAKEOFF_ROLL";
                    evidence.Name = gs < 45d ? "Entrada/alineamiento pista probable" : "Carrera de despegue probable";
                    evidence.Reason = hasFacilityRunway
                        ? "Antes de airborne: aeronave sobre/near RWY " + runwayIdent + " segun MSFS Facilities"
                        : "Antes de airborne: velocidad/IAS/phase compatibles con pista; runway exacta estimada por heading";
                    evidence.EntryCandidate = gs < 45d;
                    evidence.TakeoffRollCandidate = gs >= 35d || ias >= 35d || phase == FlightPhase.Takeoff;
                    evidence.AlignedCandidate = aligned;
                    evidence.Reliable = hasFacilityRunway ? data.FacilityOnRunwayCandidate : (evidence.TakeoffRollCandidate || (evidence.EntryCandidate && aligned));
                    _wasRunwayCandidate = true;
                    return evidence;
                }

                if (gs > 3d && gs < 35d)
                {
                    evidence.Code = "TAXIWAY_OUT_PROBABLE";
                    evidence.Name = "Rodaje salida probable";
                    evidence.Reason = "Antes de airborne con GS 3-35 kt fuera de carrera de pista";
                    evidence.TaxiwayProbable = true;
                    evidence.Reliable = true;
                    return evidence;
                }
            }

            if (afterTouchdown)
            {
                if (touchdown || (phase == FlightPhase.Landing && gs >= 40d))
                {
                    evidence.Code = touchdown ? "TOUCHDOWN_ZONE_CANDIDATE" : "LANDING_ROLL";
                    evidence.Name = touchdown ? "TDZ candidato" : "Carrera de aterrizaje";
                    evidence.Reason = touchdown
                        ? (hasFacilityRunway ? "Touchdown detectado; TDZ candidato calculado contra threshold MSFS Facilities" : "Touchdown detectado; sin threshold georeferenciado se marca TDZ candidato, no TDZ exacto")
                        : (hasFacilityRunway ? "Post-touchdown con velocidad de pista; runway MSFS Facilities" : "Post-touchdown con velocidad de pista; runway exacta estimada por heading");
                    evidence.TouchdownZoneCandidate = hasFacilityRunway ? data.FacilityTouchdownZoneCandidate : touchdown;
                    evidence.LandingRollCandidate = true;
                    evidence.AlignedCandidate = aligned;
                    evidence.Reliable = hasFacilityRunway ? data.FacilityOnRunwayCandidate : true;
                    _wasRunwayCandidate = true;
                    return evidence;
                }

                if (gs > 3d && gs <= 35d)
                {
                    evidence.Code = _wasRunwayCandidate && !_runwayExitDetected ? "RUNWAY_EXIT_CANDIDATE" : "TAXIWAY_IN_PROBABLE";
                    evidence.Name = _wasRunwayCandidate && !_runwayExitDetected ? "Salida de pista candidata" : "Rodaje llegada probable";
                    evidence.Reason = _wasRunwayCandidate && !_runwayExitDetected
                        ? "Post-landing con GS taxi; probable salida de pista hacia calle de rodaje"
                        : "Post-landing con GS 3-35 kt; taxiway inferido sin geometria aeroportuaria";
                    evidence.ExitCandidate = _wasRunwayCandidate && !_runwayExitDetected;
                    evidence.TaxiwayProbable = true;
                    evidence.Reliable = true;
                    if (evidence.ExitCandidate) _runwayExitDetected = true;
                    return evidence;
                }
            }

            if (gs > 3d && gs <= 35d)
            {
                evidence.Code = "TAXIWAY_PROBABLE";
                evidence.Name = "Rodaje probable";
                evidence.Reason = "OnGround con velocidad de taxi; sin geometria para nombrar taxiway";
                evidence.TaxiwayProbable = true;
                evidence.Reliable = true;
                return evidence;
            }

            evidence.Code = "GROUND_INTERMEDIATE";
            evidence.Name = "Suelo intermedio";
            evidence.Reason = "OnGround sin velocidad/condicion suficiente para clasificar pista, rodaje o gate";
            evidence.Reliable = true;
            return evidence;
        }

        private double ResolveRunwayAxis(FlightPhase phase, bool afterTouchdown, double heading)
        {
            if (afterTouchdown && !double.IsNaN(_arrivalRunwayHeadingDeg))
            {
                return _arrivalRunwayHeadingDeg;
            }

            if (!afterTouchdown && !double.IsNaN(_departureRunwayHeadingDeg))
            {
                return _departureRunwayHeadingDeg;
            }

            if ((phase == FlightPhase.Approach || phase == FlightPhase.Landing) && !double.IsNaN(_arrivalRunwayHeadingDeg))
            {
                return _arrivalRunwayHeadingDeg;
            }

            return heading;
        }

        private static double NormalizeHeading(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0d;
            var heading = value % 360d;
            if (heading < 0d) heading += 360d;
            return heading;
        }

        private static double SmallestHeadingDifference(double a, double b)
        {
            var diff = Math.Abs(NormalizeHeading(a) - NormalizeHeading(b)) % 360d;
            return diff > 180d ? 360d - diff : diff;
        }

        private static string RunwayIdentFromHeading(double heading)
        {
            var normalized = NormalizeHeading(heading);
            var number = (int)Math.Round(normalized / 10d, MidpointRounding.AwayFromZero);
            if (number <= 0) number = 36;
            if (number > 36) number -= 36;
            return number.ToString("00", CultureInfo.InvariantCulture);
        }

        private sealed class PhasePrevalidationEvidence
        {
            public string Status { get; set; } = "PENDING";
            public string Summary { get; set; } = string.Empty;
            public string Flags { get; set; } = string.Empty;
        }

        private static PhasePrevalidationEvidence BuildPhasePrevalidation(SimData data, FlightPhase phase, bool onGround, bool isAirborneSample, bool changed, string confidence)
        {
            var flags = new List<string>();
            var agl = ResolveAgl(data);
            var msl = ResolveMsl(data);
            var gs = SafeNumber(data.GroundSpeed);
            var vs = SafeNumber(data.VerticalSpeed);
            var code = ToOperationalPhaseCode(phase);

            if (data.CapturedAtUtc == default(DateTime))
            {
                flags.Add("MissingTimestamp");
            }

            if (!data.IsAltitudeReliable)
            {
                flags.Add("AltitudeUnreliable");
            }

            if (onGround && agl > 15d)
            {
                flags.Add("GroundAglAboveZero");
            }

            if (!onGround && IsGroundOperationalCode(code))
            {
                flags.Add("AirborneButGroundPhase");
            }

            if (onGround && IsAirborneOperationalCode(code))
            {
                flags.Add("OnGroundButAirbornePhase");
            }

            if (!onGround && isAirborneSample && agl <= 20d && phase != FlightPhase.Landing)
            {
                flags.Add("AirborneLowAglReview");
            }

            if (phase == FlightPhase.Arrived)
            {
                if (gs > 3d) flags.Add("GateGsTooHigh");
                if (!data.ParkingBrake) flags.Add("GateParkingBrakeOff");
                if (!data.HasBeenAirborne && !data.TouchdownDetected) flags.Add("GateWithoutAirborneEvidence");
            }

            if (string.Equals(confidence, "pending", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("PendingPhaseTransition");
            }

            if (!string.IsNullOrWhiteSpace(data.PhaseChecklistMissing))
            {
                flags.Add("ChecklistMissing");
            }

            if (Math.Abs(vs) > 7000d)
            {
                flags.Add("VerticalSpeedExtreme");
            }

            if (msl < -1500d || msl > 70000d)
            {
                flags.Add("MslOutOfRange");
            }

            var hasBlock = flags.Any(flag =>
                string.Equals(flag, "OnGroundButAirbornePhase", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(flag, "AirborneButGroundPhase", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(flag, "GateWithoutAirborneEvidence", StringComparison.OrdinalIgnoreCase));
            var hasWait = flags.Any(flag => flag.StartsWith("Gate", StringComparison.OrdinalIgnoreCase) || string.Equals(flag, "PendingPhaseTransition", StringComparison.OrdinalIgnoreCase));
            var hasWarn = flags.Count > 0;

            var status = hasBlock ? "BLOCK" : hasWait ? "WAIT" : hasWarn ? "WARN" : "READY";
            var summary = status == "READY"
                ? "C6 listo para prueba: fase, altitud y checklist coherentes"
                : status == "WAIT"
                    ? "C6 esperando condiciones de fase/gate"
                    : status == "WARN"
                        ? "C6 con advertencias: revisar antes de Web/Supabase"
                        : "C6 bloqueante: fase contradice telemetria";

            return new PhasePrevalidationEvidence
            {
                Status = status,
                Summary = summary,
                Flags = string.Join(",", flags.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(flag => flag))
            };
        }

        private sealed class PhaseReviewContract
        {
            public string ExpectedActions { get; set; } = string.Empty;
            public string MeasuredMetrics { get; set; } = string.Empty;
            public string ScoringHints { get; set; } = string.Empty;
            public string ReviewQuestion { get; set; } = string.Empty;
        }

        private static PhaseReviewContract BuildPhaseReviewContract(FlightPhase phase)
        {
            switch (phase)
            {
                case FlightPhase.PreFlight:
                    return new PhaseReviewContract
                    {
                        ExpectedActions = "Despacho cargado; avion/matricula/origen correctos; parking brake ON; motores OFF o condicion cold&dark segun perfil; combustible/payload/OFP disponibles.",
                        MeasuredMetrics = "dispatch, aircraft_match, airport_match, parking_brake, engines, battery/avionics si confiables, fuel_start, payload, qnh, lights capability-aware.",
                        ScoringHints = "Reglas PRE/gate; unsupported=N/D sin penalizacion; score oficial solo Web/Supabase.",
                        ReviewQuestion = "¿El prevuelo reconoce aeropuerto, avion, matricula, freno, combustible y cold&dark sin exigir variables unsupported?"
                    };
                case FlightPhase.PushbackTaxi:
                    return new PhaseReviewContract
                    {
                        ExpectedActions = "Soltar parking brake; iniciar rodaje bajo control; mantener velocidad taxi razonable; usar luces segun perfil; XPDR solo si soportado.",
                        MeasuredMetrics = "block_off, gs_taxi, parking_brake_off, taxi_light, beacon/nav/strobe, flaps, com/pic, xpdr capability-aware.",
                        ScoringHints = "Taxi se evalua por OnGround+GS+freno; XPDR/puertas/luces solo con capability=true.",
                        ReviewQuestion = "¿Taxi out aparece despues de soltar freno y antes de takeoff roll, con AGL=0 y MSL real?"
                    };
                case FlightPhase.Takeoff:
                    return new PhaseReviewContract
                    {
                        ExpectedActions = "Entrar pista; acelerar; rotar; confirmar aire por transicion OnGround true→false; mantener configuracion de despegue.",
                        MeasuredMetrics = "takeoff_roll, airborne, gs/ias, agl>20, vs, flaps, lights, fuel_at_takeoff, heading/runway aproximado.",
                        ScoringHints = "TO no se confirma solo por MSL; requiere energia + WOW/AGL; Web evalua configuracion de despegue con capabilities.",
                        ReviewQuestion = "¿El XML registra TAKEOFF_ROLL y AIRBORNE en orden correcto?"
                    };
                case FlightPhase.Climb:
                    return new PhaseReviewContract
                    {
                        ExpectedActions = "Ascender estabilizado; limpiar configuracion; mantener IAS/VS coherente; seguir ruta inicial.",
                        MeasuredMetrics = "altitude_msl_trend, agl, vs_positive, ias/max_speed, fuel_flow, lights/ap si soportado, eventos de overspeed/stall.",
                        ScoringHints = "Climb usa MSL para perfil vertical y AGL solo como contexto; si OnGround vuelve true debe salir de CLB.",
                        ReviewQuestion = "¿CLB termina al estabilizar crucero, iniciar descenso o tocar tierra, sin quedar pegado?"
                    };
                case FlightPhase.Cruise:
                    return new PhaseReviewContract
                    {
                        ExpectedActions = "Mantener nivel/FL; controlar combustible, velocidad, ruta y PIC checks.",
                        MeasuredMetrics = "msl/pressure_altitude/flight_level, stable_vs, cruise_ias/gs, fuel, route_distance, sim_rate/pause, pic_com2.",
                        ScoringHints = "Sobre transicion mostrar FL; MSL se conserva para max_altitude; AGL no define altitud maxima.",
                        ReviewQuestion = "¿CRZ solo aparece con VS estable y altura suficiente, no durante taxi/approach?"
                    };
                case FlightPhase.Descent:
                    return new PhaseReviewContract
                    {
                        ExpectedActions = "Iniciar descenso sostenido hacia destino; preparar aproximacion; controlar velocidad y luces.",
                        MeasuredMetrics = "negative_vs, decreasing_msl, distance_to_destination, speed_below_limits, qnh, landing_lights/flaps/gear if supported.",
                        ScoringHints = "DES requiere tendencia MSL o VS negativa confirmada; no penalizar gear/lights si unsupported.",
                        ReviewQuestion = "¿DES aparece antes de APP y cuando la altitud MSL baja de forma sostenida?"
                    };
                case FlightPhase.Approach:
                    return new PhaseReviewContract
                    {
                        ExpectedActions = "Configurar aproximacion; reducir velocidad; flaps/gear/landing lights segun aeronave; estabilizar antes de final.",
                        MeasuredMetrics = "agl<3000, distance_to_destination, vs, ias, flaps, gear, landing_lights, qnh_destino, stabilized_approach evidence.",
                        ScoringHints = "APP usa AGL+distancia+VS; MSL no reemplaza AGL; variables unsupported quedan N/D.",
                        ReviewQuestion = "¿APP aparece en final/terminal y no durante crucero alto o rodaje?"
                    };
                case FlightPhase.Landing:
                    return new PhaseReviewContract
                    {
                        ExpectedActions = "Touchdown; mantener control direccional; desacelerar; salir de pista.",
                        MeasuredMetrics = "touchdown false→true, landing_vs, landing_g, ias/gs_touchdown, pitch/bank, reverser/spoilers si soportado, runway_exit.",
                        ScoringHints = "LDG se confirma por transicion aire→tierra; G absurda se descarta; hard landing lo evalua Web/Supabase.",
                        ReviewQuestion = "¿LDG se registra por touchdown real y no por AGL=0 en gate?"
                    };
                case FlightPhase.Taxi:
                    return new PhaseReviewContract
                    {
                        ExpectedActions = "Rodaje post-aterrizaje; abandonar pista; apagar luces segun perfil; dirigirse a gate.",
                        MeasuredMetrics = "on_ground_after_touchdown, gs_taxi_in, runway_vacated, taxi_light, landing/strobe off, fuel_remaining, xpdr standby si soportado.",
                        ScoringHints = "Taxi in ocurre solo despues de touchdown; XPDR standby no penaliza si capability=false.",
                        ReviewQuestion = "¿TAX_IN aparece despues de LDG y antes de GATE?"
                    };
                case FlightPhase.Arrived:
                    return new PhaseReviewContract
                    {
                        ExpectedActions = "Detener en gate; parking brake ON; motores OFF/cold&dark segun perfil; finalizar manualmente en ACARS.",
                        MeasuredMetrics = "on_ground, gs<=3, parking_brake_on, engines_off/cold_dark, final_fuel, doors if supported, manual_gate_closeout.",
                        ScoringHints = "GATE habilita cierre manual; no depende de XPDR; Web/Supabase decide score oficial.",
                        ReviewQuestion = "¿GATE se habilita solo detenido, en tierra, con freno y despues de touchdown?"
                    };
                default:
                    return new PhaseReviewContract
                    {
                        ExpectedActions = "Recolectar telemetria base y mantener vuelo activo hasta decision manual.",
                        MeasuredMetrics = "connection, position, altitude_msl, agl, gs, vs, fuel, systems capability-aware.",
                        ScoringHints = "Sin score en ACARS; evidencia RAW para Web/Supabase.",
                        ReviewQuestion = "¿La fase desconocida tiene suficiente evidencia para clasificarla despues?"
                    };
            }
        }

        private bool ResolveOperationalOnGround(SimData data, double agl, double gs, double ias, double vs)
        {
            if (data == null)
            {
                return true;
            }

            var msl = ResolveMsl(data);
            var ground = SafeNumber(data.GroundElevationFeet);
            var mslAboveGround = ground > 0d ? Math.Max(0d, msl - ground) : 0d;
            var highEnergyAirborne = (gs >= 60d || ias >= 55d)
                && !data.ParkingBrake
                && (agl > 20d || mslAboveGround > 80d || Math.Abs(vs) > 120d || gs >= 80d || ias >= 70d);

            // Some MSFS/addon combinations keep SIM ON GROUND=true or AGL=0 while the
            // aircraft is clearly flying. Do not let that stale WOW/AGL sample freeze
            // the phase in PRE or block the later gate closeout.
            if (highEnergyAirborne)
            {
                return false;
            }

            if (data.OnGround)
            {
                return true;
            }

            // Before first airborne, allow conservative ground fallback for unsupported WOW.
            if (!_hasBeenAirborne)
            {
                return agl <= 5d && gs < 35d && ias < 45d;
            }

            // After airborne, do not let one bad AGL=0/disconnect sample pull CLB/CRZ/DES into ground.
            // Require live connection, low energy and no vertical movement unless SIM ON GROUND is true.
            if (!data.IsConnected)
            {
                return false;
            }

            return agl <= 5d && gs < 45d && ias < 55d && Math.Abs(vs) < 800d;
        }

        private void UpdateAltitudeTrend(double msl, bool onGround)
        {
            if (onGround)
            {
                _lastAirborneMsl = 0d;
                _mslIncreasingSamples = 0;
                _mslDecreasingSamples = 0;
                return;
            }

            if (_lastAirborneMsl <= 0d)
            {
                _lastAirborneMsl = msl;
                return;
            }

            var delta = msl - _lastAirborneMsl;
            if (delta > 80d)
            {
                _mslIncreasingSamples++;
                _mslDecreasingSamples = 0;
            }
            else if (delta < -80d)
            {
                _mslDecreasingSamples++;
                _mslIncreasingSamples = 0;
            }
            else
            {
                if (_mslIncreasingSamples > 0) _mslIncreasingSamples--;
                if (_mslDecreasingSamples > 0) _mslDecreasingSamples--;
            }

            _lastAirborneMsl = msl;
        }

        private sealed class PhaseAuditEvidence
        {
            public string Status { get; set; } = "PENDING";
            public string Summary { get; set; } = string.Empty;
            public string Flags { get; set; } = string.Empty;
        }

        private static PhaseAuditEvidence BuildPhaseAudit(SimData data, FlightPhase phase, bool onGround, bool isAirborneSample, bool changed, string confidence)
        {
            var flags = new List<string>();
            var agl = ResolveAgl(data);
            var msl = ResolveMsl(data);
            var gs = SafeNumber(data.GroundSpeed);
            var vs = SafeNumber(data.VerticalSpeed);
            var code = ToOperationalPhaseCode(phase);

            if (!data.IsAltitudeReliable)
            {
                flags.Add("AltitudeUnreliable");
            }

            if (onGround && agl > 10d)
            {
                flags.Add("GroundAglNotZero");
            }

            if (!onGround && agl <= 5d && gs > 45d)
            {
                flags.Add("AirborneLowAglCheck");
            }

            if (onGround && (phase == FlightPhase.Climb || phase == FlightPhase.Cruise || phase == FlightPhase.Descent || phase == FlightPhase.Approach))
            {
                flags.Add("AirbornePhaseButOnGround");
            }

            if (!onGround && (phase == FlightPhase.PreFlight || phase == FlightPhase.PushbackTaxi || phase == FlightPhase.Taxi || phase == FlightPhase.Arrived))
            {
                flags.Add("GroundPhaseButAirborne");
            }

            if (phase == FlightPhase.Arrived && !data.GateReadyCandidate)
            {
                flags.Add("GateNotReadyYet");
            }

            if (string.Equals(confidence, "pending", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("PendingTransition");
            }

            if (changed)
            {
                flags.Add("PhaseChanged");
            }

            if (msl <= -1500d || msl >= 70000d)
            {
                flags.Add("MslOutOfRange");
            }

            if (Math.Abs(vs) > 7000d)
            {
                flags.Add("VerticalSpeedExtreme");
            }

            var status = flags.Any(f => f == "AirbornePhaseButOnGround" || f == "GroundPhaseButAirborne" || f == "MslOutOfRange")
                ? "ERROR"
                : flags.Count == 0 ? "OK" : "WARN";

            var summary = status == "OK"
                ? "Auditoria C4 OK: fase y altitud coherentes"
                : status == "WARN"
                    ? "Auditoria C4 con advertencias: revisar flags"
                    : "Auditoria C4 critica: fase/altitud contradictoria";

            return new PhaseAuditEvidence
            {
                Status = status,
                Summary = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0} ({1}) · AGL {2:F0} ft · MSL {3:F0} ft · GS {4:F0} kt",
                    summary,
                    code,
                    agl,
                    msl,
                    gs),
                Flags = string.Join(",", flags.Distinct(StringComparer.OrdinalIgnoreCase))
            };
        }

        private sealed class PhaseChecklistEvidence
        {
            public string Status { get; set; } = "PENDING";
            public string Summary { get; set; } = string.Empty;
            public string Required { get; set; } = string.Empty;
            public string Satisfied { get; set; } = string.Empty;
            public string Missing { get; set; } = string.Empty;
            public string Warnings { get; set; } = string.Empty;
        }

        private static PhaseChecklistEvidence BuildPhaseChecklist(SimData data, FlightPhase phase, bool onGround)
        {
            var required = new List<string>();
            var satisfied = new List<string>();
            var missing = new List<string>();
            var warnings = new List<string>();

            var gs = SafeNumber(data.GroundSpeed);
            var ias = SafeNumber(data.IndicatedAirspeed);
            var agl = ResolveAgl(data);
            var vs = SafeNumber(data.VerticalSpeed);
            var enginesRunning = data.EngineOneRunning || data.EngineTwoRunning || data.EngineThreeRunning || data.EngineFourRunning
                                 || data.Engine1N1 > 15d || data.Engine2N1 > 15d || data.Engine3N1 > 15d || data.Engine4N1 > 15d;

            switch (phase)
            {
                case FlightPhase.PreFlight:
                    AddChecklistCheck(required, satisfied, missing, "OnGround", onGround);
                    AddChecklistCheck(required, satisfied, missing, "GS<=3", gs <= 3d);
                    AddChecklistCheck(required, satisfied, missing, "ParkingBrakeON", data.ParkingBrake);
                    AddChecklistWarning(warnings, "EnginesRunning", enginesRunning);
                    break;
                case FlightPhase.PushbackTaxi:
                    AddChecklistCheck(required, satisfied, missing, "OnGround", onGround);
                    AddChecklistCheck(required, satisfied, missing, "GS>3", gs > 3d);
                    AddChecklistCheck(required, satisfied, missing, "ParkingBrakeOFF", !data.ParkingBrake);
                    break;
                case FlightPhase.Takeoff:
                    if (onGround)
                    {
                        AddChecklistCheck(required, satisfied, missing, "TakeoffRollGS>=35", gs >= 35d);
                        AddChecklistCheck(required, satisfied, missing, "IAS>=30", ias >= 30d);
                    }
                    else
                    {
                        AddChecklistCheck(required, satisfied, missing, "Airborne", !onGround);
                        AddChecklistCheck(required, satisfied, missing, "AGL>20", agl > 20d);
                        AddChecklistCheck(required, satisfied, missing, "GS/IAS>40", gs > 40d || ias > 40d);
                    }
                    break;
                case FlightPhase.Climb:
                    AddChecklistCheck(required, satisfied, missing, "Airborne", !onGround);
                    AddChecklistCheck(required, satisfied, missing, "PositiveVS/LowAGL", vs > 250d || agl < 2500d);
                    break;
                case FlightPhase.Cruise:
                    AddChecklistCheck(required, satisfied, missing, "Airborne", !onGround);
                    AddChecklistCheck(required, satisfied, missing, "StableVS", Math.Abs(vs) <= 500d);
                    AddChecklistCheck(required, satisfied, missing, "AGL>=2500", agl >= 2500d);
                    break;
                case FlightPhase.Descent:
                    AddChecklistCheck(required, satisfied, missing, "Airborne", !onGround);
                    AddChecklistCheck(required, satisfied, missing, "VS<-500", vs < -500d);
                    break;
                case FlightPhase.Approach:
                    AddChecklistCheck(required, satisfied, missing, "Airborne", !onGround);
                    AddChecklistCheck(required, satisfied, missing, "AGL<3000", agl < 3000d);
                    AddChecklistCheck(required, satisfied, missing, "DescentOrEstablished", vs < -150d || Math.Abs(vs) <= 700d);
                    break;
                case FlightPhase.Landing:
                    AddChecklistCheck(required, satisfied, missing, "OnGroundAfterAirborne", onGround && data.HasBeenAirborne);
                    AddChecklistCheck(required, satisfied, missing, "TouchdownEvidence", data.TouchdownDetected || data.HasBeenAirborne);
                    break;
                case FlightPhase.Taxi:
                    AddChecklistCheck(required, satisfied, missing, "OnGroundAfterLanding", onGround && data.HasBeenAirborne);
                    AddChecklistCheck(required, satisfied, missing, "TaxiSpeed", gs > 3d && gs <= 40d);
                    break;
                case FlightPhase.Arrived:
                    AddChecklistCheck(required, satisfied, missing, "OnGround", onGround);
                    AddChecklistCheck(required, satisfied, missing, "GS<=3", gs <= 3d);
                    AddChecklistCheck(required, satisfied, missing, "ParkingBrakeON", data.ParkingBrake);
                    AddChecklistCheck(required, satisfied, missing, "AfterAirborne", data.HasBeenAirborne || data.TouchdownDetected);
                    AddChecklistWarning(warnings, "EnginesRunning", enginesRunning);
                    break;
                default:
                    AddChecklistCheck(required, satisfied, missing, "TelemetryPresent", data.CapturedAtUtc != default(DateTime));
                    break;
            }

            var status = missing.Count == 0 ? (warnings.Count == 0 ? "OK" : "WARN") : "INCOMPLETE";
            var summary = status == "OK"
                ? "Fase coherente"
                : status == "WARN"
                    ? "Fase coherente con advertencias"
                    : "Fase con evidencia incompleta";

            return new PhaseChecklistEvidence
            {
                Status = status,
                Summary = summary,
                Required = string.Join(",", required),
                Satisfied = string.Join(",", satisfied),
                Missing = string.Join(",", missing),
                Warnings = string.Join(",", warnings)
            };
        }

        private static void AddChecklistCheck(List<string> required, List<string> satisfied, List<string> missing, string name, bool condition)
        {
            required.Add(name);
            if (condition)
            {
                satisfied.Add(name);
            }
            else
            {
                missing.Add(name);
            }
        }

        private static void AddChecklistWarning(List<string> warnings, string name, bool condition)
        {
            if (condition)
            {
                warnings.Add(name);
            }
        }

        private static string ToOperationalPhaseCode(FlightPhase phase)
        {
            switch (phase)
            {
                case FlightPhase.PreFlight: return "PRE";
                case FlightPhase.Boarding: return "BRD";
                case FlightPhase.PushbackTaxi: return "TAX_OUT";
                case FlightPhase.Takeoff: return "TO";
                case FlightPhase.Climb: return "CLB";
                case FlightPhase.Cruise: return "CRZ";
                case FlightPhase.Descent: return "DES";
                case FlightPhase.Approach: return "APP";
                case FlightPhase.Landing: return "LDG";
                case FlightPhase.Taxi: return "TAX_IN";
                case FlightPhase.Arrived: return "GATE";
                case FlightPhase.Deboarding: return "DEB";
                default: return "PRE";
            }
        }

        private static string ToOperationalPhaseName(FlightPhase phase)
        {
            switch (phase)
            {
                case FlightPhase.PreFlight: return "Preflight";
                case FlightPhase.Boarding: return "Boarding";
                case FlightPhase.PushbackTaxi: return "Taxi out";
                case FlightPhase.Takeoff: return "Takeoff roll / initial airborne";
                case FlightPhase.Climb: return "Climb";
                case FlightPhase.Cruise: return "Cruise";
                case FlightPhase.Descent: return "Descent";
                case FlightPhase.Approach: return "Approach";
                case FlightPhase.Landing: return "Landing roll";
                case FlightPhase.Taxi: return "Taxi in";
                case FlightPhase.Arrived: return "Gate ready";
                case FlightPhase.Deboarding: return "Deboarding";
                default: return "Preflight";
            }
        }

        private static bool IsAirborneOperationalCode(string code)
        {
            switch ((code ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "TO":
                case "CLB":
                case "CRZ":
                case "DES":
                case "APP":
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsGroundOperationalCode(string code)
        {
            switch ((code ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "PRE":
                case "BRD":
                case "TAX_OUT":
                case "TAX_IN":
                case "GATE":
                case "DEB":
                    return true;
                default:
                    return false;
            }
        }

        private static double ResolveAgl(SimData data)
        {
            if (data == null) return 0d;
            var agl = data.AltitudeAglFeet >= 0d ? data.AltitudeAglFeet : data.AltitudeAGL;
            if (double.IsNaN(agl) || double.IsInfinity(agl)) return 0d;
            return Math.Max(0d, agl);
        }

        private static double ResolveMsl(SimData data)
        {
            if (data == null) return 0d;
            var msl = data.AltitudeMslFeet > 0d ? data.AltitudeMslFeet : data.AltitudeFeet;
            if (double.IsNaN(msl) || double.IsInfinity(msl)) return 0d;
            return msl;
        }

        private static double SafeNumber(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
        }

        private static double SecondsSince(DateTime startUtc, DateTime nowUtc)
        {
            if (startUtc == default(DateTime) || nowUtc < startUtc) return 0d;
            return (nowUtc - startUtc).TotalSeconds;
        }

        private void SetFlightLock(bool value)
        {
            if (IsFlightLocked == value)
            {
                return;
            }

            IsFlightLocked = value;
            FlightLockChanged?.Invoke(value);
        }

        private void SetPhase(FlightPhase phase)
        {
            CurrentPhase = phase;
            PhaseChanged?.Invoke(phase);
        }

        public FlightReport GenerateReport(string pilotCallSign)
        {
            if (_currentFlight == null)
                throw new InvalidOperationException("No hay vuelo activo.");

            var arrivalTime = DateTime.UtcNow;
            var fuelUsed = _fuelAtStart > 0
                ? Math.Max(0, _fuelAtStart - _lastSimData.FuelTotalLbs)
                : 0;

            // ── SUR Air dual scoring ────────────────────────────────────────
            var aircraftProfile = AircraftNormalizationService.ResolveProfile(_currentFlight.AircraftName);
            var evaluator = new FlightEvaluationService(
                _telemetryLog,
                _lastLandingVS,
                _lastLandingG,
                _currentFlight.AircraftIcao,
                _currentFlight.AircraftName,
                IsAircraftPressurized(),
                cabinSystemsReliable: aircraftProfile.CabinSystemsReliable);

            var eval = evaluator.Evaluate();

            var report = new FlightReport
            {
                FlightNumber = _currentFlight.FlightNumber,
                PilotCallSign = pilotCallSign,
                DepartureIcao = _currentFlight.DepartureIcao,
                ArrivalIcao = _currentFlight.ArrivalIcao,
                AircraftIcao = _currentFlight.AircraftIcao,
                DepartureTime = _blockOutTime,
                ArrivalTime = arrivalTime,
                BlockOutTimeUtc = _blockOutTime,
                TakeoffTimeUtc = _takeoffTime,
                TouchdownTimeUtc = _touchdownTime,
                Distance = Math.Round(_totalDistanceNm, 1),
                FuelUsed = Math.Round(fuelUsed, 0),
                LandingVS = IsOperationalVerticalSpeed(_lastLandingVS) ? _lastLandingVS : 0d,
                LandingG = Math.Round(IsOperationalGForce(_lastLandingG) ? _lastLandingG : 0d, 2),
                PatagoniaScore = eval.PatagoniaScore,
                PatagoniaGrade = eval.PatagoniaGrade,

                // Canonical closeout contract
                ProcedureScore   = eval.ProcedureScore,
                PerformanceScore = eval.PerformanceScore,
                ProcedureGrade   = eval.ProcedureGrade,
                PerformanceGrade = eval.PerformanceGrade,
                Violations       = eval.Violations,
                Bonuses          = eval.Bonuses,
                Evaluation       = eval.PatagoniaEvaluation,

                Simulator = _currentFlight.Simulator,
                Remarks = _currentFlight.Remarks ?? string.Empty,
                Status = FlightStatus.Pending,
                MaxAltitudeFeet = Math.Round(_maxAltitude, 0),
                MaxSpeedKts = Math.Round(_maxSpeed, 0),
                ApproachQnhHpa = ComputeApproachQnh(),

                // Legacy penalty breakdown
                LandingPenalty  = eval.LandingPenalty,
                TaxiPenalty     = eval.TaxiPenalty,
                AirbornePenalty = eval.AirbornePenalty,
                ApproachPenalty = eval.ApproachPenalty,
                CabinPenalty    = eval.CabinPenalty
            };

            ApplyStableApproachPolicy(report);
            report.ProceduralSummary = eval.Summary;
            report.ApplyLegacyScoreProjection();
            return report;
        }

        private void ApplyStableApproachPolicy(FlightReport report)
        {
            if (report == null || _telemetryLog.Count == 0) return;

            var candidate = _telemetryLog
                .Where(s => !s.OnGround
                            && ResolveAgl(s) > 0d
                            && ResolveAgl(s) <= 2000d
                            && (s.OperationalPhaseCode == "APP" || s.SurfaceProcedurePhaseCode == "APPROACH_FINAL")
                            && s.FacilityNearestRunwayDistanceMeters > 0d
                            && (s.FacilityNearestRunwayDistanceMeters / 1852d) <= 7d)
                .OrderByDescending(s => ResolveAgl(s))
                .FirstOrDefault();

            if (candidate == null) return;

            var penalties = new List<string>();
            var headingError = candidate.FacilityRunwayHeadingErrorDeg > 0d
                ? candidate.FacilityRunwayHeadingErrorDeg
                : (candidate.RunwayHeadingDeltaDeg > 0d ? candidate.RunwayHeadingDeltaDeg : 999d);

            if (!candidate.GearDown) penalties.Add("tren_abajo");
            if (!candidate.FlapsDeployed || candidate.FlapsPercent < 0.05d) penalties.Add("flaps");
            if (!candidate.LandingLightsOn) penalties.Add("landing_lights");
            if (Math.Abs(candidate.Bank) > 5d) penalties.Add("bank_gt_5");
            if (headingError > 10d) penalties.Add("hdg_runway_gt_10");

            if (penalties.Count == 0) return;

            var penaltyPoints = penalties.Count * 5;
            report.ApproachPenalty += penaltyPoints;
            report.PatagoniaScore = Math.Max(0, report.PatagoniaScore - penaltyPoints);
            report.ProcedureScore = Math.Max(0, report.ProcedureScore - penaltyPoints);
            report.Violations.Add(new ScoreEvent
            {
                Code = "APP-STABLE-2000",
                Phase = "APP",
                Description = "Aproximacion no estabilizada a 2000 ft: " + string.Join(", ", penalties),
                Points = -penaltyPoints
            });
        }

        private double ComputeApproachQnh()
        {
            var samples = _telemetryLog
                .Where(s => !s.OnGround && s.AltitudeAGL > 0 && s.AltitudeAGL < 3000 && s.QNH > 900)
                .ToList();
            if (samples.Count == 0) return 0;
            return Math.Round(samples.Average(s => s.QNH), 1);
        }

        private static double CalculateDistanceNm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 3440.065;
            var dLat = DegToRad(lat2 - lat1);
            var dLon = DegToRad(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(DegToRad(lat1)) * Math.Cos(DegToRad(lat2))
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double DegToRad(double deg) => deg * Math.PI / 180.0;

        /// <summary>
        /// Determina si la aeronave activa tiene presurización de cabina.
        /// Usa AircraftNormalizationService (catálogo estable) con fallback a ICAO/nombre.
        /// </summary>
        private bool IsAircraftPressurized()
        {
            // Primero intentar con el título real del sim (más preciso)
            var simTitle = _telemetryLog.Count > 0 ? _telemetryLog[0].AircraftTitle : string.Empty;
            if (!string.IsNullOrWhiteSpace(simTitle))
            {
                var profile = AircraftNormalizationService.ResolveProfile(simTitle);
                if (profile.Code != "MSFS_NATIVE")
                    return profile.IsPressurized;
            }

            // Fallback: ICAO del despacho
            if (_currentFlight == null) return true;
            var icao = (_currentFlight.AircraftIcao ?? string.Empty).Trim().ToUpperInvariant();
            var name = (_currentFlight.AircraftName  ?? string.Empty).Trim().ToUpperInvariant();
            var combined = icao + " " + name;

            // Aeronaves NO presurizadas conocidas → retornar false
            var unpressurized = new[] { "C208","CARAVAN","GRAND CARAVAN","BE58","BARON 58",
                "C172","SKYHAWK","C152","C182","C206","SR20","SR22","DA40","DA20",
                "PA28","PA-28","WARRIOR","ARCHER","MOONEY","ROBIN" };
            foreach (var token in unpressurized)
                if (combined.Contains(token)) return false;

            // Presurizado por defecto para jets y turbohélices de línea
            return true;
        }


        public void MarkCrash()
        {
            MarkCrash("simconnect_crashed_event");
        }

        public void MarkCrash(string reason)
        {
            if (_crashMarked)
            {
                return;
            }

            _crashMarked = true;
            _crashMarkedAtUtc = DateTime.UtcNow;
            _crashReason = string.IsNullOrWhiteSpace(reason) ? "crash_detected" : reason.Trim();

            if (_damageCollector != null)
            {
                _damageCollector.MarkCrash();
            }
        }

        private void DetectCrashLikeImpact(SimData data)
        {
            if (_currentFlight == null || _crashMarked || data == null)
            {
                return;
            }

            var phase = CurrentPhase;
            var activeFlightPhase = phase == FlightPhase.Takeoff ||
                                    phase == FlightPhase.Climb ||
                                    phase == FlightPhase.Cruise ||
                                    phase == FlightPhase.Descent ||
                                    phase == FlightPhase.Approach ||
                                    phase == FlightPhase.Landing;

            if (!activeFlightPhase)
            {
                return;
            }

            var landingG = Math.Abs(IsOperationalGForce(data.LandingG) ? data.LandingG : 0d);
            var landingVs = Math.Abs(IsOperationalVerticalSpeed(data.LandingVS) ? data.LandingVS : 0d);
            var verticalSpeed = Math.Abs(IsOperationalVerticalSpeed(data.VerticalSpeed) ? data.VerticalSpeed : 0d);
            var bank = Math.Abs(data.Bank);
            var pitch = Math.Abs(data.Pitch);
            var highEnergyGroundContact = data.OnGround && (data.GroundSpeed > 115 || data.IndicatedAirspeed > 120);

            var severeImpact = data.OnGround &&
                               (landingG >= 4.0 ||
                                landingVs >= 1200 ||
                                verticalSpeed >= 2500 ||
                                bank >= 70 ||
                                pitch >= 45 ||
                                highEnergyGroundContact);

            if (!severeImpact)
            {
                return;
            }

            var reason = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "crash_like_impact phase={0} gs={1:F0} ias={2:F0} vs={3:F0} ldg_vs={4:F0} g={5:F1} pitch={6:F0} bank={7:F0}",
                phase,
                data.GroundSpeed,
                data.IndicatedAirspeed,
                data.VerticalSpeed,
                data.LandingVS,
                data.LandingG,
                data.Pitch,
                data.Bank);

            // PIREP Perfect A1:
            // No cerrar ni enviar PIREP automáticamente por crash_like_impact. Se marca
            // como evidencia/daño, pero el cierre oficial queda siempre bajo control del
            // piloto en FINALIZAR EN GATE o CANCELAR VUELO. Esto evita falsos cierres por
            // lecturas corruptas de GForce/LandingG.
            MarkCrash(reason);
            Debug.WriteLine("[FlightService] Crash-like impact recorded as evidence only: " + reason);
        }

        private static void SanitizeSampleForPirep(SimData data)
        {
            if (data == null)
            {
                return;
            }

            if (!IsOperationalGForce(data.GForce))
            {
                data.GForce = 0d;
            }

            if (!IsOperationalGForce(data.LandingG))
            {
                data.LandingG = 0d;
            }

            if (!IsOperationalVerticalSpeed(data.LandingVS))
            {
                data.LandingVS = 0d;
            }

            if (!IsOperationalVerticalSpeed(data.VerticalSpeed))
            {
                data.VerticalSpeed = 0d;
            }
        }

        private static bool IsOperationalGForce(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= -3.0d && value <= 8.0d;
        }

        private static bool IsOperationalVerticalSpeed(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= 8000d;
        }

        private static bool IsValidPosition(double latitude, double longitude)
        {
            return !double.IsNaN(latitude) && !double.IsNaN(longitude) &&
                   !double.IsInfinity(latitude) && !double.IsInfinity(longitude) &&
                   Math.Abs(latitude) <= 90d && Math.Abs(longitude) <= 180d &&
                   !(Math.Abs(latitude) < 0.000001d && Math.Abs(longitude) < 0.000001d);
        }

        public IReadOnlyList<AircraftDamageEvent> GetDamageEventsSnapshot()
        {
            if (_damageCollector == null)
            {
                return _finalDamageEvents.ToArray();
            }

            _finalDamageEvents.Clear();
            _finalDamageEvents.AddRange(_damageCollector.BuildDamageEvents(this));
            return _finalDamageEvents.ToArray();
        }

        public bool HasCrashMarked => _crashMarked;
        public string CrashReason => _crashReason;
        public DateTime CrashMarkedAtUtc => _crashMarkedAtUtc;

        public void ArmCloseoutReset()
        {
            _closeoutResetAuthorized = true;
        }

        public void Reset(bool force = false)
        {
            if (!force && _currentFlight != null && !_closeoutResetAuthorized)
            {
                Debug.WriteLine("[FlightService] Reset ignored: active flight is not authorized for closeout reset.");
                return;
            }

            _closeoutResetAuthorized = false;
            _currentFlight = null;
            _lastSimData = new SimData();
            _telemetryLog.Clear();
            _maxAltitude = 0;
            _maxSpeed = 0;
            _totalDistanceNm = 0;
            _fuelAtStart = 0;
            _blockOutTime = default(DateTime);
            _takeoffTime = default(DateTime);
            _touchdownTime = default(DateTime);
            _lastLandingVS = 0;
            _lastLandingG = 0;
            _lastLatitude = 0;
            _lastLongitude = 0;
            _hasPosition = false;
            _crashMarked = false;
            _crashMarkedAtUtc = default(DateTime);
            _crashReason = string.Empty;
            _hasBeenAirborne = false;
            _takeoffDetected = false;
            _touchdownDetected = false;
            _airborneConfirmSamples = 0;
            _groundConfirmSamples = 0;
            _descentConfirmSamples = 0;
            _approachConfirmSamples = 0;
            _cruiseStableSamples = 0;
            _lastPhaseChangeUtc = default(DateTime);
            _candidatePhase = FlightPhase.Disconnected;
            _candidatePhaseSamples = 0;
            _phaseStabilitySamples = 0;
            _phaseTransitionIndex = 0;
            _phaseEnteredAtUtc = default(DateTime);
            _lastAirborneMsl = 0d;
            _mslIncreasingSamples = 0;
            _mslDecreasingSamples = 0;
            _taxiOutConfirmSamples = 0;
            _takeoffRollConfirmSamples = 0;
            _landingRollConfirmSamples = 0;
            _gateReadyConfirmSamples = 0;
            _damageCollector = null;
            _finalDamageEvents.Clear();
            SetFlightLock(false);
            SetPhase(FlightPhase.Disconnected);
        }
    }
}
