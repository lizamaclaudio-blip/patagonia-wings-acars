using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public class PreFlightViewModel : ViewModelBase
    {
        // ── Datos del plan de vuelo ────────────────────────────────────────────
        private string _flightNumber = string.Empty;
        private string _departureIcao = string.Empty;
        private string _arrivalIcao = string.Empty;
        private string _aircraftIcao = string.Empty;
        private string _route = string.Empty;
        private int _plannedAlt = 35000;
        private int _plannedSpeed = 450;
        private string _remarks = string.Empty;

        public string FlightNumber { get => _flightNumber; set => SetField(ref _flightNumber, value); }
        public string DepartureIcao { get => _departureIcao; set => SetField(ref _departureIcao, value); }
        public string ArrivalIcao { get => _arrivalIcao; set => SetField(ref _arrivalIcao, value); }
        public string AircraftIcao { get => _aircraftIcao; set => SetField(ref _aircraftIcao, value); }
        public string Route { get => _route; set => SetField(ref _route, value); }
        public int PlannedAlt { get => _plannedAlt; set => SetField(ref _plannedAlt, value); }
        public int PlannedSpeed { get => _plannedSpeed; set => SetField(ref _plannedSpeed, value); }
        public string Remarks { get => _remarks; set => SetField(ref _remarks, value); }

        // ── Simulador ─────────────────────────────────────────────────────────
        private SimulatorType _selectedSim = SimulatorType.MSFS2020;
        public SimulatorType SelectedSim { get => _selectedSim; set => SetField(ref _selectedSim, value); }

        public ObservableCollection<string> SimulatorOptions { get; } = new()
        {
            "MSFS 2020", "MSFS 2024", "X-Plane 12", "X-Plane 11"
        };

        // ── METAR ─────────────────────────────────────────────────────────────
        private Airport? _depAirport;
        private Airport? _arrAirport;
        private string _depMetar = string.Empty;
        private string _arrMetar = string.Empty;
        private bool _isLoadingMetar;

        public Airport? DepAirport { get => _depAirport; set => SetField(ref _depAirport, value); }
        public Airport? ArrAirport { get => _arrAirport; set => SetField(ref _arrAirport, value); }
        public string DepMetar { get => _depMetar; set => SetField(ref _depMetar, value); }
        public string ArrMetar { get => _arrMetar; set => SetField(ref _arrMetar, value); }
        public bool IsLoadingMetar { get => _isLoadingMetar; set => SetField(ref _isLoadingMetar, value); }

        // ── Estado ────────────────────────────────────────────────────────────
        private string _statusMessage = string.Empty;
        private bool _flightStarted;
        public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }
        public bool FlightStarted { get => _flightStarted; set => SetField(ref _flightStarted, value); }

        // ── Comandos ──────────────────────────────────────────────────────────
        public ICommand FetchMetarCommand { get; }
        public ICommand StartFlightCommand { get; }

        public PreFlightViewModel()
        {
            FetchMetarCommand = new RelayCommand(async _ =>
            {
                if (string.IsNullOrWhiteSpace(DepartureIcao) && string.IsNullOrWhiteSpace(ArrivalIcao)) return;
                IsLoadingMetar = true;
                DepMetar = string.Empty;
                ArrMetar = string.Empty;

                if (!string.IsNullOrWhiteSpace(DepartureIcao))
                {
                    var r = await AcarsContext.Api.GetMetarAsync(DepartureIcao);
                    if (r.Success && r.Data != null)
                    {
                        DepMetar = r.Data.RawMetar;
                        DepAirport = new Airport { Icao = DepartureIcao, Metar = r.Data.RawMetar };
                    }
                }
                if (!string.IsNullOrWhiteSpace(ArrivalIcao))
                {
                    var r = await AcarsContext.Api.GetMetarAsync(ArrivalIcao);
                    if (r.Success && r.Data != null)
                    {
                        ArrMetar = r.Data.RawMetar;
                        ArrAirport = new Airport { Icao = ArrivalIcao, Metar = r.Data.RawMetar };
                    }
                }
                IsLoadingMetar = false;
            });

            StartFlightCommand = new RelayCommand(async _ =>
            {
                if (string.IsNullOrWhiteSpace(FlightNumber) ||
                    string.IsNullOrWhiteSpace(DepartureIcao) ||
                    string.IsNullOrWhiteSpace(ArrivalIcao) ||
                    string.IsNullOrWhiteSpace(AircraftIcao))
                {
                    StatusMessage = "Completa todos los campos obligatorios.";
                    return;
                }

                var flight = new Flight
                {
                    FlightNumber = FlightNumber.ToUpper(),
                    DepartureIcao = DepartureIcao.ToUpper(),
                    ArrivalIcao = ArrivalIcao.ToUpper(),
                    AircraftIcao = AircraftIcao.ToUpper(),
                    Route = Route,
                    PlannedAltitude = PlannedAlt,
                    PlannedSpeed = PlannedSpeed,
                    Remarks = Remarks,
                    Simulator = SelectedSim,
                    StartTime = DateTime.UtcNow
                };

                var result = await AcarsContext.Api.StartFlightAsync(flight);
                if (result.Success)
                {
                    AcarsContext.FlightService.StartFlight(flight, 0);
                    AcarsContext.Sound.PlayDing();
                    _ = AcarsContext.Sound.PlayGroundBienvenidoAsync();
                    FlightStarted = true;
                    StatusMessage = $"¡Vuelo {FlightNumber} iniciado! Conecta el simulador.";
                }
                else
                {
                    StatusMessage = $"Error al registrar vuelo: {result.Error}";
                }
            });
        }
    }
}
