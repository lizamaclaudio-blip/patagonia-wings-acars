using System;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    public static class CharterDispatchRules
    {
        public static bool IsCharter(string flightModeCode)
        {
            return string.Equals((flightModeCode ?? string.Empty).Trim(), "CHARTER", StringComparison.OrdinalIgnoreCase)
                || string.Equals((flightModeCode ?? string.Empty).Trim(), "charter", StringComparison.OrdinalIgnoreCase);
        }

        public static CharterDispatchContext FromReservation(
            string reservationId,
            string pilotCallsign,
            string aircraftId,
            string aircraftRegistration,
            string aircraftTypeCode,
            string originIcao,
            string destinationIcao,
            DateTime? scheduledDepartureUtc)
        {
            return new CharterDispatchContext
            {
                ReservationId = reservationId ?? string.Empty,
                PilotCallsign = pilotCallsign ?? string.Empty,
                AircraftId = aircraftId ?? string.Empty,
                AircraftRegistration = aircraftRegistration ?? string.Empty,
                AircraftTypeCode = aircraftTypeCode ?? string.Empty,
                OriginIcao = NormalizeIcao(originIcao),
                DestinationIcao = NormalizeIcao(destinationIcao),
                ScheduledDepartureUtc = scheduledDepartureUtc,
                RealWeatherRequired = true,
                MovePilotOnClose = true,
                MoveAircraftOnClose = true
            };
        }

        public static bool IsRouteValid(CharterDispatchContext context, out string reason)
        {
            reason = string.Empty;

            if (context == null)
            {
                reason = "Contexto chárter vacío.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(context.OriginIcao) || context.OriginIcao.Trim().Length < 3)
            {
                reason = "Origen ICAO inválido.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(context.DestinationIcao) || context.DestinationIcao.Trim().Length < 3)
            {
                reason = "Destino ICAO inválido.";
                return false;
            }

            if (string.Equals(context.OriginIcao.Trim(), context.DestinationIcao.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                reason = "Origen y destino no pueden ser iguales.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(context.AircraftId) && string.IsNullOrWhiteSpace(context.AircraftRegistration))
            {
                reason = "Aeronave chárter no definida.";
                return false;
            }

            return true;
        }

        private static string NormalizeIcao(string value)
        {
            return (value ?? string.Empty).Trim().ToUpperInvariant();
        }
    }
}
