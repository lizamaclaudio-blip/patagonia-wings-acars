using System.Windows.Input;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public class ProfileViewModel : ViewModelBase
    {
        private Pilot? _pilot;
        private bool _isLoading;
        private string _preferredLanguage = "ESP";
        private bool _voiceFemale = true;

        public Pilot? Pilot { get => _pilot; set => SetField(ref _pilot, value); }
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }
        public string PreferredLanguage { get => _preferredLanguage; set { SetField(ref _preferredLanguage, value); AcarsContext.Sound.Language = value; } }
        public bool VoiceFemale { get => _voiceFemale; set { SetField(ref _voiceFemale, value); AcarsContext.Sound.VoiceFemale = value; } }

        public string RankImageResource => Pilot != null
            ? $"pack://application:,,,/Resources/Ranks/Rango{(int)Pilot.Rank}.png"
            : string.Empty;

        public string ProgressToNextRank
        {
            get
            {
                if (Pilot == null) return "0%";
                var next = Pilot.Rank == PilotRank.ComandanteTLA ? 100 : 50;
                return $"{(Pilot.Points % next) * 100 / next}%";
            }
        }

        public ICommand SavePreferencesCommand { get; }

        public ProfileViewModel()
        {
            SavePreferencesCommand = new RelayCommand(() =>
            {
                if (Pilot != null)
                {
                    Pilot.Language = PreferredLanguage;
                    Pilot.CopilotVoiceFemale = VoiceFemale;
                    AcarsContext.Auth.SaveSession(Pilot);
                    AcarsContext.Sound.PlayDing();
                }
            });
        }

        public async void LoadAsync()
        {
            IsLoading = true;
            var result = await AcarsContext.Api.GetCurrentPilotAsync();
            if (result.Success && result.Data != null)
            {
                Pilot = result.Data;
                PreferredLanguage = Pilot.Language;
                VoiceFemale = Pilot.CopilotVoiceFemale;
                OnPropertyChanged(nameof(RankImageResource));
                OnPropertyChanged(nameof(ProgressToNextRank));
            }
            else
            {
                Pilot = AcarsContext.Auth.CurrentPilot;
            }
            IsLoading = false;
        }
    }
}
