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
        private object? _currentPage;
        private string _currentPageName = "Dashboard";
        private bool _flightLocked;

        public object? CurrentPage { get => _currentPage; set => SetField(ref _currentPage, value); }
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

        private Pilot? _pilot;
        public Pilot? Pilot
        {
            get => _pilot;
            set
            {
                if (SetField(ref _pilot, value))
                {
                    OnPropertyChanged(nameof(PilotDisplay));
                    if (value != null)
                    {
                        ProfileVM.ApplyPilotSnapshot(value);
                    }
                }
            }
        }

        public string PilotDisplay => Pilot != null ? $"{Pilot.CallSign} · {Pilot.RankName}" : string.Empty;

        private bool _simConnected;
        private SimulatorType _simType = SimulatorType.None;
        private string _simStatusText = "Sin simulador";

        public bool SimConnected
        {
            get => _simConnected;
            set
            {
                if (SetField(ref _simConnected, value))
                {
                    OnPropertyChanged(nameof(SimStatusColor));
                }
            }
        }

        public SimulatorType SimType { get => _simType; set => SetField(ref _simType, value); }
        public string SimStatusText { get => _simStatusText; set => SetField(ref _simStatusText, value); }
        public string SimStatusColor => _simConnected ? "#44CC44" : "#FF4444";

        private FlightPhase _flightPhase = FlightPhase.Disconnected;
        public FlightPhase FlightPhase { get => _flightPhase; set => SetField(ref _flightPhase, value); }

        private string _utcTime = string.Empty;
        public string UtcTime { get => _utcTime; set => SetField(ref _utcTime, value); }

        public ICommand NavDashboardCommand { get; }
        public ICommand NavPreFlightCommand { get; }
        public ICommand NavInFlightCommand { get; }
        public ICommand NavProfileCommand { get; }
        public ICommand NavCommunityCommand { get; }
        public ICommand LogoutCommand { get; }

        public Action? OnLogout { get; set; }

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
        }

        public async void LoadPilot()
        {
            // Mostrar datos locales mientras se conecta
            Pilot = AcarsContext.Auth.CurrentPilot;
            if (Pilot != null)
            {
                ProfileVM.ApplyPilotSnapshot(Pilot);
            }

            // Si el token expiró, refrescar antes de cualquier llamada a Supabase
            if (AcarsContext.Auth.HasExpiredToken)
            {
                await AcarsContext.TryRefreshSessionAsync();
                // Actualizar referencia local tras refresh
                Pilot = AcarsContext.Auth.CurrentPilot;
                if (Pilot != null) ProfileVM.ApplyPilotSnapshot(Pilot);
            }

            DashboardVM.LoadAsync();
            _ = PreFlightVM.LoadPreparedDispatchAsync();
            ProfileVM.LoadAsync();

            try
            {
                var result = await AcarsContext.Api.GetCurrentPilotAsync();
                if (result.Success && result.Data != null)
                {
                    AcarsContext.Auth.SaveSession(result.Data);
                    Pilot = result.Data;
                    ProfileVM.ApplyPilotSnapshot(result.Data);
                    DashboardVM.LoadAsync();
                    _ = PreFlightVM.LoadPreparedDispatchAsync();
                    ProfileVM.LoadAsync();
                }
            }
            catch
            {
                // Keep the restored local session if Supabase is temporarily unavailable.
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

            if (page == "Profile")
            {
                if (Pilot != null)
                {
                    ProfileVM.ApplyPilotSnapshot(Pilot);
                }
                else
                {
                    ProfileVM.LoadAsync();
                }
            }

            CurrentPageName = page;
            OnPropertyChanged(nameof(CurrentPageName));
        }

        public void ShowPostFlightReport(FlightReport report)
        {
            PostFlightVM.LoadReport(report);
            NavigateTo("PostFlight");
        }

        private void DoLogout()
        {
            AcarsContext.Auth.Logout();
            _clockTimer.Stop();
            OnLogout?.Invoke();
        }
    }
}
