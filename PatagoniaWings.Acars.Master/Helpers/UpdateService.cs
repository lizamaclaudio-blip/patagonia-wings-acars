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
    ///
    /// Flujo completo:
    ///  1. Detecta versión nueva leyendo autoupdater.xml desde GitHub.
    ///  2. Muestra un banner de progreso DENTRO de la ventana principal (no una ventana aparte).
    ///  3. Descarga el instalador en background con progreso visible al usuario.
    ///  4. Escribe un archivo flag antes de cerrar para detectar "recién actualizado" al reabrir.
    ///  5. Lanza un CMD diferido (+5 s) que ejecuta el instalador con /VERYSILENT.
    ///  6. Inno Setup [Run] con Check:WizardSilent reabre el ACARS automáticamente.
    ///  7. Al iniciar, si existe el flag, muestra "✓ Actualizado a vX.X.X" y borra el flag.
    /// </summary>
    public static class UpdateService
    {
        private const string AutoUpdaterXmlUrl =
            "https://raw.githubusercontent.com/lizamaclaudio-blip/patagonia-wings-acars/main/Web/autoupdater.xml";

        private static readonly string JustUpdatedFlagPath =
            Path.Combine(Path.GetTempPath(), "PatagoniaWings_JustUpdated.txt");

        // Cooldown: no volver a verificar si ya se verificó hace menos de 10 minutos
        private static DateTime _lastCheckUtc = DateTime.MinValue;
        private static readonly TimeSpan CheckCooldown = TimeSpan.FromMinutes(10);

        // ── Eventos para la barra de progreso dentro del ACARS ──────────────────
        /// <summary>Progreso de descarga 0-100. Se dispara en el thread de background.</summary>
        public static event Action<int>? DownloadProgressChanged;

        /// <summary>Mensaje de estado textual durante la actualización.</summary>
        public static event Action<string>? UpdateStatusChanged;

        // ── Versión instalada ────────────────────────────────────────────────────
        public static string CurrentVersion => ReadSetting("AppVersion", GetAssemblyVersion());

        static UpdateService()
        {
            AutoUpdater.CheckForUpdateEvent += OnCheckForUpdate;
        }

        // ── API pública ──────────────────────────────────────────────────────────

        public static void CheckAndStartUpdate(Window owner)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCheckUtc) < CheckCooldown)
            {
                WriteLog($"Saltando verificación: última hace {(now - _lastCheckUtc).TotalSeconds:F0}s (cooldown={CheckCooldown.TotalMinutes}m)");
                return;
            }
            _lastCheckUtc = now;

            WriteLog($"Checking updates. Installed={CurrentVersion}");
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

        /// <summary>
        /// Llama este método al arrancar la ventana principal.
        /// Si el ACARS acaba de actualizarse, muestra una notificación breve.
        /// </summary>
        public static void CheckAndShowPostUpdateNotification()
        {
            try
            {
                if (!File.Exists(JustUpdatedFlagPath)) return;

                var updatedTo = File.ReadAllText(JustUpdatedFlagPath).Trim();
                File.Delete(JustUpdatedFlagPath);

                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    ShowToast($"✓  Actualizado a v{updatedTo}  ·  Todo listo", "#16B989", 6);
                }));

                WriteLog($"Post-update notification: v{updatedTo}");
            }
            catch (Exception ex)
            {
                WriteLog($"CheckAndShowPostUpdateNotification error: {ex.Message}");
            }
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

            WriteLog($"Nueva versión {available} detectada. Iniciando descarga silenciosa: {downloadUrl}");

            // Notificar status inicial → la barra del ACARS se hace visible
            UpdateStatusChanged?.Invoke($"Descargando actualización v{available}...");
            DownloadProgressChanged?.Invoke(0);

            // Descargar e instalar en background
            Task.Run(async () => await DownloadAndInstallSilentAsync(downloadUrl, available));
        }

        // ── Descarga + instalación silenciosa ────────────────────────────────────

        private static async Task DownloadAndInstallSilentAsync(string downloadUrl, string version)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "PWAcarsUpdate");
                Directory.CreateDirectory(tempDir);
                var installerPath = Path.Combine(tempDir, $"PatagoniaWingsACARSSetup-{version}.exe");

                WriteLog($"Descargando a: {installerPath}");

                // Descargar con progreso
                using (var client = new WebClient())
                {
                    client.DownloadProgressChanged += (s, e) =>
                    {
                        DownloadProgressChanged?.Invoke(e.ProgressPercentage);
                    };

                    await client.DownloadFileTaskAsync(new Uri(downloadUrl), installerPath);
                }

                WriteLog("Descarga completada. Preparando instalación.");

                // Notificar que la descarga terminó
                DownloadProgressChanged?.Invoke(100);
                UpdateStatusChanged?.Invoke($"Instalando v{version}...  El ACARS reabrirá automáticamente");

                // Esperar 2 s para que el usuario vea el mensaje final
                await Task.Delay(2000);

                // Escribir flag ANTES de cerrar — al reabrir el ACARS verá que acaba de actualizar
                try { File.WriteAllText(JustUpdatedFlagPath, version); } catch { }

                // CMD diferido: espera 5 s (proceso ACARS termina) y luego lanza el instalador
                var launcherPath = Path.Combine(tempDir, "launch_update.cmd");
                File.WriteAllText(launcherPath,
                    "@echo off\r\n" +
                    "ping 127.0.0.1 -n 6 >nul\r\n" +
                    $"\"{installerPath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-\r\n");

                var psi = new ProcessStartInfo("cmd.exe")
                {
                    Arguments       = $"/c \"{launcherPath}\"",
                    UseShellExecute = true,
                    WindowStyle     = ProcessWindowStyle.Hidden,
                    CreateNoWindow  = true
                };

                Process.Start(psi);
                WriteLog("Script de instalación lanzado. Cerrando ACARS.");

                // Cerrar el ACARS en el hilo UI
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    WriteLog("Application.Shutdown() — instalador tomará control en ~5s.");
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                WriteLog($"Error en descarga/instalación silenciosa: {ex.Message}");
                UpdateStatusChanged?.Invoke($"Error en actualización: {ex.Message}");
            }
        }

        // ── Toast genérico (solo se usa para la notificación post-update) ────────

        private static void ShowToast(string message, string hexColor, int durationSeconds = 4)
        {
            try
            {
                var color = (System.Windows.Media.Color)
                    System.Windows.Media.ColorConverter.ConvertFromString(hexColor);

                var toast = new Window
                {
                    Width = 340,
                    Height = 56,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                };

                var screen = SystemParameters.WorkArea;
                toast.Left = screen.Right - 350;
                toast.Top = screen.Bottom - 66;

                toast.Content = new System.Windows.Controls.Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(230, color.R, color.G, color.B)),
                    CornerRadius = new CornerRadius(8),
                    Child = new System.Windows.Controls.TextBlock
                    {
                        Text = message,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 12,
                        FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(16, 0, 16, 0)
                    }
                };

                toast.Show();

                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(durationSeconds)
                };
                timer.Tick += (s, e) => { timer.Stop(); try { toast.Close(); } catch { } };
                timer.Start();
            }
            catch (Exception ex)
            {
                WriteLog($"ShowToast error: {ex.Message}");
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static bool IsVersionNewer(string available, string installed)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(available)) return false;

                var avVer   = NormalizeVersion(available);
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
            var raw    = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
            var source = raw.Split('.');
            var norm   = new string[4];

            for (var i = 0; i < norm.Length; i++)
                norm[i] = (i < source.Length && !string.IsNullOrWhiteSpace(source[i]))
                    ? source[i].Trim() : "0";

            return string.Join(".", norm);
        }

        private static string GetAssemblyVersion()
        {
            try
            {
                var ver = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
                              ?.GetName()?.Version;
                return ver == null ? "3.1.4" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
            }
            catch { return "3.1.4"; }
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
