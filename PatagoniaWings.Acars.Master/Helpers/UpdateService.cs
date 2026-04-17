#nullable enable
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using AutoUpdaterDotNET;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// Actualización automática y silenciosa.
    /// Al detectar una versión nueva: descarga el instalador en background
    /// y lo ejecuta con /VERYSILENT /SUPPRESSMSGBOXES, luego cierra la app.
    /// No requiere ningún click del usuario.
    /// </summary>
    public static class UpdateService
    {
        private const string AutoUpdaterXmlUrl =
            "https://raw.githubusercontent.com/lizamaclaudio-blip/patagonia-wings-acars/main/Web/autoupdater.xml";

        // Cooldown: no volver a verificar si ya se verificó hace menos de 10 minutos
        private static DateTime _lastCheckUtc = DateTime.MinValue;
        private static readonly TimeSpan CheckCooldown = TimeSpan.FromMinutes(10);

        public static string CurrentVersion => ReadSetting("AppVersion", GetAssemblyVersion());

        static UpdateService()
        {
            AutoUpdater.CheckForUpdateEvent += OnCheckForUpdate;
        }

        public static void CheckAndStartUpdate(Window owner)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCheckUtc) < CheckCooldown)
            {
                WriteLog($"Saltando verificación: última hace {(now - _lastCheckUtc).TotalSeconds:F0}s (cooldown={CheckCooldown.TotalMinutes}m)");
                return;
            }
            _lastCheckUtc = now;

            WriteLog($"Checking updates silently. Installed={CurrentVersion}");
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

            var available = args?.CurrentVersion?.ToString() ?? string.Empty;
            var installed = CurrentVersion;
            var isNewer = IsVersionNewer(available, installed);

            WriteLog($"Check result: installed={installed} available={available} newer={isNewer}");

            if (!isNewer) return;

            var downloadUrl = args?.DownloadURL ?? string.Empty;
            if (string.IsNullOrWhiteSpace(downloadUrl)) return;

            WriteLog($"Nueva versión {available} detectada. Descargando silenciosamente: {downloadUrl}");

            // Mostrar banner informativo no bloqueante, luego actualizar sin click
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                try { ShowUpdateBanner(available); }
                catch { }
            });

            // Descargar e instalar en background
            Task.Run(() => DownloadAndInstallSilent(downloadUrl, available));
        }

        private static void ShowUpdateBanner(string version)
        {
            // Ventana toast pequeña, no modal, se cierra sola en 4 segundos
            var toast = new Window
            {
                Width = 320,
                Height = 60,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };

            var screen = System.Windows.SystemParameters.WorkArea;
            toast.Left = screen.Right - 330;
            toast.Top = screen.Bottom - 70;

            var border = new System.Windows.Controls.Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(230, 2, 132, 199)),
                CornerRadius = new CornerRadius(8),
                Child = new System.Windows.Controls.TextBlock
                {
                    Text = $"⬆  Actualizando ACARS a v{version}...",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(16, 0, 16, 0)
                }
            };

            toast.Content = border;
            toast.Show();

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(4)
            };
            timer.Tick += (s, e) => { timer.Stop(); try { toast.Close(); } catch { } };
            timer.Start();
        }

        private static void DownloadAndInstallSilent(string downloadUrl, string version)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "PWAcarsUpdate");
                Directory.CreateDirectory(tempDir);
                var installerPath = Path.Combine(tempDir, $"PatagoniaWingsACARSSetup-{version}.exe");

                WriteLog($"Descargando a: {installerPath}");

                using (var client = new WebClient())
                {
                    client.DownloadFile(downloadUrl, installerPath);
                }

                WriteLog($"Descarga completada. Ejecutando instalador silencioso.");

                var psi = new ProcessStartInfo(installerPath)
                {
                    Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
                    UseShellExecute = true
                };

                Process.Start(psi);

                // Cerrar app para que el instalador pueda reemplazar el exe
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    WriteLog("Cerrando app para instalar actualización.");
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                WriteLog($"Error en descarga/instalación silenciosa: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static bool IsVersionNewer(string available, string installed)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(available)) return false;

                var avVer = NormalizeVersion(available);
                var instVer = NormalizeVersion(installed);

                WriteLog($"Normalized => available={avVer} installed={instVer}");

                if (!Version.TryParse(avVer, out var a) || !Version.TryParse(instVer, out var b))
                    return false;

                return a > b;
            }
            catch (Exception ex)
            {
                WriteLog($"IsVersionNewer error: {ex.Message}");
                return false;
            }
        }

        private static string NormalizeVersion(string value)
        {
            var raw = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
            var source = raw.Split('.');
            var normalized = new string[4];

            for (var i = 0; i < normalized.Length; i++)
            {
                if (i < source.Length && !string.IsNullOrWhiteSpace(source[i]))
                    normalized[i] = source[i].Trim();
                else
                    normalized[i] = "0";
            }

            return string.Join(".", normalized);
        }

        private static string GetAssemblyVersion()
        {
            try
            {
                var ver = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
                              ?.GetName()?.Version;
                if (ver == null) return "3.0.8";
                return $"{ver.Major}.{ver.Minor}.{ver.Build}";
            }
            catch { return "3.0.8"; }
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
