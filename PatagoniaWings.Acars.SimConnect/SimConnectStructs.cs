using System.Runtime.InteropServices;
using Microsoft.FlightSimulator.SimConnect;

namespace PatagoniaWings.Acars.SimConnect
{
    // IDs de definición de datos
    public enum DataDefineId
    {
        AircraftData,
        EnvironmentData
    }

    // IDs de request
    public enum RequestId
    {
        AircraftData,
        EnvironmentData
    }

    // IDs de evento
    public enum EventId
    {
        Pause,
        Crashed,
        SimStart,
        SimStop
    }

    // IDs de notificaciones de sistema
    public enum NotificationGroupId
    {
        Cockpit,
        System
    }

    /// <summary>Datos del avión recibidos desde SimConnect.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct AircraftDataStruct
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
        public double FuelFlowLbsHour;
        public double Engine1N1;
        public double Engine2N1;
        public double LandingVS;
        public double GForce;
        [MarshalAs(UnmanagedType.I4)] public int OnGround;
        [MarshalAs(UnmanagedType.I4)] public int StrobeLights;
        [MarshalAs(UnmanagedType.I4)] public int BeaconLights;
        [MarshalAs(UnmanagedType.I4)] public int LandingLights;
        [MarshalAs(UnmanagedType.I4)] public int ParkingBrake;
        [MarshalAs(UnmanagedType.I4)] public int AutopilotActive;
    }

    /// <summary>Datos del entorno/ambiente.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    public struct EnvironmentDataStruct
    {
        public double OutsideTemperature;
        public double WindSpeed;
        public double WindDirection;
        public double SeaLevelPressure;
        [MarshalAs(UnmanagedType.I4)] public int Precipitation;
    }
}
