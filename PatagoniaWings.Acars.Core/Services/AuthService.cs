using System;
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
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "PatagoniaWings", "Acars");
            Directory.CreateDirectory(folder);
            _sessionFile = Path.Combine(folder, "session.json");
            _json = new JavaScriptSerializer();
        }

        public bool IsLoggedIn => CurrentPilot != null && CurrentPilot.HasUsableToken;

        public void SetCurrentPilot(Pilot pilot)
        {
            if (pilot == null)
            {
                return;
            }

            CurrentPilot = MergePilot(CurrentPilot, pilot);
        }

        public void SaveSession(Pilot pilot)
        {
            SetCurrentPilot(pilot);
            if (CurrentPilot != null)
            {
                File.WriteAllText(_sessionFile, _json.Serialize(CurrentPilot));
            }
        }

        public void ClearSavedSession()
        {
            if (File.Exists(_sessionFile))
            {
                File.Delete(_sessionFile);
            }
        }

        public bool TryRestoreSession()
        {
            if (!File.Exists(_sessionFile)) return false;
            try
            {
                var json = File.ReadAllText(_sessionFile);
                CurrentPilot = _json.Deserialize<Pilot>(json);
                return IsLoggedIn;
            }
            catch
            {
                CurrentPilot = null;
                return false;
            }
        }

        public void Logout()
        {
            CurrentPilot = null;
            ClearSavedSession();
        }

        private static Pilot MergePilot(Pilot? current, Pilot incoming)
        {
            if (current == null)
            {
                return incoming;
            }

            if (string.IsNullOrWhiteSpace(incoming.Token))
            {
                incoming.Token = current.Token;
            }

            if (string.IsNullOrWhiteSpace(incoming.RefreshToken))
            {
                incoming.RefreshToken = current.RefreshToken;
            }

            if (!incoming.TokenExpiresAtUtc.HasValue)
            {
                incoming.TokenExpiresAtUtc = current.TokenExpiresAtUtc;
            }

            if (string.IsNullOrWhiteSpace(incoming.CallSign))
            {
                incoming.CallSign = current.CallSign;
            }

            if (string.IsNullOrWhiteSpace(incoming.Email))
            {
                incoming.Email = current.Email;
            }

            if (string.IsNullOrWhiteSpace(incoming.FullName))
            {
                incoming.FullName = current.FullName;
            }

            if (string.IsNullOrWhiteSpace(incoming.RankName))
            {
                incoming.RankName = current.RankName;
            }

            if (string.IsNullOrWhiteSpace(incoming.Language))
            {
                incoming.Language = current.Language;
            }

            if (string.IsNullOrWhiteSpace(incoming.PreferredSimulator))
            {
                incoming.PreferredSimulator = current.PreferredSimulator;
            }

            return incoming;
        }
    }
}
