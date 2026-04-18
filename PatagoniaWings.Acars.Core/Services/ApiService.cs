using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
        private readonly PirepXmlBuilder _pirepXmlBuilder;
        private readonly AircraftDamageApiClient _damageApi;
        private PreparedDispatch? _activePreparedDispatch;
        private readonly string _authLogFile;

        /// <summary>Despacho activo actual (null si no hay vuelo en curso).</summary>
        public PreparedDispatch? ActiveDispatch => _activePreparedDispatch;
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

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logFolder = Path.Combine(appData, "PatagoniaWings", "Acars", "logs");
            Directory.CreateDirectory(logFolder);
            _authLogFile = Path.Combine(logFolder, "auth.log");

            _pirepXmlBuilder = new PirepXmlBuilder();
            _damageApi = new AircraftDamageApiClient(_supabaseUrl, _supabaseAnonKey, _useSupabaseDirect);
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

            _damageApi.SetAuthToken(_token);
        }

        // ── AUTH ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Renueva el access token usando el refresh token de Supabase.
        /// Actualiza _token en el HttpClient si tiene éxito.
        /// </summary>
        public async Task<ApiResult<Pilot>> RefreshTokenAsync(string refreshToken)
        {
            if (!CanUseSupabaseDirect)
                return ApiResult<Pilot>.Fail("Requiere Supabase direct activo.");

            if (string.IsNullOrWhiteSpace(refreshToken))
                return ApiResult<Pilot>.Fail("No hay refresh token.");

            try
            {
                WriteAuthLog("---- REFRESH TOKEN START ----");
                using (var request = CreateSupabaseRequest(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token", false))
                {
                    var payload = _json.Serialize(new { refresh_token = refreshToken });
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                    var response = await _http.SendAsync(request);
                    var raw = await response.Content.ReadAsStringAsync();
                    WriteAuthLog($"POST /auth/v1/token?grant_type=refresh_token => {(int)response.StatusCode}");
                    WriteAuthLog(raw);

                    if (!response.IsSuccessStatusCode)
                        return ApiResult<Pilot>.Fail($"Refresh falló: {SimplifySupabaseError(raw)}");

                    var doc = _json.DeserializeObject(raw) as Dictionary<string, object>;
                    if (doc == null)
                        return ApiResult<Pilot>.Fail("Respuesta de refresh vacía.");

                    var accessToken = ConvertToString(doc, "access_token");
                    var newRefreshToken = ConvertToString(doc, "refresh_token");
                    var expiresIn = ConvertToInt(doc, "expires_in", 3600);

                    if (string.IsNullOrWhiteSpace(accessToken))
                        return ApiResult<Pilot>.Fail("Refresh OK pero sin access_token.");

                    SetAuthToken(accessToken);
                    WriteAuthLog("Refresh token exitoso, nuevo access token activo.");

                    var profile = await GetCurrentPilotFromSupabaseAsync();
                    if (!profile.Success || profile.Data == null)
                        return ApiResult<Pilot>.Fail(profile.Error ?? "Refresh OK pero no pude leer piloto.");

                    var pilot = profile.Data;
                    pilot.Token = accessToken;
                    pilot.RefreshToken = string.IsNullOrWhiteSpace(newRefreshToken) ? refreshToken : newRefreshToken;
                    pilot.TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn);

                    WriteAuthLog($"REFRESH OK => callsign={pilot.CallSign}");
                    return ApiResult<Pilot>.Ok(pilot);
                }
            }
            catch (Exception ex)
            {
                WriteAuthLog($"REFRESH EXCEPTION => {ex}");
                return ApiResult<Pilot>.Fail(ex.Message);
            }
        }

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

        public async Task<ApiResult<AcarsReadyFlight>> GetReadyForAcarsFlightAsync(string pilotCallsign)
        {
            if (CanUseSupabaseDirect)
            {
                return await GetReadyForAcarsFlightFromSupabaseAsync(pilotCallsign);
            }

            return ApiResult<AcarsReadyFlight>.Fail("El vuelo listo para ACARS requiere Supabase direct activo.");
        }

        public async Task<ApiResult<PreparedDispatch>> GetPreparedDispatchAsync(string pilotCallsign)
        {
            if (CanUseSupabaseDirect)
            {
                return await GetPreparedDispatchFromSupabaseAsync(pilotCallsign);
            }

            return ApiResult<PreparedDispatch>.Fail("El despacho directo de la web requiere Supabase direct activo.");
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

        public async Task<ApiResult<FlightReport>> SubmitFlightReportAsync(
            FlightReport report,
            Flight? activeFlight = null,
            IReadOnlyList<SimData>? telemetryLog = null,
            SimData? lastSimData = null,
            IReadOnlyList<AircraftDamageEvent>? damageEvents = null)
        {
            if (CanUseSupabaseDirect)
            {
                return await SubmitFlightReportToSupabaseAsync(report, activeFlight, telemetryLog, lastSimData, damageEvents);
            }

            var body = _json.Serialize(report);
            return await PostAsync<FlightReport>("/api/flights/report", body);
        }

        /// <summary>
        /// Cierra una reserva activa sin completarla (abandon / crash / app close).
        /// status debe ser "cancelled" o "interrupted". Fire-and-forget seguro.
        /// </summary>
        public async Task CloseReservationAsync(string reservationId, string status = "cancelled")
        {
            if (string.IsNullOrWhiteSpace(reservationId)) return;
            try
            {
                var nowUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                using (var req = CreateSupabaseRequest(
                           new HttpMethod("PATCH"),
                           $"/rest/v1/flight_reservations?id=eq.{Uri.EscapeDataString(reservationId)}",
                           true))
                {
                    req.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
                    req.Content = new StringContent(
                        _json.Serialize(new { status, updated_at = nowUtc }),
                        Encoding.UTF8, "application/json");
                    await _http.SendAsync(req).ConfigureAwait(false);
                }
                using (var req2 = CreateSupabaseRequest(
                           new HttpMethod("PATCH"),
                           $"/rest/v1/dispatch_packages?reservation_id=eq.{Uri.EscapeDataString(reservationId)}",
                           true))
                {
                    req2.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
                    req2.Content = new StringContent(
                        _json.Serialize(new { dispatch_status = status, updated_at = nowUtc }),
                        Encoding.UTF8, "application/json");
                    await _http.SendAsync(req2).ConfigureAwait(false);
                }
            }
            catch { /* best-effort, no lanzar */ }
        }

        public async Task<ApiResult<bool>> StartFlightAsync(Flight flight, PreparedDispatch? preparedDispatch = null)
        {
            if (CanUseSupabaseDirect)
            {
                // Etapa inicial: permitir iniciar localmente el ACARS aunque el backend cloud no esté activo.
                // El despacho / reserva real se conectará en el siguiente bloque.
                return await StartFlightWithSupabaseAsync(flight, preparedDispatch);
            }

            var body = _json.Serialize(flight);
            return await PostAsync<bool>("/api/flights/start", body);
        }

        // ── AEROPUERTOS ───────────────────────────────────────────────────────

        public async Task<ApiResult<Airport>> GetAirportAsync(string icao)
            => await GetAsync<Airport>($"/api/airports/{icao.ToUpperInvariant()}");

        public async Task<ApiResult<WeatherInfo>> GetMetarAsync(string icao)
        {
            // Fuente primaria: aviationweather.gov directamente (misma fuente que la web)
            // No depende del backend fly.dev.
            var code = (icao ?? string.Empty).ToUpperInvariant().Trim();
            if (string.IsNullOrEmpty(code)) return ApiResult<WeatherInfo>.Fail("ICAO vacío");

            try
            {
                var url = $"https://aviationweather.gov/api/data/metar?ids={code}&format=json";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("User-Agent",
                    "PatagoniaWingsACARSClient/3.1 (+preflight metar)");
                request.Headers.TryAddWithoutValidation("Accept", "application/json");

                var resp = await _http.SendAsync(request).ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                    return ApiResult<WeatherInfo>.Fail($"aviationweather.gov HTTP {(int)resp.StatusCode}");

                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                var arr  = _json.Deserialize<List<Dictionary<string, object>>>(body);

                if (arr == null || arr.Count == 0)
                    return ApiResult<WeatherInfo>.Fail("Sin datos METAR disponibles para " + code);

                var row = arr[0];
                string? raw = null;
                foreach (var key in new[] { "rawOb", "raw_text", "raw" })
                    if (row.TryGetValue(key, out var v) && v is string s && !string.IsNullOrWhiteSpace(s))
                    { raw = s; break; }

                if (string.IsNullOrWhiteSpace(raw))
                    return ApiResult<WeatherInfo>.Fail("METAR vacío para " + code);

                return ApiResult<WeatherInfo>.Ok(WeatherInfo.ParseRaw(raw));
            }
            catch (Exception ex)
            {
                return ApiResult<WeatherInfo>.Fail("Error METAR: " + ex.Message);
            }
        }

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
                var identifier = username ?? string.Empty;
                WriteAuthLog("---- LOGIN START ----");
                WriteAuthLog($"Identifier: {identifier.Trim()}");

                SetAuthToken(string.Empty);

                var email = await ResolveEmailForLoginAsync(identifier);
                WriteAuthLog($"Resolved email: {email}");

                if (string.IsNullOrWhiteSpace(email))
                {
                    if (IsEmail(identifier))
                    {
                        email = identifier.Trim();
                    }
                    else
                    {
                        return ApiResult<LoginResponse>.Fail(
                            "No pude resolver ese callsign en pilot_profiles. Revisa que el perfil tenga email y callsign.");
                    }
                }

                using (var request = CreateSupabaseRequest(HttpMethod.Post, "/auth/v1/token?grant_type=password", false))
                {
                    var payload = _json.Serialize(new { email, password });
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                    var response = await _http.SendAsync(request);
                    var raw = await response.Content.ReadAsStringAsync();
                    WriteAuthLog($"POST /auth/v1/token => {(int)response.StatusCode} {response.ReasonPhrase}");
                    WriteAuthLog(raw);

                    if (!response.IsSuccessStatusCode)
                    {
                        return ApiResult<LoginResponse>.Fail($"Supabase auth: {SimplifySupabaseError(raw)}");
                    }

                    var doc = _json.DeserializeObject(raw) as Dictionary<string, object>;
                    if (doc == null)
                    {
                        return ApiResult<LoginResponse>.Fail("Supabase devolvió una respuesta de login vacía.");
                    }

                    var accessToken = ConvertToString(doc, "access_token");
                    if (string.IsNullOrWhiteSpace(accessToken))
                    {
                        return ApiResult<LoginResponse>.Fail("Supabase autenticó, pero el login respondió sin token válido.");
                    }

                    var refreshToken = ConvertToString(doc, "refresh_token");
                    var expiresIn = ConvertToInt(doc, "expires_in", 3600);

                    SetAuthToken(accessToken);
                    WriteAuthLog("Access token recibido correctamente.");

                    var profile = await GetCurrentPilotFromSupabaseAsync();
                    if (!profile.Success || profile.Data == null)
                    {
                        WriteAuthLog($"Pilot resolution failed: {profile.Error}");
                        return ApiResult<LoginResponse>.Fail(
                            string.IsNullOrWhiteSpace(profile.Error)
                                ? "Supabase autenticó, pero la app no pudo construir el piloto activo. Revisa auth.log."
                                : profile.Error);
                    }

                    var pilot = profile.Data;
                    pilot.Email = string.IsNullOrWhiteSpace(pilot.Email) ? email : pilot.Email;
                    pilot.Token = accessToken;
                    pilot.RefreshToken = refreshToken;
                    pilot.TokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresIn);

                    WriteAuthLog($"LOGIN OK => callsign={pilot.CallSign}, email={pilot.Email}");

                    return ApiResult<LoginResponse>.Ok(new LoginResponse
                    {
                        Token = accessToken,
                        Pilot = pilot
                    });
                }
            }
            catch (Exception ex)
            {
                WriteAuthLog($"LOGIN EXCEPTION => {ex}");
                return ApiResult<LoginResponse>.Fail(ex.Message);
            }
        }

        private async Task<ApiResult<Pilot>> GetCurrentPilotFromSupabaseAsync()
        {
            if (string.IsNullOrWhiteSpace(_token))
            {
                return ApiResult<Pilot>.Fail("No hay sesión Supabase activa.");
            }

            var authUser = await GetSupabaseAuthenticatedUserAsync();
            if (!authUser.Success || authUser.Data == null)
            {
                return ApiResult<Pilot>.Fail(authUser.Error ?? "Supabase autenticó, pero no pude leer /auth/v1/user.");
            }

            var authRow = authUser.Data;
            var authUserId = ConvertToString(authRow, "id");
            var email = ConvertToString(authRow, "email");
            var lookupEndpoints = new List<string>();

            if (!string.IsNullOrWhiteSpace(authUserId))
            {
                var encodedId = Uri.EscapeDataString(authUserId);
                // La columna PK en pilot_profiles es "id" (= auth user id), no "auth_user_id"
                lookupEndpoints.Add($"/rest/v1/pilot_profiles?select=*&id=eq.{encodedId}&limit=1");
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                var encodedEmail = Uri.EscapeDataString(email.Trim());
                lookupEndpoints.Add($"/rest/v1/pilot_profiles?select=*&email=eq.{encodedEmail}&limit=1");
            }

            lookupEndpoints.Add("/rest/v1/pilot_profiles?select=*&limit=1");

            string lastError = string.Empty;
            foreach (var endpoint in lookupEndpoints.Distinct())
            {
                try
                {
                    using (var request = CreateSupabaseRequest(HttpMethod.Get, endpoint, true))
                    {
                        var response = await _http.SendAsync(request);
                        var raw = await response.Content.ReadAsStringAsync();
                        WriteAuthLog($"GET {endpoint} => {(int)response.StatusCode} {response.ReasonPhrase}");
                        WriteAuthLog(raw);

                        if (!response.IsSuccessStatusCode)
                        {
                            lastError = $"Lookup de pilot_profiles falló: {SimplifySupabaseError(raw)}";
                            continue;
                        }

                        var list = _json.DeserializeObject(raw) as object[];
                        if (list == null || list.Length == 0)
                        {
                            lastError = "pilot_profiles respondió vacío para la sesión autenticada.";
                            continue;
                        }

                        var row = list[0] as Dictionary<string, object>;
                        if (row == null)
                        {
                            lastError = "pilot_profiles respondió un payload inválido.";
                            continue;
                        }

                        var pilot = MapPilot(row);
                        if (string.IsNullOrWhiteSpace(pilot.Email) && !string.IsNullOrWhiteSpace(email))
                        {
                            pilot.Email = email;
                        }

                        if (string.IsNullOrWhiteSpace(pilot.CallSign))
                        {
                            lastError = "Se encontró la fila del piloto, pero viene sin callsign.";
                            continue;
                        }

                        // Enriquecer con datos de pw_pilot_scores (legado_points, total_flights)
                        // que no existen en pilot_profiles
                        await EnrichPilotWithScoresAsync(pilot);

                        return ApiResult<Pilot>.Ok(pilot);
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    WriteAuthLog($"Lookup exception => {ex}");
                }
            }

            return ApiResult<Pilot>.Fail(string.IsNullOrWhiteSpace(lastError)
                ? "Supabase autenticó, pero no encontré un perfil coincidente en public.pilot_profiles."
                : lastError);
        }

        private async Task EnrichPilotWithScoresAsync(Pilot pilot)
        {
            // pw_pilot_scores tiene: legado_points, progression_flights_total, valid_flights_in_window
            // pilot_profiles no tiene total_flights ni legado_points — se leen de aquí.
            if (string.IsNullOrWhiteSpace(pilot.CallSign)) return;
            try
            {
                var encodedCallsign = Uri.EscapeDataString(pilot.CallSign.Trim().ToUpperInvariant());
                var endpoint = $"/rest/v1/pw_pilot_scores?select=legado_points,progression_flights_total,valid_flights_in_window,pulso_10,ruta_10&pilot_callsign=eq.{encodedCallsign}&limit=1";
                using (var request = CreateSupabaseRequest(HttpMethod.Get, endpoint, true))
                {
                    var response = await _http.SendAsync(request);
                    var raw = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode) return;

                    var list = _json.DeserializeObject(raw) as object[];
                    if (list == null || list.Length == 0) return;

                    var scoreRow = list[0] as Dictionary<string, object>;
                    if (scoreRow == null) return;

                    var points = ConvertToInt(scoreRow, "legado_points", 0);
                    var totalFlights = ConvertToInt(scoreRow, "progression_flights_total", 0);
                    if (totalFlights <= 0) totalFlights = ConvertToInt(scoreRow, "valid_flights_in_window", 0);

                    pilot.LegadoPoints = points;
                    if (points > 0) pilot.Points = points;
                    if (totalFlights > 0) pilot.TotalFlights = totalFlights;
                    pilot.Pulso10 = FirstNonEmptyNumber(scoreRow, "pulso_10");
                    pilot.Ruta10 = FirstNonEmptyNumber(scoreRow, "ruta_10");
                }
            }
            catch
            {
                // silencioso — los datos de scores son complementarios, no críticos
            }
        }

        private async Task<ApiResult<Dictionary<string, object>>> GetSupabaseAuthenticatedUserAsync()
        {
            try
            {
                using (var request = CreateSupabaseRequest(HttpMethod.Get, "/auth/v1/user", true))
                {
                    var response = await _http.SendAsync(request);
                    var raw = await response.Content.ReadAsStringAsync();
                    WriteAuthLog($"GET /auth/v1/user => {(int)response.StatusCode} {response.ReasonPhrase}");
                    WriteAuthLog(raw);

                    if (!response.IsSuccessStatusCode)
                    {
                        return ApiResult<Dictionary<string, object>>.Fail($"No pude leer /auth/v1/user: {SimplifySupabaseError(raw)}");
                    }

                    var row = _json.DeserializeObject(raw) as Dictionary<string, object>;
                    if (row == null)
                    {
                        return ApiResult<Dictionary<string, object>>.Fail("/auth/v1/user devolvió una respuesta inválida.");
                    }

                    return ApiResult<Dictionary<string, object>>.Ok(row);
                }
            }
            catch (Exception ex)
            {
                WriteAuthLog($"/auth/v1/user exception => {ex}");
                return ApiResult<Dictionary<string, object>>.Fail($"No pude consultar /auth/v1/user: {ex.Message}");
            }
        }

        private async Task<ApiResult<List<FlightReport>>> GetMyFlightsFromSupabaseAsync(int page)
        {
            try
            {
                var offset = Math.Max(0, page - 1) * 20;
                var pilot = GetCurrentPilotFromMemory();
                var endpoint =
                    "/rest/v1/flight_reservations?select=reservation_code,route_code,origin_ident,destination_ident,aircraft_type_code,status,created_at,completed_at,actual_block_minutes,procedure_score,mission_score,legado_credits"
                    + $"&order=created_at.desc&limit=20&offset={offset}";

                if (pilot != null && !string.IsNullOrWhiteSpace(pilot.CallSign))
                {
                    endpoint += $"&pilot_callsign=eq.{Uri.EscapeDataString(pilot.CallSign.Trim().ToUpperInvariant())}";
                }

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

        private async Task<ApiResult<PreparedDispatch>> GetPreparedDispatchFromSupabaseAsync(string pilotCallsign)
        {
            if (string.IsNullOrWhiteSpace(_token))
            {
                return ApiResult<PreparedDispatch>.Fail("No hay sesión Supabase activa.");
            }

            if (string.IsNullOrWhiteSpace(pilotCallsign))
            {
                return ApiResult<PreparedDispatch>.Fail("No hay callsign activo para buscar el despacho.");
            }

            try
            {
                Dictionary<string, object>? selectedRow = null;

                // Query directo a flight_reservations usando columnas reales de Supabase.
                // Priorizamos estados realmente activos del flujo web -> ACARS.
                var cs = Uri.EscapeDataString(pilotCallsign.Trim().ToUpperInvariant());
                var directEp = $"/rest/v1/flight_reservations?select=*&pilot_callsign=eq.{cs}&status=in.(reserved,dispatch_ready,dispatched,in_progress,in_flight)&order=updated_at.desc.nullslast,created_at.desc.nullslast&limit=5";
                using (var req = CreateSupabaseRequest(HttpMethod.Get, directEp, true))
                {
                    var resp = await _http.SendAsync(req);
                    var raw = await resp.Content.ReadAsStringAsync();
                    if (resp.IsSuccessStatusCode)
                    {
                        var rows = _json.DeserializeObject(raw) as object[];
                        if (rows != null)
                        {
                            foreach (var item in rows)
                            {
                                var row = item as Dictionary<string, object>;
                                if (row == null) continue;
                                var st = FirstNonEmpty(row, "status").Trim().ToLowerInvariant();
                                // Prioridad: dispatched / in_flight primero, luego cualquier activa
                                if (st == "dispatched" || st == "dispatch_ready" || st == "in_progress" || st == "in_flight")
                                { selectedRow = row; break; }
                                if (selectedRow == null) selectedRow = row;
                            }
                        }
                    }
                }

                if (selectedRow == null)
                {
                    return ApiResult<PreparedDispatch>.Fail("No hay ninguna reserva activa para este piloto. Crea una desde la web.");
                }

                var reservationId = FirstNonEmpty(selectedRow, "reservation_id", "id");
                var dispatchPackage = await GetDispatchPackageAsync(reservationId, pilotCallsign);
                var prepared = MapPreparedDispatch(selectedRow, dispatchPackage);

                if (prepared.IsDispatchReady)
                    _activePreparedDispatch = prepared;

                return ApiResult<PreparedDispatch>.Ok(prepared);
            }
            catch (Exception ex)
            {
                return ApiResult<PreparedDispatch>.Fail(ex.Message);
            }
        }

        private async Task<ApiResult<AcarsReadyFlight>> GetReadyForAcarsFlightFromSupabaseAsync(string pilotCallsign)
        {
            var preparedResult = await GetPreparedDispatchFromSupabaseAsync(pilotCallsign);
            if (!preparedResult.Success || preparedResult.Data == null)
            {
                return ApiResult<AcarsReadyFlight>.Fail(preparedResult.Error);
            }

            try
            {
                Pilot? effectivePilot = null;
                var pilotResult = await GetCurrentPilotFromSupabaseAsync();
                if (pilotResult.Success && pilotResult.Data != null)
                {
                    effectivePilot = pilotResult.Data;
                }
                else if (!string.IsNullOrWhiteSpace(pilotCallsign))
                {
                    effectivePilot = new Pilot
                    {
                        CallSign = pilotCallsign.Trim().ToUpperInvariant()
                    };
                }

                var readyFlight = MapAcarsReadyFlight(preparedResult.Data, effectivePilot);
                return ApiResult<AcarsReadyFlight>.Ok(readyFlight);
            }
            catch (Exception ex)
            {
                return ApiResult<AcarsReadyFlight>.Fail(ex.Message);
            }
        }

        private async Task<ApiResult<bool>> StartFlightWithSupabaseAsync(Flight flight, PreparedDispatch? preparedDispatch)
        {
            if (string.IsNullOrWhiteSpace(_token))
            {
                return ApiResult<bool>.Fail("No hay sesion Supabase activa.");
            }

            if (flight == null)
            {
                return ApiResult<bool>.Fail("No hay datos de vuelo para iniciar.");
            }

            try
            {
                var dispatch = preparedDispatch;
                if (dispatch == null)
                {
                    var pilotResult = await GetCurrentPilotFromSupabaseAsync();
                    if (!pilotResult.Success || pilotResult.Data == null || string.IsNullOrWhiteSpace(pilotResult.Data.CallSign))
                    {
                        return ApiResult<bool>.Fail("No pude resolver el piloto activo para cargar el despacho web.");
                    }

                    var dispatchResult = await GetPreparedDispatchFromSupabaseAsync(pilotResult.Data.CallSign);
                    if (!dispatchResult.Success || dispatchResult.Data == null)
                    {
                        return ApiResult<bool>.Fail(
                            string.IsNullOrWhiteSpace(dispatchResult.Error)
                                ? "No hay un despacho web activo para iniciar este vuelo."
                                : dispatchResult.Error);
                    }

                    dispatch = dispatchResult.Data;
                }

                if (string.IsNullOrWhiteSpace(dispatch.ReservationId))
                {
                    return ApiResult<bool>.Fail("El despacho web no trae reservation_id valido.");
                }

                var validationError = ValidateFlightAgainstDispatch(flight, dispatch);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    return ApiResult<bool>.Fail(validationError);
                }

                var nowUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

                using (var request = CreateSupabaseRequest(
                           new HttpMethod("PATCH"),
                           $"/rest/v1/flight_reservations?id=eq.{Uri.EscapeDataString(dispatch.ReservationId)}",
                           true))
                {
                    request.Headers.TryAddWithoutValidation("Prefer", "return=representation");
                    // flight_reservations solo acepta columnas que existen en la tabla.
                    // route_text y remarks no existen en flight_reservations; van en dispatch_packages.
                    var payload = _json.Serialize(new
                    {
                        status = "in_flight",
                        updated_at = nowUtc
                    });
                    request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                    var response = await _http.SendAsync(request);
                    var raw = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        return ApiResult<bool>.Fail($"No pude marcar la reserva ACARS en Supabase: {SimplifySupabaseError(raw)}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(dispatch.DispatchPackageStatus))
                {
                    using (var request = CreateSupabaseRequest(
                               new HttpMethod("PATCH"),
                               $"/rest/v1/dispatch_packages?reservation_id=eq.{Uri.EscapeDataString(dispatch.ReservationId)}",
                               true))
                    {
                        request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
                        // dispatch_status = nombre real de la columna en dispatch_packages
                        var payload = _json.Serialize(new
                        {
                            dispatch_status = "released",
                            updated_at = nowUtc
                        });
                        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                        var response = await _http.SendAsync(request);
                        var raw = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode)
                        {
                            return ApiResult<bool>.Fail($"No pude liberar el dispatch package en Supabase: {SimplifySupabaseError(raw)}");
                        }
                    }
                }

                _activePreparedDispatch = dispatch;
                return ApiResult<bool>.Ok(true);
            }
            catch (Exception ex)
            {
                return ApiResult<bool>.Fail(ex.Message);
            }
        }

        private async Task<Dictionary<string, object>?> GetDispatchPackageAsync(string reservationId, string pilotCallsign = "")
        {
            Dictionary<string, object>? package = null;

            if (!string.IsNullOrWhiteSpace(reservationId))
            {
                using (var request = CreateSupabaseRequest(
                           HttpMethod.Get,
                           $"/rest/v1/dispatch_packages?select=*&reservation_id=eq.{Uri.EscapeDataString(reservationId)}&order=updated_at.desc.nullslast,created_at.desc.nullslast&limit=1",
                           true))
                {
                    var response = await _http.SendAsync(request);
                    var raw = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        var list = _json.DeserializeObject(raw) as object[];
                        if (list != null && list.Length > 0)
                        {
                            package = list[0] as Dictionary<string, object>;
                        }
                    }
                }
            }

            if (package != null || string.IsNullOrWhiteSpace(pilotCallsign))
            {
                return package;
            }

            using (var fallbackRequest = CreateSupabaseRequest(
                       HttpMethod.Get,
                       $"/rest/v1/dispatch_packages?select=*&pilot_callsign=eq.{Uri.EscapeDataString(pilotCallsign.Trim().ToUpperInvariant())}&dispatch_status=in.(prepared,ready,validated,dispatched,released)&order=updated_at.desc.nullslast,created_at.desc.nullslast&limit=1",
                       true))
            {
                var response = await _http.SendAsync(fallbackRequest);
                var raw = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var list = _json.DeserializeObject(raw) as object[];
                if (list != null && list.Length > 0)
                {
                    return list[0] as Dictionary<string, object>;
                }
            }

            return null;
        }

        private async Task<ApiResult<FlightReport>> SubmitFlightReportToSupabaseAsync(
            FlightReport report,
            Flight? activeFlight,
            IReadOnlyList<SimData>? telemetryLog,
            SimData? lastSimData,
            IReadOnlyList<AircraftDamageEvent>? damageEvents)
        {
            if (string.IsNullOrWhiteSpace(_token))
            {
                return ApiResult<FlightReport>.Fail("No hay sesion Supabase activa.");
            }

            if (report == null)
            {
                return ApiResult<FlightReport>.Fail("No hay reporte para cerrar el vuelo.");
            }

            try
            {
                var pilotResult = await GetCurrentPilotFromSupabaseAsync();
                if (!pilotResult.Success || pilotResult.Data == null || string.IsNullOrWhiteSpace(pilotResult.Data.CallSign))
                {
                    return ApiResult<FlightReport>.Fail("No pude resolver el piloto activo para cerrar el vuelo.");
                }

                var pilot = pilotResult.Data;
                var dispatch = _activePreparedDispatch;

                if (dispatch == null || string.IsNullOrWhiteSpace(dispatch.ReservationId))
                {
                    var dispatchResult = await GetPreparedDispatchFromSupabaseAsync(pilot.CallSign);
                    if (!dispatchResult.Success || dispatchResult.Data == null)
                    {
                        return ApiResult<FlightReport>.Fail(
                            string.IsNullOrWhiteSpace(dispatchResult.Error)
                                ? "No hay un despacho web activo para cerrar este vuelo."
                                : dispatchResult.Error);
                    }

                    dispatch = dispatchResult.Data;
                }

                var validationError = ValidateReportAgainstDispatch(report, dispatch);
                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    var refreshedDispatch = await GetPreparedDispatchFromSupabaseAsync(pilot.CallSign);
                    if (refreshedDispatch.Success && refreshedDispatch.Data != null)
                    {
                        dispatch = refreshedDispatch.Data;
                        validationError = ValidateReportAgainstDispatch(report, dispatch);
                    }
                }

                if (!string.IsNullOrWhiteSpace(validationError))
                {
                    return ApiResult<FlightReport>.Fail(validationError);
                }

                var closeoutResult = await TrySubmitHiddenPirepAsync(
                    pilot,
                    dispatch,
                    report,
                    activeFlight,
                    telemetryLog,
                    lastSimData);

                if (!closeoutResult.Success || closeoutResult.Data == null)
                {
                    return closeoutResult;
                }

                if (activeFlight != null)
                {
                    var flightHours = Math.Max(0, report.Duration.TotalHours);
                    var landingCycles = report.LandingVS != 0 ? 1 : 0;
                    var damageList = damageEvents ?? Array.Empty<AircraftDamageEvent>();
                    var damageSync = await _damageApi.SubmitDamageAsync(activeFlight, flightHours, landingCycles, damageList);

                    if (!damageSync.Success)
                    {
                        WriteAuthLog("Damage sync warning => " + damageSync.Error);
                        var warning = "Damage sync warning: " + (damageSync.Error ?? "unknown");
                        report.Remarks = string.IsNullOrWhiteSpace(report.Remarks)
                            ? warning
                            : report.Remarks + " | " + warning;
                    }
                }

                _activePreparedDispatch = null;
                return closeoutResult;
            }
            catch (Exception ex)
            {
                return ApiResult<FlightReport>.Fail(ex.Message);
            }
        }

        private async Task<ApiResult<FlightReport>> TrySubmitHiddenPirepAsync(
            Pilot pilot,
            PreparedDispatch dispatch,
            FlightReport report,
            Flight? activeFlight,
            IReadOnlyList<SimData>? telemetryLog,
            SimData? lastSimData)
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var nowUtcIso = nowUtc.ToString("o", CultureInfo.InvariantCulture);
                var telemetry = telemetryLog ?? Array.Empty<SimData>();
                var lastSample = lastSimData ?? (telemetry.Count > 0 ? telemetry[telemetry.Count - 1] : null);
                var actualBlockMinutes = Math.Max(1, (int)Math.Round(report.Duration.TotalMinutes));
                var fuelUsedKg = Math.Max(0, report.FuelUsed) * 0.45359237d;
                var fuelEndKg = lastSample != null ? Math.Max(0, lastSample.FuelTotalLbs) * 0.45359237d : 0;
                var fuelStartKg = 0d;

                if (dispatch.FuelPlannedKg > 0)
                {
                    fuelStartKg = dispatch.FuelPlannedKg;
                }
                else if (activeFlight != null && activeFlight.BlockFuel > 0)
                {
                    fuelStartKg = activeFlight.BlockFuel;
                }
                else if (fuelEndKg > 0 || fuelUsedKg > 0)
                {
                    fuelStartKg = fuelEndKg + fuelUsedKg;
                }

                var remarks = FirstNonEmpty(
                    new Dictionary<string, object>
                    {
                        { "report_remarks", report.Remarks },
                        { "flight_remarks", activeFlight == null ? string.Empty : activeFlight.Remarks },
                        { "dispatch_remarks", dispatch.Remarks }
                    },
                    "report_remarks",
                    "flight_remarks",
                    "dispatch_remarks");

                using (var reservationRequest = CreateSupabaseRequest(
                           new HttpMethod("PATCH"),
                           $"/rest/v1/flight_reservations?id=eq.{Uri.EscapeDataString(dispatch.ReservationId)}",
                           true))
                {
                    reservationRequest.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
                    // flight_reservations: solo columnas que existen en la tabla.
                    // route_text y remarks no son columnas de flight_reservations.
                    var scorePayload = new Dictionary<string, object>
                    {
                        ["procedure_score"]   = report.ProcedureScore,
                        ["performance_score"] = report.PerformanceScore,
                        ["procedure_grade"]   = report.ProcedureGrade,
                        ["performance_grade"] = report.PerformanceGrade,
                        ["grade"]             = report.Grade,       // legacy
                        ["violations_count"]  = report.Violations?.Count ?? 0,
                        ["bonuses_count"]     = report.Bonuses?.Count ?? 0,
                        ["landing_vs_fpm"]    = report.LandingVS,
                        ["landing_g"]         = report.LandingG,
                        ["max_altitude_ft"]   = report.MaxAltitudeFeet,
                        ["max_speed_kts"]     = report.MaxSpeedKts,
                        ["approach_qnh_hpa"]  = report.ApproachQnhHpa,
                        ["landing_penalty"]   = report.LandingPenalty,
                        ["taxi_penalty"]      = report.TaxiPenalty,
                        ["airborne_penalty"]  = report.AirbornePenalty,
                        ["approach_penalty"]  = report.ApproachPenalty,
                        ["cabin_penalty"]     = report.CabinPenalty,
                        ["summary"]           = report.ProceduralSummary,
                        ["source"]            = "acars_client_v3"
                    };
                    reservationRequest.Content = new StringContent(
                        _json.Serialize(new
                        {
                            status              = "completed",
                            completed_at        = nowUtcIso,
                            actual_block_minutes = actualBlockMinutes,
                            procedure_score     = report.ProcedureScore,
                            performance_score   = report.PerformanceScore,
                            procedure_grade     = report.ProcedureGrade,
                            performance_grade   = report.PerformanceGrade,
                            mission_score       = report.ProcedureScore,   // legacy alias
                            scoring_status      = "scored",
                            scoring_applied_at  = nowUtcIso,
                            score_payload       = scorePayload,
                            updated_at          = nowUtcIso
                        }),
                        Encoding.UTF8,
                        "application/json");

                    var reservationResponse = await _http.SendAsync(reservationRequest);
                    var reservationRaw = await reservationResponse.Content.ReadAsStringAsync();
                    if (!reservationResponse.IsSuccessStatusCode)
                    {
                        return ApiResult<FlightReport>.Fail($"No pude marcar completed en Supabase: {SimplifySupabaseError(reservationRaw)}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(dispatch.DispatchPackageStatus))
                {
                    using (var packageRequest = CreateSupabaseRequest(
                               new HttpMethod("PATCH"),
                               $"/rest/v1/dispatch_packages?reservation_id=eq.{Uri.EscapeDataString(dispatch.ReservationId)}",
                               true))
                    {
                        packageRequest.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
                        // dispatch_status = nombre real de la columna en dispatch_packages
                        packageRequest.Content = new StringContent(
                            _json.Serialize(new
                            {
                                dispatch_status = "completed",
                                updated_at = nowUtcIso
                            }),
                            Encoding.UTF8,
                            "application/json");

                        var packageResponse = await _http.SendAsync(packageRequest);
                        var packageRaw = await packageResponse.Content.ReadAsStringAsync();
                        if (!packageResponse.IsSuccessStatusCode)
                        {
                            return ApiResult<FlightReport>.Fail($"No pude cerrar el dispatch package en Supabase: {SimplifySupabaseError(packageRaw)}");
                        }
                    }
                }

                var repositionError = await CompleteFlightAndRepositionAsync(
                    dispatch,
                    pilot,
                    report.ArrivalIcao,
                    nowUtcIso);
                if (!string.IsNullOrWhiteSpace(repositionError))
                {
                    return ApiResult<FlightReport>.Fail(repositionError);
                }

                // pw_flight_score_reports: desglose por fase para scoring histórico
                var blockHours = Math.Round(actualBlockMinutes / 60.0, 3);
                var totalPenalty = report.LandingPenalty + report.TaxiPenalty + report.AirbornePenalty + report.ApproachPenalty + report.CabinPenalty;
                var scoreReportPayload = new Dictionary<string, object>
                {
                    ["procedure_score"]   = report.ProcedureScore,
                    ["performance_score"] = report.PerformanceScore,
                    ["procedure_grade"]   = report.ProcedureGrade,
                    ["performance_grade"] = report.PerformanceGrade,
                    ["violations_count"]  = report.Violations?.Count ?? 0,
                    ["bonuses_count"]     = report.Bonuses?.Count ?? 0,
                    ["landing_vs_fpm"]    = report.LandingVS,
                    ["landing_g_force"]   = report.LandingG,
                    ["max_altitude_ft"]   = report.MaxAltitudeFeet,
                    ["max_speed_kts"]     = report.MaxSpeedKts,
                    ["approach_qnh_hpa"]  = report.ApproachQnhHpa,
                    ["procedural_summary"] = report.ProceduralSummary,
                    ["simulator"]         = report.Simulator.ToString()
                };
                using (var scoreRequest = CreateSupabaseRequest(HttpMethod.Post, "/rest/v1/pw_flight_score_reports", true))
                {
                    scoreRequest.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
                    scoreRequest.Content = new StringContent(
                        _json.Serialize(new[]
                        {
                            new Dictionary<string, object?>
                            {
                                ["reservation_id"]       = dispatch.ReservationId,
                                ["pilot_callsign"]       = pilot.CallSign,
                                ["route_code"]           = NullIfEmpty(dispatch.RouteCode),
                                ["flight_mode_code"]     = NullIfEmpty(dispatch.FlightMode),
                                ["block_minutes"]        = actualBlockMinutes,
                                ["block_hours"]          = blockHours,
                                ["landing_points"]       = report.LandingPenalty,
                                ["taxi_out_points"]      = report.TaxiPenalty,
                                ["takeoff_climb_points"] = report.AirbornePenalty,
                                ["approach_points"]      = report.ApproachPenalty,
                                ["cruise_points"]        = report.CabinPenalty,
                                ["penalty_points"]       = totalPenalty,
                                ["procedure_score"]      = report.ProcedureScore,
                                ["performance_score"]    = report.PerformanceScore,
                                ["procedure_grade"]      = NullIfEmpty(report.ProcedureGrade),
                                ["performance_grade"]    = NullIfEmpty(report.PerformanceGrade),
                                ["mission_score"]        = report.ProcedureScore,   // legacy alias
                                ["legado_credits"]       = report.ProcedureScore,
                                ["valid_for_progression"] = report.ProcedureScore >= 60,
                                ["score_payload"]        = scoreReportPayload,
                                ["notes"]                = report.ProceduralSummary,
                                ["scored_at"]            = nowUtcIso
                            }
                        }),
                        Encoding.UTF8,
                        "application/json");

                    var scoreResponse = await _http.SendAsync(scoreRequest);
                    var scoreRaw = await scoreResponse.Content.ReadAsStringAsync();
                    if (!scoreResponse.IsSuccessStatusCode)
                    {
                        return ApiResult<FlightReport>.Fail($"No pude guardar el score report: {SimplifySupabaseError(scoreRaw)}");
                    }
                }

                // pirep_reports: registro básico del vuelo (best-effort, no falla el flujo)
                try
                {
                    using (var pirepRequest = CreateSupabaseRequest(HttpMethod.Post, "/rest/v1/pirep_reports", true))
                    {
                        pirepRequest.Headers.TryAddWithoutValidation("Prefer", "resolution=ignore-duplicates,return=minimal");
                        pirepRequest.Content = new StringContent(
                            _json.Serialize(new[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["reference_code"] = dispatch.ReservationId,
                                    ["callsign"] = pilot.CallSign,
                                    ["flight_number"] = NormalizeCommercialFlightNumber(dispatch.FlightDesignator, report.FlightNumber),
                                    ["flight_type"] = NullIfEmpty(dispatch.FlightMode) ?? "line",
                                    ["origin_icao"] = report.DepartureIcao,
                                    ["destination_icao"] = report.ArrivalIcao,
                                    ["aircraft_registration"] = NullIfEmpty(dispatch.AircraftRegistration) ?? string.Empty,
                                    ["aircraft_model"] = ResolveAcarsAircraftIcao(dispatch.AircraftIcao),
                                    ["created_on_utc"] = nowUtcIso,
                                    ["result_status"] = "completed"
                                }
                            }),
                            Encoding.UTF8,
                            "application/json");

                        await _http.SendAsync(pirepRequest);
                    }
                }
                catch
                {
                    // pirep_reports es registro secundario; no bloquea el flujo
                }

                // Enriquecer reporte con datos del piloto
                report.PilotQualifications = pilot.ActiveQualifications ?? string.Empty;
                report.PilotCertifications = pilot.ActiveCertifications ?? string.Empty;

                report.Status = FlightStatus.Pending;
                return ApiResult<FlightReport>.Ok(report);
            }
            catch (Exception ex)
            {
                return ApiResult<FlightReport>.Fail(ex.Message);
            }
        }

        private async Task<string> CompleteFlightAndRepositionAsync(
            PreparedDispatch dispatch,
            Pilot pilot,
            string destinationIcao,
            string nowUtcIso)
        {
            var normalizedDestination = (destinationIcao ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedDestination))
            {
                return "No pude cerrar el vuelo porque falta el ICAO de destino.";
            }

            if (string.IsNullOrWhiteSpace(dispatch.ReservationId))
            {
                return "No pude cerrar el vuelo porque el despacho no trae reservation_id.";
            }

            if (string.IsNullOrWhiteSpace(pilot.CallSign))
            {
                return "No pude cerrar el vuelo porque el piloto activo no trae callsign.";
            }

            try
            {
                using (var request = CreateSupabaseRequest(HttpMethod.Post, "/rest/v1/rpc/pw_complete_flight_and_reposition", true))
                {
                    request.Content = new StringContent(
                        _json.Serialize(new
                        {
                            p_reservation_id = dispatch.ReservationId,
                            p_pilot_callsign = pilot.CallSign,
                            p_destination_ident = normalizedDestination
                        }),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _http.SendAsync(request);
                    var raw = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        return string.Empty;
                    }
                }
            }
            catch
            {
                // fallback below
            }

            var pilotEndpoint = $"/rest/v1/pilot_profiles?callsign=eq.{Uri.EscapeDataString(pilot.CallSign.Trim().ToUpperInvariant())}";
            var pilotError = await PatchSupabaseRowAsync(
                pilotEndpoint,
                new Dictionary<string, object>
                {
                    ["current_airport_code"] = normalizedDestination,
                    ["current_airport_icao"] = normalizedDestination,
                    ["updated_at"] = nowUtcIso
                },
                new Dictionary<string, object>
                {
                    ["current_airport_code"] = normalizedDestination,
                    ["updated_at"] = nowUtcIso
                },
                new Dictionary<string, object>
                {
                    ["current_airport_icao"] = normalizedDestination,
                    ["updated_at"] = nowUtcIso
                });

            if (!string.IsNullOrWhiteSpace(pilotError))
            {
                return $"No pude reposicionar al piloto en Supabase: {pilotError}";
            }

            if (!string.IsNullOrWhiteSpace(dispatch.AircraftId))
            {
                var aircraftEndpoint = $"/rest/v1/aircraft?id=eq.{Uri.EscapeDataString(dispatch.AircraftId)}";
                var aircraftError = await PatchSupabaseRowAsync(
                    aircraftEndpoint,
                    new Dictionary<string, object>
                    {
                        ["current_airport_code"] = normalizedDestination,
                        ["current_airport_icao"] = normalizedDestination,
                        ["status"] = "available",
                        ["updated_at"] = nowUtcIso
                    },
                    new Dictionary<string, object>
                    {
                        ["current_airport_code"] = normalizedDestination,
                        ["status"] = "available",
                        ["updated_at"] = nowUtcIso
                    },
                    new Dictionary<string, object>
                    {
                        ["current_airport_icao"] = normalizedDestination,
                        ["status"] = "available",
                        ["updated_at"] = nowUtcIso
                    });

                if (!string.IsNullOrWhiteSpace(aircraftError))
                {
                    return $"No pude reposicionar la aeronave en Supabase: {aircraftError}";
                }
            }

            return string.Empty;
        }

        private async Task<string> PatchSupabaseRowAsync(string endpoint, params Dictionary<string, object>[] payloadOptions)
        {
            string lastError = string.Empty;

            foreach (var payload in payloadOptions)
            {
                using (var request = CreateSupabaseRequest(new HttpMethod("PATCH"), endpoint, true))
                {
                    request.Headers.TryAddWithoutValidation("Prefer", "return=minimal");
                    request.Content = new StringContent(_json.Serialize(payload), Encoding.UTF8, "application/json");

                    var response = await _http.SendAsync(request);
                    var raw = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        return string.Empty;
                    }

                    lastError = SimplifySupabaseError(raw);
                }
            }

            return lastError;
        }

        private string ValidateReportAgainstDispatch(FlightReport report, PreparedDispatch dispatch)
        {
            var expectedFlightNumber = NormalizeCommercialFlightNumber(dispatch.FlightDesignator, dispatch.FlightNumber);
            var actualFlightNumber = NormalizeCommercialFlightNumber(report.FlightNumber, report.FlightNumber);
            if (!string.Equals(expectedFlightNumber, actualFlightNumber, StringComparison.OrdinalIgnoreCase))
            {
                return $"El numero de vuelo no coincide con el despacho web ({expectedFlightNumber}).";
            }

            var expectedOrigin = (dispatch.DepartureIcao ?? string.Empty).Trim().ToUpperInvariant();
            var actualOrigin = (report.DepartureIcao ?? string.Empty).Trim().ToUpperInvariant();
            if (!string.Equals(expectedOrigin, actualOrigin, StringComparison.OrdinalIgnoreCase))
            {
                return $"El origen no coincide con el despacho web ({expectedOrigin}).";
            }

            var expectedDestination = (dispatch.ArrivalIcao ?? string.Empty).Trim().ToUpperInvariant();
            var actualDestination = (report.ArrivalIcao ?? string.Empty).Trim().ToUpperInvariant();
            if (!string.Equals(expectedDestination, actualDestination, StringComparison.OrdinalIgnoreCase))
            {
                return $"El destino no coincide con el despacho web ({expectedDestination}).";
            }

            var expectedAircraft = ResolveAcarsAircraftIcao(dispatch.AircraftIcao);
            var actualAircraft = ResolveAcarsAircraftIcao(report.AircraftIcao);
            if (!string.Equals(expectedAircraft, actualAircraft, StringComparison.OrdinalIgnoreCase))
            {
                return $"El airframe no coincide con el despacho web ({expectedAircraft}).";
            }

            return string.Empty;
        }

        private List<Dictionary<string, object>> BuildHiddenPirepLogRows(
            string hiddenPirepId,
            FlightReport report,
            IReadOnlyList<SimData>? telemetryLog)
        {
            var rows = new List<Dictionary<string, object>>();
            var sequence = 1;

            rows.Add(new Dictionary<string, object>
            {
                ["hidden_pirep_id"] = hiddenPirepId,
                ["sequence_no"] = sequence++,
                ["sample_kind"] = "header",
                ["event_time_utc"] = report.DepartureTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                ["event_code"] = "flight_completed",
                ["event_message"] = $"Vuelo {report.FlightNumber} completado en ACARS",
                ["raw_line"] = $"{report.DepartureIcao}-{report.ArrivalIcao} {report.AircraftIcao}"
            });

            if (telemetryLog != null)
            {
                foreach (var sample in telemetryLog)
                {
                    rows.Add(new Dictionary<string, object>
                    {
                        ["hidden_pirep_id"] = hiddenPirepId,
                        ["sequence_no"] = sequence++,
                        ["sample_kind"] = "telemetry",
                        ["event_time_local"] = sample.CapturedAtUtc == default(DateTime)
                            ? string.Empty
                            : sample.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                        ["event_time_utc"] = sample.CapturedAtUtc == default(DateTime)
                            ? string.Empty
                            : sample.CapturedAtUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                        ["latitude"] = sample.Latitude,
                        ["longitude"] = sample.Longitude,
                        ["heading_deg"] = sample.Heading,
                        ["altitude_ft"] = sample.AltitudeFeet,
                        ["ias_kts"] = sample.IndicatedAirspeed,
                        ["ground_speed_kts"] = sample.GroundSpeed,
                        ["vertical_speed_fpm"] = sample.VerticalSpeed,
                        ["fuel_kg"] = Math.Max(0, sample.FuelTotalLbs) * 0.45359237d,
                        ["ft_agl"] = sample.AltitudeAGL,
                        ["indicated_altitude_ft"] = sample.AltitudeFeet,
                        ["altimeter_hpa"] = sample.QNH,
                        ["bank_deg"] = sample.Bank
                    });
                }
            }

            rows.Add(new Dictionary<string, object>
            {
                ["hidden_pirep_id"] = hiddenPirepId,
                ["sequence_no"] = sequence,
                ["sample_kind"] = "system",
                ["event_time_utc"] = report.ArrivalTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                ["event_code"] = "xml_pending_generation",
                ["event_message"] = "PIREP invisible creado y listo para generar XML final"
            });

            return rows;
        }

        private Dictionary<string, object> BuildTelemetrySummary(
            FlightReport report,
            IReadOnlyList<SimData>? telemetryLog,
            SimData? lastSample)
        {
            var maxAltitude = 0d;
            var maxGroundSpeed = 0d;
            var maxIas = 0d;
            var count = 0;

            if (telemetryLog != null)
            {
                foreach (var sample in telemetryLog)
                {
                    count++;
                    if (sample.AltitudeFeet > maxAltitude) maxAltitude = sample.AltitudeFeet;
                    if (sample.GroundSpeed > maxGroundSpeed) maxGroundSpeed = sample.GroundSpeed;
                    if (sample.IndicatedAirspeed > maxIas) maxIas = sample.IndicatedAirspeed;
                }
            }

            return new Dictionary<string, object>
            {
                ["sample_count"] = count,
                ["distance_nm"] = report.Distance,
                ["fuel_used_kg"] = Math.Max(0, report.FuelUsed) * 0.45359237d,
                ["max_altitude_ft"] = maxAltitude,
                ["max_ground_speed_kts"] = maxGroundSpeed,
                ["max_ias_kts"] = maxIas,
                ["last_latitude"] = lastSample == null ? 0 : lastSample.Latitude,
                ["last_longitude"] = lastSample == null ? 0 : lastSample.Longitude,
                ["last_altitude_ft"] = lastSample == null ? 0 : lastSample.AltitudeFeet
            };
        }

        private List<Dictionary<string, object>> BuildEventLog(FlightReport report, IReadOnlyList<SimData>? telemetryLog)
        {
            var events = new List<Dictionary<string, object>>
            {
                new Dictionary<string, object>
                {
                    ["code"] = "completed",
                    ["message"] = $"Vuelo {report.FlightNumber} marcado completed en Supabase",
                    ["time_utc"] = report.ArrivalTime.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture)
                }
            };

            if (telemetryLog != null && telemetryLog.Count > 0)
            {
                events.Add(new Dictionary<string, object>
                {
                    ["code"] = "telemetry_captured",
                    ["message"] = $"Se guardaron {telemetryLog.Count} muestras ACARS",
                    ["time_utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
                });
            }

            return events;
        }

        private static object? NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private async Task<string> ResolveEmailForLoginAsync(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier)) return string.Empty;
            if (IsEmail(identifier)) return identifier.Trim();

            var normalized = Uri.EscapeDataString(identifier.Trim());
            var candidateEndpoints = new[]
            {
                $"/rest/v1/pilot_profiles?select=email&callsign=eq.{normalized}&limit=1",
                $"/rest/v1/pilot_profiles?select=email&email=eq.{normalized}&limit=1",
                $"/rest/v1/pilot_profiles?select=email&or=(callsign.eq.{normalized},email.eq.{normalized})&limit=1"
            };

            foreach (var endpoint in candidateEndpoints)
            {
                try
                {
                    using (var request = CreateSupabaseRequest(HttpMethod.Get, endpoint, false))
                    {
                        var response = await _http.SendAsync(request);
                        var raw = await response.Content.ReadAsStringAsync();
                        WriteAuthLog($"ResolveEmail GET {endpoint} => {(int)response.StatusCode} {response.ReasonPhrase}");
                        WriteAuthLog(raw);
                        if (!response.IsSuccessStatusCode) continue;

                        var list = _json.DeserializeObject(raw) as object[];
                        if (list == null || list.Length == 0) continue;

                        var row = list[0] as Dictionary<string, object>;
                        var email = row != null ? ConvertToString(row, "email") : string.Empty;
                        if (!string.IsNullOrWhiteSpace(email))
                        {
                            return email.Trim();
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteAuthLog($"ResolveEmail exception => {ex}");
                }
            }

            return string.Empty;
        }


        private Pilot? GetCurrentPilotFromMemory()
        {
            try
            {
                var type = Type.GetType("PatagoniaWings.Acars.Master.Helpers.AcarsContext, PatagoniaWings.Acars.Master", false);
                if (type == null) return null;
                var authProp = type.GetProperty("Auth", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                var authService = authProp == null ? null : authProp.GetValue(null, null);
                if (authService == null) return null;
                var pilotProp = authService.GetType().GetProperty("CurrentPilot");
                return pilotProp == null ? null : pilotProp.GetValue(authService, null) as Pilot;
            }
            catch
            {
                return null;
            }
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
            // Columnas reales de pilot_profiles:
            //   callsign, first_name, last_name, display_name, email,
            //   career_rank_code, rank_code, career_hours, total_hours,
            //   base_hub_code, base_hub, current_airport_code, current_airport_icao,
            //   simulator, auth_user_id
            var callsign = FirstNonEmpty(row, "callsign", "pilot_callsign", "username");
            var email = FirstNonEmpty(row, "email");

            // display_name es el nombre completo preferido; si no existe, concatenar first+last
            var fullName = FirstNonEmpty(row, "display_name", "full_name");
            if (string.IsNullOrWhiteSpace(fullName))
            {
                var first = FirstNonEmpty(row, "first_name", "name", "nombres");
                var last = FirstNonEmpty(row, "last_name", "surname", "apellidos");
                fullName = (first + " " + last).Trim();
            }

            // career_rank_code es el rango activo; rank_code es legado
            // pilot_profiles nuevo no tiene estos campos → defecto "Cadete" para pilotos sin rango
            var rankCode = FirstNonEmpty(row, "career_rank_code", "rank_code");
            if (string.IsNullOrWhiteSpace(rankCode)) rankCode = "Cadete";
            // career_hours es el acumulado oficial; total_hours como fallback
            var totalHours = FirstNonEmptyNumber(row, "career_hours", "total_hours", "career_flight_hours");

            // simulator = nombre del sim preferido (MSFS 2020 / MSFS 2024)
            var simPreference = FirstNonEmpty(row, "simulator", "preferred_simulator",
                                              "sim_preference", "sim_type", "default_simulator");
            if (string.IsNullOrWhiteSpace(simPreference)) simPreference = "MSFS 2020";

            return new Pilot
            {
                Username   = !string.IsNullOrWhiteSpace(callsign) ? callsign : email,
                Email      = email,
                FullName   = fullName,
                CallSign   = callsign,
                RankCode   = FirstNonEmpty(row, "rank_code"),
                CareerRankCode = FirstNonEmpty(row, "career_rank_code"),
                RankName   = rankCode,
                Rank       = MapRankCodeToEnum(rankCode),
                TotalHours = totalHours,
                TransferredHours = FirstNonEmptyNumber(row, "transferred_hours"),
                // TotalFlights y Points se llenan después por EnrichPilotWithScoresAsync
                // (están en pw_pilot_scores, no en pilot_profiles)
                TotalFlights  = 0,
                TotalDistance = 0,
                Points        = 0,
                AvatarUrl     = FirstNonEmpty(row, "avatar_url"),
                PreferredSimulator = simPreference,
                Language = FirstNonEmpty(row, "language", "locale") == string.Empty
                               ? "ESP"
                               : FirstNonEmpty(row, "language", "locale"),
                CopilotVoiceFemale = ConvertToBool(row, "copilot_voice_female", true),
                ActiveQualifications = FirstNonEmpty(row, "active_qualifications", "qualifications"),
                ActiveCertifications = FirstNonEmpty(row, "active_certifications", "certifications"),
                // Columnas reales: current_airport_icao y base_hub (sin sufijo _code)
                CurrentAirportCode = FirstNonEmpty(row, "current_airport_icao", "current_airport_code"),
                BaseHubCode        = FirstNonEmpty(row, "base_hub", "base_hub_code")
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

        private AcarsReadyFlight MapAcarsReadyFlight(PreparedDispatch preparedDispatch, Pilot? pilot)
        {
            var flightNumber = NormalizeCommercialFlightNumber(preparedDispatch.FlightDesignator, preparedDispatch.FlightNumber);

            return new AcarsReadyFlight
            {
                ReservationId = preparedDispatch.ReservationId,
                DispatchPackageId = preparedDispatch.DispatchId,
                PilotCallsign = pilot == null ? string.Empty : pilot.CallSign,
                PilotUserId = preparedDispatch.PilotUserId,
                RankCode = !string.IsNullOrWhiteSpace(preparedDispatch.RankCode)
                    ? preparedDispatch.RankCode
                    : (pilot == null ? string.Empty : pilot.RankCode),
                CareerRankCode = !string.IsNullOrWhiteSpace(preparedDispatch.CareerRankCode)
                    ? preparedDispatch.CareerRankCode
                    : (pilot == null ? string.Empty : pilot.CareerRankCode),
                BaseHubCode = !string.IsNullOrWhiteSpace(preparedDispatch.BaseHubCode)
                    ? preparedDispatch.BaseHubCode
                    : (pilot == null ? string.Empty : pilot.BaseHubCode),
                CurrentAirportCode = !string.IsNullOrWhiteSpace(preparedDispatch.CurrentAirportCode)
                    ? preparedDispatch.CurrentAirportCode
                    : (pilot == null ? string.Empty : pilot.CurrentAirportCode),
                FlightModeCode = preparedDispatch.FlightMode,
                RouteCode = preparedDispatch.RouteCode,
                FlightNumber = flightNumber,
                OriginIdent = preparedDispatch.DepartureIcao,
                DestinationIdent = preparedDispatch.ArrivalIcao,
                AircraftId = preparedDispatch.AircraftId,
                AircraftRegistration = preparedDispatch.AircraftRegistration,
                AircraftTypeCode = preparedDispatch.AircraftIcao,
                AircraftDisplayName = preparedDispatch.AircraftDisplayName,
                AircraftVariantCode = preparedDispatch.AircraftVariantCode,
                AddonProvider = preparedDispatch.AddonProvider,
                RouteText = preparedDispatch.RouteText,
                PlannedAltitude = ParseCruiseLevelAsFeet(preparedDispatch.CruiseLevel),
                PlannedSpeed = null,
                CruiseLevel = preparedDispatch.CruiseLevel,
                AlternateIcao = preparedDispatch.AlternateIcao,
                DispatchToken = preparedDispatch.DispatchToken,
                SimbriefUsername = preparedDispatch.SimbriefUsername,
                Remarks = preparedDispatch.Remarks,
                ScheduledDepartureUtc = preparedDispatch.ScheduledDepartureUtc,
                ReadyForAcars = preparedDispatch.IsDispatchReady,
                SimbriefStatus = preparedDispatch.SimbriefStatus,
                ReservationStatus = preparedDispatch.ReservationStatus,
                DispatchStatus = preparedDispatch.DispatchPackageStatus,
                PassengerCount = preparedDispatch.PassengerCount,
                CargoKg = preparedDispatch.CargoKg,
                FuelPlannedKg = preparedDispatch.FuelPlannedKg,
                PayloadKg = preparedDispatch.PayloadKg,
                ZeroFuelWeightKg = preparedDispatch.ZeroFuelWeightKg,
                ScheduledBlockMinutes = preparedDispatch.ScheduledBlockMinutes,
                ExpectedBlockP50Minutes = preparedDispatch.ExpectedBlockP50Minutes,
                ExpectedBlockP80Minutes = preparedDispatch.ExpectedBlockP80Minutes
            };
        }

        private static int? ParseCruiseLevelAsFeet(string cruiseLevel)
        {
            var normalized = (cruiseLevel ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (normalized.StartsWith("FL"))
            {
                normalized = normalized.Substring(2);
            }

            if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return null;
            }

            return parsed >= 1000 ? parsed : parsed * 100;
        }

        private PreparedDispatch MapPreparedDispatch(
            Dictionary<string, object> reservationRow,
            Dictionary<string, object>? dispatchPackage)
        {
            var safeDispatchPackage = dispatchPackage ?? new Dictionary<string, object>();

            var reservationFlightRef = FirstNonEmpty(reservationRow, "flight_designator", "flight_number", "route_code", "reservation_code");
            var dispatchFlightRef = FirstNonEmpty(
                safeDispatchPackage,
                "flight_designator",
                "flight_number",
                "route_code",
                "reservation_code");

            var flightDesignator = NormalizeCommercialFlightNumber(reservationFlightRef, dispatchFlightRef);
            if (string.IsNullOrWhiteSpace(flightDesignator))
                flightDesignator = FirstNonEmpty(safeDispatchPackage, "flight_designator", "flight_number", "route_code",
                    "reservation_code");
            if (string.IsNullOrWhiteSpace(flightDesignator))
                flightDesignator = FirstNonEmpty(reservationRow, "flight_designator", "flight_number", "route_code",
                    "reservation_code");

            var reservationAircraftRef = FirstNonEmpty(reservationRow, "aircraft_type_code", "aircraft_code");
            var dispatchAircraftRef = FirstNonEmpty(safeDispatchPackage, "aircraft_type_code", "aircraft_code");
            var aircraftIcao = ResolveAcarsAircraftIcao(
                !string.IsNullOrWhiteSpace(reservationAircraftRef) ? reservationAircraftRef : dispatchAircraftRef);

            // Extraer simbrief_normalized / simbrief_ofp_json como fuentes adicionales de datos SimBrief.
            object rawSimbriefNormalized;
            object rawSimbriefOfp;
            safeDispatchPackage.TryGetValue("simbrief_normalized", out rawSimbriefNormalized);
            safeDispatchPackage.TryGetValue("simbrief_ofp_json", out rawSimbriefOfp);

            var sbNorm = rawSimbriefNormalized == null
                ? new Dictionary<string, object>()
                : TryDeserializeJsonObject(rawSimbriefNormalized);
            var sbOfp = rawSimbriefOfp == null
                ? new Dictionary<string, object>()
                : TryDeserializeJsonObject(rawSimbriefOfp);

            var routeText = FirstNonEmpty(
                safeDispatchPackage,
                "route_text",
                "route",
                "route_string",
                "route_full",
                "ofp_route",
                "navlog_route",
                "simbrief_route",
                "ats_route",
                "route_summary",
                "route_text_compact");

            if (string.IsNullOrWhiteSpace(routeText))
                routeText = FirstNonEmpty(sbNorm, "route_text", "route", "route_string", "route_full", "navlog_route", "simbrief_route", "ats_route");
            if (string.IsNullOrWhiteSpace(routeText))
                routeText = FirstNonEmpty(sbOfp, "route_text", "route", "route_string", "route_full", "navlog_route", "simbrief_route", "ats_route");
            if (string.IsNullOrWhiteSpace(routeText))
                routeText = FirstNonEmpty(reservationRow, "route_text");

            // alternate_icao: columna directa, normalized u OFP.
            var alternateIcao = FirstNonEmpty(safeDispatchPackage, "alternate_icao", "alternate_ident", "alternate");
            if (string.IsNullOrWhiteSpace(alternateIcao))
                alternateIcao = FirstNonEmpty(sbNorm, "alternate_icao", "alternate_ident", "alternate");
            if (string.IsNullOrWhiteSpace(alternateIcao))
                alternateIcao = FirstNonEmpty(sbOfp, "alternate_icao", "alternate_ident", "alternate");

            // dispatch_token: columna directa, normalized u OFP
            var dispatchToken = FirstNonEmpty(safeDispatchPackage, "dispatch_token", "token");
            if (string.IsNullOrWhiteSpace(dispatchToken))
                dispatchToken = FirstNonEmpty(sbNorm, "dispatch_token", "token");
            if (string.IsNullOrWhiteSpace(dispatchToken))
                dispatchToken = FirstNonEmpty(sbOfp, "dispatch_token", "token");

            // Datos de carga/combustible: columnas reales de dispatch_packages o payloads SimBrief.
            var passengerCount = ConvertToInt(safeDispatchPackage, "passenger_count",
                                     ConvertToInt(sbNorm, "passenger_count",
                                         ConvertToInt(sbOfp, "passenger_count",
                                             ConvertToInt(safeDispatchPackage, "pax_count", 0))));
            var cargoKg = FirstNonEmptyNumber(safeDispatchPackage, "cargo_kg");
            if (cargoKg <= 0) cargoKg = FirstNonEmptyNumber(sbNorm, "cargo_kg");
            if (cargoKg <= 0) cargoKg = FirstNonEmptyNumber(sbOfp, "cargo_kg");

            var fuelPlannedKg = FirstNonEmptyNumber(safeDispatchPackage, "planned_fuel_kg", "fuel_planned_kg", "block_fuel_kg", "fuel_block_kg");
            if (fuelPlannedKg <= 0) fuelPlannedKg = FirstNonEmptyNumber(sbNorm, "block_fuel_kg", "fuel_planned_kg", "planned_fuel_kg");
            if (fuelPlannedKg <= 0) fuelPlannedKg = FirstNonEmptyNumber(sbOfp, "block_fuel_kg", "fuel_planned_kg", "planned_fuel_kg");

            var payloadKg = FirstNonEmptyNumber(safeDispatchPackage, "planned_payload_kg", "payload_kg");
            if (payloadKg <= 0) payloadKg = FirstNonEmptyNumber(sbNorm, "payload_kg", "planned_payload_kg");
            if (payloadKg <= 0) payloadKg = FirstNonEmptyNumber(sbOfp, "payload_kg", "planned_payload_kg");

            var zfwKg = FirstNonEmptyNumber(safeDispatchPackage, "zero_fuel_weight_kg");
            if (zfwKg <= 0) zfwKg = FirstNonEmptyNumber(sbNorm, "zero_fuel_weight_kg", "zfw_kg");
            if (zfwKg <= 0) zfwKg = FirstNonEmptyNumber(sbOfp, "zero_fuel_weight_kg", "zfw_kg");

            var scheduledBlock = ConvertToInt(safeDispatchPackage, "scheduled_block_minutes",
                                     ConvertToInt(sbNorm, "scheduled_block_minutes",
                                         ConvertToInt(sbOfp, "scheduled_block_minutes",
                                             ConvertToInt(safeDispatchPackage, "estimated_enroute_min", 0))));
            var blockP50 = ConvertToInt(safeDispatchPackage, "expected_block_p50_minutes",
                               ConvertToInt(sbNorm, "expected_block_p50_minutes",
                                   ConvertToInt(sbOfp, "expected_block_p50_minutes", 0)));
            var blockP80 = ConvertToInt(safeDispatchPackage, "expected_block_p80_minutes",
                               ConvertToInt(sbNorm, "expected_block_p80_minutes",
                                   ConvertToInt(sbOfp, "expected_block_p80_minutes", 0)));
            var scheduledDeparture = ConvertToDateTime(safeDispatchPackage, "planned_offblock_at")
                                     ?? ConvertToDateTime(safeDispatchPackage, "planned_engine_start_at")
                                     ?? ConvertToDateTime(reservationRow, "scheduled_departure");

            return new PreparedDispatch
            {
                ReservationId      = FirstNonEmpty(reservationRow, "reservation_id", "id"),
                DispatchId         = FirstNonEmpty(safeDispatchPackage, "id", "dispatch_id") is string dpId && !string.IsNullOrEmpty(dpId) ? dpId : FirstNonEmpty(reservationRow, "dispatch_id"),
                DispatchToken      = dispatchToken,
                PilotUserId        = FirstNonEmpty(reservationRow, "pilot_user_id", "pilot_id", "user_id"),
                RankCode           = FirstNonEmpty(reservationRow, "rank_code"),
                CareerRankCode     = FirstNonEmpty(reservationRow, "career_rank_code"),
                BaseHubCode        = FirstNonEmpty(reservationRow, "base_hub_code", "base_hub"),
                CurrentAirportCode = FirstNonEmpty(reservationRow, "current_airport_code", "current_airport_icao"),
                FlightNumber       = flightDesignator,
                FlightDesignator   = flightDesignator,
                RouteCode          = FirstNonEmpty(reservationRow, "route_code", "reservation_code") is string rc && !string.IsNullOrWhiteSpace(rc)
                                        ? rc
                                        : FirstNonEmpty(safeDispatchPackage, "route_code"),
                DepartureIcao      = FirstNonEmpty(reservationRow, "origin_ident", "origin_icao") is string dep && !string.IsNullOrWhiteSpace(dep)
                                        ? dep
                                        : FirstNonEmpty(safeDispatchPackage, "origin_ident", "origin_icao"),
                ArrivalIcao        = FirstNonEmpty(reservationRow, "destination_ident", "destination_icao") is string arr && !string.IsNullOrWhiteSpace(arr)
                                        ? arr
                                        : FirstNonEmpty(safeDispatchPackage, "planned_destination_ident", "destination_ident", "destination_icao"),
                AlternateIcao      = alternateIcao,
                AircraftId         = FirstNonEmpty(reservationRow, "aircraft_id") is string aid && !string.IsNullOrWhiteSpace(aid)
                                        ? aid
                                        : FirstNonEmpty(safeDispatchPackage, "aircraft_id"),
                AircraftIcao       = aircraftIcao,
                AircraftRegistration  = FirstNonEmpty(reservationRow, "aircraft_registration", "registration") is string reg && !string.IsNullOrWhiteSpace(reg)
                                        ? reg
                                        : FirstNonEmpty(safeDispatchPackage, "aircraft_registration", "registration"),
                AircraftDisplayName   = FirstNonEmpty(reservationRow, "aircraft_display_name", "display_name", "aircraft_name") is string ad && !string.IsNullOrWhiteSpace(ad)
                                        ? ad
                                        : FirstNonEmpty(safeDispatchPackage, "aircraft_display_name", "display_name", "aircraft_name"),
                AircraftVariantCode   = FirstNonEmpty(reservationRow, "aircraft_variant_code", "variant_code") is string av && !string.IsNullOrWhiteSpace(av)
                                        ? av
                                        : FirstNonEmpty(safeDispatchPackage, "aircraft_variant_code", "variant_code"),
                AddonProvider         = FirstNonEmpty(reservationRow, "addon_provider") is string ap && !string.IsNullOrWhiteSpace(ap)
                                        ? ap
                                        : FirstNonEmpty(safeDispatchPackage, "addon_provider"),
                RouteText          = routeText.Trim(),
                FlightMode         = FirstNonEmpty(reservationRow, "flight_mode_code", "flight_mode"),
                ReservationStatus  = FirstNonEmpty(reservationRow, "status").Trim().ToLowerInvariant(),
                // dispatch_status: desde dispatch package, fallback a reservation row, default a "prepared"
                DispatchPackageStatus = GetDispatchPackageStatus(safeDispatchPackage, reservationRow),
                SimbriefStatus     = FirstNonEmpty(safeDispatchPackage, "simbrief_status"),
                SimbriefUsername   = FirstNonEmpty(safeDispatchPackage, "simbrief_username"),
                // cruise_fl = nombre real de la columna en dispatch_packages
                CruiseLevel        = FirstNonEmpty(safeDispatchPackage, "cruise_fl", "cruise_level", "initial_altitude", "planned_altitude"),
                Remarks            = FirstNonEmpty(reservationRow, "remarks"),
                ScheduledDepartureUtc = scheduledDeparture,
                PassengerCount     = passengerCount,
                CargoKg            = cargoKg,
                FuelPlannedKg      = fuelPlannedKg,
                PayloadKg          = payloadKg,
                ZeroFuelWeightKg   = zfwKg,
                ScheduledBlockMinutes   = scheduledBlock,
                ExpectedBlockP50Minutes = blockP50,
                ExpectedBlockP80Minutes = blockP80,
            };
        }

        private string ValidateFlightAgainstDispatch(Flight flight, PreparedDispatch dispatch)
        {
            var expectedFlightNumber = NormalizeCommercialFlightNumber(dispatch.FlightDesignator, dispatch.FlightNumber);
            var actualFlightNumber = NormalizeCommercialFlightNumber(flight.FlightNumber, flight.FlightNumber);
            if (!string.Equals(expectedFlightNumber, actualFlightNumber, StringComparison.OrdinalIgnoreCase))
            {
                return $"El numero de vuelo no coincide con el despacho web ({expectedFlightNumber}).";
            }

            var expectedOrigin = (dispatch.DepartureIcao ?? string.Empty).Trim().ToUpperInvariant();
            var actualOrigin = (flight.DepartureIcao ?? string.Empty).Trim().ToUpperInvariant();
            if (!string.Equals(expectedOrigin, actualOrigin, StringComparison.OrdinalIgnoreCase))
            {
                return $"El origen no coincide con el despacho web ({expectedOrigin}).";
            }

            var expectedDestination = (dispatch.ArrivalIcao ?? string.Empty).Trim().ToUpperInvariant();
            var actualDestination = (flight.ArrivalIcao ?? string.Empty).Trim().ToUpperInvariant();
            if (!string.Equals(expectedDestination, actualDestination, StringComparison.OrdinalIgnoreCase))
            {
                return $"El destino no coincide con el despacho web ({expectedDestination}).";
            }

            // Solo validar aeronave si el despacho trae un ICAO resuelto.
            // Si la web no guardó aircraft_type_code (reservas legacy), se omite
            // para no bloquear el inicio del vuelo innecesariamente.
            var expectedAircraft = ResolveAcarsAircraftIcao(dispatch.AircraftIcao);
            if (!string.IsNullOrWhiteSpace(expectedAircraft))
            {
                var actualAircraft = ResolveAcarsAircraftIcao(flight.AircraftIcao);
                if (!string.IsNullOrWhiteSpace(actualAircraft) &&
                    !string.Equals(expectedAircraft, actualAircraft, StringComparison.OrdinalIgnoreCase))
                {
                    return $"El airframe no coincide con el despacho web ({expectedAircraft}).";
                }
            }

            return string.Empty;
        }

        private static string NormalizeCommercialFlightNumber(string preferred, string fallback)
        {
            var candidate = !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;
            var normalized = (candidate ?? string.Empty).Trim().ToUpperInvariant().Replace(" ", string.Empty);
            if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

            if (normalized.Contains("-"))
            {
                var digits = ExtractFlightDigits(normalized);
                return string.IsNullOrWhiteSpace(digits) ? string.Empty : "PWG" + digits;
            }

            if (normalized.StartsWith("PWG"))
            {
                var digits = ExtractFlightDigits(normalized);
                return string.IsNullOrWhiteSpace(digits) ? normalized : "PWG" + digits;
            }

            var suffixDigits = ExtractFlightDigits(normalized);
            return string.IsNullOrWhiteSpace(suffixDigits) ? normalized : "PWG" + suffixDigits;
        }

        private static string ExtractFlightDigits(string value)
        {
            var digits = string.Empty;
            foreach (var character in value ?? string.Empty)
            {
                if (char.IsDigit(character))
                {
                    digits += character;
                }
            }

            if (string.IsNullOrWhiteSpace(digits))
            {
                return string.Empty;
            }

            if (digits.Length > 3)
            {
                digits = digits.Substring(digits.Length - 3);
            }

            return digits.PadLeft(3, '0');
        }

        private static string ResolveAcarsAircraftIcao(string value)
        {
            var normalized = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;

            if (normalized.StartsWith("ATR72") || normalized.StartsWith("AT76")) return "AT76";
            if (normalized.StartsWith("B736") || normalized == "B737-700") return "B737";
            if (normalized.StartsWith("B738") || normalized == "B737-800") return "B738";
            if (normalized.StartsWith("B739") || normalized == "B737-900") return "B739";
            if (normalized.StartsWith("B38M")) return "B38M";
            if (normalized.StartsWith("A319")) return "A319";
            if (normalized.StartsWith("A320")) return "A320";
            if (normalized.StartsWith("A20N")) return "A20N";
            if (normalized.StartsWith("A321")) return "A321";
            if (normalized.StartsWith("A21N")) return "A21N";
            if (normalized.StartsWith("A339")) return "A339";
            if (normalized.StartsWith("A359")) return "A359";
            if (normalized.StartsWith("B77W")) return "B77W";
            if (normalized.StartsWith("B772")) return "B772";
            if (normalized.StartsWith("B789")) return "B789";
            if (normalized.StartsWith("B78X")) return "B78X";
            if (normalized.StartsWith("B350")) return "B350";
            if (normalized.StartsWith("BE58")) return "BE58";
            if (normalized.StartsWith("C208")) return "C208";
            if (normalized.StartsWith("E175")) return "E175";
            if (normalized.StartsWith("E190")) return "E190";
            if (normalized.StartsWith("E195")) return "E195";
            if (normalized.StartsWith("MD82")) return "MD82";
            if (normalized.StartsWith("MD83")) return "MD83";
            if (normalized.StartsWith("MD88")) return "MD88";
            if (normalized.StartsWith("TBM8")) return "TBM8";
            if (normalized.StartsWith("TBM9")) return "TBM9";

            return normalized;
        }

        private Dictionary<string, object> TryDeserializeJsonObject(object raw)
        {
            if (raw is Dictionary<string, object> dict)
            {
                return dict;
            }

            var rawString = raw as string ?? Convert.ToString(raw, CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(rawString))
            {
                return new Dictionary<string, object>();
            }

            try
            {
                var parsed = _json.DeserializeObject(rawString) as Dictionary<string, object>;
                return parsed ?? new Dictionary<string, object>();
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private void WriteAuthLog(string message)
        {
            try
            {
                File.AppendAllText(
                    _authLogFile,
                    $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
            }
            catch
            {
                // no romper auth por logging
            }
        }

        private string SimplifySupabaseError(string raw)
        {
            try
            {
                var doc = _json.DeserializeObject(raw) as Dictionary<string, object>;
                var message = doc is null ? null : FirstNonEmpty(doc, "msg", "message", "error_description", "error");
                return string.IsNullOrWhiteSpace(message)
                    ? (raw ?? string.Empty)
                    : (message ?? string.Empty);
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

        private static string GetDispatchPackageStatus(Dictionary<string, object> dispatchPackage, Dictionary<string, object> reservationRow)
        {
            // Primero intentar desde el dispatch_package
            var status = FirstNonEmpty(dispatchPackage, "dispatch_status", "status");
            if (!string.IsNullOrWhiteSpace(status))
                return status.Trim().ToLowerInvariant();

            // Fallback a la reserva
            status = FirstNonEmpty(reservationRow, "dispatch_status");
            if (!string.IsNullOrWhiteSpace(status))
                return status.Trim().ToLowerInvariant();

            // Default: prepared (asume que existe porque hay un dispatch package)
            return "prepared";
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
