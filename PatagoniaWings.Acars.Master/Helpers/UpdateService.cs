#nullable enable
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using AutoUpdaterDotNET;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// ActualizaciÃ³n automÃ¡tica y silenciosa.
    ///
    /// Flujo completo:
        ///  1. Detecta versiÃ³n nueva leyendo autoupdater.xml desde producciÃ³n.
    ///  2. Muestra un banner de progreso dentro de la ventana principal.
    ///  3. Descarga el instalador en background con progreso visible al usuario.
    ///  4. Escribe un archivo flag antes de cerrar para detectar "reciÃ©n actualizado" al reabrir.
    ///  5. Lanza un CMD diferido (+5 s) que ejecuta el instalador con /VERYSILENT.
    ///  6. Inno Setup [Run] con Check:WizardSilent reabre el ACARS automÃ¡ticamente.
    ///  7. Al iniciar, si existe el flag, muestra "âœ“ Actualizado a vX.X.X" y borra el flag.
    /// </summary>
    public static class UpdateService
    {
        private static string AutoUpdaterXmlUrl =>
            ReadSetting("AutoUpdaterXmlUrl", "https://patagoniaw.com/downloads/autoupdater.xml");

        private static readonly string JustUpdatedFlagPath =
            Path.Combine(Path.GetTempPath(), "PatagoniaWings_JustUpdated.txt");

        private static readonly string SplashCloseFlagPath =
            Path.Combine(Path.GetTempPath(), "PatagoniaWings_UpdateSplashClose.txt");

        // Cooldown: no volver a verificar si ya se verificÃ³ hace menos de 10 minutos
        private static DateTime _lastCheckUtc = DateTime.MinValue;
        private static readonly TimeSpan CheckCooldown = TimeSpan.FromMinutes(10);

        // â”€â”€ Eventos para la barra de progreso dentro del ACARS â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>Progreso de descarga 0-100. Se dispara en el thread de background.</summary>
        public static event Action<int>? DownloadProgressChanged;

        /// <summary>Mensaje de estado textual durante la actualizaciÃ³n.</summary>
        public static event Action<string>? UpdateStatusChanged;

        // â”€â”€ VersiÃ³n instalada â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static string CurrentVersion => ReadSetting("AppVersion", GetAssemblyVersion());

        public static void NotifyStartupComplete()
        {
            try
            {
                File.WriteAllText(SplashCloseFlagPath, DateTime.UtcNow.ToString("O"));
            }
            catch
            {
                // best-effort
            }
        }

        static UpdateService()
        {
            AutoUpdater.CheckForUpdateEvent += OnCheckForUpdate;
        }

        // â”€â”€ API pÃºblica â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        public static void CheckAndStartUpdate(Window owner)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastCheckUtc) < CheckCooldown)
            {
                WriteLog($"Saltando verificaciÃ³n: Ãºltima hace {(now - _lastCheckUtc).TotalSeconds:F0}s (cooldown={CheckCooldown.TotalMinutes}m)");
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
        /// Llama este mÃ©todo al arrancar la ventana principal.
        /// Si el ACARS acaba de actualizarse, muestra una notificaciÃ³n breve.
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
                    ShowToast("Actualizacion completada correctamente", "#16B989", 6);
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

        // â”€â”€ Handler principal â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

            WriteLog($"Nueva versiÃ³n {available} detectada. Iniciando descarga silenciosa: {downloadUrl}");

            // Notificar status inicial â†’ la barra del ACARS se hace visible
            UpdateStatusChanged?.Invoke($"Descargando actualizaciÃ³n {installed} → {available}...");
            DownloadProgressChanged?.Invoke(0);

            // Descargar e instalar en background
            Task.Run(async () => await DownloadAndInstallSilentAsync(downloadUrl, available));
        }

        // â”€â”€ Descarga + instalaciÃ³n silenciosa â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

                WriteLog("Descarga completada. Preparando instalaciÃ³n.");

                // Notificar que la descarga terminÃ³
                DownloadProgressChanged?.Invoke(100);
                UpdateStatusChanged?.Invoke($"Instalando {CurrentVersion} → {version}...  El ACARS reabrirÃ¡ automÃ¡ticamente");

                // Esperar 2 s para que el usuario vea el mensaje final
                await Task.Delay(2000);

                // Escribir flag ANTES de cerrar â€” al reabrir el ACARS verÃ¡ que acaba de actualizar
                try { File.WriteAllText(JustUpdatedFlagPath, version); } catch { }
                var splashScriptPath = Path.Combine(tempDir, "show_update_splash.ps1");
                var appExePath = GetInstalledExePath();
                WriteLog($"Relaunch target: {appExePath}");
                File.WriteAllText(splashScriptPath, BuildUpdateSplashScript(installerPath, appExePath, CurrentVersion, version, SplashCloseFlagPath));

                var psi = new ProcessStartInfo("powershell.exe")
                {
                    Arguments       = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{splashScriptPath}\"",
                    UseShellExecute = true,
                    WindowStyle     = ProcessWindowStyle.Hidden,
                    CreateNoWindow  = true
                };

                Process.Start(psi);
                WriteLog("Script de instalaciÃ³n lanzado. Cerrando ACARS.");

                // Cerrar el ACARS en el hilo UI
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    WriteLog("Application.Shutdown() â€” instalador tomarÃ¡ control en ~5s.");
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                WriteLog($"Error en descarga/instalaciÃ³n silenciosa: {ex.Message}");
                UpdateStatusChanged?.Invoke($"Error en actualizaciÃ³n: {ex.Message}");
            }
        }

        // â”€â”€ Toast genÃ©rico (solo se usa para la notificaciÃ³n post-update) â”€â”€â”€â”€â”€â”€â”€â”€

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

        // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
                return ver == null ? "4.0.0" : $"{ver.Major}.{ver.Minor}.{ver.Build}";
            }
            catch { return "4.0.0"; }
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

        private static string GetInstalledExePath()
        {
            const string exeName = "PatagoniaWings.Acars.Master.exe";

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\PatagoniaWings\ACARS");
                var installPath = key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(installPath))
                {
                    var candidate = Path.Combine(installPath.Trim(), exeName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"GetInstalledExePath registry error: {ex.Message}");
            }

            return (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())?.Location
                   ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
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

        private static string BuildUpdateSplashScript(string installerPath, string appExePath, string currentVersion, string version, string splashCloseFlagPath)
        {
            string Escape(string value) => value.Replace("'", "''");
            var appDirectory = Path.GetDirectoryName(appExePath) ?? AppDomain.CurrentDomain.BaseDirectory;

            return
                "Add-Type -AssemblyName System.Windows.Forms\r\n" +
                "Add-Type -AssemblyName System.Drawing\r\n" +
                "[System.Windows.Forms.Application]::EnableVisualStyles()\r\n" +
                "$phrases = @(\r\n" +
                "  'Despachando al equipo de mantenimiento digital...',\r\n" +
                "  'Ajustando remaches invisibles del ACARS...',\r\n" +
                "  'Cargando cafe para el copiloto virtual...',\r\n" +
                "  'Enviando un mecanico a revisar el datalink...',\r\n" +
                "  'Puliendo la cabina para la nueva version...'\r\n" +
                ")\r\n" +
                "$script:installerStarted = $false\r\n" +
                "$script:installerDone = $false\r\n" +
                "$script:appLaunched = $false\r\n" +
                "$script:installerPid = 0\r\n" +
                "$form = New-Object System.Windows.Forms.Form\r\n" +
                "$form.Text = 'Patagonia Wings ACARS'\r\n" +
                "$form.StartPosition = 'CenterScreen'\r\n" +
                "$form.FormBorderStyle = 'None'\r\n" +
                "$form.TopMost = $true\r\n" +
                "$form.Width = 560\r\n" +
                "$form.Height = 230\r\n" +
                "$form.BackColor = [System.Drawing.Color]::FromArgb(10,22,35)\r\n" +
                "$title = New-Object System.Windows.Forms.Label\r\n" +
                "$title.Text = 'Actualizando ACARS v" + Escape(currentVersion) + " → v" + Escape(version) + "'\r\n" +
                "$title.ForeColor = [System.Drawing.Color]::FromArgb(147,197,253)\r\n" +
                "$title.Font = New-Object System.Drawing.Font('Segoe UI',18,[System.Drawing.FontStyle]::Bold)\r\n" +
                "$title.AutoSize = $true\r\n" +
                "$title.Location = New-Object System.Drawing.Point(28,28)\r\n" +
                "$subtitle = New-Object System.Windows.Forms.Label\r\n" +
                "$subtitle.Text = 'Cerrando, instalando y relanzando la version real nueva.'\r\n" +
                "$subtitle.ForeColor = [System.Drawing.Color]::FromArgb(191,219,254)\r\n" +
                "$subtitle.Font = New-Object System.Drawing.Font('Segoe UI',10)\r\n" +
                "$subtitle.AutoSize = $true\r\n" +
                "$subtitle.Location = New-Object System.Drawing.Point(30,74)\r\n" +
                "$fun = New-Object System.Windows.Forms.Label\r\n" +
                "$fun.Text = $phrases[0]\r\n" +
                "$fun.ForeColor = [System.Drawing.Color]::FromArgb(110,231,183)\r\n" +
                "$fun.Font = New-Object System.Drawing.Font('Segoe UI',11,[System.Drawing.FontStyle]::Italic)\r\n" +
                "$fun.AutoSize = $true\r\n" +
                "$fun.Location = New-Object System.Drawing.Point(30,118)\r\n" +
                "$bar = New-Object System.Windows.Forms.ProgressBar\r\n" +
                "$bar.Style = 'Marquee'\r\n" +
                "$bar.MarqueeAnimationSpeed = 35\r\n" +
                "$bar.Width = 500\r\n" +
                "$bar.Height = 18\r\n" +
                "$bar.Location = New-Object System.Drawing.Point(30,160)\r\n" +
                "$form.Controls.AddRange(@($title,$subtitle,$fun,$bar))\r\n" +
                "$script:tick = 0\r\n" +
                "$rotate = New-Object System.Windows.Forms.Timer\r\n" +
                "$rotate.Interval = 1600\r\n" +
                "$rotate.Add_Tick({ $script:tick++; $fun.Text = $phrases[$script:tick % $phrases.Count] })\r\n" +
                "$start = New-Object System.Windows.Forms.Timer\r\n" +
                "$start.Interval = 1800\r\n" +
                "$start.Add_Tick({ " +
                "$start.Stop(); " +
                "$p = Start-Process '" + Escape(installerPath) + "' -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-' -PassThru; " +
                "$script:installerPid = $p.Id; " +
                "$script:installerStarted = $true; " +
                "})\r\n" +
                "$watch = New-Object System.Windows.Forms.Timer\r\n" +
                "$watch.Interval = 1000\r\n" +
                "$watch.Add_Tick({ " +
                "if (Test-Path '" + Escape(splashCloseFlagPath) + "') { try { Remove-Item '" + Escape(splashCloseFlagPath) + "' -Force -ErrorAction SilentlyContinue } catch { }; $watch.Stop(); $form.Close(); return }; " +
                "if ($script:installerStarted -and -not $script:installerDone) { try { $null = Get-Process -Id $script:installerPid -ErrorAction Stop } catch { $script:installerDone = $true } }; " +
                "if ($script:installerDone -and -not $script:appLaunched -and (Test-Path '" + Escape(appExePath) + "')) { Start-Sleep -Seconds 2; Start-Process -FilePath '" + Escape(appExePath) + "' -WorkingDirectory '" + Escape(appDirectory) + "'; $script:appLaunched = $true; $watch.Stop(); $form.Close() } " +
                "})\r\n" +
                "$close = New-Object System.Windows.Forms.Timer\r\n" +
                "$close.Interval = 90000\r\n" +
                "$close.Add_Tick({ $close.Stop(); $watch.Stop(); $form.Close() })\r\n" +
                "$form.Add_Shown({ try { Remove-Item '" + Escape(splashCloseFlagPath) + "' -Force -ErrorAction SilentlyContinue } catch { }; $rotate.Start(); $start.Start(); $watch.Start(); $close.Start() })\r\n" +
                "[void]$form.ShowDialog()\r\n";
        }
    }
}
