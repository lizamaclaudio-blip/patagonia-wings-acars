using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using PatagoniaWings.Acars.Master.Helpers;

namespace PatagoniaWings.Acars.Master.Views
{
    public partial class UpdateWindow : Window
    {
        private readonly UpdateService.UpdateCheckResult _checkResult;
        private bool _started;
        private readonly string _manualDownloadUrl;

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
            SourceLine.Visibility = Visibility.Collapsed;
            StatusText.Text = checkResult.SupportsDifferential
                ? "Preparando actualizacion diferencial..."
                : "Preparando descarga inmediata...";
            _manualDownloadUrl = checkResult.DownloadUrl ?? string.Empty;

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
                RetryButton.Visibility = Visibility.Visible;
                ManualButton.Visibility = string.IsNullOrWhiteSpace(_manualDownloadUrl) ? Visibility.Collapsed : Visibility.Visible;
                LogsButton.Visibility = Visibility.Visible;
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

        private void RetryButton_Click(object sender, RoutedEventArgs e)
        {
            ContinueButton.Visibility = Visibility.Collapsed;
            RetryButton.Visibility = Visibility.Collapsed;
            ManualButton.Visibility = Visibility.Collapsed;
            LogsButton.Visibility = Visibility.Collapsed;
            StatusText.Text = "Reintentando descarga del instalador...";
            UpdateService.StartImmediateUpdate(_checkResult);
        }

        private void ManualButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_manualDownloadUrl))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _manualDownloadUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusText.Text = "No se pudo abrir la descarga manual: " + ex.Message;
            }
        }

        private void LogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var logsPath = UpdateService.LogsDirectory;
                Directory.CreateDirectory(logsPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = logsPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusText.Text = "No se pudo abrir carpeta de logs: " + ex.Message;
            }
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
