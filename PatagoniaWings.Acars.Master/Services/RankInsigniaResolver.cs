using System;
using System.IO;

namespace PatagoniaWings.Acars.Master.Services
{
    public static class RankInsigniaResolver
    {
        private const string FallbackFile = "cadet-school.png";

        public static string Resolve(string? rankCode)
        {
            var fileName = ResolveFileName((rankCode ?? string.Empty).Trim().ToUpperInvariant());
            return BuildSiteOfOriginUri(fileName);
        }

        private static string ResolveFileName(string normalizedRankCode)
        {
            switch (normalizedRankCode)
            {
                case "CADET":
                    return FallbackFile;
                case "SECOND_OFFICER":
                    return "second-officer.png";
                case "JUNIOR_FIRST_OFFICER":
                    return "junior-first-officer.png";
                case "FIRST_OFFICER":
                    return "first-officer.png";
                case "SENIOR_FIRST_OFFICER":
                    return "senior-first-officer.png";
                case "JUNIOR_CAPTAIN":
                    return "junior-captain.png";
                case "CAPTAIN":
                    return "captain.png";
                case "SENIOR_CAPTAIN":
                    return "senior-captain.png";
                case "INTERNATIONAL_COMMANDER":
                    return "international-commander.png";
                case "LINE_CHECK_CAPTAIN":
                    return "line-check-captain.png";
                default:
                    return FallbackFile;
            }
        }

        private static string BuildSiteOfOriginUri(string fileName)
        {
            var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Ranks", fileName);
            if (!File.Exists(filePath))
            {
                filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Ranks", FallbackFile);
            }

            var absoluteUri = new Uri(filePath, UriKind.Absolute);
            return absoluteUri.AbsoluteUri;
        }
    }
}
