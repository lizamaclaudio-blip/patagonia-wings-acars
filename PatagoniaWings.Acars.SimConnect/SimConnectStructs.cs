#nullable enable
using System.Runtime.InteropServices;

namespace PatagoniaWings.Acars.SimConnect
{
    internal enum DataDefineId
    {
        AircraftData    = 1,
        EnvironmentData = 2
    }

    internal enum RequestId
    {
        AircraftData    = 1,
        EnvironmentData = 2
    }

    internal enum EventId
    {
        Pause   = 1,
        Crashed = 2
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    internal struct AircraftDataStruct
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Title;

        public double AltitudeFeet;
        public double GroundSpeed;
        public double VerticalSpeed;
        public double Latitude;
        public double Longitude;
        public double Heading;

        public double OnGround;
        public double ParkingBrake;

        public double LightNav;
        public double LightTaxi;
        public double LightLanding;
        public double LightStrobe;
        public double LightBeacon;

        public double BatteryMaster;
        public double AvionicsMaster;
        public double NoSmokingSign;

        public double ApuPct;

        public double EngineOneCombustion;
        public double EngineTwoCombustion;

        public double GearHandleDown;

        public double FuelTotalLbs;
        public double TotalWeight;
        public double EmptyWeight;
        public double DoorPercent;

        // Extendido PWG
        public double AltitudeAGL;
        public double IndicatedAirspeed;
        public double Pitch;
        public double Bank;

        public double FuelTotalCapacity;
        public double FuelLeftQuantity;
        public double FuelRightQuantity;
        public double FuelCenterQuantity;

        public double Engine1FuelFlowPph;
        public double Engine2FuelFlowPph;
        public double TurbEngN1_1;
        public double TurbEngN1_2;

        public double AutopilotActive;
        public double FlapsPercent;
        public double SpoilersHandlePercent;

        public double TransponderState;
        public double TransponderCode;
        public double TransponderStateBackup;
        public double TransponderCodeBackup;

        public double CabinAltitudeFeet;
        public double PressureDiffPsi;
        public double SeatBeltSign;
        public double BleedAirOn;

        // Bloque mapas por sistema (añadido al final para no pisar luces/base)
        public double AutopilotHeadingLock;
        public double AutopilotAltitudeLock;
        public double AutopilotNav1Lock;
        public double AutopilotApproachHold;
        public double AutopilotWingLeveler;
        public double AutopilotDisengaged;
        public double TransponderAvailable;

        // Radios (campo 54 — al final del struct)
        public double Com2FrequencyMhz; // COM ACTIVE FREQUENCY:2
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    internal struct EnvironmentDataStruct
    {
        public double OutsideTemperature;
        public double WindSpeed;
        public double WindDirection;
        public double SeaLevelPressure;
        public double PrecipState;
    }
}
