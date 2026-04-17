using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PatagoniaWings.Acars.SimConnect
{
    internal enum AircraftLightStrategy
    {
        Hybrid,
        Bitmask,
        Individual
    }

    [DataContract]
    internal sealed class AircraftProfilesDocument
    {
        [DataMember(Name = "profiles")]
        public List<AircraftProfileDefinition> Profiles { get; set; } = new List<AircraftProfileDefinition>();
    }

    [DataContract]
    internal sealed class AircraftProfileDefinition
    {
        [DataMember(Name = "name")]
        public string Name { get; set; } = "MSFS Native";

        [DataMember(Name = "matches")]
        public List<string> Matches { get; set; } = new List<string>();

        [DataMember(Name = "exact_titles")]
        public List<string> ExactTitles { get; set; } = new List<string>();

        [DataMember(Name = "lights")]
        public AircraftProfileLightsDefinition Lights { get; set; } = new AircraftProfileLightsDefinition();

        [DataMember(Name = "supported")]
        public bool Supported { get; set; } = true;

        public bool MatchesTitle(string aircraftTitle)
        {
            var title = (aircraftTitle ?? string.Empty).Trim();
            if (title.Length == 0)
            {
                return false;
            }

            if (ExactTitles.Any(exact =>
                !string.IsNullOrWhiteSpace(exact) &&
                (string.Equals(exact.Trim(), title, StringComparison.OrdinalIgnoreCase) ||
                 title.IndexOf(exact.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)))
            {
                return true;
            }

            if (Matches.Any(match =>
                !string.IsNullOrWhiteSpace(match) &&
                match != "*" &&
                title.IndexOf(match.Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return true;
            }

            return Matches.Any(match => string.Equals(match, "*", StringComparison.Ordinal));
        }

        public AircraftLightStrategy GetLightStrategy()
        {
            var mode = (Lights?.Mode ?? string.Empty).Trim().ToLowerInvariant();
            switch (mode)
            {
                case "bitmask":
                    return AircraftLightStrategy.Bitmask;
                case "individual":
                    return AircraftLightStrategy.Individual;
                default:
                    return AircraftLightStrategy.Hybrid;
            }
        }
    }

    [DataContract]
    internal sealed class AircraftProfileLightsDefinition
    {
        [DataMember(Name = "mode")]
        public string Mode { get; set; } = "hybrid";
    }

    internal static class AircraftProfileCatalog
    {
        private static readonly AircraftProfileDefinition DefaultProfile = new AircraftProfileDefinition
        {
            Name = "MSFS Native",
            Matches = new List<string> { "*" },
            ExactTitles = new List<string>(),
            Lights = new AircraftProfileLightsDefinition { Mode = "hybrid" },
            Supported = true
        };

        private static readonly object Sync = new object();
        private static string _loadedFrom = string.Empty;
        private static DateTime _loadedAtUtc = DateTime.MinValue;
        private static List<AircraftProfileDefinition>? _cache;

        public static AircraftProfileDefinition Resolve(string baseDirectory, string aircraftTitle)
        {
            var profiles = LoadProfiles(baseDirectory);
            var title = aircraftTitle ?? string.Empty;

            var exact = profiles.FirstOrDefault(p =>
                p.ExactTitles.Any(ex =>
                    !string.IsNullOrWhiteSpace(ex) &&
                    (string.Equals(ex.Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase) ||
                     title.IndexOf(ex.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)));
            if (exact != null)
            {
                return exact;
            }

            var matched = profiles.FirstOrDefault(p => p.MatchesTitle(title));
            return matched ?? DefaultProfile;
        }

        private static List<AircraftProfileDefinition> LoadProfiles(string baseDirectory)
        {
            var jsonPath = Path.Combine(baseDirectory ?? string.Empty, "AircraftProfiles.json");
            var lastWrite = File.Exists(jsonPath) ? File.GetLastWriteTimeUtc(jsonPath) : DateTime.MinValue;

            lock (Sync)
            {
                if (_cache != null && string.Equals(_loadedFrom, jsonPath, StringComparison.OrdinalIgnoreCase) && _loadedAtUtc == lastWrite)
                {
                    return _cache;
                }

                if (!File.Exists(jsonPath))
                {
                    Debug.WriteLine($"[Profile] AircraftProfiles.json no encontrado en {jsonPath}");
                    _loadedFrom = jsonPath;
                    _loadedAtUtc = DateTime.MinValue;
                    _cache = new List<AircraftProfileDefinition> { DefaultProfile };
                    return _cache;
                }

                try
                {
                    using (var stream = File.OpenRead(jsonPath))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(AircraftProfilesDocument));
                        var document = serializer.ReadObject(stream) as AircraftProfilesDocument;
                        _cache = document?.Profiles?.Where(p => p != null).ToList() ?? new List<AircraftProfileDefinition>();
                    }

                    if (_cache.Count == 0)
                    {
                        _cache.Add(DefaultProfile);
                    }

                    _loadedFrom = jsonPath;
                    _loadedAtUtc = lastWrite;
                    Debug.WriteLine($"[Profile] Catálogo cargado: {_cache.Count} perfiles desde {jsonPath}");
                    return _cache;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Profile] Error cargando catálogo: {ex.Message}");
                    _loadedFrom = jsonPath;
                    _loadedAtUtc = lastWrite;
                    _cache = new List<AircraftProfileDefinition> { DefaultProfile };
                    return _cache;
                }
            }
        }
    }
}
