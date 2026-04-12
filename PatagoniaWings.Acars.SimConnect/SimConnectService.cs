#nullable enable
using System;
using System.Diagnostics;
using System.Windows.Interop;
using Microsoft.FlightSimulator.SimConnect;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.SimConnect
{
    /// <summary>
    /// Integración MSFS 2020/2024 enfocada primero en telemetría estable.
    /// Se usan simvars conservadoras y ampliamente soportadas para asegurar lectura real.
    /// </summary>
    public sealed class SimConnectService : IDisposable
    {
        private const int WM_USER_SIMCONNECT = 0x0402;

        private Microsoft.FlightSimulator.SimConnect.SimConnect? _simConnect;
        private HwndSource? _hwndSource;
        private bool _disposed;
        private bool _isPaused;
        private bool _hasReceivedAircraftData;
        private EnvironmentDataStruct _lastEnv;

        public bool IsConnected { get; private set; }
        public SimulatorType DetectedSimulator { get; private set; } = SimulatorType.None;

        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<SimData>? DataReceived;
        public event Action<bool>? PauseChanged;
        public event Action? Crashed;

        public void Connect(IntPtr windowHandle)
        {
            if (IsConnected)
            {
                return;
            }

            try
            {
                _hwndSource = HwndSource.FromHwnd(windowHandle);
                _hwndSource?.AddHook(WndProc);

                _simConnect = new Microsoft.FlightSimulator.SimConnect.SimConnect(
                    "PatagoniaWings ACARS",
                    windowHandle,
                    WM_USER_SIMCONNECT,
                    null,
                    0);

                RegisterDataDefinitions();
                RegisterEvents();

                // La conexión real se confirma cuando SimConnect emite OnRecvOpen y empieza
                // a aceptar solicitudes de datos. Antes de eso no marcamos la app como conectada
                // para evitar “verde falso” sin telemetría.
                IsConnected = false;
                DetectedSimulator = SimulatorType.None;
                _hasReceivedAircraftData = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SimConnect Connect error: {ex}");
                IsConnected = false;
                DetectedSimulator = SimulatorType.None;
                throw;
            }
        }

        private void RegisterDataDefinitions()
        {
            if (_simConnect == null)
            {
                return;
            }

            uint sc = Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED;

            // ---------------------------
            // TELEMETRÍA BASE SEGURA
            // ---------------------------
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE ALT ABOVE GROUND", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AIRSPEED INDICATED", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GROUND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "VERTICAL SPEED", "feet per minute", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE HEADING DEGREES TRUE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE PITCH DEGREES", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE BANK DEGREES", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL TOTAL QUANTITY WEIGHT", "pounds", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GENERAL ENG FUEL FLOW PPH:1", "pounds per hour", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GENERAL ENG FUEL FLOW PPH:2", "pounds per hour", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "ENG N1 RPM:1", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "ENG N1 RPM:2", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "SIM ON GROUND", "bool", SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "BRAKE PARKING POSITION", "bool", SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AUTOPILOT MASTER", "bool", SIMCONNECT_DATATYPE.INT32, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT STROBE", "bool", SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT BEACON", "bool", SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT LANDING", "bool", SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT TAXI", "bool", SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT NAV", "bool", SIMCONNECT_DATATYPE.INT32, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GEAR HANDLE POSITION", "bool", SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FLAPS HANDLE PERCENT", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "SPOILERS HANDLE POSITION", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TRANSPONDER STATE:1", "number", SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TRANSPONDER CODE:1", "bco16", SIMCONNECT_DATATYPE.INT32, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "APU PCT RPM", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PRESSURIZATION CABIN ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PRESSURIZATION PRESSURE DIFFERENTIAL", "psi", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "CABIN SEATBELTS ALERT SWITCH", "bool", SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "CABIN NO SMOKING ALERT SWITCH", "bool", SIMCONNECT_DATATYPE.INT32, 0, sc);

            _simConnect.RegisterDataDefineStruct<AircraftDataStruct>(DataDefineId.AircraftData);

            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT TEMPERATURE", "celsius", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT WIND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT WIND DIRECTION", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "SEA LEVEL PRESSURE", "millibars", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT PRECIP STATE", "number", SIMCONNECT_DATATYPE.INT32, 0, sc);

            _simConnect.RegisterDataDefineStruct<EnvironmentDataStruct>(DataDefineId.EnvironmentData);
        }

        private void RegisterEvents()
        {
            if (_simConnect == null)
            {
                return;
            }

            _simConnect.OnRecvOpen += OnRecvOpen;
            _simConnect.OnRecvSimobjectData += OnRecvSimobjectData;
            _simConnect.OnRecvQuit += OnRecvQuit;
            _simConnect.OnRecvException += OnRecvException;
            _simConnect.OnRecvEvent += OnRecvEvent;

            _simConnect.SubscribeToSystemEvent(EventId.Pause, "Pause");
            _simConnect.SubscribeToSystemEvent(EventId.Crashed, "Crashed");
        }

        private void RequestData()
        {
            if (_simConnect == null)
            {
                return;
            }

            uint userObjectId = Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER;

            _simConnect.RequestDataOnSimObject(
                RequestId.AircraftData,
                DataDefineId.AircraftData,
                userObjectId,
                SIMCONNECT_PERIOD.SECOND,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0,
                0,
                0);

            _simConnect.RequestDataOnSimObject(
                RequestId.EnvironmentData,
                DataDefineId.EnvironmentData,
                userObjectId,
                SIMCONNECT_PERIOD.SECOND,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0,
                0,
                0);
        }

        private void OnRecvSimobjectData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            try
            {
                if (data.dwRequestID == (uint)RequestId.AircraftData)
                {
                    var raw = (AircraftDataStruct)data.dwData[0];
                    var simData = MapToSimData(raw, _lastEnv);
                    simData.IsConnected = true;
                    simData.SimulatorType = DetectedSimulator == SimulatorType.None ? SimulatorType.MSFS2020 : DetectedSimulator;
                    simData.Pause = _isPaused;

                    if (!_hasReceivedAircraftData)
                    {
                        _hasReceivedAircraftData = true;
                        if (!IsConnected)
                        {
                            IsConnected = true;
                            DetectedSimulator = simData.SimulatorType;
                            Connected?.Invoke();
                        }
                    }

                    DataReceived?.Invoke(simData);
                }
                else if (data.dwRequestID == (uint)RequestId.EnvironmentData)
                {
                    _lastEnv = (EnvironmentDataStruct)data.dwData[0];
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnRecvSimobjectData error: {ex}");
            }
        }


        private void OnRecvOpen(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_OPEN data)
        {
            DetectedSimulator = SimulatorType.MSFS2020;
            RequestData();
        }

        private static SimData MapToSimData(AircraftDataStruct r, EnvironmentDataStruct e)
        {
            return new SimData
            {
                Latitude = r.Latitude,
                Longitude = r.Longitude,
                AltitudeFeet = r.AltitudeFeet,
                AltitudeAGL = r.AltitudeAGL,

                IndicatedAirspeed = r.IndicatedAirspeed,
                GroundSpeed = r.GroundSpeed,
                VerticalSpeed = r.VerticalSpeed,
                Heading = r.Heading,
                Pitch = r.Pitch,
                Bank = r.Bank,

                FuelTotalLbs = r.FuelTotalLbs,
                FuelFlowLbsHour = Math.Max(0, r.Engine1FuelFlowPph) + Math.Max(0, r.Engine2FuelFlowPph),
                Engine1N1 = r.Engine1N1,
                Engine2N1 = r.Engine2N1,

                LandingVS = r.VerticalSpeed,
                LandingG = 0,

                OnGround = r.OnGround != 0,
                ParkingBrake = r.ParkingBrake != 0,
                AutopilotActive = r.AutopilotActive != 0,

                StrobeLightsOn = r.StrobeLights != 0,
                BeaconLightsOn = r.BeaconLights != 0,
                LandingLightsOn = r.LandingLights != 0,
                TaxiLightsOn = r.TaxiLights != 0,
                NavLightsOn = r.NavLights != 0,

                SeatBeltSign = r.SeatBeltSign != 0,
                NoSmokingSign = r.NoSmokingSign != 0,

                GearDown = r.GearHandleDown != 0,
                GearTransitioning = false,
                FlapsDeployed = r.FlapsPercent > 0.01,
                FlapsPercent = r.FlapsPercent,
                SpoilersArmed = r.SpoilersHandlePercent > 0.01,
                ReverserActive = false,

                TransponderCode = r.TransponderCode,
                TransponderCharlieMode = r.TransponderState >= 3,

                ApuAvailable = r.ApuPct > 1,
                ApuRunning = r.ApuPct > 85,

                BleedAirOn = false,
                CabinAltitudeFeet = r.CabinAltitudeFeet,
                PressureDiffPsi = r.PressureDiffPsi,

                OutsideTemperature = e.OutsideTemperature,
                WindSpeed = e.WindSpeed,
                WindDirection = e.WindDirection,
                QNH = e.SeaLevelPressure,
                IsRaining = e.PrecipState > 0
            };
        }

        private void OnRecvEvent(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EVENT data)
        {
            if (data.uEventID == (uint)EventId.Pause)
            {
                _isPaused = data.dwData != 0;
                PauseChanged?.Invoke(_isPaused);
            }
            else if (data.uEventID == (uint)EventId.Crashed)
            {
                Crashed?.Invoke();
            }
        }

        private void OnRecvQuit(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV data)
        {
            IsConnected = false;
            DetectedSimulator = SimulatorType.None;
            Disconnected?.Invoke();
        }

        private void OnRecvException(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            Debug.WriteLine($"SimConnect exception: {data.dwException}");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_USER_SIMCONNECT)
            {
                try
                {
                    _simConnect?.ReceiveMessage();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"ReceiveMessage error: {ex}");
                }

                handled = true;
            }

            return IntPtr.Zero;
        }

        public void Disconnect()
        {
            var wasConnected = IsConnected;

            if (_simConnect != null)
            {
                try
                {
                    _simConnect.Dispose();
                }
                catch
                {
                    // ignore
                }

                _simConnect = null;
            }

            IsConnected = false;
            DetectedSimulator = SimulatorType.None;

            // Solo emitir evento si alguna vez llegamos a estar conectados,
            // para evitar spam de "MSFS disconnected" en cada reintento fallido.
            if (wasConnected)
            {
                Disconnected?.Invoke();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Disconnect();
            _hwndSource?.RemoveHook(WndProc);
            _disposed = true;
        }
    }
}
