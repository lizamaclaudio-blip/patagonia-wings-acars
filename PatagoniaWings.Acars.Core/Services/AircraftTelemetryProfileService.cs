using System;
using System.Collections.Generic;
using System.Linq;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    /// <summary>
    /// Traduce el perfil de aeronave a capacidades reales de telemetria.
    /// El reglaje consulta esta capa antes de evaluar para evitar falsos FAIL
    /// cuando un addon no expone una variable de forma confiable.
    /// </summary>
    public static class AircraftTelemetryProfileService
    {
        private sealed class CapabilityDescriptor
        {
            public string Code { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
            public string ExpectedSource { get; set; } = string.Empty;
            public string[] MetricKeys { get; set; } = Array.Empty<string>();
            public Func<AircraftProfile, bool> IsApplicable { get; set; } = _ => true;
            public Func<AircraftProfile, bool> IsSupported { get; set; } = _ => false;
            public Func<AircraftProfile, string> ImplementedSource { get; set; } = _ => string.Empty;
            public Func<AircraftProfile, string> Observation { get; set; } = _ => string.Empty;
        }

        private const string CapabilityStatusSupported = "SUPPORTED";
        private const string CapabilityStatusPartial = "PARTIAL";
        private const string CapabilityStatusNotApplicable = "N_A";
        private const string CapabilityStatusExcludedByProfile = "EXCLUDED_BY_PROFILE";

        private static readonly CapabilityDescriptor[] CapabilityDescriptors =
        {
            Create("light_nav", "NAV", "SimVar/SimConnect", new[] { "telemetry.last.nav_on" },
                profile => profile.SupportsLightsRead,
                profile => profile.SupportsLightsRead ? "SimVar/SimConnect" : "Excluded",
                profile => profile.SupportsLightsRead ? "Lectura base nativa" : "Sin lectura fiable de luces"),
            Create("light_beacon", "BEACON", "SimVar/SimConnect", new[] { "telemetry.last.beacon_on" },
                profile => profile.SupportsLightsRead,
                profile => profile.SupportsLightsRead ? "SimVar/SimConnect" : "Excluded",
                profile => profile.SupportsLightsRead ? "Lectura base nativa" : "Sin lectura fiable de luces"),
            Create("light_taxi", "TAXI", "SimVar/SimConnect", new[] { "telemetry.last.taxi_on" },
                profile => profile.SupportsLightsRead,
                profile => profile.SupportsLightsRead ? "SimVar/SimConnect" : "Excluded",
                profile => profile.SupportsLightsRead ? "Lectura base nativa" : "Sin lectura fiable de luces"),
            Create("light_landing", "LANDING", "SimVar/SimConnect", new[] { "telemetry.last.landing_on" },
                profile => profile.SupportsLightsRead,
                profile => profile.SupportsLightsRead ? "SimVar/SimConnect" : "Excluded",
                profile => profile.SupportsLightsRead ? "Lectura base nativa" : "Sin lectura fiable de luces"),
            Create("light_strobe", "STROBE", "SimVar/SimConnect", new[] { "telemetry.last.strobe_on" },
                profile => profile.SupportsLightsRead,
                profile => profile.SupportsLightsRead ? "SimVar/SimConnect" : "Excluded",
                profile => profile.SupportsLightsRead ? "Lectura base nativa" : "Sin lectura fiable de luces"),
            Create("parking_brake", "Parking Brake", "SimVar/SimConnect", new[] { "telemetry.last.parking_brake_on", "preflight.parking_brake_on" },
                profile => profile.SupportsParkingBrakeRead,
                profile => profile.SupportsParkingBrakeRead ? "SimVar/SimConnect" : "Excluded",
                profile => profile.SupportsParkingBrakeRead ? "Gate de inicio habilitado" : "Sin lectura fiable de parking brake"),
            Create("on_ground", "Weight On Wheels", "SimVar/SimConnect", new[] { "telemetry.last.on_ground" },
                _ => true,
                _ => "SimVar/SimConnect",
                _ => "Base para fases tierra/vuelo"),
            Create("pushback", "Pushback Inference", "Posicion/GS/heading", Array.Empty<string>(),
                profile => profile.SupportsPushbackInference,
                _ => "Inferido por trayectoria",
                profile => profile.SupportsPushbackInference ? "Listo para reglas futuras" : "Inferencia deshabilitada para este perfil"),
            Create("gear", "Gear", "SimVar/SimConnect", new[] { "telemetry.last.gear_down" },
                profile => profile.SupportsGearRead,
                profile => profile.SupportsGearRead ? "SimVar/SimConnect" : "Excluded",
                profile => profile.SupportsGearRead ? "Lectura de tren disponible" : "Sin lectura fiable de tren"),
            Create("flaps", "Flaps", "SimVar/SimConnect", new[] { "telemetry.last.flaps_percent", "telemetry.last.flaps_deployed" },
                profile => profile.SupportsGearRead,
                profile => profile.SupportsGearRead ? "SimVar/SimConnect" : "Excluded",
                profile => profile.SupportsGearRead ? "Lectura de configuracion disponible" : "Sin lectura fiable de flaps"),
            Create("engines", "Engine Running", "N1/Combustion", new[] { "preflight.engines_off", "telemetry.last.engine1_n1", "telemetry.last.engine2_n1", "telemetry.last.engine3_n1", "telemetry.last.engine4_n1" },
                profile => profile.SupportsEngineRunRead || !string.IsNullOrWhiteSpace(profile.N1Source),
                profile => DescribeEngineSource(profile),
                profile => profile.SupportsEngineRunRead ? "Running + N1 disponibles" : "Se usa proxy N1"),
            Create("autopilot", "Autopilot", "SimVar/FSUIPC/MobiFlight", new[] { "telemetry.last.autopilot_on" },
                profile => !string.IsNullOrWhiteSpace(profile.AutopilotSource),
                profile => ResolveSource(profile.PreferFsuipcAutopilot ? "fsuipc" : profile.AutopilotSource),
                profile => profile.PreferFsuipcAutopilot ? "Overlay FSUIPC priorizado" : "Lectura segun perfil"),
            Create("apu", "APU", "SimVar/LVAR/MobiFlight", new[] { "telemetry.last.apu_on" },
                profile => SupportsApuEvaluation(profile),
                profile => ResolveSource(profile.ApuSource),
                profile => DescribeObservation(profile, "apu")),
            Create("bleed", "Bleed / Packs", "SimVar/LVAR/MobiFlight", new[] { "telemetry.last.bleed_on" },
                profile => SupportsBleedEvaluation(profile),
                profile => ResolveSource(profile.BleedAirSource),
                profile => DescribeObservation(profile, "bleed")),
            Create("seatbelt", "Seatbelts", "SimVar/LVAR/MobiFlight", new[] { "telemetry.last.seatbelt_on" },
                profile => SupportsSeatbeltEvaluation(profile),
                profile => ResolveSource(profile.SeatbeltSource),
                profile => DescribeObservation(profile, "seatbelt")),
            Create("no_smoking", "No Smoking", "SimVar/LVAR/MobiFlight", new[] { "telemetry.last.no_smoking_on" },
                profile => SupportsNoSmokingEvaluation(profile),
                profile => ResolveSource(profile.NoSmokingSource),
                profile => DescribeObservation(profile, "no_smoking")),
            Create("transponder_mode", "Transponder Mode", "SimVar/FSUIPC", new[] { "telemetry.last.transponder_charlie" },
                profile => profile.SupportsTransponderModeSystem,
                profile => ResolveSource(profile.PreferFsuipcTransponder ? "fsuipc" : profile.TransponderStateSource),
                profile => profile.SupportsTransponderModeSystem ? "Lectura de estado XPDR disponible" : "Sin lectura fiable de modo XPDR"),
            Create("squawk", "Squawk", "SimVar/FSUIPC", new[] { "telemetry.last.transponder_code" },
                profile => profile.SupportsSquawkSystem,
                profile => ResolveSource(profile.PreferFsuipcTransponder ? "fsuipc" : profile.TransponderStateSource),
                profile => profile.SupportsSquawkSystem ? "Lectura de codigo disponible" : "Sin lectura fiable de squawk"),
            Create("qnh", "Altimeter / QNH", "Aircraft-specific altimeter", new[] { "telemetry.last.qnh_hpa" },
                profile => profile.SupportsQnhReadback,
                profile => profile.SupportsQnhReadback ? "SimConnect/SeaLevelPressure" : "Excluded",
                profile => profile.SupportsQnhReadback ? "Lectura QNH disponible por SimConnect" : "QNH excluido por perfil"),
            Create("fuel_total", "Fuel Total", "SimVar/SimConnect", new[] { "telemetry.last.fuel_kg", "telemetry.first.fuel_kg" },
                profile => profile.SupportsFuelRead,
                profile => profile.SupportsFuelRead ? "SimVar/SimConnect" : "Excluded",
                profile => profile.SupportsFuelRead ? "Combustible usable para score" : "Sin lectura fiable de combustible"),
            Create("payload", "Payload", "SimConnect/Derived", new[] { "telemetry.last.payload_kg", "telemetry.last.total_weight_kg", "telemetry.last.empty_weight_kg" },
                profile => profile.SupportsPayloadRead,
                profile => profile.SupportsPayloadRead ? "SimConnect/Derived" : "Excluded",
                profile => profile.SupportsPayloadRead ? "Payload derivado desde peso total, vacio y combustible" : "Payload excluido por perfil"),
            Create("zfw", "Zero Fuel Weight", "SimConnect/Derived", new[] { "telemetry.last.zero_fuel_weight_kg" },
                profile => profile.SupportsZfwReadback,
                profile => profile.SupportsZfwReadback ? "SimConnect/Derived" : "Excluded",
                profile => profile.SupportsZfwReadback ? "ZFW derivado desde peso total y combustible" : "ZFW excluido por perfil"),
            Create("fuel_pumps", "Fuel Pumps", "Aircraft/Addon specific", new[] { "telemetry.last.fuel_pumps_on" },
                profile => profile.SupportsFuelPumpReadback,
                profile => ResolveAdvancedSource(profile.FuelPumpSource, profile.SupportsFuelPumpReadback, "Aircraft/Addon specific"),
                profile => profile.SupportsFuelPumpReadback ? "Fuel pumps listos por perfil" : "Fuel pumps excluidos por perfil"),
            Create("doors", "Operable Doors", new[] { "telemetry.last.door_open" },
                profile => SupportsDoorEvaluation(profile),
                profile => ResolveSource(profile.DoorSource),
                profile => DescribeObservation(profile, "doors")),
            Create("battery", "Battery Master", new[] { "telemetry.last.battery_on" },
                profile => profile.SupportsBatteryRead,
                profile => profile.SupportsBatteryRead ? "SimVar/SimConnect" : "Excluded",
                profile => profile.SupportsBatteryRead ? "Cold and dark usa lectura real" : "Battery fuera de evaluacion"),
            Create("avionics", "Avionics Master", new[] { "telemetry.last.avionics_on" },
                profile => profile.SupportsAvionicsRead,
                profile => profile.SupportsAvionicsRead ? "SimVar/SimConnect" : "Excluded",
                profile => profile.SupportsAvionicsRead ? "Cold and dark usa lectura real" : "Avionics fuera de evaluacion"),
            Create("continuous_ignition", "Continuous Ignition", "Aircraft/Addon specific", new[] { "telemetry.last.continuous_ignition_on" },
                profile => profile.SupportsContinuousIgnitionReadback,
                profile => ResolveAdvancedSource(profile.ContinuousIgnitionSource, profile.SupportsContinuousIgnitionReadback, "Aircraft/Addon specific"),
                profile => profile.SupportsContinuousIgnitionReadback ? "Ignicion continua lista por perfil" : "Ignicion continua excluida por perfil"),
            Create("fire_test", "Fire Test", "Aircraft/Addon specific", new[] { "telemetry.last.fire_test_on" },
                profile => profile.SupportsFireTestReadback,
                profile => ResolveAdvancedSource(profile.FireTestSource, profile.SupportsFireTestReadback, "Aircraft/Addon specific"),
                profile => profile.SupportsFireTestReadback ? "Fire test listo por perfil" : "Fire test excluido por perfil"),
            Create("inertial_separator", "Inertial Separator", "LVAR/MobiFlight or addon specific", new[] { "telemetry.last.inertial_separator_on" },
                profile => profile.SupportsInertialSeparatorSystem,
                profile => ResolveAdvancedSource(profile.InertialSeparatorSource, profile.SupportsInertialSeparatorSystem, "Aircraft/Addon specific"),
                profile => DescribeObservation(profile, "inertial_separator"))
        };

        private static readonly Dictionary<string, CapabilityDescriptor> DescriptorByMetric =
            CapabilityDescriptors
                .SelectMany(descriptor => descriptor.MetricKeys.Select(metric => new KeyValuePair<string, CapabilityDescriptor>(metric, descriptor)))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        public static AircraftProfile ResolveProfile(PatagoniaEvaluationInput input)
        {
            var profileCode = FirstNonEmpty(
                input == null || input.CurrentTelemetry == null ? string.Empty : input.CurrentTelemetry.DetectedProfileCode,
                input == null || input.TelemetryLog == null || input.TelemetryLog.Count == 0
                    ? string.Empty
                    : input.TelemetryLog[input.TelemetryLog.Count - 1].DetectedProfileCode);

            if (!string.IsNullOrWhiteSpace(profileCode))
            {
                return AircraftNormalizationService.GetProfile(profileCode);
            }

            var title = FirstNonEmpty(
                input == null || input.CurrentTelemetry == null ? string.Empty : input.CurrentTelemetry.AircraftTitle,
                input == null || input.TelemetryLog == null || input.TelemetryLog.Count == 0
                    ? string.Empty
                    : input.TelemetryLog[input.TelemetryLog.Count - 1].AircraftTitle,
                input == null || input.Flight == null ? string.Empty : input.Flight.AircraftDisplayName,
                input == null || input.Flight == null ? string.Empty : input.Flight.AircraftName);

            return AircraftNormalizationService.ResolveProfile(title);
        }

        public static PatagoniaAircraftValidationResult BuildValidation(AircraftProfile profile)
        {
            profile = profile ?? AircraftNormalizationService.GetProfile("MSFS_NATIVE");
            var matrix = CapabilityDescriptors.Select(descriptor => BuildMatrixEntry(profile, descriptor)).ToList();

            return new PatagoniaAircraftValidationResult
            {
                DetectedProfileCode = profile.Code,
                FamilyGroup = profile.FamilyGroup,
                AddonProvider = profile.AddonProvider,
                PrimaryTelemetrySource = ResolvePrimarySource(profile),
                CapabilityAuditState = ResolveOverallState(profile, matrix),
                TelemetrySources = ResolveTelemetrySources(profile),
                SupportedSystems = matrix.Where(item => string.Equals(item.Status, CapabilityStatusSupported, StringComparison.OrdinalIgnoreCase)).Select(item => item.DisplayName).ToList(),
                PartialSystems = matrix.Where(item => string.Equals(item.Status, CapabilityStatusPartial, StringComparison.OrdinalIgnoreCase)).Select(item => item.DisplayName).ToList(),
                UnsupportedSystems = matrix.Where(item => !item.Evaluate).Select(item => item.DisplayName).ToList(),
                NotApplicableSystems = matrix.Where(item => string.Equals(item.Status, CapabilityStatusNotApplicable, StringComparison.OrdinalIgnoreCase)).Select(item => item.DisplayName).ToList(),
                ExcludedByProfileSystems = matrix.Where(item => string.Equals(item.Status, CapabilityStatusExcludedByProfile, StringComparison.OrdinalIgnoreCase)).Select(item => item.DisplayName).ToList(),
                ExtraSourceSystems = matrix
                    .Where(item => (string.Equals(item.Status, CapabilityStatusSupported, StringComparison.OrdinalIgnoreCase) || string.Equals(item.Status, CapabilityStatusPartial, StringComparison.OrdinalIgnoreCase))
                                   && RequiresExtraSource(item.ImplementedSource))
                    .Select(item => item.DisplayName + " -> " + item.ImplementedSource)
                    .ToList(),
                CapabilityMatrix = matrix
            };
        }

        public static bool SupportsMetric(AircraftProfile profile, string metricKey)
        {
            if (string.IsNullOrWhiteSpace(metricKey))
            {
                return false;
            }

            CapabilityDescriptor descriptor;
            if (!DescriptorByMetric.TryGetValue(metricKey.Trim(), out descriptor))
            {
                return true;
            }

            return descriptor.IsSupported(profile ?? AircraftNormalizationService.GetProfile("MSFS_NATIVE"));
        }

        public static List<string> GetUnsupportedMetrics(AircraftProfile profile, IEnumerable<string> metricKeys)
        {
            var result = new List<string>();
            if (metricKeys == null)
            {
                return result;
            }

            foreach (var metricKey in metricKeys.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                if (!SupportsMetric(profile, metricKey))
                {
                    result.Add(metricKey.Trim());
                }
            }

            return result;
        }

        public static string DescribeUnsupportedMetrics(AircraftProfile profile, IEnumerable<string> metricKeys)
        {
            var unsupported = GetUnsupportedMetrics(profile, metricKeys);
            if (unsupported.Count == 0)
            {
                return string.Empty;
            }

            var grouped = unsupported
                .Select(metricKey =>
                {
                    CapabilityDescriptor descriptor;
                    return DescriptorByMetric.TryGetValue(metricKey, out descriptor)
                        ? descriptor.DisplayName
                        : metricKey;
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return "Telemetry excluded for profile " + (profile == null ? "MSFS_NATIVE" : profile.Code) + ": " + string.Join(", ", grouped);
        }

        public static string ResolvePrimarySource(AircraftProfile profile)
        {
            profile = profile ?? AircraftNormalizationService.GetProfile("MSFS_NATIVE");

            if (!string.IsNullOrWhiteSpace(profile.PrimaryTelemetrySource))
            {
                return profile.PrimaryTelemetrySource;
            }

            var sources = ResolveTelemetrySources(profile);
            return sources.Count == 0 ? "SimVar" : string.Join(" + ", sources);
        }

        public static List<string> ResolveTelemetrySources(AircraftProfile profile)
        {
            profile = profile ?? AircraftNormalizationService.GetProfile("MSFS_NATIVE");
            var sources = new List<string>();

            foreach (var configured in profile.TelemetrySourcePriority ?? new List<string>())
            {
                AddDistinctSource(sources, configured);
            }

            AddDistinctSource(sources, "SimVar");

            if (profile.RequiresLvars
                || profile.UsesLvarSeatbelt
                || profile.UsesLvarNoSmoking
                || profile.UsesLvarDoor
                || profile.UsesLvarApu
                || profile.UsesLvarBleedAir)
            {
                AddDistinctSource(sources, "MobiFlight WASM");
            }

            if (profile.PreferFsuipcAutopilot
                || profile.PreferFsuipcTransponder
                || string.Equals(profile.AutopilotSource, "fsuipc", StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile.TransponderStateSource, "fsuipc", StringComparison.OrdinalIgnoreCase))
            {
                AddDistinctSource(sources, "FSUIPC");
            }

            return sources;
        }

        public static bool IsColdAndDark(SimData sample, AircraftProfile profile)
        {
            if (sample == null)
            {
                return false;
            }

            if (!EnginesStopped(sample, profile))
            {
                return false;
            }

            if (profile.SupportsLightsRead)
            {
                if (sample.NavLightsOn || sample.BeaconLightsOn || sample.StrobeLightsOn || sample.LandingLightsOn || sample.TaxiLightsOn)
                {
                    return false;
                }
            }

            if (SupportsApuEvaluation(profile) && sample.ApuRunning)
            {
                return false;
            }

            if (SupportsBleedEvaluation(profile) && sample.BleedAirOn)
            {
                return false;
            }

            if (profile.SupportsBatteryRead && sample.BatteryMasterOn)
            {
                return false;
            }

            if (profile.SupportsAvionicsRead && sample.AvionicsMasterOn)
            {
                return false;
            }

            return true;
        }

        public static bool EnginesStopped(SimData sample, AircraftProfile profile)
        {
            if (sample == null)
            {
                return true;
            }

            if (profile != null && profile.SupportsEngineRunRead)
            {
                return !sample.EngineOneRunning
                    && !sample.EngineTwoRunning
                    && !sample.EngineThreeRunning
                    && !sample.EngineFourRunning;
            }

            return sample.Engine1N1 < 5
                   && sample.Engine2N1 < 5
                   && sample.Engine3N1 < 5
                   && sample.Engine4N1 < 5;
        }

        private static CapabilityDescriptor Create(
            string code,
            string displayName,
            string expectedSource,
            string[] metricKeys,
            Func<AircraftProfile, bool> isSupported,
            Func<AircraftProfile, string> implementedSource,
            Func<AircraftProfile, string> observation)
        {
            return new CapabilityDescriptor
            {
                Code = code,
                DisplayName = displayName,
                ExpectedSource = expectedSource,
                MetricKeys = metricKeys ?? Array.Empty<string>(),
                IsSupported = isSupported,
                ImplementedSource = implementedSource,
                Observation = observation
            };
        }

        private static CapabilityDescriptor Create(
            string code,
            string displayName,
            string[] metricKeys,
            Func<AircraftProfile, bool> isSupported,
            Func<AircraftProfile, string> implementedSource,
            Func<AircraftProfile, string> observation)
        {
            return Create(code, displayName, displayName, metricKeys, isSupported, implementedSource, observation);
        }

        private static PatagoniaAircraftCapabilityMatrixEntry BuildMatrixEntry(AircraftProfile profile, CapabilityDescriptor descriptor)
        {
            var applicable = IsCapabilityApplicable(profile, descriptor);
            var supported = applicable && descriptor.IsSupported(profile);
            var implementedSource = descriptor.ImplementedSource(profile);
            var status = ResolveCapabilityStatus(applicable, supported, implementedSource);

            return new PatagoniaAircraftCapabilityMatrixEntry
            {
                CapabilityCode = descriptor.Code,
                DisplayName = descriptor.DisplayName,
                ExpectedSource = descriptor.ExpectedSource,
                ImplementedSource = implementedSource,
                Status = status,
                Evaluate = supported,
                Observation = descriptor.Observation(profile)
            };
        }

        private static bool IsCapabilityApplicable(AircraftProfile profile, CapabilityDescriptor descriptor)
        {
            if (profile == null || descriptor == null)
            {
                return false;
            }

            switch ((descriptor.Code ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "apu":
                    return profile.HasApu;
                case "bleed":
                    return profile.IsPressurized && !IsExplicitlyExcluded(profile.BleedAirSource);
                case "seatbelt":
                    return !IsBe58Profile(profile);
                case "no_smoking":
                    return !IsBe58Profile(profile);
                case "inertial_separator":
                    return profile.HasInertialSeparator;
                default:
                    return true;
            }
        }

        private static string ResolveCapabilityStatus(bool applicable, bool supported, string implementedSource)
        {
            if (!applicable)
            {
                return CapabilityStatusNotApplicable;
            }

            if (!supported)
            {
                return CapabilityStatusExcludedByProfile;
            }

            return RequiresExtraSource(implementedSource)
                ? CapabilityStatusPartial
                : CapabilityStatusSupported;
        }

        private static bool SupportsSeatbeltEvaluation(AircraftProfile profile)
        {
            if (profile == null) return false;
            if (IsBe58Profile(profile)) return false;
            if (IsMaddogProfile(profile)) return false;
            return profile.SupportsSeatbeltSystem;
        }

        private static bool SupportsNoSmokingEvaluation(AircraftProfile profile)
        {
            if (profile == null) return false;
            if (IsBe58Profile(profile)) return false;
            if (IsMaddogProfile(profile)) return false;
            return profile.SupportsNoSmokingSystem;
        }

        private static bool SupportsApuEvaluation(AircraftProfile profile)
        {
            if (profile == null) return false;
            if (IsMaddogProfile(profile)) return false;
            return profile.SupportsApuSystem;
        }

        private static bool SupportsBleedEvaluation(AircraftProfile profile)
        {
            if (profile == null) return false;
            if (IsMaddogProfile(profile)) return false;
            return profile.SupportsBleedAirSystem;
        }

        private static bool SupportsDoorEvaluation(AircraftProfile profile)
        {
            if (profile == null) return false;
            if (IsMaddogProfile(profile)) return false;
            return profile.SupportsDoorSystem;
        }

        private static string ResolveOverallState(AircraftProfile profile, IEnumerable<PatagoniaAircraftCapabilityMatrixEntry> matrix)
        {
            var entries = (matrix ?? Enumerable.Empty<PatagoniaAircraftCapabilityMatrixEntry>()).ToList();
            if (!string.IsNullOrWhiteSpace(profile.CapabilityAuditState) &&
                !string.Equals(profile.CapabilityAuditState, "PARCIAL", StringComparison.OrdinalIgnoreCase))
            {
                return profile.CapabilityAuditState.Trim().ToUpperInvariant();
            }

            var criticalCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "light_nav",
                "light_beacon",
                "light_taxi",
                "light_landing",
                "light_strobe",
                "parking_brake",
                "on_ground",
                "gear",
                "flaps",
                "engines",
                "transponder_mode",
                "squawk",
                "fuel_total"
            };

            var criticalOk = entries
                .Where(item => criticalCodes.Contains(item.CapabilityCode))
                .All(item => item.Evaluate);

            var supportedCount = entries.Count(item => item.Evaluate);
            var advancedProcedureCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "apu",
                "bleed",
                "seatbelt",
                "no_smoking",
                "doors"
            };
            var advancedGaps = entries.Any(item =>
                advancedProcedureCodes.Contains(item.CapabilityCode)
                && string.Equals(item.Status, CapabilityStatusExcludedByProfile, StringComparison.OrdinalIgnoreCase));

            if (criticalOk && supportedCount >= 12 && !advancedGaps)
            {
                return "OK";
            }

            if (criticalOk)
            {
                return "PARCIAL";
            }

            return "LIMITADO";
        }

        private static string DescribeEngineSource(AircraftProfile profile)
        {
            if (profile == null) return "Excluded";
            if (profile.SupportsEngineRunRead) return "Combustion + N1";
            if (!string.IsNullOrWhiteSpace(profile.N1Source)) return "N1 proxy (" + profile.N1Source + ")";
            return "Excluded";
        }

        private static string DescribeObservation(AircraftProfile profile, string capabilityCode)
        {
            if (profile == null)
            {
                return "Sin perfil";
            }

            if (IsMaddogProfile(profile) &&
                (string.Equals(capabilityCode, "apu", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(capabilityCode, "bleed", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(capabilityCode, "seatbelt", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(capabilityCode, "no_smoking", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(capabilityCode, "doors", StringComparison.OrdinalIgnoreCase)))
            {
                return "Maddog queda fuera de evaluacion hasta validar puente LVAR/WASM en runtime";
            }

            if (string.Equals(capabilityCode, "inertial_separator", StringComparison.OrdinalIgnoreCase))
            {
                if (!profile.HasInertialSeparator)
                {
                    return "El perfil no usa separador de inercia";
                }

                return profile.SupportsInertialSeparatorSystem
                    ? "Lectura LVAR/MobiFlight activa para el separador"
                    : "Separador presente pero excluido por perfil hasta validar runtime";
            }

            if (IsBe58Profile(profile) &&
                (string.Equals(capabilityCode, "seatbelt", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(capabilityCode, "no_smoking", StringComparison.OrdinalIgnoreCase)))
            {
                return "La familia Baron no expone signs operativos para este reglaje";
            }

            var implementedSource = capabilityCode == "seatbelt"
                ? ResolveSource(profile.SeatbeltSource)
                : capabilityCode == "no_smoking"
                    ? ResolveSource(profile.NoSmokingSource)
                    : capabilityCode == "apu"
                        ? ResolveSource(profile.ApuSource)
                        : capabilityCode == "bleed"
                            ? ResolveSource(profile.BleedAirSource)
                            : capabilityCode == "doors"
                                ? ResolveSource(profile.DoorSource)
                                : string.Empty;

            if (RequiresExtraSource(implementedSource))
            {
                return "Evaluacion condicionada a overlay " + implementedSource;
            }

            if (string.Equals(implementedSource, "Excluded", StringComparison.OrdinalIgnoreCase))
            {
                return "Excluido por perfil";
            }

            return "Listo para evaluacion";
        }

        private static bool RequiresExtraSource(string implementedSource)
        {
            var source = (implementedSource ?? string.Empty).Trim();
            return source.IndexOf("FSUIPC", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("MobiFlight", StringComparison.OrdinalIgnoreCase) >= 0
                || source.IndexOf("LVAR", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddDistinctSource(List<string> sources, string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return;
            }

            if (!sources.Any(item => string.Equals(item, source.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                sources.Add(source.Trim());
            }
        }

        private static string ResolveSource(string rawSource)
        {
            var source = (rawSource ?? string.Empty).Trim().ToLowerInvariant();
            switch (source)
            {
                case "":
                case "native":
                case "mode_based":
                case "native_or_derived":
                    return "SimVar/SimConnect";
                case "fsuipc":
                    return "FSUIPC";
                case "bridge":
                    return "Bridge (no evaluable)";
                case "mobiflight":
                case "lvar":
                case "pmdg_737_custom":
                    return "MobiFlight/LVAR";
                case "none":
                case "unsupported":
                case "n/a":
                case "na":
                    return "Excluded";
                default:
                    return rawSource;
            }
        }

        private static string ResolveAdvancedSource(string rawSource, bool supported, string fallbackSource)
        {
            if (!string.IsNullOrWhiteSpace(rawSource))
            {
                return ResolveSource(rawSource);
            }

            return supported ? fallbackSource : "Excluded";
        }

        private static bool IsExplicitlyExcluded(string rawSource)
        {
            var source = (rawSource ?? string.Empty).Trim().ToLowerInvariant();
            return source == "none"
                || source == "unsupported"
                || source == "n/a"
                || source == "na";
        }

        private static bool IsMaddogProfile(AircraftProfile profile)
        {
            if (profile == null) return false;
            return string.Equals(profile.Code, "MD82_MADDOG", StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile.Code, "MD83_MADDOG", StringComparison.OrdinalIgnoreCase)
                || string.Equals(profile.Code, "MD88_MADDOG", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsBe58Profile(AircraftProfile profile)
        {
            if (profile == null) return false;
            return string.Equals(profile.FamilyGroup, "BE58_FAMILY", StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }
    }
}
