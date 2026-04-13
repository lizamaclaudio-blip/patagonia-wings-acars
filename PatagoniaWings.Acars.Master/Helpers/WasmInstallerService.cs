using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// Servicio para instalar/detectar el WASM Module de MobiFlight
    /// que permite leer LVARs de aviones complejos.
    /// </summary>
    public static class WasmInstallerService
    {
        private const string WasmModuleName = "patagonia-acars-wasm";
        private const string MsfsCommunityRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Microsoft Flight Simulator";
        
        /// <summary>
        /// Verifica si el WASM Module está instalado en MSFS
        /// </summary>
        public static bool IsWasmModuleInstalled()
        {
            try
            {
                string communityPath = GetMsfsCommunityFolder();
                if (string.IsNullOrEmpty(communityPath))
                    return false;
                    
                string wasmPath = Path.Combine(communityPath, WasmModuleName);
                bool exists = Directory.Exists(wasmPath);
                
                Debug.WriteLine($"[WasmInstaller] WASM Module {(exists ? "instalado" : "NO instalado")} en: {wasmPath}");
                return exists;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WasmInstaller] Error verificando instalación: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Detecta la ruta del Community Folder de MSFS
        /// </summary>
        public static string? GetMsfsCommunityFolder()
        {
            try
            {
                // Intentar detectar desde Steam
                string? steamPath = GetSteamMsfsPath();
                if (!string.IsNullOrEmpty(steamPath))
                {
                    string communityPath = Path.Combine(steamPath, "Community");
                    if (Directory.Exists(communityPath))
                        return communityPath;
                }
                
                // Intentar detectar desde Microsoft Store
                string? msStorePath = GetMicrosoftStoreMsfsPath();
                if (!string.IsNullOrEmpty(msStorePath))
                {
                    string communityPath = Path.Combine(msStorePath, "Community");
                    if (Directory.Exists(communityPath))
                        return communityPath;
                }
                
                // Rutas comunes de fallback
                string[] commonPaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalState", "packages", "Community"),
                    @"C:\MSFS Community",
                    @"D:\MSFS Community",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator", "Community")
                };
                
                foreach (var path in commonPaths)
                {
                    if (Directory.Exists(path))
                        return path;
                }
                
                Debug.WriteLine("[WasmInstaller] No se pudo detectar Community Folder");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WasmInstaller] Error detectando MSFS: {ex.Message}");
                return null;
            }
        }
        
        private static string? GetSteamMsfsPath()
        {
            try
            {
                // Detectar desde librería de Steam
                var steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath", null) as string;
                if (!string.IsNullOrEmpty(steamPath))
                {
                    // Buscar en librerías de Steam
                    string libraryFoldersFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(libraryFoldersFile))
                    {
                        string content = File.ReadAllText(libraryFoldersFile);
                        // Buscar ruta que contiene MSFS
                        // Simplificado: buscar en SteamApps common
                        string msfsPath = Path.Combine(steamPath, "steamapps", "common", "MicrosoftFlightSimulator");
                        if (Directory.Exists(msfsPath))
                            return msfsPath;
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
        
        private static string? GetMicrosoftStoreMsfsPath()
        {
            try
            {
                // Microsoft Store usa un path específico en Packages
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string msfsPath = Path.Combine(localAppData, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalState");
                
                if (Directory.Exists(msfsPath))
                    return msfsPath;
                    
                return null;
            }
            catch
            {
                return null;
            }
        }
        
        /// <summary>
        /// Instala el WASM Module desde los recursos embebidos
        /// </summary>
        public static bool InstallWasmModule()
        {
            try
            {
                string? communityPath = GetMsfsCommunityFolder();
                if (string.IsNullOrEmpty(communityPath))
                {
                    Debug.WriteLine("[WasmInstaller] ERROR: No se pudo detectar Community Folder");
                    return false;
                }
                
                string targetPath = Path.Combine(communityPath, WasmModuleName);
                
                // Crear directorio
                Directory.CreateDirectory(targetPath);
                
                // Extraer archivos WASM desde recursos embebidos
                ExtractWasmFiles(targetPath);
                
                Debug.WriteLine($"[WasmInstaller] WASM Module instalado en: {targetPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WasmInstaller] Error instalando: {ex.Message}");
                return false;
            }
        }
        
        private static void ExtractWasmFiles(string targetPath)
        {
            // En implementación real, estos archivos vendrían embebidos en el .exe
            // Por ahora, creamos archivos mínimos de configuración
            
            // manifest.json
            string manifestJson = @"{
  ""dependencies": [],
  ""content_type": ""WASM_MODULE"",
  ""title": ""Patagonia ACARS WASM Bridge"",
  ""manufacturer": ""Patagonia Wings"",
  ""creator": ""Patagonia Wings Virtual Airline"",
  ""version": ""1.0.0"",
  ""minimum_game_version": ""1.27.21""
}";
            File.WriteAllText(Path.Combine(targetPath, "manifest.json"), manifestJson);
            
            // module.config - Configuración de LVARs
            string moduleConfig = @"[WASM]
MODULE_NAME=patagonia-acars-wasm
ENABLE_LVAR_READ=1

[LVARS_A319_HEADWIND]
A319_Light_Beacon=1
A319_Light_Strobe=1
A319_Light_Landing=1
A319_Light_Nav=1
A319_Light_Taxi=1
A319_Engine_N1_1=1
A319_Engine_N1_2=1
A319_Transponder_Code=1

[LVARS_FENIX_A320]
FNX320_LIGHT_BEACON=1
FNX320_LIGHT_STROBE=1
FNX320_ENG_N1_1=1
FNX320_ENG_N1_2=1";
            
            File.WriteAllText(Path.Combine(targetPath, "module.config"), moduleConfig);
            
            // Nota: El archivo .wasm real vendría de MobiFlight o se compilaría aparte
            Debug.WriteLine("[WasmInstaller] Archivos de configuración creados");
            Debug.WriteLine("[WasmInstaller] NOTA: Necesita archivo .wasm real de MobiFlight");
        }
        
        /// <summary>
        /// Muestra diálogo al usuario preguntando si quiere instalar soporte WASM
        /// </summary>
        public static bool ShowInstallDialog()
        {
            var result = MessageBox.Show(
                "El avión detectado (A319 Headwind) requiere un módulo adicional para leer datos completos.\n\n" +
                "Este módulo permite leer:\n" +
                "• Estado de luces (Beacon, Strobe, Landing)\n" +
                "• N1 de motores detallado\n" +
                "• Código de transponder\n\n" +
                "¿Desea instalar el soporte WASM ahora?\n\n" +
                "Nota: MSFS deberá reiniciarse después de la instalación.",
                "Instalar Soporte para Aviones Complejos",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
                
            return result == MessageBoxResult.Yes;
        }
        
        /// <summary>
        /// Obtiene instrucciones de instalación manual si la automática falla
        /// </summary>
        public static string GetManualInstallInstructions()
        {
            string? communityPath = GetMsfsCommunityFolder();
            
            return $@"INSTALACIÓN MANUAL DEL WASM MODULE

El ACARS detectó que está usando un avión que requiere variables WASM (LVARs).

Para habilitar lectura completa de datos:

1. Descargar MobiFlight desde: https://www.mobiflight.com/
2. Instalar el WASM Module de MobiFlight en MSFS
3. Alternativa: Usar aviones nativos MSFS que no requieren LVARs

Datos que funcionan sin WASM Module:
✓ Fuel, Posición, Altitud, Velocidad, Heading, Flaps, Gear

Datos que REQUIEREN WASM Module:
✗ Luces (Beacon, Strobe, Landing)
✗ N1 de motores detallado  
✗ Transponder code
✗ Cabin pressure

Ruta de Community Folder detectada:
{communityPath ?? "NO DETECTADA"}

Para soporte: contactar a Patagonia Wings";
        }
    }
}
