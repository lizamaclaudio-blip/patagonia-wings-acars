#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// Flujo de autoupdate real:
    /// 1. Detecta version visible + revision interna desde channel.json.
    /// 2. Descarga manifest.json y compara hashes locales vs remotos.
    /// 3. Aplica hot update a assets/reglas/perfiles cuando no hace falta reinicio.
    /// 4. Si hay binarios bloqueados, deja staging seguro y relanza con restart corto.
    /// 5. Mantiene fallback al instalador completo si el delta no es utilizable.
    /// </summary>
    public static class UpdateService
    {
        private sealed class LocalUpdateState
        {
            public string Version { get; set; } = string.Empty;
            public string Revision { get; set; } = string.Empty;
            public string Channel { get; set; } = string.Empty;
            public string InstalledAtUtc { get; set; } = string.Empty;
        }

        private sealed class RemoteManifestFile
        {
            public string Path { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string Sha256 { get; set; } = string.Empty;
            public long Size { get; set; }
            public bool RestartRequired { get; set; }
            public string UpdateMode { get; set; } = string.Empty;
        }

        private sealed class RemoteDeletedFile
        {
            public string Path { get; set; } = string.Empty;
            public bool RestartRequired { get; set; }
        }

        private sealed class RemoteManifestModel
        {
            public string Version { get; set; } = string.Empty;
            public string Revision { get; set; } = string.Empty;
            public string Channel { get; set; } = string.Empty;
            public string InstallerUrl { get; set; } = string.Empty;
            public string Notes { get; set; } = string.Empty;
            public string ReleaseDate { get; set; } = string.Empty;
            public List<RemoteManifestFile> Files { get; set; } = new List<RemoteManifestFile>();
            public List<RemoteDeletedFile> Deleted { get; set; } = new List<RemoteDeletedFile>();
        }

        private static string AutoUpdaterXmlUrl =>
            ReadSetting("AutoUpdaterXmlUrl", "https://qoradagitvccyabfkgkw.supabase.co/storage/v1/object/public/acars-releases/autoupdater.xml");

        private static string UpdateManifestUrl =>
            ReadSetting("UpdateManifestUrl", "https://qoradagitvccyabfkgkw.supabase.co/storage/v1/object/public/acars-releases/acars-update.json");

        private static string UpdateChannelUrl =>
            ReadSetting("UpdateChannelUrl", "https://qoradagitvccyabfkgkw.supabase.co/storage/v1/object/public/acars-releases/channel.json");

        private static string PackagesIndexUrl =>
            ReadSetting("PackagesIndexUrl", "https://qoradagitvccyabfkgkw.supabase.co/storage/v1/object/public/acars-releases/packages/index.json");

        private static string InstallerDownloadUrl =>
            ReadSetting("InstallerDownloadUrl", "https://qoradagitvccyabfkgkw.supabase.co/storage/v1/object/public/acars-releases/PatagoniaWingsACARSSetup.exe");

        private static readonly string AppDataRoot =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PatagoniaWings", "Acars");

        private static readonly string UpdateStatePath =
            Path.Combine(AppDataRoot, "update-state.json");

        private static readonly string JustUpdatedFlagPath =
            Path.Combine(Path.GetTempPath(), "PatagoniaWings_JustUpdated.txt");

        private static readonly string SplashCloseFlagPath =
            Path.Combine(Path.GetTempPath(), "PatagoniaWings_UpdateSplashClose.txt");

        private static DateTime _lastCheckUtc = DateTime.MinValue;
        private static readonly TimeSpan CheckCooldown = TimeSpan.FromMinutes(5);
        private const long MinimumInstallerSizeBytes = 1024 * 1024;
        private static bool _updateInProgress;

        public static event Action<int>? DownloadProgressChanged;
        public static event Action<string>? UpdateStatusChanged;
        public static event Action<string>? UpdateFailed;
        public static event Action<bool>? UpdateCompleted;

        public static bool IsInstallerTakingControl { get; private set; }

        public static string CurrentVersion => ReadSetting("AppVersion", GetAssemblyVersion());

        public static string CurrentRevision
        {
            get
            {
                var state = ReadLocalUpdateState();
                if (!string.IsNullOrWhiteSpace(state.Revision))
                {
                    return state.Revision;
                }

                return ReadSetting("AppRevision", GetAssemblyRevision());
            }
        }

        public static string CurrentChannel => ReadSetting("UpdateChannel", "beta");

        public static void NotifyStartupComplete()
        {
            try
            {
                File.WriteAllText(SplashCloseFlagPath, DateTime.UtcNow.ToString("O"));
            }
            catch
            {
            }
        }

        public static void CheckAndStartUpdate(Window owner)
        {
            _ = NotifyIfUpdateAvailableAsync(owner);
        }

        public static async Task NotifyIfUpdateAvailableAsync(Window owner)
        {
            var check = await CheckForUpdatesAsync().ConfigureAwait(true);
            if (check.Success && check.IsUpdateAvailable)
            {
                StartImmediateUpdate(check);
            }
        }

        public static void StartImmediateUpdate(UpdateCheckResult checkResult)
        {
            if (_updateInProgress)
            {
                WriteLog("StartImmediateUpdate ignored: update already in progress.");
                return;
            }

            _updateInProgress = true;
            IsInstallerTakingControl = false;
            DownloadProgressChanged?.Invoke(0);

            if (checkResult.SupportsDifferential && !string.IsNullOrWhiteSpace(checkResult.ManifestUrl))
            {
                UpdateStatusChanged?.Invoke("Preparando actualizacion diferencial...");
                Task.Run(async () => await ApplyDifferentialUpdateAsync(checkResult));
                return;
            }

            StartImmediateUpdate(checkResult.DownloadUrl, checkResult.LatestVersion, checkResult.LatestRevision);
        }

        public static void StartImmediateUpdate(string downloadUrl, string version)
        {
            StartImmediateUpdate(downloadUrl, version, string.Empty);
        }

        public static void StartImmediateUpdate(string downloadUrl, string version, string revision)
        {
            if (_updateInProgress)
            {
                WriteLog("StartImmediateUpdate(url) ignored: update already in progress.");
                return;
            }

            if (string.IsNullOrWhiteSpace(downloadUrl) || !IsSupportedInstallerUrl(downloadUrl))
            {
                var message = "No se encontro un instalador remoto valido para la actualizacion.";
                WriteLog(message + " => " + downloadUrl);
                UpdateStatusChanged?.Invoke(message);
                UpdateFailed?.Invoke(message);
                return;
            }

            _updateInProgress = true;
            IsInstallerTakingControl = false;
            DownloadProgressChanged?.Invoke(0);
            UpdateStatusChanged?.Invoke("Descargando instalador completo...");
            Task.Run(async () => await DownloadAndInstallSilentAsync(downloadUrl, version, revision));
        }

        public static async Task<UpdateCheckResult> CheckForUpdatesAsync(bool force = false)
        {
            var now = DateTime.UtcNow;
            if (!force && (now - _lastCheckUtc) < CheckCooldown)
            {
                return new UpdateCheckResult
                {
                    Success = true,
                    CurrentVersion = CurrentVersion,
                    CurrentRevision = CurrentRevision,
                    LatestVersion = CurrentVersion,
                    LatestRevision = CurrentRevision,
                    Channel = CurrentChannel,
                };
            }

            _lastCheckUtc = now;

            try
            {
                var differentialCheck = await CheckDifferentialFeedAsync().ConfigureAwait(false);
                if (differentialCheck.Success)
                {
                    return differentialCheck;
                }
            }
            catch (Exception ex)
            {
                WriteLog("Differential feed error: " + ex.Message);
            }

            return await CheckLegacyManifestAsync().ConfigureAwait(false);
        }

        public static void CheckAndShowPostUpdateNotification()
        {
            try
            {
                if (!File.Exists(JustUpdatedFlagPath))
                {
                    return;
                }

                var payload = File.ReadAllText(JustUpdatedFlagPath).Trim();
                File.Delete(JustUpdatedFlagPath);

                // Persistir revisión instalada para que el próximo chequeo compare correctamente
                // Formato del flag: "{version} (rev {revision})"
                TryPersistRevisionFromPayload(payload);

                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    ShowToast("Actualizacion completada correctamente", "#16B989", 6);
                }));

                WriteLog("Post-update notification => " + payload);
            }
            catch (Exception ex)
            {
                WriteLog("Post-update notification error: " + ex.Message);
            }
        }

        private static void TryPersistRevisionFromPayload(string payload)
        {
            try
            {
                // Formato: "6.0.1 (rev 2026.4.25.1)"
                var revIdx = payload.IndexOf("(rev ", StringComparison.OrdinalIgnoreCase);
                var version = revIdx > 0 ? payload.Substring(0, revIdx).Trim() : payload.Trim();
                var revision = revIdx >= 0
                    ? payload.Substring(revIdx + 5).TrimEnd(')', ' ')
                    : string.Empty;

                if (string.IsNullOrWhiteSpace(revision))
                    revision = version;

                WriteLocalUpdateState(version, revision, CurrentChannel);
                WriteLog($"Persisted post-install state: version={version} revision={revision}");
            }
            catch (Exception ex)
            {
                WriteLog("TryPersistRevisionFromPayload error: " + ex.Message);
            }
        }

        public class UpdateCheckResult
        {
            public bool Success { get; set; }
            public bool IsUpdateAvailable { get; set; }
            public bool SupportsDifferential { get; set; }
            public bool RestartRequired { get; set; }
            public string CurrentVersion { get; set; } = string.Empty;
            public string CurrentRevision { get; set; } = string.Empty;
            public string LatestVersion { get; set; } = string.Empty;
            public string LatestRevision { get; set; } = string.Empty;
            public string DownloadUrl { get; set; } = string.Empty;
            public string ManifestUrl { get; set; } = string.Empty;
            public string PackagesIndexUrl { get; set; } = string.Empty;
            public string Channel { get; set; } = string.Empty;
            public string Notes { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
        }

        private static async Task<UpdateCheckResult> CheckDifferentialFeedAsync()
        {
            var channelRaw = await DownloadStringAsync(UpdateChannelUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(channelRaw))
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    Error = "channel.json vacio.",
                    CurrentVersion = CurrentVersion,
                    CurrentRevision = CurrentRevision,
                };
            }

            var channel = JObject.Parse(channelRaw);
            var latestVersion = ReadJsonString(channel, "version", "visibleVersion", "latestVersion");
            var latestRevision = ReadJsonString(channel, "revision", "latestRevision", "buildRevision", "releaseRevision");
            var manifestUrl = ReadJsonString(channel, "manifestUrl", "manifest_url");
            var packagesIndexUrl = ReadJsonString(channel, "packagesIndexUrl", "packages_index_url");
            var installerUrl = ReadJsonString(channel, "installerUrl", "downloadUrl", "download_url");
            var notes = ReadJsonString(channel, "notes", "changelog");
            var channelName = ReadJsonString(channel, "channel") ?? CurrentChannel;

            latestVersion = string.IsNullOrWhiteSpace(latestVersion) ? CurrentVersion : latestVersion!;
            latestRevision = string.IsNullOrWhiteSpace(latestRevision) ? CurrentRevision : latestRevision!;
            installerUrl = string.IsNullOrWhiteSpace(installerUrl) ? InstallerDownloadUrl : installerUrl!;
            packagesIndexUrl = string.IsNullOrWhiteSpace(packagesIndexUrl) ? PackagesIndexUrl : packagesIndexUrl!;

            var forceUpdate = channel.GetValue("forceUpdate", StringComparison.OrdinalIgnoreCase)?.Value<bool?>() ?? false;
            var hasNewVisibleVersion = IsVersionNewer(latestVersion!, CurrentVersion);
            var hasNewRevision = SameVersion(latestVersion!, CurrentVersion) && IsVersionNewer(latestRevision!, CurrentRevision);
            var updateAvailable = forceUpdate == true || hasNewVisibleVersion || hasNewRevision;

            var restartRequired = false;
            var supportsDifferential = false;

            if (updateAvailable && !string.IsNullOrWhiteSpace(manifestUrl))
            {
                var manifest = await LoadRemoteManifestAsync(manifestUrl!).ConfigureAwait(false);
                if (manifest != null)
                {
                    supportsDifferential = manifest.Files.Count > 0;
                    restartRequired = manifest.Files.Any(file => NeedsRestart(file.Path, file.RestartRequired, file.UpdateMode))
                        || manifest.Deleted.Any(file => NeedsRestart(file.Path, file.RestartRequired, "restart"));
                }
            }

            WriteLog($"Differential check => visible={CurrentVersion}->{latestVersion} revision={CurrentRevision}->{latestRevision} available={updateAvailable} differential={supportsDifferential}");

            return new UpdateCheckResult
            {
                Success = true,
                IsUpdateAvailable = updateAvailable,
                SupportsDifferential = supportsDifferential,
                RestartRequired = restartRequired,
                CurrentVersion = CurrentVersion,
                CurrentRevision = CurrentRevision,
                LatestVersion = latestVersion!,
                LatestRevision = latestRevision!,
                DownloadUrl = installerUrl!,
                ManifestUrl = manifestUrl ?? string.Empty,
                PackagesIndexUrl = packagesIndexUrl!,
                Channel = channelName!,
                Notes = notes ?? string.Empty,
            };
        }

        private static async Task<UpdateCheckResult> CheckLegacyManifestAsync()
        {
            try
            {
                var raw = await DownloadStringAsync(UpdateManifestUrl).ConfigureAwait(false);
                var json = JObject.Parse(raw);

                var latestVersion = (json["version"]?.ToString() ?? string.Empty).Trim();
                var latestRevision = (json["revision"]?.ToString() ?? string.Empty).Trim();
                var downloadUrl = (json["downloadUrl"]?.ToString() ?? string.Empty).Trim();
                var channel = (json["channel"]?.ToString() ?? CurrentChannel).Trim();
                var notes = (json["notes"]?.ToString() ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(latestRevision))
                {
                    latestRevision = CurrentRevision;
                }

                return new UpdateCheckResult
                {
                    Success = true,
                    CurrentVersion = CurrentVersion,
                    CurrentRevision = CurrentRevision,
                    LatestVersion = latestVersion,
                    LatestRevision = latestRevision,
                    IsUpdateAvailable = IsVersionNewer(latestVersion, CurrentVersion)
                        || (SameVersion(latestVersion, CurrentVersion) && IsVersionNewer(latestRevision, CurrentRevision)),
                    DownloadUrl = string.IsNullOrWhiteSpace(downloadUrl) ? InstallerDownloadUrl : downloadUrl,
                    Channel = channel,
                    Notes = notes,
                };
            }
            catch (Exception ex)
            {
                WriteLog("Legacy manifest check error: " + ex.Message);
                return new UpdateCheckResult
                {
                    Success = false,
                    CurrentVersion = CurrentVersion,
                    CurrentRevision = CurrentRevision,
                    Error = ex.Message,
                };
            }
        }

        private static async Task ApplyDifferentialUpdateAsync(UpdateCheckResult checkResult)
        {
            try
            {
                var manifest = await LoadRemoteManifestAsync(checkResult.ManifestUrl).ConfigureAwait(false);
                if (manifest == null || manifest.Files.Count == 0)
                {
                    WriteLog("Differential manifest missing or empty. Falling back to installer.");
                    await DownloadAndInstallSilentAsync(checkResult.DownloadUrl, checkResult.LatestVersion, checkResult.LatestRevision).ConfigureAwait(false);
                    return;
                }

                var installRoot = GetInstalledRootDirectory();
                var changedFiles = manifest.Files
                    .Where(file => !string.IsNullOrWhiteSpace(file.Url)
                                   && Uri.IsWellFormedUriString(file.Url, UriKind.Absolute)
                                   && ShouldDownloadFile(file, installRoot))
                    .ToList();
                var deletedFiles = manifest.Deleted
                    .Where(file => File.Exists(Path.Combine(installRoot, NormalizeRelativePath(file.Path))))
                    .ToList();

                // Sin archivos válidos en el diferencial → ir directo al instalador completo
                if (changedFiles.Count == 0 && deletedFiles.Count == 0
                    && manifest.Files.Count > 0 && manifest.Files.All(f => string.IsNullOrWhiteSpace(f.Url)))
                {
                    WriteLog("Differential manifest has no valid URLs. Falling back to full installer.");
                    await DownloadAndInstallSilentAsync(checkResult.DownloadUrl, checkResult.LatestVersion, checkResult.LatestRevision).ConfigureAwait(false);
                    return;
                }

                if (changedFiles.Count == 0 && deletedFiles.Count == 0)
                {
                    WriteLog("Differential manifest already aligned locally.");
                    WriteLocalUpdateState(checkResult.LatestVersion, checkResult.LatestRevision, checkResult.Channel);
                    _updateInProgress = false;
                    UpdateStatusChanged?.Invoke("El cliente ya estaba alineado con la revision remota.");
                    UpdateCompleted?.Invoke(false);
                    return;
                }

                var pendingRoot = Path.Combine(Path.GetTempPath(), "PWAcarsDelta", SanitizePathSegment(checkResult.LatestRevision));
                var filesRoot = Path.Combine(pendingRoot, "files");
                Directory.CreateDirectory(filesRoot);

                var totalBytes = changedFiles.Sum(file => file.Size > 0 ? file.Size : 1L);
                var processedBytes = 0L;
                var restartRequired = false;

                foreach (var file in changedFiles)
                {
                    var relativePath = NormalizeRelativePath(file.Path);
                    UpdateStatusChanged?.Invoke("Sincronizando " + relativePath + "...");

                    var data = await DownloadBytesAsync(file.Url, file.Size).ConfigureAwait(false);
                    ValidateDownloadedPayload(relativePath, data, file.Sha256);

                    if (NeedsRestart(relativePath, file.RestartRequired, file.UpdateMode))
                    {
                        restartRequired = true;
                        var stagedPath = Path.Combine(filesRoot, relativePath);
                        var stagedDir = Path.GetDirectoryName(stagedPath);
                        if (!string.IsNullOrWhiteSpace(stagedDir))
                        {
                            Directory.CreateDirectory(stagedDir);
                        }

                        File.WriteAllBytes(stagedPath, data);
                    }
                    else
                    {
                        ApplyHotFile(installRoot, relativePath, data);
                    }

                    processedBytes += file.Size > 0 ? file.Size : data.LongLength;
                    PublishProgress(processedBytes, totalBytes);
                }

                var restartDeleteList = deletedFiles
                    .Where(file => NeedsRestart(file.Path, file.RestartRequired, "restart"))
                    .Select(file => NormalizeRelativePath(file.Path))
                    .ToList();

                var hotDeleteList = deletedFiles
                    .Where(file => !NeedsRestart(file.Path, file.RestartRequired, "restart"))
                    .Select(file => NormalizeRelativePath(file.Path))
                    .ToList();

                foreach (var relativePath in hotDeleteList)
                {
                    TryDeleteHotFile(installRoot, relativePath);
                }

                if (restartDeleteList.Count > 0)
                {
                    restartRequired = true;
                }

                if (restartRequired)
                {
                    var stagedStatePath = Path.Combine(pendingRoot, "update-state.json");
                    var deleteListPath = Path.Combine(pendingRoot, "delete-list.json");

                    File.WriteAllText(stagedStatePath, BuildStatePayload(checkResult.LatestVersion, checkResult.LatestRevision, checkResult.Channel));
                    File.WriteAllText(deleteListPath, new JArray(restartDeleteList).ToString());

                    var scriptPath = Path.Combine(pendingRoot, "apply-delta.ps1");
                    var appExePath = GetInstalledExePath();
                    File.WriteAllText(
                        scriptPath,
                        BuildDifferentialRestartScript(
                            filesRoot,
                            installRoot,
                            appExePath,
                            stagedStatePath,
                            deleteListPath,
                            SplashCloseFlagPath,
                            CurrentVersion,
                            checkResult.LatestVersion,
                            checkResult.LatestRevision));

                    try
                    {
                        File.WriteAllText(JustUpdatedFlagPath, $"{checkResult.LatestVersion} (rev {checkResult.LatestRevision})");
                    }
                    catch
                    {
                    }

                    var psi = new ProcessStartInfo("powershell.exe")
                    {
                        Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    };

                    Process.Start(psi);
                    IsInstallerTakingControl = true;

                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        WriteLog("Differential restart script launched. Closing app.");
                        Application.Current.Shutdown();
                    });

                    return;
                }

                WriteLocalUpdateState(checkResult.LatestVersion, checkResult.LatestRevision, checkResult.Channel);
                _updateInProgress = false;
                DownloadProgressChanged?.Invoke(100);
                UpdateStatusChanged?.Invoke("Actualizacion aplicada en caliente. Continuando...");
                UpdateCompleted?.Invoke(false);
                WriteLog($"Hot update applied successfully => {checkResult.LatestVersion} rev {checkResult.LatestRevision}");
            }
            catch (Exception ex)
            {
                _updateInProgress = false;
                WriteLog("Differential update error: " + ex.Message);
                UpdateStatusChanged?.Invoke("No se pudo completar la actualizacion diferencial. Se intentara fallback.");

                if (!string.IsNullOrWhiteSpace(checkResult.DownloadUrl) && IsSupportedInstallerUrl(checkResult.DownloadUrl))
                {
                    await DownloadAndInstallSilentAsync(checkResult.DownloadUrl, checkResult.LatestVersion, checkResult.LatestRevision).ConfigureAwait(false);
                    return;
                }

                UpdateFailed?.Invoke(ex.Message);
            }
        }

        private static async Task DownloadAndInstallSilentAsync(string downloadUrl, string version, string revision)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "PWAcarsUpdate");
                Directory.CreateDirectory(tempDir);
                var installerPath = Path.Combine(tempDir, $"PatagoniaWingsACARSSetup-{SanitizePathSegment(version)}.exe");

                WriteLog("Downloading installer => " + installerPath);

                using (var client = new WebClient())
                {
                    client.DownloadProgressChanged += (s, e) =>
                    {
                        DownloadProgressChanged?.Invoke(e.ProgressPercentage);
                    };

                    await client.DownloadFileTaskAsync(new Uri(downloadUrl), installerPath).ConfigureAwait(false);
                }

                ValidateDownloadedInstaller(installerPath, version);
                DownloadProgressChanged?.Invoke(100);
                UpdateStatusChanged?.Invoke("Instalando release completa. El ACARS se relanzara automaticamente.");

                try
                {
                    File.WriteAllText(JustUpdatedFlagPath, $"{version} (rev {revision})");
                }
                catch
                {
                }

                var splashScriptPath = Path.Combine(tempDir, "show_update_splash.ps1");
                var appExePath = GetInstalledExePath();
                File.WriteAllText(
                    splashScriptPath,
                    BuildUpdateSplashScript(installerPath, appExePath, CurrentVersion, version, revision, SplashCloseFlagPath));

                var psi = new ProcessStartInfo("powershell.exe")
                {
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{splashScriptPath}\"",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                Process.Start(psi);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsInstallerTakingControl = true;
                    WriteLog("Installer handoff ready. Closing app.");
                    Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                _updateInProgress = false;
                WriteLog("Installer update error: " + ex.Message);
                UpdateStatusChanged?.Invoke("No se pudo completar la actualizacion. " + ex.Message);
                UpdateFailed?.Invoke(ex.Message);
            }
        }

        private static async Task<RemoteManifestModel?> LoadRemoteManifestAsync(string manifestUrl)
        {
            if (string.IsNullOrWhiteSpace(manifestUrl))
            {
                return null;
            }

            var raw = await DownloadStringAsync(manifestUrl).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var json = JObject.Parse(raw);
            var files = new List<RemoteManifestFile>();
            var deleted = new List<RemoteDeletedFile>();

            foreach (var token in json["files"] as JArray ?? new JArray())
            {
                if (!(token is JObject fileJson))
                {
                    continue;
                }

                var path = ReadJsonString(fileJson, "path");
                var url = ReadJsonString(fileJson, "url");
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(url)
                    || !Uri.IsWellFormedUriString(url, UriKind.Absolute))
                {
                    continue;
                }

                files.Add(new RemoteManifestFile
                {
                    Path = path!,
                    Url = url!,
                    Sha256 = ReadJsonString(fileJson, "sha256", "hash") ?? string.Empty,
                    Size = fileJson.GetValue("size", StringComparison.OrdinalIgnoreCase)?.Value<long?>() ?? 0,
                    RestartRequired = fileJson.GetValue("restartRequired", StringComparison.OrdinalIgnoreCase)?.Value<bool?>() ?? false,
                    UpdateMode = ReadJsonString(fileJson, "updateMode", "mode") ?? string.Empty
                });
            }

            foreach (var token in json["deleted"] as JArray ?? new JArray())
            {
                if (token is JObject deletedJson)
                {
                    var path = ReadJsonString(deletedJson, "path");
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        deleted.Add(new RemoteDeletedFile
                        {
                            Path = path!,
                            RestartRequired = deletedJson.GetValue("restartRequired", StringComparison.OrdinalIgnoreCase)?.Value<bool?>() ?? false
                        });
                    }
                }
                else if (token.Type == JTokenType.String)
                {
                    deleted.Add(new RemoteDeletedFile
                    {
                        Path = token.ToString()
                    });
                }
            }

            return new RemoteManifestModel
            {
                Version = ReadJsonString(json, "version") ?? string.Empty,
                Revision = ReadJsonString(json, "revision") ?? string.Empty,
                Channel = ReadJsonString(json, "channel") ?? string.Empty,
                InstallerUrl = ReadJsonString(json, "installerUrl", "downloadUrl") ?? string.Empty,
                Notes = ReadJsonString(json, "notes", "changelog") ?? string.Empty,
                ReleaseDate = ReadJsonString(json, "releaseDate") ?? string.Empty,
                Files = files,
                Deleted = deleted
            };
        }

        private static bool ShouldDownloadFile(RemoteManifestFile file, string installRoot)
        {
            var relativePath = NormalizeRelativePath(file.Path);
            var targetPath = Path.Combine(installRoot, relativePath);
            if (!File.Exists(targetPath))
            {
                return true;
            }

            var expectedHash = (file.Sha256 ?? string.Empty).Trim();
            if (expectedHash.Length == 0)
            {
                return true;
            }

            try
            {
                var localHash = ComputeSha256FromFile(targetPath);
                return !string.Equals(localHash, expectedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                WriteLog("Hash compare error => " + relativePath + " => " + ex.Message);
                return true;
            }
        }

        private static void ApplyHotFile(string installRoot, string relativePath, byte[] payload)
        {
            var normalized = NormalizeRelativePath(relativePath);
            var targetPath = Path.Combine(installRoot, normalized);
            var targetDirectory = Path.GetDirectoryName(targetPath);

            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.WriteAllBytes(targetPath, payload);
        }

        private static void TryDeleteHotFile(string installRoot, string relativePath)
        {
            try
            {
                var normalized = NormalizeRelativePath(relativePath);
                var targetPath = Path.Combine(installRoot, normalized);
                if (!File.Exists(targetPath))
                {
                    return;
                }

                File.Delete(targetPath);
            }
            catch (Exception ex)
            {
                WriteLog("Hot delete failed => " + relativePath + " => " + ex.Message);
            }
        }

        private static void ValidateDownloadedInstaller(string installerPath, string version)
        {
            var info = new FileInfo(installerPath);
            if (!info.Exists)
            {
                throw new InvalidOperationException($"No existe el instalador descargado para {version}.");
            }

            if (info.Length < MinimumInstallerSizeBytes)
            {
                throw new InvalidOperationException($"El instalador descargado para {version} es demasiado pequeno.");
            }

            using (var stream = File.OpenRead(installerPath))
            {
                var header = new byte[2];
                if (stream.Read(header, 0, header.Length) != header.Length || header[0] != 'M' || header[1] != 'Z')
                {
                    throw new InvalidOperationException($"El instalador descargado para {version} no es un ejecutable valido.");
                }
            }
        }

        private static void ValidateDownloadedPayload(string relativePath, byte[] payload, string expectedSha256)
        {
            if (payload == null || payload.Length == 0)
            {
                throw new InvalidOperationException("Payload vacio para " + relativePath);
            }

            if (string.IsNullOrWhiteSpace(expectedSha256))
            {
                return;
            }

            var actualSha256 = ComputeSha256FromBytes(payload);
            if (!string.Equals(actualSha256, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Hash invalido para " + relativePath);
            }
        }

        private static void PublishProgress(long processedBytes, long totalBytes)
        {
            if (totalBytes <= 0)
            {
                DownloadProgressChanged?.Invoke(100);
                return;
            }

            var progress = (int)Math.Round((processedBytes / (double)totalBytes) * 100d, MidpointRounding.AwayFromZero);
            DownloadProgressChanged?.Invoke(Math.Max(0, Math.Min(100, progress)));
        }

        private static LocalUpdateState ReadLocalUpdateState()
        {
            try
            {
                if (!File.Exists(UpdateStatePath))
                {
                    return new LocalUpdateState();
                }

                var raw = File.ReadAllText(UpdateStatePath);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return new LocalUpdateState();
                }

                var json = JObject.Parse(raw);
                return new LocalUpdateState
                {
                    Version = json["version"]?.ToString() ?? string.Empty,
                    Revision = json["revision"]?.ToString() ?? string.Empty,
                    Channel = json["channel"]?.ToString() ?? string.Empty,
                    InstalledAtUtc = json["installedAtUtc"]?.ToString() ?? string.Empty
                };
            }
            catch (Exception ex)
            {
                WriteLog("ReadLocalUpdateState error: " + ex.Message);
                return new LocalUpdateState();
            }
        }

        private static void WriteLocalUpdateState(string version, string revision, string channel)
        {
            try
            {
                Directory.CreateDirectory(AppDataRoot);
                File.WriteAllText(UpdateStatePath, BuildStatePayload(version, revision, channel));
            }
            catch (Exception ex)
            {
                WriteLog("WriteLocalUpdateState error: " + ex.Message);
            }
        }

        private static string BuildStatePayload(string version, string revision, string channel)
        {
            return new JObject
            {
                ["version"] = version,
                ["revision"] = revision,
                ["channel"] = channel,
                ["installedAtUtc"] = DateTime.UtcNow.ToString("O")
            }.ToString();
        }

        private static bool IsVersionNewer(string available, string installed)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(available))
                {
                    return false;
                }

                var normalizedAvailable = NormalizeVersion(available);
                var normalizedInstalled = NormalizeVersion(installed);

                Version a;
                Version b;
                if (!Version.TryParse(normalizedAvailable, out a) || !Version.TryParse(normalizedInstalled, out b))
                {
                    return !string.Equals(available.Trim(), installed.Trim(), StringComparison.OrdinalIgnoreCase);
                }

                return a > b;
            }
            catch (Exception ex)
            {
                WriteLog("IsVersionNewer error: " + ex.Message);
                return false;
            }
        }

        private static bool SameVersion(string left, string right)
        {
            return string.Equals(NormalizeVersion(left), NormalizeVersion(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeVersion(string value)
        {
            var raw = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
            var suffix = raw.IndexOfAny(new[] { '-', '+', ' ' });
            if (suffix >= 0)
            {
                raw = raw.Substring(0, suffix);
            }

            var source = raw.Split('.');
            var normalized = new string[4];

            for (var i = 0; i < normalized.Length; i++)
            {
                normalized[i] = i < source.Length ? LeadingDigitsOrZero(source[i]) : "0";
            }

            return string.Join(".", normalized);
        }

        private static string LeadingDigitsOrZero(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "0";
            }

            var raw = value.Trim();
            var end = 0;
            while (end < raw.Length && char.IsDigit(raw[end]))
            {
                end++;
            }

            return end == 0 ? "0" : raw.Substring(0, end);
        }

        private static bool NeedsRestart(string relativePath, bool restartRequired, string updateMode)
        {
            if (restartRequired)
            {
                return true;
            }

            if (string.Equals(updateMode, "restart", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var extension = Path.GetExtension(relativePath) ?? string.Empty;
            switch (extension.ToLowerInvariant())
            {
                case ".exe":
                case ".dll":
                case ".config":
                case ".pdb":
                    return true;
            }

            return false;
        }

        private static bool IsSupportedInstallerUrl(string downloadUrl)
        {
            Uri uri;
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out uri))
            {
                return false;
            }

            return uri.Scheme == Uri.UriSchemeHttps &&
                   uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRelativePath(string value)
        {
            return (value ?? string.Empty)
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
        }

        private static string SanitizePathSegment(string value)
        {
            var raw = string.IsNullOrWhiteSpace(value) ? "current" : value.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                raw = raw.Replace(invalid, '_');
            }

            return raw.Replace(" ", "_");
        }

        private static string ComputeSha256FromFile(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static string ComputeSha256FromBytes(byte[] payload)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(payload);
                return BitConverter.ToString(hash).Replace("-", string.Empty);
            }
        }

        private static async Task<string> DownloadStringAsync(string url)
        {
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                client.Encoding = Encoding.UTF8;
                return await client.DownloadStringTaskAsync(new Uri(url)).ConfigureAwait(false);
            }
        }

        private static async Task<byte[]> DownloadBytesAsync(string url, long expectedSize = 0)
        {
            using (var client = new WebClient())
            {
                client.Headers[HttpRequestHeader.CacheControl] = "no-cache";
                if (expectedSize > 0)
                {
                    client.DownloadProgressChanged += (s, e) =>
                        DownloadProgressChanged?.Invoke(e.ProgressPercentage);
                }
                var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(3));
                var tcs = new System.Threading.Tasks.TaskCompletionSource<byte[]>();
                cts.Token.Register(() => { try { client.CancelAsync(); } catch { } tcs.TrySetException(new TimeoutException("Descarga superó 3 minutos.")); });
                var task = client.DownloadDataTaskAsync(new Uri(url));
                var completed = await Task.WhenAny(task, tcs.Task).ConfigureAwait(false);
                return await completed.ConfigureAwait(false);
            }
        }

        private static string GetAssemblyVersion()
        {
            try
            {
                var version = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())?.GetName()?.Version;
                return version == null ? "4.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
            }
            catch
            {
                return "4.0.0";
            }
        }

        private static string GetAssemblyRevision()
        {
            try
            {
                var v = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())?.GetName()?.Version;
                return v == null ? "0.0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
            catch
            {
                return "0.0.0.0";
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

        private static string GetInstalledExePath()
        {
            const string exeName = "PatagoniaWings.Acars.Master.exe";

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\PatagoniaWings\ACARS"))
                {
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
            }
            catch (Exception ex)
            {
                WriteLog("GetInstalledExePath registry error: " + ex.Message);
            }

            return (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())?.Location
                   ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, exeName);
        }

        private static string GetInstalledRootDirectory()
        {
            var exePath = GetInstalledExePath();
            return Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
        }

        private static string? ReadJsonString(JObject json, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = json.GetValue(key, StringComparison.OrdinalIgnoreCase)?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private static void WriteLog(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(AppDataRoot, "logs"));
                File.AppendAllText(
                    Path.Combine(AppDataRoot, "logs", "autoupdater.log"),
                    $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        private static void ShowToast(string message, string hexColor, int durationSeconds = 4)
        {
            try
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);

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
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    try
                    {
                        toast.Close();
                    }
                    catch
                    {
                    }
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                WriteLog("ShowToast error: " + ex.Message);
            }
        }

        private static string BuildUpdateSplashScript(string installerPath, string appExePath, string currentVersion, string version, string revision, string splashCloseFlagPath)
        {
            string Escape(string value) => value.Replace("'", "''");
            var appDirectory = Path.GetDirectoryName(appExePath) ?? AppDomain.CurrentDomain.BaseDirectory;

            return
                "Add-Type -AssemblyName System.Windows.Forms\r\n" +
                "Add-Type -AssemblyName System.Drawing\r\n" +
                "[System.Windows.Forms.Application]::EnableVisualStyles()\r\n" +
                "$script:installerStarted = $false\r\n" +
                "$script:installerDone = $false\r\n" +
                "$script:appLaunched = $false\r\n" +
                "$script:installerPid = 0\r\n" +
                "$form = New-Object System.Windows.Forms.Form\r\n" +
                "$form.Text = 'Patagonia Wings ACARS'\r\n" +
                "$form.StartPosition = 'CenterScreen'\r\n" +
                "$form.FormBorderStyle = 'None'\r\n" +
                "$form.TopMost = $true\r\n" +
                "$form.Width = 540\r\n" +
                "$form.Height = 310\r\n" +
                "$form.BackColor = [System.Drawing.Color]::FromArgb(9,17,28)\r\n" +
                "$title = New-Object System.Windows.Forms.Label\r\n" +
                "$title.Text = 'Actualizando Patagonia Wings ACARS'\r\n" +
                "$title.ForeColor = [System.Drawing.Color]::FromArgb(147,197,253)\r\n" +
                "$title.Font = New-Object System.Drawing.Font('Segoe UI',18,[System.Drawing.FontStyle]::Bold)\r\n" +
                "$title.AutoSize = $true\r\n" +
                "$title.Location = New-Object System.Drawing.Point(24,24)\r\n" +
                "$subtitle = New-Object System.Windows.Forms.Label\r\n" +
                "$subtitle.Text = 'Version " + Escape(currentVersion) + " -> " + Escape(version) + " | revision " + Escape(revision) + "'\r\n" +
                "$subtitle.ForeColor = [System.Drawing.Color]::FromArgb(191,219,254)\r\n" +
                "$subtitle.Font = New-Object System.Drawing.Font('Segoe UI',10)\r\n" +
                "$subtitle.AutoSize = $true\r\n" +
                "$subtitle.Location = New-Object System.Drawing.Point(26,66)\r\n" +
                "$status = New-Object System.Windows.Forms.Label\r\n" +
                "$status.Text = 'Instalando release completa y preparando relanzamiento automatico...'\r\n" +
                "$status.ForeColor = [System.Drawing.Color]::FromArgb(110,231,183)\r\n" +
                "$status.Font = New-Object System.Drawing.Font('Segoe UI',11,[System.Drawing.FontStyle]::Regular)\r\n" +
                "$status.AutoSize = $true\r\n" +
                "$status.Location = New-Object System.Drawing.Point(26,118)\r\n" +
                "$bar = New-Object System.Windows.Forms.ProgressBar\r\n" +
                "$bar.Style = 'Marquee'\r\n" +
                "$bar.MarqueeAnimationSpeed = 35\r\n" +
                "$bar.Width = 490\r\n" +
                "$bar.Height = 20\r\n" +
                "$bar.Location = New-Object System.Drawing.Point(24,160)\r\n" +
                "$form.Controls.AddRange(@($title,$subtitle,$status,$bar))\r\n" +
                "$watch = New-Object System.Windows.Forms.Timer\r\n" +
                "$watch.Interval = 1000\r\n" +
                "$watch.Add_Tick({ " +
                "if (Test-Path '" + Escape(splashCloseFlagPath) + "') { try { Remove-Item '" + Escape(splashCloseFlagPath) + "' -Force -ErrorAction SilentlyContinue } catch { }; $watch.Stop(); $form.Close(); return }; " +
                "if ($script:installerStarted -and -not $script:installerDone) { try { $null = Get-Process -Id $script:installerPid -ErrorAction Stop } catch { $script:installerDone = $true } }; " +
                "if ($script:installerDone -and -not $script:appLaunched -and (Test-Path '" + Escape(appExePath) + "')) { Start-Process -FilePath '" + Escape(appExePath) + "' -WorkingDirectory '" + Escape(appDirectory) + "'; $script:appLaunched = $true; $watch.Stop(); $form.Close() } " +
                "})\r\n" +
                "$close = New-Object System.Windows.Forms.Timer\r\n" +
                "$close.Interval = 90000\r\n" +
                "$close.Add_Tick({ $close.Stop(); $watch.Stop(); $form.Close() })\r\n" +
                "$form.Add_Shown({ try { Remove-Item '" + Escape(splashCloseFlagPath) + "' -Force -ErrorAction SilentlyContinue } catch { }; $p = Start-Process '" + Escape(installerPath) + "' -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-' -PassThru; $script:installerPid = $p.Id; $script:installerStarted = $true; $watch.Start(); $close.Start() })\r\n" +
                "[void]$form.ShowDialog()\r\n";
        }

        private static string BuildDifferentialRestartScript(string stagedFilesRoot, string appRoot, string appExePath, string stagedStatePath, string deleteListPath, string splashCloseFlagPath, string currentVersion, string version, string revision)
        {
            string Escape(string value) => value.Replace("'", "''");
            return
                "$exe = '" + Escape(appExePath) + "'\r\n" +
                "$src = '" + Escape(stagedFilesRoot) + "'\r\n" +
                "$tgt = '" + Escape(appRoot) + "'\r\n" +
                "$d = (Get-Date).AddSeconds(30)\r\n" +
                "while ((Get-Date) -lt $d) {\r\n" +
                "  try { $s = [IO.File]::Open($exe,'Open','Read','None'); $s.Close(); break } catch { Start-Sleep -Milliseconds 300 }\r\n" +
                "}\r\n" +
                "Start-Sleep -Milliseconds 600\r\n" +
                "if (Test-Path $src) {\r\n" +
                "  Get-ChildItem -Path $src -Recurse -File | ForEach-Object {\r\n" +
                "    $r = $_.FullName.Substring($src.Length).TrimStart('\\')\r\n" +
                "    $t = Join-Path $tgt $r\r\n" +
                "    $p = Split-Path $t -Parent\r\n" +
                "    if ($p) { New-Item -ItemType Directory -Path $p -Force | Out-Null }\r\n" +
                "    Copy-Item -LiteralPath $_.FullName -Destination $t -Force\r\n" +
                "  }\r\n" +
                "}\r\n" +
                "if (Test-Path '" + Escape(stagedStatePath) + "') { Copy-Item -LiteralPath '" + Escape(stagedStatePath) + "' -Destination '" + Escape(UpdateStatePath) + "' -Force }\r\n" +
                "Start-Process -FilePath $exe -WorkingDirectory $tgt\r\n";
        }
    }
}
