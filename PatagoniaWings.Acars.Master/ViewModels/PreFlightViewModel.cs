using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public class PreFlightViewModel : ViewModelBase
    {
        private AcarsReadyFlight? _readyFlight;
        private PreparedDispatch? _preparedDispatch;
        private string _flightNumber = string.Empty;
        private string _departureIcao = string.Empty;
        private string _arrivalIcao = string.Empty;
        private string _aircraftIcao = string.Empty;
        private string _route = string.Empty;
        private int _plannedAlt = 35000;
        private int _plannedSpeed = 450;
        private string _remarks = string.Empty;
        private SimulatorType _selectedSim = SimulatorType.MSFS2020;
        private string _selectedSimulatorOption = "MSFS 2020";
        private Airport? _depAirport;
        private Airport? _arrAirport;
        private WeatherInfo? _depWeather;
        private WeatherInfo? _arrWeather;
        private string _depMetar = string.Empty;
        private string _arrMetar = string.Empty;
        private bool _isLoadingMetar;
        private bool _isLoadingDispatch;
        private string _statusMessage = string.Empty;
        private bool _flightStarted;
        private bool _isFlightDataLocked;

        public AcarsReadyFlight? ReadyFlight
        {
            get => _readyFlight;
            set
            {
                if (SetField(ref _readyFlight, value))
                {
                    IsFlightDataLocked = value != null;
                    OnPropertyChanged(nameof(CanStartFlight));
                }
            }
        }

        public PreparedDispatch? PreparedDispatch
        {
            get => _preparedDispatch;
            set
            {
                if (SetField(ref _preparedDispatch, value))
                {
                    OnPropertyChanged(nameof(ReadyFlightVariantSummary));
                    OnPropertyChanged(nameof(ReadyFlightModeSummary));
                    OnPropertyChanged(nameof(OperationalQualificationsSummary));
                    OnPropertyChanged(nameof(CanStartFlight));
                    OnPropertyChanged(nameof(DepartureIcaoDisplay));
                    OnPropertyChanged(nameof(ArrivalIcaoDisplay));
                    OnPropertyChanged(nameof(ShowDispatchLoadingHint));
                }
            }
        }

        public string FlightNumber { get => _flightNumber; set => SetField(ref _flightNumber, value); }
        public string DepartureIcao { get => _departureIcao; set => SetField(ref _departureIcao, value); }
        public string ArrivalIcao { get => _arrivalIcao; set => SetField(ref _arrivalIcao, value); }
        public string AircraftIcao { get => _aircraftIcao; set => SetField(ref _aircraftIcao, value); }
        public string Route { get => _route; set => SetField(ref _route, value); }
        public int PlannedAlt { get => _plannedAlt; set => SetField(ref _plannedAlt, value); }
        public int PlannedSpeed { get => _plannedSpeed; set => SetField(ref _plannedSpeed, value); }
        public string Remarks { get => _remarks; set => SetField(ref _remarks, value); }
        public SimulatorType SelectedSim
        {
            get => _selectedSim;
            set
            {
                if (SetField(ref _selectedSim, value))
                {
                    var label = ToSimulatorOption(value);
                    if (_selectedSimulatorOption != label)
                    {
                        _selectedSimulatorOption = label;
                        OnPropertyChanged(nameof(SelectedSimulatorOption));
                    }
                }
            }
        }

        public string SelectedSimulatorOption
        {
            get => _selectedSimulatorOption;
            set
            {
                if (SetField(ref _selectedSimulatorOption, value))
                {
                    SelectedSim = ParseSimulatorOption(value);
                }
            }
        }
        public bool IsFlightDataLocked { get => _isFlightDataLocked; set => SetField(ref _isFlightDataLocked, value); }
        public bool CanStartFlight => PreparedDispatch?.IsDispatchReady == true && !IsLoadingDispatch && !FlightStarted;
        public string ReadyFlightVariantSummary
        {
            get
            {
                if (PreparedDispatch == null)
                {
                    return string.Empty;
                }

                var variant = PreparedDispatch.AircraftVariantCode?.Trim() ?? string.Empty;
                var addon = PreparedDispatch.AddonProvider?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(variant) && string.IsNullOrWhiteSpace(addon))
                {
                    return "Variante no informada por la web.";
                }

                if (string.IsNullOrWhiteSpace(variant))
                {
                    return addon;
                }

                if (string.IsNullOrWhiteSpace(addon))
                {
                    return variant;
                }

                return $"{variant} · {addon}";
            }
        }

        public string ReadyFlightModeSummary =>
            PreparedDispatch == null || string.IsNullOrWhiteSpace(PreparedDispatch.FlightMode)
                ? "Modo operativo no informado."
                : $"Modo {PreparedDispatch.FlightMode}";

        public ObservableCollection<string> SimulatorOptions { get; } = new()
        {
            "MSFS 2020", "MSFS 2024"
        };

        public Airport? DepAirport { get => _depAirport; set => SetField(ref _depAirport, value); }
        public Airport? ArrAirport { get => _arrAirport; set => SetField(ref _arrAirport, value); }
        public WeatherInfo? DepWeather
        {
            get => _depWeather;
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
            get => _arrWeather;
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

        public string DepMetar
        {
            get => _depMetar;
            set
            {
                if (SetField(ref _depMetar, value))
                {
                    OnPropertyChanged(nameof(DepartureMetarDisplay));
                    OnPropertyChanged(nameof(ShowMetarLoadingHint));
                }
            }
        }

        public string ArrMetar
        {
            get => _arrMetar;
            set
            {
                if (SetField(ref _arrMetar, value))
                {
                    OnPropertyChanged(nameof(ArrivalMetarDisplay));
                    OnPropertyChanged(nameof(ShowMetarLoadingHint));
                }
            }
        }

        public string DepartureIcaoDisplay => string.IsNullOrWhiteSpace(PreparedDispatch?.DepartureIcao) ? "----" : (PreparedDispatch?.DepartureIcao ?? "----");
        public string ArrivalIcaoDisplay => string.IsNullOrWhiteSpace(PreparedDispatch?.ArrivalIcao) ? "----" : (PreparedDispatch?.ArrivalIcao ?? "----");
        public string DepartureMetarDisplay => string.IsNullOrWhiteSpace(DepMetar) ? "Sin METAR cargado." : DepMetar;
        public string ArrivalMetarDisplay => string.IsNullOrWhiteSpace(ArrMetar) ? "Sin METAR cargado." : ArrMetar;
        public string DepartureWeatherSummary => BuildWeatherSummary(DepWeather);
        public string ArrivalWeatherSummary => BuildWeatherSummary(ArrWeather);
        public string OperationalMinimaSummary => BuildOperationalMinimaSummary();
        public string OperationalQualificationsSummary => BuildOperationalQualificationsSummary();
        public bool IsLoadingMetar
        {
            get => _isLoadingMetar;
            set
            {
                if (SetField(ref _isLoadingMetar, value))
                {
                    OnPropertyChanged(nameof(ShowMetarLoadingHint));
                }
            }
        }
        public bool ShowDispatchLoadingHint => IsLoadingDispatch && PreparedDispatch == null;
        public bool ShowMetarLoadingHint => IsLoadingMetar && string.IsNullOrWhiteSpace(DepMetar) && string.IsNullOrWhiteSpace(ArrMetar);
        public bool IsLoadingDispatch
        {
            get => _isLoadingDispatch;
            set
            {
                if (SetField(ref _isLoadingDispatch, value))
                {
                    OnPropertyChanged(nameof(CanStartFlight));
                    OnPropertyChanged(nameof(ShowDispatchLoadingHint));
                }
            }
        }

        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        public bool FlightStarted
        {
            get => _flightStarted;
            set
            {
                if (SetField(ref _flightStarted, value))
                {
                    OnPropertyChanged(nameof(CanStartFlight));
                }
            }
        }

        public ICommand FetchMetarCommand { get; }
        public ICommand LoadDispatchCommand { get; }
        public ICommand StartFlightCommand { get; }

        public PreFlightViewModel()
        {
            LoadDispatchCommand = new RelayCommand(async _ => await LoadPreparedDispatchAsync());

            FetchMetarCommand = new RelayCommand(async _ => await LoadMetarAsync());

            StartFlightCommand = new RelayCommand(async _ =>
            {
                if (PreparedDispatch == null || !PreparedDispatch.IsDispatchReady || ReadyFlight == null)
                {
                    StatusMessage = "No hay vuelo reservado/despachado listo para ACARS. Preparalo primero desde la web.";
                    return;
                }

                if (string.IsNullOrWhiteSpace(FlightNumber) ||
                    string.IsNullOrWhiteSpace(DepartureIcao) ||
                    string.IsNullOrWhiteSpace(ArrivalIcao) ||
                    string.IsNullOrWhiteSpace(AircraftIcao))
                {
                    StatusMessage = "La reserva activa llegÃ³ incompleta. Recarga el despacho desde la web.";
                    return;
                }

                var flight = new Flight
                {
                    ReservationId = PreparedDispatch.ReservationId,
                    DispatchPackageId = PreparedDispatch.DispatchId,
                    AircraftId = PreparedDispatch.AircraftId,
                    FlightNumber = FlightNumber.ToUpperInvariant(),
                    DepartureIcao = DepartureIcao.ToUpperInvariant(),
                    ArrivalIcao = ArrivalIcao.ToUpperInvariant(),
                    AircraftIcao = AircraftIcao.ToUpperInvariant(),
                    AircraftTypeCode = PreparedDispatch.AircraftIcao,
                    AircraftName = string.IsNullOrWhiteSpace(PreparedDispatch.AircraftDisplayName)
                        ? AircraftIcao.ToUpperInvariant()
                        : PreparedDispatch.AircraftDisplayName,
                    AircraftDisplayName = PreparedDispatch.AircraftDisplayName,
                    AircraftVariantCode = PreparedDispatch.AircraftVariantCode,
                    AddonProvider = PreparedDispatch.AddonProvider,
                    Route = Route,
                    FlightModeCode = PreparedDispatch.FlightMode,
                    RouteCode = PreparedDispatch.RouteCode,
                    PlannedAltitude = PlannedAlt,
                    PlannedSpeed = PlannedSpeed,
                    Remarks = Remarks,
                    Simulator = SelectedSim,
                    StartTime = DateTime.UtcNow
                };

                var result = await AcarsContext.Api.StartFlightAsync(flight, PreparedDispatch);
                if (result.Success)
                {
                    var initialFuelLbs = PreparedDispatch.FuelPlannedKg > 0
                        ? PreparedDispatch.FuelPlannedKg / 0.45359237d
                        : 0d;

                    AcarsContext.FlightService.StartFlight(flight, initialFuelLbs);
                    AcarsContext.Sound.PlayDing();
                    _ = AcarsContext.Sound.PlayGroundBienvenidoAsync();
                    FlightStarted = true;
                    StatusMessage = $"Vuelo {FlightNumber} iniciado desde la reserva activa. El ACARS queda bloqueado en vista operacional.";
                }
                else
                {
                    StatusMessage = $"Error al registrar vuelo: {result.Error}";
                }
            });
        }

        public async Task LoadPreparedDispatchAsync()
        {
            var pilot = AcarsContext.Auth.CurrentPilot;
            if (pilot == null || string.IsNullOrWhiteSpace(pilot.CallSign))
            {
                var pilotResult = await AcarsContext.Api.GetCurrentPilotAsync();
                if (pilotResult.Success && pilotResult.Data != null && !string.IsNullOrWhiteSpace(pilotResult.Data.CallSign))
                {
                    pilot = pilotResult.Data;
                    AcarsContext.Auth.SaveSession(pilot);
                }
            }

            if (pilot == null || string.IsNullOrWhiteSpace(pilot.CallSign))
            {
                StatusMessage = "Inicia sesion para cargar la reserva activa desde la web.";
                return;
            }

            IsLoadingDispatch = true;
            StatusMessage = "Buscando despacho activo del piloto...";

            try
            {
                var result = await AcarsContext.Api.GetReadyForAcarsFlightAsync(pilot.CallSign);
                if (!result.Success || result.Data == null)
                {
                    ClearLoadedFlight();
                    StatusMessage = string.IsNullOrWhiteSpace(result.Error)
                        ? "No hay un vuelo reservado/despachado listo para ACARS."
                        : result.Error;
                    return;
                }

                ReadyFlight = result.Data;
                PreparedDispatch = result.Data.ToPreparedDispatch();
                FlightStarted = false;
                OnPropertyChanged(nameof(DepartureIcaoDisplay));
                OnPropertyChanged(nameof(ArrivalIcaoDisplay));

                FlightNumber = result.Data.FlightNumber;
                DepartureIcao = result.Data.OriginIdent;
                ArrivalIcao = result.Data.DestinationIdent;
                AircraftIcao = result.Data.AircraftTypeCode;
                Route = result.Data.RouteText;
                PlannedAlt = result.Data.PlannedAltitude ?? PlannedAlt;
                PlannedSpeed = result.Data.PlannedSpeed ?? PlannedSpeed;
                Remarks = result.Data.Remarks;

                StatusMessage = $"Reserva activa cargada: {result.Data.FlightNumber} {result.Data.OriginIdent}-{result.Data.DestinationIdent}.";
                await LoadMetarAsync();
            }
            catch (Exception ex)
            {
                ClearLoadedFlight();
                StatusMessage = $"No se pudo cargar el vuelo listo para ACARS: {ex.Message}";
            }
            finally
            {
                IsLoadingDispatch = false;
            }
        }

        private void ClearLoadedFlight()
        {
            ReadyFlight = null;
            PreparedDispatch = null;
            FlightStarted = false;
            OnPropertyChanged(nameof(DepartureIcaoDisplay));
            OnPropertyChanged(nameof(ArrivalIcaoDisplay));
            ResetWeatherContext();
        }

        private async Task LoadMetarAsync()
        {
            if (string.IsNullOrWhiteSpace(DepartureIcao) && string.IsNullOrWhiteSpace(ArrivalIcao))
            {
                return;
            }

            IsLoadingMetar = true;
            ResetWeatherContext();
            OnPropertyChanged(nameof(ShowMetarLoadingHint));

            try
            {
                if (!string.IsNullOrWhiteSpace(DepartureIcao))
                {
                    var response = await AcarsContext.Api.GetMetarAsync(DepartureIcao);
                    if (response.Success && response.Data != null)
                    {
                        DepWeather = response.Data;
                        DepMetar = response.Data.RawMetar;
                        DepAirport = new Airport
                        {
                            Icao = DepartureIcao,
                            Metar = response.Data.RawMetar,
                            QNH = response.Data.QNH > 0
                                ? Math.Round(response.Data.QNH, 0).ToString("F0")
                                : string.Empty
                        };
                    }
                }

                if (!string.IsNullOrWhiteSpace(ArrivalIcao))
                {
                    var response = await AcarsContext.Api.GetMetarAsync(ArrivalIcao);
                    if (response.Success && response.Data != null)
                    {
                        ArrWeather = response.Data;
                        ArrMetar = response.Data.RawMetar;
                        ArrAirport = new Airport
                        {
                            Icao = ArrivalIcao,
                            Metar = response.Data.RawMetar,
                            QNH = response.Data.QNH > 0
                                ? Math.Round(response.Data.QNH, 0).ToString("F0")
                                : string.Empty
                        };
                    }
                }
            }
            finally
            {
                IsLoadingMetar = false;
                OnPropertyChanged(nameof(ShowMetarLoadingHint));
            }
        }

        private void ResetWeatherContext()
        {
            DepWeather = null;
            ArrWeather = null;
            DepMetar = string.Empty;
            ArrMetar = string.Empty;
            DepAirport = null;
            ArrAirport = null;
        }

        private static string BuildWeatherSummary(WeatherInfo? weather)
        {
            if (weather == null)
            {
                return "Sin METAR operativo cargado.";
            }

            var fragments = new List<string>();

            if (!string.IsNullOrWhiteSpace(weather.FlightCategory))
            {
                fragments.Add(weather.FlightCategory.Trim().ToUpperInvariant());
            }

            if (!string.IsNullOrWhiteSpace(weather.Visibility))
            {
                fragments.Add($"Vis {weather.Visibility.Trim()}");
            }

            if (weather.QNH > 0)
            {
                fragments.Add($"QNH {Math.Round(weather.QNH, 0):F0} hPa");
            }

            if (!string.IsNullOrWhiteSpace(weather.Wind))
            {
                fragments.Add(weather.Wind.Trim());
            }

            if (weather.HasThunderstorm)
            {
                fragments.Add("Tormenta");
            }
            else if (weather.IsSnowing)
            {
                fragments.Add("Nieve");
            }
            else if (weather.IsRaining)
            {
                fragments.Add("Lluvia");
            }

            return fragments.Count == 0
                ? "METAR recibido, pero sin resumen operativo util."
                : string.Join(" · ", fragments);
        }

        private string BuildOperationalMinimaSummary()
        {
            var worstWeather = GetWorstWeather();
            if (worstWeather == null)
            {
                return "Carga METAR para evaluar QNH, categoria y minimos operativos.";
            }

            var advisories = new List<string>();
            if (DepWeather?.HasThunderstorm == true || ArrWeather?.HasThunderstorm == true)
            {
                advisories.Add("actividad convectiva");
            }

            if (DepWeather?.IsSnowing == true || ArrWeather?.IsSnowing == true)
            {
                advisories.Add("nieve");
            }
            else if (DepWeather?.IsRaining == true || ArrWeather?.IsRaining == true)
            {
                advisories.Add("precipitacion");
            }

            if (DepWeather?.QNH > 0 && (DepWeather.QNH < 995 || DepWeather.QNH > 1035) ||
                ArrWeather?.QNH > 0 && (ArrWeather.QNH < 995 || ArrWeather.QNH > 1035))
            {
                advisories.Add("QNH fuera de rango comodo");
            }

            var baseSummary = GetFlightCategorySeverity(worstWeather.FlightCategory) switch
            {
                3 => "Condiciones LIFR/IFR fuertes. Minimos restringidos y despacho solo con criterio instrumental.",
                2 => "Condiciones IFR. Requiere brief fino de aproximacion, alterno y minimos de llegada.",
                1 => "Condiciones MVFR. Ajusta combustible, briefing y margen operacional.",
                _ => "Condiciones VFR estables para la operacion prevista."
            };

            return advisories.Count == 0
                ? baseSummary
                : $"{baseSummary} Riesgos: {string.Join(", ", advisories)}.";
        }

        private string BuildOperationalQualificationsSummary()
        {
            var pilot = AcarsContext.Auth.CurrentPilot;
            if (pilot == null)
            {
                return "Sin sesion de piloto para evaluar habilitaciones operativas.";
            }

            var qualificationText = FormatOperationalList(pilot.ActiveQualifications);
            var certificationText = FormatOperationalList(pilot.ActiveCertifications);
            var severeWeather =
                GetWorstWeather() != null &&
                (GetFlightCategorySeverity(GetWorstWeather()!.FlightCategory) >= 2 ||
                 GetWorstWeather()!.HasThunderstorm ||
                 GetWorstWeather()!.IsSnowing);
            var hasInstrumentQualification = HasOperationalToken(
                pilot.ActiveQualifications,
                "IFR",
                "INSTRUMENT",
                "RNAV",
                "ILS");

            var advisory = severeWeather
                ? hasInstrumentQualification
                    ? "Perfil instrumental detectado para condiciones degradadas."
                    : "Verifica habilitacion IFR/instrumental antes de liberar este tramo."
                : "Operacion estandar con habilitaciones vivas cargadas en la sesion.";

            return $"{advisory} Qual: {qualificationText}. Cert: {certificationText}.";
        }

        private WeatherInfo? GetWorstWeather()
        {
            var departureSeverity = GetFlightCategorySeverity(DepWeather?.FlightCategory);
            var arrivalSeverity = GetFlightCategorySeverity(ArrWeather?.FlightCategory);

            if (ArrWeather != null && arrivalSeverity >= departureSeverity)
            {
                return ArrWeather;
            }

            return DepWeather ?? ArrWeather;
        }

        private static int GetFlightCategorySeverity(string? category)
        {
            var normalized = category?.Trim().ToUpperInvariant() ?? string.Empty;
            return normalized switch
            {
                "LIFR" => 3,
                "IFR" => 2,
                "MVFR" => 1,
                _ => 0
            };
        }

        private static bool HasOperationalToken(string source, params string[] candidates)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in source.Split(new[] { ',', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim();
                if (token.Length == 0)
                {
                    continue;
                }

                values.Add(token);
                values.Add(token.Replace("-", "_").Replace(" ", "_"));
            }

            foreach (var candidate in candidates)
            {
                if (values.Contains(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatOperationalList(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return "sin registro";
            }

            return source.Trim();
        }

        private static SimulatorType ParseSimulatorOption(string value)
        {
            switch ((value ?? string.Empty).Trim())
            {
                case "MSFS 2024":
                    return SimulatorType.MSFS2024;
                default:
                    return SimulatorType.MSFS2020;
            }
        }

        private static string ToSimulatorOption(SimulatorType value)
        {
            switch (value)
            {
                case SimulatorType.MSFS2024:
                    return "MSFS 2024";
                default:
                    return "MSFS 2020";
            }
        }
    }
}
