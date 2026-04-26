#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.FlightSimulator.SimConnect;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Core.Services;

namespace PatagoniaWings.Acars.SimConnect
{
    /// <summary>
    /// Integración MSFS 2020/2024 via SimConnect.
    ///
    /// Arquitectura SUR Air: todos los campos booleanos se registran como FLOAT64 (no INT32).
    /// Las luces se leen como simvars individuales (LIGHT NAV/TAXI/LANDING/STROBE/BEACON)
    /// directamente como FLOAT64 — más confiable que LIGHT ON STATES bitmask para addons MSFS.
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

        // Última telemetría base para poder re-emitir cuando cambie el entorno
        private AircraftDataStruct _lastAircraftRaw;
                private AircraftProfile _lastProfile = new AircraftProfile();

        private MobiFlightIntegration? _mobiFlight;
        private PmdgNg3SdkBridge? _pmdgSdk;

        public bool IsConnected { get; private set; }
        public SimulatorType DetectedSimulator { get; private set; } = SimulatorType.None;
        public bool IsMobiFlightAvailable => _mobiFlight != null && _mobiFlight.IsAvailable;
        public bool IsPmdgSdkAvailable => _pmdgSdk != null && _pmdgSdk.IsAvailable;
        public bool IsLvarOverlayActive =>
            (IsMobiFlightAvailable && ProfileRequiresLvars(_lastProfile))
            || (IsPmdgSdkAvailable && ProfileRequiresPmdgSdk(_lastProfile != null ? _lastProfile.Code : string.Empty));

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

