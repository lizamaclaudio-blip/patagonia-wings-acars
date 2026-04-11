using System.Collections.ObjectModel;
using System.Windows.Input;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public class CommunityViewModel : ViewModelBase
    {
        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }

        public ObservableCollection<Pilot> OnlinePilots { get; } = new();
        public ObservableCollection<Pilot> Leaderboard { get; } = new();

        private int _selectedTab;
        public int SelectedTab { get => _selectedTab; set { SetField(ref _selectedTab, value); LoadCurrentTab(); } }

        public ICommand RefreshCommand { get; }

        public CommunityViewModel()
        {
            RefreshCommand = new RelayCommand(() => LoadCurrentTab());
        }

        private void LoadCurrentTab()
        {
            if (SelectedTab == 0) LoadOnlinePilots();
            else LoadLeaderboard();
        }

        public async void LoadOnlinePilots()
        {
            IsLoading = true;
            var result = await AcarsContext.Api.GetOnlinePilotsAsync();
            if (result.Success && result.Data != null)
            {
                OnlinePilots.Clear();
                foreach (var p in result.Data)
                    OnlinePilots.Add(p);
            }
            IsLoading = false;
        }

        public async void LoadLeaderboard()
        {
            IsLoading = true;
            var result = await AcarsContext.Api.GetLeaderboardAsync();
            if (result.Success && result.Data != null)
            {
                Leaderboard.Clear();
                foreach (var p in result.Data)
                    Leaderboard.Add(p);
            }
            IsLoading = false;
        }
    }
}
