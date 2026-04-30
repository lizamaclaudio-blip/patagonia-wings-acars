using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace PatagoniaWings.Acars.Master.Services
{
    public sealed class SayIntentionsFlightJsonService
    {
        private Timer _pollTimer;
        private string _lastPath = string.Empty;
        private DateTime _lastReadUtc = DateTime.MinValue;
        private bool _connected;
        private string _callsign = string.Empty;
        private string _origin = string.Empty;
        private string _destination = string.Empty;

        public void Initialize()
        {
            _pollTimer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15));
        }

        public void Shutdown()
        {
            try { _pollTimer?.Dispose(); } catch { }
            _pollTimer = null;
        }

        public string GetStatusText()
        {
            if (!_connected)
            {
                return "SayIntentions: no detectado";
            }

            var stamp = _lastReadUtc == DateTime.MinValue ? "sin lectura" : _lastReadUtc.ToLocalTime().ToString("HH:mm:ss");
            return "SayIntentions: conectado" +
                   (string.IsNullOrWhiteSpace(_callsign) ? string.Empty : " | " + _callsign) +
                   (string.IsNullOrWhiteSpace(_origin) ? string.Empty : " " + _origin) +
                   (string.IsNullOrWhiteSpace(_destination) ? string.Empty : "-" + _destination) +
                   " | " + stamp;
        }

        public string GetFlightJsonFolderPath()
        {
            var file = ResolveFlightJsonPath();
            return Path.GetDirectoryName(file) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        private void Poll()
        {
            try
            {
                var path = ResolveFlightJsonPath();
                _lastPath = path;

                if (!File.Exists(path))
                {
                    _connected = false;
                    _callsign = string.Empty;
                    _origin = string.Empty;
                    _destination = string.Empty;
                    return;
                }

                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text))
                {
                    _connected = false;
                    return;
                }

                var json = JObject.Parse(text);
                _callsign = ReadJsonText(json, "callsign");
                _origin = ReadJsonText(json, "origin");
                _destination = ReadJsonText(json, "destination");
                _connected = true;
                _lastReadUtc = DateTime.UtcNow;
            }
            catch
            {
                _connected = false;
            }
        }

        private static string ResolveFlightJsonPath()
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "SayIntentionsAI", "flight.json");
        }

        private static string ReadJsonText(JObject json, string key)
        {
            var token = json.SelectToken(key) ?? json.SelectToken("flight." + key) ?? json.SelectToken("data." + key);
            return token == null ? string.Empty : (token.ToString().Trim());
        }
    }
}
