using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    public class ApiService
    {
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json;
        private readonly string _baseUrl;
        private readonly string _supabaseUrl;
        private readonly string _supabaseAnonKey;
        private readonly bool _useSupabaseDirect;
        private string _token = string.Empty;

        public ApiService(string baseUrl, string supabaseUrl = "", string supabaseAnonKey = "", bool useSupabaseDirect = false)
        {
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            _supabaseUrl = (supabaseUrl ?? string.Empty).TrimEnd('/');
            _supabaseAnonKey = supabaseAnonKey ?? string.Empty;
            _useSupabaseDirect = useSupabaseDirect;

            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _json = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 128
            };
        }

        private bool CanUseSupabaseDirect
            => _useSupabaseDirect
               && !string.IsNullOrWhiteSpace(_supabaseUrl)
               && !string.IsNullOrWhiteSpace(_supabaseAnonKey);

        public void SetAuthToken(string token)
        {
            _token = token ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_token))
            {
                _http.DefaultRequestHeaders.Authorization = null;
            }
            else
            {
                _http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _token);
            }
        }

        // ── AUTH ──────────────────────────────────────────────────────────────

        public async Task<ApiResult<LoginResponse>> LoginAsync(string username, string password)
        {
            if (CanUseSupabaseDirect)
            {
                return await LoginWithSupabaseAsync(username, password);
            }

            var body = _json.Serialize(new { username, password });
            return await PostAsync<LoginResponse>("/api/auth/login", body);
        }

        public async Task<ApiResult<Pilot>> GetCurrentPilotAsync()
        {
            if (CanUseSupabaseDirect)
            {
                return await GetCurrentPilotFromSupabaseAsync();
            }

            return await GetAsync<Pilot>("/api/pilot/me");
        }

        // ── VUELOS ────────────────────────────────────────────────────────────

        public async Task<ApiResult<List<FlightReport>>> GetMyFlightsAsync(int page = 1)
        {
            if (CanUseSupabaseDirect)
            {
                return await GetMyFlightsFromSupabaseAsync(page);
            }

            return await GetAsync<List<FlightReport>>($"/api/flights/mine?page={page}");
        }

        public async Task<ApiResult<FlightReport>> SubmitFlightReportAsync(FlightReport report)
        {
            var body = _json.Serialize(report);
            return await PostAsync<FlightReport>("/api/flights/report", body);
        }

        public async Task<ApiResult<bool>> StartFlightAsync(Flight flight)
        {
            if (CanUseSupabaseDirect)
            {
                // Etapa inicial: permitir iniciar localmente el ACARS aunque el backend cloud no esté activo.
                // El despacho / reserva real se conectará en el siguiente bloque.
                return ApiResult<bool>.Ok(true);
            }

            var body = _json.Serialize(flight);
            return await PostAsync<bool>("/api/flights/start", body);
        }

        // ── AEROPUERTOS ───────────────────────────────────────────────────────

        public async Task<ApiResult<Airport>> GetAirportAsync(string icao)
            => await GetAsync<Airport>($"/api/airports/{icao.ToUpperInvariant()}");

        public async Task<ApiResult<WeatherInfo>> GetMetarAsync(string icao)
            => await GetAsync<WeatherInfo>($"/api/weather/metar/{icao.ToUpperInvariant()}");

        // ── COMUNIDAD ─────────────────────────────────────────────────────────

        public async Task<ApiResult<List<Pilot>>> GetOnlinePilotsAsync()
        {
            if (CanUseSupabaseDirect)
            {
                return ApiResult<List<Pilot>>.Ok(new List<Pilot>());
            }

            return await GetAsync<List<Pilot>>("/api/community/online");
        }

        public async Task<ApiResult<List<Pilot>>> GetLeaderboardAsync()
        {
            if (CanUseSupabaseDirect)
            {
                return ApiResult<List<Pilot>>.Ok(new List<Pilot>());
            }

            return await GetAsync<List<Pilot>>("/api/community/leaderboard");
        }

        // ── SUPABASE DIRECT ───────────────────────────────────────────────────

        private async Task<ApiResult<LoginResponse>> LoginWithSupabaseAsync(string username, string password)
        {
            try
            {
                var email = await ResolveEmailForLoginAsync(username);
                if (string.IsNullOrWhiteSpace(email))
                {
                    if (IsEmail(username))
                    {
                        email = username.Trim();
                    }
                    else
                    {
                        return ApiResult<LoginResponse>.Fail(
                            "No pude resolver ese usuario en Supabase. Usa tu correo de acceso o expón la columna email en pilot_profiles.");
                    }
                }

                using (var request = CreateSupabaseRequest(HttpMethod.Post, "/auth/v1/token?grant_type=password", false))
                {
                    var payload = _json.Serialize(new { email, password });
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                    var response = await _http.SendAsync(request);
                    var raw = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        return ApiResult<LoginResponse>.Fail($"Supabase auth: {SimplifySupabaseError(raw)}");
                    }

                    var doc = _json.DeserializeObject(raw) as Dictionary<string, object>;
                    if (doc == null || !doc.ContainsKey("access_token"))
                    {
                        return ApiResult<LoginResponse>.Fail("Supabase no devolvió un access_token válido.");
                    }

                    var accessToken = ConvertToString(doc, "access_token");
                    var refreshToken = ConvertToString(doc, "refresh_token");
                    var expiresIn = ConvertToInt(doc, "expires_in", 3600);

                    SetAuthToken(accessToken);

                    var profile = await GetCurrentPilotFromSupabaseAsync();
                    Pilot pilot;
                    if (profile.Success && profile.Data != null)
                    {
                        pilot = profile.Data;
                    }
                    else
                    {
                        pilot = BuildPilotFromMinimalSession(email, username);
                    }

                    pilot.Email = string.IsNullOrWhiteSpace(pilot.Email) ? email : pilot.Email;
                    pilot.Token = accessToken;
                    pilot.RefreshToken = refreshToken;
                    pilot.TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn);

                    return ApiResult<LoginResponse>.Ok(new LoginResponse
                    {
                        Token = accessToken,
                        Pilot = pilot
                    });
                }
            }
            catch (Exception ex)
            {
                return ApiResult<LoginResponse>.Fail(ex.Message);
            }
        }

        private async Task<ApiResult<Pilot>> GetCurrentPilotFromSupabaseAsync()
        {
            if (string.IsNullOrWhiteSpace(_token))
            {
                return ApiResult<Pilot>.Fail("No hay sesión Supabase activa.");
            }

            var endpoints = new[]
            {
                "/rest/v1/pilot_profiles?select=*&limit=1",
                "/rest/v1/v_pilot_live_bridge?select=*&limit=1",
                "/rest/v1/v_pilot_profile?select=*&limit=1"
            };

            foreach (var endpoint in endpoints)
            {
                try
                {
                    using (var request = CreateSupabaseRequest(HttpMethod.Get, endpoint, true))
                    {
                        var response = await _http.SendAsync(request);
                        var raw = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode) continue;

                        var list = _json.DeserializeObject(raw) as object[];
                        if (list != null && list.Length > 0)
                        {
                            var row = list[0] as Dictionary<string, object>;
                            if (row != null)
                            {
                                return ApiResult<Pilot>.Ok(MapPilot(row));
                            }
                        }
                    }
                }
                catch
                {
                    // probar siguiente endpoint
                }
            }

            return ApiResult<Pilot>.Fail("No encontré el perfil del piloto en Supabase con la sesión actual.");
        }

        private async Task<ApiResult<List<FlightReport>>> GetMyFlightsFromSupabaseAsync(int page)
        {
            try
            {
                var offset = Math.Max(0, page - 1) * 20;
                var endpoint =
                    "/rest/v1/flight_reservations?select=reservation_code,route_code,origin_ident,destination_ident,aircraft_type_code,status,created_at,completed_at,actual_block_minutes,procedure_score,mission_score,legado_credits"
                    + $"&order=created_at.desc&limit=20&offset={offset}";

                using (var request = CreateSupabaseRequest(HttpMethod.Get, endpoint, true))
                {
                    var response = await _http.SendAsync(request);
                    var raw = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        return ApiResult<List<FlightReport>>.Ok(new List<FlightReport>());
                    }

                    var list = _json.DeserializeObject(raw) as object[];
                    var flights = new List<FlightReport>();
                    if (list != null)
                    {
                        foreach (var item in list)
                        {
                            var row = item as Dictionary<string, object>;
                            if (row == null) continue;
                            flights.Add(MapFlightReport(row));
                        }
                    }

                    return ApiResult<List<FlightReport>>.Ok(flights);
                }
            }
            catch
            {
                return ApiResult<List<FlightReport>>.Ok(new List<FlightReport>());
            }
        }

        private async Task<string> ResolveEmailForLoginAsync(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return string.Empty;
            if (IsEmail(identifier)) return identifier.Trim();

            var normalized = Uri.EscapeDataString(identifier.Trim());
            var candidateEndpoints = new[]
            {
                $"/rest/v1/pilot_profiles?select=email&callsign=eq.{normalized}&limit=1",
                $"/rest/v1/pilot_profiles?select=email&username=eq.{normalized}&limit=1",
                $"/rest/v1/pilot_profiles?select=email&email=eq.{normalized}&limit=1",
                $"/rest/v1/pilot_profiles?select=email&or=(callsign.eq.{normalized},username.eq.{normalized},email.eq.{normalized})&limit=1"
            };

            foreach (var endpoint in candidateEndpoints)
            {
                try
                {
                    using (var request = CreateSupabaseRequest(HttpMethod.Get, endpoint, false))
                    {
                        var response = await _http.SendAsync(request);
                        var raw = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode) continue;

                        var list = _json.DeserializeObject(raw) as object[];
                        if (list == null || list.Length == 0) continue;

                        var row = list[0] as Dictionary<string, object>;
                        var email = ConvertToString(row, "email");
                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            return email.Trim();
                        }
                    }
                }
                catch
                {
                    // ignorar y probar el siguiente formato
                }
            }

            return string.Empty;
        }

        private HttpRequestMessage CreateSupabaseRequest(HttpMethod method, string endpoint, bool authenticated)
        {
            var request = new HttpRequestMessage(method, _supabaseUrl + endpoint);
            request.Headers.TryAddWithoutValidation("apikey", _supabaseAnonKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (authenticated && !string.IsNullOrWhiteSpace(_token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            }
            return request;
        }

        private Pilot MapPilot(Dictionary<string, object> row)
        {
            var callsign = FirstNonEmpty(row, "callsign", "pilot_callsign", "username");
            var email = FirstNonEmpty(row, "email");
            var fullName = FirstNonEmpty(row, "full_name", "display_name");
            if (string.IsNullOrWhiteSpace(fullName))
            {
                var first = FirstNonEmpty(row, "first_name", "name", "nombres");
                var last = FirstNonEmpty(row, "last_name", "surname", "apellidos");
                fullName = (first + " " + last).Trim();
            }

            var rankCode = FirstNonEmpty(row, "career_rank_code", "rank_code");
            var legacyRankCode = FirstNonEmpty(row, "rank_code");
            var totalHours = FirstNonEmptyNumber(row, "total_hours", "career_hours");

            return new Pilot
            {
                Username = !string.IsNullOrWhiteSpace(callsign) ? callsign : email,
                Email = email,
                FullName = fullName,
                CallSign = callsign,
                RankName = !string.IsNullOrWhiteSpace(rankCode) ? rankCode : legacyRankCode,
                Rank = MapRankCodeToEnum(!string.IsNullOrWhiteSpace(rankCode) ? rankCode : legacyRankCode),
                TotalHours = totalHours,
                TotalFlights = ConvertToInt(row, "total_flights", 0),
                TotalDistance = FirstNonEmptyNumber(row, "total_distance", "career_distance_nm", "distance_nm"),
                Points = ConvertToInt(row, "legado_points", 0),
                AvatarUrl = FirstNonEmpty(row, "avatar_url"),
                PreferredSimulator = FirstNonEmpty(row, "preferred_simulator", "simulator", "sim_preference", "sim_type", "default_simulator") == string.Empty
                    ? "MSFS 2020"
                    : FirstNonEmpty(row, "preferred_simulator", "simulator", "sim_preference", "sim_type", "default_simulator"),
                Language = FirstNonEmpty(row, "language", "locale") == string.Empty ? "ESP" : FirstNonEmpty(row, "language", "locale"),
                CopilotVoiceFemale = ConvertToBool(row, "copilot_voice_female", true),
                CurrentAirportCode = FirstNonEmpty(row, "current_airport_code"),
                BaseHubCode = FirstNonEmpty(row, "base_hub_code")
            };
        }

        private FlightReport MapFlightReport(Dictionary<string, object> row)
        {
            var createdAt = ConvertToDateTime(row, "created_at") ?? DateTime.UtcNow;
            var completedAt = ConvertToDateTime(row, "completed_at") ?? createdAt;
            var durationMinutes = ConvertToInt(row, "actual_block_minutes", 0);
            var score = ConvertToInt(row, "procedure_score", 0);
            if (score <= 0) score = ConvertToInt(row, "mission_score", 0);

            return new FlightReport
            {
                FlightNumber = FirstNonEmpty(row, "reservation_code", "route_code"),
                DepartureIcao = FirstNonEmpty(row, "origin_ident"),
                ArrivalIcao = FirstNonEmpty(row, "destination_ident"),
                AircraftIcao = FirstNonEmpty(row, "aircraft_type_code"),
                DepartureTime = createdAt,
                ArrivalTime = completedAt > createdAt ? completedAt : createdAt.AddMinutes(durationMinutes <= 0 ? 1 : durationMinutes),
                Score = score,
                Grade = ScoreToGrade(score),
                PointsEarned = ConvertToInt(row, "legado_credits", 0),
                Status = ParseFlightStatus(FirstNonEmpty(row, "status"))
            };
        }

        private Pilot BuildPilotFromMinimalSession(string email, string fallbackUser)
        {
            var name = string.Empty;
            if (!string.IsNullOrWhiteSpace(email) && email.Contains("@"))
            {
                name = email.Split('@')[0];
            }

            return new Pilot
            {
                Username = fallbackUser,
                Email = email,
                FullName = name,
                CallSign = fallbackUser.ToUpperInvariant(),
                RankName = "CADET",
                Rank = PilotRank.Aspirante,
                PreferredSimulator = "MSFS 2020",
                Language = "ESP",
                CopilotVoiceFemale = true
            };
        }

        private static bool IsEmail(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Contains("@");
        }

        private string SimplifySupabaseError(string raw)
        {
            try
            {
                var doc = _json.DeserializeObject(raw) as Dictionary<string, object>;
                var message = FirstNonEmpty(doc, "msg", "message", "error_description", "error");
                return string.IsNullOrWhiteSpace(message) ? raw : message;
            }
            catch
            {
                return raw;
            }
        }

        private static PilotRank MapRankCodeToEnum(string rankCode)
        {
            switch ((rankCode ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "CADET":
                case "CADET_SENIOR":
                    return PilotRank.Aspirante;
                case "FIRST_OFFICER":
                case "FIRST_OFFICER_JUNIOR":
                case "SECOND_OFFICER":
                case "SECOND_OFFICER_SENIOR":
                    return PilotRank.SegundoOficialDomestico;
                case "FIRST_OFFICER_SENIOR":
                    return PilotRank.PrimerOficialDomestico;
                case "CAPTAIN_JUNIOR":
                case "CAPTAIN":
                    return PilotRank.ComandanteDomestico;
                case "CAPTAIN_SENIOR":
                case "COMMANDER":
                case "COMMANDER_SENIOR":
                    return PilotRank.ComandanteRegional;
                case "CHECK_AIRMAN":
                case "MASTER_ROUTE":
                case "MASTER_LINE":
                case "FLEET_MENTOR":
                case "OPS_INSPECTOR":
                case "SENIOR_INSPECTOR":
                case "PATAGONIA_LEGEND":
                    return PilotRank.ComandantePrimera;
                default:
                    return PilotRank.Aspirante;
            }
        }

        private static FlightStatus ParseFlightStatus(string status)
        {
            switch ((status ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "completed":
                case "approved":
                case "closed":
                    return FlightStatus.Approved;
                case "cancelled":
                case "rejected":
                    return FlightStatus.Rejected;
                default:
                    return FlightStatus.Pending;
            }
        }

        private static string ScoreToGrade(int score)
        {
            if (score >= 95) return "A+";
            if (score >= 90) return "A";
            if (score >= 80) return "B";
            if (score >= 70) return "C";
            if (score >= 60) return "D";
            return "F";
        }

        private static string FirstNonEmpty(Dictionary<string, object> row, params string[] keys)
        {
            if (row == null) return string.Empty;
            foreach (var key in keys)
            {
                var value = ConvertToString(row, key);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return string.Empty;
        }

        private static string ConvertToString(Dictionary<string, object> row, string key)
        {
            if (row == null || string.IsNullOrWhiteSpace(key) || !row.ContainsKey(key) || row[key] == null)
                return string.Empty;

            return Convert.ToString(row[key], CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static int ConvertToInt(Dictionary<string, object> row, string key, int defaultValue)
        {
            if (row == null || string.IsNullOrWhiteSpace(key) || !row.ContainsKey(key) || row[key] == null)
                return defaultValue;

            var raw = Convert.ToString(row[key], CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(raw)) return defaultValue;

            if (int.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var i)) return i;
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return (int)Math.Round(d);
            return defaultValue;
        }

        private static bool ConvertToBool(Dictionary<string, object> row, string key, bool defaultValue)
        {
            if (row == null || string.IsNullOrWhiteSpace(key) || !row.ContainsKey(key) || row[key] == null)
                return defaultValue;

            var raw = Convert.ToString(row[key], CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
            if (bool.TryParse(raw, out var b)) return b;
            if (raw == "1") return true;
            if (raw == "0") return false;
            return defaultValue;
        }

        private static double FirstNonEmptyNumber(Dictionary<string, object> row, params string[] keys)
        {
            if (row == null) return 0;
            foreach (var key in keys)
            {
                if (!row.ContainsKey(key) || row[key] == null) continue;
                var raw = Convert.ToString(row[key], CultureInfo.InvariantCulture);
                if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)) return value;
            }
            return 0;
        }

        private static DateTime? ConvertToDateTime(Dictionary<string, object> row, string key)
        {
            if (row == null || string.IsNullOrWhiteSpace(key) || !row.ContainsKey(key) || row[key] == null)
                return null;

            var raw = Convert.ToString(row[key], CultureInfo.InvariantCulture);
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date))
                return date;
            return null;
        }

        // ── HTTP helpers ──────────────────────────────────────────────────────

        private async Task<ApiResult<T>> GetAsync<T>(string endpoint)
        {
            try
            {
                var response = await _http.GetAsync(_baseUrl + endpoint);
                return await ParseResponse<T>(response);
            }
            catch (Exception ex)
            {
                return ApiResult<T>.Fail(ex.Message);
            }
        }

        private async Task<ApiResult<T>> PostAsync<T>(string endpoint, string jsonBody)
        {
            try
            {
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(_baseUrl + endpoint, content);
                return await ParseResponse<T>(response);
            }
            catch (Exception ex)
            {
                return ApiResult<T>.Fail(ex.Message);
            }
        }

        private async Task<ApiResult<T>> ParseResponse<T>(HttpResponseMessage response)
        {
            var raw = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                var data = _json.Deserialize<T>(raw);
                return ApiResult<T>.Ok(data);
            }
            return ApiResult<T>.Fail($"Error {(int)response.StatusCode}: {raw}");
        }
    }

    public class ApiResult<T>
    {
        public bool Success { get; private set; }
        public T? Data { get; private set; }
        public string Error { get; private set; } = string.Empty;

        public static ApiResult<T> Ok(T data) => new ApiResult<T> { Success = true, Data = data };
        public static ApiResult<T> Fail(string error) => new ApiResult<T> { Success = false, Error = error };
    }

    public class LoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public Pilot Pilot { get; set; } = new Pilot();
    }
}
