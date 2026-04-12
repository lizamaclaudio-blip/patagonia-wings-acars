using System;
using System.Collections.ObjectModel;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public class DashboardViewModel : ViewModelBase
    {
        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }

        public ObservableCollection<FlightReport> RecentFlights { get; } = new();

        private string _totalHours = "0.0";
        private string _totalFlights = "0";
        private string _totalDistance = "0";
        private string _rankName = string.Empty;

        public string TotalHours { get => _totalHours; set => SetField(ref _totalHours, value); }
        public string TotalFlights { get => _totalFlights; set => SetField(ref _totalFlights, value); }
        public string TotalDistance { get => _totalDistance; set => SetField(ref _totalDistance, value); }
        public string RankName { get => _rankName; set => SetField(ref _rankName, value); }

        public async void LoadAsync()
        {
            IsLoading = true;

            try
            {
                var pilot = AcarsContext.Auth.CurrentPilot;
                if (pilot != null)
                {
                    TotalHours = pilot.TotalHours.ToString("F1");
                    TotalFlights = pilot.TotalFlights.ToString();
                    TotalDistance = pilot.TotalDistance.ToString("F0");
                    RankName = pilot.RankName;
                }
                else
                {
                    TotalHours = "0.0";
                    TotalFlights = "0";
                    TotalDistance = "0";
                    RankName = "Sin sesión";
                }

                var result = await AcarsContext.Api.GetMyFlightsAsync(1);
                if (result.Success && result.Data != null)
                {
                    RecentFlights.Clear();
                    foreach (var f in result.Data)
                    {
                        RecentFlights.Add(f);
                    }
                }
            }
            catch
            {
                // Evitar que un problema de carga del dashboard tumbe la apertura principal.
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
