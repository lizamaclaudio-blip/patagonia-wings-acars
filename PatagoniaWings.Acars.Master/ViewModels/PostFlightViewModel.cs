using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
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
        private bool _sendToMaintenance;
        private string _submitMessage = string.Empty;
        private string _closeoutComments = string.Empty;
        private string _aircraftNotams = string.Empty;

        public PostFlightViewModel()
        {
            // El boton visual sigue siendo la entrada oficial al pipeline de closeout.
            SubmitCommand = new AsyncRelayCommand(async _ => await RunCloseoutAsync(), _ => CanSubmit);
        }

        public FlightReport? Report
        {
            get => _report;
            set
            {
                if (SetField(ref _report, value))
                {
                    OnPropertyChanged(nameof(ProcedureScore));
                    OnPropertyChanged(nameof(ProcedureGrade));
                    OnPropertyChanged(nameof(ProcedureScoreColor));
                    OnPropertyChanged(nameof(PerformanceScore));
                    OnPropertyChanged(nameof(PerformanceGrade));
                    OnPropertyChanged(nameof(PerformanceScoreColor));
                    OnPropertyChanged(nameof(PatagoniaScore));
                    OnPropertyChanged(nameof(PatagoniaGrade));
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
                    OnPropertyChanged(nameof(HasReport));
                    OnPropertyChanged(nameof(FlightNumberDisplay));
                    OnPropertyChanged(nameof(RouteDisplay));
                    OnPropertyChanged(nameof(CloseButtonTitle));
                    OnPropertyChanged(nameof(CanSubmit));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsSubmitting
        {
            get => _isSubmitting;
            set
            {
                if (SetField(ref _isSubmitting, value))
                {
                    OnPropertyChanged(nameof(CloseButtonTitle));
                    OnPropertyChanged(nameof(CanSubmit));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool Submitted
        {
            get => _submitted;
            set
            {
                if (SetField(ref _submitted, value))
                {
                    OnPropertyChanged(nameof(CloseButtonTitle));
                    OnPropertyChanged(nameof(CanSubmit));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool SendToMaintenance
        {
            get => _sendToMaintenance;
            set => SetField(ref _sendToMaintenance, value);
        }

        public string SubmitMessage
        {
            get => _submitMessage;
            set => SetField(ref _submitMessage, value);
        }

        public string CloseoutComments
        {
            get => _closeoutComments;
            set => SetField(ref _closeoutComments, value);
        }

        public string AircraftNotams
        {
            get => _aircraftNotams;
            set => SetField(ref _aircraftNotams, value);
        }

        public int PatagoniaScore => Report?.PatagoniaScore ?? 0;
        public string PatagoniaGrade => string.IsNullOrWhiteSpace(Report?.PatagoniaGrade) ? "-" : Report!.PatagoniaGrade;

        public int ProcedureScore => Report?.ProcedureScore ?? 0;
        public string ProcedureGrade => Report?.ProcedureGrade ?? "-";
        public string ProcedureScoreColor => ScoreColor(ProcedureScore);

        public int PerformanceScore => Report?.PerformanceScore ?? 0;
        public string PerformanceGrade => Report?.PerformanceGrade ?? "-";
        public string PerformanceScoreColor => ScoreColor(PerformanceScore);

        public IReadOnlyList<ScoreEvent> Violations => Report?.Violations ?? new List<ScoreEvent>();
        public IReadOnlyList<ScoreEvent> Bonuses => Report?.Bonuses ?? new List<ScoreEvent>();
        public bool HasViolations => Violations.Count > 0;
        public bool HasBonuses => Bonuses.Count > 0;
        public bool HasReport => Report != null;
        public bool CanSubmit => Report != null && !IsSubmitting && !Submitted;

        public string FlightNumberDisplay => string.IsNullOrWhiteSpace(Report?.FlightNumber) ? "PW0000" : Report!.FlightNumber;

        public string RouteDisplay
        {
            get
            {
                if (Report == null)
                {
                    return "---- - ----";
                }

                return string.Format("{0} - {1}", Report.DepartureIcao ?? "----", Report.ArrivalIcao ?? "----");
            }
        }

        public string MaintenanceHint => "Al enviar a mantenimiento, la aeronave queda marcada para revision operativa.";

        public string CloseButtonTitle
        {
            get
            {
                if (IsSubmitting)
                {
                    return "ENVIANDO...";
                }

                if (Submitted)
                {
                    return "PIREP ENVIADO";
                }

                return "CERRAR VUELO";
            }
        }

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
                return "Muy dura";
            }
        }

        public string LandingQualityColor
        {
            get
            {
                if (Report == null) return "#CBD5E1";
                var vs = Math.Abs(Report.LandingVS);
                if (vs < 200) return "#10B981";
                if (vs < 400) return "#F59E0B";
                if (vs < 700) return "#F97316";
                return "#EF4444";
            }
        }

        public string MaxAltDisplay => Report == null || Report.MaxAltitudeFeet <= 0
            ? "-"
            : string.Format("{0:F0} ft", Report.MaxAltitudeFeet);

        public string MaxSpeedDisplay => Report == null || Report.MaxSpeedKts <= 0
            ? "-"
            : string.Format("{0:F0} kt", Report.MaxSpeedKts);

        public string QnhDisplay => Report == null || Report.ApproachQnhHpa <= 0
            ? "-"
            : string.Format("{0:F1} hPa", Report.ApproachQnhHpa);

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
                {
                    parts.Add(Report.PilotQualifications.Trim());
                }

                if (!string.IsNullOrWhiteSpace(Report.PilotCertifications))
                {
                    parts.Add(Report.PilotCertifications.Trim());
                }

                return string.Join(" | ", parts);
            }
        }

        public ICommand SubmitCommand { get; }

        public void LoadReport(FlightReport report)
        {
            Report = report;
            Submitted = IsCloseoutAlreadyResolved(report.ResultStatus);
            IsSubmitting = false;
            SendToMaintenance = false;
            CloseoutComments = string.Empty;
            AircraftNotams = string.Empty;

            SubmitMessage = Submitted
                ? (string.Equals(report.ResultStatus, "queued_retry", StringComparison.OrdinalIgnoreCase)
                    ? "PIREP ya consolidado localmente y en cola de reintento."
                    : "PIREP ya consolidado y sincronizado.")
                : "Completa el cierre y confirma el envio del PIREP.";
        }

        private async Task RunCloseoutAsync()
        {
            if (!CanSubmit || Report == null)
            {
                return;
            }

            IsSubmitting = true;
            SubmitMessage = "Consolidando closeout y enviando PIREP...";

            try
            {
                ApplyCloseoutInputs();

                var result = await AcarsContext.Api.SubmitFlightReportAsync(
                    Report,
                    AcarsContext.FlightService.CurrentFlight,
                    AcarsContext.FlightService.GetTelemetrySnapshot(),
                    AcarsContext.FlightService.LastSimData,
                    AcarsContext.FlightService.GetDamageEventsSnapshot());

                if (!result.Success)
                {
                    Submitted = false;
                    SubmitMessage = "No se pudo cerrar el vuelo: " + result.Error;
                    return;
                }

                if (result.Data != null)
                {
                    Report = result.Data;
                    Report.ReservationId = result.Data.ReservationId;
                    Report.ResultUrl = result.Data.ResultUrl;
                    Report.ResultStatus = result.Data.ResultStatus;
                    Report.PilotQualifications = result.Data.PilotQualifications;
                    Report.PilotCertifications = result.Data.PilotCertifications;
                }

                Submitted = true;

                var isQueued = string.Equals(Report?.ResultStatus, "queued_retry", StringComparison.OrdinalIgnoreCase);
                SubmitMessage = isQueued
                    ? string.Format(
                        "Vuelo cerrado localmente | Patagonia {0} pts | PIREP en cola para reintento automatico.",
                        Report?.PatagoniaScore ?? 0)
                    : string.Format(
                        "Vuelo cerrado | Patagonia {0} pts | Proc {1} pts ({2}) | Perf {3} pts ({4}) | guardado en Supabase.",
                        Report?.PatagoniaScore ?? 0,
                        Report?.ProcedureScore ?? 0,
                        Report?.ProcedureGrade ?? "-",
                        Report?.PerformanceScore ?? 0,
                        Report?.PerformanceGrade ?? "-");

                AcarsContext.FlightService.Reset();
                AcarsContext.Sound.PlayDing();
                _ = AcarsContext.Sound.PlayGroundArrivedAsync();

                var resultUrl = Report?.ResultUrl;
                if (!isQueued && !string.IsNullOrWhiteSpace(resultUrl))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = resultUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        SubmitMessage += " | No pude abrir la web: " + ex.Message;
                    }
                }
            }
            finally
            {
                IsSubmitting = false;
            }
        }

        private void ApplyCloseoutInputs()
        {
            if (Report == null)
            {
                return;
            }

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(CloseoutComments))
            {
                parts.Add("Comentarios: " + CloseoutComments.Trim());
            }

            if (!string.IsNullOrWhiteSpace(AircraftNotams))
            {
                parts.Add("NOTAMs aeronave: " + AircraftNotams.Trim());
            }

            if (SendToMaintenance)
            {
                parts.Add("Mantenimiento solicitado por la tripulacion.");
            }

            Report.Remarks = string.Join(" | ", parts);
        }

        private static bool IsCloseoutAlreadyResolved(string? status)
        {
            var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "completed" || normalized == "queued_retry";
        }

        private static string ScoreColor(int score)
        {
            if (score >= 90) return "#10B981";
            if (score >= 75) return "#3B82F6";
            if (score >= 60) return "#F59E0B";
            return "#EF4444";
        }
    }
}
