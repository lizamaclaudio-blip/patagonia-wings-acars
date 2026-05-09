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

    internal enum FacilityRequestId
    {
        AirportList = 1101,
        AirportsInRangeNew = 1102,
        AirportsInRangeOld = 1103,
        AirportFacilityData = 1104
    }

    internal enum FacilityDefineId
    {
        Airport = 1201,
        Runway = 1202,
        TaxiPoint = 1203,
        TaxiPath = 1204,
        TaxiName = 1205,
        TaxiParking = 1206
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
        public double TrueAltitudeFeet;
        public double PressureAltitudeFeet;
        public double RadioAltitudeFeet;
        public double GroundAltitudeFeet;
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
        public double FlapsHandlePercent;
        public double FlapsLeftPercent;
        public double FlapsRightPercent;
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

        // Radios (campos 54-57 — agregados al final para no alterar orden legacy previo)
        public double Com1FrequencyMhz;        // COM ACTIVE FREQUENCY:1
        public double Com1StandbyFrequencyMhz; // COM STANDBY FREQUENCY:1
        public double Com2FrequencyMhz;        // COM ACTIVE FREQUENCY:2
        public double Com2StandbyFrequencyMhz; // COM STANDBY FREQUENCY:2

        // Carga estructural instantanea (campo 58 — agregado al final para no alterar orden legacy)
        public double GForce; // G FORCE

        // Energia electrica real por bus (campo 59 — agregado al final para no alterar orden legacy)
        public double ElectricalMainBusVoltage; // ELECTRICAL MAIN BUS VOLTAGE
    }


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    internal struct FacilityAirportDataStruct
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 9)]
        public string Icao;

        public double Latitude;
        public double Longitude;
        public double AltitudeMeters;
        public int RunwayCount;
        public int TaxiPointCount;
        public int TaxiParkingCount;
        public int TaxiPathCount;
        public int TaxiNameCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct FacilityRunwayDataStruct
    {
        public int PrimaryNumber;
        public int PrimaryDesignator;
        public int SecondaryNumber;
        public int SecondaryDesignator;
        public double Latitude;
        public double Longitude;
        public double AltitudeMeters;

        // C11C3: MSFS FacilityData RUNWAY declares HEADING/LENGTH/WIDTH as FLOAT32.
        // Keeping these as double shifts the following fields and produces bogus
        // values like Heading=E-34, Length=E-314, Width=E-309 even while RUNWAY
        // records are actually arriving correctly.
        public float HeadingDegrees;
        public float LengthMeters;
        public float WidthMeters;
        public int Surface;
    }



    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct FacilityTaxiParkingDataStruct
    {
        public int Type;
        public int TaxiPointType;
        public int Name;
        public int Suffix;
        public uint Number;
        public int Orientation;
        public float HeadingDegrees;
        public float RadiusMeters;
        public float BiasX;
        public float BiasZ;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct FacilityTaxiPointDataStruct
    {
        public int Type;
        public int Orientation;
        public float BiasX;
        public float BiasZ;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct FacilityTaxiPathDataStruct
    {
        public int Type;
        public float WidthMeters;
        public float LeftHalfWidthMeters;
        public float RightHalfWidthMeters;
        public uint WeightLimitLbs;
        public int RunwayNumber;
        public int RunwayDesignator;
        public int LeftEdge;
        public int LeftEdgeLighted;
        public int RightEdge;
        public int RightEdgeLighted;
        public int CenterLine;
        public int CenterLineLighted;
        public int Start;
        public int End;
        public uint NameIndex;
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
