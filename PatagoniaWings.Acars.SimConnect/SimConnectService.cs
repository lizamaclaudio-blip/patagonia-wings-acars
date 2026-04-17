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
    /// Integración MSFS 2020/2024 via SimConnect.
    ///
    /// Luces: se leen via LIGHT ON STATES (bitmask único) con SIMCONNECT_PERIOD.VISUAL_FRAME.
    /// Esto evita el bug conocido en MSFS donde los simvars individuales LIGHT STROBE/BEACON/etc.
    /// no actualizan en tiempo real con PERIOD.SECOND.
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

        // Estado de luces: actualizado via LIGHT ON STATES (VISUAL_FRAME) para respuesta inmediata
        private int _lastLightStates;
        private bool _hasLightData;

        // Última telemetría base para poder re-emitir al actualizar luces
        private AircraftDataStruct _lastAircraftRaw;
        private bool _hasAircraftRaw;

        private MobiFlightIntegration? _mobiFlight;

        public bool IsConnected { get; private set; }
        public SimulatorType DetectedSimulator { get; private set; } = SimulatorType.None;

        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<SimData>? DataReceived;
        public event Action<bool>? PauseChanged;
        public event Action? Crashed;

        // ──────────────────────────────────────────────────────────────────────
        // CONNECT / REGISTER
        // ──────────────────────────────────────────────────────────────────────

        public void Connect(IntPtr windowHandle)
        {
            if (IsConnected) return;

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

                // MobiFlight para LVARs (A320 FBW, etc.)
                try
                {
                    _mobiFlight = new MobiFlightIntegration();
                    _mobiFlight.Initialize(_simConnect);
                    Debug.WriteLine(_mobiFlight.IsAvailable
                        ? "[SimConnect] ✓ MobiFlight WASM detectado"
                        : "[SimConnect] ✗ MobiFlight no disponible");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SimConnect] MobiFlight init error: {ex.Message}");
                    _mobiFlight = null;
                }

                IsConnected = false;
                DetectedSimulator = SimulatorType.None;
                _hasReceivedAircraftData = false;
                _hasLightData  = false;
                _hasAircraftRaw = false;
                _lastLightStates = 0;
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
            if (_simConnect == null) return;

            uint sc = Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED;

            // ── AIRCRAFT DATA ─────────────────────────────────────────────────
            // Debe coincidir EXACTAMENTE con los campos de AircraftDataStruct (en orden)

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE LATITUDE",           "degrees",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE LONGITUDE",          "degrees",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE ALTITUDE",           "feet",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE ALT ABOVE GROUND",   "feet",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AIRSPEED INDICATED",       "knots",            SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GROUND VELOCITY",          "knots",            SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "VERTICAL SPEED",           "feet per minute",  SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE HEADING DEGREES TRUE","degrees",         SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE PITCH DEGREES",      "degrees",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE BANK DEGREES",       "degrees",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            // Fuel
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL TOTAL QUANTITY WEIGHT","pounds",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL TOTAL CAPACITY",       "pounds",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL LEFT QUANTITY",        "pounds",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL RIGHT QUANTITY",       "pounds",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL CENTER QUANTITY",      "pounds",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            // Motores
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GENERAL ENG FUEL FLOW PPH:1","pounds per hour",SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GENERAL ENG FUEL FLOW PPH:2","pounds per hour",SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "ENG N1 RPM:1",             "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "ENG N1 RPM:2",             "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TURB ENG N1:1",            "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TURB ENG N1:2",            "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            // Estado
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "SIM ON GROUND",            "bool",             SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "BRAKE PARKING POSITION",   "bool",             SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AUTOPILOT MASTER",         "bool",             SIMCONNECT_DATATYPE.INT32, 0, sc);

            // NOTA: LIGHT STROBE/BEACON/etc. eliminados — ahora vienen via LightsData (LIGHT ON STATES)

            // Controles de vuelo
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GEAR HANDLE POSITION",     "bool",             SIMCONNECT_DATATYPE.INT32,   0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FLAPS HANDLE PERCENT",     "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "SPOILERS HANDLE POSITION", "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            // Transponder
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TRANSPONDER STATE:1",      "number",           SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TRANSPONDER CODE:1",       "bco16",            SIMCONNECT_DATATYPE.INT32, 0, sc);

            // APU / Presurización / Cabina
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "APU PCT RPM",                        "percent", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PRESSURIZATION CABIN ALTITUDE",      "feet",    SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PRESSURIZATION PRESSURE DIFFERENTIAL","psi",    SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "CABIN SEATBELTS ALERT SWITCH",       "bool",    SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "CABIN NO SMOKING ALERT SWITCH",      "bool",    SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "BLEED AIR ENGINE:1",                 "bool",    SIMCONNECT_DATATYPE.INT32, 0, sc);

            _simConnect.RegisterDataDefineStruct<AircraftDataStruct>(DataDefineId.AircraftData);

            // ── ENVIRONMENT DATA ──────────────────────────────────────────────
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT TEMPERATURE",   "celsius",   SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT WIND VELOCITY", "knots",     SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT WIND DIRECTION","degrees",   SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "SEA LEVEL PRESSURE",    "millibars", SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT PRECIP STATE",  "number",    SIMCONNECT_DATATYPE.INT32,   0, sc);
            _simConnect.RegisterDataDefineStruct<EnvironmentDataStruct>(DataDefineId.EnvironmentData);

            // ── LIGHTS DATA — LIGHT ON STATES bitmask ─────────────────────────
            // Un único simvar nativo que agrupa TODAS las luces. Mucho más confiable
            // que leer LIGHT STROBE/BEACON/etc. individualmente en MSFS 2020/2024.
            // Se solicita con VISUAL_FRAME para respuesta inmediata al togglear.
            _simConnect.AddToDataDefinition(DataDefineId.LightsData, "LIGHT ON STATES", "Mask", SIMCONNECT_DATATYPE.INT32, 0, sc);
            _simConnect.RegisterDataDefineStruct<LightsDataStruct>(DataDefineId.LightsData);
        }

        private void RegisterEvents()
        {
            if (_simConnect == null) return;

            _simConnect.OnRecvOpen             += OnRecvOpen;
            _simConnect.OnRecvSimobjectData    += OnRecvSimobjectData;
            _simConnect.OnRecvClientData       += OnRecvClientData;
            _simConnect.OnRecvQuit             += OnRecvQuit;
            _simConnect.OnRecvException        += OnRecvException;
            _simConnect.OnRecvEvent            += OnRecvEvent;

            _simConnect.SubscribeToSystemEvent(EventId.Pause,   "Pause");
            _simConnect.SubscribeToSystemEvent(EventId.Crashed, "Crashed");
        }

        private void RequestData()
        {
            if (_simConnect == null) return;

            uint userObjectId = Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_OBJECT_ID_USER;

            // Datos de vuelo: cada segundo
            _simConnect.RequestDataOnSimObject(
                RequestId.AircraftData,
                DataDefineId.AircraftData,
                userObjectId,
                SIMCONNECT_PERIOD.SECOND,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);

            // Entorno: cada segundo
            _simConnect.RequestDataOnSimObject(
                RequestId.EnvironmentData,
                DataDefineId.EnvironmentData,
                userObjectId,
                SIMCONNECT_PERIOD.SECOND,
                SIMCONNECT_DATA_REQUEST_FLAG.DEFAULT,
                0, 0, 0);

            // ── LUCES: VISUAL_FRAME + CHANGED ────────────────────────────────
            // VISUAL_FRAME garantiza que capturamos el toggle de luz apenas ocurre
            // (sin esperar hasta el próximo ciclo de SECOND).
            // CHANGED evita inundar con datos cuando nada cambia.
            _simConnect.RequestDataOnSimObject(
                RequestId.LightsData,
                DataDefineId.LightsData,
                userObjectId,
                SIMCONNECT_PERIOD.VISUAL_FRAME,
                SIMCONNECT_DATA_REQUEST_FLAG.CHANGED,
                0, 0, 0);
        }

        // ──────────────────────────────────────────────────────────────────────
        // DATA RECEIVED
        // ──────────────────────────────────────────────────────────────────────

        private void OnRecvSimobjectData(
            Microsoft.FlightSimulator.SimConnect.SimConnect sender,
            SIMCONNECT_RECV_SIMOBJECT_DATA data)
        {
            try
            {
                if (data.dwRequestID == (uint)RequestId.AircraftData)
                {
                    HandleAircraftData((AircraftDataStruct)data.dwData[0]);
                }
                else if (data.dwRequestID == (uint)RequestId.EnvironmentData)
                {
                    _lastEnv = (EnvironmentDataStruct)data.dwData[0];
                }
                else if (data.dwRequestID == (uint)RequestId.LightsData)
                {
                    HandleLightsData((LightsDataStruct)data.dwData[0]);
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

        private void HandleAircraftData(AircraftDataStruct raw)
        {
            string aircraftTitle = raw.Title ?? "Unknown";
            Debug.WriteLine($"[SimConnect AIRCRAFT] Title: {aircraftTitle}");
            Debug.WriteLine($"[SimConnect RAW] LAT={raw.Latitude:F4} ALT={raw.AltitudeFeet:F0}");
            Debug.WriteLine($"[SimConnect FUEL] Total={raw.FuelTotalLbs:F0}");
            Debug.WriteLine($"[SimConnect LIGHTS] OnStates=0x{_lastLightStates:X3} Nav:{(_lastLightStates & 0x01) != 0} Beacon:{(_lastLightStates & 0x02) != 0} Landing:{(_lastLightStates & 0x04) != 0} Taxi:{(_lastLightStates & 0x08) != 0} Strobe:{(_lastLightStates & 0x10) != 0}");

            _lastAircraftRaw = raw;
            _hasAircraftRaw  = true;

            var simData = BuildSimData(raw, _lastEnv, _lastLightStates);
            simData.IsConnected    = true;
            simData.SimulatorType  = DetectedSimulator == SimulatorType.None ? SimulatorType.MSFS2020 : DetectedSimulator;
            simData.Pause          = _isPaused;

            string profileName = DetectProfileName(aircraftTitle);
            simData.AircraftProfile = profileName;

            if (_mobiFlight?.IsAvailable == true && ProfileRequiresLvars(profileName))
            {
                _mobiFlight.RequestLvarsForProfile(profileName);
                simData = _mobiFlight.EnrichWithLvars(simData);
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
        }

        private void HandleLightsData(LightsDataStruct lightsRaw)
        {
            _lastLightStates = lightsRaw.LightOnStates;
            _hasLightData    = true;

            Debug.WriteLine($"[SimConnect LIGHTS UPDATE] 0x{_lastLightStates:X3} " +
                $"Nav:{(_lastLightStates & 0x01) != 0} " +
                $"Beacon:{(_lastLightStates & 0x02) != 0} " +
                $"Landing:{(_lastLightStates & 0x04) != 0} " +
                $"Taxi:{(_lastLightStates & 0x08) != 0} " +
                $"Strobe:{(_lastLightStates & 0x10) != 0}");

            // Re-emitir datos con luces actualizadas si ya tenemos datos de vuelo
            if (!_hasAircraftRaw || !IsConnected) return;

            var simData = BuildSimData(_lastAircraftRaw, _lastEnv, _lastLightStates);
            simData.IsConnected   = true;
            simData.SimulatorType = DetectedSimulator;
            simData.Pause         = _isPaused;

            if (_mobiFlight?.IsAvailable == true && !string.IsNullOrEmpty(simData.AircraftProfile)
                && ProfileRequiresLvars(simData.AircraftProfile))
            {
                simData = _mobiFlight.EnrichWithLvars(simData);
            }

            DataReceived?.Invoke(simData);
        }

        private void OnRecvClientData(
            Microsoft.FlightSimulator.SimConnect.SimConnect sender,
            SIMCONNECT_RECV_CLIENT_DATA data)
        {
            try { _mobiFlight?.ProcessClientData(data); }
            catch (Exception ex) { Debug.WriteLine($"OnRecvClientData error: {ex.Message}"); }
        }

        private void OnRecvOpen(
            Microsoft.FlightSimulator.SimConnect.SimConnect sender,
            SIMCONNECT_RECV_OPEN data)
        {
            DetectedSimulator = SimulatorType.MSFS2020;
            RequestData();
        }

        // ──────────────────────────────────────────────────────────────────────
        // BUILD SIM DATA
        // ──────────────────────────────────────────────────────────────────────

        private static SimData BuildSimData(AircraftDataStruct r, EnvironmentDataStruct e, int lightStates)
        {
            // Fuel con fallback
            double fuelTotal = r.FuelTotalLbs;
            if (fuelTotal <= 0.1) fuelTotal = r.FuelLeftQuantity + r.FuelRightQuantity + r.FuelCenterQuantity;
            if (fuelTotal <= 0.1) fuelTotal = r.FuelTotalCapacity;

            // N1 con fallback
            double n1Eng1 = r.Engine1N1 > 0.1 ? r.Engine1N1 : r.TurbEngN1_1;
            double n1Eng2 = r.Engine2N1 > 0.1 ? r.Engine2N1 : r.TurbEngN1_2;

            // Squawk
            int squawk = DecodeBco16(r.TransponderCode);
            if (squawk < 0 || squawk > 9999) squawk = 0;

            // ── LUCES desde LIGHT ON STATES bitmask ──
            // Bit 0: Nav | Bit 1: Beacon | Bit 2: Landing | Bit 3: Taxi | Bit 4: Strobe
            bool navOn     = (lightStates & 0x001) != 0;
            bool beaconOn  = (lightStates & 0x002) != 0;
            bool landingOn = (lightStates & 0x004) != 0;
            bool taxiOn    = (lightStates & 0x008) != 0;
            bool strobeOn  = (lightStates & 0x010) != 0;

            Debug.WriteLine($"[SimConnect] Fuel={fuelTotal:F0} N1={n1Eng1:F1}/{n1Eng2:F1} Squawk={squawk} " +
                $"Lights: Nav={navOn} Beacon={beaconOn} Landing={landingOn} Taxi={taxiOn} Strobe={strobeOn}");

            return new SimData
            {
                AircraftTitle      = r.Title ?? "Unknown",
                Latitude           = r.Latitude,
                Longitude          = r.Longitude,
                AltitudeFeet       = r.AltitudeFeet,
                AltitudeAGL        = r.AltitudeAGL,
                IndicatedAirspeed  = r.IndicatedAirspeed,
                GroundSpeed        = r.GroundSpeed,
                VerticalSpeed      = r.OnGround != 0 ? 0.0 : r.VerticalSpeed,
                Heading            = r.Heading,
                Pitch              = r.Pitch,
                Bank               = r.Bank,

                FuelTotalLbs       = fuelTotal,
                FuelFlowLbsHour    = Math.Max(0, r.Engine1FuelFlowPph) + Math.Max(0, r.Engine2FuelFlowPph),
                Engine1N1          = n1Eng1,
                Engine2N1          = n1Eng2,
                FuelLeftTankLbs    = r.FuelLeftQuantity,
                FuelRightTankLbs   = r.FuelRightQuantity,
                FuelCenterTankLbs  = r.FuelCenterQuantity,
                FuelTotalCapacityLbs = r.FuelTotalCapacity,

                LandingVS          = r.VerticalSpeed,
                LandingG           = 0,

                OnGround           = r.OnGround != 0,
                ParkingBrake       = r.ParkingBrake != 0,
                AutopilotActive    = r.AutopilotActive != 0,

                // ── Luces desde LIGHT ON STATES ──
                NavLightsOn        = navOn,
                BeaconLightsOn     = beaconOn,
                LandingLightsOn    = landingOn,
                TaxiLightsOn       = taxiOn,
                StrobeLightsOn     = strobeOn,

                SeatBeltSign       = r.SeatBeltSign != 0,
                NoSmokingSign      = r.NoSmokingSign != 0,

                GearDown           = r.GearHandleDown != 0,
                GearTransitioning  = false,
                FlapsDeployed      = r.FlapsPercent > 0.01,
                FlapsPercent       = r.FlapsPercent,
                SpoilersArmed      = r.SpoilersHandlePercent > 0.01,
                ReverserActive     = false,

                TransponderCode         = squawk,
                // TRANSPONDER STATE: 0=Off, 1=Standby, 2=Test, 3=On(Mode A), 4=Alt(Mode C), 5=Ground/Mode S
                // "Charlie mode" = altitud reportada = estado 4 o superior
                TransponderCharlieMode  = r.TransponderState >= 4,

                ApuAvailable       = r.ApuPct > 1,
                ApuRunning         = r.ApuPct > 85,

                BleedAirOn         = r.BleedAirOn != 0,
                CabinAltitudeFeet  = (r.CabinAltitudeFeet >= 0 && r.CabinAltitudeFeet < 50000) ? r.CabinAltitudeFeet : 0,
                PressureDiffPsi    = (r.PressureDiffPsi > 0 && r.PressureDiffPsi < 20) ? r.PressureDiffPsi : 0,

                OutsideTemperature = e.OutsideTemperature,
                WindSpeed          = e.WindSpeed,
                WindDirection      = e.WindDirection,
                QNH                = e.SeaLevelPressure,
                IsRaining          = e.PrecipState > 0
            };
        }

        // ──────────────────────────────────────────────────────────────────────
        // EVENTS
        // ──────────────────────────────────────────────────────────────────────

        private void OnRecvEvent(
            Microsoft.FlightSimulator.SimConnect.SimConnect sender,
            SIMCONNECT_RECV_EVENT data)
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

        private void OnRecvQuit(
            Microsoft.FlightSimulator.SimConnect.SimConnect sender,
            SIMCONNECT_RECV data)
        {
            IsConnected = false;
            DetectedSimulator = SimulatorType.None;
            Disconnected?.Invoke();
        }

        private void OnRecvException(
            Microsoft.FlightSimulator.SimConnect.SimConnect sender,
            SIMCONNECT_RECV_EXCEPTION data)
        {
            Debug.WriteLine($"SimConnect exception: {data.dwException}");
        }

        // ──────────────────────────────────────────────────────────────────────
        // MESSAGE PUMP
        // ──────────────────────────────────────────────────────────────────────

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_USER_SIMCONNECT)
            {
                try { _simConnect?.ReceiveMessage(); }
                catch (Exception ex) { Debug.WriteLine($"ReceiveMessage error: {ex}"); }
                handled = true;
            }
            return IntPtr.Zero;
        }

        // ──────────────────────────────────────────────────────────────────────
        // DISCONNECT / DISPOSE
        // ──────────────────────────────────────────────────────────────────────

        public void Disconnect()
        {
            var wasConnected = IsConnected;

            if (_simConnect != null)
            {
                try { _simConnect.Dispose(); } catch { }
                _simConnect = null;
            }

            IsConnected = false;
            DetectedSimulator = SimulatorType.None;

            if (wasConnected) Disconnected?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Disconnect();
            _hwndSource?.RemoveHook(WndProc);
            _disposed = true;
        }

        // ──────────────────────────────────────────────────────────────────────
        // AIRCRAFT PROFILE DETECTION
        // ──────────────────────────────────────────────────────────────────────

        private static string DetectProfileName(string aircraftTitle)
        {
            try
            {
                var exeDir   = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                var jsonPath = System.IO.Path.Combine(exeDir, "AircraftProfiles.json");
                if (!System.IO.File.Exists(jsonPath))
                {
                    Debug.WriteLine($"[Profile] AircraftProfiles.json no encontrado en {jsonPath}");
                    return "MSFS Native";
                }

                var json  = System.IO.File.ReadAllText(jsonPath);
                var title = aircraftTitle.ToUpperInvariant();

                // 1. exact_titles
                int exactIdx = json.IndexOf("\"exact_titles\"", StringComparison.OrdinalIgnoreCase);
                while (exactIdx >= 0)
                {
                    int start = json.IndexOf('[', exactIdx);
                    int end   = json.IndexOf(']', start);
                    if (start < 0 || end < 0) break;
                    var block   = json.Substring(start, end - start + 1);
                    int nameIdx = json.LastIndexOf("\"name\"", exactIdx);
                    if (nameIdx >= 0)
                    {
                        int q1 = json.IndexOf('"', nameIdx + 7);
                        int q2 = json.IndexOf('"', q1 + 1);
                        var profileName = json.Substring(q1 + 1, q2 - q1 - 1);
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

                // 2. matches
                int matchesIdx = json.IndexOf("\"matches\"", StringComparison.OrdinalIgnoreCase);
                while (matchesIdx >= 0)
                {
                    int start = json.IndexOf('[', matchesIdx);
                    int end   = json.IndexOf(']', start);
                    if (start < 0 || end < 0) break;
                    var block   = json.Substring(start, end - start + 1);
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

        private static bool ProfileRequiresLvars(string profileName) =>
            profileName == "A319 Headwind"
            || profileName == "Fenix A320"
            || profileName == "Headwind A320"
            || profileName == "FlyByWire A32NX";

        // ──────────────────────────────────────────────────────────────────────
        // BCO16 DECODE (transponder)
        // ──────────────────────────────────────────────────────────────────────

        private static int DecodeBco16(int bcoValue)
        {
            if (bcoValue < 0) return 0;
            int result = 0, multiplier = 1, temp = bcoValue;
            while (temp > 0)
            {
                result   += (temp & 0x7) * multiplier;
                multiplier *= 10;
                temp       >>= 3;
            }
            return result;
        }

        // ──────────────────────────────────────────────────────────────────────
        // LVAR INTEGRATION (MobiFlight — solo A32NX / Fenix)
        // ──────────────────────────────────────────────────────────────────────

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
                _simConnect  = simConnect;
                _isAvailable = true;
                Debug.WriteLine("[MobiFlight] LVAR integration lista");
            }

            private static readonly (string Var, string Key)[] A32NX_LVARS =
            {
                ("(L:A32NX_ELEC_AC_ESS_BUS_IS_POWERED, bool)", "Beacon"),
                ("(L:A32NX_ELEC_AC_1_BUS_IS_POWERED, bool)",   "BusPowered"),
                ("LIGHT BEACON",                                 "BeaconNative"),
                ("LIGHT STROBE",                                 "StrobeNative"),
                ("LIGHT LANDING",                                "LandingNative"),
                ("LIGHT NAV",                                    "NavNative"),
                ("LIGHT TAXI",                                   "TaxiNative"),
                ("(L:A32NX_ENGINE_N1:1, percent)",               "N1_1"),
                ("(L:A32NX_ENGINE_N1:2, percent)",               "N1_2"),
            };

            private static readonly (string Var, string Key)[] FENIX_LVARS =
            {
                ("LIGHT BEACON",  "BeaconNative"),
                ("LIGHT STROBE",  "StrobeNative"),
                ("LIGHT LANDING", "LandingNative"),
                ("LIGHT NAV",     "NavNative"),
                ("LIGHT TAXI",    "TaxiNative"),
                ("TURB ENG N1:1", "N1_1"),
                ("TURB ENG N1:2", "N1_2"),
            };

            public void RequestLvarsForProfile(string profileName)
            {
                if (!_isAvailable || _simConnect == null || _registeredProfile == profileName) return;
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
                    Debug.WriteLine($"[MobiFlight] LVARs registradas para '{profileName}'");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MobiFlight] Error: {ex.Message}");
                }
            }

            [System.Runtime.InteropServices.StructLayout(
                System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
            private struct LvarBlock9
            {
                public double V0, V1, V2, V3, V4, V5, V6, V7, V8;
            }

            public void ProcessSimObjectData(SIMCONNECT_RECV_SIMOBJECT_DATA data)
            {
                if (data.dwRequestID != (uint)LvarReqId.Block) return;
                try
                {
                    var block = (LvarBlock9)data.dwData[0];
                    double[] vals = { block.V0, block.V1, block.V2, block.V3, block.V4, block.V5, block.V6, block.V7, block.V8 };
                    var lvars = _registeredProfile == "Fenix A320" ? FENIX_LVARS : A32NX_LVARS;
                    for (int i = 0; i < lvars.Length && i < vals.Length; i++)
                        _lvarCache[lvars[i].Key] = vals[i];
                }
                catch (Exception ex) { Debug.WriteLine($"[MobiFlight] Error: {ex.Message}"); }
            }

            public void ProcessClientData(SIMCONNECT_RECV_CLIENT_DATA data) { }

            public SimData EnrichWithLvars(SimData baseData)
            {
                if (!_isAvailable || _lvarCache.Count == 0) return baseData;
                if (_lvarCache.TryGetValue("BeaconNative",  out var b)) baseData.BeaconLightsOn  = b > 0.5;
                if (_lvarCache.TryGetValue("StrobeNative",  out var s)) baseData.StrobeLightsOn  = s > 0.5;
                if (_lvarCache.TryGetValue("LandingNative", out var l)) baseData.LandingLightsOn = l > 0.5;
                if (_lvarCache.TryGetValue("NavNative",     out var n)) baseData.NavLightsOn      = n > 0.5;
                if (_lvarCache.TryGetValue("TaxiNative",    out var t)) baseData.TaxiLightsOn    = t > 0.5;
                if (_lvarCache.TryGetValue("N1_1",          out var n1)) baseData.Engine1N1      = n1;
                if (_lvarCache.TryGetValue("N1_2",          out var n2)) baseData.Engine2N1      = n2;
                return baseData;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _simConnect = null;
                _disposed   = true;
            }
        }
    }
}
