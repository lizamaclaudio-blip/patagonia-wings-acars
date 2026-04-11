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
        // ── Navegación ────────────────────────────────────────────────────────
        private object? _currentPage;
        private string _currentPageName = "Dashboard";

        public object? CurrentPage { get => _currentPage; set => SetField(ref _currentPage, value); }
        public string CurrentPageName { get => _currentPageName; set => SetField(ref _currentPageName, value); }

        // ── Piloto ────────────────────────────────────────────────────────────
        private Pilot? _pilot;
        public Pilot? Pilot { get => _pilot; set { SetField(ref _pilot, value); OnPropertyChanged(nameof(PilotDisplay)); } }
        public string PilotDisplay => Pilot != null ? $"{Pilot.CallSign} · {Pilot.RankName}" : string.Empty;

        // ── Simulador ─────────────────────────────────────────────────────────
        private bool _simConnected;
        private SimulatorType _simType = SimulatorType.None;
        private string _simStatusText = "Desconectado";
        private string _simStatusColor = "#FF4444";

        public bool SimConnected { get => _simConnected; set { SetField(ref _simConnected, value); OnPropertyChanged(nameof(SimStatusColor)); OnPropertyChanged(nameof(SimStatusText)); } }
        public SimulatorType SimType { get => _simType; set => SetField(ref _simType, value); }
        public string SimStatusText { get => _simConnected ? $"Conectado · {_simType}" : "Sin simulador"; set => SetField(ref _simStatusText, value); }
        public string SimStatusColor => _simConnected ? "#44CC44" : "#FF4444";

        // ── Fase de vuelo ─────────────────────────────────────────────────────
        private FlightPhase _flightPhase = FlightPhase.Disconnected;
        public FlightPhase FlightPhase { get => _flightPhase; set => SetField(ref _flightPhase, value); }

        // ── Reloj UTC ─────────────────────────────────────────────────────────
        private string _utcTime = string.Empty;
        public string UtcTime { get => _utcTime; set => SetField(ref _utcTime, value); }

        // ── Comandos de navegación ────────────────────────────────────────────
        public ICommand NavDashboardCommand { get; }
        public ICommand NavPreFlightCommand { get; }
        public ICommand NavInFlightCommand { get; }
        public ICommand NavProfileCommand { get; }
        public ICommand NavCommunityCommand { get; }
        public ICommand LogoutCommand { get; }

        public Action? OnLogout { get; set; }

        private readonly System.Windows.Threading.DispatcherTimer _clockTimer;

        // Sub-viewmodels
        public DashboardViewModel DashboardVM { get; } = new DashboardViewModel();
        public PreFlightViewModel PreFlightVM { get; } = new PreFlightViewModel();
        public InFlightViewModel InFlightVM { get; }
        public PostFlightViewModel PostFlightVM { get; } = new PostFlightViewModel();
        public ProfileViewModel ProfileVM { get; } = new ProfileViewModel();
        public CommunityViewModel CommunityVM { get; } = new CommunityViewModel();

        public MainViewModel()
        {
            InFlightVM = new InFlightViewModel(this);

            NavDashboardCommand = new RelayCommand(() => NavigateTo("Dashboard"));
            NavPreFlightCommand = new RelayCommand(() => NavigateTo("PreFlight"));
            NavInFlightCommand = new RelayCommand(() => NavigateTo("InFlight"));
            NavProfileCommand = new RelayCommand(() => NavigateTo("Profile"));
            NavCommunityCommand = new RelayCommand(() => NavigateTo("Community"));
            LogoutCommand = new RelayCommand(DoLogout);

            _clockTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, __) => UtcTime = DateTime.UtcNow.ToString("HH:mm:ss") + " UTC";
            _clockTimer.Start();

            // Suscribir cambios de fase
            AcarsContext.FlightService.PhaseChanged += p =>
            {
                Application.Current.Dispatcher.Invoke(() => FlightPhase = p);
            };
        }

        public void LoadPilot()
        {
            Pilot = AcarsContext.Auth.CurrentPilot;
            DashboardVM.LoadAsync();
            ProfileVM.LoadAsync();
        }

        private void NavigateTo(string page)
        {
            CurrentPageName = page;
            OnPropertyChanged(nameof(CurrentPageName));
        }

        private void DoLogout()
        {
            AcarsContext.Auth.Logout();
            _clockTimer.Stop();
            OnLogout?.Invoke();
        }
    }
}
