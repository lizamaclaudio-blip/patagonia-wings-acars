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

            VersionLine.Text = string.Format("Version instalada: {0}  ->  nueva version: {1}", checkResult.CurrentVersion, checkResult.LatestVersion);
            SourceLine.Text = "Origen: " + (TryGetHost(checkResult.DownloadUrl) ?? "patagoniaw.com");
            StatusText.Text = "Preparando descarga inmediata...";

            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateService.DownloadProgressChanged += OnDownloadProgressChanged;
            UpdateService.UpdateStatusChanged += OnUpdateStatusChanged;
            UpdateService.UpdateFailed += OnUpdateFailed;

            if (_started)
            {
                return;
            }

            _started = true;
            UpdateService.StartImmediateUpdate(_checkResult.DownloadUrl, _checkResult.LatestVersion);
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            UpdateService.DownloadProgressChanged -= OnDownloadProgressChanged;
            UpdateService.UpdateStatusChanged -= OnUpdateStatusChanged;
            UpdateService.UpdateFailed -= OnUpdateFailed;
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
