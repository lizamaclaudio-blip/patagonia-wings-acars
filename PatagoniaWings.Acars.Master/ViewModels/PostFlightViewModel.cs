using System;
using System.Collections.Generic;
using System.Linq;
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

        // ── Procedimientos ─────────────────────────────────────────────────────
        public int ProcedureScore => Report?.ProcedureScore ?? 0;
        public string ProcedureGrade => Report?.ProcedureGrade ?? "—";
        public string ProcedureScoreColor => ScoreColor(ProcedureScore);

        // ── Performance ────────────────────────────────────────────────────────
        public int PerformanceScore => Report?.PerformanceScore ?? 0;
        public string PerformanceGrade => Report?.PerformanceGrade ?? "—";
        public string PerformanceScoreColor => ScoreColor(PerformanceScore);

        // ── Violations / Bonuses ───────────────────────────────────────────────
        public IReadOnlyList<ScoreEvent> Violations => Report?.Violations ?? new List<ScoreEvent>();
        public IReadOnlyList<ScoreEvent> Bonuses    => Report?.Bonuses    ?? new List<ScoreEvent>();
        public bool HasViolations => Violations.Count > 0;
        public bool HasBonuses    => Bonuses.Count > 0;

        // ── Aterrizaje ─────────────────────────────────────────────────────────
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

        public string LandingQualityColor
        {
            get
            {
                if (Report == null) return "#CBD5E1";
                var vs = Math.Abs(Report.LandingVS);
                if (vs < 200) return "#10B981";   // verde
                if (vs < 400) return "#F59E0B";   // amarillo
                if (vs < 700) return "#F97316";   // naranja
                return "#EF4444";                  // rojo
            }
        }

        // ── Stats ──────────────────────────────────────────────────────────────
        public string MaxAltDisplay => Report == null || Report.MaxAltitudeFeet <= 0
            ? "—" : $"{Report.MaxAltitudeFeet:F0} ft";

        public string MaxSpeedDisplay => Report == null || Report.MaxSpeedKts <= 0
            ? "—" : $"{Report.MaxSpeedKts:F0} kts";

        public string QnhDisplay => Report == null || Report.ApproachQnhHpa <= 0
            ? "—" : $"{Report.ApproachQnhHpa:F1} hPa";

        // ── Habilitaciones ─────────────────────────────────────────────────────
        public bool HasQualifications =>
            Report != null &&
            (!string.IsNullOrWhiteSpace(Report.PilotQualifications) ||
             !string.IsNullOrWhiteSpace(Report.PilotCertifications));

        public string QualificationsDisplay
        {
            get
            {
                if (Report == null) return string.Empty;
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Report.PilotQualifications))
                    parts.Add(Report.PilotQualifications.Trim());
                if (!string.IsNullOrWhiteSpace(Report.PilotCertifications))
                    parts.Add(Report.PilotCertifications.Trim());
                return string.Join("  ·  ", parts);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static string ScoreColor(int score)
        {
            if (score >= 90) return "#10B981";   // verde esmeralda — Excelente
            if (score >= 75) return "#3B82F6";   // azul — Satisfactorio
            if (score >= 60) return "#F59E0B";   // ámbar — Marginal
            return "#EF4444";                     // rojo — Insatisfactorio
        }

        // ── Comando ────────────────────────────────────────────────────────────
        public ICommand SubmitCommand { get; }

        public PostFlightViewModel()
        {
            SubmitCommand = new AsyncRelayCommand(async _ =>
            {
                if (Report == null || Submitted) return;
                IsSubmitting = true;

                var result = await AcarsContext.Api.SubmitFlightReportAsync(
                    Report,
                    AcarsContext.FlightService.CurrentFlight,
                    AcarsContext.FlightService.GetTelemetrySnapshot(),
                    AcarsContext.FlightService.LastSimData,
                    AcarsContext.FlightService.GetDamageEventsSnapshot());

                if (result.Success)
                {
                    Submitted = true;
                    SubmitMessage = string.Format(
                        "Vuelo cerrado · Proc {0} pts ({1}) · Perf {2} pts ({3}) · guardado en Supabase.",
                        Report.ProcedureScore, Report.ProcedureGrade,
                        Report.PerformanceScore, Report.PerformanceGrade);

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
            OnPropertyChanged(nameof(ProcedureScore));
            OnPropertyChanged(nameof(ProcedureGrade));
            OnPropertyChanged(nameof(ProcedureScoreColor));
            OnPropertyChanged(nameof(PerformanceScore));
            OnPropertyChanged(nameof(PerformanceGrade));
            OnPropertyChanged(nameof(PerformanceScoreColor));
            OnPropertyChanged(nameof(Violations));
            OnPropertyChanged(nameof(Bonuses));
            OnPropertyChanged(nameof(HasViolations));
            OnPropertyChanged(nameof(HasBonuses));
            OnPropertyChanged(nameof(LandingQuality));
            OnPropertyChanged(nameof(LandingQualityColor));
            OnPropertyChanged(nameof(MaxAltDisplay));
            OnPropertyChanged(nameof(MaxSpeedDisplay));
            OnPropertyChanged(nameof(QnhDisplay));
            OnPropertyChanged(nameof(HasQualifications));
            OnPropertyChanged(nameof(QualificationsDisplay));
        }
    }
}