                // PMDG 737 SDK oficial
                try
                {
                    Pmdg737OptionsConfigurator.TryEnsureSdkEnabled();
                    _pmdgSdk = new PmdgNg3SdkBridge();
                    _pmdgSdk.Initialize(_simConnect);
                    Debug.WriteLine(_pmdgSdk.IsAvailable
                        ? "[SimConnect] ✓ PMDG NG3 SDK bridge listo"
                        : "[SimConnect] ✗ PMDG NG3 SDK bridge no disponible");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SimConnect] PMDG SDK init error: {ex.Message}");
                    _pmdgSdk = null;
                }

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

            // AIRCRAFT DATA
            // El orden debe coincidir EXACTAMENTE con AircraftDataStruct.
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TITLE", null, SIMCONNECT_DATATYPE.STRING256, 0, sc);

            // Bloque base SUR-compatible
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "INDICATED ALTITUDE CALIBRATED", "feet",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GROUND VELOCITY",            "knots",            SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "VERTICAL SPEED",             "feet per minute",  SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE LATITUDE",             "degrees",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE LONGITUDE",            "degrees",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE HEADING DEGREES TRUE", "degrees",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "SIM ON GROUND",              "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "BRAKE PARKING POSITION",     "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT NAV",                  "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT TAXI",                 "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT LANDING",              "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT STROBE",               "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "LIGHT BEACON",               "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "ELECTRICAL MASTER BATTERY",  "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AVIONICS MASTER SWITCH",     "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "CABIN NO SMOKING ALERT SWITCH", "Bool",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "APU PCT RPM",                "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GENERAL ENG COMBUSTION:1",   "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GENERAL ENG COMBUSTION:2",   "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GEAR HANDLE POSITION",       "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL TOTAL QUANTITY WEIGHT", "pounds",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TOTAL WEIGHT",               "pounds",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "EMPTY WEIGHT",               "pounds",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "EXIT OPEN:0",                "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            // Bloque extendido PWG
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE ALT ABOVE GROUND",     "feet",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AIRSPEED INDICATED",         "knots",            SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE PITCH DEGREES",        "degrees",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE BANK DEGREES",         "degrees",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL TOTAL CAPACITY",        "pounds",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL LEFT QUANTITY",         "pounds",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL RIGHT QUANTITY",        "pounds",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL CENTER QUANTITY",       "pounds",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GENERAL ENG FUEL FLOW PPH:1","pounds per hour",  SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GENERAL ENG FUEL FLOW PPH:2","pounds per hour",  SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TURB ENG N1:1",              "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TURB ENG N1:2",              "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AUTOPILOT MASTER",           "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FLAPS HANDLE PERCENT",       "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "SPOILERS HANDLE POSITION",   "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TRANSPONDER STATE:1",        "number",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TRANSPONDER CODE:1",         "bco16",            SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PRESSURIZATION CABIN ALTITUDE", "feet",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PRESSURIZATION PRESSURE DIFFERENTIAL", "psi",   SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "CABIN SEATBELTS ALERT SWITCH", "Bool",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "BLEED AIR ENGINE:1",         "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            // Bloque extendido AP/XPDR al final del struct — orden EXACTO con la struct (campos 47-53)
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AUTOPILOT HEADING LOCK",    "Bool",   SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 47
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AUTOPILOT ALTITUDE LOCK",   "Bool",   SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 48
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AUTOPILOT NAV1 LOCK",       "Bool",   SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 49
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AUTOPILOT APPROACH HOLD",   "Bool",   SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 50
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AUTOPILOT WING LEVELER",    "Bool",   SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 51
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AUTOPILOT DISENGAGED",      "Bool",   SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 52
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TRANSPONDER AVAILABLE",     "Bool",   SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 53
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "COM ACTIVE FREQUENCY:2",   "MHz",    SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 54

            _simConnect.RegisterDataDefineStruct<AircraftDataStruct>(DataDefineId.AircraftData);

            // Environment
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT TEMPERATURE",      "celsius",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT WIND VELOCITY",    "knots",            SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT WIND DIRECTION",   "degrees",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "SEA LEVEL PRESSURE",       "millibars",        SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.EnvironmentData, "AMBIENT PRECIP STATE",     "number",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.RegisterDataDefineStruct<EnvironmentDataStruct>(DataDefineId.EnvironmentData);
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
            Debug.WriteLine($"[SimConnect LIGHTS] Nav={raw.LightNav != 0} Beacon={raw.LightBeacon != 0} Landing={raw.LightLanding != 0} Taxi={raw.LightTaxi != 0} Strobe={raw.LightStrobe != 0}");

            _lastAircraftRaw = raw;
            
            var exeDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            var profile = AircraftProfileCatalog.Resolve(exeDir, aircraftTitle);
            profile = NormalizeAddonProfile(profile, aircraftTitle);
            _lastProfile = profile;

            var simData = BuildSimData(raw, _lastEnv, profile);
            simData.IsConnected    = true;
            simData.SimulatorType  = DetectedSimulator == SimulatorType.None ? SimulatorType.MSFS2020 : DetectedSimulator;
            simData.Pause          = _isPaused;

            string profileCode = profile?.Code ?? "MSFS_NATIVE";
            string profileDisplayName = profile?.DisplayName ?? "MSFS Native";
            simData.AircraftProfile = profileDisplayName;

            if (_pmdgSdk?.IsAvailable == true && ProfileRequiresPmdgSdk(profileCode))
            {
                _pmdgSdk.RequestSdkData();
                simData = _pmdgSdk.EnrichWithSdk(simData);
            }
            else if (profile != null && _mobiFlight?.IsAvailable == true && ProfileRequiresLvars(profile))
            {
                _mobiFlight.RequestLvarsForProfile(profile);
                simData = _mobiFlight.EnrichWithLvars(simData, profile);
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

        private void OnRecvClientData(
            Microsoft.FlightSimulator.SimConnect.SimConnect sender,
            SIMCONNECT_RECV_CLIENT_DATA data)
        {
            try
            {
                _pmdgSdk?.ProcessClientData(data);
                _mobiFlight?.ProcessClientData(data);
            }
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

        private static SimData BuildSimData(
            AircraftDataStruct r,
            EnvironmentDataStruct e,
            AircraftProfile? profile)
        {
            // Fuel con fallback
            double fuelTotal = r.FuelTotalLbs;
            if (fuelTotal <= 0.1) fuelTotal = r.FuelLeftQuantity + r.FuelRightQuantity + r.FuelCenterQuantity;
            if (fuelTotal <= 0.1) fuelTotal = r.FuelTotalCapacity;

            // N1 con fallback
            double n1Eng1 = r.TurbEngN1_1 > 0.1 ? r.TurbEngN1_1 : (r.EngineOneCombustion != 0 ? 20.0 : 0.0);
            double n1Eng2 = r.TurbEngN1_2 > 0.1 ? r.TurbEngN1_2 : (r.EngineTwoCombustion != 0 ? 20.0 : 0.0);

            // Squawk: algunos aviones lo entregan ya en decimal, otros en BCO16
            int rawTransponderCode = (int)r.TransponderCode;
            int squawk = NormalizeTransponderCode(rawTransponderCode);

            // ── LUCES — lectura directa desde simvars individuales FLOAT64 ─────
            // Arquitectura SUR Air: cada luz es un FLOAT64 individual, no bitmask.
            // Un valor != 0.0 significa ON. Es el método más confiable en MSFS 2020/2024.
            bool navOn     = r.LightNav     != 0;
            bool beaconOn  = r.LightBeacon  != 0;
            bool landingOn = r.LightLanding != 0;
            bool taxiOn    = r.LightTaxi    != 0;
            bool strobeOn  = r.LightStrobe  != 0;

            // ── Pesos / combustible en kg ─────────────────────────────────────
            double fuelKg          = fuelTotal / 2.20462;
            double totalWeightLbs  = Math.Max(0, r.TotalWeight);
            double totalWeightKg   = totalWeightLbs / 2.20462;
            double emptyWeightLbs  = Math.Max(0, r.EmptyWeight);
            double emptyWeightKg   = emptyWeightLbs / 2.20462;
            double zeroFuelWeightKg = totalWeightKg > 0
                ? Math.Max(0, totalWeightKg - fuelKg)
                : 0;
            double payloadKg = zeroFuelWeightKg > 0
                ? Math.Max(0, zeroFuelWeightKg - emptyWeightKg)
                : 0;

            // ── Perfil de aeronave normalizado ────────────────────────────────
            var profileCode = profile?.Code ?? AircraftNormalizationService.ResolveCode(r.Title ?? string.Empty);

            double indicatedAirspeed = r.IndicatedAirspeed;
            double groundSpeed = r.GroundSpeed;
            if (ProfileIsMaddog(profileCode) && r.OnGround != 0)
            {
                if (indicatedAirspeed < 10.0) indicatedAirspeed = 0.0;
                if (groundSpeed < 3.0) groundSpeed = 0.0;
            }

            Debug.WriteLine($"[SimConnect] Fuel={fuelTotal:F0} lbs / {fuelKg:F0} kg  N1={n1Eng1:F1}/{n1Eng2:F1} Squawk={squawk} " +
                $"Profile={profile?.DisplayName ?? "MSFS Native"} Code={profileCode} " +
                $"Lights: Nav={navOn} Beacon={beaconOn} Landing={landingOn} Taxi={taxiOn} Strobe={strobeOn} " +
                $"Eng1={r.EngineOneCombustion != 0} Batt={r.BatteryMaster != 0} Avionics={r.AvionicsMaster != 0} Door={r.DoorPercent:F0}%");

            if (ProfileIsMaddog(profileCode))
            {
                Debug.WriteLine($"[MADDOG RAW] IAS={indicatedAirspeed:F1} GS={groundSpeed:F1} AP={Convert.ToInt32(Math.Round(r.AutopilotActive))} XPDR={Convert.ToInt32(Math.Round(r.TransponderState))} Seatbelt={Convert.ToInt32(Math.Round(r.SeatBeltSign))} NoSmoking={Convert.ToInt32(Math.Round(r.NoSmokingSign))} APU={r.ApuPct:F1} Bleed={Convert.ToInt32(Math.Round(r.BleedAirOn))} DoorPct={r.DoorPercent:F1}");
            }

            var simconnectXpdrRaw = NormalizeTransponderState(r.TransponderAvailable != 0, Convert.ToInt32(Math.Round(r.TransponderState)));

            return new SimData
            {
                AircraftTitle      = r.Title ?? "Unknown",
                Latitude           = r.Latitude,
                Longitude          = r.Longitude,
                AltitudeFeet       = r.AltitudeFeet,
                AltitudeAGL        = r.AltitudeAGL,
                IndicatedAirspeed  = indicatedAirspeed,
                GroundSpeed        = groundSpeed,
                // Clamp VS a 0 en tierra: SIM ON GROUND puede oscilar brevemente
                // devolviendo ±1-5 fpm incluso estacionado (ruido del sim)
                VerticalSpeed      = (r.OnGround != 0 || Math.Abs(r.VerticalSpeed) < 10) && r.OnGround != 0
                                     ? 0.0 : r.VerticalSpeed,
                Heading            = r.Heading,
                Pitch              = r.Pitch,
                Bank               = r.Bank,

                FuelTotalLbs       = fuelTotal,          // en libras (nativo SimConnect)
                FuelKg             = fuelKg,             // convertido a kg
                FuelFlowLbsHour    = Math.Max(0, r.Engine1FuelFlowPph) + Math.Max(0, r.Engine2FuelFlowPph),
                Engine1N1          = n1Eng1,
                Engine2N1          = n1Eng2,
                FuelLeftTankLbs    = r.FuelLeftQuantity,
                FuelRightTankLbs   = r.FuelRightQuantity,
                FuelCenterTankLbs  = r.FuelCenterQuantity,
                FuelTotalCapacityLbs = r.FuelTotalCapacity,
                TotalWeightLbs     = totalWeightLbs,
                TotalWeightKg      = totalWeightKg,
                ZeroFuelWeightKg   = zeroFuelWeightKg,
                PayloadKg          = payloadKg,
                EmptyWeightLbs     = emptyWeightLbs,
                EmptyWeightKg      = emptyWeightKg,

                LandingVS          = r.VerticalSpeed,
                LandingG           = 0,

                OnGround           = r.OnGround != 0,
                ParkingBrake       = r.ParkingBrake != 0,
                AutopilotActive    = r.AutopilotActive != 0 && r.AutopilotDisengaged == 0,

                // ── Luces desde simvars individuales FLOAT64 (SUR Air) ──
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
                TransponderStateRaw     = simconnectXpdrRaw,
                TransponderCharlieMode  = simconnectXpdrRaw >= 3,

                ApuAvailable       = r.ApuPct > 1,
                ApuRunning         = r.ApuPct > 85,

                BleedAirOn         = r.BleedAirOn != 0,
                CabinAltitudeFeet  = (r.CabinAltitudeFeet >= 0 && r.CabinAltitudeFeet < 50000) ? r.CabinAltitudeFeet : 0,
                PressureDiffPsi    = (r.PressureDiffPsi > 0 && r.PressureDiffPsi < 20) ? r.PressureDiffPsi : 0,

                OutsideTemperature = e.OutsideTemperature,
                WindSpeed          = e.WindSpeed,
                WindDirection      = e.WindDirection,
                QNH                = e.SeaLevelPressure,
                IsRaining          = e.PrecipState > 0,

                // ── Campos extendidos (arquitectura SUR Air) ──
                EngineOneRunning   = r.EngineOneCombustion != 0,
                EngineTwoRunning   = r.EngineTwoCombustion != 0,
                BatteryMasterOn    = r.BatteryMaster       != 0,
                AvionicsMasterOn   = r.AvionicsMaster      != 0,
                DoorOpen           = r.DoorPercent > 5.0,   // umbral 5% (igual que SUR Air)
                DetectedProfileCode = profileCode,
                Com2FrequencyMhz   = r.Com2FrequencyMhz > 100 ? r.Com2FrequencyMhz : 0,
            };
        }

        private static int NormalizeTransponderCode(int rawCode)
        {
            if (LooksLikeOctalCode(rawCode))
            {
                return rawCode;
            }

            int decoded = DecodeBco16(rawCode);
            if (LooksLikeOctalCode(decoded))
            {
                return decoded;
            }

            return 0;
        }

        private static bool LooksLikeOctalCode(int value)
        {
            if (value < 0 || value > 7777) return false;
            int temp = value;
            do
            {
                int digit = temp % 10;
                if (digit > 7) return false;
                temp /= 10;
            } while (temp > 0);

            return true;
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

            _pmdgSdk?.Dispose();
            _pmdgSdk = null;
            _mobiFlight?.Dispose();
            _mobiFlight = null;

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
            _disposed = true;

            // Remover el hook ANTES de disponer SimConnect para evitar
            // que mensajes tardíos de SimConnect lleguen a una ventana destruida,
            // lo que colgaba MSFS al cerrar y volver a abrir el ACARS.
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;

            Disconnect();
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


        private static AircraftProfile NormalizeAddonProfile(AircraftProfile profile, string aircraftTitle)
        {
            var t = (aircraftTitle ?? string.Empty).Trim().ToUpperInvariant();
            if (t.Length == 0) return profile;

            bool looksLikeLvfrAirbus =
                !t.Contains("FENIX") && !t.Contains("FLYBYWIRE") && !t.Contains("A32NX")
                && !t.Contains("HEADWIND") && !t.Contains("INIBUILDS")
                && (
                    t.Contains("LATINVFR") || t.Contains("LVFR")
                    || (t.Contains("AIRBUS A319") && (t.Contains("CFM") || t.Contains("IAE")))
                    || (t.Contains("AIRBUS A320") && (t.Contains("CFM") || t.Contains("IAE")))
                    || (t.Contains("AIRBUS A321") && (t.Contains("CFM") || t.Contains("IAE")))
                );

            if (!looksLikeLvfrAirbus) return profile;
            if (profile != null && profile.Code != "MSFS_NATIVE" && profile.Code != "A319_FENIX" && profile.Code != "A320_FENIX" && profile.Code != "A321_FENIX")
                return profile;

            string code = t.Contains("A321") ? "A321_LVFR"
                        : t.Contains("A320") ? "A320_LVFR"
                        : "A319_LVFR";

            string display = code == "A321_LVFR" ? "Airbus A321 LVFR"
                           : code == "A320_LVFR" ? "Airbus A320 LVFR"
                           : "Airbus A319 LVFR";

            return new AircraftProfile
            {
                Code = code,
                DisplayName = display,
                FamilyGroup = "AIRBUS_NB",
                Simulator = "MSFS2020",
                AddonProvider = "LVFR",
                EngineCount = 2,
                IsPressurized = true,
                HasApu = true,
                ImageAsset = code.StartsWith("A321") ? "A321.png" : code.StartsWith("A320") ? "A320.png" : "A319.png",
                Supported = true
            };
        }

        private static bool ProfileRequiresLvars(AircraftProfile? profile)
        {
            if (profile == null) return false;
            if (profile.RequiresLvars) return true;
            return ProfileHasBridgeExpressions(profile);
        }

        private static bool ProfileRequiresPmdgSdk(string profileCode) =>
            profileCode == "B736_PMDG"
            || profileCode == "B737_PMDG"
            || profileCode == "B738_PMDG"
            || profileCode == "B739_PMDG";

        private static bool ProfileIsMaddog(string profileCode) =>
            profileCode == "MD82_MADDOG"
            || profileCode == "MD83_MADDOG"
            || profileCode == "MD88_MADDOG";

        private static bool ProfileHasBridgeExpressions(AircraftProfile profile)
        {
            if (profile == null) return false;

            // Reservado para una futura implementación real del bridge Maddog.
            // Hoy no se debe usar para activar overlay porque todavía no existe
            // una lectura viva de esos sistemas.
            return false;
        }

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
        // PMDG 737 NG3 SDK (Client Data oficial)
        // ──────────────────────────────────────────────────────────────────────

        private sealed class PmdgNg3SdkBridge : IDisposable
        {
            private const string PMDG_NG3_DATA_NAME = "PMDG_NG3_Data";
            private const string PMDG_NG3_CONTROL_NAME = "PMDG_NG3_Control";

            private Microsoft.FlightSimulator.SimConnect.SimConnect? _simConnect;
            private bool _isSubscribed;
            private bool _hasData;
            private PmdgNg3SelectedData _latestData;
            private PmdgNg3ControlArea _controlArea;

            private enum ClientDataId : uint
            {
                Data = 0x4E473331,
                Control = 0x4E473333
            }

            private enum ClientDataDefinitionId : uint
            {
                Data = 0x4E473332,
                Control = 0x4E473334
            }

            private enum ClientRequestId : uint
            {
                Data = 920,
                Control = 921
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private struct PmdgNg3SelectedData
            {
                public byte NoSmokingSelector;
                public byte FastenBeltsSelector;

                public byte BleedAirLeft;
                public byte BleedAirRight;
                public byte ApuBleedAir;

                public byte DoorFwdEntry;
                public byte DoorFwdService;
                public byte DoorAirstair;
                public byte DoorLeftFwdOverwing;
                public byte DoorRightFwdOverwing;
                public byte DoorFwdCargo;
                public byte DoorEquip;
                public byte DoorLeftAftOverwing;
                public byte DoorRightAftOverwing;
                public byte DoorAftCargo;
                public byte DoorAftEntry;
                public byte DoorAftService;

                public byte TaxiLight;
                public byte ApuSelector;
                public byte LogoLight;
                public byte PositionLight;
                public byte AntiCollision;

                public byte McpFdLeft;
                public byte McpFdRight;
                public byte McpAtArm;
                public byte McpCmdA;
                public byte McpCwsA;
                public byte McpCmdB;
                public byte McpCwsB;
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private struct PmdgNg3ControlArea
            {
                public uint Event;
                public uint Parameter;
            }

            public bool IsAvailable => _simConnect != null;
            public bool HasData => _hasData;

            public void Initialize(Microsoft.FlightSimulator.SimConnect.SimConnect simConnect)
            {
                _simConnect = simConnect;
                _hasData = false;
                _isSubscribed = false;
                _controlArea.Event = 0;
                _controlArea.Parameter = 0;

                MapClientData();
            }

            private void MapClientData()
            {
                if (_simConnect == null) return;

                try
                {
                    uint sc = Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED;

                    _simConnect.MapClientDataNameToID(PMDG_NG3_DATA_NAME, ClientDataId.Data);
                    _simConnect.MapClientDataNameToID(PMDG_NG3_CONTROL_NAME, ClientDataId.Control);

                    // Definición compacta solo con offsets que Patagonia Wings necesita.
                    // Offsets calculados desde PMDG_NG3_SDK.h
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 218, 1, 0, sc); // COMM_NoSmokingSelector
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 219, 1, 0, sc); // COMM_FastenBeltsSelector

                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 282, 1, 0, sc); // AIR_BleedAirSwitch[0]
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 283, 1, 0, sc); // AIR_BleedAirSwitch[1]
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 284, 1, 0, sc); // AIR_APUBleedAirSwitch

                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 344, 1, 0, sc); // DOOR_annunFWD_ENTRY
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 345, 1, 0, sc); // DOOR_annunFWD_SERVICE
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 346, 1, 0, sc); // DOOR_annunAIRSTAIR
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 347, 1, 0, sc); // DOOR_annunLEFT_FWD_OVERWING
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 348, 1, 0, sc); // DOOR_annunRIGHT_FWD_OVERWING
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 349, 1, 0, sc); // DOOR_annunFWD_CARGO
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 350, 1, 0, sc); // DOOR_annunEQUIP
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 351, 1, 0, sc); // DOOR_annunLEFT_AFT_OVERWING
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 352, 1, 0, sc); // DOOR_annunRIGHT_AFT_OVERWING
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 353, 1, 0, sc); // DOOR_annunAFT_CARGO
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 354, 1, 0, sc); // DOOR_annunAFT_ENTRY
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 355, 1, 0, sc); // DOOR_annunAFT_SERVICE

                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 378, 1, 0, sc); // LTS_TaxiSw
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 379, 1, 0, sc); // APU_Selector
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 383, 1, 0, sc); // LTS_LogoSw
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 384, 1, 0, sc); // LTS_PositionSw
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 385, 1, 0, sc); // LTS_AntiCollisionSw

                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 435, 1, 0, sc); // MCP_FDSw[0]
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 436, 1, 0, sc); // MCP_FDSw[1]
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 437, 1, 0, sc); // MCP_ATArmSw
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 453, 1, 0, sc); // MCP_annunCMD_A
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 454, 1, 0, sc); // MCP_annunCWS_A
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 455, 1, 0, sc); // MCP_annunCMD_B
                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Data, 456, 1, 0, sc); // MCP_annunCWS_B

                    _simConnect.RegisterStruct<Microsoft.FlightSimulator.SimConnect.SIMCONNECT_RECV_CLIENT_DATA, PmdgNg3SelectedData>(ClientDataDefinitionId.Data);

                    _simConnect.AddToClientDataDefinition(ClientDataDefinitionId.Control, 0, (uint)Marshal.SizeOf(typeof(PmdgNg3ControlArea)), 0, sc);
                    _simConnect.RegisterStruct<Microsoft.FlightSimulator.SimConnect.SIMCONNECT_RECV_CLIENT_DATA, PmdgNg3ControlArea>(ClientDataDefinitionId.Control);

                    Debug.WriteLine("[PMDG SDK] ClientData mapeado");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[PMDG SDK] Error mapeando ClientData: " + ex.Message);
                }
            }

            public void RequestSdkData()
            {
                if (_simConnect == null || _isSubscribed) return;

                try
                {
                    _simConnect.RequestClientData(
                        ClientDataId.Data,
                        ClientRequestId.Data,
                        ClientDataDefinitionId.Data,
                        SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET,
                        SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.CHANGED,
                        0, 0, 0);

                    _simConnect.RequestClientData(
                        ClientDataId.Control,
                        ClientRequestId.Control,
                        ClientDataDefinitionId.Control,
                        SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET,
                        SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.CHANGED,
                        0, 0, 0);

                    _isSubscribed = true;
                    Debug.WriteLine("[PMDG SDK] Suscripción ON_SET activa");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[PMDG SDK] Error solicitando datos: " + ex.Message);
                }
            }

            public void ProcessClientData(SIMCONNECT_RECV_CLIENT_DATA data)
            {
                try
                {
                    if (data.dwRequestID == (uint)ClientRequestId.Data)
                    {
                        _latestData = (PmdgNg3SelectedData)data.dwData[0];
                        _hasData = true;

                        Debug.WriteLine(
                            "[PMDG SDK RAW] SeatbeltSel=" + _latestData.FastenBeltsSelector +
                            " NoSmokingSel=" + _latestData.NoSmokingSelector +
                            " ApuSel=" + _latestData.ApuSelector +
                            " BleedL=" + _latestData.BleedAirLeft +
                            " BleedR=" + _latestData.BleedAirRight +
                            " ApuBleed=" + _latestData.ApuBleedAir +
                            " CmdA=" + _latestData.McpCmdA +
                            " CmdB=" + _latestData.McpCmdB +
                            " CwsA=" + _latestData.McpCwsA +
                            " CwsB=" + _latestData.McpCwsB +
                            " DoorOpen=" + (AnyDoorOpen(_latestData) ? 1 : 0));
                    }
                    else if (data.dwRequestID == (uint)ClientRequestId.Control)
                    {
                        _controlArea = (PmdgNg3ControlArea)data.dwData[0];
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[PMDG SDK] Error procesando ClientData: " + ex.Message);
                }
            }

            public SimData EnrichWithSdk(SimData baseData)
            {
                if (!_hasData) return baseData;

                bool seatbeltOn = _latestData.FastenBeltsSelector == 2;
                bool noSmokingOn = _latestData.NoSmokingSelector == 2;

                bool anyBleedOn = _latestData.BleedAirLeft != 0
                    || _latestData.BleedAirRight != 0
                    || _latestData.ApuBleedAir != 0;

                bool anyDoorOpen = AnyDoorOpen(_latestData);

                bool autopilotOn =
                    _latestData.McpCmdA != 0
                    || _latestData.McpCmdB != 0
                    || _latestData.McpCwsA != 0
                    || _latestData.McpCwsB != 0;

                bool apuSwitchOn = _latestData.ApuSelector == 1 || _latestData.ApuSelector == 2;

                baseData.SeatBeltSign = seatbeltOn;
                baseData.NoSmokingSign = noSmokingOn;
                baseData.BleedAirOn = anyBleedOn;
                baseData.DoorOpen = anyDoorOpen;
                baseData.ApuAvailable = apuSwitchOn;
                baseData.ApuRunning = apuSwitchOn;
                baseData.AutopilotActive = autopilotOn;

                // Luces PMDG más fiables que algunos simvars estándar en ciertas variantes
                baseData.TaxiLightsOn = _latestData.TaxiLight != 0;
                baseData.BeaconLightsOn = _latestData.AntiCollision != 0;
                baseData.NavLightsOn = _latestData.PositionLight != 1;

                Debug.WriteLine(
                    "[PMDG SDK] Seatbelt=" + seatbeltOn +
                    " NoSmoking=" + noSmokingOn +
                    " APU=" + apuSwitchOn +
                    " AP=" + autopilotOn +
                    " Bleed=" + anyBleedOn +
                    " Door=" + anyDoorOpen);

                return baseData;
            }

            private static bool AnyDoorOpen(PmdgNg3SelectedData d)
            {
                return d.DoorFwdEntry != 0
                    || d.DoorFwdService != 0
                    || d.DoorAirstair != 0
                    || d.DoorLeftFwdOverwing != 0
                    || d.DoorRightFwdOverwing != 0
                    || d.DoorFwdCargo != 0
                    || d.DoorEquip != 0
                    || d.DoorLeftAftOverwing != 0
                    || d.DoorRightAftOverwing != 0
                    || d.DoorAftCargo != 0
                    || d.DoorAftEntry != 0
                    || d.DoorAftService != 0;
            }

            public void Dispose()
            {
                _simConnect = null;
            }
        }


        private static class Pmdg737OptionsConfigurator
        {
            private const string IniFileName = "737_Options.ini";
            private const string SdkSectionHeader = "[SDK]";
            private const string EnableBroadcastLine = "EnableDataBroadcast=1";

            public static void TryEnsureSdkEnabled()
            {
                try
                {
                    foreach (var path in GetCandidateIniPaths())
                    {
                        if (!File.Exists(path))
                            continue;

                        EnsureIniPatched(path);
                        Debug.WriteLine($"[PMDG SDK] Options.ini validado automáticamente: {path}");
                        return;
                    }

                    Debug.WriteLine("[PMDG SDK] 737_Options.ini no encontrado; se omite auto-configuración.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[PMDG SDK] No se pudo auto-configurar 737_Options.ini: " + ex.Message);
                }
            }

            private static string[] GetCandidateIniPaths()
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                return new[]
                {
                    Path.Combine(localAppData, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalState", "packages", "pmdg-aircraft-737", "work", IniFileName),
                    Path.Combine(appData, "Microsoft Flight Simulator", "Packages", "pmdg-aircraft-737", "work", IniFileName),
                };
            }

            private static void EnsureIniPatched(string path)
            {
                var original = File.ReadAllText(path, Encoding.UTF8);
                var normalized = original.Replace("\r\n", "\n").Replace("\r", "\n");
                var lines = normalized.Split(new[] { '\n' }, StringSplitOptions.None).ToList();

                var sdkIndex = lines.FindIndex(line => string.Equals(line.Trim(), SdkSectionHeader, StringComparison.OrdinalIgnoreCase));
                var changed = false;

                if (sdkIndex < 0)
                {
                    if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                        lines.Add(string.Empty);

                    lines.Add(SdkSectionHeader);
                    lines.Add(EnableBroadcastLine);
                    changed = true;
                }
                else
                {
                    var nextSectionIndex = lines.FindIndex(sdkIndex + 1, line => line.TrimStart().StartsWith("[", StringComparison.Ordinal));
                    if (nextSectionIndex < 0) nextSectionIndex = lines.Count;

                    var foundEnable = false;
                    for (var i = sdkIndex + 1; i < nextSectionIndex; i++)
                    {
                        var trimmed = lines[i].Trim();
                        if (!trimmed.StartsWith("EnableDataBroadcast", StringComparison.OrdinalIgnoreCase))
                            continue;

                        foundEnable = true;
                        if (!string.Equals(trimmed, EnableBroadcastLine, StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = EnableBroadcastLine;
                            changed = true;
                        }
                    }

                    if (!foundEnable)
                    {
                        lines.Insert(sdkIndex + 1, EnableBroadcastLine);
                        changed = true;
                    }
                }

                if (!changed)
                    return;

                var backupPath = path + ".bak";
                if (!File.Exists(backupPath))
                    File.Copy(path, backupPath, false);

                var finalText = string.Join(Environment.NewLine, lines).TrimEnd() + Environment.NewLine;
                File.WriteAllText(path, finalText, Encoding.UTF8);
            }
        }


        // ──────────────────────────────────────────────────────────────────────
        // LVAR INTEGRATION (MobiFlight — solo A32NX / Fenix)
        // ──────────────────────────────────────────────────────────────────────

        private class MobiFlightIntegration : IDisposable
        {
            private Microsoft.FlightSimulator.SimConnect.SimConnect? _simConnect;
            private bool _isAvailable;
            private bool _disposed;
            private bool _defaultChannelsMapped;
            private bool _clientChannelsMapped;
            private bool _clientAddRequested;
            private bool _clientRegistered;
            private string _registeredProfile = string.Empty;
            private string _pendingProfileCode = string.Empty;

            private readonly Dictionary<string, double> _lvarCache = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<uint, string> _requestKeyMap = new Dictionary<uint, string>();
            private readonly Dictionary<uint, string> _defaultRequestKeyMap = new Dictionary<uint, string>();
            private List<LvarSpec> _registeredSpecs = new List<LvarSpec>();
            private List<LvarSpec> _pendingSpecs = new List<LvarSpec>();
            private int _maxDefinedLvarSlots;
            private int _maxDefaultDefinedLvarSlots;
            private bool _usingDefaultLvarChannel;
            private DateTime _clientAddRequestedUtc = DateTime.MinValue;

            private const string DefaultCommandChannelName = "MobiFlight.Command";
            private const string DefaultResponseChannelName = "MobiFlight.Response";
            private const string DefaultLvarsChannelName = "MobiFlight.LVars";
            private const string ClientName = "PatagoniaWingsAcars";
            private const string ClientCommandChannelName = ClientName + ".Command";
            private const string ClientResponseChannelName = ClientName + ".Response";
            private const string ClientLvarsChannelName = ClientName + ".LVars";
            private const int StringAreaBytes = 256;
            private const uint FirstLvarDefId = 11000;
            private const uint FirstLvarReqId = 12000;

            private enum ClientDataId : uint
            {
                DefaultCommand = 1000,
                DefaultResponse = 1001,
                DefaultLvars = 1005,
                ClientCommand = 1002,
                ClientResponse = 1003,
                ClientLvars = 1004
            }

            private enum ClientDefId : uint
            {
                CommandString = 10000,
                ResponseString = 10001,
                ClientResponseString = 10002
            }

            private enum ClientReqId : uint
            {
                DefaultResponse = 13000,
                ClientResponse = 13001
            }

            private const uint FirstDefaultLvarDefId = 14000;
            private const uint FirstDefaultLvarReqId = 15000;

            private struct LvarSpec
            {
                public string Var;
                public string Key;
                public LvarSpec(string varName, string key)
                {
                    Var = varName;
                    Key = key;
                }
            }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Ansi, Pack = 1)]
            private struct ClientDataString256
            {
                [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = StringAreaBytes)]
                public string Value;
            }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
            private struct ClientDataFloat
            {
                public float Value;
            }

            public bool IsAvailable => _isAvailable;

            public void Initialize(Microsoft.FlightSimulator.SimConnect.SimConnect simConnect)
            {
                _simConnect = simConnect;
                _isAvailable = true;
                try
                {
                    EnsureDefaultChannels();
                    Debug.WriteLine("[MobiFlight] LVAR integration lista (ClientData)");
                }
                catch (Exception ex)
                {
                    _isAvailable = false;
                    Debug.WriteLine($"[MobiFlight] init error: {ex.Message}");
                }
            }

            private static readonly LvarSpec[] A32NX_LVARS =
            {
                new LvarSpec("(L:A32NX_ELEC_AC_ESS_BUS_IS_POWERED, bool)", "Beacon"),
                new LvarSpec("(L:A32NX_ELEC_AC_1_BUS_IS_POWERED, bool)",   "BusPowered"),
                new LvarSpec("LIGHT BEACON",                                 "BeaconNative"),
                new LvarSpec("LIGHT STROBE",                                 "StrobeNative"),
                new LvarSpec("LIGHT LANDING",                                "LandingNative"),
                new LvarSpec("LIGHT NAV",                                    "NavNative"),
                new LvarSpec("LIGHT TAXI",                                   "TaxiNative"),
                new LvarSpec("(L:A32NX_ENGINE_N1:1, percent)",               "N1_1"),
                new LvarSpec("(L:A32NX_ENGINE_N1:2, percent)",               "N1_2"),
            };

            private static readonly LvarSpec[] FENIX_LVARS =
            {
                new LvarSpec("LIGHT BEACON",  "BeaconNative"),
                new LvarSpec("LIGHT STROBE",  "StrobeNative"),
                new LvarSpec("LIGHT LANDING", "LandingNative"),
                new LvarSpec("LIGHT NAV",     "NavNative"),
                new LvarSpec("LIGHT TAXI",    "TaxiNative"),
                new LvarSpec("TURB ENG N1:1", "N1_1"),
                new LvarSpec("TURB ENG N1:2", "N1_2"),
            };

            public void RequestLvarsForProfile(AircraftProfile profile)
            {
                if (!_isAvailable || _simConnect == null || profile == null) return;

                var lvars = BuildLvarsForProfile(profile);
                if (lvars.Count == 0) return;

                try
                {
                    EnsureDefaultChannels();
                    var preferDefaultChannel = ShouldPreferDefaultLvarChannel(profile);

                    if (preferDefaultChannel)
                    {
                        ConfigureDefaultLvars(profile.Code ?? string.Empty, lvars);
                        return;
                    }

                    if (!_clientRegistered)
                    {
                        _pendingProfileCode = profile.Code ?? string.Empty;
                        _pendingSpecs = lvars;
                        if (!_clientAddRequested)
                        {
                            SendDefaultCommand("MF.Clients.Add." + ClientName);
                            _clientAddRequested = true;
                            _clientAddRequestedUtc = DateTime.UtcNow;
                            Debug.WriteLine("[MobiFlight] Solicitando cliente dedicado: " + ClientName);
                        }
                        if ((DateTime.UtcNow - _clientAddRequestedUtc).TotalSeconds >= 2)
                        {
                            ConfigureDefaultLvars(profile.Code ?? string.Empty, lvars);
                            Debug.WriteLine("[MobiFlight] Fallback a canal por defecto para perfil '" + (profile.Code ?? string.Empty) + "'");
                        }
                        return;
                    }

                    if (_registeredProfile == profile.Code && _registeredSpecs.Count == lvars.Count)
                    {
                        if (_lvarCache.Count == 0)
                        {
                            if (_usingDefaultLvarChannel)
                                SubscribeCurrentDefaultLvars();
                            else
                                SubscribeCurrentLvars();
                            Debug.WriteLine($"[MobiFlight] Reintentando polling LVAR para '{_registeredProfile}' (cache vacía)");
                        }
                        return;
                    }

                    ConfigureClientLvars(profile.Code ?? string.Empty, lvars);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MobiFlight] Error registrando/pidiendo LVARs: {ex.Message}");
                }
            }

            private static bool ShouldPreferDefaultLvarChannel(AircraftProfile profile)
            {
                if (profile == null) return false;
                return string.Equals(profile.LvarProfile, "MADDOG_HYBRID", StringComparison.OrdinalIgnoreCase);
            }

            private void EnsureDefaultChannels()
            {
                if (_simConnect == null || _defaultChannelsMapped) return;

                _simConnect.MapClientDataNameToID(DefaultCommandChannelName, ClientDataId.DefaultCommand);
                _simConnect.MapClientDataNameToID(DefaultResponseChannelName, ClientDataId.DefaultResponse);
                _simConnect.MapClientDataNameToID(DefaultLvarsChannelName, ClientDataId.DefaultLvars);

                _simConnect.AddToClientDataDefinition(ClientDefId.CommandString, 0, StringAreaBytes, 0, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
                _simConnect.AddToClientDataDefinition(ClientDefId.ResponseString, 0, StringAreaBytes, 0, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
                _simConnect.RegisterDataDefineStruct<ClientDataString256>(ClientDefId.CommandString);
                _simConnect.RegisterDataDefineStruct<ClientDataString256>(ClientDefId.ResponseString);
                _simConnect.RequestClientData(ClientDataId.DefaultResponse, ClientReqId.DefaultResponse, ClientDefId.ResponseString, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

                _defaultChannelsMapped = true;
                SendDefaultCommand("MF.Ping");
            }

            private void EnsureClientChannels()
            {
                if (_simConnect == null || _clientChannelsMapped) return;

                _simConnect.MapClientDataNameToID(ClientCommandChannelName, ClientDataId.ClientCommand);
                _simConnect.MapClientDataNameToID(ClientResponseChannelName, ClientDataId.ClientResponse);
                _simConnect.MapClientDataNameToID(ClientLvarsChannelName, ClientDataId.ClientLvars);

                _simConnect.AddToClientDataDefinition(ClientDefId.ClientResponseString, 0, StringAreaBytes, 0, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
                _simConnect.RegisterDataDefineStruct<ClientDataString256>(ClientDefId.ClientResponseString);
                _simConnect.RequestClientData(ClientDataId.ClientResponse, ClientReqId.ClientResponse, ClientDefId.ClientResponseString, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, 0, 0, 0);

                _clientChannelsMapped = true;
                SendClientCommand("MF.Ping");
            }

            private void ConfigureClientLvars(string profileCode, List<LvarSpec> lvars)
            {
                if (_simConnect == null) return;

                EnsureClientChannels();
                _usingDefaultLvarChannel = false;
                _lvarCache.Clear();
                _requestKeyMap.Clear();
                _registeredSpecs = lvars;
                _registeredProfile = profileCode;

                SendClientCommand("MF.SimVars.Clear");
                SendClientCommand("MF.Config.MAX_VARS_PER_FRAME.Set.30");

                for (int i = 0; i < lvars.Count; i++)
                {
                    var spec = lvars[i];
                    SendClientCommand("MF.SimVars.Add." + spec.Var);
                    EnsureLvarDefinitionSlot(i);

                    var reqId = (ClientReqId)(FirstLvarReqId + (uint)i);
                    var defId = (ClientDefId)(FirstLvarDefId + (uint)i);
                    _requestKeyMap[(uint)reqId] = spec.Key;

                    _simConnect.RequestClientData(ClientDataId.ClientLvars, reqId, defId, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, (uint)(i * 4), 0, 0);
                    Debug.WriteLine($"[MobiFlight] Subscribed => {spec.Key} :: {spec.Var} @ offset {i * 4}");
                }

                Debug.WriteLine($"[MobiFlight] LVARs registradas para '{_registeredProfile}' ({lvars.Count}) por ClientData");
            }

            private void ConfigureDefaultLvars(string profileCode, List<LvarSpec> lvars)
            {
                if (_simConnect == null) return;

                EnsureDefaultChannels();
                _usingDefaultLvarChannel = true;
                _lvarCache.Clear();
                _defaultRequestKeyMap.Clear();
                _registeredSpecs = lvars;
                _registeredProfile = profileCode;

                SendDefaultCommand("MF.SimVars.Clear");
                SendDefaultCommand("MF.Config.MAX_VARS_PER_FRAME.Set.30");

                for (int i = 0; i < lvars.Count; i++)
                {
                    var spec = lvars[i];
                    SendDefaultCommand("MF.SimVars.Add." + spec.Var);
                    EnsureDefaultLvarDefinitionSlot(i);

                    var reqId = (ClientReqId)(FirstDefaultLvarReqId + (uint)i);
                    var defId = (ClientDefId)(FirstDefaultLvarDefId + (uint)i);
                    _defaultRequestKeyMap[(uint)reqId] = spec.Key;

                    _simConnect.RequestClientData(ClientDataId.DefaultLvars, reqId, defId, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, (uint)(i * 4), 0, 0);
                    Debug.WriteLine($"[MobiFlight] Default subscribed => {spec.Key} :: {spec.Var} @ offset {i * 4}");
                }

                Debug.WriteLine($"[MobiFlight] LVARs registradas para '{_registeredProfile}' ({lvars.Count}) por canal por defecto");
            }

            private void EnsureLvarDefinitionSlot(int index)
            {
                if (_simConnect == null) return;
                while (_maxDefinedLvarSlots <= index)
                {
                    var defId = (ClientDefId)(FirstLvarDefId + (uint)_maxDefinedLvarSlots);
                    _simConnect.AddToClientDataDefinition(defId, 0, 4, 0, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
                    _simConnect.RegisterDataDefineStruct<ClientDataFloat>(defId);
                    _maxDefinedLvarSlots++;
                }
            }

            private void EnsureDefaultLvarDefinitionSlot(int index)
            {
                if (_simConnect == null) return;
                while (_maxDefaultDefinedLvarSlots <= index)
                {
                    var defId = (ClientDefId)(FirstDefaultLvarDefId + (uint)_maxDefaultDefinedLvarSlots);
                    _simConnect.AddToClientDataDefinition(defId, 0, 4, 0, Microsoft.FlightSimulator.SimConnect.SimConnect.SIMCONNECT_UNUSED);
                    _simConnect.RegisterDataDefineStruct<ClientDataFloat>(defId);
                    _maxDefaultDefinedLvarSlots++;
                }
            }

            private void SubscribeCurrentLvars()
            {
                if (_simConnect == null || !_clientRegistered || _registeredSpecs.Count == 0) return;
                for (int i = 0; i < _registeredSpecs.Count; i++)
                {
                    var reqId = (ClientReqId)(FirstLvarReqId + (uint)i);
                    var defId = (ClientDefId)(FirstLvarDefId + (uint)i);
                    _simConnect.RequestClientData(ClientDataId.ClientLvars, reqId, defId, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, (uint)(i * 4), 0, 0);
                }
                SendClientCommand("MF.Ping");
            }

            private void SubscribeCurrentDefaultLvars()
            {
                if (_simConnect == null || _registeredSpecs.Count == 0) return;
                for (int i = 0; i < _registeredSpecs.Count; i++)
                {
                    var reqId = (ClientReqId)(FirstDefaultLvarReqId + (uint)i);
                    var defId = (ClientDefId)(FirstDefaultLvarDefId + (uint)i);
                    _simConnect.RequestClientData(ClientDataId.DefaultLvars, reqId, defId, SIMCONNECT_CLIENT_DATA_PERIOD.ON_SET, SIMCONNECT_CLIENT_DATA_REQUEST_FLAG.DEFAULT, (uint)(i * 4), 0, 0);
                }
                SendDefaultCommand("MF.Ping");
            }

            private void SendDefaultCommand(string command)
            {
                if (_simConnect == null || string.IsNullOrWhiteSpace(command)) return;
                var payload = new ClientDataString256 { Value = command };
                _simConnect.SetClientData(ClientDataId.DefaultCommand, ClientDefId.CommandString, SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0, payload);
                Debug.WriteLine("[MobiFlight] CMD(default) => " + command);
            }

            private void SendClientCommand(string command)
            {
                if (_simConnect == null || !_clientChannelsMapped || string.IsNullOrWhiteSpace(command)) return;
                var payload = new ClientDataString256 { Value = command };
                _simConnect.SetClientData(ClientDataId.ClientCommand, ClientDefId.CommandString, SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0, payload);
                Debug.WriteLine("[MobiFlight] CMD(client) => " + command);
            }

            public void ProcessSimObjectData(SIMCONNECT_RECV_SIMOBJECT_DATA data)
            {
                // El bridge MobiFlight/WASM correcto usa ClientData, no SimObjectData.
            }

            public void ProcessClientData(SIMCONNECT_RECV_CLIENT_DATA data)
            {
                try
                {
                    uint requestId = data.dwRequestID;
                    object payload = data.dwData;
                    try
                    {
                        if (data.dwData != null && data.dwData.Length > 0)
                            payload = data.dwData;
                    }
                    catch (Exception exPayload)
                    {
                        Debug.WriteLine("[MobiFlight] Error leyendo dwData[0]: " + exPayload.Message);
                    }

                    if (requestId == (uint)ClientReqId.DefaultResponse)
                    {
                        string msg = NormalizeString(ExtractStringPayload(payload));
                        if (!string.IsNullOrWhiteSpace(msg))
                        {
                            Debug.WriteLine("[MobiFlight] RESP(default) <= " + msg);
                            HandleResponse(msg, false);
                        }
                        else
                        {
                            Debug.WriteLine("[MobiFlight] RESP(default) vacío o no parseable" + DescribePayload(payload));
                        }
                        return;
                    }

                    if (requestId == (uint)ClientReqId.ClientResponse)
                    {
                        string msg = NormalizeString(ExtractStringPayload(payload));
                        if (!string.IsNullOrWhiteSpace(msg))
                        {
                            Debug.WriteLine("[MobiFlight] RESP(client) <= " + msg);
                            HandleResponse(msg, true);
                        }
                        else
                        {
                            Debug.WriteLine("[MobiFlight] RESP(client) vacío o no parseable" + DescribePayload(payload));
                        }
                        return;
                    }

                    if (_requestKeyMap.TryGetValue(requestId, out var key))
                    {
                        double value;
                        if (TryExtractFloatPayload(payload, out value))
                        {
                            _lvarCache[key] = value;
                            Debug.WriteLine("[MobiFlight] LVAR <= " + key + "=" + value.ToString("F2", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            Debug.WriteLine("[MobiFlight] LVAR sin parse para " + key + DescribePayload(payload));
                        }
                        return;
                    }

                    if (_defaultRequestKeyMap.TryGetValue(requestId, out var defaultKey))
                    {
                        double value;
                        if (TryExtractFloatPayload(payload, out value))
                        {
                            _lvarCache[defaultKey] = value;
                            Debug.WriteLine("[MobiFlight] Default LVAR <= " + defaultKey + "=" + value.ToString("F2", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            Debug.WriteLine("[MobiFlight] Default LVAR sin parse para " + defaultKey + DescribePayload(payload));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[MobiFlight] Error procesando ClientData: " + ex.Message);
                }
            }

            private void HandleResponse(string message, bool fromClientChannel)
            {
                if (string.IsNullOrWhiteSpace(message)) return;

                if (message.Equals("MF.Pong", StringComparison.OrdinalIgnoreCase))
                    return;

                if (message.Equals("MF.Clients.Add." + ClientName + ".Finished", StringComparison.OrdinalIgnoreCase))
                {
                    _clientRegistered = true;
                    _clientAddRequested = false;
                    EnsureClientChannels();
                    Debug.WriteLine("[MobiFlight] Cliente dedicado listo => " + ClientName);
                    if (_pendingSpecs.Count > 0)
                        ConfigureClientLvars(_pendingProfileCode, new List<LvarSpec>(_pendingSpecs));
                    return;
                }

                if (!fromClientChannel && message.StartsWith("MF.Clients.Add.", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine("[MobiFlight] Respuesta add-client distinta => " + message);
                    return;
                }
            }

            private static string NormalizeString(string value)
            {
                return string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : value.TrimEnd('\0', ' ', '\r', '\n', '\t');
            }


            private static string DescribePayload(object payload)
            {
                if (payload == null) return " [payload=null]";
                try
                {
                    return " [payload=" + payload.GetType().FullName + "]";
                }
                catch
                {
                    return " [payload=<unknown>]";
                }
            }

            private static string ExtractStringPayload(object payload)
            {
                if (payload == null) return string.Empty;
                try
                {
                    if (payload is string s)
                        return s;
                    if (payload is byte[] bytes)
                        return Encoding.ASCII.GetString(bytes).TrimEnd('\0');
                    if (payload is char[] chars)
                        return new string(chars).TrimEnd('\0');
                    if (TryConvertPayloadToBytes(payload, out var rawBytes) && rawBytes.Length > 0)
                        return Encoding.ASCII.GetString(rawBytes).TrimEnd('\0');
                    if (payload is Array arr && arr.Length > 0)
                    {
                        foreach (object first in arr)
                        {
                            if (first == null || ReferenceEquals(first, payload))
                                continue;
                            string nested = ExtractStringPayload(first);
                            if (!string.IsNullOrWhiteSpace(nested)) return nested;
                        }
                    }
                    Type type = payload.GetType();
                    FieldInfo field = type.GetField("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        object fieldValue = field.GetValue(payload);
                        if (fieldValue != null && !ReferenceEquals(fieldValue, payload))
                        {
                            string nested = ExtractStringPayload(fieldValue);
                            if (!string.IsNullOrWhiteSpace(nested)) return nested;
                        }
                    }
                    PropertyInfo prop = type.GetProperty("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null && prop.CanRead)
                    {
                        object propValue = prop.GetValue(payload, null);
                        if (propValue != null && !ReferenceEquals(propValue, payload))
                        {
                            string nested = ExtractStringPayload(propValue);
                            if (!string.IsNullOrWhiteSpace(nested)) return nested;
                        }
                    }
                }
                catch
                {
                }
                return string.Empty;
            }

            private static bool TryConvertPayloadToBytes(object payload, out byte[] bytes)
            {
                bytes = Array.Empty<byte>();
                if (payload == null) return false;

                try
                {
                    if (payload is byte[] raw)
                    {
                        bytes = raw;
                        return true;
                    }

                    if (!(payload is Array arr) || arr.Length == 0)
                        return false;

                    var buffer = new List<byte>();
                    foreach (object item in arr)
                    {
                        if (item == null) continue;

                        switch (item)
                        {
                            case byte b:
                                buffer.Add(b);
                                break;
                            case sbyte sb:
                                buffer.Add(unchecked((byte)sb));
                                break;
                            case short s:
                                buffer.AddRange(BitConverter.GetBytes(s));
                                break;
                            case ushort us:
                                buffer.AddRange(BitConverter.GetBytes(us));
                                break;
                            case int i:
                                buffer.AddRange(BitConverter.GetBytes(i));
                                break;
                            case uint ui:
                                buffer.AddRange(BitConverter.GetBytes(ui));
                                break;
                            case long l:
                                buffer.AddRange(BitConverter.GetBytes(l));
                                break;
                            case ulong ul:
                                buffer.AddRange(BitConverter.GetBytes(ul));
                                break;
                        }
                    }

                    if (buffer.Count == 0)
                        return false;

                    bytes = buffer.ToArray();
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private static bool TryExtractFloatPayload(object payload, out double value)
            {
                value = 0;
                if (payload == null) return false;
                try
                {
                    if (payload is float f) { value = f; return true; }
                    if (payload is double d) { value = d; return true; }
                    if (payload is decimal dm) { value = (double)dm; return true; }
                    if (payload is int i) { value = i; return true; }
                    if (payload is uint ui) { value = ui; return true; }
                    if (payload is short s) { value = s; return true; }
                    if (payload is ushort us) { value = us; return true; }
                    if (payload is long l) { value = l; return true; }
                    if (payload is ulong ul) { value = ul; return true; }
                    if (payload is byte b) { value = b; return true; }
                    if (payload is sbyte sb) { value = sb; return true; }
                    if (payload is bool bo) { value = bo ? 1 : 0; return true; }
                    if (payload is string str)
                    {
                        double parsed;
                        if (double.TryParse(str, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed))
                        {
                            value = parsed;
                            return true;
                        }
                    }
                    if (payload is byte[] byteArray && byteArray.Length >= 4)
                    {
                        value = BitConverter.ToSingle(byteArray, 0);
                        return true;
                    }
                    if (payload is Array arr && arr.Length > 0)
                    {
                        object first = arr.GetValue(0);
                        if (first != null && !ReferenceEquals(first, payload) && TryExtractFloatPayload(first, out value))
                            return true;
                    }
                    Type type = payload.GetType();
                    FieldInfo field = type.GetField("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        object nested = field.GetValue(payload);
                        if (nested != null && !ReferenceEquals(nested, payload) && TryExtractFloatPayload(nested, out value))
                            return true;
                    }
                    PropertyInfo prop = type.GetProperty("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null && prop.CanRead)
                    {
                        object nested = prop.GetValue(payload, null);
                        if (nested != null && !ReferenceEquals(nested, payload) && TryExtractFloatPayload(nested, out value))
                            return true;
                    }
                    if (payload is IConvertible)
                    {
                        value = Convert.ToDouble(payload, CultureInfo.InvariantCulture);
                        return true;
                    }
                }
                catch
                {
                }
                return false;
            }

            private static List<LvarSpec> BuildLvarsForProfile(AircraftProfile profile)
            {
                if (profile == null) return new List<LvarSpec>();

                if (profile.Code == "A319_FENIX" || profile.Code == "A320_FENIX" || profile.Code == "A321_FENIX")
                    return new List<LvarSpec>(FENIX_LVARS);

                if (profile.Code == "A20N_FBW" || profile.Code == "A339_HEADWIND")
                    return new List<LvarSpec>(A32NX_LVARS);

                var specs = new List<LvarSpec>();
                AddIfPresent(specs, profile.MobiFlightSeatbeltExpression, "SeatbeltRaw");
                AddIfPresent(specs, profile.MobiFlightNoSmokingExpression, "NoSmokingRaw");
                AddIfPresent(specs, profile.MobiFlightApuExpression, "ApuPct");
                AddIfPresent(specs, profile.MobiFlightAutopilotExpression, "AutopilotRaw");
                AddIfPresent(specs, profile.MobiFlightBleedAirExpression, "BleedAirRaw");
                AddIfPresent(specs, profile.MobiFlightInertialSeparatorExpression, "InertialSeparatorRaw");
                if (profile.MobiFlightDoorExpressions != null)
                {
                    for (int i = 0; i < profile.MobiFlightDoorExpressions.Count && i < 8; i++)
                        AddIfPresent(specs, profile.MobiFlightDoorExpressions[i], "Door" + i);
                }
                return specs;
            }

            private static void AddIfPresent(List<LvarSpec> specs, string expression, string key)
            {
                if (!string.IsNullOrWhiteSpace(expression))
                    specs.Add(new LvarSpec(expression.Trim(), key));
            }

            public SimData EnrichWithLvars(SimData baseData, AircraftProfile profile)
            {
                if (!_isAvailable || profile == null) return baseData;
                if (_lvarCache.Count == 0)
                {
                    Debug.WriteLine("[MobiFlight] cache LVAR vacía para perfil " + (profile.Code ?? "UNKNOWN"));
                    return baseData;
                }

                if (_lvarCache.TryGetValue("BeaconNative", out var b)) baseData.BeaconLightsOn  = b > 0.5;
                if (_lvarCache.TryGetValue("StrobeNative", out var s)) baseData.StrobeLightsOn  = s > 0.5;
                if (_lvarCache.TryGetValue("LandingNative", out var l)) baseData.LandingLightsOn = l > 0.5;
                if (_lvarCache.TryGetValue("NavNative", out var n)) baseData.NavLightsOn      = n > 0.5;
                if (_lvarCache.TryGetValue("TaxiNative", out var t)) baseData.TaxiLightsOn    = t > 0.5;
                if (_lvarCache.TryGetValue("N1_1", out var n1)) baseData.Engine1N1 = n1;
                if (_lvarCache.TryGetValue("N1_2", out var n2)) baseData.Engine2N1 = n2;

                if (_lvarCache.TryGetValue("SeatbeltRaw", out var genericSeatbeltRaw))
                {
                    if (string.Equals(profile.LvarProfile, "PMDG 737", StringComparison.OrdinalIgnoreCase))
                    {
                        bool autoActive = genericSeatbeltRaw > 25 && genericSeatbeltRaw < 75;
                        baseData.SeatBeltSign = genericSeatbeltRaw >= 75 || (autoActive && (baseData.FlapsDeployed || baseData.GearDown || baseData.OnGround));
                    }
                    else
                    {
                        baseData.SeatBeltSign = genericSeatbeltRaw > 0.5;
                    }
                }

                if (_lvarCache.TryGetValue("NoSmokingRaw", out var genericNoSmokingRaw))
                {
                    if (string.Equals(profile.LvarProfile, "PMDG 737", StringComparison.OrdinalIgnoreCase))
                    {
                        bool autoActive = genericNoSmokingRaw > 25 && genericNoSmokingRaw < 75;
                        baseData.NoSmokingSign = genericNoSmokingRaw >= 75 || (autoActive && (baseData.FlapsDeployed || baseData.GearDown || baseData.OnGround));
                    }
                    else
                    {
                        baseData.NoSmokingSign = genericNoSmokingRaw > 0.5;
                    }
                }
                else if (string.Equals(profile.LvarProfile, "PMDG 737", StringComparison.OrdinalIgnoreCase) && _lvarCache.ContainsKey("SeatbeltRaw"))
                {
                    baseData.NoSmokingSign = baseData.SeatBeltSign;
                }

                if (_lvarCache.TryGetValue("ApuPct", out var genericApuRaw))
                {
                    baseData.ApuAvailable = genericApuRaw > 0.1;
                    baseData.ApuRunning = string.Equals(profile.LvarProfile, "PMDG 737", StringComparison.OrdinalIgnoreCase)
                        ? genericApuRaw > 0.1
                        : genericApuRaw > 0.5;
                }

                if (_lvarCache.TryGetValue("AutopilotRaw", out var genericApRaw))
                    baseData.AutopilotActive = genericApRaw > 0.5;

                if (_lvarCache.TryGetValue("BleedAirRaw", out var genericBleedRaw))
                    baseData.BleedAirOn = genericBleedRaw > 0.5;

                if (_lvarCache.TryGetValue("InertialSeparatorRaw", out var inertialSeparatorRaw))
                    baseData.InertialSeparatorOn = inertialSeparatorRaw > 0.5;

                double maxDoorPct = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (_lvarCache.TryGetValue("Door" + i, out var doorPct) && doorPct > maxDoorPct)
                        maxDoorPct = doorPct;
                }
                if (maxDoorPct > 0)
                    baseData.DoorOpen = maxDoorPct > Math.Max(0.1, profile.DoorOpenThresholdPercent);

                if (string.Equals(profile.LvarProfile, "PMDG 737", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[MobiFlight PMDG737 RAW] SeatbeltRaw={(_lvarCache.ContainsKey("SeatbeltRaw") ? _lvarCache["SeatbeltRaw"] : -1)} NoSmokingRaw={(_lvarCache.ContainsKey("NoSmokingRaw") ? _lvarCache["NoSmokingRaw"] : -1)} ApuRaw={( _lvarCache.ContainsKey("ApuPct") ? _lvarCache["ApuPct"] : -1)} ApRaw={(_lvarCache.ContainsKey("AutopilotRaw") ? _lvarCache["AutopilotRaw"] : -1)} BleedRaw={(_lvarCache.ContainsKey("BleedAirRaw") ? _lvarCache["BleedAirRaw"] : -1)} DoorMax={maxDoorPct}");
                    Debug.WriteLine($"[MobiFlight PMDG737] Seatbelt={baseData.SeatBeltSign} NoSmoking={baseData.NoSmokingSign} APU={baseData.ApuRunning} AP={baseData.AutopilotActive} Bleed={baseData.BleedAirOn} Door={baseData.DoorOpen}");
                }
                else if (ProfileIsMaddog(profile.Code))
                {
                    Debug.WriteLine($"[MobiFlight MADDOG] SeatbeltRaw={(_lvarCache.ContainsKey("SeatbeltRaw") ? _lvarCache["SeatbeltRaw"] : -1)} NoSmokingRaw={(_lvarCache.ContainsKey("NoSmokingRaw") ? _lvarCache["NoSmokingRaw"] : -1)} ApuRaw={(_lvarCache.ContainsKey("ApuPct") ? _lvarCache["ApuPct"] : -1)} ApRaw={(_lvarCache.ContainsKey("AutopilotRaw") ? _lvarCache["AutopilotRaw"] : -1)} BleedRaw={(_lvarCache.ContainsKey("BleedAirRaw") ? _lvarCache["BleedAirRaw"] : -1)} DoorMax={maxDoorPct} Seatbelt={baseData.SeatBeltSign} NoSmoking={baseData.NoSmokingSign} APU={baseData.ApuRunning} AP={baseData.AutopilotActive} Bleed={baseData.BleedAirOn} Door={baseData.DoorOpen}");
                }

                return baseData;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _simConnect = null;
                _disposed = true;
            }
        }

        private static int NormalizeTransponderState(bool available, int rawState)
        {
            if (!available) return -1;
            if (rawState < 0 || rawState > 5) return -1;
            return rawState;
        }
    }
}
