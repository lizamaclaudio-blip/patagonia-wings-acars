#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

            if (_mobiFlight?.IsAvailable == true && ProfileRequiresLvars(profileCode))
            {
                _mobiFlight.RequestLvarsForProfile(profileCode);
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

            // ── Perfil de aeronave normalizado ────────────────────────────────
            var profileCode = profile?.Code ?? AircraftNormalizationService.ResolveCode(r.Title ?? string.Empty);

            Debug.WriteLine($"[SimConnect] Fuel={fuelTotal:F0} lbs / {fuelKg:F0} kg  N1={n1Eng1:F1}/{n1Eng2:F1} Squawk={squawk} " +
                $"Profile={profile?.DisplayName ?? "MSFS Native"} Code={profileCode} " +
                $"Lights: Nav={navOn} Beacon={beaconOn} Landing={landingOn} Taxi={taxiOn} Strobe={strobeOn} " +
                $"Eng1={r.EngineOneCombustion != 0} Batt={r.BatteryMaster != 0} Avionics={r.AvionicsMaster != 0} Door={r.DoorPercent:F0}%");

            return new SimData
            {
                AircraftTitle      = r.Title ?? "Unknown",
                Latitude           = r.Latitude,
                Longitude          = r.Longitude,
                AltitudeFeet       = r.AltitudeFeet,
                AltitudeAGL        = r.AltitudeAGL,
                IndicatedAirspeed  = r.IndicatedAirspeed,
                GroundSpeed        = r.GroundSpeed,
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
                // Simplificado a ON/OFF para la UI.
                TransponderStateRaw     = (r.TransponderAvailable != 0 && r.TransponderState >= 3) ? 1 : 0,
                TransponderCharlieMode  = r.TransponderAvailable != 0 && r.TransponderState >= 3,

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

        private static bool ProfileRequiresLvars(string profileCode) =>
            profileCode == "A20N_FBW"
            || profileCode == "A319_FENIX"
            || profileCode == "A320_FENIX"
            || profileCode == "A321_FENIX"
            || profileCode == "A339_HEADWIND";

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

            public void RequestLvarsForProfile(string profileCode)
            {
                if (!_isAvailable || _simConnect == null || _registeredProfile == profileCode) return;
                var lvars = (profileCode == "A319_FENIX" || profileCode == "A320_FENIX" || profileCode == "A321_FENIX") ? FENIX_LVARS : A32NX_LVARS;
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
                    _registeredProfile = profileCode;
                    Debug.WriteLine($"[MobiFlight] LVARs registradas para '{profileCode}'");
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
                    var lvars = (_registeredProfile == "A319_FENIX" || _registeredProfile == "A320_FENIX" || _registeredProfile == "A321_FENIX") ? FENIX_LVARS : A32NX_LVARS;
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
