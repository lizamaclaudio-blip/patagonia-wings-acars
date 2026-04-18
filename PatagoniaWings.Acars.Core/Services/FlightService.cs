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
            _finalDamageEvents.Clear();

            var profile = DamageRuleMapper.CreateProfile(
                flight.AircraftTypeCode,
                string.Concat(flight.AircraftName ?? string.Empty, " ", flight.AircraftDisplayName ?? string.Empty));

            _damageCollector = string.IsNullOrWhiteSpace(flight.AircraftId)
                ? null
                : new DamageEventCollector(flight.AircraftId, flight.ReservationId, profile);

            SetFlightLock(true);
            SetPhase(FlightPhase.PreFlight);
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

                if (_hasPosition)
                {
                    _totalDistanceNm += CalculateDistanceNm(
                        _lastLatitude, _lastLongitude, data.Latitude, data.Longitude);
                }

                if (data.AltitudeFeet > _maxAltitude) _maxAltitude = data.AltitudeFeet;
                if (data.IndicatedAirspeed > _maxSpeed) _maxSpeed = data.IndicatedAirspeed;

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

        private void UpdatePhase(SimData data)
        {
            if (_currentFlight == null) return;

            var newPhase = CurrentPhase;
            var sampleTime = data.CapturedAtUtc == default(DateTime) ? DateTime.UtcNow : data.CapturedAtUtc;
            var maxEngineN1 = Math.Max(data.Engine1N1, data.Engine2N1);
            var plannedCruiseAltitude = _currentFlight.PlannedAltitude > 0 ? _currentFlight.PlannedAltitude : 30000;

            switch (CurrentPhase)
            {
                case FlightPhase.PreFlight:
                    if (!data.OnGround && data.IndicatedAirspeed > 80)
                    {
                        _takeoffTime = sampleTime;
                        newPhase = FlightPhase.Takeoff;
                    }
                    else if (!data.ParkingBrake && data.OnGround && data.GroundSpeed > 2)
                        newPhase = FlightPhase.PushbackTaxi;
                    break;

                case FlightPhase.Boarding:
                    if (!data.ParkingBrake && data.OnGround && data.GroundSpeed > 2)
                        newPhase = FlightPhase.PushbackTaxi;
                    break;

                case FlightPhase.PushbackTaxi:
                    if (!data.OnGround && data.IndicatedAirspeed > 80)
                    {
                        _takeoffTime = sampleTime;
                        newPhase = FlightPhase.Takeoff;
                    }
                    break;

                case FlightPhase.Takeoff:
                    if (!data.OnGround && (data.AltitudeAGL > 800 || data.AltitudeFeet > 1500 || data.VerticalSpeed > 700))
                    {
                        newPhase = FlightPhase.Climb;
                    }
                    break;

                case FlightPhase.Climb:
                    if (data.VerticalSpeed < -500 && data.AltitudeAGL > 4000)
                    {
                        newPhase = FlightPhase.Descent;
                    }
                    else if (data.AltitudeFeet >= plannedCruiseAltitude - 2000 && Math.Abs(data.VerticalSpeed) < 500)
                        newPhase = FlightPhase.Cruise;
                    break;

                case FlightPhase.Cruise:
                    if (data.VerticalSpeed < -500)
                        newPhase = FlightPhase.Descent;
                    break;

                case FlightPhase.Descent:
                    if ((data.AltitudeAGL > 0 && data.AltitudeAGL < 4000) || data.AltitudeFeet < 3000)
                        newPhase = FlightPhase.Approach;
                    break;

                case FlightPhase.Approach:
                    if (data.OnGround)
                    {
                        _lastLandingVS = data.LandingVS;
                        _lastLandingG = data.LandingG;
                        _touchdownTime = sampleTime;
                        newPhase = FlightPhase.Landing;
                    }
                    break;

                case FlightPhase.Landing:
                    if (data.OnGround && data.GroundSpeed < 40)
                        newPhase = FlightPhase.Taxi;
                    break;

                case FlightPhase.Taxi:
                    if (data.OnGround && data.ParkingBrake && data.GroundSpeed < 1 && maxEngineN1 < 25)
                        newPhase = FlightPhase.Arrived;
                    break;
            }

            if (newPhase != CurrentPhase)
                SetPhase(newPhase);
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
            var evaluator = new FlightEvaluationService(
                _telemetryLog,
                _lastLandingVS,
                _lastLandingG,
                _currentFlight.AircraftIcao,
                _currentFlight.AircraftName,
                IsAircraftPressurized());

            var eval = evaluator.Evaluate();

            return new FlightReport
            {
                FlightNumber = _currentFlight.FlightNumber,
                PilotCallSign = pilotCallSign,
                DepartureIcao = _currentFlight.DepartureIcao,
                ArrivalIcao = _currentFlight.ArrivalIcao,
                AircraftIcao = _currentFlight.AircraftIcao,
                DepartureTime = _blockOutTime,
                ArrivalTime = arrivalTime,
                Distance = Math.Round(_totalDistanceNm, 1),
                FuelUsed = Math.Round(fuelUsed, 0),
                LandingVS = _lastLandingVS,
                LandingG = Math.Round(_lastLandingG, 2),

                // Legacy (Supabase)
                Score = eval.ProcedureScore,
                Grade = eval.ProcedureGrade,
                ProceduralSummary = eval.Summary,
                PointsEarned = eval.ProcedureScore,

                // SUR Air dual scores
                ProcedureScore   = eval.ProcedureScore,
                PerformanceScore = eval.PerformanceScore,
                ProcedureGrade   = eval.ProcedureGrade,
                PerformanceGrade = eval.PerformanceGrade,
                Violations       = eval.Violations,
                Bonuses          = eval.Bonuses,

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
            _crashMarked = true;
            if (_damageCollector != null)
            {
                _damageCollector.MarkCrash();
            }
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

        public void Reset()
        {
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
            _damageCollector = null;
            _finalDamageEvents.Clear();
            SetFlightLock(false);
            SetPhase(FlightPhase.Disconnected);
        }
    }
}
