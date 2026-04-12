#nullable enable
using System.Runtime.InteropServices;

namespace PatagoniaWings.Acars.SimConnect
{
    internal enum DataDefineId
    {
        AircraftData = 1,
        EnvironmentData = 2
    }

    internal enum RequestId
    {
        AircraftData = 1,
        EnvironmentData = 2
    }

    internal enum EventId
    {
        Pause = 1,
        Crashed = 2
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct AircraftDataStruct
    {
        public double Latitude;
        public double Longitude;
        public double AltitudeFeet;
        public double AltitudeAGL;
        public double IndicatedAirspeed;
        public double GroundSpeed;
        public double VerticalSpeed;
        public double Heading;
        public double Pitch;
        public double Bank;

        public double FuelTotalLbs;
        public double Engine1FuelFlowPph;
        public double Engine2FuelFlowPph;
        public double Engine1N1;
        public double Engine2N1;

        public int OnGround;
        public int ParkingBrake;
        public int AutopilotActive;

        public int StrobeLights;
        public int BeaconLights;
        public int LandingLights;
        public int TaxiLights;
        public int NavLights;

        public int GearHandleDown;
        public double FlapsPercent;
        public double SpoilersHandlePercent;

        public int TransponderState;
        public int TransponderCode;

        public double ApuPct;
        public double CabinAltitudeFeet;
        public double PressureDiffPsi;

        public int SeatBeltSign;
        public int NoSmokingSign;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct EnvironmentDataStruct
    {
        public double OutsideTemperature;
        public double WindSpeed;
        public double WindDirection;
        public double SeaLevelPressure;
        public int PrecipState;
    }
}
