#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using FSUIPC;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// Telemetría MSFS 2020/2024 via FSUIPC7. Backend principal.
    /// </summary>
    public sealed class FsuipcService : IDisposable
    {
        private Thread? _pollThread;
        private volatile bool _running;
        private bool _disposed;
        private bool _hasReceivedData;

        // ── Posición / Actitud (float64 nativo FSUIPC7) ──────────────────────
        private Offset<double>? _lat;
        private Offset<double>? _lon;
        private Offset<double>? _altM;       // metros MSL
        private Offset<double>? _hdg;
        private Offset<double>? _pitch;
        private Offset<double>? _bank;

        // ── Velocidades (int32 estándar FSUIPC) ──────────────────────────────
        private Offset<int>? _ias;           // knots × 128
        private Offset<int>? _gs;            // m/s × 65536
        private Offset<int>? _vs;            // ft/min × 256
        private Offset<int>? _groundAltFt;   // ft × 65536 → para calcular AGL

        // ── Estado ────────────────────────────────────────────────────────────
        private Offset<short>? _onGround;
        private Offset<int>?   _parkingBrake;
        private Offset<short>? _autopilot;
        private Offset<short>? _pause;

        // ── Sistemas ──────────────────────────────────────────────────────────
        private Offset<short>? _lights;      // bitmask: 0=nav 1=beacon 2=ldg 3=taxi 4=strobe
        private Offset<int>?   _gear;        // 0=up  16383=down
        private Offset<int>?   _flaps;       // 0-16383
        private Offset<short>? _seatBelt;
        private Offset<short>? _noSmoking;

        // ── Motores / Combustible ─────────────────────────────────────────────
        private Offset<int>?    _n1Eng1;     // % × 16384
        private Offset<int>?    _n1Eng2;
        private Offset<double>? _fuelKg;

        // ── Ambiente ──────────────────────────────────────────────────────────
        private Offset<double>? _oat;
        private Offset<double>? _windSpeed;
        private Offset<double>? _windDir;
        private Offset<short>?  _qnh;        // mb × 16

        // ── Aviónica ──────────────────────────────────────────────────────────
        private Offset<short>? _xpdrCode;
        private Offset<short>? _xpdrMode;

        public bool IsConnected { get; private set; }

        public event Action?          Connected;
        public event Action?          Disconnected;
        public event Action<SimData>? DataReceived;

        // ─────────────────────────────────────────────────────────────────────

        public void Connect()
        {
            if (IsConnected) return;
            FSUIPCConnection.Open();           // lanza FSUIPCException si FSUIPC7 no está disponible
            InitOffsets();
            _hasReceivedData = false;
            _running = true;
            _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "FSUIPC7-Poll" };
            _pollThread.Start();
        }

        private void InitOffsets()
        {
            _lat          = new Offset<double>(0x0560);
            _lon          = new Offset<double>(0x0568);
            _altM         = new Offset<double>(0x0570);
            _hdg          = new Offset<double>(0x0580);
            _pitch        = new Offset<double>(0x0578);
            _bank         = new Offset<double>(0x057C);

            _ias          = new Offset<int>(0x02BC);
            _gs           = new Offset<int>(0x02B4);
            _vs           = new Offset<int>(0x02C8);
            _groundAltFt  = new Offset<int>(0x0B4C);

            _onGround     = new Offset<short>(0x0366);
            _parkingBrake = new Offset<int>(0x0BC8);
            _autopilot    = new Offset<short>(0x07D0);
            _pause        = new Offset<short>(0x0264);

            _lights       = new Offset<short>(0x0D0C);
            _gear         = new Offset<int>(0x0BE8);
            _flaps        = new Offset<int>(0x0BDC);
            _seatBelt     = new Offset<short>(0x3B62);
            _noSmoking    = new Offset<short>(0x3B64);

            _n1Eng1       = new Offset<int>(0x0898);
            _n1Eng2       = new Offset<int>(0x0930);
            _fuelKg       = new Offset<double>(0x126C);

            _oat          = new Offset<double>(0x0E8C);
            _windSpeed    = new Offset<double>(0x0E90);
            _windDir      = new Offset<double>(0x0E92);
            _qnh          = new Offset<short>(0x0330);

            _xpdrCode     = new Offset<short>(0x0354);
            _xpdrMode     = new Offset<short>(0x0C3A);
        }

        private void PollLoop()
        {
            while (_running)
            {
                try
                {
                    FSUIPCConnection.Process();
                    var sd = BuildSimData();

                    if (!_hasReceivedData)
                    {
                        _hasReceivedData = true;
                        IsConnected = true;
                        Connected?.Invoke();
                    }

                    DataReceived?.Invoke(sd);
                    Thread.Sleep(1000);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("FSUIPC7 poll error: " + ex.Message);
                    var wasConnected = IsConnected;
                    IsConnected = false;
                    _running = false;
                    if (wasConnected) Disconnected?.Invoke();
                    break;
                }
            }
        }

        private SimData BuildSimData()
        {
            short lights  = _lights?.Value  ?? 0;
            double altFt  = (_altM?.Value ?? 0) * 3.28084;
            double gndFt  = (_groundAltFt?.Value ?? 0) / 65536.0;
            double aglFt  = Math.Max(0, altFt - gndFt);
            double ias    = (_ias?.Value ?? 0) / 128.0;
            double gs     = (_gs?.Value  ?? 0) / 65536.0 * 1.94384;
            double vs     = (_vs?.Value  ?? 0) / 256.0;
            double n1e1   = (_n1Eng1?.Value ?? 0) / 16384.0 * 100.0;
            double n1e2   = (_n1Eng2?.Value ?? 0) / 16384.0 * 100.0;
            double fuelLbs = (_fuelKg?.Value ?? 0) * 2.20462;
            double qnh    = (_qnh?.Value ?? 0) / 16.0;
            int    gear   = _gear?.Value  ?? 0;
            int    flaps  = _flaps?.Value ?? 0;

            return new SimData
            {
                CapturedAtUtc     = DateTime.UtcNow,
                Latitude          = _lat?.Value   ?? 0,
                Longitude         = _lon?.Value   ?? 0,
                AltitudeFeet      = altFt,
                AltitudeAGL       = aglFt,
                IndicatedAirspeed = ias,
                GroundSpeed       = gs,
                VerticalSpeed     = vs,
                Heading           = _hdg?.Value   ?? 0,
                Pitch             = _pitch?.Value ?? 0,
                Bank              = _bank?.Value  ?? 0,
                OnGround          = (_onGround?.Value    ?? 0) != 0,
                ParkingBrake      = (_parkingBrake?.Value ?? 0) != 0,
                AutopilotActive   = (_autopilot?.Value   ?? 0) != 0,
                Pause             = (_pause?.Value       ?? 0) != 0,
                StrobeLightsOn    = (lights & (1 << 4)) != 0,
                BeaconLightsOn    = (lights & (1 << 1)) != 0,
                LandingLightsOn   = (lights & (1 << 2)) != 0,
                TaxiLightsOn      = (lights & (1 << 3)) != 0,
                NavLightsOn       = (lights & (1 << 0)) != 0,
                GearDown          = gear > 8000,
                GearTransitioning = gear > 100 && gear < 8000,
                FlapsDeployed     = flaps > 500,
                FlapsPercent      = flaps / 16383.0 * 100.0,
                Engine1N1         = n1e1,
                Engine2N1         = n1e2,
                FuelTotalLbs      = fuelLbs,
                FuelFlowLbsHour   = 0,
                OutsideTemperature = _oat?.Value       ?? 0,
                WindSpeed          = _windSpeed?.Value  ?? 0,
                WindDirection      = _windDir?.Value    ?? 0,
                QNH                = qnh,
                SeatBeltSign       = (_seatBelt?.Value  ?? 0) != 0,
                NoSmokingSign      = (_noSmoking?.Value ?? 0) != 0,
                TransponderCode    = _xpdrCode?.Value   ?? 0,
                TransponderCharlieMode = (_xpdrMode?.Value ?? 0) >= 3,
                SimulatorType      = SimulatorType.MSFS2020,
                IsConnected        = true
            };
        }

        public void Disconnect()
        {
            _running = false;
            var wasConnected = IsConnected;
            IsConnected = false;
            try { FSUIPCConnection.Close(); } catch { }
            if (wasConnected) Disconnected?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            Disconnect();
            _disposed = true;
        }
    }
}
