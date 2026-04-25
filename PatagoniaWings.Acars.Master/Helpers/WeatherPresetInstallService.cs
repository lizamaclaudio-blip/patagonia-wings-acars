using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace PatagoniaWings.Acars.Master.Helpers
{
    public sealed class WeatherPresetInstallResult
    {
        public bool Success { get; set; }
        public string InstalledPath { get; set; } = string.Empty;
        public List<string> Installed { get; set; } = new List<string>();
        public List<string> Skipped { get; set; } = new List<string>();
        public List<string> Errors { get; set; } = new List<string>();
        public string Message { get; set; } = string.Empty;
    }

    public sealed class WeatherPresetCheckResult
    {
        public bool Found { get; set; }
        public bool HashMatch { get; set; }
        public string BlockMessage { get; set; } = string.Empty;
    }

    public static class WeatherPresetInstallService
    {
        // Nombres de archivo de los presets PWG
        public static readonly IReadOnlyDictionary<string, string> CheckridePresets =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HAB-IFR"]     = "PWG_Checkride_IFR_IMC.WPR",
                ["HAB-CAT-I"]   = "PWG_Checkride_CAT_I.WPR",
                ["HAB-CAT-II"]  = "PWG_Checkride_CAT_II.WPR",
                ["HAB-CAT-III"] = "PWG_Checkride_CAT_III.WPR",
                ["HAB-XWIND"]   = "PWG_Checkride_XWIND.WPR",
                ["HAB-SPECIAL"] = "PWG_Checkride_SPECIAL_AIRPORT.WPR",
            };

        private static string SettingsFilePath
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(appData, "PatagoniaWings", "Acars", "weather-preset-config.json");
            }
        }

        private static string AppPresetsFolder
        {
            get
            {
                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                return Path.Combine(exeDir, "Assets", "Presets");
            }
        }

        // Detecta las rutas candidatas de MSFS
        public static List<string> DetectCandidatePaths()
        {
            var candidates = new List<string>();

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var storePath = Path.Combine(localAppData,
                "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe",
                "LocalState", "Weather", "Presets");

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var steamPath = Path.Combine(appData,
                "Microsoft Flight Simulator", "Weather", "Presets");

            if (Directory.Exists(storePath)) candidates.Add(storePath);
            if (Directory.Exists(steamPath)) candidates.Add(steamPath);

            return candidates;
        }

        // Resuelve la ruta de instalación: salvo config guardada, elige la disponible
        public static string? ResolveInstallPath(string? manualOverride = null)
        {
            if (!string.IsNullOrWhiteSpace(manualOverride) && Directory.Exists(manualOverride))
                return manualOverride;

            var saved = LoadSavedPath();
            if (!string.IsNullOrWhiteSpace(saved) && Directory.Exists(saved))
                return saved;

            var candidates = DetectCandidatePaths();
            if (candidates.Count == 1)
                return candidates[0];

            if (candidates.Count > 1)
            {
                // Elige la modificada más recientemente (instalación más activa)
                return candidates.OrderByDescending(p =>
                    new DirectoryInfo(p).LastWriteTimeUtc).First();
            }

            return null;
        }

        // Instala todos los presets PWG en la ruta MSFS detectada
        public static WeatherPresetInstallResult InstallAll(string? manualPath = null)
        {
            var result = new WeatherPresetInstallResult();

            var targetPath = ResolveInstallPath(manualPath);
            if (targetPath == null)
            {
                result.Success = false;
                result.Message = "No se encontró la carpeta de presets de MSFS. Instala MSFS o proporciona la ruta manualmente.";
                return result;
            }

            try
            {
                Directory.CreateDirectory(targetPath);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"No se pudo crear la carpeta de presets: {ex.Message}";
                return result;
            }

            foreach (var kvp in CheckridePresets)
            {
                var fileName = kvp.Value;
                var sourceFile = Path.Combine(AppPresetsFolder, fileName);
                var destFile = Path.Combine(targetPath, fileName);

                if (!File.Exists(sourceFile))
                {
                    result.Errors.Add($"{fileName}: archivo fuente no encontrado en la instalación del ACARS.");
                    continue;
                }

                try
                {
                    File.Copy(sourceFile, destFile, overwrite: true);
                    result.Installed.Add(fileName);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{fileName}: {ex.Message}");
                }
            }

            result.InstalledPath = targetPath;
            result.Success = result.Errors.Count == 0;
            result.Message = result.Success
                ? $"Presets instalados en: {targetPath}"
                : $"Instalación parcial en: {targetPath}. Errores: {result.Errors.Count}";

            SavePath(targetPath);
            return result;
        }

        // Verifica si el preset requerido para un checkride existe e intacto
        public static WeatherPresetCheckResult VerifyCheckridePreset(string checkrideCode)
        {
            var checkResult = new WeatherPresetCheckResult();

            if (!CheckridePresets.TryGetValue(checkrideCode, out var fileName))
            {
                checkResult.Found = true;
                checkResult.HashMatch = true;
                return checkResult;
            }

            var installPath = ResolveInstallPath();
            if (installPath == null)
            {
                checkResult.BlockMessage =
                    $"Debes instalar el preset climático obligatorio: {fileName}. " +
                    "No se encontró la carpeta de presets de MSFS.";
                return checkResult;
            }

            var targetFile = Path.Combine(installPath, fileName);
            if (!File.Exists(targetFile))
            {
                checkResult.BlockMessage =
                    $"Debes instalar/cargar el preset climático obligatorio: {fileName}. " +
                    "Usa el botón 'Instalar presets' en Ajustes del ACARS.";
                return checkResult;
            }

            checkResult.Found = true;

            // Verificación SHA256 contra el archivo fuente
            var sourceFile = Path.Combine(AppPresetsFolder, fileName);
            if (File.Exists(sourceFile))
            {
                var sourceHash = ComputeSha256(sourceFile);
                var targetHash = ComputeSha256(targetFile);
                checkResult.HashMatch = string.Equals(sourceHash, targetHash, StringComparison.OrdinalIgnoreCase);

                if (!checkResult.HashMatch)
                {
                    checkResult.BlockMessage =
                        $"El preset climático '{fileName}' fue modificado o está desactualizado. " +
                        "Reinstálalo desde Ajustes del ACARS antes de continuar el checkride.";
                }
            }
            else
            {
                checkResult.HashMatch = true; // sin fuente local, no validamos hash
            }

            return checkResult;
        }

        private static string ComputeSha256(string filePath)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var bytes = sha.ComputeHash(stream);
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }

        private static string? LoadSavedPath()
        {
            try
            {
                if (!File.Exists(SettingsFilePath)) return null;
                var json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
                var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                string? path = null;
                data?.TryGetValue("WeatherPresetsPath", out path);
                return path;
            }
            catch { return null; }
        }

        private static void SavePath(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsFilePath)!;
                Directory.CreateDirectory(dir);
                var data = new Dictionary<string, string> { ["WeatherPresetsPath"] = path };
                File.WriteAllText(SettingsFilePath,
                    JsonConvert.SerializeObject(data, Formatting.Indented),
                    Encoding.UTF8);
            }
            catch { }
        }
    }
}
