namespace PatagoniaWings.Acars.Core.Models
{
    public class Aircraft
    {
        public string Icao { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int MaxPassengers { get; set; }
        public double MaxFuel { get; set; }
        public double MaxRange { get; set; }
        public string ImageResource { get; set; } = string.Empty;
        public bool IsAvailable { get; set; } = true;
    }
}
