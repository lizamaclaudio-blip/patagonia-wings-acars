using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// Reproductor de sonidos del ACARS. Soporta voces de copiloto y crew terrestre.
    /// </summary>
    public class AcarsSoundPlayer : IDisposable
    {
        private readonly string _soundsDir;
        private SoundPlayer? _player;
        private bool _disposed;

        public string Language { get; set; } = "ESP";
        public bool VoiceFemale { get; set; } = true;

        public AcarsSoundPlayer()
        {
            _soundsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds");
        }

        // ── Sonidos generales ─────────────────────────────────────────────────

        public void PlayDing() => PlayFile("DingACARS.wav");
        public void PlayBeep() => PlayFile("Beep.wav");
        public void PlayRadio() => PlayFile("Radio.wav");

        // ── Copiloto ──────────────────────────────────────────────────────────

        public Task PlayCopilotBienvenidoAsync() =>
            PlayCopilotAsync("Bienvenido");

        public Task PlayCopilot10000PiesAscAsync() =>
            PlayCopilotAsync("10000 pies MAS");

        public Task PlayCopilot10000PiesDescAsync() =>
            PlayCopilotAsync("10000 pies FEM");

        public Task PlayCopilotAproximacionAsync() =>
            PlayCopilotAsync("Aproximacion");

        public Task PlayCopilotPerdidaAsync() =>
            PlayCopilotAsync("Perdida");

        private Task PlayCopilotAsync(string name)
        {
            var gender = VoiceFemale ? "FEM" : "MAS";
            var langPrefix = Language == "ESP" ? "ESP " : Language == "CHI" ? "CHI " : "";
            var fileName = $"{langPrefix}{name} {gender}.wav";
            var path = Path.Combine(_soundsDir, "Copiloto", fileName);
            if (!File.Exists(path))
            {
                // Fallback sin prefijo de idioma
                path = Path.Combine(_soundsDir, "Copiloto", $"{name} {gender}.wav");
            }
            return Task.Run(() => PlayFileAbsolute(path));
        }

        // ── Crew terrestre ────────────────────────────────────────────────────

        public Task PlayGroundBienvenidoAsync() =>
            PlayGroundAsync("Bienvenido 1");

        public Task PlayGroundBoardingAsync() =>
            PlayGroundAsync("Embarcando");

        public Task PlayGroundDoorClosedAsync() =>
            PlayGroundAsync("Puertas Cerradas");

        public Task PlayGroundEnginesAsync() =>
            PlayGroundAsync("Encendido de Motores");

        public Task PlayGroundArrivedAsync() =>
            PlayGroundAsync("Llegada 1");

        public Task PlayGroundDeboardAsync() =>
            PlayGroundAsync("Desembarcando");

        public Task PlayGroundHardLandingAsync() =>
            PlayGroundAsync("Aterrizaje duro");

        public Task PlayGroundNoLightsAsync() =>
            PlayGroundAsync("No luces");

        private Task PlayGroundAsync(string name)
        {
            var langPrefix = Language == "ESP" ? "ESP " : Language == "CHI" ? "CHI " : Language == "BRA" ? "BRA " : "";
            var fileName = $"{langPrefix}{name}.wav";
            var path = Path.Combine(_soundsDir, "Ground", fileName);
            if (!File.Exists(path))
                path = Path.Combine(_soundsDir, "Ground", $"{name}.wav");
            return Task.Run(() => PlayFileAbsolute(path));
        }

        // ── Checkride ─────────────────────────────────────────────────────────

        public void PlayCheckrideStart() => PlayGroundFile("Checkride", "Comenzar Checkride.wav");
        public void PlayCheckrideApproved() => PlayGroundFile("Checkride", "Aprobo.wav");
        public void PlayCheckrideRejected() => PlayGroundFile("Checkride", "No aprobo.wav");

        // ── Números ───────────────────────────────────────────────────────────

        public void PlayNumber(int digit)
        {
            if (digit < 0 || digit > 9) return;
            PlayFile($"{digit}.wav");
        }

        public void SpeakNumber(int number)
        {
            foreach (var ch in number.ToString())
            {
                if (ch == '.') PlayFile("point.wav");
                else if (char.IsDigit(ch)) PlayNumber(ch - '0');
            }
        }

        // ── Internos ──────────────────────────────────────────────────────────

        private void PlayGroundFile(string folder, string file)
        {
            var path = Path.Combine(_soundsDir, folder, file);
            PlayFileAbsolute(path);
        }

        private void PlayFile(string fileName)
        {
            var path = Path.Combine(_soundsDir, fileName);
            PlayFileAbsolute(path);
        }

        private void PlayFileAbsolute(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                _player?.Stop();
                _player?.Dispose();
                _player = new SoundPlayer(path);
                _player.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sound error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _player?.Stop();
                _player?.Dispose();
                _disposed = true;
            }
        }
    }
}
