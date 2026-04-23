using System.Collections.Generic;

namespace PatagoniaWings.Acars.Core.Models
{
    public sealed class AircraftProfile
    {
        public string Code { get; set; } = "MSFS_NATIVE";
        public string DisplayName { get; set; } = "MSFS Native";
        public string FamilyGroup { get; set; } = "GENERIC";
        public string Simulator { get; set; } = "MSFS2020";
        public string AddonProvider { get; set; } = "Asobo";
        public int EngineCount { get; set; } = 1;
        public bool IsPressurized { get; set; } = false;
        public bool HasApu { get; set; } = false;
        public string ImageAsset { get; set; } = "default_aircraft.png";
        public bool Supported { get; set; } = true;
        public string PrimaryTelemetrySource { get; set; } = string.Empty;
        public List<string> TelemetrySourcePriority { get; set; } = new List<string>();
        public string CapabilityAuditState { get; set; } = "PARCIAL";

        public List<string> ExactTitles { get; set; } = new List<string>();
        public List<string> Matches { get; set; } = new List<string>();

        public string LightMode { get; set; } = "individual";
        public bool RequiresLvars { get; set; } = false;
        public string LvarProfile { get; set; } = string.Empty;
        public string N1Source { get; set; } = "turb_n1";

        public string DoorSource { get; set; } = "exit_open_0";
        public double DoorOpenThresholdPercent { get; set; } = 5.0;

        public string SeatbeltSource { get; set; } = "native";
        public int SeatbeltDebounceFrames { get; set; } = 1;

        public string NoSmokingSource { get; set; } = "native";

        public string AutopilotSource { get; set; } = "native";
        public int AutopilotDebounceFrames { get; set; } = 1;

        public string TransponderStateSource { get; set; } = "native";
        public int TransponderStateDebounceFrames { get; set; } = 1;
        public int TransponderDefaultState { get; set; } = 1;

        public string TransponderCodeFormat { get; set; } = "decimal_or_bco16";
        public int TransponderCodeDebounceFrames { get; set; } = 1;

        public string ApuSource { get; set; } = "native";
        public string BleedAirSource { get; set; } = "native";
        public string FuelPumpSource { get; set; } = string.Empty;
        public string ContinuousIgnitionSource { get; set; } = string.Empty;
        public string FireTestSource { get; set; } = string.Empty;
        public string InertialSeparatorSource { get; set; } = string.Empty;

        public bool PreferFsuipcAutopilot { get; set; } = false;
        public bool PreferFsuipcTransponder { get; set; } = false;

        // Expresiones opcionales listas para MobiFlight / Calculator code
        public string MobiFlightSeatbeltExpression { get; set; } = string.Empty;
        public string MobiFlightNoSmokingExpression { get; set; } = string.Empty;
        public string MobiFlightApuExpression { get; set; } = string.Empty;
        public string MobiFlightAutopilotExpression { get; set; } = string.Empty;
        public string MobiFlightBleedAirExpression { get; set; } = string.Empty;
        public string MobiFlightInertialSeparatorExpression { get; set; } = string.Empty;
        public List<string> MobiFlightDoorExpressions { get; set; } = new List<string>();

        /// <summary>
        /// false = el addon usa sistemas propios; no penalizar si la lectura nativa no es fiable.
        /// </summary>
        public bool CabinSystemsReliable { get; set; } = true;

        public bool SupportsFuelRead { get; set; } = true;
        public bool SupportsPayloadRead { get; set; } = false;
        public bool SupportsFlagsRead { get; set; } = true;
        public bool SupportsParkingBrakeRead { get; set; } = true;
        public bool SupportsApuRead { get; set; } = false;
        public bool SupportsLightsRead { get; set; } = true;
        public bool SupportsGearRead { get; set; } = true;
        public bool SupportsDoorRead { get; set; } = false;
        public bool SupportsBatteryRead { get; set; } = false;
        public bool SupportsAvionicsRead { get; set; } = false;
        public bool SupportsEngineRunRead { get; set; } = false;
        public bool SupportsZfwReadback { get; set; } = false;
        public bool SupportsQnhReadback { get; set; } = false;
        public bool SupportsTransponderModeReadback { get; set; } = true;
        public bool SupportsSquawkReadback { get; set; } = true;
        public bool SupportsPushbackInference { get; set; } = true;
        public bool SupportsFuelPumpReadback { get; set; } = false;
        public bool SupportsContinuousIgnitionReadback { get; set; } = false;
        public bool SupportsFireTestReadback { get; set; } = false;
        public bool HasInertialSeparator { get; set; } = false;
        public bool SupportsInertialSeparatorReadback { get; set; } = false;

        private static bool IsBridgeLike(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            source = source.Trim().ToLowerInvariant();

            // OJO:
            // "bridge" describe una intención/configuración por perfil,
            // pero NO implica que exista una lectura viva implementada.
            // Solo tratamos como overlay real lo que venga por LVAR/MobiFlight.
            return source == "lvar"
                || source == "mobiflight";
        }

        private static bool IsExplicitlyUnsupported(string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return false;
            source = source.Trim().ToLowerInvariant();
            return source == "none"
                || source == "unsupported"
                || source == "n/a"
                || source == "na";
        }

        public bool UsesLvarSeatbelt => IsBridgeLike(SeatbeltSource) || !string.IsNullOrWhiteSpace(MobiFlightSeatbeltExpression);
        public bool UsesLvarNoSmoking => IsBridgeLike(NoSmokingSource) || !string.IsNullOrWhiteSpace(MobiFlightNoSmokingExpression);
        public bool UsesLvarDoor => IsBridgeLike(DoorSource) || (MobiFlightDoorExpressions != null && MobiFlightDoorExpressions.Count > 0);
        public bool UsesLvarAutopilot => IsBridgeLike(AutopilotSource) || !string.IsNullOrWhiteSpace(MobiFlightAutopilotExpression);
        public bool UsesLvarApu => IsBridgeLike(ApuSource) || !string.IsNullOrWhiteSpace(MobiFlightApuExpression);
        public bool UsesLvarBleedAir => IsBridgeLike(BleedAirSource) || !string.IsNullOrWhiteSpace(MobiFlightBleedAirExpression);
        public bool UsesLvarInertialSeparator => IsBridgeLike(InertialSeparatorSource) || !string.IsNullOrWhiteSpace(MobiFlightInertialSeparatorExpression);
        public bool SupportsDoorSystem => !IsExplicitlyUnsupported(DoorSource) && (SupportsDoorRead || UsesLvarDoor);
        public bool SupportsSeatbeltSystem => !IsExplicitlyUnsupported(SeatbeltSource) && (SupportsFlagsRead || UsesLvarSeatbelt);
        public bool SupportsNoSmokingSystem => !IsExplicitlyUnsupported(NoSmokingSource) && (SupportsFlagsRead || UsesLvarNoSmoking);
        public bool SupportsApuSystem => HasApu && !IsExplicitlyUnsupported(ApuSource) && (SupportsApuRead || UsesLvarApu);
        public bool SupportsBleedAirSystem => IsPressurized && !IsExplicitlyUnsupported(BleedAirSource);
        public bool SupportsTransponderModeSystem => SupportsTransponderModeReadback && !IsExplicitlyUnsupported(TransponderStateSource);
        public bool SupportsSquawkSystem => SupportsSquawkReadback && !IsExplicitlyUnsupported(TransponderStateSource);
        public bool SupportsInertialSeparatorSystem => HasInertialSeparator && !IsExplicitlyUnsupported(InertialSeparatorSource) && (SupportsInertialSeparatorReadback || UsesLvarInertialSeparator);
    }
}
