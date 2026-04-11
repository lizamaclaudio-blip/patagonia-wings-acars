using PatagoniaWings.Acars.Core.Enums;

namespace PatagoniaWings.Acars.Core.Models
{
    public class SimData
    {
        // Posición
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
        public bool ParkingBrake { get; set; }
        public bool AutopilotActive { get; set; }
        public bool Pause { get; set; }

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
