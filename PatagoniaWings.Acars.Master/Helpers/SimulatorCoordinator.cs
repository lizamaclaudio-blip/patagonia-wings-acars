#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.SimConnect;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// Intenta SimConnect primero (nativo MSFS — luces, datos, todo funciona correctamente).
    /// Si falla, usa FSUIPC7 como fallback.
    /// Solo marca conectado cuando entra telemetría real.
    /// </summary>
    public sealed class SimulatorCoordinator : IDisposable
    {
        private const int InitialTelemetryTimeoutSeconds = 6;
        private const int LiveTelemetryTimeoutSeconds = 4;

        private readonly object _sync = new object();
        private readonly string _logFile;

        private FsuipcService? _fsuipc;
        private SimConnectService? _simConnect;
        private Timer? _healthTimer;
        private bool _disposed;
        private bool _disconnectRaised;
        private DateTime _connectStartedUtc;
        private DateTime? _lastFrameUtc;
        private IntPtr _windowHandle;
        private int _consecutiveInvalidFrames;
        private const int MaxInvalidFramesBeforeFallback = 3;

        public bool IsConnected { get; private set; }
        public string ActiveBackend { get; private set; } = "None";

        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<SimData>? DataReceived;
        public event Action? Crashed;

        public SimulatorCoordinator(string logFile)
        {
            _logFile = logFile;
        }

        public void TryConnect(IntPtr hwnd)
        {
            StopHealthTimer();
            DisposeProviders();

            _windowHandle = hwnd;
            IsConnected = false;
            ActiveBackend = "None";
            _disconnectRaised = false;
            _connectStartedUtc = DateTime.UtcNow;
            _lastFrameUtc = null;
            _consecutiveInvalidFrames = 0;

            // ── 1. SimConnect primero: nativo MSFS, luces y datos completos ──────
            try
            {
                _simConnect = new SimConnectService();
                _simConnect.Connected += OnProviderConnected;
                _simConnect.Disconnected += OnProviderDisconnected;
                _simConnect.DataReceived += OnDataReceived;
                _simConnect.Crashed += OnProviderCrashed;
                _simConnect.Connect(hwnd);
                ActiveBackend = "SimConnect";
                WriteLog("Backend activo: SimConnect");
                StartHealthTimer();
                return;
            }
            catch (Exception ex)
            {
                WriteLog("SimConnect no disponible: " + ex.Message);
                DisposeSimConnect();
            }

            // ── 2. FSUIPC7 como fallback ──────────────────────────────────────────
            try
            {
                _fsuipc = new FsuipcService();
                _fsuipc.Connected += OnProviderConnected;
                _fsuipc.Disconnected += OnProviderDisconnected;
                _fsuipc.DataReceived += OnDataReceived;
                _fsuipc.Connect();
                ActiveBackend = "FSUIPC7";
                WriteLog("Backend activo: FSUIPC7 (fallback)");
                StartHealthTimer();
            }
            catch (Exception ex)
            {
                WriteLog("FSUIPC7 no disponible: " + ex.Message);
                DisposeFsuipc();
                ActiveBackend = "None";
                throw;
            }
        }

        private void OnProviderConnected()
        {
            Debug.WriteLine($"[Coordinator] Proveedor conectado: {ActiveBackend}");
            WriteLog("Handshake inicial con " + ActiveBackend);
        }

        private void OnProviderDisconnected()
        {
            ForceDisconnect("Proveedor desconectado: " + ActiveBackend);
        }

        private void OnProviderCrashed()
        {
            WriteLog("Crash detectado desde " + ActiveBackend);
            Crashed?.Invoke();
        }

        private void OnDataReceived(SimData data)
        {
            if (data == null)
            {
                return;
            }

            Debug.WriteLine($"[Coordinator] Datos recibidos de {ActiveBackend} - ALT={data.AltitudeFeet:F0} FUEL={data.FuelTotalLbs:F0}");

            // Validar que son datos reales del simulador.
            // Latitude != 0 es la señal más fiable: el sim nunca envía lat=0 para un avión cargado.
            // altValid acepta negativos para aeropuertos bajo nivel del mar (ej. Dead Sea, -1300 ft).
            bool latValid   = data.Latitude >= -90 && data.Latitude <= 90 && Math.Abs(data.Latitude) > 0.0001;
            bool altValid   = data.AltitudeFeet >= -2000 && data.AltitudeFeet < 100000;
            bool speedValid = data.IndicatedAirspeed >= 0 && data.IndicatedAirspeed < 1000;
            bool hasValidData = latValid && (altValid || speedValid);

            if (!hasValidData)
            {
                _consecutiveInvalidFrames++;
                Debug.WriteLine($"[Coordinator] Datos inválidos detectados - ignorando (frame {_consecutiveInvalidFrames}/{MaxInvalidFramesBeforeFallback}) LAT={data.Latitude:F2} ALT={data.AltitudeFeet:F0}");

                // Si SimConnect envía muchos frames inválidos, intentar FSUIPC7
                if (ActiveBackend == "SimConnect" && _consecutiveInvalidFrames >= MaxInvalidFramesBeforeFallback)
                {
                    Debug.WriteLine("[Coordinator] Demasiados frames inválidos de SimConnect - intentando FSUIPC7...");
                    WriteLog("SimConnect enviando datos vacíos - cambiando a FSUIPC7");
                    ForceDisconnect("SimConnect datos vacíos");
                    TryFsuipcFallback();
                }
                return;
            }

            _consecutiveInvalidFrames = 0;

            var capturedAt = data.CapturedAtUtc == default(DateTime)
                ? DateTime.UtcNow
                : data.CapturedAtUtc;

            var shouldRaiseConnected = false;
            lock (_sync)
            {
                _lastFrameUtc = capturedAt;
                if (!IsConnected)
                {
                    IsConnected = true;
                    _disconnectRaised = false;
                    shouldRaiseConnected = true;
                }
            }

            if (shouldRaiseConnected)
            {
                WriteLog("Telemetría real recibida desde " + ActiveBackend);
                Connected?.Invoke();
            }

            DataReceived?.Invoke(data);
        }

        private void StartHealthTimer()
        {
            StopHealthTimer();
            _healthTimer = new Timer(_ => HealthTick(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        private void StopHealthTimer()
        {
            _healthTimer?.Dispose();
            _healthTimer = null;
        }

        private void HealthTick()
        {
            if (_disposed) return;

            DateTime now = DateTime.UtcNow;
            bool shouldDisconnect = false;
            string reason = string.Empty;

            lock (_sync)
            {
                if (_disconnectRaised || string.IsNullOrWhiteSpace(ActiveBackend) || ActiveBackend == "None")
                    return;

                if (_lastFrameUtc.HasValue)
                {
                    if ((now - _lastFrameUtc.Value).TotalSeconds > LiveTelemetryTimeoutSeconds)
                    {
                        shouldDisconnect = true;
                        reason = "Telemetría expirada en " + ActiveBackend;
                    }
                }
                else if ((now - _connectStartedUtc).TotalSeconds > InitialTelemetryTimeoutSeconds)
                {
                    shouldDisconnect = true;
                    reason = "No llegó telemetría inicial desde " + ActiveBackend;
                }
            }

            if (shouldDisconnect)
            {
                // Si SimConnect no entregó datos válidos, intentar FSUIPC7 automáticamente
                if (ActiveBackend == "SimConnect" && reason.Contains("No llegó telemetría inicial"))
                {
                    Debug.WriteLine("[Coordinator] SimConnect sin datos - intentando FSUIPC7...");
                    WriteLog("SimConnect sin datos válidos - cambiando a FSUIPC7");
                    ForceDisconnect(reason);
                    TryFsuipcFallback();
                    return;
                }
                ForceDisconnect(reason);
            }
        }

        private void TryFsuipcFallback()
        {
            try
            {
                _fsuipc = new FsuipcService();
                _fsuipc.Connected += OnProviderConnected;
                _fsuipc.Disconnected += OnProviderDisconnected;
                _fsuipc.DataReceived += OnDataReceived;
                _fsuipc.Connect();
                ActiveBackend = "FSUIPC7";
                Debug.WriteLine("[Coordinator] FSUIPC7 fallback activado");
                WriteLog("Backend activo: FSUIPC7 (fallback)");
                StartHealthTimer();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Coordinator] FSUIPC7 fallback falló: {ex.Message}");
                WriteLog("FSUIPC7 fallback falló: " + ex.Message);
                DisposeFsuipc();
                ActiveBackend = "None";
            }
        }

        private void ForceDisconnect(string reason)
        {
            bool shouldRaise;
            lock (_sync)
            {
                if (_disconnectRaised) return;

                _disconnectRaised = true;
                shouldRaise = IsConnected || !string.IsNullOrWhiteSpace(ActiveBackend);
                IsConnected = false;
                _lastFrameUtc = null;
            }

            WriteLog(reason);
            StopHealthTimer();
            DisposeProviders();

            if (shouldRaise)
            {
                Disconnected?.Invoke();
            }
        }

        private void DisposeProviders()
        {
            DisposeFsuipc();
            DisposeSimConnect();
        }

        private void DisposeFsuipc()
        {
            if (_fsuipc == null) return;

            _fsuipc.Connected   -= OnProviderConnected;
            _fsuipc.Disconnected -= OnProviderDisconnected;
            _fsuipc.DataReceived -= OnDataReceived;
            _fsuipc.Dispose();
            _fsuipc = null;
        }

        private void DisposeSimConnect()
        {
            if (_simConnect == null) return;

            _simConnect.Connected    -= OnProviderConnected;
            _simConnect.Disconnected -= OnProviderDisconnected;
            _simConnect.DataReceived -= OnDataReceived;
            _simConnect.Crashed -= OnProviderCrashed;
            _simConnect.Dispose();
            _simConnect = null;
        }

        public void Dispose()
        {
            if (_disposed) return;

            StopHealthTimer();
            DisposeProviders();
            _disposed = true;
            IsConnected = false;
            ActiveBackend = "None";
        }

        private void WriteLog(string msg)
        {
            try
            {
                File.AppendAllText(_logFile,
                    "[" + DateTime.UtcNow.ToString("o") + "] " + msg + Environment.NewLine);
            }
            catch { }
        }
    }
}
