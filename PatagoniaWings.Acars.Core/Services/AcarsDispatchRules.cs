using System;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    public static class AcarsDispatchRules
    {
        public static bool IsCharter(AcarsActiveDispatchPackage dispatch)
        {
            return dispatch != null
                && string.Equals((dispatch.FlightModeCode ?? string.Empty).Trim(), "CHARTER", StringComparison.OrdinalIgnoreCase);
        }

        public static bool RequiresRealWeather(AcarsActiveDispatchPackage dispatch)
        {
            if (dispatch == null)
            {
                return false;
            }

            return dispatch.RealWeatherRequired || IsCharter(dispatch);
        }

        public static bool MovesPilotAndAircraftOnClose(AcarsActiveDispatchPackage dispatch)
        {
            if (dispatch == null)
            {
                return false;
            }

            if (IsCharter(dispatch))
            {
                return true;
            }

            return dispatch.MovePilotOnClose && dispatch.MoveAircraftOnClose;
        }

        public static bool RouteMatches(AcarsActiveDispatchPackage dispatch, string originIcao, string destinationIcao)
        {
            if (dispatch == null)
            {
                return false;
            }

            return string.Equals((dispatch.OriginIcao ?? string.Empty).Trim(), (originIcao ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals((dispatch.DestinationIcao ?? string.Empty).Trim(), (destinationIcao ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public static bool AircraftMatches(AcarsActiveDispatchPackage dispatch, string aircraftRegistration, string aircraftTypeCode)
        {
            if (dispatch == null)
            {
                return false;
            }

            var registrationMatches = string.IsNullOrWhiteSpace(dispatch.AircraftRegistration)
                || string.Equals(dispatch.AircraftRegistration.Trim(), (aircraftRegistration ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

            var typeMatches = string.IsNullOrWhiteSpace(dispatch.AircraftTypeCode)
                || string.Equals(dispatch.AircraftTypeCode.Trim(), (aircraftTypeCode ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

            return registrationMatches && typeMatches;
        }
    }
}
