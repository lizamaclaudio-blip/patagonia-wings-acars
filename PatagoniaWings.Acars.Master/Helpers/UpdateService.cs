#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace PatagoniaWings.Acars.Master.Helpers
{
    public sealed class UpdateCheckResult
    {
        public bool Success { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public bool Mandatory { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    internal sealed class UpdateManifest
    {
        public string version { get; set; } = string.Empty;
        public string downloadUrl { get; set; } = string.Empty;
        public string notes { get; set; } = string.Empty;
        public bool mandatory { get; set; }
    }

    // Fila de la tabla acars_releases en Supabase
    internal sealed class AcarsReleaseRow
    {
        public string version { get; set; } = string.Empty;
        public string download_url { get; set; } = string.Empty;
        public string notes { get; set; } = string.Empty;
        public bool mandatory { get; set; }
        public bool is_active { get; set; }
    }

    public static class UpdateService
    {
        private const string DefaultManifestUrl = "https://www.patagoniaw.com/downloads/acars-update.json";
        private const string DefaultInstallerUrl = "https://www.patagoniaw.com/downloads/PatagoniaWingsACARSSetup.exe";

        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();
        private static bool _checkedThisSession;
        private static UpdateCheckResult? _cachedResult;

        public static string CurrentVersion => GetAssemblyVersion();

        public static async Task NotifyIfUpdateAvailableAsync(Window owner)
        {
            try
            {
                var result = await CheckForUpdatesAsync();
                if (!result.Success || !result.IsUpdateAvailable)
                {
                    return;
                }

                var message =
                    "Hay una nueva version de Patagonia Wings ACARS disponible." + Environment.NewLine + Environment.NewLine +
                    "Version actual: " + result.CurrentVersion + Environment.NewLine +
                    "Nueva version: " + result.LatestVersion + Environment.NewLine + Environment.NewLine +
                    "La aplicacion puede descargar e instalar esta actualizacion automaticamente." + Environment.NewLine + Environment.NewLine +
                    (string.IsNullOrWhiteSpace(result.Notes)
                        ? "Quieres actualizar ahora?"
                        : result.Notes + Environment.NewLine + Environment.NewLine + "Quieres actualizar ahora?");

                var response = MessageBox.Show(
                    owner,
                    message,
                    "Actualizacion disponible",
                    result.Mandatory ? MessageBoxButton.OKCancel : MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (response == MessageBoxResult.Yes || response == MessageBoxResult.OK)
                {
                    await DownloadAndLaunchInstallerAsync(owner, result);
                }
                else if (result.Mandatory)
                {
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                WriteUpdateLog("NotifyIfUpdateAvailableAsync error: " + ex);
            }
        }

        public static async Task<UpdateCheckResult> CheckForUpdatesAsync(bool force = false)
        {
            if (!force && _checkedThisSession && _cachedResult != null)
            {
                return _cachedResult;
            }

            _checkedThisSession = true;

            var result = new UpdateCheckResult { CurrentVersion = CurrentVersion };

            // 1. Intentar desde Supabase (fuente principal)
            var supabaseResult = await TryCheckFromSupabaseAsync(result.CurrentVersion);
            if (supabaseResult != null)
            {
                _cachedResult = supabaseResult;
                return supabaseResult;
            }

            // 2. Fallback al JSON estático
            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
                {
                    var manifestUrl = BuildManifestRequestUrl(ReadSetting("UpdateManifestUrl", DefaultManifestUrl));
                    WriteUpdateLog("Fallback: checking updates at " + manifestUrl);

                    var json = await client.GetStringAsync(manifestUrl);
                    var manifest = Serializer.Deserialize<UpdateManifest>(json);

                    if (manifest == null || string.IsNullOrWhiteSpace(manifest.version))
                    {
                        result.Error = "Manifest vacío o inválido.";
                        _cachedResult = result;
                        return result;
                    }

                    result.Success = true;
                    result.LatestVersion = manifest.version.Trim();
                    result.Mandatory = manifest.mandatory;
                    result.DownloadUrl = string.IsNullOrWhiteSpace(manifest.downloadUrl)
                        ? ReadSetting("InstallerDownloadUrl", DefaultInstallerUrl)
                        : manifest.downloadUrl.Trim();
                    result.Notes = (manifest.notes ?? string.Empty).Trim();

                    CompareVersions(result);
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                WriteUpdateLog("CheckForUpdatesAsync fallback error: " + ex);
            }

            _cachedResult = result;
            return result;
        }

        private static async Task<UpdateCheckResult?> TryCheckFromSupabaseAsync(string currentVersion)
        {
            try
            {
                var supabaseUrl = ReadSetting("SupabaseUrl", string.Empty);
                var supabaseKey = ReadSetting("SupabaseAnonKey", string.Empty);

                if (string.IsNullOrWhiteSpace(supabaseUrl) || string.IsNullOrWhiteSpace(supabaseKey))
                    return null;

                var endpoint = supabaseUrl + "/rest/v1/acars_releases?select=*&is_active=eq.true&order=version.desc&limit=1";

                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) })
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", supabaseKey);
                    client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer " + supabaseKey);

                    WriteUpdateLog("Checking updates from Supabase: " + endpoint);

                    var json = await client.GetStringAsync(endpoint);
                    var rows = Serializer.Deserialize<List<AcarsReleaseRow>>(json);

                    if (rows == null || rows.Count == 0)
                    {
                        WriteUpdateLog("Supabase: no releases found");
                        return null;
                    }

                    var row = rows[0];

                    if (string.IsNullOrWhiteSpace(row.version))
                        return null;

                    var result = new UpdateCheckResult
                    {
                        CurrentVersion = currentVersion,
                        Success = true,
                        LatestVersion = row.version.Trim(),
                        Mandatory = row.mandatory,
                        DownloadUrl = string.IsNullOrWhiteSpace(row.download_url)
                            ? ReadSetting("InstallerDownloadUrl", DefaultInstallerUrl)
                            : row.download_url.Trim(),
                        Notes = (row.notes ?? string.Empty).Trim()
                    };

                    CompareVersions(result);
                    WriteUpdateLog($"Supabase: latest={result.LatestVersion} current={result.CurrentVersion} updateAvailable={result.IsUpdateAvailable}");
                    return result;
                }
            }
            catch (Exception ex)
            {
                WriteUpdateLog("TryCheckFromSupabaseAsync error: " + ex.Message);
                return null;
            }
        }

        private static void CompareVersions(UpdateCheckResult result)
        {
            var currentOk = Version.TryParse(NormalizeVersion(result.CurrentVersion), out var current);
            var latestOk = Version.TryParse(NormalizeVersion(result.LatestVersion), out var latest);

            if (currentOk && latestOk)
                result.IsUpdateAvailable = latest > current;
            else
                result.Error = "No se pudo comparar versiones.";
        }

        private static async Task DownloadAndLaunchInstallerAsync(Window owner, UpdateCheckResult result)
        {
            var downloadUrl = string.IsNullOrWhiteSpace(result.DownloadUrl)
                ? ReadSetting("InstallerDownloadUrl", DefaultInstallerUrl)
                : result.DownloadUrl;

            var updatesFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PatagoniaWings",
                "Acars",
                "updates");

            Directory.CreateDirectory(updatesFolder);

            var fileName = "PatagoniaWingsACARSSetup-" + SanitizeFileVersion(result.LatestVersion) + ".exe";
            var installerPath = Path.Combine(updatesFolder, fileName);

            try
            {
                owner.Cursor = System.Windows.Input.Cursors.Wait;
                WriteUpdateLog("Downloading installer from " + downloadUrl + " to " + installerPath);

                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
                using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    using (var input = await response.Content.ReadAsStreamAsync())
                    using (var output = new FileStream(installerPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await input.CopyToAsync(output);
                    }
                }

                WriteUpdateLog("Installer downloaded successfully.");

                Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    WorkingDirectory = Path.GetDirectoryName(installerPath),
                    UseShellExecute = true
                });

                WriteUpdateLog("Installer launched. Closing app for update.");
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                WriteUpdateLog("DownloadAndLaunchInstallerAsync error: " + ex);
                owner.Cursor = null;

                var response = MessageBox.Show(
                    owner,
                    "No pude descargar o abrir la actualizacion automaticamente." + Environment.NewLine + Environment.NewLine +
                    "Quieres abrir la descarga manual en el navegador?",
                    "Actualizacion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (response == MessageBoxResult.Yes)
                {
                    OpenDownload(downloadUrl);
                }

                return;
            }
            finally
            {
                owner.Cursor = null;
            }
        }

        private static void OpenDownload(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                url = ReadSetting("InstallerDownloadUrl", DefaultInstallerUrl);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private static string BuildManifestRequestUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return DefaultManifestUrl;
            }

            var separator = baseUrl.Contains("?") ? "&" : "?";
            return baseUrl + separator + "ts=" + DateTime.UtcNow.Ticks;
        }

        private static string NormalizeVersion(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "0.0.0" : value.Trim();
        }

        private static string SanitizeFileVersion(string value)
        {
            var normalized = NormalizeVersion(value);
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                normalized = normalized.Replace(invalid, '_');
            }

            return normalized.Replace(' ', '_');
        }

        private static string GetAssemblyVersion()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                if (version == null)
                {
                    return "0.0.0";
                }

                return version.Build >= 0
                    ? string.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build)
                    : string.Format("{0}.{1}", version.Major, version.Minor);
            }
            catch
            {
                return "0.0.0";
            }
        }

        private static string ReadSetting(string key, string fallback)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            }
            catch
            {
                return fallback;
            }
        }

        private static void WriteUpdateLog(string message)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var logFolder = Path.Combine(appData, "PatagoniaWings", "Acars", "logs");
                Directory.CreateDirectory(logFolder);
                var logFile = Path.Combine(logFolder, "update.log");
                File.AppendAllText(logFile, "[" + DateTime.UtcNow.ToString("o") + "] " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
