using System.Collections.ObjectModel;

namespace PatagoniaWings.Acars.Master.Preview
{
    /// <summary>
    /// TEMP/PREVIEW: fallback visual local para poblar la UI cuando todavia no hay datos reales.
    /// Vive solo en Master para poder eliminarlo sin tocar Core ni contratos.
    /// </summary>
    public static class AcarsPreviewData
    {
        public static bool Enabled => true;

        public static string PortalUrl => "https://www.patagoniaw.com";

        public static string PilotCallsign => "PW4002";
        public static string PilotName => "Santiago Cianciulli";
        public static string PilotHours => "991.8";
        public static string PilotScore => "134";
        public static string PilotRank => "Comandante";
        public static string PilotLocationCode => "SCEL";
        public static string PilotLocationSubtitle => "Base operativa actual";
        public static string CommunityOnline => "26";
        public static string CommunityAtBase => "8";

        public static ReadOnlyCollection<PreviewRecentFlightItem> RecentFlights { get; } =
            new ReadOnlyCollection<PreviewRecentFlightItem>(new[]
            {
                new PreviewRecentFlightItem("PWG738", "SCEL", "SCIE", 175),
                new PreviewRecentFlightItem("PWG915", "SCDA", "SCEL", 162),
                new PreviewRecentFlightItem("PWG258", "SCTE", "SCEL", 148),
                new PreviewRecentFlightItem("PWG442", "SCFA", "SCEL", 171)
            });

        public static string DispatchFlightNumber => "PWG258";
        public static string DispatchOrigin => "SCEL";
        public static string DispatchDestination => "SCIE";
        public static string DispatchStd => "17:00";
        public static string DispatchBlock => "01:18";
        public static string DispatchAircraft => "LV-JMU · A320 · Fenix";
        public static string DispatchFuel => "8649 kg";
        public static string DispatchPayload => "Payload 5643 kg · 135 pax";
        public static string DispatchAlternate => "SCVM";
        public static string DispatchCruiseLevel => "FL310";
        public static string DispatchRoute => "KIKES6A KIKES UM674 LOSIP UL302 VUKSU";
        public static string DispatchStatus => "DESPACHO LISTO";
        public static string DispatchState => "Reserva dispatched · Paquete prepared";
        public static string DispatchGate => "Motores OFF · Freno set · Aeronave alineada con la reserva";
        public static string DispatchSource => "Fuente oficial: Web Patagonia Wings";

        public static string LiveFlightNumber => "PWG258";
        public static string LiveRegistration => "LV-JMU";
        public static string LiveOrigin => "SCEL";
        public static string LiveDestination => "SCIE";
        public static string LiveDistanceFromOrigin => "214 nm";
        public static string LiveDistanceToDestination => "191 nm";
        public static string LiveProgress => "53%";
        public static string LiveElapsed => "01:18";
        public static string LiveOfficialPhase => "CRU · Crucero";
        public static string LiveFuel => "8649 kg";
        public static string LivePhaseLabel => "CRUCERO ESTABLE";
        public static string LiveRoute => "KIKES6A KIKES UM674 LOSIP UL302 VUKSU";
        public static string LiveTransponder => "MODO C · 2000";
        public static string LiveAltitude => "FL310";
        public static string LiveHeading => "182°";
        public static string LiveGs => "442 kt";
        public static string LiveVs => "+200 fpm";
        public static string LiveWind => "210 / 24";
        public static string LiveBlock => "01:18";
        public static string LiveAcarsLog =>
            "14:47:22  ACARS SYS  METAR despacho cargado\n" +
            "14:47:31  ACARS SYS  Pushback autorizado\n" +
            "14:50:04  ACARS SYS  Taxi detectado\n" +
            "15:08:42  ACARS SYS  Climb estable y Modo C activo";

        public static string CloseFlightNumber => "PWG258";
        public static string CloseRegistration => "LV-JMU";
        public static string CloseRoute => "SCEL -> SCIE";
        public static string ClosePatagoniaScore => "96";
        public static string CloseProcedureScore => "82";
        public static string ClosePerformanceScore => "14";
        public static string CloseQualifications => "A320 typed · VMC/IMC OK · Crosswind CAT I";

        public static string NoDispatchTitle => "NO TIENES UN VUELO DESPACHADO";
        public static string NoDispatchBody =>
            "Debes generar o reservar un vuelo desde Patagonia Wings Web antes de continuar con el flujo ACARS.";
        public static string NoDispatchHint =>
            "Cuando ya tengas una reserva activa o un dispatch preparado, usa Reintentar para volver a consultar sin cerrar la app.";
    }

    public sealed class PreviewRecentFlightItem
    {
        public PreviewRecentFlightItem(string flightNumber, string departureIcao, string arrivalIcao, int score)
        {
            FlightNumber = flightNumber;
            DepartureIcao = departureIcao;
            ArrivalIcao = arrivalIcao;
            Score = score;
        }

        public string FlightNumber { get; }
        public string DepartureIcao { get; }
        public string ArrivalIcao { get; }
        public int Score { get; }
    }
}
