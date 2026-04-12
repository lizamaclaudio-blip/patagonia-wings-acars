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
        public string PreferredLanguage
        {
            get => _preferredLanguage;
            set
            {
                if (SetField(ref _preferredLanguage, value))
                {
                    AcarsContext.Sound.Language = value;
                    OnPropertyChanged(nameof(PreferredLanguageLabel));
                }
            }
        }

        public bool VoiceFemale
        {
            get => _voiceFemale;
            set
            {
                if (SetField(ref _voiceFemale, value))
                {
                    AcarsContext.Sound.VoiceFemale = value;
                    OnPropertyChanged(nameof(VoiceLabel));
                    OnPropertyChanged(nameof(VoiceSelection));
                }
            }
        }
        public int VoiceSelection
        {
            get => VoiceFemale ? 0 : 1;
            set => VoiceFemale = value == 0;
        }

        public string RankImageResource => Pilot != null
            ? string.Format("pack://application:,,,/Resources/Ranks/Rango{0}.png", (int)Pilot.Rank)
            : string.Empty;

        public string PilotInitials
        {
            get
            {
                if (Pilot == null)
                {
                    return "PW";
                }

                string source = string.IsNullOrWhiteSpace(Pilot.FullName) ? Pilot.CallSign : Pilot.FullName;
                string[] parts = source.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length == 0)
                {
                    return "PW";
                }

                if (parts.Length == 1)
                {
                    return parts[0].Length >= 2
                        ? parts[0].Substring(0, 2).ToUpperInvariant()
                        : parts[0].ToUpperInvariant();
                }

                return string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant();
            }
        }

        public string PreferredLanguageLabel
            => PreferredLanguage == "CHI" ? "Chileno"
            : PreferredLanguage == "BRA" ? "PortuguÃªs (BR)"
            : "EspaÃ±ol";

        public string VoiceLabel => VoiceFemale ? "Femenina" : "Masculina";
        public string QualificationsDisplay => string.IsNullOrWhiteSpace(Pilot?.ActiveQualifications) ? "Sin calificaciones activas" : (Pilot?.ActiveQualifications ?? "Sin calificaciones activas");
        public string CertificationsDisplay => string.IsNullOrWhiteSpace(Pilot?.ActiveCertifications) ? "Sin certificaciones activas" : (Pilot?.ActiveCertifications ?? "Sin certificaciones activas");
        public string BaseHubDisplay => string.IsNullOrWhiteSpace(Pilot?.BaseHubCode) ? "No definido" : (Pilot?.BaseHubCode ?? "No definido");
        public string CurrentAirportDisplay => string.IsNullOrWhiteSpace(Pilot?.CurrentAirportCode) ? "Sin aeropuerto activo" : (Pilot?.CurrentAirportCode ?? "Sin aeropuerto activo");
        public string PreferredSimulatorDisplay => string.IsNullOrWhiteSpace(Pilot?.PreferredSimulator) ? "No definido" : (Pilot?.PreferredSimulator ?? "No definido");
        public string TotalDistanceDisplay => Pilot != null ? string.Format("{0:N0} NM", Pilot.TotalDistance) : "0 NM";
        public string TransferredHoursDisplay => Pilot != null ? string.Format("{0:F1} h", Pilot.TransferredHours) : "0.0 h";
        public string Pulso10Display => Pilot != null ? Pilot.Pulso10.ToString("F1") : "0.0";
        public string Ruta10Display => Pilot != null ? Pilot.Ruta10.ToString("F1") : "0.0";
        public string LegadoPointsDisplay => Pilot != null ? Pilot.LegadoPoints.ToString("N0") : "0";

        public string ProgressToNextRank
        {
            get
            {
                if (Pilot == null) return "0%";
                var next = Pilot.Rank == PilotRank.ComandanteTLA ? 100 : 50;
                return string.Format("{0}%", (Pilot.Points % next) * 100 / next);
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
            OnPropertyChanged(nameof(VoiceSelection));
            OnPropertyChanged(nameof(PilotInitials));
            OnPropertyChanged(nameof(PreferredLanguageLabel));
            OnPropertyChanged(nameof(VoiceLabel));
            OnPropertyChanged(nameof(QualificationsDisplay));
            OnPropertyChanged(nameof(CertificationsDisplay));
            OnPropertyChanged(nameof(BaseHubDisplay));
            OnPropertyChanged(nameof(CurrentAirportDisplay));
            OnPropertyChanged(nameof(PreferredSimulatorDisplay));
            OnPropertyChanged(nameof(TotalDistanceDisplay));
            OnPropertyChanged(nameof(TransferredHoursDisplay));
            OnPropertyChanged(nameof(Pulso10Display));
            OnPropertyChanged(nameof(Ruta10Display));
            OnPropertyChanged(nameof(LegadoPointsDisplay));
        }
    }
}
