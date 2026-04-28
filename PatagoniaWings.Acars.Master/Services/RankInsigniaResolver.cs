using System;

namespace PatagoniaWings.Acars.Master.Services
{
    public static class RankInsigniaResolver
    {
        private const string Fallback = "Assets/Ranks/cadet-school.png";

        public static string Resolve(string? rankCode)
        {
            switch ((rankCode ?? string.Empty).Trim().ToUpperInvariant())
            {
                case "CADET":
                    return Fallback;
                case "SECOND_OFFICER":
                    return "Assets/Ranks/second-officer.png";
                case "JUNIOR_FIRST_OFFICER":
                    return "Assets/Ranks/junior-first-officer.png";
                case "FIRST_OFFICER":
                    return "Assets/Ranks/first-officer.png";
                case "SENIOR_FIRST_OFFICER":
                    return "Assets/Ranks/senior-first-officer.png";
                case "JUNIOR_CAPTAIN":
                    return "Assets/Ranks/junior-captain.png";
                case "CAPTAIN":
                    return "Assets/Ranks/captain.png";
                case "SENIOR_CAPTAIN":
                    return "Assets/Ranks/senior-captain.png";
                case "INTERNATIONAL_COMMANDER":
                    return "Assets/Ranks/international-commander.png";
                case "LINE_CHECK_CAPTAIN":
                    return "Assets/Ranks/line-check-captain.png";
                default:
                    return Fallback;
            }
        }
    }
}
