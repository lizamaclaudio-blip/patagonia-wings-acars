#nullable enable

using System;

using System.Diagnostics;

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

        // Múltiples offsets de fuel para compatibilidad con diferentes aviones

        private Offset<double>? _fuelKg;      // 0x0B74 - Fuel total kg (estándar)

        private Offset<double>? _fuelLbs;     // 0x0B78 - Fuel total lbs (alternativo)

        private Offset<double>? _fuelCapacityKg; // 0x126C - Fuel capacity kg (A320 Headwind)



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

            Debug.WriteLine("[FSUIPC] Abriendo conexión FSUIPC7...");

            FSUIPCConnection.Open();

            Debug.WriteLine("[FSUIPC] Conexión FSUIPC7 abierta");

            InitOffsets();

            _hasReceivedData = false;

            _running = true;

            

            // Primera lectura para validar que hay datos del simulador

            Debug.WriteLine("[FSUIPC] Primera lectura de offsets...");

            FSUIPCConnection.Process();

            Debug.WriteLine($"[FSUIPC] Primera lectura - LAT={_lat?.Value:F6} LON={_lon?.Value:F6} ALT={_altM?.Value:F2}");

            if (_lat?.Value == 0 && _lon?.Value == 0 && _altM?.Value == 0)

            {

                throw new Exception("FSUIPC7 no detecta simulador.");

            }

            Debug.WriteLine("[FSUIPC] Simulador detectado - iniciando polling");

            

            _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "FSUIPC7-Poll" };

            _pollThread.Start();

        }



        private void InitOffsets()

        {

            _lat          = new Offset<double>(0x0560);  // Latitude (radians)

            _lon          = new Offset<double>(0x0568);  // Longitude (radians)

            _altM         = new Offset<double>(0x0570);  // Altitude (meters MSL)

            _hdg          = new Offset<double>(0x0578);  // Heading (radians, 0-2PI)

            _pitch        = new Offset<double>(0x0580);  // Pitch (radians)

            _bank         = new Offset<double>(0x0588);  // Bank (radians)



            _ias          = new Offset<int>(0x02BC);

            _gs           = new Offset<int>(0x02B4);

            _vs           = new Offset<int>(0x02C8);

            _groundAltFt  = new Offset<int>(0x0B4C);



            _onGround     = new Offset<short>(0x0366);

            _parkingBrake = new Offset<int>(0x0BC8);

            _autopilot    = new Offset<short>(0x07DC);  // AP Master (FSUIPC7/MSFS — 0x07D0 = Attitude Hold, 0x07DC = AP Master)

            _pause        = new Offset<short>(0x0264);



            _lights       = new Offset<short>(0x0D0C);

            _gear         = new Offset<int>(0x0BE8);

            _flaps        = new Offset<int>(0x0BDC);

            _seatBelt     = new Offset<short>(0x3B62);

            _noSmoking    = new Offset<short>(0x3B64);



            _n1Eng1       = new Offset<int>(0x0898);

            _n1Eng2       = new Offset<int>(0x0930);

            // Intentar múltiples offsets de fuel para compatibilidad con diferentes aviones

            _fuelKg       = new Offset<double>(0x0B74);   // Fuel total kg (estándar)

            _fuelLbs      = new Offset<double>(0x0B78);   // Fuel total lbs (fallback)

            _fuelCapacityKg = new Offset<double>(0x126C); // Fuel capacity kg (A320 Headwind)



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

                    

                    // Log valores raw para debug

                    Debug.WriteLine($"[FSUIPC POLL] LAT={_lat?.Value:F4} LON={_lon?.Value:F4} ALT={_altM?.Value:F0} IAS={_ias?.Value}");

                    

                    // Solo procesar si hay datos válidos del simulador

                    if (_lat?.Value == 0 && _lon?.Value == 0 && _altM?.Value == 0)

                    {

                        Debug.WriteLine("[FSUIPC POLL] Sin datos - esperando...");

                        Thread.Sleep(1000);

                        continue;

                    }

                    

                    Debug.WriteLine("[FSUIPC POLL] Datos detectados - procesando...");

                    var sd = BuildSimData();

                    Debug.WriteLine($"[FSUIPC POLL] Enviando datos - ALT={sd.AltitudeFeet:F0} FUEL={sd.FuelTotalLbs:F0}");



                    if (!_hasReceivedData)

                    {

                        _hasReceivedData = true;

                        IsConnected = true;

                        Debug.WriteLine("[FSUIPC POLL] Primera conexión - evento Connected");

                        Connected?.Invoke();

                    }



                    DataReceived?.Invoke(sd);

                    Debug.WriteLine("[FSUIPC POLL] Evento DataReceived enviado");

                    Thread.Sleep(1000);

                }

                catch (Exception)

                {

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

            double gs     = (_gs?.Value  ?? 0) / 128.0;  // GS en knots (offset * 128)

            double vs     = (_vs?.Value  ?? 0) / 256.0;

            double n1e1   = (_n1Eng1?.Value ?? 0) / 16384.0 * 100.0;

            double n1e2   = (_n1Eng2?.Value ?? 0) / 16384.0 * 100.0;

            

            // Intentar leer fuel de múltiples offsets (compatibilidad con A320 Headwind y otros)

            double fuelKg = 0;

            double fuelLbsDirect = _fuelLbs?.Value ?? 0;

            double fuelCapacity = _fuelCapacityKg?.Value ?? 0;

            double fuelStandard = _fuelKg?.Value ?? 0;

            

            // DEBUG: Mostrar valores raw de posición y fuel

            Debug.WriteLine($"[FSUIPC POS RAW] LAT={_lat?.Value:F6} LON={_lon?.Value:F6} ALT={_altM?.Value:F2}");

            Debug.WriteLine($"[FSUIPC FUEL RAW] 0x0B74={fuelStandard:F2} 0x0B78={fuelLbsDirect:F2} 0x126C={fuelCapacity:F2}");

            

            if (fuelStandard > 0.1)

            {

                fuelKg = fuelStandard;

                Debug.WriteLine($"[FSUIPC FUEL] Usando offset 0x0B74: {fuelKg:F2} kg");

            }

            else if (fuelLbsDirect > 0.1)

            {

                fuelKg = fuelLbsDirect * 0.453592;

                Debug.WriteLine($"[FSUIPC FUEL] Usando offset 0x0B78: {fuelKg:F2} kg (convertido de lbs)");

            }

            else if (fuelCapacity > 0.1)

            {

                fuelKg = fuelCapacity;

                Debug.WriteLine($"[FSUIPC FUEL] Usando offset 0x126C: {fuelKg:F2} kg (capacity)");

            }

            else

            {

                Debug.WriteLine("[FSUIPC FUEL] Ningún offset devolvió valor válido");

            }

            

            double qnh    = (_qnh?.Value ?? 0) / 16.0;

            int    gear   = _gear?.Value  ?? 0;

            int    flaps  = _flaps?.Value ?? 0;



            // Convertir radianes a grados

            double latDeg = (_lat?.Value ?? 0) * 180.0 / Math.PI;

            double lonDeg = (_lon?.Value ?? 0) * 180.0 / Math.PI;

            double hdgDeg = (_hdg?.Value ?? 0) * 180.0 / Math.PI;

            double pitchDeg = (_pitch?.Value ?? 0) * 180.0 / Math.PI;

            double bankDeg = (_bank?.Value ?? 0) * 180.0 / Math.PI;

            if (hdgDeg < 0) hdgDeg += 360;

            if (hdgDeg >= 360) hdgDeg -= 360;

            

            return new SimData

            {

                CapturedAtUtc     = DateTime.UtcNow,

                Latitude          = latDeg,

                Longitude         = lonDeg,

                AltitudeFeet      = altFt,

                AltitudeAGL       = aglFt,

                IndicatedAirspeed = ias,

                GroundSpeed       = gs,

                VerticalSpeed     = vs,

                Heading           = hdgDeg,

                Pitch             = pitchDeg,

                Bank              = bankDeg,

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

                FuelTotalLbs      = fuelKg,  // Contiene kg (nombre engañoso por compatibilidad)
                FuelKg            = fuelKg,  // kg directo desde FSUIPC (offset 0x0B74)
                TotalWeightLbs    = 0,
                TotalWeightKg     = 0,
                ZeroFuelWeightKg  = 0,

                FuelFlowLbsHour   = 0,

                OutsideTemperature = _oat?.Value       ?? 0,

                WindSpeed          = _windSpeed?.Value  ?? 0,

                WindDirection      = _windDir?.Value    ?? 0,

                QNH                = qnh,

                SeatBeltSign       = (_seatBelt?.Value  ?? 0) != 0,

                NoSmokingSign      = (_noSmoking?.Value ?? 0) != 0,

                TransponderCode    = DecodeBcd16(_xpdrCode?.Value ?? 0),

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



        /// <summary>
        /// Decodifica código de squawk desde formato BCD16 (usado por FSUIPC offset 0x0354).
        /// En BCD16, cada nibble de 4 bits representa un dígito octal: 7700 → 0x7700 = 30464 → 7700.
        /// </summary>
        private static int DecodeBcd16(short bcdValue)
        {
            var val = (int)(ushort)bcdValue; // tratar como unsigned
            int d3 = (val >> 12) & 0xF;
            int d2 = (val >> 8)  & 0xF;
            int d1 = (val >> 4)  & 0xF;
            int d0 =  val        & 0xF;
            // Validar dígitos octal (0-7)
            if (d3 > 7 || d2 > 7 || d1 > 7 || d0 > 7) return 0;
            return d3 * 1000 + d2 * 100 + d1 * 10 + d0;
        }

        public void Dispose()

        {

            if (_disposed) return;

            Disconnect();

            _disposed = true;

        }

    }

}

