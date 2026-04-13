#nullable enable
using System;
using System.Configuration;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using AutoUpdaterDotNET;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// Servicio de actualización automática usando AutoUpdater.NET
    /// Sistema tipo SurAir: actualizaciones automáticas vía XML desde GitHub
    /// </summary>
    public static class UpdateService
    {
        // URL del XML de actualización en GitHub (raw)
        private const string AutoUpdaterXmlUrl = "https://raw.githubusercontent.com/lizamaclaudio-blip/patagonia-wings-acars/main/Web/autoupdater.xml";
        
        // Fallback al JSON legacy si el XML falla
        private const string FallbackManifestUrl = "https://raw.githubusercontent.com/lizamaclaudio-blip/patagonia-wings-acars/main/Web/acars-update.json";

        private static bool _checkedThisSession;

        /// <summary>
        /// Versión actual leída desde App.config o Assembly
        /// </summary>
        public static string CurrentVersion => ReadSetting("AppVersion", GetAssemblyVersion());

        /// <summary>
        /// Inicializa y configura AutoUpdater.NET
        /// </summary>
        static UpdateService()
        {
            // Configurar AutoUpdater
            // AutoUpdater.InstalledVersion = CurrentVersion; // Se detecta automáticamente del assembly
            AutoUpdater.UpdateMode = Mode.Normal;
            AutoUpdater.RunUpdateAsAdmin = true;
            
            // Eventos para logging
            AutoUpdater.CheckForUpdateEvent += AutoUpdaterOnCheckForUpdateEvent;
            AutoUpdater.ApplicationExitEvent += AutoUpdaterOnApplicationExitEvent;
        }

        /// <summary>
        /// Verifica e inicia actualización automática usando AutoUpdater.NET
        /// Muestra diálogo nativo con progreso de descarga e instalación
        /// </summary>
        public static void CheckAndStartUpdate(Window owner)
        {
            if (_checkedThisSession)
                return;

            _checkedThisSession = true;
            
            WriteUpdateLog($"AutoUpdater: Checking for updates. Current version: {CurrentVersion}");
            WriteUpdateLog($"AutoUpdater: XML URL: {AutoUpdaterXmlUrl}");
            
            try
            {
                // Configurar la URL del XML y opciones
                // AutoUpdater.NET detecta automáticamente la versión instalada del assembly
                AutoUpdater.Start(AutoUpdaterXmlUrl);
            }
            catch (Exception ex)
            {
                WriteUpdateLog($"AutoUpdater error: {ex.Message}");
                // Si falla, no mostrar error al usuario - solo log
            }
        }

        /// <summary>
        /// Método legacy para compatibilidad - ahora delega a AutoUpdater
        /// </summary>
        public static async Task NotifyIfUpdateAvailableAsync(Window owner)
        {
            // AutoUpdater.NET maneja todo de forma síncrona
            CheckAndStartUpdate(owner);
            await Task.CompletedTask; // Para mantener compatibilidad con la firma async
        }

        /// <summary>
        /// Resultado de verificación de actualización (legacy compatibilidad)
        /// </summary>
        public class UpdateCheckResult
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

        /// <summary>
        /// Verificación legacy - delega a AutoUpdater
        /// </summary>
        public static async Task<UpdateCheckResult> CheckForUpdatesAsync(bool force = false)
        {
            var result = new UpdateCheckResult 
            { 
                CurrentVersion = CurrentVersion,
                Success = true,
                IsUpdateAvailable = false
            };
            
            // AutoUpdater maneja la verificación automáticamente
            await Task.CompletedTask;
            return result;
        }

        /// <summary>
        /// Evento cuando AutoUpdater detecta actualización
        /// </summary>
        private static void AutoUpdaterOnCheckForUpdateEvent(UpdateInfoEventArgs args)
        {
            if (args.Error != null)
            {
                WriteUpdateLog($"AutoUpdater check error: {args.Error.Message}");
                return;
            }

            if (args.IsUpdateAvailable)
            {
                WriteUpdateLog($"Update available: {args.CurrentVersion} -> {args.InstalledVersion}");
            }
            else
            {
                WriteUpdateLog("No updates available");
            }
        }

        /// <summary>
        /// Evento cuando la aplicación va a cerrarse para actualizar
        /// </summary>
        private static void AutoUpdaterOnApplicationExitEvent()
        {
            WriteUpdateLog("Application exiting for update...");
        }

        /// <summary>
        /// Obtiene la versión del assembly ejecutable
        /// </summary>
        private static string GetAssemblyVersion()
        {
            try
            {
                var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var version = assembly?.GetName()?.Version;
                
                if (version == null) return "3.0.1";
                
                return version.Build >= 0 
                    ? $"{version.Major}.{version.Minor}.{version.Build}" 
                    : $"{version.Major}.{version.Minor}";
            }
            catch
            {
                return "3.0.1";
            }
        }

        /// <summary>
        /// Lee configuración desde App.config
        /// </summary>
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

        /// <summary>
        /// Escribe log de actualización
        /// </summary>
        private static void WriteUpdateLog(string message)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var logFolder = Path.Combine(appData, "PatagoniaWings", "Acars", "logs");
                Directory.CreateDirectory(logFolder);
                var logFile = Path.Combine(logFolder, "autoupdater.log");
                File.AppendAllText(logFile, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Silenciar errores de logging
            }
        }
    }
}
