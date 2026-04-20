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
    /// Flujo de autoupdate interno.
    /// 1. Lee autoupdater.xml publicado.
    /// 2. Compara version remota vs local.
    /// 3. Descarga el instalador nuevo.
    /// 4. Valida que el archivo descargado sea utilizable.
    /// 5. Cierra la app, instala en modo silencioso y relanza.
    /// 6. Al volver, muestra toast de post-update.
    /// </summary>
    public static class UpdateService
    {
        // Fuente unica de deteccion remota. Cada release futura debe actualizar este XML.
        private static string AutoUpdaterXmlUrl =>
            ReadSetting("AutoUpdaterXmlUrl", "https://patagoniaw.com/downloads/autoupdater.xml");

        private static readonly string JustUpdatedFlagPath =
            Path.Combine(Path.GetTempPath(), "PatagoniaWings_JustUpdated.txt");

        private static readonly string SplashCloseFlagPath =
            Path.Combine(Path.GetTempPath(), "PatagoniaWings_UpdateSplashClose.txt");

        // Evita dobles chequeos cuando la ventana se recrea o el usuario vuelve al login.
        private static DateTime _lastCheckUtc = DateTime.MinValue;
        private static readonly TimeSpan CheckCooldown = TimeSpan.FromMinutes(10);
        private const long MinimumInstallerSizeBytes = 1024 * 1024;

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
                // AutoUpdater.NET hace la lectura del XML remoto y dispara OnCheckForUpdate.
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
            if (!IsSupportedInstallerUrl(downloadUrl))
            {
                WriteLog($"Download URL rechazada: {downloadUrl}");
                return;
            }

            WriteLog($"Nueva versiÃ³n {available} detectada. Iniciando descarga silenciosa: {downloadUrl}");

            // La UI se entera desde aqui que ya hay update real y visible para el usuario.
            UpdateStatusChanged?.Invoke($"Descargando actualizaciÃ³n {installed} → {available}...");
            DownloadProgressChanged?.Invoke(0);

            // La descarga y aplicacion corren fuera del hilo UI para no congelar el ACARS.
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

                // Validacion local: antes de cerrar la app confirmamos que el instalador existe,
                // pesa lo esperado y mantiene cabecera PE valida.
                ValidateDownloadedInstaller(installerPath, version);
                WriteLog("Descarga completada. Preparando instalaciÃ³n.");

                // Notificar que la descarga terminÃ³
                DownloadProgressChanged?.Invoke(100);
                UpdateStatusChanged?.Invoke($"Instalando {CurrentVersion} → {version}...  El ACARS reabrirÃ¡ automÃ¡ticamente");

                // Esperar 2 s para que el usuario vea el mensaje final
                await Task.Delay(2000);

                // El flag permite distinguir un inicio normal de un relanzado post-update.
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

                // Cerramos recien cuando el instalador y el script ya estan listos.
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

        private static bool IsSupportedInstallerUrl(string downloadUrl)
        {
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri))
                return false;

            return uri.Scheme == Uri.UriSchemeHttps &&
                   uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateDownloadedInstaller(string installerPath, string version)
        {
            var info = new FileInfo(installerPath);
            if (!info.Exists)
                throw new InvalidOperationException($"No existe el instalador descargado para v{version}.");

            if (info.Length < MinimumInstallerSizeBytes)
                throw new InvalidOperationException($"El instalador descargado para v{version} es demasiado pequeño.");

            using var stream = File.OpenRead(installerPath);
            var header = new byte[2];
            if (stream.Read(header, 0, header.Length) != header.Length || header[0] != 'M' || header[1] != 'Z')
                throw new InvalidOperationException($"El instalador descargado para v{version} no es un ejecutable válido.");
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

            // Este script hace la ultima milla:
            // instala en silencio, espera a que termine y relanza la app nueva sin pedir accion manual.
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
