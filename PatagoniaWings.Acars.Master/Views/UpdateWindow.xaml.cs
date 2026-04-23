using System;
using System.Windows;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.Views
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateService.UpdateCheckResult _checkResult;
        private bool _started;

        public UpdateWindow(UpdateService.UpdateCheckResult checkResult)
        {
            _checkResult = checkResult;
            InitializeComponent();

            VersionLine.Text = string.Format(
                "Version instalada: {0} ({1})  ->  nueva version: {2} ({3})",
                checkResult.CurrentVersion,
                string.IsNullOrWhiteSpace(checkResult.CurrentRevision) ? "rev local" : checkResult.CurrentRevision,
                checkResult.LatestVersion,
                string.IsNullOrWhiteSpace(checkResult.LatestRevision) ? "rev remota" : checkResult.LatestRevision);
            SourceLine.Text = "Origen: " + (TryGetHost(checkResult.ManifestUrl) ?? TryGetHost(checkResult.DownloadUrl) ?? "patagoniaw.com");
            StatusText.Text = checkResult.SupportsDifferential
                ? "Preparando actualizacion diferencial..."
                : "Preparando descarga inmediata...";

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateService.DownloadProgressChanged += OnDownloadProgressChanged;
            UpdateService.UpdateStatusChanged += OnUpdateStatusChanged;
            UpdateService.UpdateFailed += OnUpdateFailed;
            UpdateService.UpdateCompleted += OnUpdateCompleted;

            if (_started)
            {
                return;
            }

            _started = true;
            UpdateService.StartImmediateUpdate(_checkResult);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            UpdateService.DownloadProgressChanged -= OnDownloadProgressChanged;
            UpdateService.UpdateStatusChanged -= OnUpdateStatusChanged;
            UpdateService.UpdateFailed -= OnUpdateFailed;
            UpdateService.UpdateCompleted -= OnUpdateCompleted;
        }

        private void OnDownloadProgressChanged(int value)
        {
            Dispatcher.Invoke(() => ProgressBar.Value = Math.Max(0, Math.Min(100, value)));
        }

        private void OnUpdateStatusChanged(string message)
        {
            Dispatcher.Invoke(() => StatusText.Text = message);
        }

        private void OnUpdateFailed(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "No se pudo completar la actualizacion. " + message;
                ContinueButton.Visibility = Visibility.Visible;
            });
        }

        private void OnUpdateCompleted(bool restartRequired)
        {
            if (restartRequired)
            {
                return;
            }

            Dispatcher.InvokeAsync(async () =>
            {
                StatusText.Text = "Actualizacion aplicada. Continuando al ACARS...";
                ContinueButton.Visibility = Visibility.Collapsed;
                await System.Threading.Tasks.Task.Delay(600);
                Close();
            });
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private static string? TryGetHost(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }

            return null;
        }
    }
}
