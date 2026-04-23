using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        private string _statusMessage = string.Empty;

        public SupportViewModel()
        {
            _preferences = UiPreferencesStore.Load();
            _alwaysVisible = _preferences.AlwaysVisible;
            _useKg = _preferences.UseKg;
            _simulatorIp = string.IsNullOrWhiteSpace(_preferences.SimulatorIp)
                ? "127.0.0.1"
                : _preferences.SimulatorIp.Trim();

            LogoutCommand = new RelayCommand(() => RequestLogout?.Invoke());
            BackCommand = new RelayCommand(() => RequestBack?.Invoke());
            RetryLastPirepCommand = new AsyncRelayCommand(async _ => await RetryLastPirepAsync());
            OpenDebugFolderCommand = new RelayCommand(() => OpenFolder(GetDebugFolderPath()));
            OpenDataFolderCommand = new RelayCommand(() => OpenFolder(GetDataFolderPath()));
            OpenAppFolderCommand = new RelayCommand(() => OpenFolder(AppDomain.CurrentDomain.BaseDirectory));
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

        public string AcarsVersion => "v" + UpdateService.CurrentVersion;
        public string FsuipcVersion => ResolveFsuipcVersion();

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
            UiPreferencesStore.Save(_preferences);
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
