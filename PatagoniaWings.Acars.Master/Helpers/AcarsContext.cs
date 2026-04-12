using System;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;
using PatagoniaWings.Acars.Core.Services;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// Contexto global del ACARS: servicios compartidos en toda la app.
    /// </summary>
    public static class AcarsContext
    {
        private const string DefaultApiBaseUrl = "https://patagonia-wings-acars.fly.dev";

        public static ApiService Api { get; private set; } = null!;
        public static AuthService Auth { get; private set; } = null!;
        public static FlightService FlightService { get; private set; } = null!;
        public static AcarsSoundPlayer Sound { get; private set; } = null!;

        public static void Initialize()
        {
            Auth = new AuthService();

            var apiBaseUrl = ReadSetting("ApiBaseUrl", DefaultApiBaseUrl);
            var supabaseUrl = ReadSecret("PWG_SUPABASE_URL", "SupabaseUrl");
            var supabaseAnonKey = ReadSecret("PWG_SUPABASE_ANON_KEY", "SupabaseAnonKey");
            var useSupabaseDirect = ReadSetting("UseSupabaseDirect", "false")
                .Equals("true", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(supabaseUrl)
                && !string.IsNullOrWhiteSpace(supabaseAnonKey)
                && supabaseAnonKey.IndexOf("PEGA_AQUI", StringComparison.OrdinalIgnoreCase) < 0;

            WriteBootLog(
                "AcarsContext.Initialize => " +
                "ApiBaseUrl=" + apiBaseUrl +
                " | UseSupabaseDirectSetting=" + ReadSetting("UseSupabaseDirect", "false") +
                " | SupabaseUrlPresent=" + (!string.IsNullOrWhiteSpace(supabaseUrl)) +
                " | SupabaseAnonKeyPresent=" + (!string.IsNullOrWhiteSpace(supabaseAnonKey)) +
                " | EffectiveDirectMode=" + useSupabaseDirect);

            Api = new ApiService(apiBaseUrl, supabaseUrl, supabaseAnonKey, useSupabaseDirect);
            FlightService = new FlightService();
            Sound = new AcarsSoundPlayer();

            if (Auth.TryRestoreSession() && Auth.CurrentPilot != null)
            {
                // Siempre cargamos el token (puede estar vencido — se refresca en MainViewModel)
                Api.SetAuthToken(Auth.CurrentPilot.Token);
                WriteBootLog(Auth.IsLoggedIn
                    ? "Session restored from disk (token valid)."
                    : "Session restored from disk (token expired, refresh pending).");
            }
        }

        /// <summary>
        /// Intenta renovar el access token si está vencido usando el refresh token de Supabase.
        /// Retorna true si el token es válido (ya era válido o se refrescó con éxito).
        /// </summary>
        public static async Task<bool> TryRefreshSessionAsync()
        {
            if (Auth.IsLoggedIn) return true;
            if (!Auth.HasExpiredToken) return false;

            WriteBootLog("Token expired — attempting refresh...");
            var result = await Api.RefreshTokenAsync(Auth.CurrentPilot!.RefreshToken!);
            if (result.Success && result.Data != null)
            {
                Auth.SaveSession(result.Data);
                Api.SetAuthToken(result.Data.Token);
                WriteBootLog($"Token refreshed OK => callsign={result.Data.CallSign}");
                return true;
            }

            WriteBootLog($"Token refresh failed: {result.Error}");
            return false;
        }

        public static void Shutdown()
        {
            Sound?.Dispose();
        }

        private static string ReadSetting(string key, string fallback)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                return string.IsNullOrWhiteSpace(value) ? fallback : value;
            }
            catch
            {
                return fallback;
            }
        }

        private static string ReadSecret(string envKey, string appSettingKey)
        {
            var env = Environment.GetEnvironmentVariable(envKey);
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            return ReadSetting(appSettingKey, string.Empty);
        }

        private static void WriteBootLog(string message)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var logFolder = Path.Combine(appData, "PatagoniaWings", "Acars", "logs");
                Directory.CreateDirectory(logFolder);
                var logFile = Path.Combine(logFolder, "auth.log");
                File.AppendAllText(logFile, "[" + DateTime.UtcNow.ToString("o") + "] " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
