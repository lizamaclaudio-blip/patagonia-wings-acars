using System.Threading.Tasks;
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

        public Pilot? Pilot
        {
            get => _pilot ?? AcarsContext.Auth.CurrentPilot;
            set => SetField(ref _pilot, value);
        }
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }
        public bool ShowLoadingHint => IsLoading && Pilot == null;
        public string PreferredLanguage
        {
            get => _preferredLanguage;
            set
            {
                SetField(ref _preferredLanguage, value);
                AcarsContext.Sound.Language = value;
            }
        }

        public bool VoiceFemale
        {
            get => _voiceFemale;
            set
            {
                SetField(ref _voiceFemale, value);
                AcarsContext.Sound.VoiceFemale = value;
            }
        }

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

        public void LoadAsync()
        {
            var cachedPilot = AcarsContext.Auth.CurrentPilot;
            if (cachedPilot != null)
            {
                ApplyPilotSnapshot(cachedPilot);
            }
            else
            {
                Pilot = null;
                IsLoading = true;
                OnPropertyChanged(nameof(Pilot));
                OnPropertyChanged(nameof(ShowLoadingHint));
            }

            _ = RefreshPilotAsync();
        }

        public void ApplyPilotSnapshot(Pilot pilot)
        {
            ApplyPilot(pilot);
            IsLoading = false;
            OnPropertyChanged(nameof(Pilot));
            OnPropertyChanged(nameof(ShowLoadingHint));
        }

        private async Task RefreshPilotAsync()
        {
            try
            {
                var result = await AcarsContext.Api.GetCurrentPilotAsync();
                if (result.Success && result.Data != null)
                {
                    ApplyPilotSnapshot(result.Data);
                    AcarsContext.Auth.SaveSession(result.Data);
                }
                else if (Pilot == null)
                {
                    var fallbackPilot = AcarsContext.Auth.CurrentPilot;
                    if (fallbackPilot != null)
                    {
                        ApplyPilotSnapshot(fallbackPilot);
                    }
                }
            }
            catch
            {
                if (Pilot == null)
                {
                    var fallbackPilot = AcarsContext.Auth.CurrentPilot;
                    if (fallbackPilot != null)
                    {
                        ApplyPilotSnapshot(fallbackPilot);
                    }
                }
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(Pilot));
                OnPropertyChanged(nameof(ShowLoadingHint));
            }
        }

        private void ApplyPilot(Pilot pilot)
        {
            Pilot = pilot;
            PreferredLanguage = pilot.Language;
            VoiceFemale = pilot.CopilotVoiceFemale;
            OnPropertyChanged(nameof(RankImageResource));
            OnPropertyChanged(nameof(ProgressToNextRank));
        }
    }
}
