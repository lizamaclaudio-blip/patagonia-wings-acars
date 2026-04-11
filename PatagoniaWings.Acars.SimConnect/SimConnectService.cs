using System;
using System.Windows.Interop;
using Microsoft.FlightSimulator.SimConnect;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.SimConnect
{
    /// <summary>
    /// Servicio de integración con Microsoft Flight Simulator via SimConnect.
    /// Soporta MSFS 2020 y MSFS 2024.
    /// </summary>
    public class SimConnectService : IDisposable
    {
        private const int WM_USER_SIMCONNECT = 0x0402;
        private Microsoft.FlightSimulator.SimConnect.SimConnect? _simConnect;
        private HwndSource? _hwndSource;
        private bool _disposed;
        private bool _isPaused;

        public bool IsConnected { get; private set; }
        public SimulatorType DetectedSimulator { get; private set; }

        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<SimData>? DataReceived;
        public event Action<bool>? PauseChanged;
        public event Action? Crashed;

        public void Connect(IntPtr windowHandle)
        {
            if (IsConnected) return;

            _hwndSource = HwndSource.FromHwnd(windowHandle);
            _hwndSource?.AddHook(WndProc);

            try
            {
                _simConnect = new Microsoft.FlightSimulator.SimConnect.SimConnect(
                    "PatagoniaWings ACARS",
                    windowHandle,
                    WM_USER_SIMCONNECT,
                    null,
                    0);

                RegisterDataDefinitions();
                RegisterEvents();
                IsConnected = true;
                DetectedSimulator = SimulatorType.MSFS2020;
                Connected?.Invoke();
                RequestData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SimConnect error: {ex.Message}");
                IsConnected = false;
            }
        }

        private void RegisterDataDefinitions()
        {
            if (_simConnect == null) return;

            // Definición de datos del avión
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE LATITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE LONGITUDE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE ALTITUDE", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE ALT ABOVE GROUND", "feet", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AIRSPEED INDICATED", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GROUND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "VERTICAL SPEED", "feet per minute", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE HEADING DEGREES TRUE", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE PITCH DEGREES", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE BANK DEGREES", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL TOTAL QUANTITY WEIGHT", "pounds", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "ENG FUEL FLOW PPH:1", "pounds per hour", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "ENG N1 RPM:1", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "ENG N1 RPM:2", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "VERTICAL SPEED", "feet per minute", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "G FORCE", "GForce", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "SIM ON GROUND", "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT STROBE", "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT BEACON", "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT LANDING", "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "BRAKE PARKING POSITION", "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AUTOPILOT MASTER", "bool", SIMCONNECT_DATATYPE.INT32, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);

            _simConnect.RegisterDataDefineStruct<AircraftDataStruct>(DataDefineId.AircraftData);

            // Datos de entorno
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT TEMPERATURE", "celsius", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT WIND VELOCITY", "knots", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT WIND DIRECTION", "degrees", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "SEA LEVEL PRESSURE", "millibars", SIMCONNECT_DATATYPE.FLOAT64, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT PRECIP STATE", "mask", SIMCONNECT_DATATYPE.INT32, 0.0f, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);

            _simConnect.RegisterDataDefineStruct<EnvironmentDataStruct>(DataDefineId.EnvironmentData);
        }

        private void RegisterEvents()
        {
            if (_simConnect == null) return;

            _simConnect.OnRecvSimobjectData += OnRecvSimobjectData;
            _simConnect.OnRecvQuit += OnRecvQuit;
            _simConnect.OnRecvException += OnRecvException;
            _simConnect.OnRecvEvent += OnRecvEvent;

            _simConnect.SubscribeToSystemEvent(EventId.Pause, "Pause");
            _simConnect.SubscribeToSystemEvent(EventId.Crashed, "Crashed");
        }

        private void RequestData()
        {
            if (_simConnect == null || !IsConnected) return;

            _simConnect.RequestDataOnSimObject(
                RequestId.AircraftData,
                DataDefineId.AircraftData,
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SECOND,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 1, 0);

            _simConnect.RequestDataOnSimObject(
                RequestId.EnvironmentData,
                DataDefineId.EnvironmentData,
                Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                SIMCONNECT_PERIOD.SECOND,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 5, 0);
        }

        private void OnRecvSimobjectData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            if (data.dwRequestID == (uint)RequestId.AircraftData)
            {
                var raw = (AircraftDataStruct)data.dwData[0];
                var simData = MapToSimData(raw);
                simData.IsConnected = true;
                simData.SimulatorType = DetectedSimulator;
                simData.Pause = _isPaused;
                DataReceived?.Invoke(simData);
            }
        }

        private static SimData MapToSimData(AircraftDataStruct raw) => new SimData
        {
            Latitude = raw.Latitude,
            Longitude = raw.Longitude,
            AltitudeFeet = raw.AltitudeFeet,
            AltitudeAGL = raw.AltitudeAGL,
            IndicatedAirspeed = raw.IndicatedAirspeed,
            GroundSpeed = raw.GroundSpeed,
            VerticalSpeed = raw.VerticalSpeed,
            Heading = raw.Heading,
            Pitch = raw.Pitch,
            Bank = raw.Bank,
            FuelTotalLbs = raw.FuelTotalLbs,
            FuelFlowLbsHour = raw.FuelFlowLbsHour,
            Engine1N1 = raw.Engine1N1,
            Engine2N1 = raw.Engine2N1,
            LandingVS = raw.LandingVS,
            LandingG = raw.GForce,
            OnGround = raw.OnGround != 0,
            StrobeLightsOn = raw.StrobeLights != 0,
            BeaconLightsOn = raw.BeaconLights != 0,
            LandingLightsOn = raw.LandingLights != 0,
            ParkingBrake = raw.ParkingBrake != 0,
            AutopilotActive = raw.AutopilotActive != 0
        };

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
            Disconnected?.Invoke();
        }

        private void OnRecvException(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
        {
            System.Diagnostics.Debug.WriteLine($"SimConnect exception: {data.dwException}");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_USER_SIMCONNECT)
            {
                _simConnect?.ReceiveMessage();
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Disconnect()
        {
            if (_simConnect != null)
            {
                try { _simConnect.Dispose(); }
                catch { }
                _simConnect = null;
            }
            IsConnected = false;
            Disconnected?.Invoke();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _hwndSource?.RemoveHook(WndProc);
                _disposed = true;
            }
        }
    }
}
