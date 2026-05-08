using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
                    OnPropertyChanged(nameof(AircraftRegistrationDisplay));
                    OnPropertyChanged(nameof(RouteDisplay));
                    OnPropertyChanged(nameof(CloseButtonTitle));
                    OnPropertyChanged(nameof(CanSubmit));
                    OnPropertyChanged(nameof(IsPendingCloseoutRetry));
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
                    OnPropertyChanged(nameof(IsPendingCloseoutRetry));
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
                    OnPropertyChanged(nameof(IsPendingCloseoutRetry));
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

        public bool IsPendingCloseoutRetry => IsCloseoutPendingRetry(Report?.ResultStatus);

        public string FlightNumberDisplay => string.IsNullOrWhiteSpace(Report?.FlightNumber) ? "PW0000" : Report!.FlightNumber;

        public string AircraftRegistrationDisplay
        {
            get
            {
                var dispatchRegistration = AcarsContext.Runtime.CurrentDispatch?.AircraftRegistration;
                if (!string.IsNullOrWhiteSpace(dispatchRegistration))
                {
                    return dispatchRegistration!.Trim();
                }

                var reportAircraft = Report?.AircraftIcao;
                return string.IsNullOrWhiteSpace(reportAircraft) ? string.Empty : reportAircraft!.Trim();
            }
        }

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

                if (Submitted && !IsPendingCloseoutRetry)
                {
                    return "PIREP ENVIADO";
                }

                if (IsPendingCloseoutRetry)
                {
                    return "REENVIAR PIREP";
                }

                return "ENVIAR PIREP";
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

                if (parts.Count == 0)
                {
                    return "No disponible";
                }

                var deduped = parts
                    .SelectMany(value => value.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
                    .Select(value => value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return deduped.Count == 0 ? "No disponible" : string.Join(" | ", deduped);
            }
        }

        public ICommand SubmitCommand { get; }
        public event Action? CloseoutCompleted;

        public void LoadReport(FlightReport report)
        {
            Report = report;
            Submitted = IsCloseoutAlreadyResolved(report.ResultStatus) && report.ReservationClosed;
            IsSubmitting = false;
            SendToMaintenance = false;
            CloseoutComments = string.Empty;
            AircraftNotams = string.Empty;

            if (Submitted)
            {
                SubmitMessage = "PIREP enviado y consolidado correctamente.";
            }
            else if (IsCloseoutPendingRetry(report.ResultStatus))
            {
                SubmitMessage = "PIREP pendiente de sincronizacion. Reintenta el envio cuando tengas conexion.";
            }
            else
            {
                SubmitMessage = "Completa el cierre y confirma el envio del PIREP.";
            }

            OnPropertyChanged(nameof(CloseButtonTitle));
            OnPropertyChanged(nameof(CanSubmit));
            OnPropertyChanged(nameof(IsPendingCloseoutRetry));
            CommandManager.InvalidateRequerySuggested();
        }

        private async Task RunCloseoutAsync()
        {
            if (!CanSubmit || Report == null)
            {
                return;
            }

            IsSubmitting = true;
            SubmitMessage = "Enviando PIREP a Patagonia Wings...";

            try
            {
                ApplyCloseoutInputs();

                // ── Enviar al backend oficial ────────────────────────────────────
                var result = await AcarsContext.Api.SubmitFlightReportAsync(
                    Report,
                    AcarsContext.FlightService.CurrentFlight,
                    AcarsContext.FlightService.GetTelemetrySnapshot(),
                    AcarsContext.FlightService.LastSimData,
                    AcarsContext.FlightService.GetDamageEventsSnapshot());

                // ── Validar respuesta del servidor ────────────────────────────────
                if (!result.Success)
                {
                    Submitted = false;
                    SubmitMessage = "Pendiente de sincronizacion. " + (result.Error ?? "Error de comunicacion con el servidor.");
                    // Guardar en cola local para reintento posterior
                    return;
                }

                if (result.Data == null)
                {
                    Submitted = false;
                    SubmitMessage = "Pendiente de sincronizacion. El servidor no confirmo el cierre.";
                    return;
                }

                // Actualizar reporte con datos del servidor
                Report = result.Data;
                Report.ReservationId = result.Data.ReservationId;
                Report.ResultUrl = result.Data.ResultUrl;
                Report.ResultStatus = result.Data.ResultStatus;
                Report.PilotQualifications = result.Data.PilotQualifications;
                Report.PilotCertifications = result.Data.PilotCertifications;

                // Solo marcar como enviado si el servidor confirmo persistencia real.
                var serverConfirmed = IsCloseoutServerConfirmed(Report.ResultStatus);
                var reviewRequired = IsCloseoutReviewRequired(Report.ResultStatus);
                var isQueued = IsCloseoutPendingRetry(Report.ResultStatus);

                var hasSummaryUrl = Uri.TryCreate(Report?.ResultUrl, UriKind.Absolute, out var resultUri)
                                    && (resultUri.Scheme == Uri.UriSchemeHttp || resultUri.Scheme == Uri.UriSchemeHttps);
                var reservationClosed = Report?.ReservationClosed == true;
                if (serverConfirmed && reservationClosed && hasSummaryUrl)
                {
                    Submitted = true;
                    SubmitMessage = "PIREP enviado y consolidado correctamente.";
                }
                else if (reviewRequired && reservationClosed && hasSummaryUrl)
                {
                    Submitted = true;
                    SubmitMessage = "PIREP recibido. Cierre no evaluable: requiere revision en servidor.";
                }
                else if (serverConfirmed && reservationClosed && !hasSummaryUrl)
                {
                    Submitted = false;
                    SubmitMessage = "Pendiente de sincronizacion. El servidor no confirmo summaryUrl valido para el cierre.";
                    OnPropertyChanged(nameof(CloseButtonTitle));
                    OnPropertyChanged(nameof(CanSubmit));
                    OnPropertyChanged(nameof(IsPendingCloseoutRetry));
                    CommandManager.InvalidateRequerySuggested();
                    return;
                }
                else if (serverConfirmed && !reservationClosed)
                {
                    Submitted = false;
                    SubmitMessage = "Pendiente de sincronizacion. El servidor no confirmo el cierre de la reserva.";
                    OnPropertyChanged(nameof(CloseButtonTitle));
                    OnPropertyChanged(nameof(CanSubmit));
                    OnPropertyChanged(nameof(IsPendingCloseoutRetry));
                    CommandManager.InvalidateRequerySuggested();
                    return;
                }
                else if (isQueued)
                {
                    Submitted = false;
                    SubmitMessage = "PIREP pendiente de sincronizacion. Quedo en cola local y se puede reenviar desde Soporte o desde esta pantalla.";

                    OnPropertyChanged(nameof(CloseButtonTitle));
                    OnPropertyChanged(nameof(CanSubmit));
                    OnPropertyChanged(nameof(IsPendingCloseoutRetry));
                    CommandManager.InvalidateRequerySuggested();

                    return;
                }
                else
                {
                    Submitted = false;
                    SubmitMessage = "Pendiente de sincronizacion. Estado: " + (Report?.ResultStatus ?? "unknown");

                    OnPropertyChanged(nameof(CloseButtonTitle));
                    OnPropertyChanged(nameof(CanSubmit));
                    OnPropertyChanged(nameof(IsPendingCloseoutRetry));
                    CommandManager.InvalidateRequerySuggested();

                    return;
                }

                AcarsContext.FlightService.Reset();
                AcarsContext.Runtime.ClearDispatch();
                AcarsContext.Sound.PlayDing();
                _ = AcarsContext.Sound.PlayGroundArrivedAsync();

                // ── Abrir resumen web tras confirmacion exitosa ──────────────────
                var resultUrl = Report?.ResultUrl;
                if (!string.IsNullOrWhiteSpace(resultUrl))
                {
                    try
                    {
                        SubmitMessage += " Abriendo resumen del vuelo...";
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = resultUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        SubmitMessage += " (No se pudo abrir el navegador: " + ex.Message + ")";
                    }
                }

                CloseoutCompleted?.Invoke();
            }
            catch (Exception ex)
            {
                Submitted = false;
                SubmitMessage = "Error al enviar PIREP: " + ex.Message;
            }
            finally
            {
                IsSubmitting = false;
            }
        }

        /// <summary>
        /// Determina si el servidor confirmó oficialmente el cierre del vuelo.
        /// </summary>
        private static bool IsCloseoutServerConfirmed(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;
            var normalized = status.Trim().ToLowerInvariant();
            return normalized == "completed" 
                || normalized == "scored" 
                || normalized == "approved"
                || normalized == "finalized";
        }

        private static bool IsCloseoutReviewRequired(string? status)
        {
            var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "pending_server_closeout"
                || normalized == "incomplete_closeout"
                || normalized == "no_evaluable"
                || normalized == "manual_review";
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
            return IsCloseoutServerConfirmed(status) || IsCloseoutReviewRequired(status);
        }

        private static bool IsCloseoutPendingRetry(string? status)
        {
            var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == "queued_retry"
                || normalized == "pending"
                || normalized == "pending_sync"
                || normalized == "retry_pending";
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
