using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;
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
                    OnPropertyChanged(nameof(AircraftDisplayLine));
                    OnPropertyChanged(nameof(RouteDisplayLine));
                    OnPropertyChanged(nameof(FuelDisplayLine));
                    OnPropertyChanged(nameof(PayloadDisplayLine));
                    OnPropertyChanged(nameof(BlockDisplayLine));
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
        public bool CanStartFlight { get { return PreparedDispatch != null && PreparedDispatch.IsDispatchReady && !IsLoadingDispatch && !FlightStarted; } }

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
                {
                    return "Modo operativo no informado por la web.";
                }

                return "Modo " + PreparedDispatch.FlightMode.Trim();
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
                var display = Safe(PreparedDispatch.AircraftDisplayName);
                var reg = Safe(PreparedDispatch.AircraftRegistration);
                var icao = Safe(PreparedDispatch.AircraftIcao);
                var variant = Safe(PreparedDispatch.AircraftVariantCode);
                var addon = Safe(PreparedDispatch.AddonProvider);

                if (!string.IsNullOrWhiteSpace(display)) parts.Add(display);
                if (!string.IsNullOrWhiteSpace(reg)) parts.Add(reg);
                if (!string.IsNullOrWhiteSpace(icao)) parts.Add(icao);
                if (!string.IsNullOrWhiteSpace(variant)) parts.Add(variant);
                if (!string.IsNullOrWhiteSpace(addon)) parts.Add(addon);

                return parts.Count == 0 ? "Sin aeronave asignada" : string.Join(" · ", parts.Distinct().ToArray());
            }
        }

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
                if (PreparedDispatch == null)
                {
                    return "—";
                }

                var parts = new List<string>();
                if (PreparedDispatch.PayloadKg > 0)
                {
                    parts.Add("Payload " + FormatKg(PreparedDispatch.PayloadKg));
                }
                if (PreparedDispatch.ZeroFuelWeightKg > 0)
                {
                    parts.Add("ZFW " + FormatKg(PreparedDispatch.ZeroFuelWeightKg));
                }
                if (PreparedDispatch.PassengerCount > 0)
                {
                    parts.Add(PreparedDispatch.PassengerCount + " pax");
                }

                return parts.Count == 0 ? "No informado" : string.Join(" · ", parts.ToArray());
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

                var parts = new List<string>();
                if (PreparedDispatch.ScheduledBlockMinutes > 0)
                {
                    parts.Add("STD " + FormatMinutes(PreparedDispatch.ScheduledBlockMinutes));
                }
                if (PreparedDispatch.ExpectedBlockP50Minutes > 0)
                {
                    parts.Add("P50 " + FormatMinutes(PreparedDispatch.ExpectedBlockP50Minutes));
                }
                if (PreparedDispatch.ExpectedBlockP80Minutes > 0)
                {
                    parts.Add("P80 " + FormatMinutes(PreparedDispatch.ExpectedBlockP80Minutes));
                }
                if (PreparedDispatch.ScheduledDepartureUtc.HasValue)
                {
                    parts.Add("ETD " + PreparedDispatch.ScheduledDepartureUtc.Value.ToString("HH:mm") + "Z");
                }

                return parts.Count == 0 ? "Sin block publicado" : string.Join(" · ", parts.ToArray());
            }
        }

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
                    return "INICIAR VUELO ACARS";
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
                        ReadyFlight = AcarsContext.Runtime.CurrentReadyFlight;
                        ApplyDispatchSnapshot(AcarsContext.Runtime.CurrentReadyFlight.ToPreparedDispatch());
                        StatusMessage = "Usando el último despacho almacenado localmente.";
                        _ = LoadMetarAsync();
                        return;
                    }

                    ClearLoadedFlight(false);
                    if (string.IsNullOrWhiteSpace(StatusMessage))
                    {
                        StatusMessage = "No hay ninguna reserva activa. Prepara el vuelo primero desde Patagonia Wings Web.";
                    }
                    return;
                }

                ApplyDispatchSnapshot(dispatch);
                ReadyFlight = BuildReadyFlight(dispatch, pilot.CallSign);
                if (ReadyFlight != null)
                {
                    AcarsContext.Runtime.SetReadyFlight(ReadyFlight);
                }

                FlightStarted = false;
                StatusMessage = dispatch.IsDispatchReady
                    ? "Despacho listo desde la web. Revisa METAR y simulador antes de iniciar el ACARS."
                    : "Reserva cargada desde la web. El dispatch todavía no está liberado para ACARS.";

                _ = LoadMetarAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[PreFlight] LoadPreparedDispatchAsync => " + ex);
                if (AcarsContext.Runtime.CurrentReadyFlight != null)
                {
                    ReadyFlight = AcarsContext.Runtime.CurrentReadyFlight;
                    ApplyDispatchSnapshot(AcarsContext.Runtime.CurrentReadyFlight.ToPreparedDispatch());
                    StatusMessage = "Se mantuvo el último despacho local. Error remoto: " + ex.Message;
                }
                else
                {
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
            OnPropertyChanged(nameof(DepartureWeatherSummary));
            OnPropertyChanged(nameof(ArrivalWeatherSummary));
            OnPropertyChanged(nameof(OperationalMinimaSummary));
            OnPropertyChanged(nameof(OperationalQualificationsSummary));
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
                StatusMessage = "La reserva todavía no está en estado despachable. Complétala primero en la web.";
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
            FlightNumber = string.Empty;
            DepartureIcao = string.Empty;
            ArrivalIcao = string.Empty;
            AircraftIcao = string.Empty;
            Route = string.Empty;
            Remarks = string.Empty;
            ResetWeatherContext();

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
