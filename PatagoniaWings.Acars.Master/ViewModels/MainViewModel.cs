using System;
using System.Windows;
using System.Windows.Input;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private string _currentPageName = "Dashboard";
        private bool _flightLocked;
        private Pilot? _pilot;
        private bool _simConnected;
        private SimulatorType _simType = SimulatorType.None;
        private string _simStatusText = "Sin simulador";
        private string _utcTime = string.Empty;
        private FlightPhase _flightPhase = FlightPhase.Disconnected;

        public string CurrentPageName { get => _currentPageName; set => SetField(ref _currentPageName, value); }
        public bool FlightLocked
        {
            get => _flightLocked;
            set
            {
                if (SetField(ref _flightLocked, value))
                {
                    OnPropertyChanged(nameof(CanAccessPostFlight));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }
        public bool CanAccessPostFlight => !FlightLocked || PostFlightVM.Report != null;

        public Pilot? Pilot { get => _pilot; set { if (SetField(ref _pilot, value)) OnPropertyChanged(nameof(PilotDisplay)); } }
        public string PilotDisplay => Pilot != null ? string.Format("{0} · {1}", Pilot.CallSign, Pilot.RankName) : string.Empty;

        public bool SimConnected { get => _simConnected; set { if (SetField(ref _simConnected, value)) { OnPropertyChanged(nameof(SimStatusColor)); OnPropertyChanged(nameof(SimStatusText)); } } }
        public SimulatorType SimType { get => _simType; set { if (SetField(ref _simType, value)) OnPropertyChanged(nameof(SimStatusText)); } }
        public string SimStatusText { get => _simStatusText; set => SetField(ref _simStatusText, value); }
        public string SimStatusColor => _simConnected ? "#44CC44" : "#FF4444";

        public FlightPhase FlightPhase { get => _flightPhase; set => SetField(ref _flightPhase, value); }
        public string UtcTime { get => _utcTime; set => SetField(ref _utcTime, value); }

        public ICommand NavDashboardCommand { get; }
        public ICommand NavPreFlightCommand { get; }
        public ICommand NavInFlightCommand { get; }
        public ICommand NavProfileCommand { get; }
        public ICommand NavCommunityCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand OpenLiveFlightCommand { get; }

        public Action? OnLogout { get; set; }
        public Action? OnOpenLiveFlight { get; set; }

        private readonly System.Windows.Threading.DispatcherTimer _clockTimer;

        public DashboardViewModel DashboardVM { get; } = new DashboardViewModel();
        public PreFlightViewModel PreFlightVM { get; } = new PreFlightViewModel();
        public InFlightViewModel InFlightVM { get; }
        public PostFlightViewModel PostFlightVM { get; } = new PostFlightViewModel();
        public ProfileViewModel ProfileVM { get; } = new ProfileViewModel();
        public CommunityViewModel CommunityVM { get; } = new CommunityViewModel();

        public MainViewModel()
        {
            InFlightVM = new InFlightViewModel(this);

            NavDashboardCommand = new RelayCommand(() => NavigateTo("Dashboard"), () => CanNavigate("Dashboard"));
            NavPreFlightCommand = new RelayCommand(() => NavigateTo("PreFlight"), () => CanNavigate("PreFlight"));
            NavInFlightCommand = new RelayCommand(() => NavigateTo("InFlight"));
            NavProfileCommand = new RelayCommand(() => NavigateTo("Profile"), () => CanNavigate("Profile"));
            NavCommunityCommand = new RelayCommand(() => NavigateTo("Community"), () => CanNavigate("Community"));
            LogoutCommand = new RelayCommand(DoLogout);
            OpenLiveFlightCommand = new RelayCommand(() => OnOpenLiveFlight?.Invoke());

            _clockTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (_, __) => UtcTime = DateTime.UtcNow.ToString("HH:mm:ss") + " UTC";
            _clockTimer.Start();

            AcarsContext.FlightService.PhaseChanged += p =>
            {
                Application.Current.Dispatcher.Invoke(() => FlightPhase = p);
            };
            AcarsContext.FlightService.FlightLockChanged += locked =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    FlightLocked = locked;
                    if (locked && CurrentPageName != "InFlight" && CurrentPageName != "PostFlight")
                    {
                        NavigateTo("InFlight");
                    }
                });
            };
            AcarsContext.Runtime.Changed += OnRuntimeChanged;
            ApplyRuntimeState();
        }

        public async void LoadPilot()
        {
            var cachedPilot = AcarsContext.Auth.CurrentPilot;
            if (cachedPilot != null)
            {
                AcarsContext.Runtime.SetCurrentPilot(cachedPilot);
            }

            DashboardVM.LoadAsync();
            ProfileVM.LoadAsync();
            await PreFlightVM.LoadPreparedDispatchAsync();

            try
            {
                var result = await AcarsContext.Api.GetCurrentPilotAsync();
                if (result.Success && result.Data != null)
                {
                    AcarsContext.Auth.SetCurrentPilot(result.Data);
                    AcarsContext.Runtime.SetCurrentPilot(AcarsContext.Auth.CurrentPilot);
                    DashboardVM.LoadAsync();
                    ProfileVM.LoadAsync();
                    await PreFlightVM.LoadPreparedDispatchAsync();
                }
            }
            catch
            {
            }
        }

        private void OnRuntimeChanged()
        {
            Application.Current.Dispatcher.Invoke(ApplyRuntimeState);
        }

        private void ApplyRuntimeState()
        {
            Pilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
            SimConnected = AcarsContext.Runtime.IsSimulatorConnected;
            SimType = AcarsContext.Runtime.SimulatorType;

            if (SimConnected)
            {
                var backend = AcarsContext.Runtime.SimulatorBackend;
                SimStatusText = string.IsNullOrWhiteSpace(backend)
                    ? string.Format("Conectado · {0}", SimType)
                    : string.Format("Conectado · {0} · {1}", SimType, backend);
            }
            else
            {
                SimStatusText = string.IsNullOrWhiteSpace(AcarsContext.Runtime.SimulatorBackend)
                    ? "Sin simulador"
                    : string.Format("Esperando telemetría · {0}", AcarsContext.Runtime.SimulatorBackend);
            }
        }

        private bool CanNavigate(string page)
        {
            if (!FlightLocked)
            {
                return true;
            }

            return page == "InFlight" || (page == "PostFlight" && CanAccessPostFlight);
        }

        private void NavigateTo(string page)
        {
            if (!CanNavigate(page))
            {
                return;
            }

            CurrentPageName = page;
            if (page == "Profile")
            {
                ProfileVM.LoadAsync();
            }
            else if (page == "PreFlight")
            {
                _ = PreFlightVM.LoadPreparedDispatchAsync();
            }
            else if (page == "Dashboard")
            {
                DashboardVM.LoadAsync();
            }
        }

        public void ShowPostFlightReport(FlightReport report)
        {
            PostFlightVM.LoadReport(report);
            NavigateTo("PostFlight");
        }

        public void NavigateToSimulatorConnect()
        {
            NavigateTo("InFlight");
        }

        private void DoLogout()
        {
            AcarsContext.Auth.Logout();
            AcarsContext.Runtime.ResetAll();
            _clockTimer.Stop();
            OnLogout?.Invoke();
        }
    }
}
