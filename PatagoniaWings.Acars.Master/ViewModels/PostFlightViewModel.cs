using System.Windows.Input;
using PatagoniaWings.Acars.Core.Models;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.ViewModels
{
    public class PostFlightViewModel : ViewModelBase
    {
        private FlightReport? _report;
        private bool _isSubmitting;
        private bool _submitted;
        private string _submitMessage = string.Empty;

        public FlightReport? Report { get => _report; set => SetField(ref _report, value); }
        public bool IsSubmitting { get => _isSubmitting; set => SetField(ref _isSubmitting, value); }
        public bool Submitted { get => _submitted; set => SetField(ref _submitted, value); }
        public string SubmitMessage { get => _submitMessage; set => SetField(ref _submitMessage, value); }

        // Propiedades de display
        public string GradeColor => Report?.Grade switch
        {
            "A+" or "A" => "#44CC44",
            "B" => "#AACC44",
            "C" => "#CCAA44",
            "D" => "#CC7744",
            _ => "#CC4444"
        };

        public string LandingQuality
        {
            get
            {
                if (Report == null) return string.Empty;
                var vs = System.Math.Abs(Report.LandingVS);
                if (vs < 100) return "Suave";
                if (vs < 200) return "Normal";
                if (vs < 400) return "Firme";
                if (vs < 700) return "Dura";
                return "Muy Dura";
            }
        }

        public ICommand SubmitCommand { get; }

        public PostFlightViewModel()
        {
            SubmitCommand = new RelayCommand(async _ =>
            {
                if (Report == null || Submitted) return;
                IsSubmitting = true;

                var result = await AcarsContext.Api.SubmitFlightReportAsync(Report);
                if (result.Success)
                {
                    Submitted = true;
                    SubmitMessage = $"¡Vuelo enviado! Ganaste {Report.PointsEarned} puntos.";
                    AcarsContext.Sound.PlayDing();
                    _ = AcarsContext.Sound.PlayGroundArrivedAsync();
                }
                else
                {
                    SubmitMessage = $"Error al enviar: {result.Error}";
                }
                IsSubmitting = false;
            });
        }

        public void LoadReport(FlightReport report)
        {
            Report = report;
            Submitted = false;
            SubmitMessage = string.Empty;
            OnPropertyChanged(nameof(GradeColor));
            OnPropertyChanged(nameof(LandingQuality));
        }
    }
}
