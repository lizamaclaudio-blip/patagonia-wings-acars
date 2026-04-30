using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Input;
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

        public SupportViewModel()
        {
            _preferences = UiPreferencesStore.Load();
            _alwaysVisible = _preferences.AlwaysVisible;
            _useKg = _preferences.UseKg;
            _simulatorIp = string.IsNullOrWhiteSpace(_preferences.SimulatorIp)
                ? "127.0.0.1"
                : _preferences.SimulatorIp.Trim();
            _enableInSimHud = _preferences.EnableInSimHud;
            _localHudPort = _preferences.LocalHudPort;
            _hudUpdateRateHz = _preferences.HudUpdateRateHz;
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
            OpenSayIntentionsFolderCommand = new RelayCommand(OpenSayIntentionsFolder);
            ProbeSayIntentionsCommand = new RelayCommand(ProbeSayIntentions);
            CheckUpdateCommand = new AsyncRelayCommand(async _ => await CheckUpdateAsync());
            DownloadUpdateCommand = new AsyncRelayCommand(async _ => await DownloadUpdateAsync());
            OpenUpdateLogsCommand = new RelayCommand(OpenUpdateLogs);
            CopyUpdateDiagnosticCommand = new RelayCommand(CopyUpdateDiagnostic);

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
        public ICommand OpenSayIntentionsFolderCommand { get; }
        public ICommand ProbeSayIntentionsCommand { get; }
        public ICommand CheckUpdateCommand { get; }
        public ICommand DownloadUpdateCommand { get; }
        public ICommand OpenUpdateLogsCommand { get; }
        public ICommand CopyUpdateDiagnosticCommand { get; }

        public string AcarsVersion => "v" + UpdateService.CurrentVersion;
        public string FsuipcVersion => ResolveFsuipcVersion();
        public string HudBridgeStatus => AcarsContext.HudBridge != null ? AcarsContext.HudBridge.GetHealthText() : "HUD bridge no disponible";
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
        public string SayIntentionsStatus
        {
            get
            {
                if (AcarsContext.SayIntentionsFlightJson == null) return "SayIntentions: no disponible";
                return AcarsContext.SayIntentionsFlightJson.GetStatusText();
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
                var sanitized = value < 1024 || value > 65535 ? 37677 : value;
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
                var sanitized = value < 1 ? 1 : (value > 5 ? 5 : value);
                if (SetField(ref _hudUpdateRateHz, sanitized))
                {
                    SavePreferences();
                }
            }
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

        private async Task RetryLastPirepAsync()
        {
            var before = AcarsContext.Api != null ? AcarsContext.Api.GetPendingCloseoutCount() : 0;
            StatusMessage = before > 0
                ? string.Format("Reenviando {0} PIREP pendiente(s)...", before)
                : "No hay PIREPs pendientes; igualmente se fuerza una verificación.";

            try
            {
                AcarsContext.TriggerPendingCloseoutRetry("support_manual_retry", 0);
                await Task.Delay(400).ConfigureAwait(false);
                var after = AcarsContext.Api != null ? AcarsContext.Api.GetPendingCloseoutCount() : 0;
                StatusMessage = after == 0
                    ? "Reintento lanzado. No quedan pendientes locales."
                    : string.Format("Reintento lanzado. Pendientes restantes: {0}.", after);
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude lanzar el reintento: " + ex.Message;
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
                    : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Packages", "patagoniawings-acars-hud");
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

                var stateUrl = bridge.GetStateUrl();
                var healthUrl = stateUrl.Replace("/api/hud/state", "/api/hud/health");
                var timeout = TimeSpan.FromSeconds(6);

                using (var http = new HttpClient { Timeout = timeout })
                {
                    var healthRes = await http.GetAsync(healthUrl).ConfigureAwait(false);
                    var stateRes = await http.GetAsync(stateUrl).ConfigureAwait(false);
                    var healthOk = healthRes.IsSuccessStatusCode ? "OK" : ("HTTP " + (int)healthRes.StatusCode);
                    var stateOk = stateRes.IsSuccessStatusCode ? "OK" : ("HTTP " + (int)stateRes.StatusCode);

                    StatusMessage = "HUD health: " + healthOk +
                                    " | HUD state: " + stateOk +
                                    " | " + HudCommunityStatus +
                                    " | " + SayIntentionsStatus;
                }

                OnPropertyChanged(nameof(HudBridgeStatus));
                OnPropertyChanged(nameof(HudCommunityStatus));
                OnPropertyChanged(nameof(SayIntentionsStatus));
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude probar HUD bridge: " + ex.Message;
            }
        }

        private void ProbeSayIntentions()
        {
            try
            {
                var service = AcarsContext.SayIntentionsFlightJson;
                if (service == null)
                {
                    StatusMessage = "SayIntentions no disponible.";
                    return;
                }

                var folder = service.GetFlightJsonFolderPath();
                var file = Path.Combine(folder, "flight.json");
                var exists = File.Exists(file);
                StatusMessage = exists
                    ? "SayIntentions detectado: " + service.GetStatusText()
                    : "SayIntentions no detectado: falta flight.json en " + folder;
                OnPropertyChanged(nameof(SayIntentionsStatus));
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude probar SayIntentions: " + ex.Message;
            }
        }

        private void OpenSayIntentionsFolder()
        {
            try
            {
                var path = AcarsContext.SayIntentionsFlightJson != null
                    ? AcarsContext.SayIntentionsFlightJson.GetFlightJsonFolderPath()
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SayIntentionsAI");
                OpenFolder(path);
                StatusMessage = "Carpeta SayIntentions abierta: " + path;
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude abrir carpeta SayIntentions: " + ex.Message;
            }
        }

        private async Task CheckUpdateAsync()
        {
            try
            {
                var diagnostic = await UpdateService.GetDiagnosticsAsync(true).ConfigureAwait(false);
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
                var check = await UpdateService.CheckForUpdatesAsync(true).ConfigureAwait(false);
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
                    $"simulatorStatus={SimulatorConnectionStatus}\n" +
                    $"hudStatus={HudBridgeStatus}\n" +
                    $"sayIntentions={SayIntentionsStatus}";
                System.Windows.Clipboard.SetText(payload);
                StatusMessage = "Diagnostico de update copiado.";
            }
            catch (Exception ex)
            {
                StatusMessage = "No pude copiar diagnostico: " + ex.Message;
            }
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
