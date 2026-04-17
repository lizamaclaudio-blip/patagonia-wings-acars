#nullable enable
using System.Runtime.InteropServices;

namespace PatagoniaWings.Acars.SimConnect
{
    internal enum DataDefineId
    {
        AircraftData  = 1,
        EnvironmentData = 2,
        LightsData    = 3    // ← separado, VISUAL_FRAME, LIGHT ON STATES bitmask
    }

    internal enum RequestId
    {
        AircraftData  = 1,
        EnvironmentData = 2,
        LightsData    = 3
    }

    internal enum EventId
    {
        Pause   = 1,
        Crashed = 2
    }

    /// <summary>
    /// Struct principal: posición, velocidades, fuel, motores, gear, flaps, transponder, APU, presurización.
    /// Las luces se leen por separado via LIGHT ON STATES (ver LightsDataStruct).
    /// IMPORTANTE: el orden de campos debe coincidir EXACTAMENTE con las llamadas AddToDataDefinition.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct AircraftDataStruct
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Title;

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
        public double FuelTotalCapacity;
        public double FuelLeftQuantity;
        public double FuelRightQuantity;
        public double FuelCenterQuantity;
        public double TotalWeight;

        public double Engine1FuelFlowPph;
        public double Engine2FuelFlowPph;
        public double Engine1N1;
        public double Engine2N1;
        public double TurbEngN1_1;
        public double TurbEngN1_2;

        public int OnGround;
        public int ParkingBrake;
        public int AutopilotActive;

        // Luces eliminadas de aquí — se leen via LightsDataStruct (LIGHT ON STATES)

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
        public int BleedAirOn;   // BLEED AIR ENGINE:1 — bool (switch ON = engine bleed air active)

        // Luces individuales: fallback para addons que no actualizan LIGHT ON STATES
        // (ej. Black Square Cessna 208, algunos turbohélices custom)
        public int LightBeacon;
        public int LightStrobe;
        public int LightLanding;
        public int LightTaxi;
        public int LightNav;
    }

    /// <summary>
    /// Struct para luces: usa LIGHT ON STATES (bitmask MSFS nativo).
    /// Actualizado via SIMCONNECT_PERIOD.VISUAL_FRAME para respuesta inmediata.
    ///
    /// Bits de LIGHT ON STATES:
    ///   Bit 0  (0x001): Nav
    ///   Bit 1  (0x002): Beacon
    ///   Bit 2  (0x004): Landing
    ///   Bit 3  (0x008): Taxi
    ///   Bit 4  (0x010): Strobe
    ///   Bit 5  (0x020): Panel
    ///   Bit 6  (0x040): Recognition
    ///   Bit 7  (0x080): Wing
    ///   Bit 8  (0x100): Logo
    ///   Bit 9  (0x200): Cabin
    ///   Bit 10 (0x400): Head
    ///   Bit 11 (0x800): Brake
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct LightsDataStruct
    {
        public int LightOnStates;   // LIGHT ON STATES — bitmask único, actualiza en tiempo real
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct EnvironmentDataStruct
    {
        public double OutsideTemperature;
        public double WindSpeed;
        public double WindDirection;
        public double SeaLevelPressure;
        public int    PrecipState;
    }
}
