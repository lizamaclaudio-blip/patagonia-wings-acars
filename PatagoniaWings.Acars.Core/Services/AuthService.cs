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

        public void SaveSession(Pilot pilot)
        {
            CurrentPilot = pilot;
            File.WriteAllText(_sessionFile, _json.Serialize(pilot));
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
            if (File.Exists(_sessionFile))
                File.Delete(_sessionFile);
        }
    }
}
