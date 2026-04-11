namespace PatagoniaWings.Acars.Core.Models
{
    public class Airport
    {
        public string Icao { get; set; } = string.Empty;
        public string Iata { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int Elevation { get; set; }
        public string Metar { get; set; } = string.Empty;
        public string Taf { get; set; } = string.Empty;
        public string WindDir { get; set; } = string.Empty;
        public string WindSpeed { get; set; } = string.Empty;
        public string Visibility { get; set; } = string.Empty;
        public string Temperature { get; set; } = string.Empty;
        public string QNH { get; set; } = string.Empty;
    }
}
