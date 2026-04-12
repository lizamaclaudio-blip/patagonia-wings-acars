using System;
using System.Collections.Generic;
using System.Linq;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    public class FlightService
    {
        private sealed class ProceduralScoreEvaluation
        {
            public int Score { get; set; }
            public string Summary { get; set; } = string.Empty;
            public int LandingPenalty { get; set; }
            public int TaxiPenalty { get; set; }
            public int AirbornePenalty { get; set; }
            public int ApproachPenalty { get; set; }
            public int CabinPenalty { get; set; }
        }

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

        public FlightPhase CurrentPhase { get; private set; } = FlightPhase.Disconnected;
        public bool IsFlightActive => _currentFlight != null;
        public Flight? CurrentFlight => _currentFlight;
        public SimData LastSimData => _lastSimData;
        public DateTime BlockOutTimeUtc => _blockOutTime;
        public DateTime TakeoffTimeUtc => _takeoffTime;
        public DateTime TouchdownTimeUtc => _touchdownTime;
        public double FuelAtStartLbs => _fuelAtStart;
        public bool IsFlightLocked { get; private set; }

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
            SetFlightLock(true);
            SetPhase(FlightPhase.PreFlight);
        }

        public void UpdateSimData(SimData data)
        {
            if (data == null)
            {
                return;
            }

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
            var scoreEvaluation = EvaluateProceduralScore();

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
                Score = scoreEvaluation.Score,
                Grade = GetGrade(scoreEvaluation.Score),
                ProceduralSummary = scoreEvaluation.Summary,
                Simulator = _currentFlight.Simulator,
                Remarks = _currentFlight.Remarks ?? string.Empty,
                Status = FlightStatus.Pending,
                PointsEarned = scoreEvaluation.Score,
                MaxAltitudeFeet = Math.Round(_maxAltitude, 0),
                MaxSpeedKts = Math.Round(_maxSpeed, 0),
                ApproachQnhHpa = ComputeApproachQnh(),
                LandingPenalty = scoreEvaluation.LandingPenalty,
                TaxiPenalty = scoreEvaluation.TaxiPenalty,
                AirbornePenalty = scoreEvaluation.AirbornePenalty,
                ApproachPenalty = scoreEvaluation.ApproachPenalty,
                CabinPenalty = scoreEvaluation.CabinPenalty
            };
        }

        private ProceduralScoreEvaluation EvaluateProceduralScore()
        {
            var score = 100;
            var notes = new List<string>();

            var before = score;
            ApplyLandingPenalties(ref score, notes);
            var landingPenalty = score - before;

            before = score;
            ApplyTaxiPenalties(ref score, notes);
            var taxiPenalty = score - before;

            before = score;
            ApplyAirbornePenalties(ref score, notes);
            var airbornePenalty = score - before;

            before = score;
            ApplyApproachPenalties(ref score, notes);
            var approachPenalty = score - before;

            before = score;
            ApplyCabinPressurePenalties(ref score, notes);
            ApplyNoSmokingPenalties(ref score, notes);
            var cabinPenalty = score - before;

            var normalizedScore = Math.Max(0, Math.Min(100, score));
            var summary = notes.Count == 0
                ? "Procedimiento limpio de punta a punta."
                : string.Join(" · ", notes);

            return new ProceduralScoreEvaluation
            {
                Score = normalizedScore,
                Summary = summary,
                LandingPenalty = landingPenalty,
                TaxiPenalty = taxiPenalty,
                AirbornePenalty = airbornePenalty,
                ApproachPenalty = approachPenalty,
                CabinPenalty = cabinPenalty
            };
        }

        private void ApplyNoSmokingPenalties(ref int score, List<string> notes)
        {
            var approachSamples = _telemetryLog
                .Where(s => !s.OnGround && s.AltitudeAGL > 0 && s.AltitudeAGL < 3000 && s.IndicatedAirspeed < 230)
                .ToList();

            if (approachSamples.Count == 0) return;

            if (approachSamples.Count(s => !s.NoSmokingSign) > approachSamples.Count / 2)
            {
                score -= 4;
                notes.Add("no smoking sign -4");
            }
        }

        private void ApplyCabinPressurePenalties(ref int score, List<string> notes)
        {
            var cruiseSamples = _telemetryLog
                .Where(s => !s.OnGround && s.AltitudeFeet > 10000)
                .ToList();

            if (cruiseSamples.Count == 0) return;

            var highCabinSamples = cruiseSamples.Where(s => s.CabinAltitudeFeet > 10000).ToList();
            var moderateCabinSamples = cruiseSamples.Where(s => s.CabinAltitudeFeet > 8500 && s.CabinAltitudeFeet <= 10000).ToList();

            if (highCabinSamples.Count > cruiseSamples.Count / 4)
            {
                score -= 15;
                notes.Add("presion de cabina critica -15");
            }
            else if (moderateCabinSamples.Count > cruiseSamples.Count / 3)
            {
                score -= 8;
                notes.Add("altitud de cabina -8");
            }
        }

        private double ComputeApproachQnh()
        {
            var samples = _telemetryLog
                .Where(s => !s.OnGround && s.AltitudeAGL > 0 && s.AltitudeAGL < 3000 && s.QNH > 900)
                .ToList();
            if (samples.Count == 0) return 0;
            return Math.Round(samples.Average(s => s.QNH), 1);
        }

        private void ApplyLandingPenalties(ref int score, List<string> notes)
        {
            var landingVs = Math.Abs(_lastLandingVS);
            if (landingVs > 700)
            {
                score -= 30;
                notes.Add("VS de aterrizaje -30");
            }
            else if (landingVs > 400)
            {
                score -= 20;
                notes.Add("VS de aterrizaje -20");
            }
            else if (landingVs > 200)
            {
                score -= 10;
                notes.Add("VS de aterrizaje -10");
            }

            if (_lastLandingG > 2.0)
            {
                score -= 20;
                notes.Add("factor G -20");
            }
            else if (_lastLandingG > 1.5)
            {
                score -= 10;
                notes.Add("factor G -10");
            }
        }

        private void ApplyTaxiPenalties(ref int score, List<string> notes)
        {
            var taxiSamples = _telemetryLog
                .Where(sample => sample.OnGround && sample.GroundSpeed >= 5 && sample.GroundSpeed <= 40)
                .ToList();

            if (taxiSamples.Count == 0)
            {
                return;
            }

            if (taxiSamples.Count(sample => !sample.BeaconLightsOn) > taxiSamples.Count / 3)
            {
                score -= 6;
                notes.Add("beacon en taxi -6");
            }

            if (taxiSamples.Count(sample => !sample.TaxiLightsOn) > taxiSamples.Count / 2)
            {
                score -= 4;
                notes.Add("taxi lights -4");
            }

            if (taxiSamples.Count(sample => sample.LandingLightsOn) > taxiSamples.Count / 2)
            {
                score -= 4;
                notes.Add("landing lights en taxi -4");
            }
        }

        private void ApplyAirbornePenalties(ref int score, List<string> notes)
        {
            var airborneSamples = _telemetryLog
                .Where(IsOperationalAirborneSample)
                .ToList();

            if (airborneSamples.Count == 0)
            {
                return;
            }

            if (airborneSamples.Any(sample => sample.Pause))
            {
                score -= 8;
                notes.Add("pausa en vuelo -8");
            }

            if (airborneSamples.Count(sample => !sample.StrobeLightsOn) > airborneSamples.Count / 4)
            {
                score -= 8;
                notes.Add("strobes en vuelo -8");
            }

            if (airborneSamples.Count(sample => !sample.TransponderCharlieMode) > airborneSamples.Count / 3)
            {
                score -= 6;
                notes.Add("transponder CHARLIE -6");
            }

            if (airborneSamples.Count(sample => !sample.BeaconLightsOn) > airborneSamples.Count / 3)
            {
                score -= 6;
                notes.Add("beacon en vuelo -6");
            }

            var takeoffLightingSamples = airborneSamples
                .Where(sample => sample.AltitudeAGL < 1500 || sample.IndicatedAirspeed < 200)
                .ToList();

            if (takeoffLightingSamples.Count > 0 &&
                takeoffLightingSamples.Count(sample => !sample.NavLightsOn || !sample.BeaconLightsOn) > takeoffLightingSamples.Count / 2)
            {
                score -= 8;
                notes.Add("luces en salida -8");
            }
        }

        private void ApplyApproachPenalties(ref int score, List<string> notes)
        {
            var approachSamples = _telemetryLog
                .Where(sample =>
                    !sample.OnGround &&
                    sample.AltitudeAGL > 0 &&
                    sample.AltitudeAGL < 3000 &&
                    sample.IndicatedAirspeed < 230)
                .ToList();

            if (approachSamples.Count == 0)
            {
                return;
            }

            if (approachSamples.Count(sample => !sample.GearDown) > approachSamples.Count / 2)
            {
                score -= 10;
                notes.Add("gear en aproximacion -10");
            }

            if (approachSamples.Count(sample => !sample.FlapsDeployed && sample.IndicatedAirspeed < 190) > approachSamples.Count / 3)
            {
                score -= 8;
                notes.Add("flaps en aproximacion -8");
            }

            if (approachSamples.Count(sample => !sample.LandingLightsOn) > approachSamples.Count / 2)
            {
                score -= 6;
                notes.Add("landing lights en aproximacion -6");
            }

            if (approachSamples.Count(sample => !sample.SeatBeltSign) > approachSamples.Count / 2)
            {
                score -= 4;
                notes.Add("seat belt sign -4");
            }
        }

        private static bool IsOperationalAirborneSample(SimData sample)
        {
            return !sample.OnGround || sample.AltitudeAGL > 150 || sample.IndicatedAirspeed > 80;
        }

        private static string GetGrade(int score)
        {
            if (score >= 95) return "A+";
            if (score >= 90) return "A";
            if (score >= 80) return "B";
            if (score >= 70) return "C";
            if (score >= 60) return "D";
            return "F";
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
            SetFlightLock(false);
            SetPhase(FlightPhase.Disconnected);
        }
    }
}
