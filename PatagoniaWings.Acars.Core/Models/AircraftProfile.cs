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

        /// <summary>
        /// false = el addon usa LVARs propios; seatbelt/transponder/AP no son confiables por SimConnect.
        /// Cuando es false, FlightEvaluationService no penaliza esos sistemas.
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
    }
}
