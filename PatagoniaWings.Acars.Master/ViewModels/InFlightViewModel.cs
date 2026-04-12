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

        // ── Telemetría ────────────────────────────────────────────────────────
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
        private bool _hasTelemetry;
        private DateTime _lastTelemetryAtUtc;

        public double Altitude { get => _altitude; set => SetField(ref _altitude, value); }
        public double IAS { get => _ias; set => SetField(ref _ias, value); }
        public double GS { get => _gs; set => SetField(ref _gs, value); }
        public double VS { get => _vs; set => SetField(ref _vs, value); }
        public double Heading { get => _heading; set => SetField(ref _heading, value); }
        public double FuelLbs { get => _fuelLbs; set { SetField(ref _fuelLbs, value); OnPropertyChanged(nameof(FuelKg)); } }
        public double FuelKg => Math.Round(FuelLbs * 0.453592, 0);
        public double N1Eng1 { get => _n1Eng1; set => SetField(ref _n1Eng1, value); }
        public double N1Eng2 { get => _n1Eng2; set => SetField(ref _n1Eng2, value); }
        public double OAT { get => _oat; set => SetField(ref _oat, value); }
        public double WindSpeed { get => _windSpeed; set => SetField(ref _windSpeed, value); }
        public double WindDir { get => _windDir; set => SetField(ref _windDir, value); }
        public double Lat { get => _lat; set => SetField(ref _lat, value); }
        public double Lon { get => _lon; set => SetField(ref _lon, value); }
        public bool AutopilotOn { get => _autopilotOn; set => SetField(ref _autopilotOn, value); }
        public bool OnGround { get => _onGround; set => SetField(ref _onGround, value); }
        public bool HasTelemetry { get => _hasTelemetry; private set => SetField(ref _hasTelemetry, value); }
        public DateTime LastTelemetryAtUtc { get => _lastTelemetryAtUtc; private set => SetField(ref _lastTelemetryAtUtc, value); }

        public string PhaseLabelDisplay => HasTelemetry ? PhaseLabel : "Sin telemetría";
        public string ElapsedTimeDisplay => string.IsNullOrWhiteSpace(ElapsedTime) ? "00:00:00" : ElapsedTime;
        public string IASDisplay => FormatWhole(IAS);
        public string GSDisplay => FormatWhole(GS);
        public string AltitudeDisplay => FormatWhole(Altitude);
        public string VSDisplay => HasTelemetry ? Math.Round(VS, 0).ToString("+#;-#;0") : "--";
        public string HeadingDisplay => HasTelemetry ? Math.Round(Heading, 0).ToString("000") + "°" : "---";
        public string LatDisplay => HasTelemetry ? "Lat " + Lat.ToString("F4") + "°" : "Lat --";
        public string LonDisplay => HasTelemetry ? "Lon " + Lon.ToString("F4") + "°" : "Lon --";
        public string FuelKgDisplay => FormatWhole(FuelKg);
        public string FuelLbsDisplay => FormatWhole(FuelLbs);
        public string N1Eng1Display => HasTelemetry ? Math.Round(N1Eng1, 1).ToString("F1") + "%" : "--.-%";
        public string N1Eng2Display => HasTelemetry ? Math.Round(N1Eng2, 1).ToString("F1") + "%" : "--.-%";
        public string OatDisplay => HasTelemetry ? Math.Round(OAT, 1).ToString("F1") + " °C" : "--.- °C";
        public string WindDisplay => HasTelemetry
            ? Math.Round(WindDir, 0).ToString("000") + "° / " + Math.Round(WindSpeed, 0).ToString("F0") + " kts"
            : "---° / -- kts";
        public string FlapsDisplay => HasTelemetry ? Math.Round(FlapsPercent, 0).ToString("F0") + " %" : "-- %";
        public string SquawkDisplay => HasTelemetry ? Squawk.ToString("0000") : "----";

        // ── Luces ─────────────────────────────────────────────────────────────
        private bool _strobeOn;
        private bool _beaconOn;
        private bool _landingOn;
        private bool _taxiOn;
        private bool _navOn;
        public bool StrobeOn { get => _strobeOn; set => SetField(ref _strobeOn, value); }
        public bool BeaconOn { get => _beaconOn; set => SetField(ref _beaconOn, value); }
        public bool LandingOn { get => _landingOn; set => SetField(ref _landingOn, value); }
        public bool TaxiOn { get => _taxiOn; set => SetField(ref _taxiOn, value); }
        public bool NavOn { get => _navOn; set => SetField(ref _navOn, value); }

        // ── Sistemas de cabina ────────────────────────────────────────────────
        private bool _seatBeltSign;
        private bool _noSmokingSign;
        private bool _gearDown;
        private bool _gearTransitioning;
        private double _flapsPercent;
        private bool _spoilersArmed;
        private bool _reverserActive;
        public bool SeatBeltSign { get => _seatBeltSign; set => SetField(ref _seatBeltSign, value); }
        public bool NoSmokingSign { get => _noSmokingSign; set => SetField(ref _noSmokingSign, value); }
        public bool GearDown { get => _gearDown; set => SetField(ref _gearDown, value); }
        public bool GearTransitioning { get => _gearTransitioning; set => SetField(ref _gearTransitioning, value); }
        public double FlapsPercent { get => _flapsPercent; set => SetField(ref _flapsPercent, value); }
        public bool SpoilersArmed { get => _spoilersArmed; set => SetField(ref _spoilersArmed, value); }
        public bool ReverserActive { get => _reverserActive; set => SetField(ref _reverserActive, value); }

        // ── Aviónica ──────────────────────────────────────────────────────────
        private bool _charlieMode;
        private int _squawk;
        private bool _apuRunning;
        private bool _apuAvailable;
        private bool _bleedAirOn;
        private double _cabinAlt;
        private double _pressDiff;
        public bool CharlieMode { get => _charlieMode; set => SetField(ref _charlieMode, value); }
        public int Squawk { get => _squawk; set => SetField(ref _squawk, value); }
        public bool ApuRunning { get => _apuRunning; set => SetField(ref _apuRunning, value); }
        public bool ApuAvailable { get => _apuAvailable; set => SetField(ref _apuAvailable, value); }
        public bool BleedAirOn { get => _bleedAirOn; set => SetField(ref _bleedAirOn, value); }
        public double CabinAlt { get => _cabinAlt; set => SetField(ref _cabinAlt, value); }
        public double PressDiff { get => _pressDiff; set => SetField(ref _pressDiff, value); }

        // ── Estado del vuelo ─────────────────────────────────────────────────
        private FlightPhase _phase = FlightPhase.Disconnected;
        private string _phaseLabel = "Desconectado";
        private string _elapsedTime = "00:00:00";
        private DateTime _startTime;

        public FlightPhase Phase { get => _phase; set { SetField(ref _phase, value); PhaseLabel = GetPhaseLabel(value); } }
        public string PhaseLabel { get => _phaseLabel; set => SetField(ref _phaseLabel, value); }
        public string ElapsedTime { get => _elapsedTime; set => SetField(ref _elapsedTime, value); }

        // ── Alertas ───────────────────────────────────────────────────────────
        private bool _alertNoStrobe;
        private bool _alertPause;
        public bool AlertNoStrobe { get => _alertNoStrobe; set => SetField(ref _alertNoStrobe, value); }
        public bool AlertPause { get => _alertPause; set => SetField(ref _alertPause, value); }

        // ── Comandos ──────────────────────────────────────────────────────────
        public ICommand ConnectMsfsCommand { get; }
        public ICommand DisconnectSimCommand { get; }
        public ICommand FinishFlightCommand { get; }

        private readonly System.Windows.Threading.DispatcherTimer _elapsedTimer;
        private bool _above10000 = false;

        public InFlightViewModel(MainViewModel main)
        {
            _main = main;

            ConnectMsfsCommand = new RelayCommand(() => _main.InFlightVM.TryConnectMsfs());
            DisconnectSimCommand = new RelayCommand(() => _main.InFlightVM.DisconnectSim());
            FinishFlightCommand = new RelayCommand(
                () => FinishFlight(),
                () => AcarsContext.FlightService.IsFlightActive && (Phase == FlightPhase.Arrived || (OnGround && GS < 3)));

            _elapsedTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(1) };
            _elapsedTimer.Tick += (_, __) =>
            {
                if (_startTime != default)
                {
                    ElapsedTime = (DateTime.UtcNow - _startTime).ToString(@"hh\:mm\:ss");
                    OnPropertyChanged(nameof(ElapsedTimeDisplay));
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
                HasTelemetry = true;
                LastTelemetryAtUtc = data.CapturedAtUtc == default ? DateTime.UtcNow : data.CapturedAtUtc;
                Altitude = Math.Round(data.AltitudeFeet, 0);
                IAS = Math.Round(data.IndicatedAirspeed, 0);
                GS = Math.Round(data.GroundSpeed, 0);
                VS = Math.Round(data.VerticalSpeed, 0);
                Heading = Math.Round(data.Heading, 0);
                FuelLbs = Math.Round(data.FuelTotalLbs, 0);
                N1Eng1 = Math.Round(data.Engine1N1, 1);
                N1Eng2 = Math.Round(data.Engine2N1, 1);
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

                RaiseTelemetryDisplayPropertiesChanged();
                CheckAlerts(data);
            });
        }

        private void CheckAlerts(SimData data)
        {
            // Alerta de strobes apagados en vuelo
            if (!data.OnGround && !data.StrobeLightsOn && data.IndicatedAirspeed > 60)
            {
                if (!AlertNoStrobe)
                {
                    AlertNoStrobe = true;
                    _ = AcarsContext.Sound.PlayGroundNoLightsAsync();
                }
            }
            else AlertNoStrobe = false;

            // Alerta de 10,000 pies
            bool nowAbove = data.AltitudeFeet > 10000;
            if (nowAbove && !_above10000)
            {
                _ = AcarsContext.Sound.PlayCopilot10000PiesAscAsync();
            }
            else if (!nowAbove && _above10000 && data.VerticalSpeed < -100)
            {
                _ = AcarsContext.Sound.PlayCopilot10000PiesDescAsync();
            }
            _above10000 = nowAbove;

            // Alerta aproximación final
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
                OnPropertyChanged(nameof(PhaseLabelDisplay));
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
            var pilot = AcarsContext.Auth.CurrentPilot;
            if (pilot == null) return;

            var report = AcarsContext.FlightService.GenerateReport(pilot.CallSign);
            _main.ShowPostFlightReport(report);
        }

        public void TryConnectMsfs() { /* Llamado desde MainWindow con handle */ }
        public void DisconnectSim() { /* Llamado desde MainWindow */ }

        private void RaiseTelemetryDisplayPropertiesChanged()
        {
            OnPropertyChanged(nameof(HasTelemetry));
            OnPropertyChanged(nameof(PhaseLabelDisplay));
            OnPropertyChanged(nameof(ElapsedTimeDisplay));
            OnPropertyChanged(nameof(IASDisplay));
            OnPropertyChanged(nameof(GSDisplay));
            OnPropertyChanged(nameof(AltitudeDisplay));
            OnPropertyChanged(nameof(VSDisplay));
            OnPropertyChanged(nameof(HeadingDisplay));
            OnPropertyChanged(nameof(LatDisplay));
            OnPropertyChanged(nameof(LonDisplay));
            OnPropertyChanged(nameof(FuelKgDisplay));
            OnPropertyChanged(nameof(FuelLbsDisplay));
            OnPropertyChanged(nameof(N1Eng1Display));
            OnPropertyChanged(nameof(N1Eng2Display));
            OnPropertyChanged(nameof(OatDisplay));
            OnPropertyChanged(nameof(WindDisplay));
            OnPropertyChanged(nameof(FlapsDisplay));
            OnPropertyChanged(nameof(SquawkDisplay));
        }

        private string FormatWhole(double value)
        {
            return HasTelemetry ? Math.Round(value, 0).ToString("F0") : "--";
        }

        private static string GetPhaseLabel(FlightPhase p) => p switch
        {
            FlightPhase.Disconnected => "Desconectado",
            FlightPhase.PreFlight => "Pre-Vuelo",
            FlightPhase.Boarding => "Embarque",
            FlightPhase.PushbackTaxi => "Pushback / Taxi",
            FlightPhase.Takeoff => "Despegue",
            FlightPhase.Climb => "Ascenso",
            FlightPhase.Cruise => "Crucero",
            FlightPhase.Descent => "Descenso",
            FlightPhase.Approach => "Aproximación",
            FlightPhase.Landing => "Aterrizaje",
            FlightPhase.Taxi => "Taxi",
            FlightPhase.Arrived => "¡Llegada!",
            FlightPhase.Deboarding => "Desembarque",
            _ => p.ToString()
        };
    }
}
