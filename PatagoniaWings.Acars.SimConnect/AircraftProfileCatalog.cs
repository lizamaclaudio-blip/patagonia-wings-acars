using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Core.Services;

namespace PatagoniaWings.Acars.SimConnect
{
    internal enum AircraftLightStrategy
    {
        Hybrid,
        Bitmask,
        Individual
    }

    internal static class AircraftProfileCatalog
    {
        public static AircraftProfile Resolve(string baseDirectory, string aircraftTitle)
        {
            return AircraftNormalizationService.ResolveProfile(aircraftTitle, baseDirectory);
        }

        public static AircraftLightStrategy GetLightStrategy(AircraftProfile profile)
        {
            var mode = (profile == null ? "individual" : profile.LightMode ?? "individual").Trim().ToLowerInvariant();
            switch (mode)
            {
                case "bitmask":
                    return AircraftLightStrategy.Bitmask;
                case "hybrid":
                    return AircraftLightStrategy.Hybrid;
                default:
                    return AircraftLightStrategy.Individual;
            }
        }
    }
}
