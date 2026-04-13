#nullable enable
using System;
using System.Collections.Generic;
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
        private MobiFlightIntegration? _mobiFlight;

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

                // Inicializar integración con MobiFlight para LVARs
                try
                {
                    Debug.WriteLine("[SimConnect] Inicializando MobiFlightIntegration...");
                    _mobiFlight = new MobiFlightIntegration();
                    _mobiFlight.Initialize(_simConnect);
                    
                    if (_mobiFlight.IsAvailable)
                    {
                        Debug.WriteLine("[SimConnect] ✓ MobiFlight WASM Module detectado - LVARs disponibles");
                    }
                    else
                    {
                        Debug.WriteLine("[SimConnect] ✗ MobiFlight no disponible - El módulo WASM no está instalado en MSFS");
                        Debug.WriteLine("[SimConnect] Para usar A319 Headwind completo, instalar MobiFlight WASM Module");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SimConnect] ERROR inicializando MobiFlight: {ex.Message}");
                    Debug.WriteLine($"[SimConnect] StackTrace: {ex.StackTrace}");
                    _mobiFlight = null;
                }

                // La conexión real se confirma cuando SimConnect emite OnRecvOpen y empieza
                // a aceptar solicitudes de datos. Antes de eso no marcamos la app como conectada
                // para evitar "verde falso" sin telemetría.
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
            
            // Título del avión para detección de tipo
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, sc);
            
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

            // Fuel - múltiples variables para compatibilidad con diferentes aviones
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL TOTAL QUANTITY WEIGHT", "pounds", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL TOTAL CAPACITY", "pounds", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL LEFT QUANTITY", "pounds", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL RIGHT QUANTITY", "pounds", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL CENTER QUANTITY", "pounds", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            
            // Motores - múltiples variables para compatibilidad
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GENERAL ENG FUEL FLOW PPH:1", "pounds per hour", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GENERAL ENG FUEL FLOW PPH:2", "pounds per hour", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "ENG N1 RPM:1", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "ENG N1 RPM:2", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TURB ENG N1:1", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TURB ENG N1:2", "percent", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

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
                    string aircraftTitle = raw.Title ?? "Unknown";
                    Debug.WriteLine($"[SimConnect AIRCRAFT] Title: {aircraftTitle}");
                    Debug.WriteLine($"[SimConnect RAW] LAT={raw.Latitude:F4} LON={raw.Longitude:F4} ALT={raw.AltitudeFeet:F0}");
                    Debug.WriteLine($"[SimConnect FUEL] Total={raw.FuelTotalLbs:F0} Left={raw.FuelLeftQuantity:F0} Right={raw.FuelRightQuantity:F0} Center={raw.FuelCenterQuantity:F0} Cap={raw.FuelTotalCapacity:F0}");
                    Debug.WriteLine($"[SimConnect ENG] N1_1={raw.Engine1N1:F1} N1_2={raw.Engine2N1:F1} TURB1={raw.TurbEngN1_1:F1} TURB2={raw.TurbEngN1_2:F1}");
                    Debug.WriteLine($"[SimConnect LIGHTS] B:{raw.BeaconLights} S:{raw.StrobeLights} L:{raw.LandingLights} T:{raw.TaxiLights} N:{raw.NavLights}");
                    Debug.WriteLine($"[SimConnect SQUAWK] Raw:{raw.TransponderCode} (0x{raw.TransponderCode:X}) State:{raw.TransponderState}");
                    Debug.WriteLine($"[SimConnect CABIN] Alt:{raw.CabinAltitudeFeet:F1} Press:{raw.PressureDiffPsi:F2}");
                    var simData = MapToSimData(raw, _lastEnv);
                    simData.IsConnected = true;
                    simData.SimulatorType = DetectedSimulator == SimulatorType.None ? SimulatorType.MSFS2020 : DetectedSimulator;
                    simData.Pause = _isPaused;
                    
                    // Si es A319 y MobiFlight está disponible, solicitar LVARs
                    if (_mobiFlight?.IsAvailable == true && raw.Title?.Contains("A319") == true)
                    {
                        _mobiFlight.RequestA319Lvars();
                        simData = _mobiFlight.EnrichWithLvars(simData, "A319 Headwind");
                        Debug.WriteLine("[SimConnect] Datos A319 enriquecidos con LVARs de MobiFlight");
                    }

                    if (!_hasReceivedAircraftData)
                    {
                        _hasReceivedAircraftData = true;
                        if (!IsConnected)
                        {
                            IsConnected = true;
                            DetectedSimulator = simData.SimulatorType;
                            Debug.WriteLine("[SimConnect] Primera conexión establecida");
                            Connected?.Invoke();
                        }
                    }

                    DataReceived?.Invoke(simData);
                    Debug.WriteLine("[SimConnect] Datos enviados a Coordinator");
                }
                else if (data.dwRequestID == (uint)RequestId.EnvironmentData)
                {
                    _lastEnv = (EnvironmentDataStruct)data.dwData[0];
                }
                else if (_mobiFlight?.IsAvailable == true)
                {
                    // Procesar datos de MobiFlight (LVARs)
                    _mobiFlight.ProcessSimObjectData(data);
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
            // Calcular fuel total con fallback
            double fuelTotal = r.FuelTotalLbs;
            if (fuelTotal <= 0.1)
            {
                // Intentar sumar tanques individuales
                fuelTotal = r.FuelLeftQuantity + r.FuelRightQuantity + r.FuelCenterQuantity;
            }
            if (fuelTotal <= 0.1)
            {
                // Usar capacity como último recurso (A319 Headwind a veces reporta aquí)
                fuelTotal = r.FuelTotalCapacity;
            }
            
            // Calcular N1 con fallback
            double n1Eng1 = r.Engine1N1 > 0.1 ? r.Engine1N1 : r.TurbEngN1_1;
            double n1Eng2 = r.Engine2N1 > 0.1 ? r.Engine2N1 : r.TurbEngN1_2;
            
            // Decodificar squawk - validar que sea un código válido (positivo y < 9999)
            int squawkDecoded = DecodeBco16(r.TransponderCode);
            if (squawkDecoded < 0 || squawkDecoded > 9999)
            {
                squawkDecoded = 0; // Inválido
            }
            
            Debug.WriteLine($"[SimConnect] Fuel={fuelTotal:F0} N1_1={n1Eng1:F1} N1_2={n1Eng2:F1} Squawk={squawkDecoded}");
            
            return new SimData
            {
                AircraftTitle = r.Title ?? "Unknown",
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

                FuelTotalLbs = fuelTotal,
                FuelFlowLbsHour = Math.Max(0, r.Engine1FuelFlowPph) + Math.Max(0, r.Engine2FuelFlowPph),
                Engine1N1 = n1Eng1,
                Engine2N1 = n1Eng2,
                
                // Tanques individuales para diagnóstico
                FuelLeftTankLbs = r.FuelLeftQuantity,
                FuelRightTankLbs = r.FuelRightQuantity,
                FuelCenterTankLbs = r.FuelCenterQuantity,
                FuelTotalCapacityLbs = r.FuelTotalCapacity,

                LandingVS = r.VerticalSpeed,
                LandingG = 0,

                OnGround = r.OnGround != 0,
                ParkingBrake = r.ParkingBrake != 0,
                AutopilotActive = r.AutopilotActive != 0,

                StrobeLightsOn = r.StrobeLights > 0,
                BeaconLightsOn = r.BeaconLights > 0,
                LandingLightsOn = r.LandingLights > 0,
                TaxiLightsOn = r.TaxiLights > 0,
                NavLightsOn = r.NavLights > 0,

                SeatBeltSign = r.SeatBeltSign != 0,
                NoSmokingSign = r.NoSmokingSign != 0,

                GearDown = r.GearHandleDown != 0,
                GearTransitioning = false,
                FlapsDeployed = r.FlapsPercent > 0.01,
                FlapsPercent = r.FlapsPercent,
                SpoilersArmed = r.SpoilersHandlePercent > 0.01,
                ReverserActive = false,

                TransponderCode = squawkDecoded,
                TransponderCharlieMode = r.TransponderState >= 3,

                ApuAvailable = r.ApuPct > 1,
                ApuRunning = r.ApuPct > 85,

                BleedAirOn = false,
                CabinAltitudeFeet = (r.CabinAltitudeFeet >= 0 && r.CabinAltitudeFeet < 50000) ? r.CabinAltitudeFeet : 0,
                PressureDiffPsi = (r.PressureDiffPsi >= 0 && r.PressureDiffPsi < 20 && r.PressureDiffPsi > 0) ? r.PressureDiffPsi : 0,

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

        /// <summary>
        /// Integración con MobiFlight WASM Module.
        /// Se instala silenciosamente con el ACARS y permite leer LVARs.
        /// </summary>
        private class MobiFlightIntegration : IDisposable
        {
            private Microsoft.FlightSimulator.SimConnect.SimConnect? _simConnect;
            private bool _isAvailable;
            private bool _disposed;
            
            // Eventos SimConnect para MobiFlight
            private const uint EVENT_LVAR_READ = 1000;
            private const uint DATA_ID_LVARS = 2000;
            
            // Cache de valores LVAR leídos
            private readonly Dictionary<string, double> _lvarCache = new();
            
            public bool IsAvailable => _isAvailable;
            
            public void Initialize(Microsoft.FlightSimulator.SimConnect.SimConnect simConnect)
            {
                _simConnect = simConnect;
                
                try
                {
                    RegisterLvarDataDefinition();
                    _isAvailable = true;
                    Debug.WriteLine("[MobiFlightIntegration] Módulo WASM detectado y listo");
                }
                catch (Exception ex)
                {
                    _isAvailable = false;
                    Debug.WriteLine($"[MobiFlightIntegration] Módulo no disponible: {ex.Message}");
                }
            }
            
            private void RegisterLvarDataDefinition()
            {
                if (_simConnect == null) return;
                
                uint sc = Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED;
                
                // Variables del A319
                _simConnect.AddToDataDefinition(
                    (DataDefineId)DATA_ID_LVARS,
                    "MOBIFLIGHT_A319_LIGHT_BEACON",
                    "Number",
                    SIMCONNECT_DATATYPE.FLOAT64,
                    0, sc);
                    
                _simConnect.AddToDataDefinition(
                    (DataDefineId)DATA_ID_LVARS + 1,
                    "MOBIFLIGHT_A319_LIGHT_STROBE",
                    "Number",
                    SIMCONNECT_DATATYPE.FLOAT64,
                    0, sc);
                    
                _simConnect.AddToDataDefinition(
                    (DataDefineId)DATA_ID_LVARS + 2,
                    "MOBIFLIGHT_A319_LIGHT_LANDING",
                    "Number",
                    SIMCONNECT_DATATYPE.FLOAT64,
                    0, sc);
                    
                _simConnect.AddToDataDefinition(
                    (DataDefineId)DATA_ID_LVARS + 3,
                    "MOBIFLIGHT_A319_ENG_N1_1",
                    "Percent",
                    SIMCONNECT_DATATYPE.FLOAT64,
                    0, sc);
                    
                _simConnect.AddToDataDefinition(
                    (DataDefineId)DATA_ID_LVARS + 4,
                    "MOBIFLIGHT_A319_ENG_N1_2",
                    "Percent",
                    SIMCONNECT_DATATYPE.FLOAT64,
                    0, sc);
            }
            
            public double? ReadA319Lvar(string variableName)
            {
                if (!_isAvailable || _simConnect == null)
                    return null;
                    
                try
                {
                    if (_lvarCache.TryGetValue(variableName, out var cachedValue))
                    {
                        return cachedValue;
                    }
                    
                    Debug.WriteLine($"[MobiFlightIntegration] Solicitando LVAR: {variableName}");
                    return null;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MobiFlightIntegration] Error leyendo {variableName}: {ex.Message}");
                    return null;
                }
            }
            
            public void ProcessSimObjectData(SIMCONNECT_RECV_SIMOBJECT_DATA data)
            {
                if (data.dwRequestID >= DATA_ID_LVARS && data.dwRequestID <= DATA_ID_LVARS + 10)
                {
                    var value = (double)data.dwData[0];
                    var lvarIndex = data.dwRequestID - DATA_ID_LVARS;
                    
                    Debug.WriteLine($"[MobiFlightIntegration] LVAR[{lvarIndex}] = {value}");
                    
                    switch (lvarIndex)
                    {
                        case 0: _lvarCache["Beacon"] = value; break;
                        case 1: _lvarCache["Strobe"] = value; break;
                        case 2: _lvarCache["Landing"] = value; break;
                        case 3: _lvarCache["Engine1N1"] = value; break;
                        case 4: _lvarCache["Engine2N1"] = value; break;
                    }
                }
            }
            
            public void RequestA319Lvars()
            {
                if (!_isAvailable || _simConnect == null) return;
                
                uint userObjectId = Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER;
                
                for (uint i = 0; i < 5; i++)
                {
                    _simConnect.RequestDataOnSimObject(
                        (RequestId)(DATA_ID_LVARS + i),
                        (DataDefineId)(DATA_ID_LVARS + i),
                        userObjectId,
                        SIMCONNECT_PERIOD.SECOND,
                        SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                        0, 0, 0);
                }
            }
            
            public SimData EnrichWithLvars(SimData baseData, string aircraftType)
            {
                if (!_isAvailable || aircraftType != "A319 Headwind")
                    return baseData;
                    
                if (_lvarCache.TryGetValue("Beacon", out var beacon))
                    baseData.BeaconLightsOn = beacon > 0.5;
                    
                if (_lvarCache.TryGetValue("Strobe", out var strobe))
                    baseData.StrobeLightsOn = strobe > 0.5;
                    
                if (_lvarCache.TryGetValue("Landing", out var landing))
                    baseData.LandingLightsOn = landing > 0.5;
                    
                if (_lvarCache.TryGetValue("Engine1N1", out var n1_1))
                    baseData.Engine1N1 = n1_1;
                    
                if (_lvarCache.TryGetValue("Engine2N1", out var n1_2))
                    baseData.Engine2N1 = n1_2;
                    
                return baseData;
            }
            
            public void Dispose()
            {
                if (_disposed) return;
                
                _simConnect = null;
                _disposed = true;
            }
        }

        /// <summary>
        /// Decodifica un valor BCO16 (Binary Coded Octal) del transponder a un entero.
        /// El formato BCO16 representa cada dígito octal en 3 bits.
        /// </summary>
        private static int DecodeBco16(int bcoValue)
        {
            if (bcoValue < 0)
            {
                // Valor inválido
                return 0;
            }
            
            int result = 0;
            int multiplier = 1;
            int temp = bcoValue;
            
            // Cada dígito octal está en 3 bits
            while (temp > 0)
            {
                int digit = temp & 0x7; // Extraer 3 bits (0-7)
                result += digit * multiplier;
                multiplier *= 10;
                temp >>= 3; // Shift 3 bits
            }
            
            return result;
        }
    }
}
