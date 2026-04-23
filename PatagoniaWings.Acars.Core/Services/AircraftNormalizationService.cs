using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    public static class AircraftNormalizationService
    {
        private static readonly object Sync = new object();
        private static string _loadedRoot = string.Empty;
        private static long _loadedStamp = -1;
        private static Dictionary<string, AircraftProfile> _profilesByCode =
            new Dictionary<string, AircraftProfile>(StringComparer.OrdinalIgnoreCase);

        private static readonly AircraftProfile DefaultProfile = new AircraftProfile
        {
            Code = "MSFS_NATIVE",
            DisplayName = "MSFS Native",
            FamilyGroup = "GENERIC",
            Simulator = "MSFS2020",
            AddonProvider = "Asobo",
            EngineCount = 1,
            IsPressurized = false,
            HasApu = false,
            ImageAsset = "default_aircraft.png",
            Supported = true,
            PrimaryTelemetrySource = "SimVar",
            TelemetrySourcePriority = new List<string> { "SimVar" },
            CapabilityAuditState = "LIMITADO",
            ExactTitles = new List<string>(),
            Matches = new List<string> { "*" },
            LightMode = "individual",
            RequiresLvars = false,
            LvarProfile = string.Empty,
            N1Source = "turb_n1",
            DoorSource = "exit_open_0",
            DoorOpenThresholdPercent = 5.0,
            SeatbeltSource = "native",
            SeatbeltDebounceFrames = 2,
            NoSmokingSource = "native",
            AutopilotSource = "native",
            AutopilotDebounceFrames = 2,
            TransponderStateSource = "native",
            TransponderStateDebounceFrames = 2,
            TransponderDefaultState = 1,
            TransponderCodeFormat = "decimal_or_bco16",
            TransponderCodeDebounceFrames = 2,
            SupportsFuelRead = true,
            SupportsPayloadRead = true,
            SupportsFlagsRead = true,
            SupportsParkingBrakeRead = true,
            SupportsApuRead = false,
            SupportsLightsRead = true,
            SupportsGearRead = true,
            SupportsDoorRead = false,
            SupportsBatteryRead = false,
            SupportsAvionicsRead = false,
            SupportsEngineRunRead = false,
            SupportsZfwReadback = true,
            SupportsQnhReadback = true,
            SupportsTransponderModeReadback = true,
            SupportsSquawkReadback = true,
            SupportsPushbackInference = true,
            SupportsFuelPumpReadback = false,
            SupportsContinuousIgnitionReadback = false,
            SupportsFireTestReadback = false,
            HasInertialSeparator = false,
            SupportsInertialSeparatorReadback = false
        };

        public static string ResolveCode(string aircraftTitle, string? baseDirectory = null)
        {
            return ResolveProfile(aircraftTitle, baseDirectory).Code;
        }

        public static AircraftProfile ResolveProfile(string aircraftTitle, string? baseDirectory = null)
        {
            EnsureLoaded(baseDirectory);
            var title = (aircraftTitle ?? string.Empty).Trim();
            if (title.Length == 0) return GetProfile("MSFS_NATIVE", baseDirectory);

            AircraftProfile lvfrProfile;
            if (TryResolveLvfrAirbus(title, out lvfrProfile))
                return lvfrProfile;

            foreach (var profile in _profilesByCode.Values)
            {
                foreach (var exact in profile.ExactTitles ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(exact)) continue;
                    var value = exact.Trim();
                    if (string.Equals(value, title, StringComparison.OrdinalIgnoreCase) ||
                        title.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0)
                        return profile;
                }
            }

            foreach (var profile in _profilesByCode.Values)
            {
                foreach (var match in profile.Matches ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(match) || match == "*") continue;
                    if (title.IndexOf(match.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                        return profile;
                }
            }

            return GetProfile("MSFS_NATIVE", baseDirectory);
        }

        public static AircraftProfile GetProfile(string profileCode, string? baseDirectory = null)
        {
            EnsureLoaded(baseDirectory);
            AircraftProfile profile;
            if (!string.IsNullOrWhiteSpace(profileCode) && _profilesByCode.TryGetValue(profileCode, out profile))
                return profile;
            return _profilesByCode["MSFS_NATIVE"];
        }


        private static bool TryResolveLvfrAirbus(string aircraftTitle, out AircraftProfile profile)
        {
            profile = null;
            var t = (aircraftTitle ?? string.Empty).Trim().ToUpperInvariant();
            if (t.Length == 0) return false;

            bool mentionsLvfr = t.Contains("LATINVFR") || t.Contains("LVFR");
            bool isAirbusFamily = t.Contains("A319") || t.Contains("A320") || t.Contains("A321");
            bool notKnownOtherAddon = !t.Contains("FENIX") && !t.Contains("FLYBYWIRE") && !t.Contains("A32NX")
                                      && !t.Contains("HEADWIND") && !t.Contains("INIBUILDS");

            if (!isAirbusFamily || !notKnownOtherAddon) return false;

            // Heurística pragmática para tu base actual:
            // Airbus A319/A320/A321 con título "Airbus ... CFM/IAE/..." y livery comercial
            // está entrando como genérico. Lo tratamos como LVFR para no mezclarlo con Fenix.
            bool looksLikeLvfrAirbus =
                mentionsLvfr
                || (t.Contains("AIRBUS A319") && (t.Contains("CFM") || t.Contains("IAE")))
                || (t.Contains("AIRBUS A320") && (t.Contains("CFM") || t.Contains("IAE")))
                || (t.Contains("AIRBUS A321") && (t.Contains("CFM") || t.Contains("IAE")));

            if (!looksLikeLvfrAirbus) return false;

            string code = t.Contains("A321") ? "A321_LVFR"
                        : t.Contains("A320") ? "A320_LVFR"
                        : "A319_LVFR";

            string display = code == "A321_LVFR" ? "Airbus A321 LVFR"
                           : code == "A320_LVFR" ? "Airbus A320 LVFR"
                           : "Airbus A319 LVFR";

            profile = new AircraftProfile
            {
                Code = code,
                DisplayName = display,
                FamilyGroup = "AIRBUS_NB",
                Simulator = "MSFS2020",
                AddonProvider = "LVFR",
                EngineCount = 2,
                IsPressurized = true,
                HasApu = true,
                ImageAsset = code.StartsWith("A321") ? "A321.png" : code.StartsWith("A320") ? "A320.png" : "A319.png",
                Supported = true,
                PrimaryTelemetrySource = "SimVar + FSUIPC",
                TelemetrySourcePriority = new List<string> { "SimVar", "FSUIPC" },
                CapabilityAuditState = "PARCIAL",
                ExactTitles = new List<string>(),
                Matches = new List<string> { "A319", "A320", "A321", "CFM", "IAE", "LATAM" },
                LightMode = "individual",
                RequiresLvars = false,
                LvarProfile = string.Empty,
                N1Source = "turb_n1",
                DoorSource = "exit_open_0",
                DoorOpenThresholdPercent = 5.0,
                SeatbeltSource = "native",
                SeatbeltDebounceFrames = 2,
                NoSmokingSource = "native",
                AutopilotSource = "native",
                AutopilotDebounceFrames = 2,
                TransponderStateSource = "native",
                TransponderStateDebounceFrames = 2,
                TransponderDefaultState = 1,
                TransponderCodeFormat = "decimal_or_bco16",
                TransponderCodeDebounceFrames = 2,
                PreferFsuipcAutopilot = true,
                PreferFsuipcTransponder = true,
                SupportsFuelRead = true,
                SupportsPayloadRead = true,
                SupportsFlagsRead = true,
                SupportsParkingBrakeRead = true,
                SupportsApuRead = true,
                SupportsLightsRead = true,
                SupportsGearRead = true,
                SupportsDoorRead = true,
                SupportsBatteryRead = true,
                SupportsAvionicsRead = true,
                SupportsEngineRunRead = true,
                SupportsZfwReadback = true,
                SupportsQnhReadback = true,
                SupportsTransponderModeReadback = true,
                SupportsSquawkReadback = true,
                SupportsPushbackInference = true,
                SupportsFuelPumpReadback = false,
                SupportsContinuousIgnitionReadback = false,
                SupportsFireTestReadback = false,
                HasInertialSeparator = false,
                SupportsInertialSeparatorReadback = false
            };
            ApplyDerivedDefaults(profile);
            return true;
        }

        private static void EnsureLoaded(string? baseDirectory = null)
        {
            var root = ResolveProfilesRoot(baseDirectory);
            var stamp = GetProfilesStamp(root);

            lock (Sync)
            {
                if (string.Equals(_loadedRoot, root, StringComparison.OrdinalIgnoreCase) &&
                    _loadedStamp == stamp &&
                    _profilesByCode.Count > 0)
                    return;

                _profilesByCode = LoadProfiles(root);
                _loadedRoot = root;
                _loadedStamp = stamp;
            }
        }

        private static string ResolveProfilesRoot(string? baseDirectory)
        {
            var appBase = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            var currentDirectory = Environment.CurrentDirectory ?? string.Empty;
            var candidates = new List<string>();

            AddProfilesCandidates(candidates, baseDirectory);
            AddProfilesCandidates(candidates, appBase);
            AddProfilesCandidates(candidates, exeDir);
            AddProfilesCandidates(candidates, currentDirectory);

            foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Path.Combine(appBase, "AircraftProfiles");
        }

        private static void AddProfilesCandidates(List<string> candidates, string? root)
        {
            foreach (var ancestor in EnumerateAncestors(root))
            {
                candidates.Add(Path.Combine(ancestor, "AircraftProfiles"));
                candidates.Add(Path.Combine(ancestor, "PatagoniaWings.Acars.SimConnect", "AircraftProfiles"));
            }
        }

        private static IEnumerable<string> EnumerateAncestors(string? startPath)
        {
            if (string.IsNullOrWhiteSpace(startPath))
            {
                yield break;
            }

            DirectoryInfo current;
            try
            {
                current = new DirectoryInfo(startPath);
            }
            catch
            {
                yield break;
            }

            while (current != null)
            {
                yield return current.FullName;
                current = current.Parent;
            }
        }

        private static long GetProfilesStamp(string root)
        {
            if (!Directory.Exists(root)) return -1;
            long maxTicks = 0;
            foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
            {
                var ticks = File.GetLastWriteTimeUtc(file).Ticks;
                if (ticks > maxTicks) maxTicks = ticks;
            }
            return maxTicks;
        }

        private static Dictionary<string, AircraftProfile> LoadProfiles(string root)
        {
            var registry = new Dictionary<string, AircraftProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["MSFS_NATIVE"] = DefaultProfile
            };

            if (!Directory.Exists(root))
            {
                Debug.WriteLine("[AircraftProfile] Carpeta AircraftProfiles no encontrada. Usando MSFS_NATIVE.");
                return registry;
            }

            var serializer = new JavaScriptSerializer();

            foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).OrderBy(Path.GetFileName))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var profile = serializer.Deserialize<AircraftProfile>(json);
                    if (profile == null) continue;

                    if (string.IsNullOrWhiteSpace(profile.Code))
                        profile.Code = Path.GetFileNameWithoutExtension(file).Trim().ToUpperInvariant();

                    profile.ExactTitles = profile.ExactTitles ?? new List<string>();
                    profile.Matches = profile.Matches ?? new List<string>();
                    ApplyDerivedDefaults(profile);
                    registry[profile.Code] = profile;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[AircraftProfile] Error cargando " + file + ": " + ex.Message);
                }
            }

            if (!registry.ContainsKey("MSFS_NATIVE"))
                registry["MSFS_NATIVE"] = DefaultProfile;

            Debug.WriteLine("[AircraftProfile] Catálogo cargado: " + registry.Count + " perfiles desde " + root);
            return registry;
        }

        private static void ApplyDerivedDefaults(AircraftProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            if (profile.SupportsFuelRead)
            {
                profile.SupportsQnhReadback = true;
                profile.SupportsZfwReadback = true;
                profile.SupportsPayloadRead = true;
            }

            if (string.Equals(profile.FamilyGroup, "C208_FAMILY", StringComparison.OrdinalIgnoreCase))
            {
                profile.HasInertialSeparator = true;
            }

            if (!string.IsNullOrWhiteSpace(profile.MobiFlightInertialSeparatorExpression))
            {
                profile.SupportsInertialSeparatorReadback = true;
                if (string.IsNullOrWhiteSpace(profile.InertialSeparatorSource))
                {
                    profile.InertialSeparatorSource = "mobiflight";
                }
            }
        }
    }
}
