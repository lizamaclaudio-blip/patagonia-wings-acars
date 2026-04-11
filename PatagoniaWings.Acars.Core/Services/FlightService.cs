using System;
using System.Collections.Generic;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    public class FlightService
    {
        private Flight? _currentFlight;
        private SimData _lastSimData = new SimData();
        private readonly List<SimData> _telemetryLog = new List<SimData>();

        // Estadísticas de vuelo
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

        public event Action<FlightPhase>? PhaseChanged;
        public event Action<SimData>? TelemetryUpdated;

        public void StartFlight(Flight flight, double initialFuel)
        {
            _currentFlight = flight;
            _fuelAtStart = initialFuel;
            _blockOutTime = DateTime.UtcNow;
            _maxAltitude = 0;
            _maxSpeed = 0;
            _totalDistanceNm = 0;
            _telemetryLog.Clear();
            _hasPosition = false;
            SetPhase(FlightPhase.PreFlight);
        }

        public void UpdateSimData(SimData data)
        {
            _lastSimData = data;

            if (_hasPosition)
            {
                _totalDistanceNm += CalculateDistanceNm(
                    _lastLatitude, _lastLongitude, data.Latitude, data.Longitude);
            }
            _lastLatitude = data.Latitude;
            _lastLongitude = data.Longitude;
            _hasPosition = true;

            if (data.AltitudeFeet > _maxAltitude) _maxAltitude = data.AltitudeFeet;
            if (data.IndicatedAirspeed > _maxSpeed) _maxSpeed = data.IndicatedAirspeed;

            _telemetryLog.Add(data);
            UpdatePhase(data);
            TelemetryUpdated?.Invoke(data);
        }

        private void UpdatePhase(SimData data)
        {
            if (_currentFlight == null) return;

            var newPhase = CurrentPhase;

            switch (CurrentPhase)
            {
                case FlightPhase.PreFlight:
                    if (!data.OnGround && data.IndicatedAirspeed > 60)
                        newPhase = FlightPhase.Takeoff;
                    else if (!data.ParkingBrake && data.GroundSpeed > 1)
                        newPhase = FlightPhase.PushbackTaxi;
                    break;

                case FlightPhase.Boarding:
                    if (!data.ParkingBrake && data.GroundSpeed > 1)
                        newPhase = FlightPhase.PushbackTaxi;
                    break;

                case FlightPhase.PushbackTaxi:
                    if (!data.OnGround && data.IndicatedAirspeed > 60)
                        newPhase = FlightPhase.Takeoff;
                    break;

                case FlightPhase.Takeoff:
                    if (data.AltitudeFeet > 1500)
                    {
                        _takeoffTime = DateTime.UtcNow;
                        newPhase = FlightPhase.Climb;
                    }
                    break;

                case FlightPhase.Climb:
                    if (data.VerticalSpeed < -100 && data.AltitudeFeet > 5000)
                        newPhase = FlightPhase.Cruise;
                    break;

                case FlightPhase.Cruise:
                    if (data.VerticalSpeed < -500)
                        newPhase = FlightPhase.Descent;
                    break;

                case FlightPhase.Descent:
                    if (data.AltitudeFeet < 3000)
                        newPhase = FlightPhase.Approach;
                    break;

                case FlightPhase.Approach:
                    if (data.OnGround && data.GroundSpeed < 60)
                    {
                        _lastLandingVS = data.LandingVS;
                        _lastLandingG = data.LandingG;
                        _touchdownTime = DateTime.UtcNow;
                        newPhase = FlightPhase.Landing;
                    }
                    break;

                case FlightPhase.Landing:
                    if (data.GroundSpeed < 5 && data.OnGround)
                        newPhase = FlightPhase.Taxi;
                    break;

                case FlightPhase.Taxi:
                    if (data.ParkingBrake && data.GroundSpeed < 1)
                        newPhase = FlightPhase.Arrived;
                    break;
            }

            if (newPhase != CurrentPhase)
                SetPhase(newPhase);
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
            var fuelUsed = _fuelAtStart - _lastSimData.FuelTotalLbs;
            var score = CalculateScore();

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
                Score = score,
                Grade = GetGrade(score),
                Simulator = _currentFlight.Simulator,
                Status = FlightStatus.Pending
            };
        }

        private int CalculateScore()
        {
            int score = 100;

            // Penalizar por aterrizaje brusco
            if (Math.Abs(_lastLandingVS) > 200) score -= 10;
            if (Math.Abs(_lastLandingVS) > 400) score -= 20;
            if (Math.Abs(_lastLandingVS) > 700) score -= 30;

            // Penalizar por G fuertes
            if (_lastLandingG > 1.5) score -= 10;
            if (_lastLandingG > 2.0) score -= 20;

            return Math.Max(0, score);
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
            const double R = 3440.065; // radio tierra en NM
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
            _telemetryLog.Clear();
            SetPhase(FlightPhase.Disconnected);
        }
    }
}
