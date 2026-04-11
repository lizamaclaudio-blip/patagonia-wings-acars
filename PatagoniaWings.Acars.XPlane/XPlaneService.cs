using System;
using System.Threading;
using FSUIPC;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.XPlane
{
    /// <summary>
    /// Servicio de integración con X-Plane 11/12 via FSUIPC/XPUIPC.
    /// </summary>
    public class XPlaneService : IDisposable
    {
        private Thread? _pollingThread;
        private volatile bool _running;
        private bool _disposed;

        // Offsets FSUIPC para X-Plane
        private Offset<double>? _latitude;
        private Offset<double>? _longitude;
        private Offset<double>? _altitude;
        private Offset<double>? _altitudeAGL;
        private Offset<double>? _ias;
        private Offset<double>? _gs;
        private Offset<double>? _vs;
        private Offset<double>? _heading;
        private Offset<double>? _pitch;
        private Offset<double>? _bank;
        private Offset<int>? _onGround;
        private Offset<int>? _parkingBrake;
        private Offset<int>? _autopilot;
        private Offset<double>? _fuelTotal;
        private Offset<double>? _oat;
        private Offset<double>? _windSpeed;
        private Offset<double>? _windDir;
        private Offset<double>? _qnh;

        public bool IsConnected { get; private set; }
        public SimulatorType DetectedSimulator { get; private set; }

        public event Action? Connected;
        public event Action? Disconnected;
        public event Action<SimData>? DataReceived;
#pragma warning disable CS0067
        public event Action<bool>? PauseChanged;
#pragma warning restore CS0067

        public void Connect()
        {
            if (IsConnected) return;

            try
            {
                FSUIPCConnection.Open();
                InitOffsets();
                IsConnected = true;

                // Detectar si es XP11 o XP12 por la versión de FSUIPC
                DetectedSimulator = SimulatorType.XPlane12;
                Connected?.Invoke();

                _running = true;
                _pollingThread = new Thread(PollLoop) { IsBackground = true, Name = "XPlane-Poll" };
                _pollingThread.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FSUIPC error: {ex.Message}");
                IsConnected = false;
            }
        }

        private void InitOffsets()
        {
            _latitude = new Offset<double>(0x0560);
            _longitude = new Offset<double>(0x0568);
            _altitude = new Offset<double>(0x0570);
            _altitudeAGL = new Offset<double>(0x0020);
            _ias = new Offset<double>(0x02BC);
            _gs = new Offset<double>(0x02B4);
            _vs = new Offset<double>(0x02C8);
            _heading = new Offset<double>(0x0580);
            _pitch = new Offset<double>(0x0578);
            _bank = new Offset<double>(0x057C);
            _onGround = new Offset<int>(0x0366);
            _parkingBrake = new Offset<int>(0x0BC8);
            _autopilot = new Offset<int>(0x07D0);
            _fuelTotal = new Offset<double>(0x126C);
            _oat = new Offset<double>(0x0E8C);
            _windSpeed = new Offset<double>(0x0E90);
            _windDir = new Offset<double>(0x0E92);
            _qnh = new Offset<double>(0x0330);
        }

        private void PollLoop()
        {
            while (_running && IsConnected)
            {
                try
                {
                    FSUIPCConnection.Process();
                    var simData = ReadSimData();
                    DataReceived?.Invoke(simData);
                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"FSUIPC poll error: {ex.Message}");
                    IsConnected = false;
                    Disconnected?.Invoke();
                    break;
                }
            }
        }

        private SimData ReadSimData()
        {
            return new SimData
            {
                Latitude = _latitude?.Value ?? 0,
                Longitude = _longitude?.Value ?? 0,
                AltitudeFeet = (_altitude?.Value ?? 0) * 3.28084,
                AltitudeAGL = (_altitudeAGL?.Value ?? 0) * 3.28084,
                IndicatedAirspeed = (_ias?.Value ?? 0) / 128.0,
                GroundSpeed = (_gs?.Value ?? 0) / 65536.0 * 1.94384,
                VerticalSpeed = (_vs?.Value ?? 0) * 60.0 * 3.28084,
                Heading = (_heading?.Value ?? 0) * 360.0 / 65536.0,
                Pitch = (_pitch?.Value ?? 0) * 360.0 / 65536.0,
                Bank = (_bank?.Value ?? 0) * 360.0 / 65536.0,
                OnGround = (_onGround?.Value ?? 0) != 0,
                ParkingBrake = (_parkingBrake?.Value ?? 0) != 0,
                AutopilotActive = (_autopilot?.Value ?? 0) != 0,
                FuelTotalLbs = _fuelTotal?.Value ?? 0,
                OutsideTemperature = _oat?.Value ?? 0,
                WindSpeed = _windSpeed?.Value ?? 0,
                WindDirection = _windDir?.Value ?? 0,
                QNH = _qnh?.Value ?? 0,
                SimulatorType = DetectedSimulator,
                IsConnected = true
            };
        }

        public void Disconnect()
        {
            _running = false;
            if (IsConnected)
            {
                try { FSUIPCConnection.Close(); }
                catch { }
                IsConnected = false;
                Disconnected?.Invoke();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _disposed = true;
            }
        }
    }
}
