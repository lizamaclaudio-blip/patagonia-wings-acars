using System;
using PatagoniaWings.Acars.Core.Enums;

namespace PatagoniaWings.Acars.Core.Models
{
    public class Pilot
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string CallSign { get; set; } = string.Empty;
        public PilotRank Rank { get; set; }
        public string RankName { get; set; } = string.Empty;
        public int TotalFlights { get; set; }
        public double TotalHours { get; set; }
        public double TotalDistance { get; set; }
        public int Points { get; set; }
        public bool IsOnline { get; set; }
        public string AvatarUrl { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime? TokenExpiresAtUtc { get; set; }
        public string PreferredSimulator { get; set; } = "MSFS";
        public string Language { get; set; } = "ESP";
        public bool CopilotVoiceFemale { get; set; } = true;
        public string CurrentAirportCode { get; set; } = string.Empty;
        public string BaseHubCode { get; set; } = string.Empty;

        public bool HasUsableToken
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Token)) return false;
                if (!TokenExpiresAtUtc.HasValue) return true;
                return TokenExpiresAtUtc.Value > DateTime.UtcNow.AddMinutes(1);
            }
        }
    }
}
