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
        private double _cabinAlt;
        private double _pressDiff;
        private FlightPhase _phase = FlightPhase.Disconnected;
        private string _phaseLabel = "Desconectado";
        private string _elapsedTime = "00:00:00";
        private DateTime _startTime;
        private bool _alertNoStrobe;
        private bool _alertPause;
        private bool _above10000;

        public double Altitude { get => _altitude; set { if (SetField(ref _altitude, value)) OnPropertyChanged(nameof(AltitudeDisplay)); } }
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
        public string AircraftTitle { get => _aircraftTitle; set { if (SetField(ref _aircraftTitle, value)) { DetectAircraftType(value); OnPropertyChanged(nameof(AircraftStatusDisplay)); OnPropertyChanged(nameof(ShowDoors)); } } }

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
        public bool ApuRunning { get => _apuRunning; set => SetField(ref _apuRunning, value); }
        public bool ApuAvailable { get => _apuAvailable; set => SetField(ref _apuAvailable, value); }
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
        public string VsDisplay => HasLiveTelemetry ? Math.Round(VS, 0).ToString("+#;-#;0") : "---";
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
                Altitude = data.AltitudeFeet;
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
            // Lista de aviones que requieren LVARs para datos completos
            var lvarAircraft = new[] { "A319", "Headwind", "ACJ319", "Fenix", "A320", "PMDG", "B737", "B777" };
            
            RequiresLvars = false;
            AircraftStatus = "Datos completos vía SimConnect";
            
            foreach (var aircraft in lvarAircraft)
            {
                if (title.IndexOf(aircraft, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    RequiresLvars = true;
                    AircraftStatus = "Algunos datos requieren LVARs (Luces, N1 detallado)";
                    Debug.WriteLine($"[InFlightVM] Avión detectado: {aircraft} - requiere LVARs para datos completos");
                    break;
                }
            }
            
            OnPropertyChanged(nameof(RequiresLvars));
            OnPropertyChanged(nameof(AircraftStatus));
        }
    }
}
