using System;
using System.Configuration;
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

            Api = new ApiService(apiBaseUrl, supabaseUrl, supabaseAnonKey, useSupabaseDirect);
            FlightService = new FlightService();
            Sound = new AcarsSoundPlayer();

            // Restaurar sesión si existe
            if (Auth.TryRestoreSession() && Auth.CurrentPilot != null)
                Api.SetAuthToken(Auth.CurrentPilot.Token);
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
    }
}
