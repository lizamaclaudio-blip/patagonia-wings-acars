using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PatagoniaWings.Acars.Master.Helpers;
using PatagoniaWings.Acars.Master.Preview;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public enum AcarsShellStep
    {
        Login = 0,
        PilotLounge = 1,
        NoDispatch = 2,
        Dispatch = 3,
        LiveFlight = 4,
        CloseFlight = 5,
        Support = 6
    }

    public class AcarsShellViewModel : ViewModelBase
    {
        private const string DefaultPatagoniaPortalUrl = "https://www.patagoniaw.com";
        private static readonly string ShellLogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PatagoniaWings", "Acars", "logs", "auth.log");
        private MainViewModel _mainVm = null!;
        private AcarsShellStep _currentStep = AcarsShellStep.Login;
        private AcarsShellStep _lastOperationalStep = AcarsShellStep.PilotLounge;
        private ImageSource? _pilotAvatarSource;

        public AcarsShellViewModel()
        {
            LoginVM = new LoginViewModel();
            LoginVM.OnLoginSuccess = HandleLoginSuccess;

            SupportVM = new SupportViewModel
            {
                RequestBack = GoBackFromSupport,
                RequestLogout = HandleLogout
            };

            MainVM = CreateMainViewModel();

            OpenSupportCommand = new RelayCommand(OpenSupport, () => CurrentStep != AcarsShellStep.Login);
            GoPilotLoungeCommand = new RelayCommand(GoPilotLounge, () => CurrentStep != AcarsShellStep.Login && MainVM.NavDashboardCommand.CanExecute(null));
            GoDispatchCommand = new RelayCommand(GoDispatch, () => CurrentStep != AcarsShellStep.Login && MainVM.NavPreFlightCommand.CanExecute(null));
            BeginDispatchFlowCommand = new AsyncRelayCommand(async _ => await BeginDispatchFlowAsync(), _ => CurrentStep == AcarsShellStep.PilotLounge);
            RetryDispatchResolutionCommand = new AsyncRelayCommand(async _ => await RetryDispatchResolutionAsync(), _ => CurrentStep == AcarsShellStep.NoDispatch);
            GoLiveFlightCommand = new RelayCommand(GoLiveFlight, () => CurrentStep != AcarsShellStep.Login);
            GoCloseFlightCommand = new RelayCommand(GoCloseFlight, () => CurrentStep != AcarsShellStep.Login && MainVM.CanAccessPostFlight && MainVM.PostFlightVM.Report != null);
            OpenPatagoniaPortalCommand = new RelayCommand(OpenPatagoniaPortal);
        }

        public LoginViewModel LoginVM { get; }
        public SupportViewModel SupportVM { get; }
        // TEMP/PREVIEW: fallback solo de presentacion para revisar el shell sin contaminar Core.
        public bool UsePreviewData => AcarsPreviewData.Enabled;

        public MainViewModel MainVM
        {
            get => _mainVm;
            private set
            {
                if (ReferenceEquals(_mainVm, value))
                {
                    return;
                }

                if (_mainVm != null)
                {
                    DetachMainViewModelSubscriptions(_mainVm);
                }

                _mainVm = value;
                AttachMainViewModelSubscriptions(_mainVm);
                OnPropertyChanged();
                RaisePilotPresentationChanged();
            }
        }

        public ICommand OpenSupportCommand { get; }
        public ICommand GoPilotLoungeCommand { get; }
        public ICommand GoDispatchCommand { get; }
        public ICommand BeginDispatchFlowCommand { get; }
        public ICommand RetryDispatchResolutionCommand { get; }
        public ICommand GoLiveFlightCommand { get; }
        public ICommand GoCloseFlightCommand { get; }
        public ICommand OpenPatagoniaPortalCommand { get; }

        public AcarsShellStep CurrentStep
        {
            get => _currentStep;
            private set
            {
                if (SetField(ref _currentStep, value))
                {
                    if (value != AcarsShellStep.Login && value != AcarsShellStep.Support)
                    {
                        _lastOperationalStep = value;
                    }

                    OnPropertyChanged(nameof(StepBadge));
                    OnPropertyChanged(nameof(HeaderTitle));
                    OnPropertyChanged(nameof(HeaderSubtitle));
                    OnPropertyChanged(nameof(FooterStatus));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string StepBadge
        {
            get
            {
                if (CurrentStep == AcarsShellStep.Support)
                {
                    return "AUX";
                }

                var stepNumber = CurrentStep switch
                {
                    AcarsShellStep.Login => 1,
                    AcarsShellStep.PilotLounge => 2,
                    AcarsShellStep.NoDispatch => 3,
                    AcarsShellStep.Dispatch => 3,
                    AcarsShellStep.LiveFlight => 4,
                    AcarsShellStep.CloseFlight => 5,
                    _ => 1
                };

                return string.Format("PASO {0}/6", stepNumber);
            }
        }

        public string HeaderTitle
        {
            get
            {
                return CurrentStep switch
                {
                    AcarsShellStep.Login => "INICIAR SESION",
                    AcarsShellStep.PilotLounge => "SALA DE PILOTOS",
                    AcarsShellStep.NoDispatch => "SIN VUELO DESPACHADO",
                    AcarsShellStep.Dispatch => "OFICINA DE DESPACHO",
                    AcarsShellStep.LiveFlight => "VUELO EN VIVO",
                    AcarsShellStep.CloseFlight => "CERRAR VUELO",
                    AcarsShellStep.Support => "SOPORTE",
                    _ => "PATAGONIA WINGS"
                };
            }
        }

        public string HeaderSubtitle
        {
            get
            {
                return CurrentStep switch
                {
                    AcarsShellStep.Login => "Acceso seguro al flujo ACARS",
                    AcarsShellStep.PilotLounge => "Resumen operativo del piloto y comunidad",
                    AcarsShellStep.NoDispatch => "No hay reserva activa o dispatch listo en la web",
                    AcarsShellStep.Dispatch => "Despacho, gate y preparacion de salida",
                    AcarsShellStep.LiveFlight => "Telemetria y estado operacional en tiempo real",
                    AcarsShellStep.CloseFlight => "Consolidacion de cierre y envio PIREP",
                    AcarsShellStep.Support => "Configuracion, diagnostico y utilidades",
                    _ => string.Empty
                };
            }
        }

        public string FooterStatus
        {
            get
            {
                return CurrentStep switch
                {
                    AcarsShellStep.Login => "ACARS 6.0 listo",
                    AcarsShellStep.Support => "Configuracion y diagnostico",
                    AcarsShellStep.NoDispatch => "Esperando reserva o despacho desde Patagonia Wings Web",
                    AcarsShellStep.CloseFlight => "Cierre y sincronizacion post-vuelo",
                    _ => MainVM?.SimStatusText ?? "Conectado a red Patagonia Wings"
                };
            }
        }

        public string NoDispatchTitle => "NO TIENES UN VUELO DESPACHADO";
        public string NoDispatchBody => "Debes generar o reservar un vuelo desde Patagonia Wings Web antes de continuar con el flujo ACARS.";
        public string NoDispatchHint => "Cuando ya tengas una reserva activa o un dispatch preparado, usa Reintentar para volver a consultar sin cerrar la app.";

        // TEMP/PREVIEW: mientras falten datos reales en ciertas vistas, estas props consumen AcarsPreviewData.
        public string PilotCallsignDisplay
            => !string.IsNullOrWhiteSpace(MainVM.Pilot?.CallSign)
                ? MainVM.Pilot!.CallSign
                : !string.IsNullOrWhiteSpace(MainVM.Pilot?.Username)
                    ? MainVM.Pilot!.Username.ToUpperInvariant()
                    : UsePilotLoungePreviewFallback
                        ? AcarsPreviewData.PilotCallsign
                        : "----";

        public string PilotNameDisplay
            => !string.IsNullOrWhiteSpace(MainVM.Pilot?.FullName)
                ? MainVM.Pilot!.FullName
                : !string.IsNullOrWhiteSpace(MainVM.Pilot?.Email)
                    ? MainVM.Pilot!.Email
                    : UsePilotLoungePreviewFallback
                        ? AcarsPreviewData.PilotName
                        : "Piloto Patagonia Wings";

        public string PilotHoursDisplay
            => MainVM.Pilot != null
                ? ResolvePilotHoursDisplay()
                : AcarsPreviewData.PilotHours;

        public string PilotScoreDisplay
            => MainVM.Pilot != null
                ? MainVM.Pilot.Points > 0
                    ? MainVM.Pilot.Points.ToString(CultureInfo.InvariantCulture)
                    : "--"
                : AcarsPreviewData.PilotScore;

        public string PilotRankDisplay
            => MainVM.Pilot != null
                ? ResolvePilotRankDisplay()
                : AcarsPreviewData.PilotRank;

        public string PilotLocationCodeDisplay
            => MainVM.Pilot != null
                ? ResolvePilotLocationCodeDisplay()
                : AcarsPreviewData.PilotLocationCode;

        public string PilotLocationSubtitle
            => MainVM.Pilot != null
                ? ResolvePilotLocationSubtitle()
                : AcarsPreviewData.PilotLocationSubtitle;

        public ImageSource? PilotAvatarSource => _pilotAvatarSource;

        public string OnlinePilotsDisplay
            => MainVM.Pilot != null
                ? MainVM.CommunityVM.OnlinePilots.Count.ToString(CultureInfo.InvariantCulture)
                : AcarsPreviewData.CommunityOnline;

        public string BasePilotsDisplay
        {
            get
            {
                var currentIcaoTrimmed = MainVM.Pilot?.CurrentAirportCode?.Trim() ?? string.Empty;
                if (currentIcaoTrimmed.Length > 0)
                {
                    var atBase = MainVM.CommunityVM.OnlinePilots.Count(p =>
                        !string.IsNullOrWhiteSpace(p.CurrentAirportCode) &&
                        string.Equals(p.CurrentAirportCode.Trim(), currentIcaoTrimmed, StringComparison.OrdinalIgnoreCase));
                    if (atBase > 0)
                    {
                        return atBase.ToString(CultureInfo.InvariantCulture);
                    }
                }

                return MainVM.Pilot != null ? "0" : AcarsPreviewData.CommunityAtBase;
            }
        }

        private MainViewModel CreateMainViewModel()
        {
            var vm = new MainViewModel();
            vm.OnLogout = HandleLogout;
            return vm;
        }

        private void AttachMainViewModelSubscriptions(MainViewModel vm)
        {
            vm.PropertyChanged += OnMainVmPropertyChanged;
            vm.DashboardVM.PropertyChanged += OnDashboardVmPropertyChanged;
            vm.CommunityVM.OnlinePilots.CollectionChanged += OnOnlinePilotsCollectionChanged;
        }

        private void DetachMainViewModelSubscriptions(MainViewModel vm)
        {
            vm.PropertyChanged -= OnMainVmPropertyChanged;
            vm.DashboardVM.PropertyChanged -= OnDashboardVmPropertyChanged;
            vm.CommunityVM.OnlinePilots.CollectionChanged -= OnOnlinePilotsCollectionChanged;
        }

        private void OnMainVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentPageName))
            {
                SyncStepFromMainPage();
            }
            else if (e.PropertyName == nameof(MainViewModel.SimStatusText))
            {
                OnPropertyChanged(nameof(FooterStatus));
            }

            if (e.PropertyName == nameof(MainViewModel.CurrentPageName)
                || e.PropertyName == nameof(MainViewModel.FlightLocked)
                || e.PropertyName == nameof(MainViewModel.CanAccessPostFlight))
            {
                CommandManager.InvalidateRequerySuggested();
            }

            if (e.PropertyName == nameof(MainViewModel.Pilot))
            {
                RaisePilotPresentationChanged();
            }
        }

        private void HandleLoginSuccess()
        {
            SavedLoginStore.SaveOrClear(LoginVM.Username, LoginVM.Password, LoginVM.RememberMe);

            WriteShellLog("PilotLounge load start [login_success]");
            GoPilotLounge();
        }

        private void HandleLogout()
        {
            SupportVM.StatusMessage = string.Empty;
            MainVM = CreateMainViewModel();
            CurrentStep = AcarsShellStep.Login;
        }

        private void SyncStepFromMainPage()
        {
            if (CurrentStep == AcarsShellStep.Support || CurrentStep == AcarsShellStep.Login)
            {
                return;
            }

            CurrentStep = MainVM.CurrentPageName switch
            {
                "Dashboard" => AcarsShellStep.PilotLounge,
                "PreFlight" => AcarsShellStep.Dispatch,
                "InFlight" => AcarsShellStep.LiveFlight,
                "PostFlight" => AcarsShellStep.CloseFlight,
                _ => CurrentStep
            };
        }

        private void OpenSupport()
        {
            WriteShellLog("Support clicked");
            if (CurrentStep != AcarsShellStep.Login)
            {
                _lastOperationalStep = CurrentStep;
            }

            CurrentStep = AcarsShellStep.Support;
        }

        private void GoBackFromSupport()
        {
            switch (_lastOperationalStep)
            {
                case AcarsShellStep.Dispatch:
                    GoDispatch();
                    break;
                case AcarsShellStep.NoDispatch:
                    CurrentStep = AcarsShellStep.NoDispatch;
                    break;
                case AcarsShellStep.LiveFlight:
                    GoLiveFlight();
                    break;
                case AcarsShellStep.CloseFlight:
                    GoCloseFlight();
                    break;
                case AcarsShellStep.PilotLounge:
                default:
                    GoPilotLounge();
                    break;
            }
        }

        private void GoPilotLounge()
        {
            if (CurrentStep == AcarsShellStep.NoDispatch)
            {
                WriteShellLog("NoDispatch back clicked");
            }

            if (!MainVM.NavDashboardCommand.CanExecute(null))
            {
                WriteShellLog("PilotLounge navigation blocked by current state.");
                return;
            }

            MainVM.NavDashboardCommand.Execute(null);
            CurrentStep = AcarsShellStep.PilotLounge;
            LoadPilotLoungeData("go_pilot_lounge");
        }

        private void GoDispatch()
        {
            if (!MainVM.NavPreFlightCommand.CanExecute(null))
            {
                return;
            }

            MainVM.CurrentPageName = "PreFlight";
            CurrentStep = AcarsShellStep.Dispatch;
        }

        private async Task BeginDispatchFlowAsync()
        {
            WriteShellLog("Begin clicked");
            await ResolveDispatchStepAsync("Begin").ConfigureAwait(true);
        }

        private async Task RetryDispatchResolutionAsync()
        {
            WriteShellLog("NoDispatch retry clicked");
            await ResolveDispatchStepAsync("NoDispatch retry").ConfigureAwait(true);
        }

        private async Task ResolveDispatchStepAsync(string source)
        {
            try
            {
                await MainVM.PreFlightVM.LoadPreparedDispatchAsync().ConfigureAwait(true);

                if (MainVM.PreFlightVM.HasUsableWebDispatch)
                {
                    GoDispatch();
                    if (CurrentStep == AcarsShellStep.Dispatch)
                    {
                        WriteShellLog(source + " -> Dispatch");
                        return;
                    }

                    MainVM.PreFlightVM.StatusMessage = "El despacho web existe, pero no pude abrir Oficina de Despacho desde el shell actual.";
                }
            }
            catch (Exception ex)
            {
                MainVM.PreFlightVM.StatusMessage = string.IsNullOrWhiteSpace(ex.Message)
                    ? "No pude consultar el despacho activo. Intenta nuevamente o revisa la web."
                    : "No pude consultar el despacho activo: " + ex.Message;
            }

            ApplyNoDispatchState();
            CurrentStep = AcarsShellStep.NoDispatch;
            WriteShellLog(source + " -> stay NoDispatch");
        }

        private void GoLiveFlight()
        {
            if (MainVM.NavInFlightCommand.CanExecute(null))
            {
                MainVM.NavInFlightCommand.Execute(null);
            }

            CurrentStep = AcarsShellStep.LiveFlight;
        }

        private void GoCloseFlight()
        {
            if (MainVM.PostFlightVM.Report == null || !MainVM.CanAccessPostFlight)
            {
                return;
            }

            CurrentStep = AcarsShellStep.CloseFlight;
        }

        private void OpenPatagoniaPortal()
        {
            WriteShellLog("NoDispatch reserve clicked");
            WriteShellLog("NoDispatch portal clicked => " + DefaultPatagoniaPortalUrl);
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = DefaultPatagoniaPortalUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        public void LoadPilotLoungeData(string reason)
        {
            reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason.Trim();
            WriteShellLog("PilotLounge load start [" + reason + "]");

            try
            {
                var pilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
                if (pilot != null)
                {
                    if (AcarsContext.Runtime.CurrentPilot == null)
                    {
                        AcarsContext.Runtime.SetCurrentPilot(pilot);
                    }

                    MainVM.Pilot = pilot;
                    RaisePilotPresentationChanged();
                    WriteShellLog("PilotLounge load seed [" + reason + "] => callsign=" + SafeForLog(pilot.CallSign));
                }

                MainVM.LoadPilot();
                WriteShellLog("PilotLounge load remote refresh queued [" + reason + "]");
            }
            catch (Exception ex)
            {
                WriteShellLog("PilotLounge load fail [" + reason + "] => " + ex.Message);
            }
        }

        private void ApplyNoDispatchState()
        {
            var preFlightVm = MainVM.PreFlightVM;
            if (preFlightVm.LastDispatchUsedLocalFallback)
            {
                preFlightVm.StatusMessage = "No hay un despacho web activo ahora. El ACARS conservo solo el ultimo snapshot local, pero debes generar o reservar un vuelo en Patagonia Wings Web.";
                return;
            }

            if (preFlightVm.LastDispatchResolvedFromWeb && !preFlightVm.HasUsablePreparedDispatch)
            {
                preFlightVm.StatusMessage = "La reserva existe en la web, pero todavia no trae datos suficientes para entrar a Oficina de Despacho.";
                return;
            }

            if (string.IsNullOrWhiteSpace(preFlightVm.StatusMessage))
            {
                preFlightVm.StatusMessage = "No tienes un vuelo despachado actualmente. Debes generar o reservar un vuelo desde Patagonia Wings Web antes de continuar.";
            }
        }

        private void OnOnlinePilotsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(OnlinePilotsDisplay));
            OnPropertyChanged(nameof(BasePilotsDisplay));
        }

        private void OnDashboardVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DashboardViewModel.TotalHours)
                || e.PropertyName == nameof(DashboardViewModel.RankName))
            {
                RaisePilotPresentationChanged();
            }
        }

        private void RaisePilotPresentationChanged()
        {
            RefreshPilotAvatarSource();
            OnPropertyChanged(nameof(PilotCallsignDisplay));
            OnPropertyChanged(nameof(PilotNameDisplay));
            OnPropertyChanged(nameof(PilotHoursDisplay));
            OnPropertyChanged(nameof(PilotScoreDisplay));
            OnPropertyChanged(nameof(PilotRankDisplay));
            OnPropertyChanged(nameof(PilotLocationCodeDisplay));
            OnPropertyChanged(nameof(PilotLocationSubtitle));
            OnPropertyChanged(nameof(PilotAvatarSource));
            OnPropertyChanged(nameof(OnlinePilotsDisplay));
            OnPropertyChanged(nameof(BasePilotsDisplay));
        }

        private bool UsePilotLoungePreviewFallback => UsePreviewData && MainVM.Pilot == null;

        private string ResolvePilotHoursDisplay()
        {
            if (!string.IsNullOrWhiteSpace(MainVM.DashboardVM.TotalHours) && MainVM.DashboardVM.TotalHours != "0.0")
            {
                return MainVM.DashboardVM.TotalHours;
            }

            return MainVM.Pilot != null
                ? MainVM.Pilot.TotalHours.ToString("F1", CultureInfo.InvariantCulture)
                : "0.0";
        }

        private string ResolvePilotRankDisplay()
        {
            if (!string.IsNullOrWhiteSpace(MainVM.DashboardVM.RankName))
            {
                return MainVM.DashboardVM.RankName;
            }

            return !string.IsNullOrWhiteSpace(MainVM.Pilot?.RankName)
                ? MainVM.Pilot!.RankName
                : "Sin rango";
        }

        private string ResolvePilotLocationCodeDisplay()
        {
            if (!string.IsNullOrWhiteSpace(MainVM.Pilot?.CurrentAirportCode))
            {
                return MainVM.Pilot!.CurrentAirportCode.Trim().ToUpperInvariant();
            }

            if (!string.IsNullOrWhiteSpace(MainVM.Pilot?.BaseHubCode))
            {
                return MainVM.Pilot!.BaseHubCode.Trim().ToUpperInvariant();
            }

            return "----";
        }

        private string ResolvePilotLocationSubtitle()
        {
            if (!string.IsNullOrWhiteSpace(MainVM.Pilot?.BaseHubCode))
            {
                return "Base " + MainVM.Pilot!.BaseHubCode.Trim().ToUpperInvariant();
            }

            return "Base no disponible";
        }

        private void RefreshPilotAvatarSource()
        {
            _pilotAvatarSource = BuildPilotAvatarSource(MainVM.Pilot?.AvatarUrl);
        }

        private static ImageSource? BuildPilotAvatarSource(string? avatarUrl)
        {
            if (string.IsNullOrWhiteSpace(avatarUrl))
            {
                return null;
            }

            try
            {
                var normalizedAvatarUrl = avatarUrl!.Trim();
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(normalizedAvatarUrl, UriKind.Absolute);
                bitmap.DecodePixelWidth = 120;
                bitmap.CacheOption = BitmapCacheOption.OnDemand;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        private static string SafeForLog(string? value)
            => string.IsNullOrWhiteSpace(value) ? "n/a" : (value?.Trim() ?? "n/a");

        private static void WriteShellLog(string message)
        {
            try
            {
                var folder = Path.GetDirectoryName(ShellLogFile);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                File.AppendAllText(
                    ShellLogFile,
                    "[" + DateTime.UtcNow.ToString("o") + "] " + message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
