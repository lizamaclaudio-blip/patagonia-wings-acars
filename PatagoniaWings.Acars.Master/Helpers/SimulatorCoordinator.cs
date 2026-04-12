#nullable enable
using System;
using System.IO;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.SimConnect;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// Intenta FSUIPC7 primero. Si falla, usa SimConnect como fallback.
    /// No emite Connected hasta recibir el primer frame real.
    /// </summary>
    public sealed class SimulatorCoordinator : IDisposable
    {
        private FsuipcService?    _fsuipc;
        private SimConnectService? _simConnect;
        private bool _disposed;

        private readonly string _logFile;

        public bool IsConnected { get; private set; }
        public string ActiveBackend { get; private set; } = "None";

        public event Action?          Connected;
        public event Action?          Disconnected;
        public event Action<SimData>? DataReceived;

        public SimulatorCoordinator(string logFile)
        {
            _logFile = logFile;
        }

        // hwnd solo necesario para SimConnect
        public void TryConnect(IntPtr hwnd)
        {
            DisposeProviders();
            IsConnected = false;
            ActiveBackend = "None";

            // ── Intento 1: FSUIPC7 ──────────────────────────────────────────
            try
            {
                _fsuipc = new FsuipcService();
                _fsuipc.Connected    += OnProviderConnected;
                _fsuipc.Disconnected += OnProviderDisconnected;
                _fsuipc.DataReceived += OnDataReceived;
                _fsuipc.Connect();
                ActiveBackend = "FSUIPC7";
                WriteLog("Backend activo: FSUIPC7");
                return;
            }
            catch (Exception ex)
            {
                WriteLog("FSUIPC7 no disponible: " + ex.Message);
                _fsuipc?.Dispose();
                _fsuipc = null;
            }

            // ── Intento 2: SimConnect (fallback) ────────────────────────────
            try
            {
                _simConnect = new SimConnectService();
                _simConnect.Connected    += OnProviderConnected;
                _simConnect.Disconnected += OnProviderDisconnected;
                _simConnect.DataReceived += OnDataReceived;
                _simConnect.Connect(hwnd);
                ActiveBackend = "SimConnect";
                WriteLog("Backend activo: SimConnect");
            }
            catch (Exception ex)
            {
                WriteLog("SimConnect no disponible: " + ex.Message);
                _simConnect?.Dispose();
                _simConnect = null;
                throw;     // ambos fallaron → el llamador maneja el error
            }
        }

        private void OnProviderConnected()
        {
            IsConnected = true;
            Connected?.Invoke();
        }

        private void OnProviderDisconnected()
        {
            IsConnected = false;
            WriteLog("Desconectado de " + ActiveBackend);
            Disconnected?.Invoke();
        }

        private void OnDataReceived(SimData data)
        {
            DataReceived?.Invoke(data);
        }

        private void DisposeProviders()
        {
            _fsuipc?.Dispose();
            _fsuipc = null;
            _simConnect?.Dispose();
            _simConnect = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            DisposeProviders();
            _disposed = true;
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
