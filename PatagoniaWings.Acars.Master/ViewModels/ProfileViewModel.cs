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

        public Pilot? Pilot { get => _pilot; set => SetField(ref _pilot, value); }
        public bool IsLoading { get => _isLoading; set => SetField(ref _isLoading, value); }
        public string PreferredLanguage { get => _preferredLanguage; set { SetField(ref _preferredLanguage, value); AcarsContext.Sound.Language = value; } }
        public bool VoiceFemale { get => _voiceFemale; set { SetField(ref _voiceFemale, value); AcarsContext.Sound.VoiceFemale = value; } }

        public string RankImageResource => Pilot != null
            ? string.Format("pack://application:,,,/Resources/Ranks/Rango{0}.png", (int)Pilot.Rank)
            : string.Empty;

        public string ProgressToNextRank
        {
            get
            {
                if (Pilot == null) return "0%";
                var next = Pilot.Rank == PilotRank.ComandanteTLA ? 100 : 50;
                return string.Format("{0}%", (Pilot.Points % next) * 100 / next);
            }
        }

        public double ProgressToNextRankValue
        {
            get
            {
                if (Pilot == null) return 0;
                var next = Pilot.Rank == PilotRank.ComandanteTLA ? 100 : 50;
                var pct = (double)(Pilot.Points % next) / next;
                return System.Math.Max(4, pct * 340);
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
                    AcarsContext.Runtime.SetCurrentPilot(AcarsContext.Auth.CurrentPilot);
                    AcarsContext.Sound.PlayDing();
                }
            });

            AcarsContext.Runtime.Changed += () =>
            {
                var runtimePilot = AcarsContext.Runtime.CurrentPilot;
                if (runtimePilot != null)
                {
                    ApplyPilot(runtimePilot);
                }
            };
        }

        public void LoadAsync()
        {
            var cachedPilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
            if (cachedPilot != null)
            {
                ApplyPilot(cachedPilot);
                IsLoading = false;
            }
            else
            {
                Pilot = null;
                IsLoading = true;
            }

            _ = RefreshPilotAsync();
        }

        private async Task RefreshPilotAsync()
        {
            try
            {
                var result = await AcarsContext.Api.GetCurrentPilotAsync();
                if (result.Success && result.Data != null)
                {
                    AcarsContext.Auth.SetCurrentPilot(result.Data);
                    AcarsContext.Runtime.SetCurrentPilot(AcarsContext.Auth.CurrentPilot);
                    ApplyPilot(AcarsContext.Auth.CurrentPilot);
                }
                else if (Pilot == null)
                {
                    var fallbackPilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
                    if (fallbackPilot != null)
                    {
                        ApplyPilot(fallbackPilot);
                    }
                }
            }
            catch
            {
                if (Pilot == null)
                {
                    var fallbackPilot = AcarsContext.Runtime.CurrentPilot ?? AcarsContext.Auth.CurrentPilot;
                    if (fallbackPilot != null)
                    {
                        ApplyPilot(fallbackPilot);
                    }
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyPilot(Pilot? pilot)
        {
            if (pilot == null)
            {
                return;
            }

            Pilot = pilot;
            PreferredLanguage = pilot.Language;
            VoiceFemale = pilot.CopilotVoiceFemale;
            OnPropertyChanged(nameof(RankImageResource));
            OnPropertyChanged(nameof(ProgressToNextRank));
            OnPropertyChanged(nameof(ProgressToNextRankValue));
        }
    }
}
