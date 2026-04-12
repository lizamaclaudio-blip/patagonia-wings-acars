using System;
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
        private double _n1Eng1;
        private double _n1Eng2;
        private double _oat;
        private double _windSpeed;
        private double _windDir;
        private double _lat;
        private double _lon;
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
        public double FuelKg => Math.Round(FuelLbs * 0.453592, 0);
        public double N1Eng1 { get => _n1Eng1; set { if (SetField(ref _n1Eng1, value)) OnPropertyChanged(nameof(N1Eng1Display)); } }
        public double N1Eng2 { get => _n1Eng2; set { if (SetField(ref _n1Eng2, value)) OnPropertyChanged(nameof(N1Eng2Display)); } }
        public double OAT { get => _oat; set => SetField(ref _oat, value); }
        public double WindSpeed { get => _windSpeed; set => SetField(ref _windSpeed, value); }
        public double WindDir { get => _windDir; set => SetField(ref _windDir, value); }
        public double Lat { get => _lat; set => SetField(ref _lat, value); }
        public double Lon { get => _lon; set => SetField(ref _lon, value); }
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

        public string IasDisplay => Math.Round(IAS, 0).ToString("F0");
        public string IASDisplay => IasDisplay;
        public string GsDisplay => Math.Round(GS, 0).ToString("F0");
        public string GSDisplay => GsDisplay;
        public string AltitudeDisplay => Math.Round(Altitude, 0).ToString("F0");
        public string VsDisplay => Math.Round(VS, 0).ToString("+#;-#;0");
        public string VSDisplay => VsDisplay;
        public string HeadingDisplay => Math.Round(Heading, 0).ToString("000") + "°";
        public string FuelDisplay => Math.Round(FuelKg, 0).ToString("F0");
        public string FuelKgDisplay => FuelDisplay;
        public string FuelLbsDisplay => Math.Round(FuelLbs, 0).ToString("F0");
        public string FlapsDisplay => Math.Round(FlapsPercent, 0).ToString("F0") + "%";
        public string N1Eng1Display => Math.Round(N1Eng1, 1).ToString("F1");
        public string N1Eng2Display => Math.Round(N1Eng2, 1).ToString("F1");
        public string SquawkDisplay => Squawk.ToString("0000");
        public string PhaseLabelDisplay => PhaseLabel;
        public string ElapsedTimeDisplay => ElapsedTime;
        public string SimBackendDisplay => AcarsContext.Runtime.IsSimulatorConnected
            ? AcarsContext.Runtime.SimulatorBackend + " · " + AcarsContext.Runtime.SimulatorType
            : "Sin conexión";
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
        }

        public void StartElapsedTimer()
        {
            _startTime = DateTime.UtcNow;
            _elapsedTimer.Start();
        }

        private void OnTelemetry(SimData data)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Altitude = data.AltitudeFeet;
                IAS = data.IndicatedAirspeed;
                GS = data.GroundSpeed;
                VS = data.VerticalSpeed;
                Heading = data.Heading;
                FuelLbs = data.FuelTotalLbs;
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
                OnPropertyChanged(nameof(SimBackendDisplay));
                OnPropertyChanged(nameof(IsSimConnected));
                CheckAlerts(data);
            });
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
                _ = AcarsContext.Sound.PlayCopilotAproximacionAsync();
            }
        }

        private void OnPhaseChanged(FlightPhase phase)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Phase = phase;
                CommandManager.InvalidateRequerySuggested();
                switch (phase)
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
            if (pilot == null) return;
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
    }
}
