using System;
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

            var onGround = ResolveOperationalOnGround(data, agl, gs, ias, vs);
            var previousOnGround = previous == null ? onGround : ResolveOperationalOnGround(previous, ResolveAgl(previous), SafeNumber(previous.GroundSpeed), SafeNumber(previous.IndicatedAirspeed), SafeNumber(previous.VerticalSpeed));
            var previousAirborne = previous != null && (previous.IsAirborneSample || (!previousOnGround && (ResolveAgl(previous) > 20d || SafeNumber(previous.GroundSpeed) > 55d || SafeNumber(previous.IndicatedAirspeed) > 55d)));

            var highEnergyAirborneCandidate = !onGround
                && (gs >= 60d || ias >= 55d)
                && !data.ParkingBrake
                && (Math.Abs(vs) > 80d || agl > 20d || msl > 300d);

            var takeoffRollCandidate = onGround && !_takeoffDetected && gs >= 35d && (ias >= 30d || gs >= 45d);
            var taxiOutCandidate = onGround && !_hasBeenAirborne && gs > 3d && gs <= 35d && !data.ParkingBrake;
            var airborneCandidate = !onGround
                && (agl > 20d || (_lastAirborneMsl > 0d && msl > _lastAirborneMsl + 50d) || highEnergyAirborneCandidate)
                && (gs > 40d || ias > 40d);
            var touchdownCandidate = _hasBeenAirborne && !_touchdownDetected && (previousAirborne || !previousOnGround) && onGround;

            UpdateAltitudeTrend(msl, onGround);

            if (airborneCandidate)
            {
                _airborneConfirmSamples++;
            }
            else if (onGround && !_hasBeenAirborne)
            {
                _airborneConfirmSamples = 0;
            }

            if (takeoffRollCandidate)
            {
                _takeoffRollConfirmSamples++;
            }
            else if (!_takeoffDetected)
            {
                _takeoffRollConfirmSamples = 0;
            }

            if (taxiOutCandidate)
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

            if (touchdownCandidate || (_hasBeenAirborne && !_touchdownDetected && onGround && _groundConfirmSamples >= 2))
            {
                MarkTouchdown(data, previous, sampleTime);
                _landingRollConfirmSamples = 1;
                ApplyPhaseDecision(data, FlightPhase.Landing, "touchdown_transition_air_to_ground", onGround, false, sampleTime, 1, true);
                return;
            }

            if (!_takeoffDetected && _airborneConfirmSamples >= 2)
            {
                _takeoffDetected = true;
                _hasBeenAirborne = true;
                if (_takeoffTime == default(DateTime))
                {
                    _takeoffTime = sampleTime;
                }

                ApplyPhaseDecision(data, FlightPhase.Takeoff, "airborne_confirmed_two_samples", onGround, true, sampleTime, 1, true);
                return;
            }

            if (!_hasBeenAirborne)
            {
                if (_takeoffRollConfirmSamples >= 2)
                {
                    ApplyPhaseDecision(data, FlightPhase.Takeoff, "takeoff_roll_confirmed_gs_ias", onGround, false, sampleTime, 2, false);
                    return;
                }

                if (_taxiOutConfirmSamples >= 2)
                {
                    ApplyPhaseDecision(data, FlightPhase.PushbackTaxi, "taxi_out_confirmed_ground_moving_parking_off", onGround, false, sampleTime, 2, false);
                    return;
                }

                ApplyPhaseDecision(data, FlightPhase.PreFlight, onGround ? "preflight_ground_stationary" : "preflight_waiting_airborne_confirmation", onGround, false, sampleTime, 1, false);
                return;
            }

            if (onGround)
            {
                if (gs <= 3d && data.ParkingBrake)
                {
                    _gateReadyConfirmSamples++;
                }
                else
                {
                    _gateReadyConfirmSamples = 0;
                }

                if (gs > 40d)
                {
                    _landingRollConfirmSamples++;
                    ApplyPhaseDecision(data, FlightPhase.Landing, "landing_roll_high_speed_after_touchdown", onGround, false, sampleTime, 1, _landingRollConfirmSamples <= 2);
                    return;
                }

                if (_gateReadyConfirmSamples >= 2)
                {
                    ApplyPhaseDecision(data, FlightPhase.Arrived, "gate_ready_confirmed_stopped_parking_brake", onGround, false, sampleTime, 2, false);
                    return;
                }

                if (gs > 3d)
                {
                    ApplyPhaseDecision(data, FlightPhase.Taxi, "taxi_in_after_touchdown", onGround, false, sampleTime, 2, false);
                    return;
                }

                ApplyPhaseDecision(data, FlightPhase.Landing, "landing_roll_or_stopped_before_gate_confirmation", onGround, false, sampleTime, 1, false);
                return;
            }

            // Airborne matrix. AGL controls low-altitude boundaries; MSL/pressure altitude
            // controls max altitude and cruise profile. Counters provide hysteresis.
            var nearApproachLayer = agl > 0d && agl < 3000d;
            var approachCandidate = nearApproachLayer && (_mslDecreasingSamples >= 2 || vs < -150d || CurrentPhase == FlightPhase.Approach);
            var descentCandidate = agl >= 2500d && (_mslDecreasingSamples >= 2 || vs < -500d);
            var cruiseCandidate = agl >= 2500d && (Math.Abs(vs) <= 350d || (plannedCruiseAltitude > 0d && msl >= plannedCruiseAltitude - 2000d && Math.Abs(vs) <= 500d));
            var climbCandidate = _mslIncreasingSamples >= 2 || vs > 450d || agl < 2500d;

            if (approachCandidate)
            {
                _approachConfirmSamples++;
            }
            else
            {
                _approachConfirmSamples = 0;
            }

            if (descentCandidate)
            {
                _descentConfirmSamples++;
            }
            else if (vs > -200d && _mslDecreasingSamples == 0)
            {
                _descentConfirmSamples = 0;
            }

            if (cruiseCandidate)
            {
                _cruiseStableSamples++;
            }
            else
            {
                _cruiseStableSamples = 0;
            }

            if (_approachConfirmSamples >= 2)
            {
                ApplyPhaseDecision(data, FlightPhase.Approach, "approach_confirmed_agl_descent", onGround, true, sampleTime, 2, false);
                return;
            }

            if (_descentConfirmSamples >= 2 || (CurrentPhase == FlightPhase.Cruise && descentCandidate))
            {
                ApplyPhaseDecision(data, FlightPhase.Descent, "descent_confirmed_msl_trend_or_vs", onGround, true, sampleTime, 2, false);
                return;
            }

            if (CurrentPhase == FlightPhase.Takeoff && (agl < 800d || SecondsSince(_takeoffTime, sampleTime) < 90d))
            {
                ApplyPhaseDecision(data, FlightPhase.Takeoff, "initial_climb_takeoff_window", onGround, true, sampleTime, 1, false);
                return;
            }

            if (_cruiseStableSamples >= 5)
            {
                ApplyPhaseDecision(data, FlightPhase.Cruise, "cruise_stable_five_samples", onGround, true, sampleTime, 3, false);
                return;
            }

            if (climbCandidate)
            {
                ApplyPhaseDecision(data, FlightPhase.Climb, "climb_confirmed_msl_trend_vs_or_low_agl", onGround, true, sampleTime, 2, false);
                return;
            }

            var preserved = CurrentPhase == FlightPhase.Disconnected || CurrentPhase == FlightPhase.PreFlight || CurrentPhase == FlightPhase.PushbackTaxi
                ? FlightPhase.Climb
                : CurrentPhase;
            ApplyPhaseDecision(data, preserved, "preserve_airborne_phase_no_matrix_change", onGround, true, sampleTime, 1, false);
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
            data.PhaseMatrixVersion = "C3";

            var surface = BuildSurfaceContext(data, phase, onGround);
            data.SurfaceContextCode = surface.Code;
            data.SurfaceContextName = surface.Name;
            data.SurfaceContextReason = surface.Reason;
            data.RunwayCandidate = surface.RunwayCandidate;
            data.TaxiwayCandidate = surface.TaxiwayCandidate;
            data.GateAreaCandidate = surface.GateAreaCandidate;
            data.SurfaceContextReliable = surface.Reliable;
            data.SurfaceContextVersion = "C9";

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
            var touchdown = data.TouchdownDetected || data.HasBeenAirborne;

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

            report.ProceduralSummary = eval.Summary;
            report.ApplyLegacyScoreProjection();
            return report;
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
