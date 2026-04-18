using System;
using System.Text.RegularExpressions;

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

        /// <summary>
        /// Construye un WeatherInfo básico a partir de una cadena METAR cruda.
        /// Parsea viento, visibilidad, QNH, temperatura y categoría de vuelo.
        /// </summary>
        public static WeatherInfo ParseRaw(string? raw)
        {
            var w = new WeatherInfo { RawMetar = raw ?? string.Empty };
            if (string.IsNullOrWhiteSpace(raw)) return w;
            var safeRaw = (raw ?? string.Empty).Trim();

            var parts = safeRaw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0) w.Station = parts[0];

            foreach (var p in parts)
            {
                // Viento: 18015KT / 18015G25KT / VRB05KT
                if (Regex.IsMatch(p, @"^(\d{3}|VRB)\d{2,3}(G\d{2,3})?(KT|MPS)$"))
                    w.Wind = p;

                // Visibilidad: 9999 o 1000 (metros) o 10SM
                else if (Regex.IsMatch(p, @"^\d{4}$") && w.Visibility == string.Empty)
                    w.Visibility = p == "9999" ? ">10 km" : (int.Parse(p) / 1000.0).ToString("F1") + " km";
                else if (p.EndsWith("SM", StringComparison.Ordinal) && w.Visibility == string.Empty)
                    w.Visibility = p;

                // QNH: Q1013 o A2992
                else if (p.StartsWith("Q", StringComparison.Ordinal) && p.Length == 5
                         && double.TryParse(p.Substring(1), out var qnh))
                    w.QNH = qnh;
                else if (p.StartsWith("A", StringComparison.Ordinal) && p.Length == 5
                         && double.TryParse(p.Substring(1), out var altimeter))
                    w.QNH = Math.Round(altimeter * 0.338639, 1); // inHg → hPa

                // Temperatura/Rocío: 15/08 o M05/M08
                else if (Regex.IsMatch(p, @"^M?\d{2}/M?\d{2}$"))
                {
                    var sp = p.Split('/');
                    w.Temperature = ParseMetarTemp(sp[0]);
                    w.Dewpoint    = ParseMetarTemp(sp[1]);
                }

                // Fenómenos significativos
                else if (p.Contains("TS")) w.HasThunderstorm = true;
                else if (p == "RA" || p == "-RA" || p == "+RA" || p.Contains("SH")) w.IsRaining = true;
                else if (p == "SN" || p == "-SN" || p == "+SN") w.IsSnowing = true;
            }

            // Nubes
            var cloudsMatch = Regex.Match(safeRaw, @"(FEW|SCT|BKN|OVC)\d{3}");
            if (cloudsMatch.Success) w.Clouds = cloudsMatch.Value;

            // Categoría de vuelo básica por visibilidad/nubes
            w.FlightCategory = DetermineCategory(safeRaw);

            return w;
        }

        private static double ParseMetarTemp(string s)
        {
            if (s.StartsWith("M", StringComparison.Ordinal))
                return -double.Parse(s.Substring(1));
            return double.Parse(s);
        }

        private static string DetermineCategory(string raw)
        {
            if (raw.Contains("LIFR")) return "LIFR";
            if (raw.Contains("IFR"))  return "IFR";
            if (raw.Contains("MVFR")) return "MVFR";
            // BKN/OVC bajas + vis reducida → IFR aproximado
            var ovcMatch = Regex.Match(raw, @"(BKN|OVC)(\d{3})");
            if (ovcMatch.Success && int.TryParse(ovcMatch.Groups[2].Value, out var ceiling) && ceiling < 10)
                return ceiling < 5 ? "IFR" : "MVFR";
            return "VFR";
        }
    }
}
