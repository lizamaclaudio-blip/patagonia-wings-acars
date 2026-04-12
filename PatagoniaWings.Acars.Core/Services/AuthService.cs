using System.IO;
using System.Web.Script.Serialization;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Core.Services
{
    public class AuthService
    {
        private readonly string _sessionFile;
        private readonly JavaScriptSerializer _json;
        public Pilot? CurrentPilot { get; private set; }

        public AuthService()
        {
            var appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "PatagoniaWings", "Acars");
            Directory.CreateDirectory(folder);
            _sessionFile = Path.Combine(folder, "session.json");
            _json = new JavaScriptSerializer();
        }

        public bool IsLoggedIn => CurrentPilot != null && CurrentPilot.HasUsableToken;

        public void SetCurrentPilot(Pilot pilot)
        {
            CurrentPilot = MergeWithCurrentSession(pilot);
        }

        public void SaveSession(Pilot pilot)
        {
            var mergedPilot = MergeWithCurrentSession(pilot);
            CurrentPilot = mergedPilot;
            File.WriteAllText(_sessionFile, _json.Serialize(mergedPilot));
        }

        public void ClearSavedSession()
        {
            if (File.Exists(_sessionFile))
                File.Delete(_sessionFile);
        }

        /// <summary>
        /// Carga la sesión guardada en disco y la deja en CurrentPilot aunque el token esté vencido.
        /// Retorna true si se cargó un piloto (token válido o no).
        /// Usa HasExpiredToken para detectar si hace falta refresh.
        /// </summary>
        public bool TryRestoreSession()
        {
            if (!File.Exists(_sessionFile)) return false;
            try
            {
                var json = File.ReadAllText(_sessionFile);
                CurrentPilot = _json.Deserialize<Pilot>(json);
                return CurrentPilot != null;
            }
            catch
            {
                CurrentPilot = null;
                return false;
            }
        }

        /// <summary>True si hay piloto con token vencido y refresh token disponible.</summary>
        public bool HasExpiredToken =>
            CurrentPilot != null
            && !string.IsNullOrWhiteSpace(CurrentPilot.RefreshToken)
            && !CurrentPilot.HasUsableToken;

        public void Logout()
        {
            CurrentPilot = null;
            ClearSavedSession();
        }

        private Pilot MergeWithCurrentSession(Pilot pilot)
        {
            if (pilot == null)
            {
                return new Pilot();
            }

            var current = CurrentPilot;
            if (current == null)
            {
                return pilot;
            }

            if (string.IsNullOrWhiteSpace(pilot.Token))
                pilot.Token = current.Token;

            if (string.IsNullOrWhiteSpace(pilot.RefreshToken))
                pilot.RefreshToken = current.RefreshToken;

            if (!pilot.TokenExpiresAtUtc.HasValue)
                pilot.TokenExpiresAtUtc = current.TokenExpiresAtUtc;

            if (string.IsNullOrWhiteSpace(pilot.CallSign))
                pilot.CallSign = current.CallSign;

            if (string.IsNullOrWhiteSpace(pilot.Email))
                pilot.Email = current.Email;

            if (string.IsNullOrWhiteSpace(pilot.FullName))
                pilot.FullName = current.FullName;

            if (string.IsNullOrWhiteSpace(pilot.RankName))
                pilot.RankName = current.RankName;

            if (string.IsNullOrWhiteSpace(pilot.RankCode))
                pilot.RankCode = current.RankCode;

            if (string.IsNullOrWhiteSpace(pilot.CareerRankCode))
                pilot.CareerRankCode = current.CareerRankCode;

            if (string.IsNullOrWhiteSpace(pilot.CurrentAirportCode))
                pilot.CurrentAirportCode = current.CurrentAirportCode;

            if (string.IsNullOrWhiteSpace(pilot.BaseHubCode))
                pilot.BaseHubCode = current.BaseHubCode;

            if (pilot.TotalFlights <= 0)
                pilot.TotalFlights = current.TotalFlights;

            if (pilot.TotalHours <= 0)
                pilot.TotalHours = current.TotalHours;

            if (pilot.Points <= 0)
                pilot.Points = current.Points;

            if (string.IsNullOrWhiteSpace(pilot.Language))
                pilot.Language = current.Language;

            return pilot;
        }
    }
}
