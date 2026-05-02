using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Core.Services;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public class PreFlightViewModel : ViewModelBase
    {
        private PreparedDispatch? _preparedDispatch;
        private AcarsReadyFlight? _readyFlight;
        private string _flightNumber = string.Empty;
        private string _departureIcao = string.Empty;
        private string _arrivalIcao = string.Empty;
        private string _aircraftIcao = string.Empty;
        private string _route = string.Empty;
        private int _plannedAlt = 35000;
        private int _plannedSpeed = 450;
        private string _remarks = string.Empty;
        private SimulatorType _selectedSim = SimulatorType.MSFS2020;
        private WeatherInfo? _depWeather;
        private WeatherInfo? _arrWeather;
        private string _depMetar = string.Empty;
        private string _arrMetar = string.Empty;
        private bool _isLoadingMetar;
        private bool _isLoadingDispatch;
        private string _statusMessage = string.Empty;
        private bool _flightStarted;
        private readonly PatagoniaStartFlightGateService _startFlightGateService = new PatagoniaStartFlightGateService();
        private PatagoniaStartFlightGateResult? _startGateResult;
        private bool _lastDispatchResolvedFromWeb;
        private bool _lastDispatchUsedLocalFallback;

        public ObservableCollection<SimulatorType> SimulatorOptions { get; } = new ObservableCollection<SimulatorType>
        {
            SimulatorType.MSFS2020,
            SimulatorType.MSFS2024
        };

        public ICommand LoadDispatchCommand { get; }
        public ICommand FetchMetarCommand { get; }
        public ICommand StartFlightCommand { get; }

        public PreFlightViewModel()
        {
            LoadDispatchCommand = new AsyncRelayCommand(async _ => await LoadPreparedDispatchAsync());
            FetchMetarCommand = new AsyncRelayCommand(async _ => await LoadMetarAsync(), _ => HasFlightCoreData);
            StartFlightCommand = new AsyncRelayCommand(async _ => await StartFlightAsync(), _ => CanStartFlight);

            AcarsContext.Runtime.Changed += OnRuntimeChanged;
            ApplyPilotPreferences();
            SyncFromRuntime();
        }

        public PreparedDispatch? PreparedDispatch
        {
            get { return _preparedDispatch; }
            set
            {
                if (SetField(ref _preparedDispatch, value))
                {
                    OnPropertyChanged(nameof(HasPreparedDispatch));
                    OnPropertyChanged(nameof(IsDispatchReady));
                    OnPropertyChanged(nameof(DispatchStatusLabel));
                    OnPropertyChanged(nameof(DispatchStateLine));
                    OnPropertyChanged(nameof(DispatchSourceLine));
                    OnPropertyChanged(nameof(FlightModeLine));
                    OnPropertyChanged(nameof(FlightModeDisplayLabel));
                    OnPropertyChanged(nameof(IsOnlineFlightMode));
                    OnPropertyChanged(nameof(AircraftDisplayLine));
                    OnPropertyChanged(nameof(RouteDisplayLine));
                    OnPropertyChanged(nameof(RouteDisplayHeader));
                    OnPropertyChanged(nameof(FuelDisplayLine));
                    OnPropertyChanged(nameof(PayloadDisplayLine));
                    OnPropertyChanged(nameof(PayloadMainLine));
                    OnPropertyChanged(nameof(PayloadPaxLine));
                    OnPropertyChanged(nameof(PayloadCargoLine));
                    OnPropertyChanged(nameof(PayloadZfwLine));
                    OnPropertyChanged(nameof(BlockDisplayLine));
                    OnPropertyChanged(nameof(PlannedDistanceLine));
                    OnPropertyChanged(nameof(HasUsablePreparedDispatch));
                    OnPropertyChanged(nameof(HasUsableWebDispatch));
                    OnPropertyChanged(nameof(StartButtonTitle));
                    OnPropertyChanged(nameof(StartButtonSubtitle));
                    OnPropertyChanged(nameof(CanStartFlight));
                }
            }
        }

        public AcarsReadyFlight? ReadyFlight
        {
            get { return _readyFlight; }
            set { SetField(ref _readyFlight, value); }
        }

        public string FlightNumber { get { return _flightNumber; } set { if (SetField(ref _flightNumber, value)) OnPropertyChanged(nameof(HasFlightCoreData)); } }
        public string DepartureIcao { get { return _departureIcao; } set { if (SetField(ref _departureIcao, value)) OnPropertyChanged(nameof(HasFlightCoreData)); } }
        public string ArrivalIcao { get { return _arrivalIcao; } set { if (SetField(ref _arrivalIcao, value)) OnPropertyChanged(nameof(HasFlightCoreData)); } }
        public string AircraftIcao { get { return _aircraftIcao; } set { if (SetField(ref _aircraftIcao, value)) OnPropertyChanged(nameof(HasFlightCoreData)); } }
        public string Route { get { return _route; } set { if (SetField(ref _route, value)) OnPropertyChanged(nameof(HasFlightCoreData)); } }
        public int PlannedAlt { get { return _plannedAlt; } set { SetField(ref _plannedAlt, value); } }
        public int PlannedSpeed { get { return _plannedSpeed; } set { SetField(ref _plannedSpeed, value); } }
        public string Remarks { get { return _remarks; } set { SetField(ref _remarks, value); } }
        public SimulatorType SelectedSim { get { return _selectedSim; } set { if (SetField(ref _selectedSim, value)) { OnPropertyChanged(nameof(StartButtonSubtitle)); OnPropertyChanged(nameof(CanStartFlight)); } } }

        public WeatherInfo? DepWeather
        {
            get { return _depWeather; }
            set
            {
                if (SetField(ref _depWeather, value))
                {
                    OnPropertyChanged(nameof(DepartureWeatherSummary));
                    OnPropertyChanged(nameof(OperationalMinimaSummary));
                    OnPropertyChanged(nameof(OperationalQualificationsSummary));
                }
            }
        }

        public WeatherInfo? ArrWeather
        {
            get { return _arrWeather; }
            set
            {
                if (SetField(ref _arrWeather, value))
                {
                    OnPropertyChanged(nameof(ArrivalWeatherSummary));
                    OnPropertyChanged(nameof(OperationalMinimaSummary));
                    OnPropertyChanged(nameof(OperationalQualificationsSummary));
                }
            }
        }

        public string DepMetar { get { return _depMetar; } set { SetField(ref _depMetar, value); } }
        public string ArrMetar { get { return _arrMetar; } set { SetField(ref _arrMetar, value); } }
        public bool IsLoadingMetar { get { return _isLoadingMetar; } set { SetField(ref _isLoadingMetar, value); } }

        public bool IsLoadingDispatch
        {
            get { return _isLoadingDispatch; }
            set
            {
                if (SetField(ref _isLoadingDispatch, value))
                {
                    OnPropertyChanged(nameof(StartButtonTitle));
                    OnPropertyChanged(nameof(StartButtonSubtitle));
                    OnPropertyChanged(nameof(CanStartFlight));
                }
            }
        }

        public string StatusMessage
        {
            get { return _statusMessage; }
            set
            {
                if (SetField(ref _statusMessage, value))
                {
                    OnPropertyChanged(nameof(HasStatusMessage));
                }
            }
        }

        public bool FlightStarted
        {
            get { return _flightStarted; }
            set
            {
                if (SetField(ref _flightStarted, value))
                {
                    OnPropertyChanged(nameof(StartButtonTitle));
                    OnPropertyChanged(nameof(StartButtonSubtitle));
                    OnPropertyChanged(nameof(CanStartFlight));
                }
            }
        }

        public bool HasPreparedDispatch { get { return PreparedDispatch != null; } }
        public bool HasStatusMessage { get { return !string.IsNullOrWhiteSpace(StatusMessage); } }
        public bool LastDispatchResolvedFromWeb { get { return _lastDispatchResolvedFromWeb; } }
        public bool LastDispatchUsedLocalFallback { get { return _lastDispatchUsedLocalFallback; } }
        public bool HasUsablePreparedDispatch { get { return IsDispatchUsableForShell(PreparedDispatch); } }
        public bool HasUsableWebDispatch { get { return LastDispatchResolvedFromWeb && HasUsablePreparedDispatch; } }
        public bool HasFlightCoreData
        {
            get
            {
                return !string.IsNullOrWhiteSpace(FlightNumber)
                    || !string.IsNullOrWhiteSpace(DepartureIcao)
                    || !string.IsNullOrWhiteSpace(ArrivalIcao)
                    || !string.IsNullOrWhiteSpace(AircraftIcao)
                    || !string.IsNullOrWhiteSpace(Route);
            }
        }

        public bool IsDispatchReady { get { return PreparedDispatch != null && PreparedDispatch.IsDispatchReady; } }
        public bool StartGateAllowsStart { get { return _startGateResult != null && (_startGateResult.CanStart || CanStartWithColdAndDarkOverride()); } }
        public string StartGateSummary
        {
            get
            {
                if (_startGateResult == null) return "Gate previo pendiente.";
                if (!_startGateResult.CanStart && CanStartWithColdAndDarkOverride())
                    return "Gate previo OK: C208 Black Square validado con regla operacional Patagonia Wings.";
                return _startGateResult.Summary;
            }
        }
        public bool CanStartFlight { get { return PreparedDispatch != null && PreparedDispatch.IsDispatchReady && !IsLoadingDispatch && !FlightStarted && StartGateAllowsStart; } }

        public string DispatchStatusLabel
        {
            get
            {
                if (PreparedDispatch == null)
                {
                    return "SIN DESPACHO";
                }

                var reservationStatus = NormalizeToken(PreparedDispatch.ReservationStatus);
                var packageStatus = NormalizeToken(PreparedDispatch.DispatchPackageStatus);

                if (FlightStarted || reservationStatus == "IN_FLIGHT" || reservationStatus == "IN_PROGRESS")
                {
                    return "EN VUELO";
                }

                if (PreparedDispatch.IsDispatchReady)
                {
                    return "DESPACHO LISTO";
                }

                if (reservationStatus == "RESERVED")
                {
                    return "RESERVA ACTIVA";
                }

                if (!string.IsNullOrWhiteSpace(packageStatus))
                {
                    return packageStatus.Replace("_", " ");
                }

                return "SINCRONIZANDO";
            }
        }

        public string DispatchStateLine
        {
            get
            {
                if (PreparedDispatch == null)
                {
                    return "Sin reserva o dispatch activo en la web.";
                }

                return "Reserva " + NormalizeReadable(PreparedDispatch.ReservationStatus) + " · Paquete " + NormalizeReadable(PreparedDispatch.DispatchPackageStatus);
            }
        }

        public string DispatchSourceLine
        {
            get
            {
                if (PreparedDispatch == null)
                {
                    return "Fuente oficial: Web Patagonia Wings";
                }

                var origin = !string.IsNullOrWhiteSpace(PreparedDispatch.DispatchPackageStatus)
                    ? "Web Patagonia Wings · package " + PreparedDispatch.DispatchPackageStatus.Trim()
                    : "Web Patagonia Wings";
                return "Fuente oficial: " + origin;
            }
        }

        public string FlightModeLine
        {
            get
            {
                if (PreparedDispatch == null || string.IsNullOrWhiteSpace(PreparedDispatch.FlightMode))
                    return "Modo operativo no informado por la web.";
                return "Modo " + PreparedDispatch.FlightMode.Trim();
            }
        }

        public string FlightModeDisplayLabel
        {
            get
            {
                var mode = (PreparedDispatch?.FlightMode ?? string.Empty).Trim().ToUpperInvariant();
                return mode switch
                {
                    "CAREER"                         => "Itinerario",
                    "ITINERARY"                      => "Itinerario",
                    "CHARTER"                        => "Chárter",
                    "TRAINING"                       => "Entrenamiento",
                    "FREE_FLIGHT" or "FREE"          => "Vuelo Libre",
                    "EVENT"                          => "Evento",
                    "SPECIAL_MISSION" or "MISSION"   => "Misión Especial",
                    "CHECKRIDE"                      => "Checkride",
                    "ASSIGNMENT"                     => "Habilitación",
                    "TOUR"                           => "Tour",
                    _ when mode.Contains("CHARTER")  => "Chárter",
                    _ when mode.Contains("TRAIN")    => "Entrenamiento",
                    _ when mode.Contains("ITINERARY")=> "Itinerario",
                    _ when mode.Length > 0           => mode,
                    _                                => "Vuelo",
                };
            }
        }

        public bool IsOnlineFlightMode
        {
            get
            {
                var mode = (PreparedDispatch?.FlightMode ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(mode))
                {
                    return false;
                }

                return mode.Contains("ONLINE")
                    || mode.Contains("VATSIM")
                    || mode.Contains("IVAO")
                    || mode.Contains("NETWORK");
            }
        }

        public string AircraftDisplayLine
        {
            get
            {
                if (PreparedDispatch == null)
                {
                    return "Sin aeronave asignada";
                }

                var parts = new List<string>();
                var reg = Safe(PreparedDispatch.AircraftRegistration);
                var realName = ResolveAircraftRealName(PreparedDispatch);
                var airframe = ResolveAircraftAirframe(PreparedDispatch);

                if (!string.IsNullOrWhiteSpace(reg)) parts.Add(reg);
                if (!string.IsNullOrWhiteSpace(realName)) parts.Add(realName);
                if (!string.IsNullOrWhiteSpace(airframe)) parts.Add(airframe);

                return parts.Count == 0 ? "Sin aeronave asignada" : string.Join(" - ", parts.Distinct().ToArray());
            }
        }

        public string RouteDisplayHeader { get { return "RUTA:"; } }

        public string RouteDisplayLine
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Route))
                {
                    return Route;
                }

                if (PreparedDispatch != null && !string.IsNullOrWhiteSpace(PreparedDispatch.RouteCode))
                {
                    return PreparedDispatch.RouteCode;
                }

                return "Ruta pendiente desde despacho web.";
            }
        }

        public string FuelDisplayLine
        {
            get
            {
                if (PreparedDispatch == null)
                {
                    return "—";
                }

                if (PreparedDispatch.FuelPlannedKg > 0)
                {
                    return FormatKg(PreparedDispatch.FuelPlannedKg);
                }

                return "No informado";
            }
        }

        public string PayloadDisplayLine
        {
            get
            {
                var parts = new List<string>();
                if (!string.Equals(PayloadMainLine, "No informado", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(PayloadMainLine);
                }
                if (!string.Equals(PayloadPaxLine, "Pax: —", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(PayloadPaxLine);
                }
                if (!string.Equals(PayloadZfwLine, "ZFW: —", StringComparison.OrdinalIgnoreCase))
                {
                    parts.Add(PayloadZfwLine);
                }

                return parts.Count == 0 ? "No informado" : string.Join(" · ", parts.ToArray());
            }
        }

        public string PayloadMainLine
        {
            get
            {
                if (PreparedDispatch == null)
                {
                    return "—";
                }

                if (PreparedDispatch.PayloadKg > 0)
                {
                    return FormatKg(PreparedDispatch.PayloadKg);
                }

                return "No informado";
            }
        }

        public string PayloadPaxLine
        {
            get
            {
                if (PreparedDispatch == null || PreparedDispatch.PassengerCount <= 0)
                {
                    return "Pax: —";
                }

                return "Pax: " + PreparedDispatch.PassengerCount;
            }
        }

        public string PayloadCargoLine
        {
            get
            {
                if (PreparedDispatch == null || PreparedDispatch.PayloadKg <= 0)
                {
                    return "Carga: —";
                }

                return "Carga: " + FormatKg(PreparedDispatch.PayloadKg);
            }
        }

        public string PayloadZfwLine
        {
            get
            {
                if (PreparedDispatch == null || PreparedDispatch.ZeroFuelWeightKg <= 0)
                {
                    return "ZFW: —";
                }

                return "ZFW: " + FormatKg(PreparedDispatch.ZeroFuelWeightKg);
            }
        }

        public string BlockDisplayLine
        {
            get
            {
                if (PreparedDispatch == null)
                {
                    return "—";
                }

                if (PreparedDispatch.ScheduledBlockMinutes > 0)
                {
                    return FormatMinutes(PreparedDispatch.ScheduledBlockMinutes);
                }

                if (PreparedDispatch.ExpectedBlockP50Minutes > 0)
                {
                    return FormatMinutes(PreparedDispatch.ExpectedBlockP50Minutes);
                }

                if (PreparedDispatch.ExpectedBlockP80Minutes > 0)
                {
                    return FormatMinutes(PreparedDispatch.ExpectedBlockP80Minutes);
                }

                return "Sin block publicado";
            }
        }

        public string PlannedDistanceLine
        {
            get
            {
                if (PreparedDispatch == null)
                {
                    return "N/D";
                }

                if (PreparedDispatch.PlannedDistanceNm > 0)
                {
                    return Math.Round(PreparedDispatch.PlannedDistanceNm, 0).ToString("F0") + " NM";
                }

                return "N/D";
            }
        }
        public bool StartGateParkingBrakeOk { get { return IsGateRulePassing("START_PARKING_BRAKE_ON"); } }
        public bool StartGateColdAndDarkOk { get { return IsStartGateColdAndDarkOk(); } }
        public bool StartGateAircraftTypeOk { get { return IsStartGateAircraftMatchOk(); } }
        public bool StartGateAirportOk { get { return IsGateRulePassing("START_AIRPORT_MATCH"); } }
        public string StartGateParkingBrakeState => GetGateRuleVisualState("START_PARKING_BRAKE_ON");
        public string StartGateColdAndDarkState => GetGateRuleVisualState("START_COLD_AND_DARK");
        public string StartGateAircraftTypeState => GetGateRuleVisualState("START_AIRCRAFT_MATCH");
        public string StartGateAirportState => GetGateRuleVisualState("START_AIRPORT_MATCH");
        public string StartGateParkingBrakeLabel => GetGateRuleVisualLabel("START_PARKING_BRAKE_ON");
        public string StartGateColdAndDarkLabel => GetGateRuleVisualLabel("START_COLD_AND_DARK");
        public string StartGateAircraftTypeLabel => GetGateRuleVisualLabel("START_AIRCRAFT_MATCH");
        public string StartGateAirportLabel => GetGateRuleVisualLabel("START_AIRPORT_MATCH");

        public string StartButtonTitle
        {
            get
            {
                if (IsLoadingDispatch)
                {
                    return "SINCRONIZANDO DESPACHO";
                }

                if (FlightStarted)
                {
                    return "VUELO ACARS INICIADO";
                }

                if (CanStartFlight)
                {
                    return "INICIAR VUELO";
                }

                if (PreparedDispatch != null && PreparedDispatch.IsDispatchReady)
                {
                    return "GATE PREVIO PENDIENTE";
                }

                return "DESPACHO PENDIENTE";
            }
        }

        public string StartButtonSubtitle
        {
            get
            {
                if (IsLoadingDispatch)
                {
                    return "Leyendo reserva activa y dispatch preparado desde Patagonia Wings Web.";
                }

                if (FlightStarted)
                {
                    return "El ACARS ya tomó control del vuelo y bloqueó la navegación operacional.";
                }

                if (PreparedDispatch == null)
                {
                    return "Necesitas una reserva activa con dispatch preparado en la web.";
                }

                if (!PreparedDispatch.IsDispatchReady)
                {
                    return "La reserva existe, pero el dispatch aún no está liberado para ACARS.";
                }

                if (!StartGateAllowsStart)
                {
                    return StartGateSummary;
                }

                return "Sim seleccionado: " + SelectedSim + " · el inicio validará la reserva web antes de entrar en vuelo.";
            }
        }

        public string DepartureWeatherSummary { get { return BuildWeatherSummary(DepWeather); } }
        public string ArrivalWeatherSummary { get { return BuildWeatherSummary(ArrWeather); } }
        public string OperationalMinimaSummary { get { return BuildOperationalMinimaSummary(); } }
        public string OperationalQualificationsSummary { get { return BuildOperationalQualificationsSummary(); } }

        public async Task LoadPreparedDispatchAsync()
        {
            var pilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
            if (pilot == null || string.IsNullOrWhiteSpace(pilot.CallSign))
            {
                StatusMessage = "Inicia sesión para sincronizar la reserva activa desde Patagonia Wings Web.";
                return;
            }

            SetDispatchResolutionState(false, false);
            if (IsLoadingDispatch)
            {
                return;
            }

            IsLoadingDispatch = true;
            StatusMessage = "Sincronizando despacho web para " + pilot.CallSign.Trim().ToUpperInvariant() + "...";

            try
            {
                PreparedDispatch? dispatch = null;

                var preparedTask = AcarsContext.Api.GetPreparedDispatchAsync(pilot.CallSign);
                var preparedCompleted = await Task.WhenAny(preparedTask, Task.Delay(TimeSpan.FromSeconds(10)));
                if (preparedCompleted == preparedTask)
                {
                    var preparedResult = await preparedTask;
                    Debug.WriteLine("[PreFlight] prepared => success=" + preparedResult.Success + " error=" + preparedResult.Error);
                    if (preparedResult.Success)
                    {
                        dispatch = preparedResult.Data;
                    }
                    else if (!string.IsNullOrWhiteSpace(preparedResult.Error))
                    {
                        StatusMessage = preparedResult.Error;
                    }
                }
                else
                {
                    StatusMessage = "Timeout cargando despacho web. Revisa la conexión o vuelve a recargar.";
                }

                if (dispatch == null)
                {
                    var readyTask = AcarsContext.Api.GetReadyForAcarsFlightAsync(pilot.CallSign);
                    var readyCompleted = await Task.WhenAny(readyTask, Task.Delay(TimeSpan.FromSeconds(8)));
                    if (readyCompleted == readyTask)
                    {
                        var readyResult = await readyTask;
                        Debug.WriteLine("[PreFlight] ready => success=" + readyResult.Success + " error=" + readyResult.Error);
                        if (readyResult.Success && readyResult.Data != null)
                        {
                            ReadyFlight = readyResult.Data;
                            dispatch = readyResult.Data.ToPreparedDispatch();
                        }
                        else if (string.IsNullOrWhiteSpace(StatusMessage))
                        {
                            StatusMessage = string.IsNullOrWhiteSpace(readyResult.Error)
                                ? "No hay una reserva activa disponible para ACARS."
                                : readyResult.Error;
                        }
                    }
                }

                if (dispatch == null)
                {
                    if (AcarsContext.Runtime.CurrentReadyFlight != null)
                    {
                        SetDispatchResolutionState(false, true);
                        ReadyFlight = AcarsContext.Runtime.CurrentReadyFlight;
                        ApplyDispatchSnapshot(AcarsContext.Runtime.CurrentReadyFlight.ToPreparedDispatch());
                        StatusMessage = "Usando el último despacho almacenado localmente.";
                        _ = LoadMetarAsync();
                        return;
                    }

                    SetDispatchResolutionState(false, false);
                    ClearLoadedFlight(false);
                    if (string.IsNullOrWhiteSpace(StatusMessage))
                    {
                        StatusMessage = "No hay ninguna reserva activa. Prepara el vuelo primero desde Patagonia Wings Web.";
                    }
                    return;
                }

                SetDispatchResolutionState(true, false);
                ApplyDispatchSnapshot(dispatch);
                if (!HasUsablePreparedDispatch)
                {
                    ReadyFlight = null;
                    StatusMessage = "La reserva existe en Patagonia Wings Web, pero el despacho todavia no trae datos operativos suficientes para ACARS.";
                    return;
                }

                ReadyFlight = BuildReadyFlight(dispatch, pilot.CallSign);
                if (ReadyFlight != null)
                {
                    AcarsContext.Runtime.SetReadyFlight(ReadyFlight);
                }

                FlightStarted = false;
                StatusMessage = dispatch.IsDispatchReady
                    ? "Despacho listo desde la web. Revisa METAR y simulador antes de iniciar el ACARS."
                    : "Reserva cargada desde la web. El dispatch todavia no está liberado para ACARS.";

                _ = LoadMetarAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PreFlight] LoadPreparedDispatchAsync => " + ex);
                if (AcarsContext.Runtime.CurrentReadyFlight != null)
                {
                    SetDispatchResolutionState(false, true);
                    ReadyFlight = AcarsContext.Runtime.CurrentReadyFlight;
                    ApplyDispatchSnapshot(AcarsContext.Runtime.CurrentReadyFlight.ToPreparedDispatch());
                    StatusMessage = "Se mantuvo el último despacho local. Error remoto: " + ex.Message;
                }
                else
                {
                    SetDispatchResolutionState(false, false);
                    ClearLoadedFlight(false);
                    StatusMessage = "No se pudo sincronizar el despacho web: " + ex.Message;
                }
            }
            finally
            {
                IsLoadingDispatch = false;
            }
        }

        private void ApplyDispatchSnapshot(PreparedDispatch dispatch)
        {
            PreparedDispatch = dispatch;
            FlightNumber = FirstNonEmpty(dispatch.FlightDesignator, dispatch.FlightNumber, dispatch.RouteCode);
            DepartureIcao = Safe(dispatch.DepartureIcao).ToUpperInvariant();
            ArrivalIcao = Safe(dispatch.ArrivalIcao).ToUpperInvariant();
            AircraftIcao = FirstNonEmpty(dispatch.AircraftIcao, dispatch.AircraftDisplayName, dispatch.AircraftRegistration);
            Route = FirstNonEmpty(dispatch.RouteText, dispatch.RouteCode);
            Remarks = Safe(dispatch.Remarks);

            if (!string.IsNullOrWhiteSpace(dispatch.CruiseLevel))
            {
                var normalized = dispatch.CruiseLevel.Trim().ToUpperInvariant().Replace("FL", string.Empty);
                int parsed;
                if (int.TryParse(normalized, out parsed))
                {
                    PlannedAlt = parsed >= 1000 ? parsed : parsed * 100;
                }
            }

            ApplyPilotPreferences();
            RefreshStartGateStatus(false);
            OnPropertyChanged(nameof(DepartureWeatherSummary));
            OnPropertyChanged(nameof(ArrivalWeatherSummary));
            OnPropertyChanged(nameof(OperationalMinimaSummary));
            OnPropertyChanged(nameof(OperationalQualificationsSummary));
        }

        private void SetDispatchResolutionState(bool resolvedFromWeb, bool usedLocalFallback)
        {
            if (_lastDispatchResolvedFromWeb != resolvedFromWeb)
            {
                _lastDispatchResolvedFromWeb = resolvedFromWeb;
                OnPropertyChanged(nameof(LastDispatchResolvedFromWeb));
                OnPropertyChanged(nameof(HasUsableWebDispatch));
            }

            if (_lastDispatchUsedLocalFallback != usedLocalFallback)
            {
                _lastDispatchUsedLocalFallback = usedLocalFallback;
                OnPropertyChanged(nameof(LastDispatchUsedLocalFallback));
            }
        }

        private bool IsDispatchUsableForShell(PreparedDispatch? dispatch)
        {
            if (dispatch == null)
            {
                return false;
            }

            var hasFlightIdentity = !string.IsNullOrWhiteSpace(FirstNonEmpty(dispatch.FlightDesignator, dispatch.FlightNumber, dispatch.RouteCode));
            var hasOrigin = Safe(dispatch.DepartureIcao).Length >= 4;
            var hasDestination = Safe(dispatch.ArrivalIcao).Length >= 4;

            return !string.IsNullOrWhiteSpace(Safe(dispatch.ReservationId))
                && hasFlightIdentity
                && hasOrigin
                && hasDestination
                && dispatch.HasAssignedAircraft;
        }

        private AcarsReadyFlight? BuildReadyFlight(PreparedDispatch? dispatch, string pilotCallsign)
        {
            if (dispatch == null)
            {
                return null;
            }

            return new AcarsReadyFlight
            {
                ReservationId = dispatch.ReservationId,
                DispatchPackageId = dispatch.DispatchId,
                PilotCallsign = pilotCallsign,
                PilotUserId = dispatch.PilotUserId,
                RankCode = dispatch.RankCode,
                CareerRankCode = dispatch.CareerRankCode,
                BaseHubCode = dispatch.BaseHubCode,
                CurrentAirportCode = dispatch.CurrentAirportCode,
                FlightModeCode = dispatch.FlightMode,
                RouteCode = dispatch.RouteCode,
                FlightNumber = FirstNonEmpty(dispatch.FlightDesignator, dispatch.FlightNumber, dispatch.RouteCode),
                OriginIdent = dispatch.DepartureIcao,
                DestinationIdent = dispatch.ArrivalIcao,
                AircraftId = dispatch.AircraftId,
                AircraftRegistration = dispatch.AircraftRegistration,
                AircraftTypeCode = dispatch.AircraftIcao,
                AircraftDisplayName = dispatch.AircraftDisplayName,
                AircraftVariantCode = dispatch.AircraftVariantCode,
                AddonProvider = dispatch.AddonProvider,
                RouteText = FirstNonEmpty(dispatch.RouteText, dispatch.RouteCode),
                PlannedAltitude = PlannedAlt,
                PlannedSpeed = PlannedSpeed,
                CruiseLevel = dispatch.CruiseLevel,
                AlternateIcao = dispatch.AlternateIcao,
                DispatchToken = dispatch.DispatchToken,
                SimbriefUsername = dispatch.SimbriefUsername,
                Remarks = dispatch.Remarks,
                ScheduledDepartureUtc = dispatch.ScheduledDepartureUtc,
                ReadyForAcars = dispatch.IsDispatchReady,
                SimbriefStatus = dispatch.SimbriefStatus,
                ReservationStatus = dispatch.ReservationStatus,
                DispatchStatus = dispatch.DispatchPackageStatus,
                PassengerCount = dispatch.PassengerCount,
                CargoKg = dispatch.CargoKg,
                FuelPlannedKg = dispatch.FuelPlannedKg,
                PayloadKg = dispatch.PayloadKg,
                ZeroFuelWeightKg = dispatch.ZeroFuelWeightKg,
                ScheduledBlockMinutes = dispatch.ScheduledBlockMinutes,
                ExpectedBlockP50Minutes = dispatch.ExpectedBlockP50Minutes,
                ExpectedBlockP80Minutes = dispatch.ExpectedBlockP80Minutes
            };
        }

        private async Task StartFlightAsync()
        {
            if (PreparedDispatch == null)
            {
                StatusMessage = "No existe un dispatch listo desde la web.";
                return;
            }

            if (!PreparedDispatch.IsDispatchReady)
            {
                StatusMessage = "La reserva todavia no está en estado despachable. Complétala primero en la web.";
                return;
            }

            RefreshStartGateStatus(false);
            if (!StartGateAllowsStart)
            {
                StatusMessage = StartGateSummary;
                return;
            }

            var flight = new Flight
            {
                ReservationId = PreparedDispatch.ReservationId,
                DispatchPackageId = PreparedDispatch.DispatchId,
                AircraftId = PreparedDispatch.AircraftId,
                FlightNumber = Safe(FlightNumber).ToUpperInvariant(),
                DepartureIcao = Safe(DepartureIcao).ToUpperInvariant(),
                ArrivalIcao = Safe(ArrivalIcao).ToUpperInvariant(),
                AircraftIcao = Safe(AircraftIcao).ToUpperInvariant(),
                AircraftTypeCode = PreparedDispatch.AircraftIcao,
                AircraftName = string.IsNullOrWhiteSpace(PreparedDispatch.AircraftDisplayName)
                    ? Safe(AircraftIcao).ToUpperInvariant()
                    : PreparedDispatch.AircraftDisplayName,
                AircraftDisplayName = PreparedDispatch.AircraftDisplayName,
                AircraftVariantCode = PreparedDispatch.AircraftVariantCode,
                AddonProvider = PreparedDispatch.AddonProvider,
                Route = Safe(Route),
                FlightModeCode = PreparedDispatch.FlightMode,
                RouteCode = PreparedDispatch.RouteCode,
                PlannedAltitude = PlannedAlt,
                PlannedSpeed = PlannedSpeed,
                Simulator = SelectedSim,
                Remarks = Safe(Remarks),
                StartTime = DateTime.UtcNow
            };

            var result = await AcarsContext.Api.StartFlightAsync(flight, PreparedDispatch);
            if (result.Success)
            {
                var runtimeFuelLbs = AcarsContext.Runtime.LastTelemetry?.FuelTotalLbs ?? 0d;
                var initialFuelLbs = runtimeFuelLbs > 0
                    ? runtimeFuelLbs
                    : 0d;

                AcarsContext.FlightService.StartFlight(flight, initialFuelLbs);
                AcarsContext.Sound.PlayDing();
                _ = AcarsContext.Sound.PlayGroundBienvenidoAsync();
                FlightStarted = true;
                StatusMessage = "Vuelo " + FlightNumber + " iniciado. El ACARS tomó el despacho oficial desde la web.";
            }
            else
            {
                StatusMessage = "No se pudo iniciar el vuelo ACARS: " + result.Error;
            }
        }

        private async Task LoadMetarAsync()
        {
            if (string.IsNullOrWhiteSpace(DepartureIcao) && string.IsNullOrWhiteSpace(ArrivalIcao))
            {
                return;
            }

            IsLoadingMetar = true;
            ResetWeatherContext();

            try
            {
                if (!string.IsNullOrWhiteSpace(DepartureIcao))
                {
                    var response = await AcarsContext.Api.GetMetarAsync(DepartureIcao);
                    if (response.Success && response.Data != null)
                    {
                        DepWeather = response.Data;
                        DepMetar = response.Data.RawMetar;
                    }
                }

                if (!string.IsNullOrWhiteSpace(ArrivalIcao))
                {
                    var response = await AcarsContext.Api.GetMetarAsync(ArrivalIcao);
                    if (response.Success && response.Data != null)
                    {
                        ArrWeather = response.Data;
                        ArrMetar = response.Data.RawMetar;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PreFlight] LoadMetarAsync => " + ex);
            }
            finally
            {
                IsLoadingMetar = false;
            }
        }

        private void ResetWeatherContext()
        {
            DepWeather = null;
            ArrWeather = null;
            DepMetar = string.Empty;
            ArrMetar = string.Empty;
        }

        private void ClearLoadedFlight(bool clearRuntime)
        {
            ReadyFlight = null;
            PreparedDispatch = null;
            FlightStarted = false;
            _startGateResult = null;
            FlightNumber = string.Empty;
            DepartureIcao = string.Empty;
            ArrivalIcao = string.Empty;
            AircraftIcao = string.Empty;
            Route = string.Empty;
            Remarks = string.Empty;
            ResetWeatherContext();
            OnPropertyChanged(nameof(StartGateAllowsStart));
            OnPropertyChanged(nameof(StartGateSummary));
            OnPropertyChanged(nameof(StartGateParkingBrakeOk));
            OnPropertyChanged(nameof(StartGateColdAndDarkOk));
            OnPropertyChanged(nameof(StartGateAircraftTypeOk));
            OnPropertyChanged(nameof(StartGateAirportOk));
            OnPropertyChanged(nameof(CanStartFlight));

            if (clearRuntime)
            {
                AcarsContext.Runtime.ClearDispatch();
            }
        }

        private void OnRuntimeChanged()
        {
            SyncFromRuntime();
        }

        private void SyncFromRuntime()
        {
            var runtimeFlight = AcarsContext.Runtime.CurrentReadyFlight;
            if (runtimeFlight != null)
            {
                ReadyFlight = runtimeFlight;
                if (PreparedDispatch == null || runtimeFlight.ReservationId != PreparedDispatch.ReservationId)
                {
                    ApplyDispatchSnapshot(runtimeFlight.ToPreparedDispatch());
                }
            }
            ApplyPilotPreferences();
            RefreshStartGateStatus(false);
        }

        private void RefreshStartGateStatus(bool updateStatusMessage)
        {
            if (PreparedDispatch == null || !PreparedDispatch.IsDispatchReady)
            {
                _startGateResult = new PatagoniaStartFlightGateResult
                {
                    CanStart = false,
                    Summary = "El dispatch aún no está listo para evaluar el gate de inicio."
                };
            }
            else if (AcarsContext.Runtime.LastTelemetry == null || !AcarsContext.Runtime.IsTelemetryFresh())
            {
                _startGateResult = new PatagoniaStartFlightGateResult
                {
                    CanStart = false,
                    Summary = "Esperando telemetría viva para validar cold and dark y parking brake."
                };
            }
            else
            {
                var pilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
                var gateInput = new PatagoniaEvaluationInput
                {
                    Flight = new Flight
                    {
                        ReservationId = PreparedDispatch.ReservationId,
                        DispatchPackageId = PreparedDispatch.DispatchId,
                        AircraftId = PreparedDispatch.AircraftId,
                        FlightNumber = Safe(FlightNumber).ToUpperInvariant(),
                        DepartureIcao = Safe(DepartureIcao).ToUpperInvariant(),
                        ArrivalIcao = Safe(ArrivalIcao).ToUpperInvariant(),
                        AircraftIcao = Safe(AircraftIcao).ToUpperInvariant(),
                        AircraftTypeCode = PreparedDispatch.AircraftIcao,
                        AircraftName = PreparedDispatch.AircraftDisplayName,
                        AircraftDisplayName = PreparedDispatch.AircraftDisplayName,
                        AircraftVariantCode = PreparedDispatch.AircraftVariantCode,
                        AddonProvider = PreparedDispatch.AddonProvider,
                        Route = Safe(Route),
                        FlightModeCode = PreparedDispatch.FlightMode,
                        RouteCode = PreparedDispatch.RouteCode,
                        PlannedAltitude = PlannedAlt,
                        PlannedSpeed = PlannedSpeed,
                        Simulator = SelectedSim,
                        Remarks = Safe(Remarks),
                        StartTime = DateTime.UtcNow
                    },
                    Dispatch = PreparedDispatch,
                    Report = new FlightReport
                    {
                        ReservationId = PreparedDispatch.ReservationId,
                        FlightNumber = Safe(FlightNumber).ToUpperInvariant(),
                        DepartureIcao = Safe(DepartureIcao).ToUpperInvariant(),
                        ArrivalIcao = Safe(ArrivalIcao).ToUpperInvariant(),
                        AircraftIcao = Safe(AircraftIcao).ToUpperInvariant(),
                        DepartureTime = DateTime.UtcNow,
                        ArrivalTime = DateTime.UtcNow
                    },
                    CurrentTelemetry = AcarsContext.Runtime.LastTelemetry,
                    TelemetryLog = new[] { AcarsContext.Runtime.LastTelemetry },
                    PilotQualifications = pilot == null ? string.Empty : pilot.ActiveQualifications,
                    PilotCertifications = pilot == null ? string.Empty : pilot.ActiveCertifications
                };

                _startGateResult = _startFlightGateService.Evaluate(gateInput);
                Debug.WriteLine("[PreFlight] Start gate => canStart=" + _startGateResult.CanStart + " summary=" + _startGateResult.Summary);
            }

            OnPropertyChanged(nameof(StartGateAllowsStart));
            OnPropertyChanged(nameof(StartGateSummary));
            OnPropertyChanged(nameof(StartGateParkingBrakeOk));
            OnPropertyChanged(nameof(StartGateColdAndDarkOk));
            OnPropertyChanged(nameof(StartGateAircraftTypeOk));
            OnPropertyChanged(nameof(StartGateAirportOk));
            OnPropertyChanged(nameof(StartGateParkingBrakeState));
            OnPropertyChanged(nameof(StartGateColdAndDarkState));
            OnPropertyChanged(nameof(StartGateAircraftTypeState));
            OnPropertyChanged(nameof(StartGateAirportState));
            OnPropertyChanged(nameof(StartGateParkingBrakeLabel));
            OnPropertyChanged(nameof(StartGateColdAndDarkLabel));
            OnPropertyChanged(nameof(StartGateAircraftTypeLabel));
            OnPropertyChanged(nameof(StartGateAirportLabel));
            OnPropertyChanged(nameof(CanStartFlight));
            OnPropertyChanged(nameof(StartButtonTitle));
            OnPropertyChanged(nameof(StartButtonSubtitle));

            if (updateStatusMessage && !FlightStarted)
            {
                StatusMessage = StartGateSummary;
            }
        }

        private bool IsStartGateColdAndDarkOk()
        {
            if (IsGateRulePassing("START_COLD_AND_DARK"))
            {
                return true;
            }

            var sample = AcarsContext.Runtime.LastTelemetry;
            if (sample == null || !AcarsContext.Runtime.IsTelemetryFresh())
            {
                return false;
            }

            return IsOperationalColdAndDarkForC208(sample);
        }

        private bool CanStartWithColdAndDarkOverride()
        {
            if (_startGateResult == null || _startGateResult.CanStart)
            {
                return _startGateResult != null && _startGateResult.CanStart;
            }

            if (!IsOperationalColdAndDarkForC208(AcarsContext.Runtime.LastTelemetry))
            {
                return false;
            }

            return IsGateRulePassing("START_PARKING_BRAKE_ON")
                && IsStartGateAircraftMatchOk()
                && IsGateRulePassing("START_AIRPORT_MATCH");
        }

        private static bool IsOperationalColdAndDarkForC208(SimData? sample)
        {
            if (sample == null) return false;
            if (!IsC208BlackSquareOrCaravanSample(sample)) return false;

            // Black Square C208 / Caravan puede entregar Engine N1 con valores no aeronáuticos
            // (ej.: 28672) aunque la turbina esté detenida. Por eso, para el gate previo
            // usamos primero combustión/running; N1 solo se usa si viene en rango razonable.
            var enginesStopped = !sample.EngineOneRunning
                && !sample.EngineTwoRunning
                && !sample.EngineThreeRunning
                && !sample.EngineFourRunning;

            if (IsReasonablePercent(sample.Engine1N1)) enginesStopped = enginesStopped && sample.Engine1N1 < 5;
            if (IsReasonablePercent(sample.Engine2N1)) enginesStopped = enginesStopped && sample.Engine2N1 < 5;
            if (IsReasonablePercent(sample.Engine3N1)) enginesStopped = enginesStopped && sample.Engine3N1 < 5;
            if (IsReasonablePercent(sample.Engine4N1)) enginesStopped = enginesStopped && sample.Engine4N1 < 5;

            var lightsOff = !sample.NavLightsOn
                && !sample.BeaconLightsOn
                && !sample.StrobeLightsOn
                && !sample.LandingLightsOn
                && !sample.TaxiLightsOn;

            Debug.WriteLine("[PreFlight] C208 Black Square C&D override => "
                + "enginesStopped=" + enginesStopped
                + " lightsOff=" + lightsOff
                + " title=" + (sample.AircraftTitle ?? string.Empty)
                + " profile=" + (sample.ProfileCode ?? sample.DetectedProfileCode ?? string.Empty)
                + " batt=" + sample.BatteryMasterOn
                + " avionics=" + sample.AvionicsMasterOn
                + " busV=" + sample.ElectricalMainBusVoltage.ToString("F1")
                + " n1=" + sample.Engine1N1.ToString("F1") + "/" + sample.Engine2N1.ToString("F1"));

            // En Black Square C208 no se usan BatteryMaster/Avionics/MainBus para C&D por HOT BATTERY BUS.
            return enginesStopped && lightsOff;
        }

        private static bool IsC208BlackSquareOrCaravanSample(SimData sample)
        {
            var text = (Safe(sample.ProfileCode) + " "
                + Safe(sample.DetectedProfileCode) + " "
                + Safe(sample.AircraftTypeCode) + " "
                + Safe(sample.AircraftVariantCode) + " "
                + Safe(sample.AircraftTitle) + " "
                + Safe(sample.MatchedTitle) + " "
                + Safe(sample.MatchedPattern)).ToUpperInvariant();

            var isC208 = text.Contains("C208")
                || text.Contains("C208B")
                || text.Contains("208")
                || text.Contains("CARAVAN")
                || text.Contains("GRAND CARAVAN");

            var isBlackSquare = text.Contains("BLACK SQUARE")
                || text.Contains("BLACKSQUARE")
                || text.Contains("GRAND CARAVAN EX ANALOG")
                || text.Contains("CARAVAN PROFESSIONAL");

            return isC208 && isBlackSquare;
        }

        private static bool IsReasonablePercent(double value)
        {
            return value >= 0 && value <= 120;
        }
        private bool IsStartGateAircraftMatchOk()
        {
            if (PreparedDispatch == null || !PreparedDispatch.IsDispatchReady)
            {
                return false;
            }

            var sample = AcarsContext.Runtime.LastTelemetry;
            if (sample == null || !AcarsContext.Runtime.IsTelemetryFresh())
            {
                return false;
            }

            // Validación explícita ACARS: el LED AVION no debe depender solo del rule engine.
            // Debe confirmar que el avión real cargado en el simulador coincide con el tipo despachado en Web.
            var expectedTokens = BuildExpectedAircraftTokens(PreparedDispatch);
            if (expectedTokens.Count == 0)
            {
                return false;
            }

            var actualTokens = BuildActualAircraftTokens(sample);
            if (actualTokens.Count == 0)
            {
                return false;
            }

            foreach (var expected in expectedTokens)
            {
                foreach (var actual in actualTokens)
                {
                    if (AircraftTokenMatches(expected, actual))
                    {
                        return true;
                    }
                }
            }

            Debug.WriteLine("[PreFlight] Aircraft gate mismatch expected="
                + string.Join("|", expectedTokens.ToArray())
                + " actual="
                + string.Join("|", actualTokens.ToArray()));

            return false;
        }

        private static HashSet<string> BuildExpectedAircraftTokens(PreparedDispatch dispatch)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddAircraftToken(tokens, dispatch.AircraftIcao);
            AddAircraftToken(tokens, dispatch.AircraftVariantCode);
            AddAircraftToken(tokens, dispatch.AircraftDisplayName);

            var display = Safe(dispatch.AircraftDisplayName).ToUpperInvariant();
            var icao = Safe(dispatch.AircraftIcao).ToUpperInvariant();
            var variant = Safe(dispatch.AircraftVariantCode).ToUpperInvariant();

            if (icao.Contains("C208") || variant.Contains("C208") || display.Contains("CARAVAN") || display.Contains("208"))
            {
                tokens.Add("C208");
                tokens.Add("C208B");
                tokens.Add("CARAVAN");
                tokens.Add("GRANDCARAVAN");
                tokens.Add("GRANDCARAVANEX");
            }

            if (icao.Contains("BE58") || variant.Contains("BE58") || display.Contains("BARON") || display.Contains("58"))
            {
                tokens.Add("BE58");
                tokens.Add("BARON58");
                tokens.Add("BARON");
            }

            return tokens;
        }

        private static HashSet<string> BuildActualAircraftTokens(SimData sample)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddAircraftToken(tokens, sample.AircraftTypeCode);
            AddAircraftToken(tokens, sample.AircraftVariantCode);
            AddAircraftToken(tokens, sample.ProfileCode);
            AddAircraftToken(tokens, sample.DetectedProfileCode);
            AddAircraftToken(tokens, sample.AircraftTitle);
            AddAircraftToken(tokens, sample.MatchedTitle);
            AddAircraftToken(tokens, sample.MatchedPattern);

            var title = Safe(sample.AircraftTitle + " " + sample.MatchedTitle + " " + sample.ProfileCode + " " + sample.DetectedProfileCode).ToUpperInvariant();

            if (title.Contains("C208") || title.Contains("208") || title.Contains("CARAVAN"))
            {
                tokens.Add("C208");
                tokens.Add("C208B");
                tokens.Add("CARAVAN");
                tokens.Add("GRANDCARAVAN");
                tokens.Add("GRANDCARAVANEX");
            }

            if (title.Contains("BE58") || title.Contains("BARON") || title.Contains("58TC"))
            {
                tokens.Add("BE58");
                tokens.Add("BARON58");
                tokens.Add("BARON");
            }

            return tokens;
        }

        private static void AddAircraftToken(HashSet<string> tokens, string? value)
        {
            var token = NormalizeAircraftToken(value);
            if (!string.IsNullOrWhiteSpace(token))
            {
                tokens.Add(token);
            }
        }

        private static string NormalizeAircraftToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var raw = value.Trim().ToUpperInvariant();
            var chars = raw.Where(char.IsLetterOrDigit).ToArray();
            var compact = new string(chars);

            if (compact.Contains("C208B") || compact.Contains("C208")) return compact.Contains("C208B") ? "C208B" : "C208";
            if (compact.Contains("GRANDCARAVANEX")) return "GRANDCARAVANEX";
            if (compact.Contains("GRANDCARAVAN")) return "GRANDCARAVAN";
            if (compact.Contains("CARAVAN")) return "CARAVAN";
            if (compact.Contains("BE58")) return "BE58";
            if (compact.Contains("BARON58")) return "BARON58";
            if (compact.Contains("BARON")) return "BARON";

            return compact;
        }

        private static bool AircraftTokenMatches(string expected, string actual)
        {
            expected = NormalizeAircraftToken(expected);
            actual = NormalizeAircraftToken(actual);
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual)) return false;
            if (string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase)) return true;

            if (IsC208Token(expected) && IsC208Token(actual)) return true;
            if (IsBe58Token(expected) && IsBe58Token(actual)) return true;

            return false;
        }

        private static bool IsC208Token(string token)
        {
            token = NormalizeAircraftToken(token);
            return token == "C208" || token == "C208B" || token == "CARAVAN" || token == "GRANDCARAVAN" || token == "GRANDCARAVANEX";
        }

        private static bool IsBe58Token(string token)
        {
            token = NormalizeAircraftToken(token);
            return token == "BE58" || token == "BARON" || token == "BARON58";
        }


        private bool IsGateRulePassing(string ruleId)
        {
            if (PreparedDispatch == null || !PreparedDispatch.IsDispatchReady || _startGateResult == null || _startGateResult.Evaluation == null)
            {
                return false;
            }

            var audit = _startGateResult.Evaluation.RuleAuditLog == null
                ? null
                : _startGateResult.Evaluation.RuleAuditLog.LastOrDefault(item => string.Equals(item.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));

            if (audit != null)
            {
                return string.Equals(audit.Result, PatagoniaAuditResults.Pass, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(audit.Result, "ok", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(audit.Result, "success", StringComparison.OrdinalIgnoreCase);
            }

            return _startGateResult.Evaluation.GateFailures == null
                || !_startGateResult.Evaluation.GateFailures.Any(item => string.Equals(item.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
        }

        private PatagoniaRuleAuditEntry? GetGateRuleAudit(string ruleId)
        {
            if (PreparedDispatch == null || !PreparedDispatch.IsDispatchReady || _startGateResult == null || _startGateResult.Evaluation == null)
            {
                return null;
            }

            return _startGateResult.Evaluation.RuleAuditLog == null
                ? null
                : _startGateResult.Evaluation.RuleAuditLog.LastOrDefault(item => string.Equals(item.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
        }

        private string GetGateRuleVisualState(string ruleId)
        {
            if (PreparedDispatch == null || !PreparedDispatch.IsDispatchReady)
            {
                return "PENDING";
            }

            if (AcarsContext.Runtime.LastTelemetry == null || !AcarsContext.Runtime.IsTelemetryFresh())
            {
                return "PENDING";
            }

            if (string.Equals(ruleId, "START_COLD_AND_DARK", StringComparison.OrdinalIgnoreCase) && IsStartGateColdAndDarkOk())
            {
                return "PASS";
            }

            if (string.Equals(ruleId, "START_AIRCRAFT_MATCH", StringComparison.OrdinalIgnoreCase))
            {
                return IsStartGateAircraftMatchOk() ? "PASS" : "FAIL";
            }

            var audit = GetGateRuleAudit(ruleId);
            if (audit == null)
            {
                return IsGateRulePassing(ruleId) ? "PASS" : "PENDING";
            }

            if (string.Equals(audit.Result, PatagoniaAuditResults.Pass, StringComparison.OrdinalIgnoreCase))
            {
                return "PASS";
            }

            if (string.Equals(audit.Result, PatagoniaAuditResults.Warn, StringComparison.OrdinalIgnoreCase))
            {
                return "WARN";
            }

            if (string.Equals(audit.Result, PatagoniaAuditResults.NotApplicable, StringComparison.OrdinalIgnoreCase))
            {
                return "N_A";
            }

            return "FAIL";
        }

        private string GetGateRuleVisualLabel(string ruleId)
        {
            var state = GetGateRuleVisualState(ruleId);
            switch (state)
            {
                case "PASS":
                    return "OK";
                case "WARN":
                    return "PARCIAL";
                case "N_A":
                    return "N/D";
                case "PENDING":
                    return "PEND";
                default:
                    return "NO";
            }
        }

        private void ApplyPilotPreferences()
        {
            var pilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
            if (pilot == null)
            {
                SelectedSim = SimulatorType.MSFS2020;
                return;
            }

            var preferred = Safe(pilot.PreferredSimulator).ToUpperInvariant();
            SelectedSim = preferred.Contains("2024") ? SimulatorType.MSFS2024 : SimulatorType.MSFS2020;
        }

        private static string BuildWeatherSummary(WeatherInfo? weather)
        {
            if (weather == null)
            {
                return "Sin METAR operativo cargado.";
            }

            var fragments = new List<string>();
            if (!string.IsNullOrWhiteSpace(weather.FlightCategory)) fragments.Add(weather.FlightCategory.Trim().ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(weather.Visibility)) fragments.Add("Vis " + weather.Visibility.Trim());
            if (weather.QNH > 0) fragments.Add("QNH " + Math.Round(weather.QNH, 0).ToString("F0") + " hPa");
            if (!string.IsNullOrWhiteSpace(weather.Wind)) fragments.Add(weather.Wind.Trim());
            if (weather.HasThunderstorm) fragments.Add("Tormenta");
            else if (weather.IsSnowing) fragments.Add("Nieve");
            else if (weather.IsRaining) fragments.Add("Lluvia");

            return fragments.Count == 0 ? "METAR recibido, pero sin resumen operativo útil." : string.Join(" · ", fragments.ToArray());
        }

        private string BuildOperationalMinimaSummary()
        {
            var worstWeather = GetWorstWeather();
            if (worstWeather == null)
            {
                return "Carga METAR para evaluar categoría, QNH y mínimos operativos antes de liberar el vuelo.";
            }

            var advisories = new List<string>();
            if ((DepWeather != null && DepWeather.HasThunderstorm) || (ArrWeather != null && ArrWeather.HasThunderstorm)) advisories.Add("actividad convectiva");
            if ((DepWeather != null && DepWeather.IsSnowing) || (ArrWeather != null && ArrWeather.IsSnowing)) advisories.Add("nieve");
            else if ((DepWeather != null && DepWeather.IsRaining) || (ArrWeather != null && ArrWeather.IsRaining)) advisories.Add("precipitación");

            var baseSummary = GetFlightCategorySeverity(worstWeather.FlightCategory) >= 2
                ? "Condiciones IFR/LIFR. Revisa alterno, briefing y mínimos con más margen."
                : (GetFlightCategorySeverity(worstWeather.FlightCategory) == 1
                    ? "Condiciones MVFR. Ajusta combustible y brief de llegada."
                    : "Condiciones VFR estables para la operación prevista.");

            return advisories.Count == 0 ? baseSummary : baseSummary + " Riesgos: " + string.Join(", ", advisories.ToArray()) + ".";
        }

        private string BuildOperationalQualificationsSummary()
        {
            var pilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
            if (pilot == null)
            {
                return "Sin sesión de piloto para evaluar habilitaciones operativas.";
            }

            var qualifications = FormatOperationalList(pilot.ActiveQualifications);
            var certifications = FormatOperationalList(pilot.ActiveCertifications);
            return "Qual: " + qualifications + " · Cert: " + certifications;
        }

        private WeatherInfo? GetWorstWeather()
        {
            var departureSeverity = GetFlightCategorySeverity(DepWeather?.FlightCategory);
            var arrivalSeverity = GetFlightCategorySeverity(ArrWeather?.FlightCategory);
            if (ArrWeather != null && arrivalSeverity >= departureSeverity) return ArrWeather;
            return DepWeather ?? ArrWeather;
        }

        private static int GetFlightCategorySeverity(string? category)
        {
            var normalized = Safe(category).ToUpperInvariant();
            if (normalized == "LIFR") return 3;
            if (normalized == "IFR") return 2;
            if (normalized == "MVFR") return 1;
            return 0;
        }

        private static string Safe(string? value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        private static string FirstNonEmpty(params string?[]? values)
        {
            if (values == null) return string.Empty;
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value!.Trim();
                }
            }
            return string.Empty;
        }

        private static string NormalizeToken(string value)
        {
            return Safe(value).ToUpperInvariant();
        }

        private static string NormalizeReadable(string value)
        {
            var normalized = Safe(value).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "sin estado";
            }
            return normalized.Replace("_", " ");
        }

        private static string ResolveAircraftRealName(PreparedDispatch dispatch)
        {
            var display = Safe(dispatch.AircraftDisplayName);
            if (!string.IsNullOrWhiteSpace(display)
                && display.IndexOf("_BASE", StringComparison.OrdinalIgnoreCase) < 0
                && display.IndexOf("_MULTI", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return display;
            }

            var variant = Safe(dispatch.AircraftVariantCode).ToUpperInvariant();
            if (variant.StartsWith("C208", StringComparison.Ordinal))
            {
                return "Grand Caravan";
            }

            return Safe(dispatch.AircraftIcao).ToUpperInvariant();
        }

        private static string ResolveAircraftAirframe(PreparedDispatch dispatch)
        {
            var variant = Safe(dispatch.AircraftVariantCode).ToUpperInvariant();
            if (variant.StartsWith("C208", StringComparison.Ordinal))
            {
                return "C208B";
            }

            var icao = Safe(dispatch.AircraftIcao).ToUpperInvariant();
            return string.IsNullOrWhiteSpace(icao) ? string.Empty : icao;
        }

        private static string FormatKg(double value)
        {
            return Math.Round(value, 0).ToString("F0") + " kg";
        }

        private static string FormatMinutes(int minutes)
        {
            if (minutes <= 0)
            {
                return "—";
            }

            var hours = minutes / 60;
            var rem = minutes % 60;
            if (hours <= 0)
            {
                return rem + " min";
            }

            return hours + "h " + rem.ToString("00") + "m";
        }

        private static string FormatOperationalList(string source)
        {
            return string.IsNullOrWhiteSpace(source) ? "sin registro" : source.Trim();
        }
    }
}
