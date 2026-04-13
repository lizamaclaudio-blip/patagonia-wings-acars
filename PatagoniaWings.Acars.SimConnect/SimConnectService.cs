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
            _simConnect.OnRecvClientData += OnRecvClientData;
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
                    
                    // Detectar perfil de aeronave desde AircraftProfiles.json
                    string profileName = DetectProfileName(aircraftTitle);
                    simData.AircraftProfile = profileName;
                    Debug.WriteLine($"[SimConnect] Perfil detectado: '{profileName}' para '{aircraftTitle}'");

                    if (_mobiFlight?.IsAvailable == true && ProfileRequiresLvars(profileName))
                    {
                        _mobiFlight.RequestLvarsForProfile(profileName);
                        simData = _mobiFlight.EnrichWithLvars(simData);
                        Debug.WriteLine($"[SimConnect] LVARs '{profileName}' aplicadas");
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
                else
                {
                    _mobiFlight?.ProcessSimObjectData(data);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnRecvSimobjectData error: {ex}");
            }
        }


        private void OnRecvClientData(Microsoft.FlightSimulator.SimConnect.SimConnect sender, SIMCONNECT_RECV_CLIENT_DATA data)
        {
            try
            {
                _mobiFlight?.ProcessClientData(data);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnRecvClientData error: {ex.Message}");
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

        // ──────────────────────────────────────────────────────────────────────
        // AIRCRAFT PROFILE DETECTION — lee AircraftProfiles.json
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Detecta el nombre del perfil en AircraftProfiles.json que mejor coincide
        /// con el título del avión cargado en el simulador.
        /// </summary>
        private static string DetectProfileName(string aircraftTitle)
        {
            try
            {
                var exeDir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                var jsonPath = System.IO.Path.Combine(exeDir, "AircraftProfiles.json");
                if (!System.IO.File.Exists(jsonPath))
                {
                    Debug.WriteLine($"[Profile] AircraftProfiles.json no encontrado en {jsonPath}");
                    return "MSFS Native";
                }

                var json = System.IO.File.ReadAllText(jsonPath);
                var title = aircraftTitle.ToUpperInvariant();

                // Buscar coincidencia exacta primero (exact_titles)
                int exactIdx = json.IndexOf("\"exact_titles\"", StringComparison.OrdinalIgnoreCase);
                while (exactIdx >= 0)
                {
                    int start = json.IndexOf('[', exactIdx);
                    int end   = json.IndexOf(']', start);
                    if (start < 0 || end < 0) break;
                    var block = json.Substring(start, end - start + 1);
                    // Obtener el "name" de este perfil
                    int nameIdx = json.LastIndexOf("\"name\"", exactIdx);
                    if (nameIdx >= 0)
                    {
                        int q1 = json.IndexOf('"', nameIdx + 7);
                        int q2 = json.IndexOf('"', q1 + 1);
                        var profileName = json.Substring(q1 + 1, q2 - q1 - 1);
                        // Verificar si algún exact_title coincide
                        foreach (var segment in block.Split('"'))
                        {
                            var s = segment.Trim().Trim(',', '[', ']', ' ');
                            if (s.Length > 2 && title.Contains(s.ToUpperInvariant()))
                            {
                                Debug.WriteLine($"[Profile] exact_title match: '{s}' → {profileName}");
                                return profileName;
                            }
                        }
                    }
                    exactIdx = json.IndexOf("\"exact_titles\"", exactIdx + 1, StringComparison.OrdinalIgnoreCase);
                }

                // Buscar coincidencia en "matches"
                int matchesIdx = json.IndexOf("\"matches\"", StringComparison.OrdinalIgnoreCase);
                while (matchesIdx >= 0)
                {
                    int start = json.IndexOf('[', matchesIdx);
                    int end   = json.IndexOf(']', start);
                    if (start < 0 || end < 0) break;
                    var block = json.Substring(start, end - start + 1);
                    int nameIdx = json.LastIndexOf("\"name\"", matchesIdx);
                    if (nameIdx >= 0)
                    {
                        int q1 = json.IndexOf('"', nameIdx + 7);
                        int q2 = json.IndexOf('"', q1 + 1);
                        var profileName = json.Substring(q1 + 1, q2 - q1 - 1);
                        foreach (var segment in block.Split('"'))
                        {
                            var s = segment.Trim().Trim(',', '[', ']', ' ');
                            if (s == "*") continue;
                            if (s.Length > 1 && title.Contains(s.ToUpperInvariant()))
                            {
                                Debug.WriteLine($"[Profile] matches match: '{s}' → {profileName}");
                                return profileName;
                            }
                        }
                    }
                    matchesIdx = json.IndexOf("\"matches\"", matchesIdx + 1, StringComparison.OrdinalIgnoreCase);
                }

                Debug.WriteLine($"[Profile] Sin coincidencia para '{aircraftTitle}' → MSFS Native");
                return "MSFS Native";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Profile] Error detectando perfil: {ex.Message}");
                return "MSFS Native";
            }
        }

        /// <summary>
        /// Devuelve true si el perfil detectado requiere LVARs (A319, A320 FBW/Headwind, etc.)
        /// </summary>
        private static bool ProfileRequiresLvars(string profileName)
        {
            return profileName == "A319 Headwind"
                || profileName == "Fenix A320"
                || profileName == "Headwind A320"
                || profileName == "FlyByWire A32NX";
        }

        // ──────────────────────────────────────────────────────────────────────
        // LVAR INTEGRATION
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Lee LVARs vía SimConnect DataDefinition usando la sintaxis "(L:var, unit)".
        /// Compatible con A319 Headwind, Fenix A320 y cualquier aeronave con LVARs.
        /// </summary>
        private class MobiFlightIntegration : IDisposable
        {
            private Microsoft.FlightSimulator.SimConnect.SimConnect? _simConnect;
            private bool _isAvailable;
            private bool _disposed;
            private string _registeredProfile = "";

            private enum LvarDefId : uint { Block = 800 }
            private enum LvarReqId : uint { Block = 900 }

            private readonly Dictionary<string, double> _lvarCache = new Dictionary<string, double>();

            public bool IsAvailable => _isAvailable;

            public void Initialize(Microsoft.FlightSimulator.SimConnect.SimConnect simConnect)
            {
                _simConnect = simConnect;
                _isAvailable = true;
                Debug.WriteLine("[MobiFlight] LVAR integration lista — se activa al detectar aeronave");
            }

            // LVARs A32NX (FlyByWire A319/A320/A321 y Headwind A319)
            private static readonly (string Var, string Key)[] A32NX_LVARS =
            {
                ("(L:A32NX_ELEC_AC_ESS_BUS_IS_POWERED, bool)",     "Beacon"),
                ("(L:A32NX_ELEC_AC_1_BUS_IS_POWERED, bool)",        "BusPowered"),
                ("LIGHT BEACON",                                      "BeaconNative"),
                ("LIGHT STROBE",                                      "StrobeNative"),
                ("LIGHT LANDING",                                     "LandingNative"),
                ("LIGHT NAV",                                         "NavNative"),
                ("LIGHT TAXI",                                        "TaxiNative"),
                ("(L:A32NX_ENGINE_N1:1, percent)",                   "N1_1"),
                ("(L:A32NX_ENGINE_N1:2, percent)",                   "N1_2"),
            };

            // LVARs Fenix A320
            private static readonly (string Var, string Key)[] FENIX_LVARS =
            {
                ("LIGHT BEACON",                                      "BeaconNative"),
                ("LIGHT STROBE",                                      "StrobeNative"),
                ("LIGHT LANDING",                                     "LandingNative"),
                ("LIGHT NAV",                                         "NavNative"),
                ("LIGHT TAXI",                                        "TaxiNative"),
                ("TURB ENG N1:1",                                     "N1_1"),
                ("TURB ENG N1:2",                                     "N1_2"),
            };

            public void RequestLvarsForProfile(string profileName)
            {
                if (!_isAvailable || _simConnect == null) return;
                if (_registeredProfile == profileName) return;

                var lvars = profileName == "Fenix A320" ? FENIX_LVARS : A32NX_LVARS;

                try
                {
                    uint sc = Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED;
                    foreach (var (varName, _) in lvars)
                        _simConnect.AddToDataDefinition(LvarDefId.Block, varName, null, SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

                    _simConnect.RegisterDataDefineStruct<LvarBlock9>(LvarDefId.Block);
                    _simConnect.RequestDataOnSimObject(
                        LvarReqId.Block, LvarDefId.Block,
                        Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER,
                        SIMCONNECT_PERIOD.SECOND, SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

                    _registeredProfile = profileName;
                    Debug.WriteLine($"[MobiFlight] LVARs registradas para perfil '{profileName}' ({lvars.Length} vars)");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MobiFlight] Error registrando LVARs para '{profileName}': {ex.Message}");
                }
            }

            [System.Runtime.InteropServices.StructLayout(
                System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
            private struct LvarBlock9
            {
                public double V0; public double V1; public double V2;
                public double V3; public double V4; public double V5;
                public double V6; public double V7; public double V8;
            }

            public void ProcessSimObjectData(SIMCONNECT_RECV_SIMOBJECT_DATA data)
            {
                if (data.dwRequestID != (uint)LvarReqId.Block) return;
                try
                {
                    var block = (LvarBlock9)data.dwData[0];
                    double[] vals = { block.V0, block.V1, block.V2, block.V3, block.V4,
                                      block.V5, block.V6, block.V7, block.V8 };
                    var lvars = _registeredProfile == "Fenix A320" ? FENIX_LVARS : A32NX_LVARS;
                    for (int i = 0; i < lvars.Length && i < vals.Length; i++)
                        _lvarCache[lvars[i].Key] = vals[i];

                    _lvarCache.TryGetValue("BeaconNative", out var dbg_b);
                    _lvarCache.TryGetValue("StrobeNative", out var dbg_s);
                    _lvarCache.TryGetValue("N1_1",         out var dbg_n1);
                    _lvarCache.TryGetValue("N1_2",         out var dbg_n2);
                    Debug.WriteLine($"[MobiFlight] Cache '{_registeredProfile}': " +
                        $"Beacon={dbg_b} Strobe={dbg_s} N1_1={dbg_n1:F1} N1_2={dbg_n2:F1}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MobiFlight] Error procesando LVARs: {ex.Message}");
                }
            }

            public void ProcessClientData(SIMCONNECT_RECV_CLIENT_DATA data) { }

            public SimData EnrichWithLvars(SimData baseData)
            {
                if (!_isAvailable || _lvarCache.Count == 0) return baseData;

                if (_lvarCache.TryGetValue("BeaconNative",  out var b)) baseData.BeaconLightsOn  = b  > 0.5;
                if (_lvarCache.TryGetValue("StrobeNative",  out var s)) baseData.StrobeLightsOn  = s  > 0.5;
                if (_lvarCache.TryGetValue("LandingNative", out var l)) baseData.LandingLightsOn = l  > 0.5;
                if (_lvarCache.TryGetValue("NavNative",     out var n)) baseData.NavLightsOn     = n  > 0.5;
                if (_lvarCache.TryGetValue("TaxiNative",    out var t)) baseData.TaxiLightsOn    = t  > 0.5;
                if (_lvarCache.TryGetValue("N1_1",          out var n1)) baseData.Engine1N1      = n1;
                if (_lvarCache.TryGetValue("N1_2",          out var n2)) baseData.Engine2N1      = n2;

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
