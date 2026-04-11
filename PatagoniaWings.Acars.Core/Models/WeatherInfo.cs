namespace PatagoniaWings.Acars.Core.Models
{
    public class WeatherInfo
    {
        public string Station { get; set; } = string.Empty;
        public string RawMetar { get; set; } = string.Empty;
        public string Wind { get; set; } = string.Empty;
        public string Visibility { get; set; } = string.Empty;
        public string Clouds { get; set; } = string.Empty;
        public double Temperature { get; set; }
        public double Dewpoint { get; set; }
        public double QNH { get; set; }
        public string FlightCategory { get; set; } = "VFR";
        public bool IsRaining { get; set; }
        public bool IsSnowing { get; set; }
        public bool HasThunderstorm { get; set; }
    }
}
