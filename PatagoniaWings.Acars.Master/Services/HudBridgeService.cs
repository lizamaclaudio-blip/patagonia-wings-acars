using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.Services
{
    public sealed class HudBridgeService
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private string _lastBridgeError = string.Empty;

        public bool Enabled { get; private set; }
        public int Port { get; private set; } = 37677;
        public int UpdateRateHz { get; private set; } = 2;
        public bool OnlyInFlight { get; private set; } = true;
        public string HudTheme { get; private set; } = "patagonia-navy";
        public bool IsBridgeListening => _listener != null && _listener.IsListening;
        public string LastBridgeError => _lastBridgeError;

        public void Initialize()
        {
            var prefs = UiPreferencesStore.Load();
            // Hotfix operativo: HUD debe estar disponible desde el arranque de ACARS.
            Enabled = true;
            Port = ClampPort(prefs.LocalHudPort);
            UpdateRateHz = ClampHz(prefs.HudUpdateRateHz);
            OnlyInFlight = false;
            HudTheme = string.IsNullOrWhiteSpace(prefs.HudTheme) ? "patagonia-navy" : prefs.HudTheme.Trim();

            prefs.EnableInSimHud = true;
            prefs.HudOnlyInFlight = false;
            UiPreferencesStore.Save(prefs);

            if (Enabled)
            {
                Start();
            }
        }

        public void ApplySettings(bool enabled, int port, int hz, bool onlyInFlight, string hudTheme)
        {
            Enabled = enabled;
            Port = ClampPort(port);
            UpdateRateHz = ClampHz(hz);
            OnlyInFlight = onlyInFlight;
            HudTheme = string.IsNullOrWhiteSpace(hudTheme) ? "patagonia-navy" : hudTheme.Trim();

            var prefs = UiPreferencesStore.Load();
            prefs.EnableInSimHud = Enabled;
            prefs.LocalHudPort = Port;
            prefs.HudUpdateRateHz = UpdateRateHz;
            prefs.HudOnlyInFlight = OnlyInFlight;
            prefs.HudTheme = HudTheme;
            UiPreferencesStore.Save(prefs);

            if (!Enabled)
            {
                Stop();
                return;
            }

            Restart();
        }

        public string GetStateUrl() => "http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + "/api/hud/state";

        public string GetPackageFolderPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var repoRoot = FindRepoRoot(baseDir);
            if (!string.IsNullOrWhiteSpace(repoRoot))
            {
                return Path.Combine(repoRoot, "packages", "patagoniawings-acars-hud");
            }

            var installedBundle = Path.Combine(baseDir, "MSFS-HUD-Package", "patagoniawings-acars-hud");
            if (Directory.Exists(installedBundle))
            {
                return installedBundle;
            }

            return Path.Combine(baseDir, "packages", "patagoniawings-acars-hud");
        }

        public string GetHealthText()
        {
            if (!Enabled)
            {
                return "HUD desactivado";
            }

            if (_listener == null || !_listener.IsListening)
            {
                if (!string.IsNullOrWhiteSpace(_lastBridgeError))
                {
                    return "HUD puente local detenido: " + _lastBridgeError;
                }
                return "HUD puente local detenido";
            }

            return "HUD disponible en " + GetStateUrl();
        }

        public string[] DetectCommunityFolders()
        {
            var candidates = new List<string>();
            void AddIfValid(string path)
            {
                if (string.IsNullOrWhiteSpace(path)) return;
                var normalized = path.Trim();
                if (!candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(normalized);
                }
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            AddIfValid(Path.Combine(localAppData, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "Packages", "Community"));
            AddIfValid(Path.Combine(appData, "Microsoft Flight Simulator", "Packages", "Community"));

            foreach (var userCfg in GetKnownUserCfgLocations(localAppData, appData))
            {
                var custom = TryReadCommunityFromUserCfg(userCfg);
                if (!string.IsNullOrWhiteSpace(custom))
                {
                    AddIfValid(custom);
                }
            }

            return candidates.ToArray();
        }

        public bool InstallHudToCommunity(out string statusMessage)
        {
            statusMessage = string.Empty;
            var source = GetPackageFolderPath();
            if (!Directory.Exists(source))
            {
                statusMessage = "Paquete HUD no encontrado: " + source;
                return false;
            }

            var communities = DetectCommunityFolders().Where(Directory.Exists).ToArray();
            if (communities.Length == 0)
            {
                statusMessage = "No se detectó carpeta Community automáticamente.";
                return false;
            }

            try
            {
                var installedTargets = new List<string>();
                foreach (var communityPath in communities)
                {
                    var target = Path.Combine(communityPath, "patagoniawings-acars-hud");
                    CopyDirectory(source, target);
                    installedTargets.Add(target);
                }
                statusMessage = "HUD instalado en: " + string.Join(" | ", installedTargets);
                return true;
            }
            catch (Exception ex)
            {
                statusMessage = "Falló instalación HUD: " + ex.Message;
                return false;
            }
        }

        public void Shutdown()
        {
            Stop();
        }

        private void Start()
        {
            try
            {
                Stop();
                _lastBridgeError = string.Empty;

                _cts = new CancellationTokenSource();
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://127.0.0.1:" + Port.ToString(CultureInfo.InvariantCulture) + "/");
                _listener.Start();
                _loopTask = Task.Run(() => ListenLoopAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                _lastBridgeError = ex.Message;
                Stop();
            }
        }

        private void Restart()
        {
            Stop();
            Start();
        }

        private void Stop()
        {
            try
            {
                _cts?.Cancel();
            }
            catch
            {
            }

            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch
            {
            }

            _listener = null;
            _cts = null;
            _loopTask = null;
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
            {
                HttpListenerContext? ctx = null;
                try
                {
                    var getContextTask = _listener.GetContextAsync();
                    var finished = await Task.WhenAny(getContextTask, Task.Delay(500, ct)).ConfigureAwait(false);
                    if (finished != getContextTask)
                    {
                        continue;
                    }

                    ctx = getContextTask.Result;
                    await HandleRequestAsync(ctx).ConfigureAwait(false);
                }
                catch
                {
                    if (ctx != null)
                    {
                        try { ctx.Response.Close(); } catch { }
                    }
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context)
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, OPTIONS";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type";

            if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = 204;
                context.Response.Close();
                return;
            }

            var path = context.Request.Url != null ? context.Request.Url.AbsolutePath : "/";
            if (string.Equals(path, "/api/hud/state", StringComparison.OrdinalIgnoreCase))
            {
                var payload = BuildHudStatePayload();
                await WriteJsonAsync(context.Response, payload).ConfigureAwait(false);
                return;
            }

            if (string.Equals(path, "/health", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(path, "/api/hud/health", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context.Response, new
                {
                    ok = true,
                    enabled = Enabled,
                    listening = IsBridgeListening,
                    url = GetStateUrl(),
                    error = _lastBridgeError
                }).ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 404;
            await WriteJsonAsync(context.Response, new { ok = false, error = "not_found" }).ConfigureAwait(false);
        }

        private object BuildHudStatePayload()
        {
            var runtime = AcarsContext.Runtime;
            var flight = AcarsContext.FlightService.CurrentFlight;
            var data = AcarsContext.FlightService.LastSimData;
            var phase = AcarsContext.FlightService.CurrentFlightPhase;

            var isConnected = runtime != null && runtime.IsSimulatorConnected;
            var isFlightActive = flight != null;
            var inFlightPhase = phase != FlightPhase.Disconnected &&
                                phase != FlightPhase.PreFlight &&
                                phase != FlightPhase.Boarding;
            var shouldExposeLive = !OnlyInFlight || inFlightPhase;
            var registration = runtime != null && runtime.CurrentDispatch != null
                ? runtime.CurrentDispatch.AircraftRegistration
                : string.Empty;

            bool showDoors = true;
            bool showXpdr = true;
            bool showGear = true;

            var profileCode = data != null ? (data.DetectedProfileCode ?? string.Empty) : string.Empty;
            if (profileCode.StartsWith("C208", StringComparison.OrdinalIgnoreCase) ||
                profileCode.StartsWith("BE58", StringComparison.OrdinalIgnoreCase))
            {
                showDoors = false;
            }

            var altitude = data != null ? (data.IndicatedAltitudeFeet > 0 ? data.IndicatedAltitudeFeet : data.AltitudeFeet) : 0;
            var fuelCapacityKg = Math.Round(data != null ? (data.FuelTotalCapacityLbs * 0.45359237d) : 0, 0);
            if (fuelCapacityKg <= 10)
            {
                fuelCapacityKg = 0;
            }
            var phaseCode = phase.ToString().ToUpperInvariant();

            return new
            {
                connected = isConnected,
                flightActive = isFlightActive,
                telemetryVisible = shouldExposeLive,
                simConnected = isConnected,
                profileCode = profileCode,
                detectionConfidence = data != null ? (data.DetectionConfidence ?? "unknown") : "unknown",
                phase = phaseCode,
                hudTheme = HudTheme,
                updateRateHz = UpdateRateHz,
                flightNumber = flight != null ? flight.FlightNumber : string.Empty,
                callsign = runtime != null && runtime.CurrentPilot != null ? runtime.CurrentPilot.CallSign : string.Empty,
                pilotName = runtime != null && runtime.CurrentPilot != null ? runtime.CurrentPilot.FullName : string.Empty,
                pilotRankName = runtime != null && runtime.CurrentPilot != null ? runtime.CurrentPilot.RankName : string.Empty,
                pilotRankCode = runtime != null && runtime.CurrentPilot != null
                    ? (!string.IsNullOrWhiteSpace(runtime.CurrentPilot.CareerRankCode)
                        ? runtime.CurrentPilot.CareerRankCode
                        : runtime.CurrentPilot.RankCode)
                    : string.Empty,
                dep = flight != null ? flight.DepartureIcao : string.Empty,
                arr = flight != null ? flight.ArrivalIcao : string.Empty,
                eta = string.Empty,
                ete = string.Empty,
                aircraftType = flight != null ? flight.AircraftIcao : string.Empty,
                aircraftDisplayName = flight != null ? flight.AircraftDisplayName : string.Empty,
                registration = registration,
                indicatedAltitudeFt = Math.Round(altitude, 0),
                altitudeFt = Math.Round(altitude, 0),
                groundSpeedKt = Math.Round(data != null ? data.GroundSpeed : 0, 0),
                headingDeg = Math.Round(data != null ? data.Heading : 0, 0),
                verticalSpeedFpm = Math.Round(data != null ? data.VerticalSpeed : 0, 0),
                qnh = data != null ? data.QNH : 0,
                fuelCurrentKg = Math.Round(data != null ? data.FuelKg : 0, 0),
                fuelCapacityKg = fuelCapacityKg > 0 ? fuelCapacityKg : (double?)null,
                fuelCapacityDisplay = fuelCapacityKg > 0 ? fuelCapacityKg.ToString("F0", CultureInfo.InvariantCulture) : "N/D",
                xpdrCode = data != null ? data.TransponderCode.ToString("D4", CultureInfo.InvariantCulture) : "0000",
                xpdrMode = ResolveXpdrMode(data),
                systems = new
                {
                    nav = ResolveSystemState(data != null && data.NavLightsOn),
                    beaconStrobe = ResolveSystemState(data != null && (data.BeaconLightsOn || data.StrobeLightsOn)),
                    taxi = ResolveSystemState(data != null && data.TaxiLightsOn),
                    landing = ResolveSystemState(data != null && data.LandingLightsOn),
                    gear = showGear ? ResolveGearState(data) : "unsupported",
                    apMaster = ResolveSystemState(data != null && data.AutopilotActive),
                    doors = showDoors ? ResolveDoorState(data) : "unsupported",
                    parkingBrake = ResolveSystemState(data != null && data.ParkingBrake),
                    xpdr = showXpdr ? ResolveXpdrMode(data) : "unsupported"
                },
                capabilities = new
                {
                    doors = new { visible = showDoors, supported = showDoors, reliable = showDoors, penaltyApplicable = false },
                    gear = new { visible = showGear, supported = showGear, reliable = showGear, penaltyApplicable = false },
                    xpdr = new { visible = showXpdr, supported = showXpdr, reliable = showXpdr, penaltyApplicable = false }
                }
            };
        }

        private static string ResolveSystemState(bool active)
        {
            return active ? "on" : "off";
        }

        private static string ResolveDoorState(SimData? data)
        {
            if (data == null) return "na";
            return data.DoorOpen ? "open" : "closed";
        }

        private static string ResolveGearState(SimData? data)
        {
            if (data == null) return "na";
            if (data.GearTransitioning) return "warning";
            return data.GearDown ? "down" : "up";
        }

        private static string ResolveXpdrMode(SimData? data)
        {
            if (data == null) return "na";
            if (data.TransponderCharlieMode) return "alt";
            if (data.TransponderStateRaw == 0) return "off";
            if (data.TransponderStateRaw == 1) return "sby";
            if (data.TransponderStateRaw == 2) return "test";
            return "on";
        }

        private static async Task WriteJsonAsync(HttpListenerResponse response, object payload)
        {
            var json = JsonConvert.SerializeObject(payload);
            var buffer = Encoding.UTF8.GetBytes(json);
            response.ContentType = "application/json; charset=utf-8";
            response.ContentEncoding = Encoding.UTF8;
            response.StatusCode = 200;
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Cache-Control"] = "no-store";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            response.OutputStream.Flush();
            response.Close();
        }

        private static int ClampPort(int value)
        {
            if (value < 1024) return 37677;
            if (value > 65535) return 37677;
            return value;
        }

        private static int ClampHz(int value)
        {
            if (value < 1) return 1;
            if (value > 5) return 5;
            return value;
        }

        private static IEnumerable<string> GetKnownUserCfgLocations(string localAppData, string appData)
        {
            yield return Path.Combine(localAppData, "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt");
            yield return Path.Combine(appData, "Microsoft Flight Simulator", "UserCfg.opt");
            yield return Path.Combine(localAppData, "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt");
            yield return Path.Combine(appData, "Microsoft Flight Simulator 2024", "UserCfg.opt");
        }

        private static string TryReadCommunityFromUserCfg(string userCfgPath)
        {
            try
            {
                if (!File.Exists(userCfgPath)) return string.Empty;
                foreach (var line in File.ReadAllLines(userCfgPath))
                {
                    var trimmed = line == null ? string.Empty : line.Trim();
                    if (!trimmed.StartsWith("InstalledPackagesPath", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var firstQuote = trimmed.IndexOf('"');
                    var lastQuote = trimmed.LastIndexOf('"');
                    if (firstQuote >= 0 && lastQuote > firstQuote)
                    {
                        var root = trimmed.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                        if (!string.IsNullOrWhiteSpace(root))
                        {
                            return Path.Combine(root, "Community");
                        }
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var name = Path.GetFileName(file);
                File.Copy(file, Path.Combine(targetDir, name), true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var name = Path.GetFileName(dir);
                CopyDirectory(dir, Path.Combine(targetDir, name));
            }
        }

        private static string FindRepoRoot(string startDirectory)
        {
            try
            {
                var dir = new DirectoryInfo(startDirectory);
                while (dir != null)
                {
                    var candidate = Path.Combine(dir.FullName, "ACARS NUEVO");
                    if (Directory.Exists(candidate))
                    {
                        return candidate;
                    }

                    var hasSolution = File.Exists(Path.Combine(dir.FullName, "PatagoniaWings.sln"));
                    if (hasSolution)
                    {
                        return dir.FullName;
                    }

                    dir = dir.Parent;
                }
            }
            catch
            {
            }

            return string.Empty;
        }
    }
}
