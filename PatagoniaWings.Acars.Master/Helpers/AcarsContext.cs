using System;
using System.Configuration;
using System.IO;
using PatagoniaWings.Acars.Core.Services;

namespace PatagoniaWings.Acars.Master.Helpers
{
    public static class AcarsContext
    {
        private const string DefaultApiBaseUrl = "https://patagonia-wings-acars.fly.dev";
        private const string DefaultWebBaseUrl = "http://localhost:3001";

        public static ApiService Api { get; private set; } = null!;
        public static AuthService Auth { get; private set; } = null!;
        public static FlightService FlightService { get; private set; } = null!;
        public static AcarsSoundPlayer Sound { get; private set; } = null!;
        public static AcarsRuntimeState Runtime { get; private set; } = null!;

        public static void Initialize()
        {
            Runtime = new AcarsRuntimeState();
            Auth = new AuthService();

            var apiBaseUrl = ReadSetting("ApiBaseUrl", DefaultApiBaseUrl);
            var webBaseUrl = ReadSetting("WebBaseUrl", DefaultWebBaseUrl);
            var supabaseUrl = ReadSecret("PWG_SUPABASE_URL", "SupabaseUrl");
            var supabaseAnonKey = ReadSecret("PWG_SUPABASE_ANON_KEY", "SupabaseAnonKey");
            var useSupabaseDirect = ReadSetting("UseSupabaseDirect", "false")
                .Equals("true", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(supabaseUrl)
                && !string.IsNullOrWhiteSpace(supabaseAnonKey)
                && supabaseAnonKey.IndexOf("PEGA_AQUI", StringComparison.OrdinalIgnoreCase) < 0;

            WriteBootLog(
                "AcarsContext.Initialize => ApiBaseUrl=" + apiBaseUrl +
                " | WebBaseUrl=" + webBaseUrl +
                " | UseSupabaseDirectSetting=" + ReadSetting("UseSupabaseDirect", "false") +
                " | SupabaseUrlPresent=" + (!string.IsNullOrWhiteSpace(supabaseUrl)) +
                " | SupabaseAnonKeyPresent=" + (!string.IsNullOrWhiteSpace(supabaseAnonKey)) +
                " | EffectiveDirectMode=" + useSupabaseDirect);

            Api = new ApiService(apiBaseUrl, webBaseUrl, supabaseUrl, supabaseAnonKey, useSupabaseDirect);
            FlightService = new FlightService();
            Sound = new AcarsSoundPlayer();

            if (Auth.TryRestoreSession() && Auth.CurrentPilot != null)
            {
                Api.SetAuthToken(Auth.CurrentPilot.Token);
                Runtime.SetCurrentPilot(Auth.CurrentPilot);
                WriteBootLog("Session restored from disk.");
            }
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
