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
        private const bool EnableLvarReadBridge = false;
        private const int WM_USER_SIMCONNECT = 0x0402;

        private static readonly object ActiveInstanceLock = new object();
        private static SimConnectService? _activeFacilitiesInstance;

        private Microsoft.FlightSimulator.SimConnect.SimConnect? _simConnect;
        private HwndSource? _hwndSource;
        private bool _disposed;
        private bool _isPaused;
        private bool _hasReceivedAircraftData;

        // C11A Facilities bridge. This is discovery/evidence only; exact geometry
        // resolution is handled in later C11B/C11C blocks.
        private readonly object _facilityBridgeLock = new object();
        private bool _facilityBridgeInitAttempted;
        private DateTime? _facilityBridgeLastInitAttemptUtc;
        private bool _facilityBridgeAvailable;
        private bool _facilityBridgeSubscribed;
        private bool _facilityDataReceived;
        private string _facilityBridgeStatus = "not_initialized";
        private string _facilityBridgeLastDataStatus = string.Empty;
        private readonly Dictionary<string, int> _facilityBridgeDataTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private string _facilityBridgeLastIcao = string.Empty;
        private string _facilityBridgeLastRegion = string.Empty;
        private int _facilityBridgeRecordsReceived;
        private int _facilityBridgeAirportCount;
        private string _facilityBridgeNearestAirports = string.Empty;
        private DateTime? _facilityBridgeLastRequestUtc;
        private DateTime? _facilityBridgeLastReceivedUtc;

        // C11B direct airport facility requests by ICAO. Discovery only; C11C will
        // resolve runway/taxiway/parking geometry from confirmed payloads.
        private bool _facilityAirportDefinitionConfigured;
        private readonly HashSet<string> _facilityBridgeRequestedIcaos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _facilityBridgeReceivedIcaos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<uint, string> _facilityBridgeRequestIcaoByUserRequestId = new Dictionary<uint, string>();
        private int _facilityBridgeDirectRequestsSent;
        private int _facilityBridgeDataEndCount;
        private int _facilityBridgeExceptionCount;
        private string _facilityBridgeLastException = string.Empty;
        private string _facilityBridgeLastRequestMode = string.Empty;

        // C11C: runway geometry cache populated from SimConnect FacilityData.
        // The cache is raw evidence only; ACARS does not calculate official score.
        private readonly Dictionary<string, List<FacilityRunwayGeometry>> _facilityRunwaysByAirport = new Dictionary<string, List<FacilityRunwayGeometry>>(StringComparer.OrdinalIgnoreCase);
        private string _facilityRunwayGeometryStatus = string.Empty;

        // C11D1: taxi/parking payload discovery from SimConnect Facilities.
        // This first block only proves that MSFS sends taxiway/parking records and
        // exposes a concise diagnostic in the C11 bridge line. Geometry classification
        // is intentionally deferred to C11D2 after payload validation.
        private readonly Dictionary<string, int> _facilityTaxiParkingCountByAirport = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _facilityTaxiPointCountByAirport = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _facilityTaxiPathCountByAirport = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private string _facilityTaxiGeometryStatus = string.Empty;

        // C11D4: airport-local taxi geometry cache. MSFS taxi points/parking are
        // delivered as BIAS_X/BIAS_Z offsets from the airport facility reference.
        // We keep this as evidence only, not as official scoring.
        private readonly Dictionary<string, FacilityAirportGeometry> _facilityAirportsByAirport = new Dictionary<string, FacilityAirportGeometry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FacilityTaxiParkingGeometry>> _facilityTaxiParkingsByAirport = new Dictionary<string, List<FacilityTaxiParkingGeometry>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FacilityTaxiPointGeometry>> _facilityTaxiPointsByAirport = new Dictionary<string, List<FacilityTaxiPointGeometry>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<FacilityTaxiPathGeometry>> _facilityTaxiPathsByAirport = new Dictionary<string, List<FacilityTaxiPathGeometry>>(StringComparer.OrdinalIgnoreCase);

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
            EnableLvarReadBridge
            && ((IsMobiFlightAvailable && ProfileRequiresLvars(_lastProfile))
            || (IsPmdgSdkAvailable && ProfileRequiresPmdgSdk(_lastProfile != null ? _lastProfile.Code : string.Empty)));

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

                lock (ActiveInstanceLock)
                {
                    _activeFacilitiesInstance = this;
                }

                RegisterDataDefinitions();
                RegisterEvents();
                // C11A2: initialize facilities immediately as well as from OnRecvOpen.
                // Some MSFS/SimConnect managed wrappers can miss the open event timing,
                // leaving the UI at API no disponible even though the API exists.
                InitializeFacilityBridgeDiscovery(force: true);

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

                // C11D6: modo seguro para periféricos/SPAD.next.
                // Si el bridge LVAR está deshabilitado, NO inicializamos MobiFlight ni canales ClientData.
                // Esto mantiene ACARS read-only sobre telemetría estándar y evita tocar canales que pueden
                // compartir add-ons como SPAD.next, Logitech/Saitek o perfiles externos.
                if (EnableLvarReadBridge)
                {
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
                }
                else
                {
                    _mobiFlight = null;
                    Debug.WriteLine("[SimConnect] MobiFlight/LVAR bridge no inicializado: modo read-only/peripheral-safe activo.");
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
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PLANE ALTITUDE",                "feet",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "PRESSURE ALTITUDE",             "feet",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "RADIO HEIGHT",                  "feet",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GROUND ALTITUDE",               "feet",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
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

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL TOTAL CAPACITY WEIGHT", "pounds",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL LEFT QUANTITY WEIGHT",  "pounds",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL RIGHT QUANTITY WEIGHT", "pounds",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FUEL CENTER QUANTITY WEIGHT","pounds",           SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GENERAL ENG FUEL FLOW PPH:1","pounds per hour",  SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "GENERAL ENG FUEL FLOW PPH:2","pounds per hour",  SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TURB ENG N1:1",              "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TURB ENG N1:2",              "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);

            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "AUTOPILOT MASTER",           "Bool",             SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "FLAPS HANDLE PERCENT",       "percent",          SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TRAILING EDGE FLAPS LEFT PERCENT", "percent",     SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "TRAILING EDGE FLAPS RIGHT PERCENT", "percent",    SIMCONNECT_DATATYPE.FLOAT64, 0, sc);
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
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "COM ACTIVE FREQUENCY:1",   "Frequency BCD16", SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 54
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "COM STANDBY FREQUENCY:1",  "Frequency BCD16", SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 55
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "COM ACTIVE FREQUENCY:2",   "Frequency BCD16", SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 56
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "COM STANDBY FREQUENCY:2",  "Frequency BCD16", SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 57
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "G FORCE",                  "GForce", SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 58
            _simConnect.AddToDataDefinition(DataDefineId.AircraftData, "ELECTRICAL MAIN BUS VOLTAGE", "Volts", SIMCONNECT_DATATYPE.FLOAT64, 0, sc); // 59

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
            _simConnect.OnRecvFacilityData     += OnRecvFacilityData;
            _simConnect.OnRecvFacilityDataEnd  += OnRecvFacilityDataEnd;
            _simConnect.OnRecvFacilityMinimalList += OnRecvFacilityMinimalList;

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

            // C11A2: if the bridge has not initialized yet, retry from the
            // first real SimConnect aircraft sample. This avoids relying only
            // on OnRecvOpen timing.
            bool shouldRetryFacilities = false;
            lock (_facilityBridgeLock)
            {
                shouldRetryFacilities = !_facilityBridgeAvailable ||
                    (!_facilityDataReceived && _facilityBridgeLastRequestUtc.HasValue &&
                     (DateTime.UtcNow - _facilityBridgeLastRequestUtc.Value).TotalSeconds > 20);
            }

            if (shouldRetryFacilities)
                InitializeFacilityBridgeDiscovery(force: true);

            var simData = BuildSimData(raw, _lastEnv, profile);
            simData.IsConnected    = true;
            simData.SimulatorType  = DetectedSimulator == SimulatorType.None ? SimulatorType.MSFS2020 : DetectedSimulator;
            simData.Pause          = _isPaused;
            ApplyFacilityBridgeState(simData);

            string profileCode = profile?.Code ?? "MSFS_NATIVE";
            string profileDisplayName = profile?.DisplayName ?? "MSFS Native";
            simData.AircraftProfile = profileDisplayName;

            if (_pmdgSdk?.IsAvailable == true && ProfileRequiresPmdgSdk(profileCode))
            {
                _pmdgSdk.RequestSdkData();
                simData = _pmdgSdk.EnrichWithSdk(simData);
            }
            else if (EnableLvarReadBridge && profile != null && _mobiFlight?.IsAvailable == true && ProfileRequiresLvars(profile))
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
            InitializeFacilityBridgeDiscovery(force: true);
        }

        public static void RequestAirportFacilitiesForActive(params string[] icaos)
        {
            SimConnectService? active;
            lock (ActiveInstanceLock)
            {
                active = _activeFacilitiesInstance;
            }

            active?.RequestAirportFacilitiesByIcao(icaos);
        }

        private void RequestAirportFacilitiesByIcao(IEnumerable<string>? icaos)
        {
            if (_simConnect == null || icaos == null)
                return;

            var normalized = new List<string>();
            foreach (var icao in icaos)
            {
                var value = NormalizeFacilityIcao(icao);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (!normalized.Contains(value, StringComparer.OrdinalIgnoreCase))
                    normalized.Add(value);
            }

            if (normalized.Count == 0)
                return;

            InitializeFacilityBridgeDiscovery(force: false);

            try
            {
                EnsureAirportFacilityDefinition();
            }
            catch (Exception ex)
            {
                lock (_facilityBridgeLock)
                {
                    _facilityBridgeAvailable = false;
                    _facilityBridgeStatus = "airport_facility_define_failed: " + ex.Message;
                    _facilityBridgeLastRequestUtc = DateTime.UtcNow;
                    _facilityBridgeNearestAirports = string.Join(",", normalized);
                }
                Debug.WriteLine("[C11B Facilities] airport definition failed: " + ex);
                return;
            }

            var submitted = new List<string>();
            var requestModes = new List<string>();
            foreach (var icao in normalized)
            {
                lock (_facilityBridgeLock)
                {
                    if (_facilityBridgeRequestedIcaos.Contains(icao))
                        continue;

                    _facilityBridgeRequestedIcaos.Add(icao);
                }

                try
                {
                    var requestId = (FacilityRequestId)((int)FacilityRequestId.AirportFacilityData + _facilityBridgeDirectRequestsSent + submitted.Count + 1);
                    var requestIdValue = unchecked((uint)(int)requestId);
                    var requestMode = "LEGACY_OPEN_CLOSE_REGISTERED";

                    lock (_facilityBridgeLock)
                    {
                        _facilityBridgeRequestIcaoByUserRequestId[requestIdValue] = icao;
                    }

                    try
                    {
                        // C11B5: for airport ICAO requests, prefer the documented legacy
                        // RequestFacilityData path. EX1 is retained as fallback only.
                        _simConnect.RequestFacilityData(
                            FacilityDefineId.Airport,
                            requestId,
                            icao,
                            string.Empty);
                    }
                    catch (Exception legacyEx)
                    {
                        Debug.WriteLine("[C11B5 Facilities] RequestFacilityData legacy failed for " + icao + ": " + legacyEx.Message);
                        requestMode = "EX1_AFTER_LEGACY_FAIL";
                        _simConnect.RequestFacilityData_EX1(
                            FacilityDefineId.Airport,
                            requestId,
                            icao,
                            string.Empty,
                            (sbyte)SIMCONNECT_FACILITY_DATA_TYPE.AIRPORT);
                    }

                    submitted.Add(icao);
                    requestModes.Add(icao + ":" + requestMode + "#" + requestIdValue.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception requestEx)
                {
                    lock (_facilityBridgeLock)
                    {
                        _facilityBridgeStatus = "direct_request_failed_" + icao + ": " + requestEx.Message;
                        _facilityBridgeLastRequestUtc = DateTime.UtcNow;
                        _facilityBridgeAvailable = true;
                    }
                    Debug.WriteLine("[C11B Facilities] direct request failed for " + icao + ": " + requestEx);
                }
            }

            if (submitted.Count > 0)
            {
                lock (_facilityBridgeLock)
                {
                    _facilityBridgeAvailable = true;
                    _facilityBridgeStatus = "direct_airport_facility_requested:" + string.Join(",", submitted);
                    _facilityBridgeLastIcao = string.Join(",", _facilityBridgeRequestedIcaos.OrderBy(x => x));
                    _facilityBridgeLastRegion = string.Empty;
                    _facilityBridgeNearestAirports = string.Join(",", _facilityBridgeRequestedIcaos.OrderBy(x => x));
                    _facilityBridgeLastRequestUtc = DateTime.UtcNow;
                    _facilityBridgeLastRequestMode = string.Join(",", requestModes);
                    _facilityBridgeDirectRequestsSent += submitted.Count;
                }
            }
        }

        private void EnsureAirportFacilityDefinition()
        {
            if (_simConnect == null)
                throw new InvalidOperationException("SimConnect no inicializado");

            lock (_facilityBridgeLock)
            {
                if (_facilityAirportDefinitionConfigured)
                    return;
            }

            // C11B5: SimConnect FacilityData requires a strict OPEN/CLOSE tree.
            // The previous flat definition (ICAO/LATITUDE/LONGITUDE/ALTITUDE) can
            // leave MSFS waiting without FacilityData records. Keep this as a safe
            // diagnostic definition: airport counters + runway geometry essentials.
            var fields = new[]
            {
                "OPEN AIRPORT",
                "ICAO",
                "LATITUDE",
                "LONGITUDE",
                "ALTITUDE",
                "N_RUNWAYS",
                "N_TAXI_POINTS",
                "N_TAXI_PARKINGS",
                "N_TAXI_PATHS",
                "N_TAXI_NAMES",
                "OPEN RUNWAY",
                "PRIMARY_NUMBER",
                "PRIMARY_DESIGNATOR",
                "SECONDARY_NUMBER",
                "SECONDARY_DESIGNATOR",
                "LATITUDE",
                "LONGITUDE",
                "ALTITUDE",
                "HEADING",
                "LENGTH",
                "WIDTH",
                "SURFACE",
                "CLOSE RUNWAY",
                "OPEN TAXI_PARKING",
                "TYPE",
                "TAXI_POINT_TYPE",
                "NAME",
                "SUFFIX",
                "NUMBER",
                "ORIENTATION",
                "HEADING",
                "RADIUS",
                "BIAS_X",
                "BIAS_Z",
                "CLOSE TAXI_PARKING",
                "OPEN TAXI_POINT",
                "TYPE",
                "ORIENTATION",
                "BIAS_X",
                "BIAS_Z",
                "CLOSE TAXI_POINT",
                "OPEN TAXI_PATH",
                "TYPE",
                "WIDTH",
                "LEFT_HALF_WIDTH",
                "RIGHT_HALF_WIDTH",
                "WEIGHT",
                "RUNWAY_NUMBER",
                "RUNWAY_DESIGNATOR",
                "LEFT_EDGE",
                "LEFT_EDGE_LIGHTED",
                "RIGHT_EDGE",
                "RIGHT_EDGE_LIGHTED",
                "CENTER_LINE",
                "CENTER_LINE_LIGHTED",
                "START",
                "END",
                "NAME_INDEX",
                "CLOSE TAXI_PATH",
                "CLOSE AIRPORT"
            };

            foreach (var field in fields)
            {
                _simConnect.AddToFacilityDefinition(FacilityDefineId.Airport, field);
            }

            // Managed SimConnect must know how to marshal each FacilityData type.
            // Keep registrations minimal and fail-soft so unsupported enum members
            // do not break the whole ACARS session.
            try
            {
                _simConnect.RegisterFacilityDataDefineStruct<FacilityAirportDataStruct>(SIMCONNECT_FACILITY_DATA_TYPE.AIRPORT);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[C11B5 Facilities] Register AIRPORT struct failed: " + ex.Message);
            }

            try
            {
                _simConnect.RegisterFacilityDataDefineStruct<FacilityRunwayDataStruct>(SIMCONNECT_FACILITY_DATA_TYPE.RUNWAY);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[C11B5 Facilities] Register RUNWAY struct failed: " + ex.Message);
            }

            try
            {
                _simConnect.RegisterFacilityDataDefineStruct<FacilityTaxiParkingDataStruct>(SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PARKING);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[C11D1 Facilities] Register TAXI_PARKING struct failed: " + ex.Message);
            }

            try
            {
                _simConnect.RegisterFacilityDataDefineStruct<FacilityTaxiPointDataStruct>(SIMCONNECT_FACILITY_DATA_TYPE.TAXI_POINT);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[C11D1 Facilities] Register TAXI_POINT struct failed: " + ex.Message);
            }

            try
            {
                _simConnect.RegisterFacilityDataDefineStruct<FacilityTaxiPathDataStruct>(SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PATH);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[C11D1 Facilities] Register TAXI_PATH struct failed: " + ex.Message);
            }

            lock (_facilityBridgeLock)
            {
                _facilityAirportDefinitionConfigured = true;
                _facilityBridgeAvailable = true;
                _facilityBridgeStatus = "airport_facility_definition_ready_open_close_registered";
            }
        }

        private static string NormalizeFacilityIcao(string? value)
        {
            var text = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (text.Length < 3 || text == "----" || text == "????")
                return string.Empty;

            return text;
        }

        private void InitializeFacilityBridgeDiscovery(bool force = false)
        {
            if (_simConnect == null) return;

            lock (_facilityBridgeLock)
            {
                if (!force && _facilityBridgeInitAttempted && _facilityBridgeLastInitAttemptUtc.HasValue &&
                    (DateTime.UtcNow - _facilityBridgeLastInitAttemptUtc.Value).TotalSeconds < 20)
                {
                    return;
                }

                _facilityBridgeInitAttempted = true;
                _facilityBridgeLastInitAttemptUtc = DateTime.UtcNow;
            }

            try
            {
                lock (_facilityBridgeLock)
                {
                    _facilityBridgeAvailable = true;
                    _facilityBridgeStatus = "simconnect_facilities_methods_available";
                    _facilityBridgeLastRequestUtc = DateTime.UtcNow;
                }

                // First safe step: subscribe/request nearby airport minimal facilities.
                // Exact runway/taxiway definitions are intentionally deferred to C11B/C11C
                // after we confirm live facility payload behavior in the user's simulator.
                try
                {
                    _simConnect.SubscribeToFacilities_EX1(
                        SIMCONNECT_FACILITY_LIST_TYPE.AIRPORT,
                        FacilityRequestId.AirportsInRangeNew,
                        FacilityRequestId.AirportsInRangeOld);

                    lock (_facilityBridgeLock)
                    {
                        _facilityBridgeSubscribed = true;
                        _facilityBridgeStatus = "airport_facility_subscription_active";
                    }

                    // Also request a one-shot list immediately. Subscription only reports
                    // new/old in-range changes and may stay silent while already parked.
                    try
                    {
                        _simConnect.RequestFacilitiesList_EX1(
                            SIMCONNECT_FACILITY_LIST_TYPE.AIRPORT,
                            FacilityRequestId.AirportList);

                        lock (_facilityBridgeLock)
                        {
                            _facilityBridgeStatus = "airport_facility_subscription_active_list_requested";
                            _facilityBridgeLastRequestUtc = DateTime.UtcNow;
                        }
                    }
                    catch (Exception listAfterSubscribeEx)
                    {
                        Debug.WriteLine("[C11A2 Facilities] RequestFacilitiesList_EX1 after subscribe failed: " + listAfterSubscribeEx.Message);
                    }
                }
                catch (Exception subscribeEx)
                {
                    Debug.WriteLine("[C11A Facilities] SubscribeToFacilities_EX1 unavailable/failed: " + subscribeEx.Message);
                    try
                    {
                        _simConnect.RequestFacilitiesList_EX1(
                            SIMCONNECT_FACILITY_LIST_TYPE.AIRPORT,
                            FacilityRequestId.AirportList);

                        lock (_facilityBridgeLock)
                        {
                            _facilityBridgeSubscribed = false;
                            _facilityBridgeStatus = "airport_facility_list_requested";
                        }
                    }
                    catch (Exception listEx)
                    {
                        Debug.WriteLine("[C11A2 Facilities] RequestFacilitiesList_EX1 failed: " + listEx.Message);
                        try
                        {
                            _simConnect.RequestFacilitiesList(
                                SIMCONNECT_FACILITY_LIST_TYPE.AIRPORT,
                                FacilityRequestId.AirportList);

                            lock (_facilityBridgeLock)
                            {
                                _facilityBridgeAvailable = true;
                                _facilityBridgeSubscribed = false;
                                _facilityBridgeStatus = "airport_facility_list_requested_legacy";
                                _facilityBridgeLastRequestUtc = DateTime.UtcNow;
                            }
                        }
                        catch (Exception legacyListEx)
                        {
                            lock (_facilityBridgeLock)
                            {
                                _facilityBridgeAvailable = false;
                                _facilityBridgeStatus = "facility_list_request_failed: " + legacyListEx.Message;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                lock (_facilityBridgeLock)
                {
                    _facilityBridgeAvailable = false;
                    _facilityBridgeStatus = "facility_bridge_init_failed: " + ex.Message;
                }
                Debug.WriteLine("[C11A Facilities] init error: " + ex);
            }
        }

        private void OnRecvFacilityMinimalList(
            Microsoft.FlightSimulator.SimConnect.SimConnect sender,
            SIMCONNECT_RECV_FACILITY_MINIMAL_LIST data)
        {
            try
            {
                var names = new List<string>();
                if (data.rgData != null)
                {
                    foreach (var item in data.rgData)
                    {
                        var icao = ExtractFacilityMinimalIcao(item);
                        if (!string.IsNullOrWhiteSpace(icao) && !names.Contains(icao))
                            names.Add(icao);
                    }
                }

                lock (_facilityBridgeLock)
                {
                    _facilityDataReceived = true;
                    _facilityBridgeAvailable = true;
                    _facilityBridgeLastReceivedUtc = DateTime.UtcNow;
                    _facilityBridgeAirportCount = Math.Max(_facilityBridgeAirportCount, names.Count);
                    _facilityBridgeNearestAirports = string.Join(",", names.Take(12));
                    _facilityBridgeStatus = names.Count > 0
                        ? "airport_minimal_list_received"
                        : "airport_minimal_list_received_empty";
                    if (names.Count > 0)
                        _facilityBridgeLastIcao = names[0];
                }

                // C11D3: facilities must also work for diversion/alternate operations.
                // The route request covers origin/destination/alternate; the minimal
                // airport list covers any airport that becomes relevant around the
                // aircraft in real time. Request direct FacilityData for the nearest
                // airports, de-duplicated by RequestAirportFacilitiesByIcao.
                if (names.Count > 0)
                {
                    try
                    {
                        RequestAirportFacilitiesByIcao(names.Take(6).ToArray());
                    }
                    catch (Exception requestEx)
                    {
                        Debug.WriteLine("[C11D3 Facilities] nearest airport auto request skipped: " + requestEx.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[C11A Facilities] minimal list error: " + ex.Message);
                lock (_facilityBridgeLock)
                    _facilityBridgeStatus = "airport_minimal_list_error: " + ex.Message;
            }
        }

        private void OnRecvFacilityData(
            Microsoft.FlightSimulator.SimConnect.SimConnect sender,
            SIMCONNECT_RECV_FACILITY_DATA data)
        {
            try
            {
                var payloadIcao = ExtractFacilityDataIcao(data);
                var userRequestId = ExtractFacilityDataUserRequestId(data);
                if (string.IsNullOrWhiteSpace(payloadIcao) && userRequestId > 0)
                {
                    lock (_facilityBridgeLock)
                    {
                        string mappedIcao;
                        if (_facilityBridgeRequestIcaoByUserRequestId.TryGetValue(userRequestId, out mappedIcao))
                            payloadIcao = mappedIcao;
                    }
                }

                var geometrySummary = CaptureFacilityGeometry(data, payloadIcao, userRequestId);
                var dataSummary = BuildFacilityDataPayloadSummary(data);
                var dataTypeKey = GetFacilityDataTypeName(data);
                if (string.IsNullOrWhiteSpace(dataTypeKey)) dataTypeKey = data.Type.ToString(CultureInfo.InvariantCulture);

                var rawStatus = "facility_data_received_type_" + data.Type.ToString(CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(payloadIcao))
                    rawStatus = "facility_data_received_" + payloadIcao;
                if (!string.IsNullOrWhiteSpace(dataSummary))
                    rawStatus += "_" + dataSummary;
                if (!string.IsNullOrWhiteSpace(geometrySummary))
                    rawStatus += "_" + geometrySummary;

                // C11D4C: keep the long/raw status in LastData/XML, but expose a compact
                // bridge status so the WPF operational log is readable even if old UI code
                // still binds directly to FacilityBridgeStatus.
                var compactStatus = "facility data " +
                    (string.IsNullOrWhiteSpace(payloadIcao) ? "ICAO-ND" : payloadIcao.Trim().ToUpperInvariant()) +
                    " " + dataTypeKey.Trim().ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(geometrySummary))
                    compactStatus += " geometry";
                if (!string.IsNullOrWhiteSpace(dataSummary) && dataSummary.IndexOf("taxi_", StringComparison.OrdinalIgnoreCase) >= 0)
                    compactStatus += " taxi-payload";

                lock (_facilityBridgeLock)
                {
                    _facilityDataReceived = true;
                    _facilityBridgeAvailable = true;
                    _facilityBridgeLastReceivedUtc = DateTime.UtcNow;
                    _facilityBridgeRecordsReceived++;
                    _facilityBridgeLastDataStatus = rawStatus;
                    int currentTypeCount;
                    _facilityBridgeDataTypeCounts.TryGetValue(dataTypeKey, out currentTypeCount);
                    _facilityBridgeDataTypeCounts[dataTypeKey] = currentTypeCount + 1;

                    // Keep the last FacilityData status visible until a later request/control state overwrites it,
                    // but preserve the dedicated last-data field so UI/XML can still show taxi/runway evidence.
                    _facilityBridgeStatus = compactStatus;
                    if (!string.IsNullOrWhiteSpace(payloadIcao))
                    {
                        _facilityBridgeReceivedIcaos.Add(payloadIcao);
                        _facilityBridgeLastIcao = string.Join(",", _facilityBridgeReceivedIcaos.OrderBy(x => x));
                        _facilityBridgeAirportCount = Math.Max(_facilityBridgeAirportCount, _facilityBridgeReceivedIcaos.Count);
                        _facilityBridgeNearestAirports = string.Join(",", _facilityBridgeReceivedIcaos.OrderBy(x => x));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[C11A Facilities] data error: " + ex.Message);
            }
        }

        private void OnRecvFacilityDataEnd(
            Microsoft.FlightSimulator.SimConnect.SimConnect sender,
            SIMCONNECT_RECV_FACILITY_DATA_END data)
        {
            lock (_facilityBridgeLock)
            {
                _facilityBridgeLastReceivedUtc = DateTime.UtcNow;
                _facilityBridgeDataEndCount++;
                var pending = _facilityBridgeRequestedIcaos
                    .Where(icao => !_facilityBridgeReceivedIcaos.Contains(icao))
                    .OrderBy(icao => icao)
                    .ToList();

                if (!_facilityDataReceived)
                {
                    _facilityBridgeStatus = "facility_data_end_without_records_request_" + data.RequestId.ToString(CultureInfo.InvariantCulture);
                }
                else if (pending.Count > 0)
                {
                    _facilityBridgeStatus = "facility_data_end_pending:" + string.Join(",", pending);
                }
            }
        }

        private static uint ExtractFacilityDataUserRequestId(SIMCONNECT_RECV_FACILITY_DATA data)
        {
            try
            {
                var type = data.GetType();
                foreach (var name in new[] { "UserRequestId", "RequestId", "dwRequestID", "uRequestID" })
                {
                    var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        var value = field.GetValue(data);
                        if (value != null) return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                    }

                    var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null && prop.CanRead)
                    {
                        var value = prop.GetValue(data, null);
                        if (value != null) return Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                    }
                }
            }
            catch
            {
            }

            return 0;
        }

        private static string GetFacilityDataTypeName(SIMCONNECT_RECV_FACILITY_DATA data)
        {
            try
            {
                return Enum.IsDefined(typeof(SIMCONNECT_FACILITY_DATA_TYPE), (int)data.Type)
                    ? ((SIMCONNECT_FACILITY_DATA_TYPE)(int)data.Type).ToString()
                    : data.Type.ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string BuildFacilityDataPayloadSummary(SIMCONNECT_RECV_FACILITY_DATA data)
        {
            try
            {
                var items = data.Data == null ? 0 : data.Data.Length;
                var typeName = GetFacilityDataTypeName(data);
                var userRequestId = ExtractFacilityDataUserRequestId(data);
                var requestText = userRequestId > 0 ? ";req=" + userRequestId.ToString(CultureInfo.InvariantCulture) : string.Empty;
                return "type=" + typeName + ";items=" + items.ToString(CultureInfo.InvariantCulture) + requestText;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string ExtractFacilityMinimalIcao(object item)
        {
            try
            {
                if (item == null) return string.Empty;
                if (item is SIMCONNECT_FACILITY_MINIMAL minimal)
                    return ExtractIcaoString(minimal.icao);

                var field = item.GetType().GetField("icao", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                    return ExtractIcaoString(field.GetValue(item));
            }
            catch
            {
            }
            return string.Empty;
        }

        private static string ExtractFacilityDataIcao(SIMCONNECT_RECV_FACILITY_DATA data)
        {
            try
            {
                if (data == null || data.Data == null) return string.Empty;
                foreach (var item in data.Data)
                {
                    var icao = ExtractIcaoFromPayloadObject(item);
                    if (!string.IsNullOrWhiteSpace(icao)) return icao;
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        private static string ExtractIcaoFromPayloadObject(object item)
        {
            try
            {
                if (item == null) return string.Empty;
                foreach (var name in new[] { "Icao", "ICAO", "icao", "Ident", "ident" })
                {
                    var field = item.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        var value = field.GetValue(item);
                        if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                            return NormalizeFacilityIcao(value.ToString());
                    }

                    var prop = item.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null && prop.CanRead)
                    {
                        var value = prop.GetValue(item, null);
                        if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                            return NormalizeFacilityIcao(value.ToString());
                    }
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        private static string ExtractIcaoString(object value)
        {
            if (value == null) return string.Empty;
            var direct = value.ToString();
            if (!string.IsNullOrWhiteSpace(direct) && direct.IndexOf("SIMCONNECT_", StringComparison.OrdinalIgnoreCase) < 0)
                return direct.Trim();

            try
            {
                var type = value.GetType();
                foreach (var name in new[] { "Icao", "ICAO", "ident", "Ident", "Value", "value" })
                {
                    var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null)
                    {
                        var v = f.GetValue(value);
                        if (v != null && !string.IsNullOrWhiteSpace(v.ToString())) return v.ToString().Trim();
                    }
                    var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null && p.CanRead)
                    {
                        var v = p.GetValue(value, null);
                        if (v != null && !string.IsNullOrWhiteSpace(v.ToString())) return v.ToString().Trim();
                    }
                }
            }
            catch
            {
            }
            return string.Empty;
        }

        private string CaptureFacilityGeometry(SIMCONNECT_RECV_FACILITY_DATA data, string airportIcao, uint userRequestId)
        {
            if (data == null || data.Data == null || data.Data.Length == 0)
                return string.Empty;

            if (string.IsNullOrWhiteSpace(airportIcao) && userRequestId > 0)
            {
                lock (_facilityBridgeLock)
                {
                    string mapped;
                    if (_facilityBridgeRequestIcaoByUserRequestId.TryGetValue(userRequestId, out mapped))
                        airportIcao = mapped;
                }
            }

            airportIcao = NormalizeFacilityIcao(airportIcao);
            if (string.IsNullOrWhiteSpace(airportIcao))
                return string.Empty;

            var facilityTypeName = GetFacilityDataTypeName(data);
            if (facilityTypeName.IndexOf("TAXI_PARKING", StringComparison.OrdinalIgnoreCase) >= 0)
                return CaptureFacilityTaxiParkingPayload(data, airportIcao);
            if (facilityTypeName.IndexOf("TAXI_POINT", StringComparison.OrdinalIgnoreCase) >= 0)
                return CaptureFacilityTaxiPointPayload(data, airportIcao);
            if (facilityTypeName.IndexOf("TAXI_PATH", StringComparison.OrdinalIgnoreCase) >= 0)
                return CaptureFacilityTaxiPathPayload(data, airportIcao);
            if (facilityTypeName.IndexOf("RUNWAY", StringComparison.OrdinalIgnoreCase) < 0)
                return CaptureFacilityAirportPayload(data, airportIcao, facilityTypeName);

            var parsed = new List<FacilityRunwayGeometry>();
            foreach (var item in data.Data)
            {
                FacilityRunwayGeometry runway;
                if (TryExtractRunwayGeometry(item, airportIcao, out runway))
                    parsed.Add(runway);
            }

            if (parsed.Count == 0)
            {
                var unparsed = DescribeFacilityPayloadForDebug(data);
                lock (_facilityBridgeLock)
                {
                    _facilityRunwayGeometryStatus = "runway_payload_unparsed_" + airportIcao + ":" + unparsed;
                }

                return "runway_geometry_unparsed=" + airportIcao + ";" + unparsed;
            }

            lock (_facilityBridgeLock)
            {
                List<FacilityRunwayGeometry> list;
                if (!_facilityRunwaysByAirport.TryGetValue(airportIcao, out list))
                {
                    list = new List<FacilityRunwayGeometry>();
                    _facilityRunwaysByAirport[airportIcao] = list;
                }

                foreach (var runway in parsed)
                {
                    var existingIndex = list.FindIndex(existing =>
                        string.Equals(existing.PrimaryIdent, runway.PrimaryIdent, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.SecondaryIdent, runway.SecondaryIdent, StringComparison.OrdinalIgnoreCase) &&
                        Math.Abs(NormalizeHeading(existing.HeadingDeg) - NormalizeHeading(runway.HeadingDeg)) < 1.0d);

                    if (existingIndex >= 0)
                        list[existingIndex] = runway;
                    else
                        list.Add(runway);
                }

                var total = _facilityRunwaysByAirport.Values.Sum(runways => runways.Count);
                _facilityRunwayGeometryStatus = "runway_geometry_cached_" + airportIcao + ":" + list.Count.ToString(CultureInfo.InvariantCulture) + " rwy;total=" + total.ToString(CultureInfo.InvariantCulture);
            }

            var first = parsed[0];
            return "runway_geometry_cached=" + airportIcao + ":" + first.DisplayIdent + ";count=" + parsed.Count.ToString(CultureInfo.InvariantCulture);
        }


        private string CaptureFacilityAirportPayload(SIMCONNECT_RECV_FACILITY_DATA data, string airportIcao, string facilityTypeName)
        {
            if (facilityTypeName.IndexOf("AIRPORT", StringComparison.OrdinalIgnoreCase) < 0)
                return string.Empty;

            var items = data.Data == null ? 0 : data.Data.Length;
            var summary = "airport_payload=" + airportIcao + ";items=" + items.ToString(CultureInfo.InvariantCulture);
            try
            {
                if (items > 0 && data.Data[0] != null)
                {
                    var item = data.Data[0];
                    var lat = ReadDoubleMember(item, 1, "Latitude", "LATITUDE", "latitude");
                    var lon = ReadDoubleMember(item, 2, "Longitude", "LONGITUDE", "longitude");
                    var alt = ReadDoubleMember(item, 3, "AltitudeMeters", "ALTITUDE", "altitude");
                    var runways = ReadIntMember(item, 4, "RunwayCount", "N_RUNWAYS", "runwayCount");
                    var taxiPoints = ReadIntMember(item, 5, "TaxiPointCount", "N_TAXI_POINTS", "taxiPointCount");
                    var taxiParkings = ReadIntMember(item, 6, "TaxiParkingCount", "N_TAXI_PARKINGS", "taxiParkingCount");
                    var taxiPaths = ReadIntMember(item, 7, "TaxiPathCount", "N_TAXI_PATHS", "taxiPathCount");
                    summary += ";runways=" + runways.ToString(CultureInfo.InvariantCulture) +
                               ";parkings=" + taxiParkings.ToString(CultureInfo.InvariantCulture) +
                               ";points=" + taxiPoints.ToString(CultureInfo.InvariantCulture) +
                               ";paths=" + taxiPaths.ToString(CultureInfo.InvariantCulture);

                    if (IsPlausibleLatitude(lat) && IsPlausibleLongitude(lon))
                    {
                        lock (_facilityBridgeLock)
                        {
                            _facilityAirportsByAirport[airportIcao] = new FacilityAirportGeometry
                            {
                                AirportIcao = airportIcao,
                                Latitude = lat,
                                Longitude = lon,
                                AltitudeMeters = alt,
                                RunwayCount = runways,
                                TaxiPointCount = taxiPoints,
                                TaxiParkingCount = taxiParkings,
                                TaxiPathCount = taxiPaths
                            };
                        }
                    }
                }
            }
            catch
            {
            }

            return summary;
        }

        private string CaptureFacilityTaxiParkingPayload(SIMCONNECT_RECV_FACILITY_DATA data, string airportIcao)
        {
            var items = data.Data == null ? 0 : data.Data.Length;
            var summary = "taxi_parking_payload=" + airportIcao + ";items=" + items.ToString(CultureInfo.InvariantCulture);
            var parsed = new List<FacilityTaxiParkingGeometry>();

            for (var index = 0; index < items; index++)
            {
                var item = data.Data[index];
                if (item == null) continue;

                var type = ReadIntMember(item, 0, "Type", "TYPE", "type");
                var taxiPointType = ReadIntMember(item, 1, "TaxiPointType", "TAXI_POINT_TYPE", "taxiPointType");
                var name = ReadIntMember(item, 2, "Name", "NAME", "name");
                var suffix = ReadIntMember(item, 3, "Suffix", "SUFFIX", "suffix");
                var number = ReadIntMember(item, 4, "Number", "NUMBER", "number");
                var orientation = ReadIntMember(item, 5, "Orientation", "ORIENTATION", "orientation");
                var heading = ReadDoubleMember(item, 6, "HeadingDegrees", "HEADING", "heading", "Heading");
                var radius = ReadDoubleMember(item, 7, "RadiusMeters", "RADIUS", "radius", "Radius");
                var biasX = ReadDoubleMember(item, 8, "BiasX", "BIAS_X", "biasX");
                var biasZ = ReadDoubleMember(item, 9, "BiasZ", "BIAS_Z", "biasZ");

                parsed.Add(new FacilityTaxiParkingGeometry
                {
                    AirportIcao = airportIcao,
                    Index = index,
                    Type = type,
                    TaxiPointType = taxiPointType,
                    Name = name,
                    Suffix = suffix,
                    Number = number,
                    Orientation = orientation,
                    HeadingDeg = heading,
                    RadiusMeters = radius,
                    BiasX = biasX,
                    BiasZ = biasZ
                });
            }

            if (parsed.Count > 0)
            {
                var first = parsed[0];
                summary += ";first=type" + first.Type.ToString(CultureInfo.InvariantCulture) +
                           "/name" + first.Name.ToString(CultureInfo.InvariantCulture) +
                           "/suffix" + first.Suffix.ToString(CultureInfo.InvariantCulture) +
                           "/num" + first.Number.ToString(CultureInfo.InvariantCulture) +
                           "/hdg" + first.HeadingDeg.ToString("F0", CultureInfo.InvariantCulture) +
                           "/r" + first.RadiusMeters.ToString("F0", CultureInfo.InvariantCulture) +
                           "/x" + first.BiasX.ToString("F0", CultureInfo.InvariantCulture) +
                           "/z" + first.BiasZ.ToString("F0", CultureInfo.InvariantCulture);
            }

            lock (_facilityBridgeLock)
            {
                _facilityTaxiParkingCountByAirport[airportIcao] = Math.Max(GetDictionaryValue(_facilityTaxiParkingCountByAirport, airportIcao), items);
                if (parsed.Count > 0)
                    _facilityTaxiParkingsByAirport[airportIcao] = parsed;
                _facilityTaxiGeometryStatus = BuildFacilityTaxiGeometryStatusUnsafe(airportIcao);
            }
            return summary;
        }

        private string CaptureFacilityTaxiPointPayload(SIMCONNECT_RECV_FACILITY_DATA data, string airportIcao)
        {
            var items = data.Data == null ? 0 : data.Data.Length;
            var summary = "taxi_point_payload=" + airportIcao + ";items=" + items.ToString(CultureInfo.InvariantCulture);
            var parsed = new List<FacilityTaxiPointGeometry>();

            for (var index = 0; index < items; index++)
            {
                var item = data.Data[index];
                if (item == null) continue;

                var type = ReadIntMember(item, 0, "Type", "TYPE", "type");
                var orientation = ReadIntMember(item, 1, "Orientation", "ORIENTATION", "orientation");
                var biasX = ReadDoubleMember(item, 2, "BiasX", "BIAS_X", "biasX");
                var biasZ = ReadDoubleMember(item, 3, "BiasZ", "BIAS_Z", "biasZ");

                parsed.Add(new FacilityTaxiPointGeometry
                {
                    AirportIcao = airportIcao,
                    Index = index,
                    Type = type,
                    Orientation = orientation,
                    BiasX = biasX,
                    BiasZ = biasZ
                });
            }

            if (parsed.Count > 0)
            {
                var first = parsed[0];
                summary += ";first=type" + first.Type.ToString(CultureInfo.InvariantCulture) +
                           "/orient" + first.Orientation.ToString(CultureInfo.InvariantCulture) +
                           "/x" + first.BiasX.ToString("F0", CultureInfo.InvariantCulture) +
                           "/z" + first.BiasZ.ToString("F0", CultureInfo.InvariantCulture);
            }

            lock (_facilityBridgeLock)
            {
                _facilityTaxiPointCountByAirport[airportIcao] = Math.Max(GetDictionaryValue(_facilityTaxiPointCountByAirport, airportIcao), items);
                if (parsed.Count > 0)
                    _facilityTaxiPointsByAirport[airportIcao] = parsed;
                _facilityTaxiGeometryStatus = BuildFacilityTaxiGeometryStatusUnsafe(airportIcao);
            }
            return summary;
        }

        private string CaptureFacilityTaxiPathPayload(SIMCONNECT_RECV_FACILITY_DATA data, string airportIcao)
        {
            var items = data.Data == null ? 0 : data.Data.Length;
            var summary = "taxi_path_payload=" + airportIcao + ";items=" + items.ToString(CultureInfo.InvariantCulture);
            var parsed = new List<FacilityTaxiPathGeometry>();

            for (var index = 0; index < items; index++)
            {
                var item = data.Data[index];
                if (item == null) continue;

                var type = ReadIntMember(item, 0, "Type", "TYPE", "type");
                var width = ReadDoubleMember(item, 1, "WidthMeters", "WIDTH", "width", "Width");
                var leftHalf = ReadDoubleMember(item, 2, "LeftHalfWidthMeters", "LEFT_HALF_WIDTH", "leftHalfWidth");
                var rightHalf = ReadDoubleMember(item, 3, "RightHalfWidthMeters", "RIGHT_HALF_WIDTH", "rightHalfWidth");
                var weight = ReadIntMember(item, 4, "WeightLimitLbs", "WEIGHT", "weight");
                var runwayNumber = ReadIntMember(item, 5, "RunwayNumber", "RUNWAY_NUMBER", "runwayNumber");
                var runwayDesignator = ReadIntMember(item, 6, "RunwayDesignator", "RUNWAY_DESIGNATOR", "runwayDesignator");
                var start = ReadIntMember(item, 13, "Start", "START", "start");
                var end = ReadIntMember(item, 14, "End", "END", "end");
                var nameIndex = ReadIntMember(item, 15, "NameIndex", "NAME_INDEX", "nameIndex");

                parsed.Add(new FacilityTaxiPathGeometry
                {
                    AirportIcao = airportIcao,
                    Index = index,
                    Type = type,
                    WidthMeters = width,
                    LeftHalfWidthMeters = leftHalf,
                    RightHalfWidthMeters = rightHalf,
                    WeightLimitLbs = weight,
                    RunwayNumber = runwayNumber,
                    RunwayDesignator = runwayDesignator,
                    Start = start,
                    End = end,
                    NameIndex = nameIndex
                });
            }

            if (parsed.Count > 0)
            {
                var first = parsed[0];
                summary += ";first=type" + first.Type.ToString(CultureInfo.InvariantCulture) +
                           "/w" + first.WidthMeters.ToString("F0", CultureInfo.InvariantCulture) +
                           "/start" + first.Start.ToString(CultureInfo.InvariantCulture) +
                           "/end" + first.End.ToString(CultureInfo.InvariantCulture) +
                           "/nameIdx" + first.NameIndex.ToString(CultureInfo.InvariantCulture);
            }

            lock (_facilityBridgeLock)
            {
                _facilityTaxiPathCountByAirport[airportIcao] = Math.Max(GetDictionaryValue(_facilityTaxiPathCountByAirport, airportIcao), items);
                if (parsed.Count > 0)
                    _facilityTaxiPathsByAirport[airportIcao] = parsed;
                _facilityTaxiGeometryStatus = BuildFacilityTaxiGeometryStatusUnsafe(airportIcao);
            }
            return summary;
        }

        private static int GetDictionaryValue(Dictionary<string, int> dictionary, string key)
        {
            if (dictionary == null || string.IsNullOrWhiteSpace(key)) return 0;
            int value;
            return dictionary.TryGetValue(key, out value) ? value : 0;
        }

        private static int SumDictionaryValues(Dictionary<string, int> dictionary)
        {
            if (dictionary == null || dictionary.Count == 0) return 0;
            return dictionary.Values.Sum();
        }

        private string BuildFacilityBridgeTypeHistogramUnsafe()
        {
            if (_facilityBridgeDataTypeCounts == null || _facilityBridgeDataTypeCounts.Count == 0)
                return string.Empty;

            return string.Join(",", _facilityBridgeDataTypeCounts
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key)
                .Take(8)
                .Select(pair => pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture)));
        }

        private string BuildFacilityTaxiGeometryStatusUnsafe(string airportIcao)
        {
            var parking = GetDictionaryValue(_facilityTaxiParkingCountByAirport, airportIcao);
            var points = GetDictionaryValue(_facilityTaxiPointCountByAirport, airportIcao);
            var paths = GetDictionaryValue(_facilityTaxiPathCountByAirport, airportIcao);
            var parkingGeom = GetListCount(_facilityTaxiParkingsByAirport, airportIcao);
            var pointGeom = GetListCount(_facilityTaxiPointsByAirport, airportIcao);
            var pathGeom = GetListCount(_facilityTaxiPathsByAirport, airportIcao);
            var geometry = (parkingGeom > 0 || pointGeom > 0 || pathGeom > 0)
                ? ";geom=" + parkingGeom.ToString(CultureInfo.InvariantCulture) + "/" + pointGeom.ToString(CultureInfo.InvariantCulture) + "/" + pathGeom.ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            return "taxi_geometry_payload_cached_" + airportIcao +
                   ":parking=" + parking.ToString(CultureInfo.InvariantCulture) +
                   ";points=" + points.ToString(CultureInfo.InvariantCulture) +
                   ";paths=" + paths.ToString(CultureInfo.InvariantCulture) +
                   geometry;
        }

        private static int GetListCount<T>(Dictionary<string, List<T>> dictionary, string key)
        {
            if (dictionary == null || string.IsNullOrWhiteSpace(key)) return 0;
            List<T> list;
            return dictionary.TryGetValue(key, out list) && list != null ? list.Count : 0;
        }

        private static double CountAllGeometry<T>(Dictionary<string, List<T>> dictionary)
        {
            if (dictionary == null || dictionary.Count == 0) return 0d;
            var total = 0;
            foreach (var pair in dictionary)
            {
                if (pair.Value != null) total += pair.Value.Count;
            }
            return total;
        }

        private void ApplyFacilityTaxiGeometryState(SimData simData)
        {
            if (simData == null) return;

            List<FacilityTaxiParkingGeometry> parkings;
            List<FacilityTaxiPointGeometry> points;
            List<FacilityTaxiPathGeometry> paths;
            Dictionary<string, FacilityAirportGeometry> airports;

            lock (_facilityBridgeLock)
            {
                parkings = FlattenGeometry(_facilityTaxiParkingsByAirport);
                points = FlattenGeometry(_facilityTaxiPointsByAirport);
                paths = FlattenGeometry(_facilityTaxiPathsByAirport);
                airports = new Dictionary<string, FacilityAirportGeometry>(_facilityAirportsByAirport, StringComparer.OrdinalIgnoreCase);
            }

            simData.FacilityTaxiParkingGeometryCount = parkings.Count;
            simData.FacilityTaxiPointGeometryCount = points.Count;
            simData.FacilityTaxiPathGeometryCount = paths.Count;
            simData.FacilityTaxiGeometryVersion = "C11D4";

            if (!IsPlausibleLatitude(simData.Latitude) || !IsPlausibleLongitude(simData.Longitude))
                return;

            foreach (var parking in parkings)
                EnrichTaxiPointWithAirportReference(parking, airports);
            foreach (var point in points)
                EnrichTaxiPointWithAirportReference(point, airports);

            FacilityTaxiParkingGeometry bestParking = null;
            var bestParkingDistance = double.MaxValue;
            foreach (var parking in parkings)
            {
                if (!parking.HasLatLon) continue;
                var distance = DistanceMeters(simData.Latitude, simData.Longitude, parking.Latitude, parking.Longitude);
                if (distance < bestParkingDistance)
                {
                    bestParkingDistance = distance;
                    bestParking = parking;
                }
            }

            FacilityTaxiPointGeometry bestPoint = null;
            var bestPointDistance = double.MaxValue;
            foreach (var point in points)
            {
                if (!point.HasLatLon) continue;
                var distance = DistanceMeters(simData.Latitude, simData.Longitude, point.Latitude, point.Longitude);
                if (distance < bestPointDistance)
                {
                    bestPointDistance = distance;
                    bestPoint = point;
                }
            }

            FacilityTaxiPathMatch bestPath = null;
            foreach (var path in paths)
            {
                var match = BuildTaxiPathMatch(simData, path, points);
                if (match == null) continue;
                if (bestPath == null || match.DistanceMeters < bestPath.DistanceMeters)
                    bestPath = match;
            }

            var bestDistance = Math.Min(bestParkingDistance, Math.Min(bestPointDistance, bestPath != null ? bestPath.DistanceMeters : double.MaxValue));
            if (double.IsInfinity(bestDistance) || bestDistance == double.MaxValue)
                return;

            simData.FacilityTaxiGeometryAvailable = true;
            simData.FacilityNearestTaxiAirportIcao = bestParking != null
                ? bestParking.AirportIcao
                : (bestPoint != null ? bestPoint.AirportIcao : (bestPath != null ? bestPath.AirportIcao : string.Empty));
            simData.FacilityNearestTaxiParkingLabel = bestParking == null ? string.Empty : bestParking.DisplayLabel;
            simData.FacilityNearestTaxiParkingDistanceMeters = bestParkingDistance == double.MaxValue ? 0d : bestParkingDistance;
            simData.FacilityNearestTaxiPointDistanceMeters = bestPointDistance == double.MaxValue ? 0d : bestPointDistance;
            simData.FacilityNearestTaxiPathDistanceMeters = bestPath == null ? 0d : bestPath.DistanceMeters;
            simData.FacilityGateAreaCandidate = bestParking != null && bestParkingDistance <= Math.Max(65d, bestParking.RadiusMeters + 25d);
            simData.FacilityTaxiwayCandidate = (bestPath != null && bestPath.DistanceMeters <= Math.Max(45d, bestPath.WidthMeters + 20d)) ||
                                               (!simData.FacilityGateAreaCandidate && bestPoint != null && bestPointDistance <= 55d);

            var pathText = bestPath == null
                ? "path=N/D"
                : "path=" + bestPath.DistanceMeters.ToString("F0", CultureInfo.InvariantCulture) + "m" +
                  "/w" + bestPath.WidthMeters.ToString("F0", CultureInfo.InvariantCulture) +
                  "/" + bestPath.Start.ToString(CultureInfo.InvariantCulture) + "-" + bestPath.End.ToString(CultureInfo.InvariantCulture);
            var pointText = bestPointDistance == double.MaxValue ? "point=N/D" : "point=" + bestPointDistance.ToString("F0", CultureInfo.InvariantCulture) + "m";
            var parkingText = bestParking == null
                ? "parking=N/D"
                : "parking=" + bestParking.DisplayLabel + "/" + bestParkingDistance.ToString("F0", CultureInfo.InvariantCulture) + "m";

            simData.FacilityTaxiGeometrySummary =
                (string.IsNullOrWhiteSpace(simData.FacilityNearestTaxiAirportIcao) ? "ICAO N/D" : simData.FacilityNearestTaxiAirportIcao) +
                " " + parkingText + " " + pointText + " " + pathText +
                " gate=" + (simData.FacilityGateAreaCandidate ? "YES" : "NO") +
                " taxi=" + (simData.FacilityTaxiwayCandidate ? "YES" : "NO");
        }

        private static List<T> FlattenGeometry<T>(Dictionary<string, List<T>> dictionary)
        {
            var result = new List<T>();
            if (dictionary == null) return result;
            foreach (var pair in dictionary)
            {
                if (pair.Value != null) result.AddRange(pair.Value);
            }
            return result;
        }

        private static void EnrichTaxiPointWithAirportReference(FacilityAirportLocalPoint point, Dictionary<string, FacilityAirportGeometry> airports)
        {
            if (point == null || point.HasLatLon || airports == null) return;
            FacilityAirportGeometry airport;
            if (!airports.TryGetValue(point.AirportIcao ?? string.Empty, out airport) || airport == null)
                return;
            ConvertAirportBiasToLatLon(airport.Latitude, airport.Longitude, point.BiasX, point.BiasZ, out point.Latitude, out point.Longitude);
            point.HasLatLon = IsPlausibleLatitude(point.Latitude) && IsPlausibleLongitude(point.Longitude);
        }

        private static FacilityTaxiPathMatch BuildTaxiPathMatch(SimData simData, FacilityTaxiPathGeometry path, List<FacilityTaxiPointGeometry> points)
        {
            if (path == null || points == null || points.Count == 0) return null;
            var start = FindTaxiPoint(points, path.AirportIcao, path.Start);
            var end = FindTaxiPoint(points, path.AirportIcao, path.End);
            if (start == null || end == null || !start.HasLatLon || !end.HasLatLon) return null;

            double startNorth;
            double startEast;
            double endNorth;
            double endEast;
            ProjectRelativeMeters(simData.Latitude, simData.Longitude, start.Latitude, start.Longitude, out startNorth, out startEast);
            ProjectRelativeMeters(simData.Latitude, simData.Longitude, end.Latitude, end.Longitude, out endNorth, out endEast);

            var distance = DistancePointToSegmentMeters(0d, 0d, startEast, startNorth, endEast, endNorth);
            return new FacilityTaxiPathMatch
            {
                AirportIcao = path.AirportIcao,
                Start = path.Start,
                End = path.End,
                WidthMeters = path.WidthMeters > 0d ? path.WidthMeters : Math.Max(path.LeftHalfWidthMeters + path.RightHalfWidthMeters, 0d),
                DistanceMeters = distance
            };
        }

        private static FacilityTaxiPointGeometry FindTaxiPoint(List<FacilityTaxiPointGeometry> points, string airportIcao, int index)
        {
            foreach (var point in points)
            {
                if (point != null && point.Index == index && string.Equals(point.AirportIcao, airportIcao, StringComparison.OrdinalIgnoreCase))
                    return point;
            }
            return null;
        }

        private static void ConvertAirportBiasToLatLon(double airportLatDeg, double airportLonDeg, double biasXEastMeters, double biasZNorthMeters, out double latDeg, out double lonDeg)
        {
            const double earthRadiusMeters = 6371000d;
            latDeg = airportLatDeg + (biasZNorthMeters / earthRadiusMeters) * (180d / Math.PI);
            var cosLat = Math.Cos(ToRadians(airportLatDeg));
            if (Math.Abs(cosLat) < 0.000001d) cosLat = 0.000001d;
            lonDeg = airportLonDeg + (biasXEastMeters / (earthRadiusMeters * cosLat)) * (180d / Math.PI);
        }

        private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            double north;
            double east;
            ProjectRelativeMeters(lat1, lon1, lat2, lon2, out north, out east);
            return Math.Sqrt(north * north + east * east);
        }

        private static double DistancePointToSegmentMeters(double px, double py, double ax, double ay, double bx, double by)
        {
            var dx = bx - ax;
            var dy = by - ay;
            var len2 = dx * dx + dy * dy;
            if (len2 <= 0.000001d)
            {
                var ex = px - ax;
                var ey = py - ay;
                return Math.Sqrt(ex * ex + ey * ey);
            }

            var t = ((px - ax) * dx + (py - ay) * dy) / len2;
            if (t < 0d) t = 0d;
            if (t > 1d) t = 1d;
            var cx = ax + t * dx;
            var cy = ay + t * dy;
            var ox = px - cx;
            var oy = py - cy;
            return Math.Sqrt(ox * ox + oy * oy);
        }

        private static bool TryExtractRunwayGeometry(object item, string airportIcao, out FacilityRunwayGeometry runway)
        {
            runway = null;
            if (item == null)
                return false;

            var primaryNumber = ReadIntMember(item, 0, "PrimaryNumber", "PRIMARY_NUMBER", "primaryNumber", "primary_number", "primary_number");
            var primaryDesignator = ReadIntMember(item, 1, "PrimaryDesignator", "PRIMARY_DESIGNATOR", "primaryDesignator", "primary_designator");
            var secondaryNumber = ReadIntMember(item, 2, "SecondaryNumber", "SECONDARY_NUMBER", "secondaryNumber", "secondary_number");
            var secondaryDesignator = ReadIntMember(item, 3, "SecondaryDesignator", "SECONDARY_DESIGNATOR", "secondaryDesignator", "secondary_designator");
            var lat = ReadDoubleMember(item, 4, "Latitude", "LATITUDE", "latitude", "lat");
            var lon = ReadDoubleMember(item, 5, "Longitude", "LONGITUDE", "longitude", "lon", "lng");
            var altitudeMeters = ReadDoubleMember(item, 6, "AltitudeMeters", "ALTITUDE", "altitudeMeters", "altitude", "alt");
            var heading = ReadDoubleMember(item, 7, "HeadingDegrees", "HEADING", "heading", "Heading", "headingDegrees");
            var length = ReadDoubleMember(item, 8, "LengthMeters", "LENGTH", "length", "Length", "lengthMeters");
            var width = ReadDoubleMember(item, 9, "WidthMeters", "WIDTH", "width", "Width", "widthMeters");
            var surface = ReadIntMember(item, 10, "Surface", "SURFACE", "surface");

            // Defensive fallback for managed wrapper variants where runway identifiers
            // arrive as zero but geometry fields are valid. The ident can be derived
            // from heading; geometry must still be plausible.
            if (!IsPlausibleLatitude(lat) || !IsPlausibleLongitude(lon) || length < 150d || width < 5d)
                return false;

            var primaryIdent = BuildRunwayIdent(primaryNumber, primaryDesignator);
            var secondaryIdent = BuildRunwayIdent(secondaryNumber, secondaryDesignator);
            if (string.IsNullOrWhiteSpace(primaryIdent))
                primaryIdent = RunwayIdentFromHeading(heading);
            if (string.IsNullOrWhiteSpace(secondaryIdent))
                secondaryIdent = RunwayIdentFromHeading(heading + 180d);

            runway = new FacilityRunwayGeometry
            {
                AirportIcao = airportIcao,
                PrimaryIdent = primaryIdent,
                SecondaryIdent = secondaryIdent,
                Latitude = lat,
                Longitude = lon,
                AltitudeMeters = altitudeMeters,
                HeadingDeg = NormalizeHeading(heading),
                LengthMeters = length,
                WidthMeters = width,
                Surface = surface
            };
            return true;
        }

        private void ApplyFacilityRunwayGeometryState(SimData simData)
        {
            if (simData == null)
                return;

            List<FacilityRunwayGeometry> runways;
            string status;
            lock (_facilityBridgeLock)
            {
                runways = _facilityRunwaysByAirport.Values.SelectMany(list => list).ToList();
                status = _facilityRunwayGeometryStatus ?? string.Empty;
            }

            simData.FacilityRunwayGeometryCount = runways.Count;
            simData.FacilityRunwayGeometryStatus = status;
            simData.FacilityRunwayGeometryVersion = "C11C";

            if (runways.Count == 0 || !IsPlausibleLatitude(simData.Latitude) || !IsPlausibleLongitude(simData.Longitude))
                return;

            FacilityRunwayMatch best = null;
            foreach (var runway in runways)
            {
                var match = BuildRunwayMatch(simData, runway);
                if (match == null)
                    continue;
                if (best == null || match.DistanceMeters < best.DistanceMeters)
                    best = match;
            }

            if (best == null)
                return;

            simData.FacilityRunwayGeometryAvailable = true;
            simData.FacilityNearestRunwayAirportIcao = best.Runway.AirportIcao;
            simData.FacilityNearestRunwayIdent = best.ActiveIdent;
            simData.FacilityNearestRunwayReciprocalIdent = best.ReciprocalIdent;
            simData.FacilityNearestRunwayHeadingDeg = best.ActiveHeadingDeg;
            simData.FacilityNearestRunwayLengthMeters = best.Runway.LengthMeters;
            simData.FacilityNearestRunwayWidthMeters = best.Runway.WidthMeters;
            simData.FacilityNearestRunwayDistanceMeters = best.DistanceMeters;
            simData.FacilityRunwayLateralOffsetMeters = best.LateralMeters;
            simData.FacilityRunwayLongitudinalOffsetMeters = best.LongitudinalMeters;
            simData.FacilityRunwayHeadingErrorDeg = best.HeadingErrorDeg;
            simData.FacilityRunwayDistanceFromThresholdMeters = best.DistanceFromActiveThresholdMeters;
            simData.FacilityOnRunwayCandidate = best.OnRunwayCandidate;
            simData.FacilityRunwayAlignedCandidate = best.AlignedCandidate;
            simData.FacilityTouchdownZoneCandidate = best.TouchdownZoneCandidate;
            simData.FacilityRunwayGeometrySummary =
                best.Runway.AirportIcao + " RWY " + best.ActiveIdent +
                " dist=" + best.DistanceMeters.ToString("F0", CultureInfo.InvariantCulture) + "m" +
                " lat=" + best.LateralMeters.ToString("F0", CultureInfo.InvariantCulture) + "m" +
                " thr=" + best.DistanceFromActiveThresholdMeters.ToString("F0", CultureInfo.InvariantCulture) + "m" +
                " hdgErr=" + best.HeadingErrorDeg.ToString("F0", CultureInfo.InvariantCulture) + "deg";
        }

        private static FacilityRunwayMatch BuildRunwayMatch(SimData simData, FacilityRunwayGeometry runway)
        {
            if (runway == null || runway.LengthMeters < 150d || runway.WidthMeters < 5d)
                return null;

            double northMeters;
            double eastMeters;
            ProjectRelativeMeters(runway.Latitude, runway.Longitude, simData.Latitude, simData.Longitude, out northMeters, out eastMeters);

            var primaryHeading = NormalizeHeading(runway.HeadingDeg);
            var reciprocalHeading = NormalizeHeading(primaryHeading + 180d);
            var heading = NormalizeHeading(simData.Heading);
            var primaryError = SmallestHeadingDifference(heading, primaryHeading);
            var reciprocalError = SmallestHeadingDifference(heading, reciprocalHeading);
            var useReciprocal = reciprocalError < primaryError;
            var activeHeading = useReciprocal ? reciprocalHeading : primaryHeading;
            var activeIdent = useReciprocal ? runway.SecondaryIdent : runway.PrimaryIdent;
            var reciprocalIdent = useReciprocal ? runway.PrimaryIdent : runway.SecondaryIdent;
            var headingError = useReciprocal ? reciprocalError : primaryError;

            var axisEast = Math.Sin(ToRadians(primaryHeading));
            var axisNorth = Math.Cos(ToRadians(primaryHeading));
            var along = eastMeters * axisEast + northMeters * axisNorth;
            var cross = eastMeters * axisNorth - northMeters * axisEast;
            var absCross = Math.Abs(cross);
            var absAlong = Math.Abs(along);
            var lateralOverflow = Math.Max(0d, absCross - (runway.WidthMeters / 2d));
            var longitudinalOverflow = Math.Max(0d, absAlong - (runway.LengthMeters / 2d));
            var distanceToRunway = Math.Sqrt(lateralOverflow * lateralOverflow + longitudinalOverflow * longitudinalOverflow);
            var onRunway = absCross <= (runway.WidthMeters / 2d + 30d) && absAlong <= (runway.LengthMeters / 2d + 90d);
            var aligned = headingError <= 15d;
            var distanceFromThreshold = useReciprocal
                ? (runway.LengthMeters / 2d) - along
                : along + (runway.LengthMeters / 2d);
            var tdzLimit = Math.Min(900d, Math.Max(450d, runway.LengthMeters * 0.35d));
            var touchdownZone = onRunway && aligned && distanceFromThreshold >= -100d && distanceFromThreshold <= tdzLimit;

            return new FacilityRunwayMatch
            {
                Runway = runway,
                ActiveIdent = string.IsNullOrWhiteSpace(activeIdent) ? RunwayIdentFromHeading(activeHeading) : activeIdent,
                ReciprocalIdent = string.IsNullOrWhiteSpace(reciprocalIdent) ? RunwayIdentFromHeading(activeHeading + 180d) : reciprocalIdent,
                ActiveHeadingDeg = activeHeading,
                HeadingErrorDeg = headingError,
                LateralMeters = cross,
                LongitudinalMeters = along,
                DistanceMeters = distanceToRunway,
                DistanceFromActiveThresholdMeters = distanceFromThreshold,
                OnRunwayCandidate = onRunway,
                AlignedCandidate = aligned,
                TouchdownZoneCandidate = touchdownZone
            };
        }

        private static double ReadDoubleMember(object item, int fallbackIndex, params string[] names)
        {
            object value;
            if (TryReadMember(item, out value, fallbackIndex, names) && value != null)
            {
                try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
                catch { }
            }
            return 0d;
        }

        private static double ReadDoubleMember(object item, params string[] names)
        {
            return ReadDoubleMember(item, -1, names);
        }

        private static int ReadIntMember(object item, int fallbackIndex, params string[] names)
        {
            object value;
            if (TryReadMember(item, out value, fallbackIndex, names) && value != null)
            {
                try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
                catch { }
            }
            return 0;
        }

        private static int ReadIntMember(object item, params string[] names)
        {
            return ReadIntMember(item, -1, names);
        }

        private static bool TryReadMember(object item, out object value, int fallbackIndex, params string[] names)
        {
            value = null;
            if (item == null)
                return false;

            var directValues = item as object[];
            if (directValues != null && fallbackIndex >= 0 && fallbackIndex < directValues.Length)
            {
                value = directValues[fallbackIndex];
                return true;
            }

            var array = item as Array;
            if (array != null && fallbackIndex >= 0 && fallbackIndex < array.Length)
            {
                value = array.GetValue(fallbackIndex);
                return true;
            }

            if (names == null)
                return false;

            var type = item.GetType();
            foreach (var name in names)
            {
                var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (field != null)
                {
                    value = field.GetValue(item);
                    return true;
                }

                var prop = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop != null && prop.CanRead)
                {
                    value = prop.GetValue(item, null);
                    return true;
                }
            }
            return false;
        }

        private static bool TryReadMember(object item, out object value, params string[] names)
        {
            return TryReadMember(item, out value, -1, names);
        }

        private static string DescribeFacilityPayloadForDebug(SIMCONNECT_RECV_FACILITY_DATA data)
        {
            try
            {
                if (data == null || data.Data == null || data.Data.Length == 0)
                    return "payload_empty";

                var item = data.Data[0];
                if (item == null)
                    return "payload_item_null";

                var parts = new List<string>();
                parts.Add("item=" + ShortTypeName(item.GetType()));

                var values = item as object[];
                if (values != null)
                {
                    parts.Add("arrayLen=" + values.Length.ToString(CultureInfo.InvariantCulture));
                    parts.Add("array0=" + SummarizeValues(values.Take(12)));
                    return string.Join(";", parts);
                }

                var array = item as Array;
                if (array != null)
                {
                    var first = new List<object>();
                    for (var i = 0; i < Math.Min(12, array.Length); i++)
                        first.Add(array.GetValue(i));
                    parts.Add("arrayLen=" + array.Length.ToString(CultureInfo.InvariantCulture));
                    parts.Add("array0=" + SummarizeValues(first));
                    return string.Join(";", parts);
                }

                var members = new List<string>();
                foreach (var field in item.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Take(16))
                {
                    object value = null;
                    try { value = field.GetValue(item); } catch { }
                    members.Add(field.Name + "=" + ShortValue(value));
                }

                foreach (var prop in item.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(p => p.CanRead).Take(8))
                {
                    object value = null;
                    try { value = prop.GetValue(item, null); } catch { }
                    members.Add(prop.Name + "=" + ShortValue(value));
                }

                if (members.Count == 0)
                    members.Add("no_public_members");

                parts.Add(string.Join(",", members));
                return string.Join(";", parts);
            }
            catch (Exception ex)
            {
                return "payload_debug_error=" + ex.GetType().Name;
            }
        }

        private static string ShortTypeName(Type type)
        {
            if (type == null) return "null";
            var name = type.FullName ?? type.Name;
            if (name.Length <= 80) return name;
            return name.Substring(name.Length - 80);
        }

        private static string SummarizeValues(IEnumerable<object> values)
        {
            if (values == null) return string.Empty;
            return string.Join(",", values.Select(ShortValue));
        }

        private static string ShortValue(object value)
        {
            if (value == null) return "null";
            try
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                text = text.Replace(";", ":").Replace("\r", " ").Replace("\n", " ");
                if (text.Length > 32) text = text.Substring(0, 32);
                return text;
            }
            catch
            {
                return value.GetType().Name;
            }
        }

        private static string BuildRunwayIdent(int number, int designator)
        {
            if (number <= 0 || number > 36)
                return string.Empty;

            var suffix = string.Empty;
            switch (designator)
            {
                case 1: suffix = "C"; break;
                case 2: suffix = "L"; break;
                case 3: suffix = "R"; break;
                case 4: suffix = "W"; break;
                case 5: suffix = "A"; break;
                case 6: suffix = "B"; break;
            }

            return number.ToString("00", CultureInfo.InvariantCulture) + suffix;
        }

        private static string RunwayIdentFromHeading(double headingDeg)
        {
            var normalized = NormalizeHeading(headingDeg);
            var number = (int)Math.Round(normalized / 10d, MidpointRounding.AwayFromZero);
            if (number <= 0) number = 36;
            if (number > 36) number = 36;
            return number.ToString("00", CultureInfo.InvariantCulture);
        }

        private static void ProjectRelativeMeters(double originLatDeg, double originLonDeg, double latDeg, double lonDeg, out double northMeters, out double eastMeters)
        {
            const double earthRadiusMeters = 6371000d;
            var originLatRad = ToRadians(originLatDeg);
            var dLat = ToRadians(latDeg - originLatDeg);
            var dLon = ToRadians(lonDeg - originLonDeg);
            northMeters = dLat * earthRadiusMeters;
            eastMeters = dLon * earthRadiusMeters * Math.Cos(originLatRad);
        }

        private static double NormalizeHeading(double headingDeg)
        {
            var value = headingDeg % 360d;
            if (value < 0d) value += 360d;
            return value;
        }

        private static double SmallestHeadingDifference(double a, double b)
        {
            var diff = Math.Abs(NormalizeHeading(a) - NormalizeHeading(b)) % 360d;
            return diff > 180d ? 360d - diff : diff;
        }

        private static double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180d;
        }

        private static bool IsPlausibleLatitude(double value)
        {
            return value >= -90d && value <= 90d && Math.Abs(value) > 0.0001d;
        }

        private static bool IsPlausibleLongitude(double value)
        {
            return value >= -180d && value <= 180d && Math.Abs(value) > 0.0001d;
        }

        private sealed class FacilityAirportGeometry
        {
            public string AirportIcao = string.Empty;
            public double Latitude;
            public double Longitude;
            public double AltitudeMeters;
            public int RunwayCount;
            public int TaxiPointCount;
            public int TaxiParkingCount;
            public int TaxiPathCount;
        }

        private abstract class FacilityAirportLocalPoint
        {
            public string AirportIcao = string.Empty;
            public int Index;
            public double BiasX;
            public double BiasZ;
            public double Latitude;
            public double Longitude;
            public bool HasLatLon;
        }

        private sealed class FacilityTaxiParkingGeometry : FacilityAirportLocalPoint
        {
            public int Type;
            public int TaxiPointType;
            public int Name;
            public int Suffix;
            public int Number;
            public int Orientation;
            public double HeadingDeg;
            public double RadiusMeters;

            public string DisplayLabel
            {
                get
                {
                    var number = Number > 0 ? Number.ToString(CultureInfo.InvariantCulture) : (Index + 1).ToString(CultureInfo.InvariantCulture);
                    return "P" + number;
                }
            }
        }

        private sealed class FacilityTaxiPointGeometry : FacilityAirportLocalPoint
        {
            public int Type;
            public int Orientation;
        }

        private sealed class FacilityTaxiPathGeometry
        {
            public string AirportIcao = string.Empty;
            public int Index;
            public int Type;
            public double WidthMeters;
            public double LeftHalfWidthMeters;
            public double RightHalfWidthMeters;
            public int WeightLimitLbs;
            public int RunwayNumber;
            public int RunwayDesignator;
            public int Start;
            public int End;
            public int NameIndex;
        }

        private sealed class FacilityTaxiPathMatch
        {
            public string AirportIcao = string.Empty;
            public int Start;
            public int End;
            public double WidthMeters;
            public double DistanceMeters;
        }

        private sealed class FacilityRunwayGeometry
        {
            public string AirportIcao = string.Empty;
            public string PrimaryIdent = string.Empty;
            public string SecondaryIdent = string.Empty;
            public double Latitude;
            public double Longitude;
            public double AltitudeMeters;
            public double HeadingDeg;
            public double LengthMeters;
            public double WidthMeters;
            public int Surface;

            public string DisplayIdent
            {
                get { return PrimaryIdent + "/" + SecondaryIdent; }
            }
        }

        private sealed class FacilityRunwayMatch
        {
            public FacilityRunwayGeometry Runway;
            public string ActiveIdent = string.Empty;
            public string ReciprocalIdent = string.Empty;
            public double ActiveHeadingDeg;
            public double HeadingErrorDeg;
            public double LateralMeters;
            public double LongitudinalMeters;
            public double DistanceMeters;
            public double DistanceFromActiveThresholdMeters;
            public bool OnRunwayCandidate;
            public bool AlignedCandidate;
            public bool TouchdownZoneCandidate;
        }

        // ──────────────────────────────────────────────────────────────────────
        // BUILD SIM DATA
        // ──────────────────────────────────────────────────────────────────────

        private void ApplyFacilityBridgeState(SimData simData)
        {
            if (simData == null) return;
            lock (_facilityBridgeLock)
            {
                simData.FacilityBridgeAvailable = _facilityBridgeAvailable;
                simData.FacilityBridgeSubscribed = _facilityBridgeSubscribed;
                simData.FacilityDataReceived = _facilityDataReceived;
                simData.FacilityDataSource = _facilityBridgeAvailable ? "SIMCONNECT_FACILITIES" : (_facilityBridgeInitAttempted ? "SIMCONNECT_FACILITIES_INIT_FAILED" : "UNAVAILABLE");
                simData.FacilityBridgeStatus = _facilityBridgeStatus ?? string.Empty;
                simData.FacilityBridgeLastIcao = _facilityBridgeLastIcao ?? string.Empty;
                simData.FacilityBridgeLastRegion = _facilityBridgeLastRegion ?? string.Empty;
                var requested = _facilityBridgeRequestedIcaos.OrderBy(x => x).ToList();
                var received = _facilityBridgeReceivedIcaos.OrderBy(x => x).ToList();
                var pending = requested.Where(icao => !_facilityBridgeReceivedIcaos.Contains(icao)).OrderBy(x => x).ToList();
                var secondsSinceRequest = _facilityBridgeLastRequestUtc.HasValue
                    ? Math.Max(0d, (DateTime.UtcNow - _facilityBridgeLastRequestUtc.Value).TotalSeconds)
                    : 0d;

                if (pending.Count > 0 && !_facilityDataReceived && secondsSinceRequest >= 20d && _facilityBridgeStatus.StartsWith("direct_airport_facility_requested", StringComparison.OrdinalIgnoreCase))
                {
                    _facilityBridgeStatus = "facility_data_timeout_waiting:" + string.Join(",", pending);
                }

                simData.FacilityBridgeRecordsReceived = _facilityBridgeRecordsReceived;
                simData.FacilityBridgeAirportCount = _facilityBridgeAirportCount;
                simData.FacilityBridgeNearestAirports = _facilityBridgeNearestAirports ?? string.Empty;
                simData.FacilityBridgeRequestedIcaos = string.Join(",", requested);
                simData.FacilityBridgeReceivedIcaos = string.Join(",", received);
                simData.FacilityBridgePendingIcaos = string.Join(",", pending);
                simData.FacilityBridgeDirectRequestsSent = _facilityBridgeDirectRequestsSent;
                simData.FacilityBridgeDataEndCount = _facilityBridgeDataEndCount;
                simData.FacilityBridgeExceptionCount = _facilityBridgeExceptionCount;
                simData.FacilityBridgeLastException = _facilityBridgeLastException ?? string.Empty;
                simData.FacilityBridgeLastRequestMode = _facilityBridgeLastRequestMode ?? string.Empty;
                simData.FacilityBridgeAwaitingResponse = pending.Count > 0 && !_facilityDataReceived;
                simData.FacilityBridgeSecondsSinceRequest = secondsSinceRequest;
                simData.FacilityBridgeLastRequestUtc = _facilityBridgeLastRequestUtc;
                simData.FacilityBridgeLastReceivedUtc = _facilityBridgeLastReceivedUtc;
                simData.FacilityBridgeLastDataStatus = _facilityBridgeLastDataStatus ?? string.Empty;
                simData.FacilityBridgeDataTypeHistogram = BuildFacilityBridgeTypeHistogramUnsafe();
                simData.FacilityTaxiGeometryStatus = _facilityTaxiGeometryStatus ?? string.Empty;
                simData.FacilityTaxiParkingPayloadCount = SumDictionaryValues(_facilityTaxiParkingCountByAirport);
                simData.FacilityTaxiPointPayloadCount = SumDictionaryValues(_facilityTaxiPointCountByAirport);
                simData.FacilityTaxiPathPayloadCount = SumDictionaryValues(_facilityTaxiPathCountByAirport);
                simData.FacilityTaxiGeometryVersion = "C11D4";
                simData.FacilityBridgeVersion = "C11D3";
            }

            ApplyFacilityRunwayGeometryState(simData);
            ApplyFacilityTaxiGeometryState(simData);
        }

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

            var flapPercent = ResolveFlapPercent(r, profile);
            var flapDeployThreshold = Math.Max(0.01, profile?.FlapDeployThresholdPercent ?? 0.01);

            double indicatedAirspeed = r.IndicatedAirspeed;
            double groundSpeed = r.GroundSpeed;
            if (ProfileIsMaddog(profileCode) && r.OnGround != 0)
            {
                if (indicatedAirspeed < 10.0) indicatedAirspeed = 0.0;
                if (groundSpeed < 3.0) groundSpeed = 0.0;
            }

            var electricalMainBusVoltage = NormalizeVoltage(r.ElectricalMainBusVoltage);
            var batteryMasterOn = r.BatteryMaster != 0;
            var avionicsMasterOn = r.AvionicsMaster != 0;

            // Black Square C208: el simvar nativo ELECTRICAL MASTER BATTERY y algunos buses
            // pueden quedar activos por HOT BATTERY BUS aun con BATTERY MASTER OFF.
            // Se excluye bateria/bus de Cold & Dark y se conserva avionica como senal confiable.
            if (ProfileIsBlackSquareC208(profileCode))
            {
                // Black Square C208 deja buses/simvars nativos vivos aunque la cabina este apagada.
                // No alimentar Cold & Dark con estas banderas; se evalua por motores + luces.
                batteryMasterOn = false;
                avionicsMasterOn = false;
            }

            Debug.WriteLine($"[SimConnect] Fuel={fuelTotal:F0} lbs / {fuelKg:F0} kg  N1={n1Eng1:F1}/{n1Eng2:F1} Squawk={squawk} " +
                $"Profile={profile?.DisplayName ?? "MSFS Native"} Code={profileCode} " +
                $"Lights: Nav={navOn} Beacon={beaconOn} Landing={landingOn} Taxi={taxiOn} Strobe={strobeOn} " +
                $"Eng1={r.EngineOneCombustion != 0} BattRaw={r.BatteryMaster != 0} Batt={batteryMasterOn} AvionicsRaw={r.AvionicsMaster != 0} Avionics={avionicsMasterOn} MainBusV={electricalMainBusVoltage:F1} Door={r.DoorPercent:F0}%");

            if (ProfileIsMaddog(profileCode))
            {
                Debug.WriteLine($"[MADDOG RAW] IAS={indicatedAirspeed:F1} GS={groundSpeed:F1} AP={Convert.ToInt32(Math.Round(r.AutopilotActive))} XPDR={Convert.ToInt32(Math.Round(r.TransponderState))} Seatbelt={Convert.ToInt32(Math.Round(r.SeatBeltSign))} NoSmoking={Convert.ToInt32(Math.Round(r.NoSmokingSign))} APU={r.ApuPct:F1} Bleed={Convert.ToInt32(Math.Round(r.BleedAirOn))} DoorPct={r.DoorPercent:F1}");
            }

            var simconnectXpdrRaw = NormalizeTransponderState(r.TransponderAvailable != 0, Convert.ToInt32(Math.Round(r.TransponderState)));
            if (ProfileIsBlackSquareC208(profileCode))
            {
                simconnectXpdrRaw = NormalizeBlackSquareC208TransponderState(simconnectXpdrRaw, squawk, Convert.ToInt32(Math.Round(r.TransponderState)));
            }
            var gForce = Math.Abs(r.GForce) > 0.01 ? r.GForce : 1.0;
            var detection = BuildDetectionMetadata(r.Title ?? string.Empty, profile);
            var resolvedAltitude = ResolveAltitude(r, e);

            return new SimData
            {
                AircraftTitle      = r.Title ?? "Unknown",
                Latitude           = r.Latitude,
                Longitude          = r.Longitude,
                AltitudeFeet       = resolvedAltitude.AltitudeMslFt,
                AltitudeAGL        = resolvedAltitude.AltitudeAglFt,
                AltitudeMslFeet    = resolvedAltitude.AltitudeMslFt,
                AltitudeAglFeet    = resolvedAltitude.AltitudeAglFt,
                IndicatedAltitudeFeet = resolvedAltitude.IndicatedAltitudeFt,
                TrueAltitudeFeet      = resolvedAltitude.TrueAltitudeFt,
                PressureAltitudeFeet  = resolvedAltitude.PressureAltitudeFt,
                RadioAltitudeFeet     = resolvedAltitude.RadioAltitudeFt,
                GroundAltitudeFeet    = resolvedAltitude.GroundElevationFt,
                GroundElevationFeet   = resolvedAltitude.GroundElevationFt,
                FlightLevel           = resolvedAltitude.FlightLevel,
                DisplayAltitudeMode   = resolvedAltitude.DisplayMode,
                DisplayAltitudeText   = resolvedAltitude.DisplayText,
                AltitudeSource        = resolvedAltitude.Source,
                IsAltitudeReliable    = resolvedAltitude.IsReliable,
                TransitionAltitudeFeet = resolvedAltitude.TransitionAltitudeFt,
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
                LandingG           = gForce,
                GForce             = gForce,

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
                FlapsDeployed      = flapPercent > flapDeployThreshold,
                FlapsPercent       = flapPercent,
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
                QNH                = resolvedAltitude.QnhHpa,
                QnhInHg            = Math.Round(resolvedAltitude.QnhHpa * 0.029529983071445d, 2),
                IsRaining          = e.PrecipState > 0,

                // ── Campos extendidos (arquitectura SUR Air) ──
                EngineOneRunning   = r.EngineOneCombustion != 0,
                EngineTwoRunning   = r.EngineTwoCombustion != 0,
                BatteryMasterOn    = batteryMasterOn,
                AvionicsMasterOn   = avionicsMasterOn,
                ElectricalMainBusVoltage = electricalMainBusVoltage,
                DoorOpen           = r.DoorPercent > 5.0,   // umbral 5% (igual que SUR Air)
                DetectedProfileCode = profileCode,
                AircraftTypeCode   = detection.AircraftTypeCode,
                AircraftVariantCode = detection.AircraftVariantCode,
                AddonSource        = detection.AddonSource,
                ProfileCode        = detection.ProfileCode,
                DetectionConfidence = detection.DetectionConfidence,
                DetectionReason    = detection.DetectionReason,
                DetectionSource    = detection.DetectionSource,
                MatchedTitle       = detection.MatchedTitle,
                MatchedPattern     = detection.MatchedPattern,
                FallbackUsed       = detection.FallbackUsed,
                ProfileStatus      = detection.ProfileStatus,
                Com1FrequencyMhz        = NormalizeComFrequency(r.Com1FrequencyMhz),
                Com1StandbyFrequencyMhz = NormalizeComFrequency(r.Com1StandbyFrequencyMhz),
                Com2FrequencyMhz        = NormalizeComFrequency(r.Com2FrequencyMhz),
                Com2StandbyFrequencyMhz = NormalizeComFrequency(r.Com2StandbyFrequencyMhz),
            };
        }

        private sealed class ResolvedAltitudeData
        {
            public double AltitudeMslFt { get; set; }
            public double AltitudeAglFt { get; set; }
            public double GroundElevationFt { get; set; }
            public double IndicatedAltitudeFt { get; set; }
            public double TrueAltitudeFt { get; set; }
            public double PressureAltitudeFt { get; set; }
            public double RadioAltitudeFt { get; set; }
            public string FlightLevel { get; set; } = string.Empty;
            public string DisplayMode { get; set; } = "MSL";
            public string DisplayText { get; set; } = string.Empty;
            public string Source { get; set; } = "simconnect";
            public bool IsReliable { get; set; }
            public double TransitionAltitudeFt { get; set; } = DefaultTransitionAltitudeFeet;
            public double QnhHpa { get; set; } = 1013.25d;
        }

        private const double DefaultTransitionAltitudeFeet = 10000d;

        private static ResolvedAltitudeData ResolveAltitude(AircraftDataStruct r, EnvironmentDataStruct e)
        {
            var qnhHpa = IsPlausibleQnh(e.SeaLevelPressure) ? e.SeaLevelPressure : 1013.25d;
            var rawOnGround = r.OnGround != 0;
            var indicated = IsPlausibleAltitude(r.AltitudeFeet) ? r.AltitudeFeet : 0d;
            var trueMsl = IsPlausibleAltitude(r.TrueAltitudeFeet) ? r.TrueAltitudeFeet : indicated;
            var pressure = IsPlausibleAltitude(r.PressureAltitudeFeet)
                ? r.PressureAltitudeFeet
                : ComputePressureAltitude(trueMsl, qnhHpa);
            var radio = IsPlausibleAgl(r.RadioAltitudeFeet) ? r.RadioAltitudeFeet : 0d;
            var groundElevation = IsPlausibleGroundElevation(r.GroundAltitudeFeet) ? r.GroundAltitudeFeet : 0d;

            var agl = IsPlausibleAgl(r.AltitudeAGL) ? r.AltitudeAGL : 0d;
            var aglSource = "PLANE ALT ABOVE GROUND";

            if (!IsPlausibleAgl(agl) || agl <= 0d)
            {
                if (radio > 0d && radio < 5000d)
                {
                    agl = radio;
                    aglSource = "RADIO HEIGHT";
                }
                else if (groundElevation != 0d && IsPlausibleAltitude(trueMsl))
                {
                    agl = Math.Max(0d, trueMsl - groundElevation);
                    aglSource = "MSL_MINUS_GROUND_ALTITUDE";
                }
            }

            var inferredOnGround = rawOnGround || (r.GroundSpeed < 3d && r.IndicatedAirspeed < 35d && agl <= 8d);
            if (inferredOnGround || agl <= 5d)
            {
                agl = 0d;
            }

            if (groundElevation == 0d && IsPlausibleAltitude(trueMsl))
            {
                groundElevation = Math.Max(-1500d, trueMsl - agl);
            }

            var isReliable = IsPlausibleAltitude(trueMsl)
                && IsPlausibleAgl(agl)
                && Math.Abs((trueMsl - groundElevation) - agl) < 2500d;

            var mode = "MSL";
            var display = Math.Round(trueMsl, 0).ToString("F0", CultureInfo.InvariantCulture);
            var flightLevel = string.Empty;

            if (inferredOnGround)
            {
                mode = "GROUND";
                display = Math.Round(trueMsl, 0).ToString("F0", CultureInfo.InvariantCulture);
            }
            else if (pressure >= DefaultTransitionAltitudeFeet)
            {
                var fl = Math.Max(0, (int)Math.Round(pressure / 100d, MidpointRounding.AwayFromZero));
                flightLevel = "FL" + fl.ToString("000", CultureInfo.InvariantCulture);
                mode = "FL";
                display = flightLevel;
            }

            return new ResolvedAltitudeData
            {
                AltitudeMslFt = trueMsl,
                AltitudeAglFt = agl,
                GroundElevationFt = groundElevation,
                IndicatedAltitudeFt = indicated,
                TrueAltitudeFt = trueMsl,
                PressureAltitudeFt = pressure,
                RadioAltitudeFt = radio,
                FlightLevel = flightLevel,
                DisplayMode = mode,
                DisplayText = display,
                Source = "MSL=PLANE ALTITUDE;AGL=" + aglSource,
                IsReliable = isReliable,
                TransitionAltitudeFt = DefaultTransitionAltitudeFeet,
                QnhHpa = qnhHpa
            };
        }

        private static double ComputePressureAltitude(double mslFt, double qnhHpa)
        {
            if (!IsPlausibleAltitude(mslFt)) return 0d;
            if (!IsPlausibleQnh(qnhHpa)) return mslFt;
            return mslFt + ((1013.25d - qnhHpa) * 30d);
        }

        private static bool IsPlausibleQnh(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 850d && value <= 1100d;
        }

        private static bool IsPlausibleAltitude(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= -1500d && value <= 70000d;
        }

        private static bool IsPlausibleAgl(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= -50d && value <= 60000d;
        }

        private static bool IsPlausibleGroundElevation(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= -1500d && value <= 30000d;
        }

        private sealed class DetectionMetadata
        {
            public string AircraftTypeCode { get; set; } = string.Empty;
            public string AircraftVariantCode { get; set; } = string.Empty;
            public string AddonSource { get; set; } = "UNKNOWN";
            public string ProfileCode { get; set; } = "MSFS_NATIVE";
            public string DetectionConfidence { get; set; } = "unknown";
            public string DetectionReason { get; set; } = string.Empty;
            public string DetectionSource { get; set; } = "simconnect_title";
            public string MatchedTitle { get; set; } = string.Empty;
            public string MatchedPattern { get; set; } = string.Empty;
            public bool FallbackUsed { get; set; }
            public string ProfileStatus { get; set; } = "unknown_profile";
        }

        private static DetectionMetadata BuildDetectionMetadata(string aircraftTitle, AircraftProfile? profile)
        {
            var title = (aircraftTitle ?? string.Empty).Trim();
            var code = (profile?.Code ?? "MSFS_NATIVE").Trim();
            var addon = string.IsNullOrWhiteSpace(profile?.AddonProvider) ? "UNKNOWN" : profile.AddonProvider.Trim().ToUpperInvariant();
            var isFallback = string.Equals(code, "MSFS_NATIVE", StringComparison.OrdinalIgnoreCase);
            var matchedPattern = ResolveMatchedPattern(title, profile);
            var exact = IsExactProfileMatch(title, profile);
            var containsMatch = !exact && !string.IsNullOrWhiteSpace(matchedPattern);

            var confidence = isFallback ? "fallback" : exact ? "exact" : containsMatch ? "high" : "medium";
            var profileStatus = isFallback ? "fallback_profile" :
                (string.Equals(profile?.CapabilityAuditState, "PARCIAL", StringComparison.OrdinalIgnoreCase) ? "partial_profile" : "exact_profile");
            var reason = isFallback
                ? "No profile title match. Using conservative fallback profile."
                : exact
                    ? "Exact aircraft title matched profile."
                    : containsMatch
                        ? "Aircraft title matched profile pattern."
                        : "Profile resolved with medium confidence.";

            return new DetectionMetadata
            {
                AircraftTypeCode = ResolveTypeCodeFromProfile(code),
                AircraftVariantCode = code,
                AddonSource = addon,
                ProfileCode = code,
                DetectionConfidence = confidence,
                DetectionReason = reason,
                DetectionSource = "simconnect_title",
                MatchedTitle = title,
                MatchedPattern = matchedPattern,
                FallbackUsed = isFallback,
                ProfileStatus = profileStatus
            };
        }

        private static string ResolveTypeCodeFromProfile(string profileCode)
        {
            var code = (profileCode ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(code) || code == "MSFS_NATIVE")
            {
                return string.Empty;
            }

            var idx = code.IndexOf('_');
            return idx > 0 ? code.Substring(0, idx) : code;
        }

        private static bool IsExactProfileMatch(string title, AircraftProfile? profile)
        {
            if (profile == null || profile.ExactTitles == null) return false;
            var normalized = (title ?? string.Empty).Trim();
            if (normalized.Length == 0) return false;

            foreach (var item in profile.ExactTitles)
            {
                var candidate = (item ?? string.Empty).Trim();
                if (candidate.Length == 0) continue;
                if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveMatchedPattern(string title, AircraftProfile? profile)
        {
            if (profile == null) return string.Empty;
            var normalized = (title ?? string.Empty).Trim();
            if (normalized.Length == 0) return string.Empty;

            if (profile.ExactTitles != null)
            {
                foreach (var item in profile.ExactTitles)
                {
                    var candidate = (item ?? string.Empty).Trim();
                    if (candidate.Length == 0) continue;
                    if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate;
                    }
                }
            }

            if (profile.Matches != null)
            {
                foreach (var item in profile.Matches)
                {
                    var candidate = (item ?? string.Empty).Trim();
                    if (candidate.Length == 0 || candidate == "*") continue;
                    if (normalized.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return candidate;
                    }
                }
            }

            return string.Empty;
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

            lock (_facilityBridgeLock)
            {
                var recentFacilityRequest = _facilityBridgeLastRequestUtc.HasValue
                    && (DateTime.UtcNow - _facilityBridgeLastRequestUtc.Value).TotalSeconds <= 90d;

                if (!recentFacilityRequest)
                    return;

                var exceptionCode = data.dwException.ToString(CultureInfo.InvariantCulture);

                // C11B3: SimConnect exception 26 is SIMCONNECT_EXCEPTION_ALREADY_SUBSCRIBED.
                // It can occur when the bridge retries SubscribeToFacilities while the subscription
                // is already active. Treat it as a benign signal and keep waiting for the direct
                // RequestFacilityData response instead of overwriting the UI with a hard exception.
                if (data.dwException == 26)
                {
                    _facilityBridgeAvailable = true;
                    _facilityBridgeSubscribed = true;
                    _facilityBridgeLastReceivedUtc = DateTime.UtcNow;
                    _facilityBridgeLastException = "benign_already_subscribed=26";

                    var pending = _facilityBridgeRequestedIcaos
                        .Where(x => !_facilityBridgeReceivedIcaos.Contains(x))
                        .OrderBy(x => x)
                        .ToList();

                    _facilityBridgeStatus = pending.Count > 0
                        ? "facility_subscription_already_active_waiting:" + string.Join(",", pending)
                        : "facility_subscription_already_active";
                    return;
                }

                _facilityBridgeExceptionCount++;
                _facilityBridgeAvailable = true;
                _facilityBridgeLastReceivedUtc = DateTime.UtcNow;
                _facilityBridgeLastException = "dwException=" + exceptionCode;
                _facilityBridgeStatus = "facility_exception_after_request:" + _facilityBridgeLastException;
            }
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

            lock (ActiveInstanceLock)
            {
                if (ReferenceEquals(_activeFacilitiesInstance, this))
                    _activeFacilitiesInstance = null;
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

        private static bool ProfileIsBlackSquareC208(string profileCode) =>
            string.Equals(profileCode, "C208_BLACKSQUARE", StringComparison.OrdinalIgnoreCase);

        private static double ResolveFlapPercent(AircraftDataStruct raw, AircraftProfile? profile)
        {
            if (profile != null && !profile.SupportsFlapsRead)
            {
                return 0.0;
            }

            var source = (profile?.FlapSource ?? "trailing_left_percent").Trim().ToLowerInvariant();
            var handle = NormalizePercent(raw.FlapsHandlePercent);
            var left = NormalizePercent(raw.FlapsLeftPercent);
            var right = NormalizePercent(raw.FlapsRightPercent);

            switch (source)
            {
                case "handle_percent":
                    return handle;
                case "trailing_right_percent":
                    return right;
                case "trailing_avg_percent":
                    return (left + right) / 2.0;
                case "auto":
                    if (left > 0.01 || right > 0.01) return (left + right) / 2.0;
                    return handle;
                case "trailing_left_percent":
                default:
                    return left;
            }
        }

        private static double NormalizePercent(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
            if (value < 0.0) return 0.0;
            if (value > 100.0) return 100.0;
            return value;
        }

        private static double NormalizeVoltage(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
            if (value < 0.0 || value > 80.0) return 0.0;
            return value;
        }

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
                // El master AP nativo (baseData.AutopilotActive) manda sobre modos CMD/CWS.
                // Si master esta OFF, no debemos mantener AP activo por estados residuales.
                baseData.AutopilotActive = baseData.AutopilotActive && autopilotOn;

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
                if (!EnableLvarReadBridge) return;
                if (_simConnect == null || string.IsNullOrWhiteSpace(command)) return;
                var payload = new ClientDataString256 { Value = command };
                _simConnect.SetClientData(ClientDataId.DefaultCommand, ClientDefId.CommandString, SIMCONNECT_CLIENT_DATA_SET_FLAG.DEFAULT, 0, payload);
                Debug.WriteLine("[MobiFlight] CMD(default) => " + command);
            }

            private void SendClientCommand(string command)
            {
                if (!EnableLvarReadBridge) return;
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
                    baseData.AutopilotActive = baseData.AutopilotActive && genericApRaw > 0.5;

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


        private static double NormalizeComFrequency(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d) return 0d;

            // Ya viene en MHz normal: 121.700. COM voice usable range in MSFS/IFR ops is
            // roughly 118.000-136.975; values like 100.000 are decoder noise and must
            // not satisfy PIC or pollute EventTimeline.
            if (value >= 118d && value <= 137d) return Math.Round(value, 3);

            // Algunos bridges/addons devuelven Hz: 121700000
            if (value >= 118000000d && value <= 137000000d)
                return Math.Round(value / 1000000d, 3);

            // Algunos devuelven kHz o MHz*100 / MHz*1000: 12170 o 121700
            if (value >= 118000d && value <= 137000d)
                return Math.Round(value / 1000d, 3);

            if (value >= 11800d && value <= 13700d)
                return Math.Round(value / 100d, 3);

            // SimConnect clásico con Frequency BCD16 puede entregar decimal del hex BCD.
            // Ej.: COM2 121.700 => 0x2170, que llega como 8560 decimal.
            var raw = Convert.ToInt32(Math.Round(value));
            var hex = raw.ToString("X").PadLeft(4, '0');
            if (hex.Length >= 4 && hex.All(char.IsDigit))
            {
                var d1 = hex[0] - '0';
                var d2 = hex[1] - '0';
                var d3 = hex[2] - '0';
                var d4 = hex[3] - '0';
                var mhz = 100d + (d1 * 10d) + d2 + (d3 / 10d) + (d4 / 100d);
                if (mhz >= 118d && mhz <= 137d)
                    return Math.Round(mhz, 3);
            }

            return 0d;
        }

        private static int NormalizeTransponderState(bool available, int rawState)
        {
            if (!available) return -1;
            if (rawState < 0 || rawState > 5) return -1;
            return rawState;
        }

        private static int NormalizeBlackSquareC208TransponderState(int normalizedState, int squawk, int rawState)
        {
            // C208 BlackSquare puede reportar availability/state inconsistente por SimVar,
            // aun con XPDR operativo y código válido. Si hay squawk real, forzamos ALT.
            if (normalizedState >= 3) return normalizedState;
            if (squawk <= 0) return normalizedState;
            if (normalizedState == -1 || normalizedState == 0 || normalizedState == 1 || rawState == 1)
            {
                return 3;
            }
            return normalizedState;
        }
    }
}
