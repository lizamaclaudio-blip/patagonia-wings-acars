using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Master.Helpers
{
    public sealed class AcarsRuntimeState : INotifyPropertyChanged
    {
        private const int FreshTelemetryThresholdSeconds = 3;

        private Pilot? _currentPilot;
        private AcarsReadyFlight? _currentReadyFlight;
        private PreparedDispatch? _currentDispatch;
        private SimData? _lastTelemetry;
        private bool _isSimulatorConnected;
        private SimulatorType _simulatorType = SimulatorType.None;
        private string _simulatorBackend = string.Empty;
        private DateTime? _lastTelemetryUtc;

        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action? Changed;

        public Pilot? CurrentPilot => _currentPilot;
        public AcarsReadyFlight? CurrentReadyFlight => _currentReadyFlight;
        public PreparedDispatch? CurrentDispatch => _currentDispatch;
        public SimData? LastTelemetry => _lastTelemetry;
        public bool IsSimulatorConnected => _isSimulatorConnected && IsTelemetryFresh();
        public bool HasTelemetry => _lastTelemetry != null && _lastTelemetryUtc.HasValue;
        public SimulatorType SimulatorType => _simulatorType;
        public string SimulatorBackend => _simulatorBackend;
        public DateTime? LastTelemetryUtc => _lastTelemetryUtc;
        public int TelemetryFreshnessThresholdSeconds => FreshTelemetryThresholdSeconds;
        public TimeSpan? TelemetryAge => _lastTelemetryUtc.HasValue ? DateTime.UtcNow - _lastTelemetryUtc.Value : (TimeSpan?)null;

        public void SetCurrentPilot(Pilot? pilot)
        {
            _currentPilot = pilot;
            NotifyAll();
        }

        public void SetReadyFlight(AcarsReadyFlight? readyFlight)
        {
            _currentReadyFlight = readyFlight;
            _currentDispatch = readyFlight != null ? readyFlight.ToPreparedDispatch() : null;
            NotifyAll();
        }

        public void SetPreparedDispatch(PreparedDispatch? dispatch)
        {
            _currentDispatch = dispatch;
            if (dispatch == null)
            {
                _currentReadyFlight = null;
            }
            NotifyAll();
        }

        public void ClearDispatch()
        {
            _currentReadyFlight = null;
            _currentDispatch = null;
            NotifyAll();
        }

        public void SetSimulatorWaiting(string backend, SimulatorType simulatorType)
        {
            _simulatorBackend = backend ?? string.Empty;
            _simulatorType = simulatorType;
            _isSimulatorConnected = false;
            _lastTelemetry = null;
            _lastTelemetryUtc = null;
            NotifyAll();
        }

        public void SetSimulatorDisconnected(string backend)
        {
            _simulatorBackend = backend ?? _simulatorBackend;
            _simulatorType = SimulatorType.None;
            _isSimulatorConnected = false;
            _lastTelemetry = null;
            _lastTelemetryUtc = null;
            NotifyAll();
        }

        public void SetTelemetry(SimData data, string backend)
        {
            if (data == null)
            {
                return;
            }

            if (data.CapturedAtUtc == default(DateTime))
            {
                data.CapturedAtUtc = DateTime.UtcNow;
            }

            _lastTelemetry = data;
            _lastTelemetryUtc = data.CapturedAtUtc;
            _simulatorBackend = backend ?? string.Empty;
            _simulatorType = data.SimulatorType;
            _isSimulatorConnected = true;
            NotifyAll();
        }

        public bool IsTelemetryFresh(int thresholdSeconds = FreshTelemetryThresholdSeconds)
        {
            if (!_isSimulatorConnected || !_lastTelemetryUtc.HasValue)
            {
                return false;
            }

            return (DateTime.UtcNow - _lastTelemetryUtc.Value).TotalSeconds <= thresholdSeconds;
        }

        public string GetTelemetryAgeText()
        {
            var age = TelemetryAge;
            if (!age.HasValue)
            {
                return "sin datos";
            }

            if (age.Value.TotalSeconds < 1)
            {
                return "ahora";
            }

            if (age.Value.TotalMinutes < 1)
            {
                return string.Format("hace {0:F1}s", age.Value.TotalSeconds);
            }

            return string.Format("hace {0:F1}m", age.Value.TotalMinutes);
        }

        public void ResetAll()
        {
            _currentPilot = null;
            _currentReadyFlight = null;
            _currentDispatch = null;
            _lastTelemetry = null;
            _isSimulatorConnected = false;
            _simulatorType = SimulatorType.None;
            _simulatorBackend = string.Empty;
            _lastTelemetryUtc = null;
            NotifyAll();
        }

        private void NotifyAll()
        {
            OnPropertyChanged(nameof(CurrentPilot));
            OnPropertyChanged(nameof(CurrentReadyFlight));
            OnPropertyChanged(nameof(CurrentDispatch));
            OnPropertyChanged(nameof(LastTelemetry));
            OnPropertyChanged(nameof(IsSimulatorConnected));
            OnPropertyChanged(nameof(HasTelemetry));
            OnPropertyChanged(nameof(SimulatorType));
            OnPropertyChanged(nameof(SimulatorBackend));
            OnPropertyChanged(nameof(LastTelemetryUtc));
            OnPropertyChanged(nameof(TelemetryFreshnessThresholdSeconds));
            OnPropertyChanged(nameof(TelemetryAge));
            Changed?.Invoke();
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
