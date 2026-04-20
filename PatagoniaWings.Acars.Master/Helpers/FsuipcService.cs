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

        private int _lastTraceXpdrRaw = int.MinValue;
        private int _lastTraceXpdrCode = int.MinValue;
        private int _lastTraceAutopilotComposite = int.MinValue;
        private int _lastTraceXpdrRawByte = int.MinValue;
        private int _lastTraceXpdrRawWord = int.MinValue;



        // ── Posición / Actitud (float64 nativo FSUIPC7) ──────────────────────

        private Offset<long>? _lat;

        private Offset<long>? _lon;

        private Offset<long>? _altM;       // unidades FSUIPC/FS

        private Offset<int>? _hdg;

        private Offset<int>? _pitch;

        private Offset<int>? _bank;



        // ── Velocidades (int32 estándar FSUIPC) ──────────────────────────────

        private Offset<int>? _ias;           // knots × 128

        private Offset<int>? _gs;            // m/s × 65536

        private Offset<int>? _vs;            // ft/min × 256

        private Offset<int>? _groundAltFt;   // ft × 65536 → para calcular AGL



        // ── Estado ────────────────────────────────────────────────────────────

        private Offset<short>? _onGround;

        private Offset<int>?   _parkingBrake;

        private Offset<int>?   _autopilotMaster;
        private Offset<int>?   _autopilotWingLeveler;
        private Offset<int>?   _autopilotNavLock;
        private Offset<int>?   _autopilotHeadingLock;
        private Offset<int>?   _autopilotAltitudeLock;
        private Offset<int>?   _autopilotGlideslopeHold;
        private Offset<int>?   _autopilotApproachHold;

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

        private Offset<ushort>? _xpdrCode;

        private Offset<byte>?   _xpdrModeByte;

        private Offset<short>?  _xpdrModeWord;



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

            Debug.WriteLine($"[FSUIPC] Primera lectura raw - LAT={_lat?.Value ?? 0} LON={_lon?.Value ?? 0} ALT={_altM?.Value ?? 0}");

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

            _lat          = new Offset<long>(0x0560);  // Latitude (FS units)

            _lon          = new Offset<long>(0x0568);  // Longitude (FS units)

            _altM         = new Offset<long>(0x0570);  // Altitude (FS units)

            _pitch        = new Offset<int>(0x0578);   // Pitch

            _bank         = new Offset<int>(0x057C);   // Bank

            _hdg          = new Offset<int>(0x0580);   // Heading true



            _ias          = new Offset<int>(0x02BC);

            _gs           = new Offset<int>(0x02B4);

            _vs           = new Offset<int>(0x02C8);

            _groundAltFt  = new Offset<int>(0x0B4C);



            _onGround     = new Offset<short>(0x0366);

            _parkingBrake = new Offset<int>(0x0BC8);

            _autopilotMaster        = new Offset<int>(0x07BC);  // AUTOPILOT MASTER
            _autopilotWingLeveler   = new Offset<int>(0x07C0);  // AUTOPILOT WING LEVELER
            _autopilotNavLock       = new Offset<int>(0x07C4);  // AUTOPILOT NAV1 LOCK
            _autopilotHeadingLock   = new Offset<int>(0x07C8);  // AUTOPILOT HEADING LOCK
            _autopilotAltitudeLock  = new Offset<int>(0x07D0);  // AUTOPILOT ALTITUDE LOCK
            _autopilotGlideslopeHold = new Offset<int>(0x07FC); // AUTOPILOT GLIDESLOPE HOLD
            _autopilotApproachHold  = new Offset<int>(0x0800);  // AUTOPILOT APPROACH HOLD

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



            _xpdrCode     = new Offset<ushort>(0x0354);

            _xpdrModeByte = new Offset<byte>(0x0B46);

            _xpdrModeWord = new Offset<short>(0x0B46);

        }



        private void PollLoop()

        {

            while (_running)

            {

                try

                {

                    FSUIPCConnection.Process();

                    

                    // Log valores raw para debug

                    Debug.WriteLine($"[FSUIPC POLL] LATraw={_lat?.Value ?? 0} LONraw={_lon?.Value ?? 0} ALTraw={_altM?.Value ?? 0} IAS={_ias?.Value}");

                    

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

            long latRaw   = _lat?.Value ?? 0L;
            long lonRaw   = _lon?.Value ?? 0L;
            long altRaw   = _altM?.Value ?? 0L;
            int pitchRaw  = _pitch?.Value ?? 0;
            int bankRaw   = _bank?.Value ?? 0;
            int hdgRaw    = _hdg?.Value ?? 0;

            double altM   = altRaw / (65536.0 * 65536.0);
            double altFt  = altM * 3.28084;

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

            Debug.WriteLine($"[FSUIPC POS RAW] LAT={latRaw} LON={lonRaw} ALT={altRaw}");

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



            // Convertir unidades FSUIPC/FS a grados reales

            double latDeg = latRaw * (90.0 / (10001750.0 * 65536.0 * 65536.0));

            double lonDeg = lonRaw * (360.0 / (65536.0 * 65536.0 * 65536.0 * 65536.0));

            double hdgDeg = hdgRaw * (360.0 / (65536.0 * 65536.0));

            double pitchDeg = pitchRaw * (360.0 / (65536.0 * 65536.0));

            double bankDeg = bankRaw * (360.0 / (65536.0 * 65536.0));

            if (hdgDeg < 0) hdgDeg += 360;

            if (hdgDeg >= 360) hdgDeg -= 360;

            

            int apMaster = _autopilotMaster?.Value ?? 0;
            int apWing = _autopilotWingLeveler?.Value ?? 0;
            int apNav = _autopilotNavLock?.Value ?? 0;
            int apHdg = _autopilotHeadingLock?.Value ?? 0;
            int apAlt = _autopilotAltitudeLock?.Value ?? 0;
            int apGs = _autopilotGlideslopeHold?.Value ?? 0;
            int apApp = _autopilotApproachHold?.Value ?? 0;

            bool autopilotOn =
                apMaster != 0 ||
                apWing != 0 ||
                apNav != 0 ||
                apHdg != 0 ||
                apAlt != 0 ||
                apGs != 0 ||
                apApp != 0;

            int autopilotComposite = autopilotOn ? 1 : 0;

            int xpdrModeRawByte = _xpdrModeByte?.Value ?? 0;
            int xpdrModeRawWord = _xpdrModeWord?.Value ?? 0;
            int xpdrModeRaw = ChooseXpdrModeRaw(xpdrModeRawByte, xpdrModeRawWord);
            int xpdrStateRaw = NormalizeXpdrState(xpdrModeRaw);
            int xpdrCode = DecodeBcd16(_xpdrCode?.Value ?? 0);

            if (autopilotComposite != _lastTraceAutopilotComposite
                || xpdrStateRaw != _lastTraceXpdrRaw
                || xpdrCode != _lastTraceXpdrCode
                || xpdrModeRawByte != _lastTraceXpdrRawByte
                || xpdrModeRawWord != _lastTraceXpdrRawWord)
            {
                _lastTraceAutopilotComposite = autopilotComposite;
                _lastTraceXpdrRaw = xpdrStateRaw;
                _lastTraceXpdrCode = xpdrCode;
                _lastTraceXpdrRawByte = xpdrModeRawByte;
                _lastTraceXpdrRawWord = xpdrModeRawWord;

                Debug.WriteLine("[FSUIPC AVIONICS] AP master=" + apMaster
                    + " wing=" + apWing
                    + " nav=" + apNav
                    + " hdg=" + apHdg
                    + " alt=" + apAlt
                    + " gs=" + apGs
                    + " app=" + apApp
                    + " final=" + (autopilotOn ? "ON" : "OFF")
                    + " | XPDR byte=" + xpdrModeRawByte
                    + " word=" + xpdrModeRawWord
                    + " chosen=" + xpdrModeRaw
                    + " norm=" + xpdrStateRaw
                    + " squawk=" + xpdrCode
                    + " final=" + (xpdrStateRaw >= 3 ? "ON" : "OFF"));
            }

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

                AutopilotActive   = autopilotOn,

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

                TransponderCode    = xpdrCode,
                TransponderStateRaw = xpdrStateRaw,
                TransponderCharlieMode = xpdrStateRaw >= 3,

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



        private static int NormalizeXpdrState(int rawState)
        {
            if (rawState < 0) return 0;
            if (rawState > 5) return 0;
            return rawState;
        }

        /// <summary>
        /// Decodifica código de squawk desde formato BCD16 (usado por FSUIPC offset 0x0354).
        /// En BCD16, cada nibble de 4 bits representa un dígito octal: 7700 → 0x7700 = 30464 → 7700.
        /// </summary>
        private static int ChooseXpdrModeRaw(int rawByte, int rawWord)
        {
            if (rawByte >= 0 && rawByte <= 5) return rawByte;
            if (rawWord >= 0 && rawWord <= 5) return rawWord;
            return 0;
        }

        private static int DecodeBcd16(ushort bcdValue)
        {
            var val = (int)bcdValue; // tratar como unsigned
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
