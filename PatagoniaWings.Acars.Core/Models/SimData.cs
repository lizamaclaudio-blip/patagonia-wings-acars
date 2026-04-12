using System;
using PatagoniaWings.Acars.Core.Enums;

namespace PatagoniaWings.Acars.Core.Models
{
    public class SimData
    {
        public DateTime CapturedAtUtc { get; set; }

        // Posicion
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double AltitudeFeet { get; set; }
        public double AltitudeAGL { get; set; }

        // Velocidad
        public double IndicatedAirspeed { get; set; }
        public double GroundSpeed { get; set; }
        public double VerticalSpeed { get; set; }

        // Cabeceo / Rumbo
        public double Heading { get; set; }
        public double Pitch { get; set; }
        public double Bank { get; set; }

        // Motor / Combustible
        public double FuelTotalLbs { get; set; }
        public double FuelFlowLbsHour { get; set; }
        public double Engine1N1 { get; set; }
        public double Engine2N1 { get; set; }

        // Aterrizaje
        public double LandingVS { get; set; }
        public double LandingG { get; set; }
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
        public bool IsRaining { get; set; }

        // Sim
        public SimulatorType SimulatorType { get; set; }
        public bool IsConnected { get; set; }
    }
}
