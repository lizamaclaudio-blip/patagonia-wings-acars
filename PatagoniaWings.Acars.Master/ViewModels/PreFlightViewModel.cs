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

        public ObservableCollection<SimulatorType> SimulatorOptions { get; } = new ObservableCollection<SimulatorType>
        {
            SimulatorType.MSFS2020,
            SimulatorType.MSFS2024
        };

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
        public SimulatorType SelectedSim { get => _selectedSim; set => SetField(ref _selectedSim, value); }
        public bool IsFlightDataLocked { get => _isFlightDataLocked; set => SetField(ref _isFlightDataLocked, value); }
        public bool CanStartFlight => PreparedDispatch != null && PreparedDispatch.IsDispatchReady && !IsLoadingDispatch && !FlightStarted;

        public string ReadyFlightVariantSummary
        {
            get
            {
                if (PreparedDispatch == null)
                {
                    return string.Empty;
                }

                var variant = PreparedDispatch.AircraftVariantCode == null ? string.Empty : PreparedDispatch.AircraftVariantCode.Trim();
                var addon = PreparedDispatch.AddonProvider == null ? string.Empty : PreparedDispatch.AddonProvider.Trim();

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

                return variant + " · " + addon;
            }
        }

        public string ReadyFlightModeSummary =>
            PreparedDispatch == null || string.IsNullOrWhiteSpace(PreparedDispatch.FlightMode)
                ? "Modo operativo no informado."
                : "Modo " + PreparedDispatch.FlightMode;

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

        public string DepMetar { get => _depMetar; set => SetField(ref _depMetar, value); }
        public string ArrMetar { get => _arrMetar; set => SetField(ref _arrMetar, value); }
        public string DepartureWeatherSummary => BuildWeatherSummary(DepWeather);
        public string ArrivalWeatherSummary => BuildWeatherSummary(ArrWeather);
        public string OperationalMinimaSummary => BuildOperationalMinimaSummary();
        public string OperationalQualificationsSummary => BuildOperationalQualificationsSummary();
        public bool IsLoadingMetar { get => _isLoadingMetar; set => SetField(ref _isLoadingMetar, value); }
        public bool IsLoadingDispatch
        {
            get => _isLoadingDispatch;
            set
            {
                if (SetField(ref _isLoadingDispatch, value))
                {
                    OnPropertyChanged(nameof(CanStartFlight));
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
            StartFlightCommand = new RelayCommand(async _ => await StartFlightAsync());

            AcarsContext.Runtime.Changed += OnRuntimeChanged;
            ApplyPilotPreferences();
            SyncFromRuntime();
        }

        private async Task StartFlightAsync()
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
                StatusMessage = "La reserva activa llegó incompleta. Recarga el despacho desde la web.";
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
                StatusMessage = "Vuelo " + FlightNumber + " iniciado desde la reserva activa. El ACARS queda bloqueado en vista operacional.";
            }
            else
            {
                StatusMessage = "Error al registrar vuelo: " + result.Error;
            }
        }

        public async Task LoadPreparedDispatchAsync()
        {
            var pilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
            if (pilot == null || string.IsNullOrWhiteSpace(pilot.CallSign))
            {
                StatusMessage = "Inicia sesión para cargar la reserva activa desde la web.";
                return;
            }

            IsLoadingDispatch = true;

            try
            {
                var result = await AcarsContext.Api.GetReadyForAcarsFlightAsync(pilot.CallSign);
                if (!result.Success || result.Data == null)
                {
                    if (AcarsContext.Runtime.CurrentReadyFlight != null)
                    {
                        ApplyReadyFlight(AcarsContext.Runtime.CurrentReadyFlight);
                        StatusMessage = "Usando la última reserva activa almacenada en memoria.";
                        return;
                    }

                    ClearLoadedFlight(false);
                    StatusMessage = string.IsNullOrWhiteSpace(result.Error)
                        ? "No hay un vuelo reservado/despachado listo para ACARS."
                        : result.Error;
                    return;
                }

                ApplyReadyFlight(result.Data);
                AcarsContext.Runtime.SetReadyFlight(result.Data);
                FlightStarted = false;

                StatusMessage = "Reserva activa cargada: " + result.Data.FlightNumber + " " + result.Data.OriginIdent + "-" + result.Data.DestinationIdent + ".";
                await LoadMetarAsync();
            }
            catch (Exception ex)
            {
                if (AcarsContext.Runtime.CurrentReadyFlight != null)
                {
                    ApplyReadyFlight(AcarsContext.Runtime.CurrentReadyFlight);
                    StatusMessage = "Se mantuvo la reserva activa en memoria. Error remoto: " + ex.Message;
                }
                else
                {
                    ClearLoadedFlight(false);
                    StatusMessage = "No se pudo cargar el vuelo listo para ACARS: " + ex.Message;
                }
            }
            finally
            {
                IsLoadingDispatch = false;
            }
        }

        private void ApplyReadyFlight(AcarsReadyFlight readyFlight)
        {
            ReadyFlight = readyFlight;
            PreparedDispatch = readyFlight.ToPreparedDispatch();
            FlightNumber = readyFlight.FlightNumber;
            DepartureIcao = readyFlight.OriginIdent;
            ArrivalIcao = readyFlight.DestinationIdent;
            AircraftIcao = readyFlight.AircraftTypeCode;
            Route = readyFlight.RouteText;
            PlannedAlt = readyFlight.PlannedAltitude ?? PlannedAlt;
            PlannedSpeed = readyFlight.PlannedSpeed ?? PlannedSpeed;
            Remarks = readyFlight.Remarks;
            ApplyPilotPreferences();
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
            if (runtimeFlight != null && (ReadyFlight == null || runtimeFlight.ReservationId != ReadyFlight.ReservationId))
            {
                ApplyReadyFlight(runtimeFlight);
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

            var preferred = (pilot.PreferredSimulator ?? string.Empty).Trim().ToUpperInvariant();
            SelectedSim = preferred.Contains("2024") ? SimulatorType.MSFS2024 : SimulatorType.MSFS2020;
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
                        DepAirport = new Airport
                        {
                            Icao = DepartureIcao,
                            Metar = response.Data.RawMetar,
                            QNH = response.Data.QNH > 0 ? Math.Round(response.Data.QNH, 0).ToString("F0") : string.Empty
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
                            QNH = response.Data.QNH > 0 ? Math.Round(response.Data.QNH, 0).ToString("F0") : string.Empty
                        };
                    }
                }
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
            if (!string.IsNullOrWhiteSpace(weather.FlightCategory)) fragments.Add(weather.FlightCategory.Trim().ToUpperInvariant());
            if (!string.IsNullOrWhiteSpace(weather.Visibility)) fragments.Add("Vis " + weather.Visibility.Trim());
            if (weather.QNH > 0) fragments.Add("QNH " + Math.Round(weather.QNH, 0).ToString("F0") + " hPa");
            if (!string.IsNullOrWhiteSpace(weather.Wind)) fragments.Add(weather.Wind.Trim());
            if (weather.HasThunderstorm) fragments.Add("Tormenta");
            else if (weather.IsSnowing) fragments.Add("Nieve");
            else if (weather.IsRaining) fragments.Add("Lluvia");

            return fragments.Count == 0 ? "METAR recibido, pero sin resumen operativo útil." : string.Join(" · ", fragments);
        }

        private string BuildOperationalMinimaSummary()
        {
            var worstWeather = GetWorstWeather();
            if (worstWeather == null)
            {
                return "Carga METAR para evaluar QNH, categoría y mínimos operativos.";
            }

            var advisories = new List<string>();
            if ((DepWeather != null && DepWeather.HasThunderstorm) || (ArrWeather != null && ArrWeather.HasThunderstorm)) advisories.Add("actividad convectiva");
            if ((DepWeather != null && DepWeather.IsSnowing) || (ArrWeather != null && ArrWeather.IsSnowing)) advisories.Add("nieve");
            else if ((DepWeather != null && DepWeather.IsRaining) || (ArrWeather != null && ArrWeather.IsRaining)) advisories.Add("precipitación");

            if ((DepWeather != null && DepWeather.QNH > 0 && (DepWeather.QNH < 995 || DepWeather.QNH > 1035)) ||
                (ArrWeather != null && ArrWeather.QNH > 0 && (ArrWeather.QNH < 995 || ArrWeather.QNH > 1035)))
            {
                advisories.Add("QNH fuera de rango cómodo");
            }

            var baseSummary = GetFlightCategorySeverity(worstWeather.FlightCategory) switch
            {
                3 => "Condiciones LIFR/IFR fuertes. Mínimos restringidos y despacho solo con criterio instrumental.",
                2 => "Condiciones IFR. Requiere brief fino de aproximación, alterno y mínimos de llegada.",
                1 => "Condiciones MVFR. Ajusta combustible, briefing y margen operacional.",
                _ => "Condiciones VFR estables para la operación prevista."
            };

            return advisories.Count == 0 ? baseSummary : baseSummary + " Riesgos: " + string.Join(", ", advisories) + ".";
        }

        private string BuildOperationalQualificationsSummary()
        {
            var pilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
            if (pilot == null)
            {
                return "Sin sesión de piloto para evaluar habilitaciones operativas.";
            }

            var qualificationText = FormatOperationalList(pilot.ActiveQualifications);
            var certificationText = FormatOperationalList(pilot.ActiveCertifications);
            var worstWeather = GetWorstWeather();
            var severeWeather = worstWeather != null &&
                (GetFlightCategorySeverity(worstWeather.FlightCategory) >= 2 || worstWeather.HasThunderstorm || worstWeather.IsSnowing);
            var hasInstrumentQualification = HasOperationalToken(pilot.ActiveQualifications, "IFR", "INSTRUMENT", "RNAV", "ILS");

            var advisory = severeWeather
                ? (hasInstrumentQualification
                    ? "Perfil instrumental detectado para condiciones degradadas."
                    : "Verifica habilitación IFR/instrumental antes de liberar este tramo.")
                : "Operación estándar con habilitaciones vivas cargadas en la sesión.";

            return advisory + " Qual: " + qualificationText + ". Cert: " + certificationText + ".";
        }

        private WeatherInfo? GetWorstWeather()
        {
            var departureSeverity = GetFlightCategorySeverity(DepWeather == null ? null : DepWeather.FlightCategory);
            var arrivalSeverity = GetFlightCategorySeverity(ArrWeather == null ? null : ArrWeather.FlightCategory);
            if (ArrWeather != null && arrivalSeverity >= departureSeverity) return ArrWeather;
            return DepWeather ?? ArrWeather;
        }

        private static int GetFlightCategorySeverity(string? category)
        {
            var normalized = category == null ? string.Empty : category.Trim().ToUpperInvariant();
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
            if (string.IsNullOrWhiteSpace(source)) return false;

            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in source.Split(new[] { ',', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;
                values.Add(token);
                values.Add(token.Replace("-", "_").Replace(" ", "_"));
            }

            foreach (var candidate in candidates)
            {
                if (values.Contains(candidate)) return true;
            }

            return false;
        }

        private static string FormatOperationalList(string source)
        {
            return string.IsNullOrWhiteSpace(source) ? "sin registro" : source.Trim();
        }
    }
}
