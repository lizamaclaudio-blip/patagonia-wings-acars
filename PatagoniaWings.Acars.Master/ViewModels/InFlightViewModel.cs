using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public class InFlightViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private double _altitude;
        private double _altitudeAgl;
        private double _ias;
        private double _gs;
        private double _vs;
        private double _heading;
        private double _fuelLbs;
        private double _fuelLeftTank;
        private double _fuelRightTank;
        private double _fuelCenterTank;
        private double _fuelCapacity;
        private double _n1Eng1;
        private double _n1Eng2;
        private double _oat;
        private double _windSpeed;
        private double _windDir;
        private double _lat;
        private double _lon;
        private string _aircraftTitle = string.Empty;
        private string _aircraftStatus = string.Empty;
        private bool _requiresLvars;
        private bool _autopilotOn;
        private bool _onGround;
        private bool _strobeOn;
        private bool _beaconOn;
        private bool _landingOn;
        private bool _taxiOn;
        private bool _navOn;
        private bool _seatBeltSign;
        private bool _noSmokingSign;
        private bool _gearDown;
        private bool _gearTransitioning;
        private double _flapsPercent;
        private bool _spoilersArmed;
        private bool _reverserActive;
        private bool _charlieMode;
        private int _squawk;
        private bool _apuRunning;
        private bool _apuAvailable;
        private bool _bleedAirOn;
        private bool _hasPressurization;
        private bool _hasApu;
        private bool _isSingleEngine;
        private double _cabinAlt;
        private double _pressDiff;
        private FlightPhase _phase = FlightPhase.Disconnected;
        private string _phaseLabel = "Desconectado";
        private string _elapsedTime = "00:00:00";
        private DateTime _startTime;
        private bool _alertNoStrobe;
        private bool _alertPause;
        private bool _above10000;

        public double Altitude    { get => _altitude;    set { if (SetField(ref _altitude,    value)) OnPropertyChanged(nameof(AltitudeDisplay)); } }
        public double AltitudeAGL { get => _altitudeAgl; set { if (SetField(ref _altitudeAgl, value)) OnPropertyChanged(nameof(AglDisplay)); } }
        public double IAS { get => _ias; set { if (SetField(ref _ias, value)) { OnPropertyChanged(nameof(IasDisplay)); OnPropertyChanged(nameof(IASDisplay)); } } }
        public double GS { get => _gs; set { if (SetField(ref _gs, value)) { OnPropertyChanged(nameof(GsDisplay)); OnPropertyChanged(nameof(GSDisplay)); } } }
        public double VS { get => _vs; set { if (SetField(ref _vs, value)) { OnPropertyChanged(nameof(VsDisplay)); OnPropertyChanged(nameof(VSDisplay)); } } }
        public double Heading { get => _heading; set { if (SetField(ref _heading, value)) OnPropertyChanged(nameof(HeadingDisplay)); } }
        public double FuelLbs { get => _fuelLbs; set { if (SetField(ref _fuelLbs, value)) { OnPropertyChanged(nameof(FuelKg)); OnPropertyChanged(nameof(FuelDisplay)); OnPropertyChanged(nameof(FuelKgDisplay)); OnPropertyChanged(nameof(FuelLbsDisplay)); } } }
        public double FuelKg => Math.Round(FuelLbs, 0);  // FuelTotalLbs contiene kg desde FSUIPC
        
        // Propiedades de tanques individuales (visibles para diagnóstico de aviones complejos)
        public double FuelLeftTank { get => _fuelLeftTank; set { if (SetField(ref _fuelLeftTank, value)) OnPropertyChanged(nameof(FuelLeftTankDisplay)); } }
        public double FuelRightTank { get => _fuelRightTank; set { if (SetField(ref _fuelRightTank, value)) OnPropertyChanged(nameof(FuelRightTankDisplay)); } }
        public double FuelCenterTank { get => _fuelCenterTank; set { if (SetField(ref _fuelCenterTank, value)) OnPropertyChanged(nameof(FuelCenterTankDisplay)); } }
        public double FuelCapacity { get => _fuelCapacity; set { if (SetField(ref _fuelCapacity, value)) OnPropertyChanged(nameof(FuelCapacityDisplay)); } }
        
        public string FuelLeftTankDisplay => HasLiveTelemetry ? $"{Math.Round(FuelLeftTank, 0):F0}" : "---";
        public string FuelRightTankDisplay => HasLiveTelemetry ? $"{Math.Round(FuelRightTank, 0):F0}" : "---";
        public string FuelCenterTankDisplay => HasLiveTelemetry ? $"{Math.Round(FuelCenterTank, 0):F0}" : "---";
        public string FuelCapacityDisplay => HasLiveTelemetry ? $"{Math.Round(FuelCapacity, 0):F0}" : "---";
        public double N1Eng1 { get => _n1Eng1; set { if (SetField(ref _n1Eng1, value)) OnPropertyChanged(nameof(N1Eng1Display)); } }
        public double N1Eng2 { get => _n1Eng2; set { if (SetField(ref _n1Eng2, value)) OnPropertyChanged(nameof(N1Eng2Display)); } }
        public double OAT { get => _oat; set => SetField(ref _oat, value); }
        public double WindSpeed { get => _windSpeed; set => SetField(ref _windSpeed, value); }
        public double WindDir { get => _windDir; set => SetField(ref _windDir, value); }
        public double Lat { get => _lat; set => SetField(ref _lat, value); }
        public double Lon { get => _lon; set => SetField(ref _lon, value); }
        public string AircraftTitle { get => _aircraftTitle; set { if (SetField(ref _aircraftTitle, value)) { DetectAircraftType(value); OnPropertyChanged(nameof(AircraftStatusDisplay)); OnPropertyChanged(nameof(ShowDoors)); OnPropertyChanged(nameof(AircraftImageInitials)); } } }

        /// <summary>
        /// Iniciales para mostrar en el placeholder de foto de aeronave.
        /// Ej: "A320" → "A32", "B738" → "B73", "C208" → "C20"
        /// </summary>
        public string AircraftImageInitials
        {
            get
            {
                if (!HasLiveTelemetry || string.IsNullOrWhiteSpace(_aircraftTitle)) return "---";
                // Tomar los primeros 4 caracteres del título en mayúsculas, eliminando espacios
                var t = _aircraftTitle.ToUpperInvariant();
                // Intentar extraer el tipo ICAO del título
                var tokens = new[] { "A318","A319","A320","A321","A330","A350","A380",
                    "B737","B738","B739","B747","B757","B767","B777","B787","B78X",
                    "CRJ","E170","E175","E190","E195","ATR","DHC","Q400","TBM",
                    "PC12","C208","C172","C152","SR22","DA40","BE58","B350" };
                foreach (var tok in tokens)
                    if (t.Contains(tok)) return tok;
                return t.Length >= 4 ? t.Substring(0, 4) : t;
            }
        }

        public bool ShowDoors
        {
            get
            {
                var t = (_aircraftTitle ?? string.Empty).ToUpperInvariant();
                return t.Contains("A319") || t.Contains("A320") || t.Contains("A321") ||
                       t.Contains("B737") || t.Contains("B738") || t.Contains("B777") || t.Contains("B787") || t.Contains("B78X") ||
                       t.Contains("CRJ") || t.Contains("E170") || t.Contains("E175") || t.Contains("E190") ||
                       t.Contains("ATR") || t.Contains("DHC-8") || t.Contains("DASH 8") || t.Contains("DASH8") ||
                       t.Contains("PMDG") || t.Contains("FENIX") || t.Contains("HEADWIND") || t.Contains("INIBUILDS") ||
                       t.Contains("FLYBYWIRE") || t.Contains("ACJ") || t.Contains("BBJ");
            }
        }
        public string AircraftStatus { get => _aircraftStatus; set => SetField(ref _aircraftStatus, value); }
        public bool RequiresLvars { get => _requiresLvars; set => SetField(ref _requiresLvars, value); }
        public string AircraftStatusDisplay => GetAircraftStatusDisplay();
        
        private string GetAircraftStatusDisplay()
        {
            if (!HasLiveTelemetry) return "Sin conexión";
            return AircraftTitle;
        }
        public bool AutopilotOn { get => _autopilotOn; set => SetField(ref _autopilotOn, value); }
        public bool OnGround { get => _onGround; set => SetField(ref _onGround, value); }
        public bool StrobeOn { get => _strobeOn; set => SetField(ref _strobeOn, value); }
        public bool BeaconOn { get => _beaconOn; set => SetField(ref _beaconOn, value); }
        public bool LandingOn { get => _landingOn; set => SetField(ref _landingOn, value); }
        public bool TaxiOn { get => _taxiOn; set => SetField(ref _taxiOn, value); }
        public bool NavOn { get => _navOn; set => SetField(ref _navOn, value); }
        public bool SeatBeltSign { get => _seatBeltSign; set => SetField(ref _seatBeltSign, value); }
        public bool NoSmokingSign { get => _noSmokingSign; set => SetField(ref _noSmokingSign, value); }
        public bool GearDown { get => _gearDown; set => SetField(ref _gearDown, value); }
        public bool GearTransitioning { get => _gearTransitioning; set => SetField(ref _gearTransitioning, value); }
        public double FlapsPercent { get => _flapsPercent; set { if (SetField(ref _flapsPercent, value)) OnPropertyChanged(nameof(FlapsDisplay)); } }
        public bool SpoilersArmed { get => _spoilersArmed; set => SetField(ref _spoilersArmed, value); }
        public bool ReverserActive { get => _reverserActive; set => SetField(ref _reverserActive, value); }
        public bool CharlieMode { get => _charlieMode; set => SetField(ref _charlieMode, value); }
        public int Squawk { get => _squawk; set { if (SetField(ref _squawk, value)) OnPropertyChanged(nameof(SquawkDisplay)); } }
        public bool ApuRunning   { get => _apuRunning;   set => SetField(ref _apuRunning,   value); }
        public bool ApuAvailable { get => _apuAvailable; set => SetField(ref _apuAvailable, value); }

        /// <summary>Fila APU visible solo en jets comerciales/ejecutivos que tienen APU.</summary>
        public bool ShowApu => _hasApu;
        /// <summary>Fila Bleed Air visible en aeronaves presurizadas (jets + turbohélices presurizados).</summary>
        public bool HasPressurization
        {
            get => _hasPressurization;
            set { if (SetField(ref _hasPressurization, value)) OnPropertyChanged(nameof(ShowApu)); }
        }
        /// <summary>True en jets comerciales/ejecutivos que tienen APU estándar.</summary>
        public bool HasApu
        {
            get => _hasApu;
            set { if (SetField(ref _hasApu, value)) OnPropertyChanged(nameof(ShowApu)); }
        }
        /// <summary>True para monomotores (C208, TBM, SR22, C172…). Oculta el N1 del motor 2 en la UI.</summary>
        public bool IsSingleEngine { get => _isSingleEngine; set => SetField(ref _isSingleEngine, value); }
        public bool BleedAirOn { get => _bleedAirOn; set => SetField(ref _bleedAirOn, value); }
        public double CabinAlt { get => _cabinAlt; set => SetField(ref _cabinAlt, value); }
        public double PressDiff { get => _pressDiff; set => SetField(ref _pressDiff, value); }
        public FlightPhase Phase { get => _phase; set { if (SetField(ref _phase, value)) PhaseLabel = GetPhaseLabel(value); } }
        public string PhaseLabel { get => _phaseLabel; set { if (SetField(ref _phaseLabel, value)) OnPropertyChanged(nameof(PhaseLabelDisplay)); } }
        public string ElapsedTime { get => _elapsedTime; set { if (SetField(ref _elapsedTime, value)) OnPropertyChanged(nameof(ElapsedTimeDisplay)); } }
        public bool AlertNoStrobe { get => _alertNoStrobe; set => SetField(ref _alertNoStrobe, value); }
        public bool AlertPause { get => _alertPause; set => SetField(ref _alertPause, value); }
        public bool HasLiveTelemetry => AcarsContext.Runtime.IsSimulatorConnected && AcarsContext.Runtime.LastTelemetry != null;

        public string IasDisplay => HasLiveTelemetry ? Math.Round(IAS, 0).ToString("F0") : "---";
        public string IASDisplay => IasDisplay;
        public string GsDisplay => HasLiveTelemetry ? Math.Round(GS, 0).ToString("F0") : "---";
        public string GSDisplay => GsDisplay;
        public string AltitudeDisplay => HasLiveTelemetry ? Math.Round(Altitude, 0).ToString("F0") : "---";
        public string AglDisplay      => HasLiveTelemetry ? Math.Round(AltitudeAGL, 0).ToString("F0") : "---";
        // V/S: clamp a 0 cuando en tierra (SimConnect puede retornar ±1 fpm en suelo)
        public string VsDisplay => HasLiveTelemetry ? (OnGround ? "0" : Math.Round(VS, 0).ToString("+#;-#;0")) : "---";
        public string VSDisplay => VsDisplay;
        public string HeadingDisplay => HasLiveTelemetry ? Math.Round(Heading, 0).ToString("000") + "°" : "---";
        public string FuelDisplay => HasLiveTelemetry ? Math.Round(FuelKg, 0).ToString("F0") : "---";
        public string FuelKgDisplay => FuelDisplay;
        public string FuelLbsDisplay => HasLiveTelemetry ? Math.Round(FuelLbs, 0).ToString("F0") : "---";
        public string FlapsDisplay => HasLiveTelemetry ? Math.Round(FlapsPercent, 0).ToString("F0") + "%" : "---";
        public string N1Eng1Display => HasLiveTelemetry ? Math.Round(N1Eng1, 1).ToString("F1") : "---";
        public string N1Eng2Display => HasLiveTelemetry ? Math.Round(N1Eng2, 1).ToString("F1") : "---";
        public string SquawkDisplay => HasLiveTelemetry ? Squawk.ToString("0000") : "----";
        public string PhaseLabelDisplay => PhaseLabel;
        public string ElapsedTimeDisplay => ElapsedTime;
        public string SimBackendDisplay
        {
            get
            {
                var runtime = AcarsContext.Runtime;
                var backend = runtime.SimulatorBackend;
                if (runtime.IsSimulatorConnected)
                {
                    return string.IsNullOrWhiteSpace(backend)
                        ? "Conectado"
                        : backend + " · " + runtime.SimulatorType;
                }

                if (string.IsNullOrWhiteSpace(backend))
                {
                    return "Sin conexión";
                }

                return runtime.HasTelemetry
                    ? "Sin telemetría · " + backend
                    : "Esperando · " + backend;
            }
        }
        public bool IsSimConnected => AcarsContext.Runtime.IsSimulatorConnected;

        public ICommand ConnectMsfsCommand { get; }
        public ICommand ConnectXPlaneCommand { get; }
        public ICommand DisconnectSimCommand { get; }
        public ICommand FinishFlightCommand { get; }

        private readonly System.Windows.Threading.DispatcherTimer _elapsedTimer;

        public InFlightViewModel(MainViewModel main)
        {
            _main = main;
            ConnectMsfsCommand = new RelayCommand(() => _main.NavigateToSimulatorConnect());
            ConnectXPlaneCommand = new RelayCommand(() => _main.NavigateToSimulatorConnect());
            DisconnectSimCommand = new RelayCommand(() => { });
            FinishFlightCommand = new RelayCommand(() => FinishFlight(), () => AcarsContext.FlightService.IsFlightActive && (Phase == FlightPhase.Arrived || (OnGround && GS < 3)));

            _elapsedTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += (_, __) =>
            {
                if (_startTime != default(DateTime))
                {
                    ElapsedTime = (DateTime.UtcNow - _startTime).ToString(@"hh\:mm\:ss");
                }
            };

            AcarsContext.FlightService.TelemetryUpdated += OnTelemetry;
            AcarsContext.FlightService.PhaseChanged += OnPhaseChanged;
            AcarsContext.Runtime.Changed += OnRuntimeChanged;
            ApplyRuntimeState();
        }

        public void StartElapsedTimer()
        {
            _startTime = DateTime.UtcNow;
            _elapsedTimer.Start();
        }

        private void OnRuntimeChanged()
        {
            Application.Current.Dispatcher.Invoke(ApplyRuntimeState);
        }

        private void ApplyRuntimeState()
        {
            OnPropertyChanged(nameof(HasLiveTelemetry));
            OnPropertyChanged(nameof(SimBackendDisplay));
            OnPropertyChanged(nameof(IsSimConnected));
            
            // Notificar a todas las propiedades Display que dependen de HasLiveTelemetry
            OnPropertyChanged(nameof(IasDisplay));
            OnPropertyChanged(nameof(IASDisplay));
            OnPropertyChanged(nameof(GsDisplay));
            OnPropertyChanged(nameof(GSDisplay));
            OnPropertyChanged(nameof(AltitudeDisplay));
            OnPropertyChanged(nameof(VsDisplay));
            OnPropertyChanged(nameof(VSDisplay));
            OnPropertyChanged(nameof(HeadingDisplay));
            OnPropertyChanged(nameof(FuelDisplay));
            OnPropertyChanged(nameof(FuelKgDisplay));
            OnPropertyChanged(nameof(FuelLbsDisplay));
            OnPropertyChanged(nameof(FlapsDisplay));
            OnPropertyChanged(nameof(N1Eng1Display));
            OnPropertyChanged(nameof(N1Eng2Display));
            OnPropertyChanged(nameof(SquawkDisplay));
            OnPropertyChanged(nameof(FuelLeftTankDisplay));
            OnPropertyChanged(nameof(FuelRightTankDisplay));
            OnPropertyChanged(nameof(FuelCenterTankDisplay));
            OnPropertyChanged(nameof(FuelCapacityDisplay));

            if (!AcarsContext.Runtime.IsSimulatorConnected)
            {
                ClearTelemetrySnapshot();
            }
        }

        private void OnTelemetry(SimData data)
        {
            Debug.WriteLine($"[InFlightVM] OnTelemetry - ACFT:{data.AircraftTitle} ALT={data.AltitudeFeet:F0} FUEL={data.FuelTotalLbs:F0}");
            Application.Current.Dispatcher.Invoke(() =>
            {
                AircraftTitle = data.AircraftTitle;
                Altitude    = data.AltitudeFeet;
                AltitudeAGL = data.AltitudeAGL;
                IAS = data.IndicatedAirspeed;
                GS = data.GroundSpeed;
                VS = data.VerticalSpeed;
                Heading = data.Heading;
                FuelLbs = data.FuelTotalLbs;
                
                // Tanques individuales (para diagnóstico)
                FuelLeftTank = data.FuelLeftTankLbs;
                FuelRightTank = data.FuelRightTankLbs;
                FuelCenterTank = data.FuelCenterTankLbs;
                FuelCapacity = data.FuelTotalCapacityLbs;
                
                N1Eng1 = data.Engine1N1;
                N1Eng2 = data.Engine2N1;
                OAT = Math.Round(data.OutsideTemperature, 1);
                WindSpeed = Math.Round(data.WindSpeed, 0);
                WindDir = Math.Round(data.WindDirection, 0);
                Lat = data.Latitude;
                Lon = data.Longitude;
                AutopilotOn = data.AutopilotActive;
                OnGround = data.OnGround;
                StrobeOn = data.StrobeLightsOn;
                BeaconOn = data.BeaconLightsOn;
                LandingOn = data.LandingLightsOn;
                TaxiOn = data.TaxiLightsOn;
                NavOn = data.NavLightsOn;
                
                Debug.WriteLine($"[InFlightVM] Luces - STROBE:{data.StrobeLightsOn} BEACON:{data.BeaconLightsOn} LANDING:{data.LandingLightsOn} TAXI:{data.TaxiLightsOn} NAV:{data.NavLightsOn}");
                Debug.WriteLine($"[InFlightVM] SQUAWK raw:{data.TransponderCode} -> display:{SquawkDisplay}");
                SeatBeltSign = data.SeatBeltSign;
                NoSmokingSign = data.NoSmokingSign;
                GearDown = data.GearDown;
                GearTransitioning = data.GearTransitioning;
                FlapsPercent = Math.Round(data.FlapsPercent, 0);
                SpoilersArmed = data.SpoilersArmed;
                ReverserActive = data.ReverserActive;
                CharlieMode = data.TransponderCharlieMode;
                Squawk = data.TransponderCode;
                ApuRunning = data.ApuRunning;
                ApuAvailable = data.ApuAvailable;
                BleedAirOn = data.BleedAirOn;
                CabinAlt = Math.Round(data.CabinAltitudeFeet, 0);
                PressDiff = Math.Round(data.PressureDiffPsi, 2);
                ApplyRuntimeState();
                CheckAlerts(data);
            });
        }

        private void ClearTelemetrySnapshot()
        {
            Altitude = 0;
            IAS = 0;
            GS = 0;
            VS = 0;
            Heading = 0;
            FuelLbs = 0;
            N1Eng1 = 0;
            N1Eng2 = 0;
            OAT = 0;
            WindSpeed = 0;
            WindDir = 0;
            Lat = 0;
            Lon = 0;
            AutopilotOn = false;
            OnGround = false;
            StrobeOn = false;
            BeaconOn = false;
            LandingOn = false;
            TaxiOn = false;
            NavOn = false;
            SeatBeltSign = false;
            NoSmokingSign = false;
            GearDown = false;
            GearTransitioning = false;
            FlapsPercent = 0;
            SpoilersArmed = false;
            ReverserActive = false;
            CharlieMode = false;
            Squawk = 0;
            ApuRunning = false;
            ApuAvailable = false;
            BleedAirOn = false;
            CabinAlt = 0;
            PressDiff = 0;
            AlertNoStrobe = false;
            AlertPause = false;
        }

        private void CheckAlerts(SimData data)
        {
            if (!data.OnGround && !data.StrobeLightsOn && data.IndicatedAirspeed > 60)
            {
                if (!AlertNoStrobe)
                {
                    AlertNoStrobe = true;
                    _ = AcarsContext.Sound.PlayGroundNoLightsAsync();
                }
            }
            else
            {
                AlertNoStrobe = false;
            }

            var nowAbove = data.AltitudeFeet > 10000;
            if (nowAbove && !_above10000)
            {
                _ = AcarsContext.Sound.PlayCopilot10000PiesAscAsync();
            }
            else if (!nowAbove && _above10000 && data.VerticalSpeed < -100)
            {
                _ = AcarsContext.Sound.PlayCopilot10000PiesDescAsync();
            }
            _above10000 = nowAbove;

            if (Phase == FlightPhase.Approach && data.AltitudeFeet < 1000 && !data.OnGround)
            {
                AcarsContext.Runtime.SetTelemetry(data, AcarsContext.Runtime.SimulatorBackend);
                
                // Forzar actualización de todas las propiedades Display
                OnPropertyChanged(nameof(IasDisplay));
                OnPropertyChanged(nameof(IASDisplay));
                OnPropertyChanged(nameof(GsDisplay));
                OnPropertyChanged(nameof(GSDisplay));
                OnPropertyChanged(nameof(AltitudeDisplay));
                OnPropertyChanged(nameof(VsDisplay));
                OnPropertyChanged(nameof(VSDisplay));
                OnPropertyChanged(nameof(HeadingDisplay));
                OnPropertyChanged(nameof(FuelDisplay));
                OnPropertyChanged(nameof(FuelKgDisplay));
                OnPropertyChanged(nameof(FuelLbsDisplay));
                OnPropertyChanged(nameof(FlapsDisplay));
                OnPropertyChanged(nameof(N1Eng1Display));
                OnPropertyChanged(nameof(N1Eng2Display));
                OnPropertyChanged(nameof(SquawkDisplay));
                OnPropertyChanged(nameof(FuelLeftTankDisplay));
                OnPropertyChanged(nameof(FuelRightTankDisplay));
                OnPropertyChanged(nameof(FuelCenterTankDisplay));
                OnPropertyChanged(nameof(FuelCapacityDisplay));
            }
        }

        private void OnPhaseChanged(FlightPhase newPhase)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Phase = newPhase;
                CommandManager.InvalidateRequerySuggested();
                switch (newPhase)
                {
                    case FlightPhase.Boarding:
                        _ = AcarsContext.Sound.PlayGroundBoardingAsync();
                        break;
                    case FlightPhase.PushbackTaxi:
                        _ = AcarsContext.Sound.PlayGroundDoorClosedAsync();
                        break;
                    case FlightPhase.Takeoff:
                        _ = AcarsContext.Sound.PlayGroundEnginesAsync();
                        StartElapsedTimer();
                        break;
                    case FlightPhase.Arrived:
                        _ = AcarsContext.Sound.PlayGroundArrivedAsync();
                        _elapsedTimer.Stop();
                        break;
                }
            });
        }

        private void FinishFlight()
        {
            var pilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
            if (pilot == null)
            {
                return;
            }

            var report = AcarsContext.FlightService.GenerateReport(pilot.CallSign);
            _main.ShowPostFlightReport(report);
        }

        private static string GetPhaseLabel(FlightPhase phase)
        {
            return phase switch
            {
                FlightPhase.PreFlight => "Prevuelo",
                FlightPhase.Boarding => "Embarque",
                FlightPhase.PushbackTaxi => "Rodaje",
                FlightPhase.Takeoff => "Despegue",
                FlightPhase.Climb => "Ascenso",
                FlightPhase.Cruise => "Crucero",
                FlightPhase.Descent => "Descenso",
                FlightPhase.Approach => "Aproximación",
                FlightPhase.Landing => "Aterrizaje",
                FlightPhase.Taxi => "Taxi in",
                FlightPhase.Arrived => "Arribado",
                _ => "Desconectado"
            };
        }
        
        private void DetectAircraftType(string title)
        {
            var t = (title ?? string.Empty).ToUpperInvariant();

            // ── 1. Requiere LVARs para datos completos ─────────────────────────────
            // Add-ons de alto detalle que usan LVARs propias para luces, N1, sistemas
            RequiresLvars =
                t.Contains("A318") || t.Contains("A319") || t.Contains("A320") || t.Contains("A321") ||
                t.Contains("ACJ")  || t.Contains("BBJ")  || t.Contains("ACJ319") || t.Contains("ACJ320") ||
                t.Contains("FENIX") || t.Contains("HEADWIND") || t.Contains("FLYBYWIRE") || t.Contains("INIBUILDS") ||
                t.Contains("TOLISS") || t.Contains("MAJESTIC") || t.Contains("QUALITYWINGS") ||
                t.Contains("PMDG")  || t.Contains("LEONARDO") || t.Contains("FEELTHERE") ||
                t.Contains("B737") || t.Contains("B738") || t.Contains("B739") || t.Contains("B38M") ||
                t.Contains("B747") || t.Contains("B757") || t.Contains("B767") ||
                t.Contains("B777") || t.Contains("B787") || t.Contains("B78X");

            AircraftStatus = RequiresLvars
                ? "Algunos datos requieren LVARs (Luces, N1 detallado)"
                : "Datos completos vía SimConnect";

            // ── 2. Jets comerciales / ejecutivos con APU estándar ─────────────────
            // Todos los aviones de fuselaje estrecho y ancho tienen APU
            bool isJet =
                // Airbus familia (incluyendo todos los add-ons)
                t.Contains("A318") || t.Contains("A319") || t.Contains("A320") || t.Contains("A321") ||
                t.Contains("A330") || t.Contains("A339") || t.Contains("A340") || t.Contains("A350") ||
                t.Contains("A359") || t.Contains("A380") || t.Contains("A388") ||
                t.Contains("ACJ") || t.Contains("BBJ") || t.Contains("AIRBUS") ||
                // Boeing familia
                t.Contains("B717") || t.Contains("B727") ||
                t.Contains("B737") || t.Contains("B738") || t.Contains("B739") || t.Contains("B38M") ||
                t.Contains("B747") || t.Contains("B757") || t.Contains("B767") ||
                t.Contains("B777") || t.Contains("B787") || t.Contains("B78X") || t.Contains("B77W") ||
                t.Contains("B772") || t.Contains("B789") || t.Contains("BOEING") ||
                // Jets regionales
                t.Contains("CRJ") || t.Contains("CRJ200") || t.Contains("CRJ700") || t.Contains("CRJ900") ||
                t.Contains("E170") || t.Contains("E175") || t.Contains("E190") || t.Contains("E195") ||
                t.Contains("E2 170") || t.Contains("E2 190") || t.Contains("E2 195") ||
                // McDonnell Douglas
                t.Contains("MD80") || t.Contains("MD82") || t.Contains("MD83") ||
                t.Contains("MD88") || t.Contains("MD90") || t.Contains("MD11") ||
                t.Contains("DC9") || t.Contains("DC-9") || t.Contains("DC10") || t.Contains("DC-10") ||
                // Add-ons reconocidos
                t.Contains("FENIX") || t.Contains("HEADWIND") || t.Contains("FLYBYWIRE") ||
                t.Contains("INIBUILDS") || t.Contains("TOLISS") || t.Contains("MAJESTIC") ||
                t.Contains("QUALITYWINGS") || t.Contains("PMDG") || t.Contains("LEONARDO") ||
                t.Contains("FEELTHERE") || t.Contains("ROTATE") || t.Contains("SALTY") ||
                t.Contains("CAPTAIN SIM");

            // ── 3. Turbohélices presurizados (sin APU estándar) ───────────────────
            // Tienen pressurización pero no APU (salvo King Air y algunos ATR)
            bool isPressurizedTurboprop =
                // ATR (presurizados — usados por Patagonia Wings)
                t.Contains("ATR 42") || t.Contains("ATR42") ||
                t.Contains("ATR 72") || t.Contains("ATR72") || t.Contains("AT76") ||
                // De Havilland Canada DHC-8 / Bombardier Q-series
                t.Contains("DHC-8") || t.Contains("DASH 8") || t.Contains("DASH8") ||
                t.Contains("DASH-8") || t.Contains("Q200") || t.Contains("Q300") ||
                t.Contains("Q400") || t.Contains("BOMBARDIER DASH") ||
                // Daher TBM (monomotor turbohélice PRESURIZADO)
                t.Contains("TBM") || t.Contains("TBM 700") || t.Contains("TBM 850") ||
                t.Contains("TBM 900") || t.Contains("TBM 930") || t.Contains("TBM 940") ||
                t.Contains("TBM700") || t.Contains("TBM850") || t.Contains("TBM900") ||
                t.Contains("TBM930") || t.Contains("TBM940") ||
                // Beechcraft King Air (presurizados)
                t.Contains("KING AIR") || t.Contains("B350") || t.Contains("BE350") ||
                t.Contains("C90") || t.Contains("B200") || t.Contains("B300") ||
                t.Contains("1900") ||  // Beechcraft 1900 (presurizado regional)
                // Pilatus PC-12 (monomotor turbohélice PRESURIZADO)
                t.Contains("PC-12") || t.Contains("PC12") || t.Contains("PILATUS") ||
                // Piper Meridian / M600 (presurizados)
                t.Contains("PIPER M600") || t.Contains("M600") || t.Contains("MERIDIAN") ||
                t.Contains("MALIBU") || t.Contains("PA-46");

            // King Air B350/B300/C90/1900 tienen APU estándar (Honeywell GTCP36-150)
            // Q400 tiene APU opcional (RE220T) — incluido porque el modelo MSFS lo reporta
            bool hasApuTurboprop =
                t.Contains("KING AIR") || t.Contains("B350") || t.Contains("BE350") ||
                t.Contains("B300") || t.Contains("C90") || t.Contains("1900") ||
                t.Contains("Q400") || t.Contains("DASH8-400") || t.Contains("DASH 8-400");

            HasApu            = isJet || hasApuTurboprop;
            HasPressurization = isJet || isPressurizedTurboprop; // Bleed Air en presurizados

            // ── 4. Motor único ────────────────────────────────────────────────────
            // Oculta el indicador de motor 2 en la UI y ajusta scoring
            IsSingleEngine =
                // Cessna Caravan (turbohélice monomotor NO presurizado)
                t.Contains("C208") || t.Contains("CARAVAN") || t.Contains("GRAND CARAVAN") ||
                t.Contains("CESSNA 208") ||
                // TBM y PC-12 (monomotores presurizados)
                t.Contains("TBM") || t.Contains("PC-12") || t.Contains("PC12") || t.Contains("PILATUS") ||
                t.Contains("M600") || t.Contains("MERIDIAN") || t.Contains("MALIBU") ||
                // GA monomotores comunes en MSFS
                t.Contains("SR20") || t.Contains("SR22") || t.Contains("CIRRUS") ||
                t.Contains("DA40") || t.Contains("DIAMOND DA40") || t.Contains("MOONEY") ||
                t.Contains("C172") || t.Contains("CESSNA 172") || t.Contains("SKYHAWK") ||
                t.Contains("C152") || t.Contains("C182") || t.Contains("C206") ||
                t.Contains("ROBIN") || t.Contains("PA28") || t.Contains("PA-28") ||
                t.Contains("PIPER WARRIOR") || t.Contains("PIPER ARCHER");

            Debug.WriteLine($"[InFlightVM] '{title}' → APU:{HasApu} Presurizado:{HasPressurization} Monomotor:{IsSingleEngine} LVARs:{RequiresLvars}");
            OnPropertyChanged(nameof(RequiresLvars));
            OnPropertyChanged(nameof(AircraftStatus));
            OnPropertyChanged(nameof(ShowApu));
            OnPropertyChanged(nameof(HasPressurization));
            OnPropertyChanged(nameof(IsSingleEngine));
        }
    }
}
