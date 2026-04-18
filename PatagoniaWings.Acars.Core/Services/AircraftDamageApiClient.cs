using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    public class AircraftDamageApiClient
    {
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json;
        private readonly string _supabaseUrl;
        private readonly string _supabaseAnonKey;
        private readonly bool _useSupabaseDirect;
        private readonly string _logFile;
        private string _token = string.Empty;

        public AircraftDamageApiClient(string supabaseUrl, string supabaseAnonKey, bool useSupabaseDirect)
        {
            _supabaseUrl = (supabaseUrl ?? string.Empty).TrimEnd('/');
            _supabaseAnonKey = supabaseAnonKey ?? string.Empty;
            _useSupabaseDirect = useSupabaseDirect;

            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _json = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 64
            };

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logFolder = Path.Combine(appData, "PatagoniaWings", "Acars", "logs");
            Directory.CreateDirectory(logFolder);
            _logFile = Path.Combine(logFolder, "damage-sync.log");
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
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            }
        }

        public async Task<ApiResult<AircraftDamageSubmissionResult>> SubmitDamageAsync(
            Flight? flight,
            double flightHours,
            int landingCycles,
            IReadOnlyList<AircraftDamageEvent> damageEvents)
        {
            if (!CanUseSupabaseDirect)
                return ApiResult<AircraftDamageSubmissionResult>.Fail("Supabase direct no está activo para damage sync.");

            if (flight == null)
                return ApiResult<AircraftDamageSubmissionResult>.Fail("No hay vuelo activo para sincronizar daño.");

            if (string.IsNullOrWhiteSpace(flight.AircraftId))
                return ApiResult<AircraftDamageSubmissionResult>.Fail("El vuelo no trae AircraftId.");

            var result = new AircraftDamageSubmissionResult();

            try
            {
                // 1) desgaste base
                var wearOk = await ApplyNormalWearAsync(
                    flight.AircraftId,
                    flight.ReservationId,
                    flightHours,
                    landingCycles);

                if (!wearOk.Success)
                {
                    result.Errors.Add(wearOk.Error);
                }
                else
                {
                    result.BaseWearCalls = 1;
                }

                // 2) eventos de daño específicos
                if (damageEvents != null)
                {
                    foreach (var damageEvent in damageEvents)
                    {
                        if (damageEvent == null || !damageEvent.IsValid) continue;

                        var eventResult = await ApplyDamageEventAsync(damageEvent);
                        if (!eventResult.Success)
                        {
                            result.Errors.Add(eventResult.Error);
                            continue;
                        }

                        result.DamageEventsSubmitted++;
                    }
                }

                result.Success = result.Errors.Count == 0;
                if (!result.Success && string.IsNullOrWhiteSpace(result.Error) && result.Errors.Count > 0)
                    result.Error = string.Join(" | ", result.Errors);

                WriteLog("SubmitDamageAsync => baseWear=" + result.BaseWearCalls + " damageEvents=" + result.DamageEventsSubmitted + " success=" + result.Success);
                return ApiResult<AircraftDamageSubmissionResult>.Ok(result);
            }
            catch (Exception ex)
            {
                WriteLog("SubmitDamageAsync EXCEPTION => " + ex);
                return ApiResult<AircraftDamageSubmissionResult>.Fail(ex.Message);
            }
        }

        private async Task<ApiResult<bool>> ApplyNormalWearAsync(
            string aircraftId,
            string reservationId,
            double flightHours,
            int landingCycles)
        {
            try
            {
                using (var request = CreateSupabaseRequest(HttpMethod.Post, "/rest/v1/rpc/pw_apply_normal_aircraft_wear"))
                {
                    request.Content = new StringContent(
                        _json.Serialize(new
                        {
                            p_aircraft_id = aircraftId,
                            p_flight_hours = Math.Max(0, flightHours),
                            p_landing_cycles = Math.Max(0, landingCycles),
                            p_reservation_id = string.IsNullOrWhiteSpace(reservationId) ? null : reservationId
                        }),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _http.SendAsync(request).ConfigureAwait(false);
                    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                        return ApiResult<bool>.Fail("pw_apply_normal_aircraft_wear => " + SimplifyError(raw));

                    return ApiResult<bool>.Ok(true);
                }
            }
            catch (Exception ex)
            {
                return ApiResult<bool>.Fail(ex.Message);
            }
        }

        private async Task<ApiResult<bool>> ApplyDamageEventAsync(AircraftDamageEvent damageEvent)
        {
            try
            {
                using (var request = CreateSupabaseRequest(HttpMethod.Post, "/rest/v1/rpc/pw_apply_aircraft_damage"))
                {
                    request.Content = new StringContent(
                        _json.Serialize(new
                        {
                            p_aircraft_id = damageEvent.AircraftId,
                            p_event_code = damageEvent.EventCode,
                            p_phase = damageEvent.Phase,
                            p_severity = damageEvent.Severity,
                            p_reservation_id = string.IsNullOrWhiteSpace(damageEvent.ReservationId) ? null : damageEvent.ReservationId,
                            p_details_json = damageEvent.Details ?? new Dictionary<string, object>()
                        }),
                        Encoding.UTF8,
                        "application/json");

                    var response = await _http.SendAsync(request).ConfigureAwait(false);
                    var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                        return ApiResult<bool>.Fail(damageEvent.EventCode + " => " + SimplifyError(raw));

                    return ApiResult<bool>.Ok(true);
                }
            }
            catch (Exception ex)
            {
                return ApiResult<bool>.Fail(damageEvent.EventCode + " => " + ex.Message);
            }
        }

        private HttpRequestMessage CreateSupabaseRequest(HttpMethod method, string relativePath)
        {
            var url = _supabaseUrl + relativePath;
            var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("apikey", _supabaseAnonKey);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            if (!_http.DefaultRequestHeaders.Contains("apikey"))
            {
                _http.DefaultRequestHeaders.TryAddWithoutValidation("apikey", _supabaseAnonKey);
            }
            return request;
        }

        private string SimplifyError(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "respuesta vacía";
            try
            {
                var parsed = _json.DeserializeObject(raw) as Dictionary<string, object>;
                if (parsed != null)
                {
                    if (parsed.ContainsKey("message")) return Convert.ToString(parsed["message"]) ?? raw;
                    if (parsed.ContainsKey("hint")) return Convert.ToString(parsed["hint"]) ?? raw;
                    if (parsed.ContainsKey("error_description")) return Convert.ToString(parsed["error_description"]) ?? raw;
                }
            }
            catch
            {
            }
            return raw;
        }

        private void WriteLog(string line)
        {
            try
            {
                File.AppendAllText(_logFile, DateTime.UtcNow.ToString("o") + " " + line + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
