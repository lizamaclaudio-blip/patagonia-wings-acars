#nullable enable
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using AutoUpdaterDotNET;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// Servicio de actualización. Usa AutoUpdater.NET sólo para parsear el XML remoto.
    /// El diálogo se implementa con WPF nativo para evitar el crash por
    /// AutoUpdaterDotNET.UpdateForm.InitializeComponent (FileNotFoundException en WinForms).
    /// </summary>
    public static class UpdateService
    {
        private const string AutoUpdaterXmlUrl =
            "https://raw.githubusercontent.com/lizamaclaudio-blip/patagonia-wings-acars/main/Web/autoupdater.xml";

        private static bool _checkedThisSession;

        /// <summary>Versión instalada leída desde App.config.</summary>
        public static string CurrentVersion => ReadSetting("AppVersion", GetAssemblyVersion());

        static UpdateService()
        {
            // Suscribimos CheckForUpdateEvent para interceptar el resultado y
            // mostrar NUESTRO propio diálogo WPF en lugar del UpdateForm de WinForms
            // (que crashea con FileNotFoundException al buscar satellite assemblies).
            AutoUpdater.CheckForUpdateEvent += OnCheckForUpdate;
        }

        public static void CheckAndStartUpdate(Window owner)
        {
            if (_checkedThisSession) return;
            _checkedThisSession = true;

            WriteLog($"Checking updates. Installed={CurrentVersion} XML={AutoUpdaterXmlUrl}");
            try
            {
                AutoUpdater.Start(AutoUpdaterXmlUrl);
            }
            catch (Exception ex)
            {
                WriteLog($"AutoUpdater.Start error: {ex.Message}");
            }
        }

        public static async Task NotifyIfUpdateAvailableAsync(Window owner)
        {
            CheckAndStartUpdate(owner);
            await Task.CompletedTask;
        }

        public static async Task<UpdateCheckResult> CheckForUpdatesAsync(bool force = false)
        {
            await Task.CompletedTask;
            return new UpdateCheckResult { CurrentVersion = CurrentVersion, Success = true };
        }

        public class UpdateCheckResult
        {
            public bool Success { get; set; }
            public bool IsUpdateAvailable { get; set; }
            public string CurrentVersion { get; set; } = string.Empty;
            public string LatestVersion { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
        }

        // ── Handler principal ────────────────────────────────────────────────────

        private static void OnCheckForUpdate(UpdateInfoEventArgs args)
        {
            if (args?.Error != null)
            {
                WriteLog($"XML fetch error: {args.Error.Message}");
                return;
            }

            var available = args?.CurrentVersion?.ToString() ?? "";
            var installed  = CurrentVersion;
            var isNewer    = IsVersionNewer(available, installed);

            WriteLog($"Check result: installed={installed} available={available} newer={isNewer}");

            if (!isNewer) return;

            var downloadUrl = args?.DownloadURL ?? "";

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                try
                {
                    var msg = $"Hay una nueva versión disponible de Patagonia Wings ACARS.\n\n" +
                              $"  Instalada : {installed}\n" +
                              $"  Disponible: {available}\n\n" +
                              $"¿Deseas descargar e instalar la actualización ahora?";

                    var result = MessageBox.Show(
                        msg,
                        "Patagonia Wings ACARS — Actualización disponible",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes && !string.IsNullOrWhiteSpace(downloadUrl))
                    {
                        WriteLog($"User accepted update. Opening: {downloadUrl}");
                        Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true });
                    }
                    else
                    {
                        WriteLog("User postponed update.");
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"Dialog error: {ex.Message}");
                }
            });
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static bool IsVersionNewer(string available, string installed)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(available)) return false;
                var avVer   = NormalizeVersion(available);
                var instVer = NormalizeVersion(installed);
                return Version.TryParse(avVer, out var a)
                    && Version.TryParse(instVer, out var b)
                    && a > b;
            }
            catch { return false; }
        }

        private static string NormalizeVersion(string v)
        {
            // Asegura formato Major.Minor.Build.Revision para Version.TryParse
            // 3.0.5 → 3.0.5.0  |  3.0.5.0 → 3.0.5.0
            var parts = (v ?? "0").Split('.');
            while (parts.Length < 4)
                v += ".0";
            return v;
        }

        private static string GetAssemblyVersion()
        {
            try
            {
                var ver = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
                              ?.GetName()?.Version;
                if (ver == null) return "3.0.3";
                return $"{ver.Major}.{ver.Minor}.{ver.Build}";
            }
            catch { return "3.0.3"; }
        }

        private static string ReadSetting(string key, string fallback)
        {
            try
            {
                var v = ConfigurationManager.AppSettings[key];
                return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
            }
            catch { return fallback; }
        }

        private static void WriteLog(string message)
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PatagoniaWings", "Acars", "logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "autoupdater.log"),
                    $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
