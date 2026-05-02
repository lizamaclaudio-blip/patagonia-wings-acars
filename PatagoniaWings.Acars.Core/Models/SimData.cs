using System;
using PatagoniaWings.Acars.Core.Enums;

namespace PatagoniaWings.Acars.Core.Models
{
    public class SimData
    {
        public DateTime CapturedAtUtc { get; set; }
        
        // Identificación del avión
        public string AircraftTitle { get; set; } = string.Empty;
        public string AircraftProfile { get; set; } = "MSFS Native";

        // Posicion
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double AltitudeFeet { get; set; }
        public double AltitudeAGL { get; set; }
        public double IndicatedAltitudeFeet { get; set; }
        public double TrueAltitudeFeet { get; set; }
        public double PressureAltitudeFeet { get; set; }
        public double RadioAltitudeFeet { get; set; }
        public double GroundAltitudeFeet { get; set; }

        // Velocidad
        public double IndicatedAirspeed { get; set; }
        public double GroundSpeed { get; set; }
        public double VerticalSpeed { get; set; }

        // Cabeceo / Rumbo
        public double Heading { get; set; }
        public double Pitch { get; set; }
        public double Bank { get; set; }

        // Motor / Combustible
        /// <summary>Combustible en la unidad nativa del backend (lbs para SimConnect, kg para FSUIPC).
        /// Usar siempre FuelKg para comparaciones y display.</summary>
        public double FuelTotalLbs { get; set; }
        /// <summary>Combustible total en kg, normalizado por el backend (SimConnect convierte lbs→kg).</summary>
        public double FuelKg { get; set; }
        public double FuelFlowLbsHour { get; set; }
        public double Engine1N1 { get; set; }
        public double Engine2N1 { get; set; }
        
        // Tanques individuales (para aviones complejos como A319 Headwind)
        public double FuelLeftTankLbs { get; set; }
        public double FuelRightTankLbs { get; set; }
        public double FuelCenterTankLbs { get; set; }
        public double FuelTotalCapacityLbs { get; set; }
        public double TotalWeightLbs { get; set; }
        public double TotalWeightKg { get; set; }
        public double ZeroFuelWeightKg { get; set; }
        public double PayloadKg { get; set; }

        // Aterrizaje
        public double LandingVS { get; set; }
        public double LandingG { get; set; }
        /// <summary>G instantanea leida desde el simulador (SimConnect G FORCE). Normal en vuelo estable ≈ 1.0.</summary>
        public double GForce { get; set; }
        public bool OnGround { get; set; }

        // Luces / Sistemas
        public bool StrobeLightsOn { get; set; }
        public bool BeaconLightsOn { get; set; }
        public bool LandingLightsOn { get; set; }
        public bool TaxiLightsOn { get; set; }
        public bool NavLightsOn { get; set; }
        public bool ParkingBrake { get; set; }
        public bool AutopilotActive { get; set; }
        public bool Pause { get; set; }

        // Sistemas de cabina
        public bool SeatBeltSign { get; set; }
        public bool NoSmokingSign { get; set; }
        public bool GearDown { get; set; }
        public bool GearTransitioning { get; set; }
        public bool FlapsDeployed { get; set; }
        public double FlapsPercent { get; set; }
        public bool SpoilersArmed { get; set; }
        public bool ReverserActive { get; set; }

        // Aviónica / Pressurización
        public bool TransponderCharlieMode { get; set; }
        public int TransponderCode { get; set; }
        public int TransponderStateRaw { get; set; }
        public bool ApuRunning { get; set; }
        public bool ApuAvailable { get; set; }
        public bool BleedAirOn { get; set; }
        public double CabinAltitudeFeet { get; set; }
        public double PressureDiffPsi { get; set; }

        // Ambiente
        public double OutsideTemperature { get; set; }
        public double WindSpeed { get; set; }
        public double WindDirection { get; set; }
        public double QNH { get; set; }
        public double QnhInHg { get; set; }
        public bool IsRaining { get; set; }

        // Motores extendidos (soporte 4 motores – 737/A320 usan 1-2, B744/A340 usan 1-4)
        public double Engine3N1 { get; set; }
        public double Engine4N1 { get; set; }

        // Sistemas adicionales (arquitectura SUR Air)
        public bool EngineOneRunning { get; set; }
        public bool EngineTwoRunning { get; set; }
        public bool EngineThreeRunning { get; set; }
        public bool EngineFourRunning { get; set; }
        public bool BatteryMasterOn { get; set; }
        public bool AvionicsMasterOn { get; set; }
        /// <summary>Voltaje real del bus electrico principal. En algunos addons Black Square el switch de bateria nativo puede quedar en 1 aunque el avion este sin energia; este valor permite inferir Cold & Dark con mas confianza.</summary>
        public double ElectricalMainBusVoltage { get; set; }
        public bool DoorOpen { get; set; }
        public bool InertialSeparatorOn { get; set; }
        public double EmptyWeightLbs { get; set; }
        public double EmptyWeightKg { get; set; }

        // Radios
        public double Com1FrequencyMhz { get; set; }
        public double Com1StandbyFrequencyMhz { get; set; }
        public double Com2FrequencyMhz { get; set; }
        public double Com2StandbyFrequencyMhz { get; set; }

        // Perfil de aeronave normalizado (código estable Patagonia Wings)
        // Ejemplos: C208_MSFS, C208_BLACKSQUARE, B738_PMDG, A320_FENIX, A20N_FBW
        public string DetectedProfileCode { get; set; } = "MSFS_NATIVE";
        public string AircraftTypeCode { get; set; } = string.Empty;
        public string AircraftVariantCode { get; set; } = string.Empty;
        public string AddonSource { get; set; } = string.Empty;
        public string ProfileCode { get; set; } = string.Empty;
        public string DetectionConfidence { get; set; } = "unknown";
        public string DetectionReason { get; set; } = string.Empty;
        public string DetectionSource { get; set; } = "simconnect_title";
        public string MatchedTitle { get; set; } = string.Empty;
        public string MatchedPattern { get; set; } = string.Empty;
        public bool FallbackUsed { get; set; }
        public string ProfileStatus { get; set; } = "unknown_profile";

        // Sim
        public SimulatorType SimulatorType { get; set; }
        public bool IsConnected { get; set; }
    }
}
