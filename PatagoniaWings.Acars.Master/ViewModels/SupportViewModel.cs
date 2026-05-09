using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Collections.ObjectModel;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Core.Services;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public class SupportViewModel : ViewModelBase
    {
        private readonly UiPreferencesPayload _preferences;
        private bool _alwaysVisible;
        private bool _useKg;
        private string _simulatorIp = "127.0.0.1";
        private bool _enableInSimHud;
        private int _localHudPort;
        private int _hudUpdateRateHz;
        private bool _hudOnlyInFlight;
        private string _statusMessage = string.Empty;
        private string _updateInstalledVersion = string.Empty;
        private string _updateLatestVersion = string.Empty;
        private string _updateChannel = string.Empty;
        private string _updateManifestUrl = string.Empty;
        private string _updateDownloadUrl = string.Empty;
        private string _updateLastError = string.Empty;
        private string _updateState = "Sin diagnostico";
        private string _telemetryInspectorSummary = "Sin telemetria en vivo";
        private string _telemetryInspectorLastExport = "Sin exportaciones";
        private string _telemetryInspectorLastMark = "Sin pruebas marcadas";
        private readonly ObservableCollection<TelemetryInspectorRow> _telemetryInspectorRows = new ObservableCollection<TelemetryInspectorRow>();

        public SupportViewModel()
        {
            _preferences = UiPreferencesStore.Load();
            _alwaysVisible = _preferences.AlwaysVisible;
            _useKg = _preferences.UseKg;
            _simulatorIp = string.IsNullOrWhiteSpace(_preferences.SimulatorIp)
                ? "127.0.0.1"
                : _preferences.SimulatorIp.Trim();
            _enableInSimHud = _preferences.EnableInSimHud;
            _localHudPort = SanitizeHudPort(_preferences.LocalHudPort);
            _hudUpdateRateHz = SanitizeHudRate(_preferences.HudUpdateRateHz);
            _hudOnlyInFlight = _preferences.HudOnlyInFlight;

            LogoutCommand = new RelayCommand(() => RequestLogout?.Invoke());
            BackCommand = new RelayCommand(() => RequestBack?.Invoke());
            RetryLastPirepCommand = new AsyncRelayCommand(async _ => await RetryLastPirepAsync());
            OpenDebugFolderCommand = new RelayCommand(() => OpenFolder(GetDebugFolderPath()));
            OpenDataFolderCommand = new RelayCommand(() => OpenFolder(GetDataFolderPath()));
            OpenAppFolderCommand = new RelayCommand(() => OpenFolder(AppDomain.CurrentDomain.BaseDirectory));
            OpenExportsFolderCommand = new RelayCommand(() => OpenFolder(GetExportsFolderPath()));
            OpenConfigFolderCommand = new RelayCommand(() => OpenFolder(GetConfigFolderPath()));
            OpenHudPackageFolderCommand = new RelayCommand(OpenHudPackageFolder);
            CopyHudBridgeUrlCommand = new RelayCommand(CopyHudBridgeUrl);
            InstallHudToCommunityCommand = new RelayCommand(InstallHudToCommunity);
            ProbeHudBridgeCommand = new AsyncRelayCommand(async _ => await ProbeHudBridgeAsync());
            CheckUpdateCommand = new AsyncRelayCommand(async _ => await CheckUpdateAsync());
            DownloadUpdateCommand = new AsyncRelayCommand(async _ => await DownloadUpdateAsync());
            OpenUpdateLogsCommand = new RelayCommand(OpenUpdateLogs);
            CopyUpdateDiagnosticCommand = new RelayCommand(CopyUpdateDiagnostic);
            RefreshProfileCommand = new RelayCommand(RefreshTelemetryInspector);
            ResetSampleSessionCommand = new RelayCommand(ResetTelemetryInspectorSession);
            ExportSnapshotJsonCommand = new RelayCommand(ExportTelemetrySnapshotJson);
            ExportSessionCsvCommand = new RelayCommand(ExportTelemetrySessionCsv);
            CopyTelemetryDiagnosticCommand = new RelayCommand(CopyTelemetryDiagnostic);
            MarkAircraftTestedCommand = new RelayCommand(MarkAircraftTested);

            AcarsContext.Runtime.Changed += () =>
            {
                RefreshTelemetryInspector();
            };

            UpdateService.UpdateStatusChanged += message =>
            {
                StatusMessage = message;
                UpdateState = message;
            };
            UpdateService.UpdateFailed += message =>
            {
                UpdateLastError = message;
                StatusMessage = message;
            };
            SavePreferences();
            _ = CheckUpdateAsync();
        }

        public event Action<bool>? AlwaysVisibleChanged;

        public Action? RequestBack { get; set; }
        public Action? RequestLogout { get; set; }

        public ICommand LogoutCommand { get; }
        public ICommand BackCommand { get; }
        public ICommand RetryLastPirepCommand { get; }
        public ICommand OpenDebugFolderCommand { get; }
        public ICommand OpenDataFolderCommand { get; }
        public ICommand OpenAppFolderCommand { get; }
        public ICommand OpenExportsFolderCommand { get; }
        public ICommand OpenConfigFolderCommand { get; }
        public ICommand OpenHudPackageFolderCommand { get; }
        public ICommand CopyHudBridgeUrlCommand { get; }
        public ICommand InstallHudToCommunityCommand { get; }
        public ICommand ProbeHudBridgeCommand { get; }
        public ICommand CheckUpdateCommand { get; }
        public ICommand DownloadUpdateCommand { get; }
        public ICommand OpenUpdateLogsCommand { get; }
        public ICommand CopyUpdateDiagnosticCommand { get; }
        public ICommand RefreshProfileCommand { get; }
        public ICommand ResetSampleSessionCommand { get; }
        public ICommand ExportSnapshotJsonCommand { get; }
        public ICommand ExportSessionCsvCommand { get; }
        public ICommand CopyTelemetryDiagnosticCommand { get; }
        public ICommand MarkAircraftTestedCommand { get; }

        public string AcarsVersion => "v" + UpdateService.CurrentVersion;
        public string FsuipcVersion => ResolveFsuipcVersion();
        public string HudBridgeStatus => AcarsContext.HudBridge != null ? AcarsContext.HudBridge.GetHealthText() : "HUD bridge no disponible";
        public string HudIndependenceStatus => "HUD Patagonia Wings independiente: usa solo el bridge local ACARS/MSFS, sin integraciones externas.";
        public string SimulatorConnectionStatus
        {
            get
            {
                var runtime = AcarsContext.Runtime;
                if (runtime == null)
                {
                    return "SimConnect: no disponible | Backend: N/D";
                }

                var simStatus = runtime.IsSimulatorConnected ? "conectado" : "no conectado";
                var backend = string.IsNullOrWhiteSpace(runtime.SimulatorBackend) ? "N/D" : runtime.SimulatorBackend.Trim();
                return "SimConnect: " + simStatus + " | Backend: " + backend;
            }
        }
        public string UpdateInstalledVersion
        {
            get => _updateInstalledVersion;
            set => SetField(ref _updateInstalledVersion, value);
        }
        public string UpdateLatestVersion
        {
            get => _updateLatestVersion;
            set => SetField(ref _updateLatestVersion, value);
        }
        public string UpdateChannel
        {
            get => _updateChannel;
            set => SetField(ref _updateChannel, value);
        }
        public string UpdateManifestUrl
        {
            get => _updateManifestUrl;
            set => SetField(ref _updateManifestUrl, value);
        }
        public string UpdateDownloadUrl
        {
            get => _updateDownloadUrl;
            set => SetField(ref _updateDownloadUrl, value);
        }
        public string UpdateLastError
        {
            get => _updateLastError;
            set => SetField(ref _updateLastError, value);
        }
        public string UpdateState
        {
            get => _updateState;
            set => SetField(ref _updateState, value);
        }
        public string HudCommunityStatus
        {
            get
            {
                var bridge = AcarsContext.HudBridge;
                if (bridge == null) return "Community: no disponible";
                var paths = bridge.DetectCommunityFolders();
                if (paths.Length == 0) return "Community: no detectada";
                return "Community: " + string.Join(" | ", paths);
            }
        }
        public bool AlwaysVisible
        {
            get => _alwaysVisible;
            set
            {
                if (SetField(ref _alwaysVisible, value))
                {
                    SavePreferences();
                    AlwaysVisibleChanged?.Invoke(value);
                }
            }
        }

        public bool UseKg
        {
            get => _useKg;
            set
            {
                if (SetField(ref _useKg, value))
                {
                    SavePreferences();
                }
            }
        }

        public string SimulatorIp
        {
            get => _simulatorIp;
            set
            {
                var sanitized = string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim();
                if (SetField(ref _simulatorIp, sanitized))
                {
                    SavePreferences();
                }
            }
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetField(ref _statusMessage, value);
        }

        public bool EnableInSimHud
        {
            get => _enableInSimHud;
            set
            {
                if (SetField(ref _enableInSimHud, value))
                {
                    SavePreferences();
                    OnPropertyChanged(nameof(HudBridgeStatus));
                    OnPropertyChanged(nameof(HudCommunityStatus));
                }
            }
        }

        public int LocalHudPort
        {
            get => _localHudPort;
            set
            {
                var sanitized = SanitizeHudPort(value);
                if (SetField(ref _localHudPort, sanitized))
                {
                    SavePreferences();
                    OnPropertyChanged(nameof(HudBridgeStatus));
                    OnPropertyChanged(nameof(HudCommunityStatus));
                }
            }
        }

        public int HudUpdateRateHz
        {
            get => _hudUpdateRateHz;
            set
            {
                var sanitized = SanitizeHudRate(value);
                if (SetField(ref _hudUpdateRateHz, sanitized))
                {
                    SavePreferences();
                }
            }
        }

        private static int SanitizeHudPort(int value)
        {
            return value < 1024 || value > 65535 ? 37677 : value;
        }

        private static int SanitizeHudRate(int value)
        {
            if (value < 1) return 1;
            if (value > 10) return 10;
            return value;
        }

        public bool HudOnlyInFlight
        {
            get => _hudOnlyInFlight;
            set
            {
                if (SetField(ref _hudOnlyInFlight, value))
                {
                    SavePreferences();
                }
            }
        }

        public string TelemetryInspectorSummary
        {
            get => _telemetryInspectorSummary;
            set => SetField(ref _telemetryInspectorSummary, value);
        }

        public ObservableCollection<TelemetryInspectorRow> TelemetryInspectorRows => _telemetryInspectorRows;

        public string TelemetryInspectorLastExport
        {
            get => _telemetryInspectorLastExport;
            set => SetField(ref _telemetryInspectorLastExport, value);
        }

        public string TelemetryInspectorLastMark
        {
            get => _telemetryInspectorLastMark;
            set => SetField(ref _telemetryInspectorLastMark, value);
        }

        private async Task RetryLastPirepAsync()
        {
            var api = AcarsContext.Api;
            var before = api != null ? api.GetPendingCloseoutCount() : 0;

            if (api == null)
            {
                StatusMessage = "No hay servicio API ACARS disponible para reenviar a Patagonia Wings Web/Supabase.";
                return;
            }

            StatusMessage = before > 0
                ? string.Format("Sincronizando {0} PIREP pendiente(s) con Patagonia Wings Web/Supabase...", before)
                : "No hay PIREPs pendientes locales; se fuerza verificacion con Web/Supabase.";

            try
            {
                await api.TryProcessPendingCloseoutsAsync("support_manual_retry").ConfigureAwait(false);

                var after = api.GetPendingCloseoutCount();
                var diag = api.GetLastFinalizeDiagnostic();
                StatusMessage = after == 0
                    ? "Sincronizacion completada: no quedan PIREPs pendientes locales. " + diag
                    : string.Format("Web/Supabase procesado, pero quedan {0} PIREP pendiente(s). {1}", after, diag);
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude sincronizar con Patagonia Wings Web/Supabase: " + ex.Message;
            }
        }

        private void SavePreferences()
        {
            _preferences.AlwaysVisible = _alwaysVisible;
            _preferences.UseKg = _useKg;
            _preferences.SimulatorIp = _simulatorIp;
            _preferences.EnableInSimHud = _enableInSimHud;
            _preferences.LocalHudPort = _localHudPort;
            _preferences.HudUpdateRateHz = _hudUpdateRateHz;
            _preferences.HudOnlyInFlight = _hudOnlyInFlight;
            UiPreferencesStore.Save(_preferences);

            AcarsContext.HudBridge?.ApplySettings(
                _enableInSimHud,
                _localHudPort,
                _hudUpdateRateHz,
                _hudOnlyInFlight,
                _preferences.HudTheme);
        }

        private void OpenHudPackageFolder()
        {
            try
            {
                var path = AcarsContext.HudBridge != null
                    ? AcarsContext.HudBridge.GetPackageFolderPath()
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "packages", "patagoniawings-acars-hud");
                OpenFolder(path);
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude abrir carpeta HUD: " + ex.Message;
            }
        }

        private void CopyHudBridgeUrl()
        {
            try
            {
                var url = AcarsContext.HudBridge != null
                    ? AcarsContext.HudBridge.GetStateUrl()
                    : "http://127.0.0.1:37677/api/hud/state";
                System.Windows.Clipboard.SetText(url);
                StatusMessage = "URL HUD copiada: " + url;
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude copiar URL HUD: " + ex.Message;
            }
        }

        private void InstallHudToCommunity()
        {
            try
            {
                if (AcarsContext.HudBridge == null)
                {
                    StatusMessage = "HUD bridge no disponible.";
                    return;
                }

                if (AcarsContext.HudBridge.InstallHudToCommunity(out var message))
                {
                    StatusMessage = message;
                }
                else
                {
                    StatusMessage = message + " Abre 'Paquete HUD' y copia manualmente a Community.";
                }

                OnPropertyChanged(nameof(HudCommunityStatus));
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude instalar HUD: " + ex.Message;
            }
        }

        private async Task ProbeHudBridgeAsync()
        {
            try
            {
                var bridge = AcarsContext.HudBridge;
                if (bridge == null)
                {
                    StatusMessage = "HUD bridge no disponible.";
                    return;
                }

                // Reaplica settings antes de probar para evitar estado stale.
                bridge.ApplySettings(_enableInSimHud, _localHudPort, _hudUpdateRateHz, _hudOnlyInFlight, _preferences.HudTheme);
                await Task.Delay(250);

                if (!bridge.IsBridgeListening)
                {
                    StatusMessage = "HUD bridge no iniciado. " + bridge.GetHealthText();
                    OnPropertyChanged(nameof(HudBridgeStatus));
                    return;
                }

                var stateUrl = bridge.GetStateUrl();
                var healthUrl = stateUrl.Replace("/api/hud/state", "/api/hud/health");
                var timeout = TimeSpan.FromSeconds(20);

                using (var http = new HttpClient { Timeout = timeout })
                {
                    var healthRes = await http.GetAsync(healthUrl);
                    var stateRes = await http.GetAsync(stateUrl);
                    var healthOk = healthRes.IsSuccessStatusCode ? "OK" : ("HTTP " + (int)healthRes.StatusCode);
                    var stateOk = stateRes.IsSuccessStatusCode ? "OK" : ("HTTP " + (int)stateRes.StatusCode);

                    StatusMessage = "HUD health: " + healthOk +
                                    " | HUD state: " + stateOk +
                                    " | " + HudCommunityStatus;
                }

                OnPropertyChanged(nameof(HudBridgeStatus));
                OnPropertyChanged(nameof(HudCommunityStatus));
            }
            catch (TaskCanceledException)
            {
                var err = AcarsContext.HudBridge != null ? AcarsContext.HudBridge.LastBridgeError : "timeout";
                StatusMessage = "HUD bridge timeout/cancelado. " + err;
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude probar HUD bridge: " + ex.Message;
            }
        }
        private async Task CheckUpdateAsync()
        {
            try
            {
                var diagnostic = await UpdateService.GetDiagnosticsAsync(true);
                UpdateInstalledVersion = diagnostic.InstalledVersion;
                UpdateLatestVersion = diagnostic.LatestVersion;
                UpdateChannel = diagnostic.Channel;
                UpdateManifestUrl = diagnostic.ManifestUrl;
                UpdateDownloadUrl = diagnostic.DownloadUrl;
                UpdateLastError = diagnostic.LastError;
                UpdateState = diagnostic.UpdateAvailable
                    ? "Actualizacion disponible"
                    : "Cliente al dia";
                StatusMessage = $"Update: {diagnostic.InstalledVersion} -> {diagnostic.LatestVersion} | disponible={diagnostic.UpdateAvailable}";
            }
            catch (Exception ex)
            {
                UpdateLastError = ex.Message;
                StatusMessage = "No pude consultar actualizacion: " + ex.Message;
            }
        }

        private async Task DownloadUpdateAsync()
        {
            try
            {
                var check = await UpdateService.CheckForUpdatesAsync(true);
                UpdateInstalledVersion = check.CurrentVersion;
                UpdateLatestVersion = check.LatestVersion;
                UpdateChannel = check.Channel;
                UpdateManifestUrl = string.IsNullOrWhiteSpace(check.ManifestUrl) ? "manifest legado" : check.ManifestUrl;
                UpdateDownloadUrl = check.DownloadUrl;

                if (!check.Success)
                {
                    UpdateLastError = check.Error;
                    StatusMessage = "Chequeo update fallo: " + check.Error;
                    return;
                }

                if (!check.IsUpdateAvailable)
                {
                    StatusMessage = "No hay actualizacion disponible.";
                    UpdateState = "Cliente al dia";
                    return;
                }

                UpdateState = "Iniciando descarga";
                UpdateService.StartImmediateUpdate(check);
            }
            catch (Exception ex)
            {
                UpdateLastError = ex.Message;
                StatusMessage = "No pude descargar update: " + ex.Message;
            }
        }

        private void OpenUpdateLogs()
        {
            OpenFolder(UpdateService.LogsDirectory);
        }

        private void CopyUpdateDiagnostic()
        {
            try
            {
                var payload =
                    $"installedVersion={UpdateInstalledVersion}\n" +
                    $"latestVersion={UpdateLatestVersion}\n" +
                    $"channel={UpdateChannel}\n" +
                    $"manifestUrl={UpdateManifestUrl}\n" +
                    $"downloadUrl={UpdateDownloadUrl}\n" +
                    $"state={UpdateState}\n" +
                    $"lastError={UpdateLastError}\n" +
                    $"pendingCloseouts={AcarsContext.Api.GetPendingCloseoutCount()}\n" +
                    $"lastFinalize={AcarsContext.Api.GetLastFinalizeDiagnostic()}\n" +
                    $"simulatorStatus={SimulatorConnectionStatus}\n" +
                    $"hudStatus={HudBridgeStatus}";
                System.Windows.Clipboard.SetText(payload);
                StatusMessage = "Diagnostico de update copiado.";
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude copiar diagnostico: " + ex.Message;
            }
        }

        private void RefreshTelemetryInspector()
        {
            var sample = AcarsContext.Runtime.LastTelemetry ?? AcarsContext.FlightService.LastSimData;
            if (sample == null)
            {
                TelemetryInspectorSummary = "Sin telemetria en vivo";
                _telemetryInspectorRows.Clear();
            var profile = AircraftNormalizationService.ResolveProfile(sample.AircraftTitle ?? string.Empty);
            var supportsGear = profile?.SupportsGearRead ?? true;
            var supportsReverse = (profile?.EngineCount ?? 1) > 1 || (sample.DetectedProfileCode?.IndexOf("C208", StringComparison.OrdinalIgnoreCase) >= 0);
            var n1IsProxy = string.Equals(profile?.N1Source, "combustion_proxy", StringComparison.OrdinalIgnoreCase);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Patagonia Wings ACARS Data Lab");
            sb.AppendLine($"capturedAtUtc={sample.CapturedAtUtc:O}");
            sb.AppendLine($"simConnected={AcarsContext.Runtime.IsSimulatorConnected} backend={AcarsContext.Runtime.SimulatorBackend}");
            sb.AppendLine($"aircraftTitle={sample.AircraftTitle}");
            sb.AppendLine($"detectedProfileCode={sample.DetectedProfileCode} profileCode={sample.ProfileCode} status={sample.ProfileStatus}");
            sb.AppendLine($"detectionConfidence={sample.DetectionConfidence} reason={sample.DetectionReason}");
            sb.AppendLine($"pos=({sample.Latitude:F6},{sample.Longitude:F6}) hdg={sample.Heading:F0}");
            sb.AppendLine($"altMSL={sample.AltitudeMslFeet:F0} altAGL={sample.AltitudeAglFeet:F0} reliable={sample.IsAltitudeReliable}");
            sb.AppendLine($"ias={sample.IndicatedAirspeed:F0} gs={sample.GroundSpeed:F0} vs={sample.VerticalSpeed:F0}");
            sb.AppendLine($"phase={sample.OperationalPhaseCode}/{sample.OperationalPhaseName} checklist={sample.PhaseChecklistStatus}");
            sb.AppendLine($"eng={BoolTo01(sample.EngineOneRunning)}{BoolTo01(sample.EngineTwoRunning)}{BoolTo01(sample.EngineThreeRunning)}{BoolTo01(sample.EngineFourRunning)} n1={sample.Engine1N1:F0}/{sample.Engine2N1:F0}");
            sb.AppendLine($"fuelKg={sample.FuelKg:F0} payloadKg={sample.PayloadKg:F0} zfwKg={sample.ZeroFuelWeightKg:F0}");
            sb.AppendLine($"xpdr={sample.TransponderCode:D4} raw={sample.TransponderStateRaw} c={sample.TransponderCharlieMode}");
            sb.AppendLine($"lights bcn={sample.BeaconLightsOn} nav={sample.NavLightsOn} stb={sample.StrobeLightsOn} land={sample.LandingLightsOn} taxi={sample.TaxiLightsOn}");
            sb.AppendLine($"cfg pb={sample.ParkingBrake} gear={sample.GearDown}/{sample.GearTransitioning} flaps={sample.FlapsPercent:F0} rev={sample.ReverserActive}");
            TelemetryInspectorSummary = sb.ToString().TrimEnd();
            RebuildTelemetryInspectorRows(sample);
        }

        private void RebuildTelemetryInspectorRows(SimData sample)
        {
            _telemetryInspectorRows.Clear();
            var profile = AircraftNormalizationService.ResolveProfile(sample.AircraftTitle ?? string.Empty);
            var supportsGear = profile?.SupportsGearRead ?? true;
            var supportsReverse = (profile?.EngineCount ?? 1) > 1 || (sample.DetectedProfileCode?.IndexOf("C208", StringComparison.OrdinalIgnoreCase) >= 0);
            var n1IsProxy = string.Equals(profile?.N1Source, "combustion_proxy", StringComparison.OrdinalIgnoreCase);

            void Add(string field, string readValue, string expected, string status)
            {
                _telemetryInspectorRows.Add(new TelemetryInspectorRow
                {
                    Field = field,
                    ReadValue = readValue,
                    ExpectedByRule = expected,
                    Status = status
                });
            }

            Add("Aircraft Title", sample.AircraftTitle, "No vacÃ­o; debe coincidir con perfil", string.IsNullOrWhiteSpace(sample.AircraftTitle) ? "NOT_AVAILABLE" : "OK");
            Add("Profile Code", sample.DetectedProfileCode, "Perfil detectado exacto o fallback controlado", string.IsNullOrWhiteSpace(sample.DetectedProfileCode) ? "NOT_AVAILABLE" : "OK");
            Add("Latitude", sample.Latitude.ToString("F6"), "Debe variar con movimiento", ResolveFieldStatus(sample.Latitude));
            Add("Longitude", sample.Longitude.ToString("F6"), "Debe variar con movimiento", ResolveFieldStatus(sample.Longitude));
            Add("Altitude MSL", sample.AltitudeMslFeet.ToString("F1") + " ft", "Fuente MSL confiable", sample.IsAltitudeReliable ? "OK" : "SUSPECT");
            Add("Altitude AGL", sample.AltitudeAglFeet.ToString("F1") + " ft", ">= 0; en tierra cercano a 0", sample.AltitudeAglFeet < -5 ? "SUSPECT" : "OK");
            Add("IAS", sample.IndicatedAirspeed.ToString("F1") + " kt", "0 en estacionario; sube en carrera", ResolveFieldStatus(sample.IndicatedAirspeed));
            Add("Ground Speed", sample.GroundSpeed.ToString("F1") + " kt", "0 en gate; >0 en rodaje/vuelo", ResolveFieldStatus(sample.GroundSpeed));
            Add("Vertical Speed", sample.VerticalSpeed.ToString("F1") + " fpm", "Cerca 0 en nivelado", "OK");
            Add("Fuel Kg", sample.FuelKg.ToString("F1") + " kg", ">0 mientras tanque con combustible", sample.FuelKg <= 0 ? "SUSPECT" : "OK");
            Add("Payload Kg", sample.PayloadKg.ToString("F1") + " kg", ">= 0", sample.PayloadKg < 0 ? "SUSPECT" : "OK");
            Add("Parking Brake", sample.ParkingBrake ? "ON" : "OFF", "ON en gate/preflight", "OK");
            if (supportsGear)
            {
                Add("Gear Down", sample.GearDown ? "DOWN" : "UP", "DOWN en tierra", "OK");
            }
            else
            {
                Add("Gear Down", "N/D", "No aplica a este perfil (tren fijo o no confiable)", "UNSUPPORTED");
            }
            Add("Flaps Percent", sample.FlapsPercent.ToString("F1") + " %", "Debe leer posiciÃ³n real de flap por aeronave", sample.FlapsPercent < 0 ? "SUSPECT" : "OK");
            Add("Flaps Deployed", sample.FlapsDeployed ? "YES" : "NO", "YES cuando flaps > 0", (sample.FlapsDeployed == (sample.FlapsPercent > 0.01)) ? "OK" : "SUSPECT");
            Add("Spoilers Armed", sample.SpoilersArmed ? "YES" : "NO", "SegÃºn perfil/procedimiento", "OK");
            if (supportsReverse)
            {
                Add("Reverser Active", sample.ReverserActive ? "YES" : "NO", "NO en preflight", "OK");
            }
            else
            {
                Add("Reverser Active", "N/D", "No aplica a este perfil", "UNSUPPORTED");
            }
            Add("Beacon", sample.BeaconLightsOn ? "ON" : "OFF", "ON antes de arranque", "OK");
            Add("Nav Lights", sample.NavLightsOn ? "ON" : "OFF", "ON en operaciÃ³n", "OK");
            Add("Strobe", sample.StrobeLightsOn ? "ON" : "OFF", "ON en pista; OFF en gate/taxi", "OK");
            Add("Landing Lights", sample.LandingLightsOn ? "ON" : "OFF", "ON en despegue/aproximaciÃ³n", "OK");
            Add("Taxi Lights", sample.TaxiLightsOn ? "ON" : "OFF", "ON en rodaje", "OK");
            Add(
                "Engine 1 N1",
                sample.Engine1N1.ToString("F1"),
                n1IsProxy ? "Lectura proxy (combustion): 0 apagado / ~20 encendido" : "Coherente con motor encendido",
                n1IsProxy
                    ? ((sample.EngineOneRunning && sample.Engine1N1 >= 15) || (!sample.EngineOneRunning && sample.Engine1N1 <= 1) ? "OK" : "SUSPECT")
                    : "OK");
            Add("Engine Running", $"{BoolTo01(sample.EngineOneRunning)}{BoolTo01(sample.EngineTwoRunning)}{BoolTo01(sample.EngineThreeRunning)}{BoolTo01(sample.EngineFourRunning)}", "Estado por motor segÃºn perfil", "OK");
            Add("Battery Master", sample.BatteryMasterOn ? "ON" : "OFF", "ON si cabina energizada", "OK");
            Add("Avionics Master", sample.AvionicsMasterOn ? "ON" : "OFF", "ON en preparaciÃ³n/operaciÃ³n", "OK");
            Add("XPDR Code", sample.TransponderCode.ToString("D4"), "CÃ³digo vÃ¡lido 0000-7777", sample.TransponderCode > 0 ? "OK" : "NOT_AVAILABLE");
            Add("XPDR Raw State", sample.TransponderStateRaw.ToString(), "Debe cambiar con selector XPDR", "OK");
            Add("XPDR Charlie", sample.TransponderCharlieMode ? "YES" : "NO", "YES en vuelo", "OK");
            Add("Phase", sample.OperationalPhaseCode + " / " + sample.OperationalPhaseName, "Fase operacional coherente", string.IsNullOrWhiteSpace(sample.OperationalPhaseCode) ? "NOT_AVAILABLE" : "OK");
            Add("Phase Checklist", sample.PhaseChecklistStatus, "READY/WARN/PENDING segÃºn checks", string.IsNullOrWhiteSpace(sample.PhaseChecklistStatus) ? "NOT_AVAILABLE" : "OK");
        }

        private void ResetTelemetryInspectorSession()
        {
            TelemetryInspectorLastMark = "Sesion de muestra reiniciada";
            TelemetryInspectorLastExport = "Sin exportaciones";
            StatusMessage = "Telemetry Inspector: sesion reiniciada.";
        }

        private void ExportTelemetrySnapshotJson()
        {
            try
            {
                var sample = AcarsContext.Runtime.LastTelemetry ?? AcarsContext.FlightService.LastSimData;
                if (sample == null)
                {
                    StatusMessage = "No hay telemetria para exportar JSON.";
                    return;
                }

                var exportDir = GetTelemetryInspectorExportsFolderPath();
                Directory.CreateDirectory(exportDir);
                var fileName = $"{SafeFile(sample.DetectedProfileCode)}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var path = Path.Combine(exportDir, fileName);
                File.WriteAllText(path, BuildSnapshotJson(sample), Encoding.UTF8);
                TelemetryInspectorLastExport = "JSON: " + path;
                StatusMessage = "Export JSON listo.";
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude exportar JSON: " + ex.Message;
            }
        }

        private void ExportTelemetrySessionCsv()
        {
            try
            {
                var sample = AcarsContext.Runtime.LastTelemetry ?? AcarsContext.FlightService.LastSimData;
                if (sample == null)
                {
                    StatusMessage = "No hay telemetria para exportar CSV.";
                    return;
                }

                var exportDir = GetTelemetryInspectorExportsFolderPath();
                Directory.CreateDirectory(exportDir);
                var fileName = $"{SafeFile(sample.DetectedProfileCode)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var path = Path.Combine(exportDir, fileName);
                File.WriteAllText(path, BuildSnapshotCsv(sample), Encoding.UTF8);
                TelemetryInspectorLastExport = "CSV: " + path;
                StatusMessage = "Export CSV listo.";
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude exportar CSV: " + ex.Message;
            }
        }

        private void CopyTelemetryDiagnostic()
        {
            try
            {
                RefreshTelemetryInspector();
                System.Windows.Clipboard.SetText(TelemetryInspectorSummary);
                StatusMessage = "Diagnostico de telemetria copiado.";
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude copiar diagnostico: " + ex.Message;
            }
        }

        private void MarkAircraftTested()
        {
            try
            {
                var sample = AcarsContext.Runtime.LastTelemetry ?? AcarsContext.FlightService.LastSimData;
                if (sample == null)
                {
                    StatusMessage = "No hay aeronave activa para marcar prueba.";
                    return;
                }

                var dir = Path.Combine(GetDataFolderPath(), "TelemetryInspector");
                Directory.CreateDirectory(dir);
                var markFile = Path.Combine(dir, "aircraft_tested.log");
                var line = $"{DateTime.UtcNow:O}|{sample.DetectedProfileCode}|{sample.AircraftTitle}|{sample.ProfileStatus}|{sample.DetectionConfidence}";
                File.AppendAllLines(markFile, new[] { line }, Encoding.UTF8);
                TelemetryInspectorLastMark = line;
                StatusMessage = "Aeronave marcada como probada.";
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude marcar aeronave: " + ex.Message;
            }
        }

        private static string BuildSnapshotJson(SimData sample)
        {
            string E(string text) => (text ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"timestamp\": \"{sample.CapturedAtUtc:O}\",");
            sb.AppendLine($"  \"appVersion\": \"{UpdateService.CurrentVersion}\",");
            sb.AppendLine("  \"identity\": {");
            sb.AppendLine($"    \"aircraftTitle\": \"{E(sample.AircraftTitle)}\",");
            sb.AppendLine($"    \"detectedProfileCode\": \"{E(sample.DetectedProfileCode)}\",");
            sb.AppendLine($"    \"profileCode\": \"{E(sample.ProfileCode)}\",");
            sb.AppendLine($"    \"aircraftTypeCode\": \"{E(sample.AircraftTypeCode)}\",");
            sb.AppendLine($"    \"aircraftVariantCode\": \"{E(sample.AircraftVariantCode)}\",");
            sb.AppendLine($"    \"addonSource\": \"{E(sample.AddonSource)}\",");
            sb.AppendLine($"    \"detectionConfidence\": \"{E(sample.DetectionConfidence)}\",");
            sb.AppendLine($"    \"detectionReason\": \"{E(sample.DetectionReason)}\",");
            sb.AppendLine($"    \"profileStatus\": \"{E(sample.ProfileStatus)}\"");
            sb.AppendLine("  },");
            sb.AppendLine("  \"normalizedValues\": {");
            sb.AppendLine($"    \"latitude\": {sample.Latitude:F6},");
            sb.AppendLine($"    \"longitude\": {sample.Longitude:F6},");
            sb.AppendLine($"    \"altitudeMslFt\": {sample.AltitudeMslFeet:F1},");
            sb.AppendLine($"    \"altitudeAglFt\": {sample.AltitudeAglFeet:F1},");
            sb.AppendLine($"    \"ias\": {sample.IndicatedAirspeed:F1},");
            sb.AppendLine($"    \"groundSpeed\": {sample.GroundSpeed:F1},");
            sb.AppendLine($"    \"fuelKg\": {sample.FuelKg:F1},");
            sb.AppendLine($"    \"payloadKg\": {sample.PayloadKg:F1},");
            sb.AppendLine($"    \"xpdrCode\": {sample.TransponderCode},");
            sb.AppendLine($"    \"phaseCode\": \"{E(sample.OperationalPhaseCode)}\"");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string BuildSnapshotCsv(SimData sample)
        {
            var sb = new StringBuilder();
            sb.AppendLine("timestamp,aircraftTitle,profileCode,field,value,unit,status,source,reliability,penaltyEligible,reason");
            void L(string field, string value, string unit, string status, string source, string reliability, string penaltyEligible, string reason)
            {
                sb.AppendLine(string.Join(",",
                    Csv(sample.CapturedAtUtc.ToString("O")),
                    Csv(sample.AircraftTitle),
                    Csv(sample.DetectedProfileCode),
                    Csv(field),
                    Csv(value),
                    Csv(unit),
                    Csv(status),
                    Csv(source),
                    Csv(reliability),
                    Csv(penaltyEligible),
                    Csv(reason)));
            }
            L("latitude", sample.Latitude.ToString("F6"), "deg", ResolveFieldStatus(sample.Latitude), "sim.telemetry", "raw", "false", "");
            L("longitude", sample.Longitude.ToString("F6"), "deg", ResolveFieldStatus(sample.Longitude), "sim.telemetry", "raw", "false", "");
            L("altitude_msl_ft", sample.AltitudeMslFeet.ToString("F1"), "ft", sample.IsAltitudeReliable ? "OK" : "SUSPECT", "sim.telemetry", sample.IsAltitudeReliable ? "high" : "low", "true", sample.AltitudeSource);
            L("fuel_kg", sample.FuelKg.ToString("F1"), "kg", ResolveFieldStatus(sample.FuelKg), "sim.telemetry", "medium", "true", "");
            L("flaps_percent", sample.FlapsPercent.ToString("F1"), "%", sample.FlapsPercent >= 0 ? "OK" : "SUSPECT", "sim.telemetry", "medium", "true", "Debe reflejar posicion real de flap");
            L("flaps_deployed", sample.FlapsDeployed ? "1" : "0", "bool", "OK", "sim.telemetry", "medium", "true", "Coherente con flaps_percent > 0");
            L("xpdr_code", sample.TransponderCode.ToString(), "code", sample.TransponderCode > 0 ? "OK" : "NOT_AVAILABLE", "sim.telemetry", "medium", "true", "");
            L("phase", sample.OperationalPhaseCode, "code", string.IsNullOrWhiteSpace(sample.OperationalPhaseCode) ? "NOT_AVAILABLE" : "OK", "phase.resolver", "high", "true", sample.OperationalPhaseReason);
            return sb.ToString();
        }

        private static string Csv(string value)
        {
            var safe = (value ?? string.Empty).Replace("\"", "\"\"");
            return $"\"{safe}\"";
        }

        private static string ResolveFieldStatus(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "SUSPECT";
            if (Math.Abs(value) < 0.0001d) return "ZERO";
            return "OK";
        }

        private static string BoolTo01(bool value)
        {
            return value ? "1" : "0";
        }

        private static string SafeFile(string value)
        {
            var text = string.IsNullOrWhiteSpace(value) ? "UNKNOWN_PROFILE" : value.Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                text = text.Replace(c, '_');
            }
            return text;
        }

        public sealed class TelemetryInspectorRow
        {
            public string Field { get; set; } = string.Empty;
            public string ReadValue { get; set; } = string.Empty;
            public string ExpectedByRule { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }

        private static string ResolveFsuipcVersion()
        {
            try
            {
                var dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fsuipcClient.dll");
                if (!File.Exists(dllPath))
                {
                    dllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "libs", "fsuipcClient.dll");
                }

                if (!File.Exists(dllPath))
                {
                    return "No detectado";
                }

                var version = FileVersionInfo.GetVersionInfo(dllPath).FileVersion;
                return string.IsNullOrWhiteSpace(version) ? "No detectado" : "v" + version.Trim();
            }
            catch
            {
                return "No detectado";
            }
        }

        private static string GetDebugFolderPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PatagoniaWings",
                "Acars",
                "logs");
        }

        private static string GetDataFolderPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PatagoniaWings",
                "Acars");
        }

        private static string GetExportsFolderPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PatagoniaWings",
                "Acars",
                "exports");
        }

        private static string GetTelemetryInspectorExportsFolderPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PatagoniaWings",
                "Acars",
                "TelemetryInspector",
                "exports");
        }

        private static string GetConfigFolderPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PatagoniaWings",
                "Acars",
                "config");
        }

        private void OpenFolder(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude abrir la carpeta: " + ex.Message;
            }
        }
    }
}

