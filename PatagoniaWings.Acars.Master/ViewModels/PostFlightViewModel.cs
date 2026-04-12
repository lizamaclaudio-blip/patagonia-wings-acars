using System;
using System.Text;
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
                var vs = Math.Abs(Report.LandingVS);
                if (vs < 100) return "Suave";
                if (vs < 200) return "Normal";
                if (vs < 400) return "Firme";
                if (vs < 700) return "Dura";
                return "Muy Dura";
            }
        }

        public string ProceduralSummary => Report?.ProceduralSummary ?? string.Empty;

        public string MaxAltDisplay => Report == null || Report.MaxAltitudeFeet <= 0
            ? "—"
            : $"{Report.MaxAltitudeFeet:F0} ft";

        public string MaxSpeedDisplay => Report == null || Report.MaxSpeedKts <= 0
            ? "—"
            : $"{Report.MaxSpeedKts:F0} kts";

        public string QnhDisplay => Report == null || Report.ApproachQnhHpa <= 0
            ? "—"
            : $"{Report.ApproachQnhHpa:F1} hPa";

        public string ScoreBreakdown
        {
            get
            {
                if (Report == null) return string.Empty;
                var sb = new StringBuilder();
                AppendPhase(sb, "Aterrizaje", Report.LandingPenalty);
                AppendPhase(sb, "Taxi", Report.TaxiPenalty);
                AppendPhase(sb, "Vuelo", Report.AirbornePenalty);
                AppendPhase(sb, "Aproximación", Report.ApproachPenalty);
                AppendPhase(sb, "Cabina / QNH", Report.CabinPenalty);
                return sb.Length == 0 ? "Sin penalizaciones" : sb.ToString().TrimEnd('·', ' ');
            }
        }

        public bool HasQualifications =>
            Report != null &&
            (!string.IsNullOrWhiteSpace(Report.PilotQualifications) || !string.IsNullOrWhiteSpace(Report.PilotCertifications));

        public string QualificationsDisplay
        {
            get
            {
                if (Report == null) return string.Empty;
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(Report.PilotQualifications))
                    parts.Add(Report.PilotQualifications.Trim());
                if (!string.IsNullOrWhiteSpace(Report.PilotCertifications))
                    parts.Add(Report.PilotCertifications.Trim());
                return string.Join("  ·  ", parts);
            }
        }

        private static void AppendPhase(StringBuilder sb, string label, int penalty)
        {
            if (penalty < 0)
                sb.Append($"{label}: {penalty}  ·  ");
            else
                sb.Append($"{label}: ok  ·  ");
        }

        public ICommand SubmitCommand { get; }

        public PostFlightViewModel()
        {
            SubmitCommand = new RelayCommand(async _ =>
            {
                if (Report == null || Submitted) return;
                IsSubmitting = true;

                var result = await AcarsContext.Api.SubmitFlightReportAsync(
                    Report,
                    AcarsContext.FlightService.CurrentFlight,
                    AcarsContext.FlightService.GetTelemetrySnapshot(),
                    AcarsContext.FlightService.LastSimData);

                if (result.Success)
                {
                    Submitted = true;
                    SubmitMessage = $"Vuelo cerrado · Score {Report.Score} pts ({Report.Grade}) guardado en Supabase.";
                    if (result.Data != null)
                    {
                        Report.PilotQualifications = result.Data.PilotQualifications;
                        Report.PilotCertifications = result.Data.PilotCertifications;
                        OnPropertyChanged(nameof(HasQualifications));
                        OnPropertyChanged(nameof(QualificationsDisplay));
                    }
                    AcarsContext.FlightService.Reset();
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
            OnPropertyChanged(nameof(ProceduralSummary));
            OnPropertyChanged(nameof(MaxAltDisplay));
            OnPropertyChanged(nameof(MaxSpeedDisplay));
            OnPropertyChanged(nameof(QnhDisplay));
            OnPropertyChanged(nameof(ScoreBreakdown));
            OnPropertyChanged(nameof(HasQualifications));
            OnPropertyChanged(nameof(QualificationsDisplay));
        }
    }
}
