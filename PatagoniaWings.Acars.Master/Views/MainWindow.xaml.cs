#nullable enable
using System;
using System.IO;
using System.Threading;
using System.Windows;
using PatagoniaWings.Acars.Core.Enums;
using PatagoniaWings.Acars.Master.Helpers;
using PatagoniaWings.Acars.Master.ViewModels;

namespace PatagoniaWings.Acars.Master.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;
        private SimulatorCoordinator? _coordinator;
        private Timer? _reconnectTimer;
        private bool _isConnecting;

        private static readonly string LogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PatagoniaWings", "Acars", "logs", "simulator.log");

        public MainWindow()
        {
            InitializeComponent();

            _vm = new MainViewModel();
            _vm.OnLogout = () =>
            {
                var login = new LoginWindow();
                login.Show();
                Close();
            };

            DataContext = _vm;
            _vm.LoadPilot();

            // Versión dinámica en el sidebar (refleja lo que está instalado realmente)
            TxtSidebarVersion.Text = $"ACARS v{UpdateService.CurrentVersion}";

            // Suscribirse a los eventos de progreso de actualización
            UpdateService.DownloadProgressChanged += OnUpdateProgressChanged;
            UpdateService.UpdateStatusChanged     += OnUpdateStatusChanged;

            Loaded += OnWindowLoaded;
            Closed += OnWindowClosed;
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // 3 s de espera: da tiempo a MSFS para limpiar la sesión SimConnect anterior
            // (cierro ACARS → abro ACARS de nuevo → sin colgar el simulador)
            await System.Threading.Tasks.Task.Delay(3000);
            TryConnectQuiet();

            _reconnectTimer = new Timer(_ =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (!AcarsContext.Runtime.IsSimulatorConnected && !_isConnecting)
                    {
                        TryConnectQuiet();
                    }
                });
            }, null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));

            // Si el ACARS acaba de actualizarse, mostrar notificación de éxito
            UpdateService.CheckAndShowPostUpdateNotification();
        }

        // ── Update progress (eventos del UpdateService) ──────────────────────────

        private void OnUpdateProgressChanged(int percent)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateBanner.Visibility   = Visibility.Visible;
                UpdateProgress.Value      = percent;
                TxtUpdatePercent.Text     = $"{percent} %";
            });
        }

        private void OnUpdateStatusChanged(string message)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateBanner.Visibility = Visibility.Visible;
                TxtUpdateStatus.Text    = message;
            });
        }

        // ── SimConnect ───────────────────────────────────────────────────────────

        private void TryConnectQuiet()
        {
            if (_isConnecting) return;

            _isConnecting = true;
            try
            {
                ConnectSimulator(true);
            }
            finally
            {
                _isConnecting = false;
            }
        }

        public void ConnectSimulator(bool silent = false)
        {
            try
            {
                _coordinator?.Dispose();
                _coordinator = new SimulatorCoordinator(LogFile);

                _coordinator.Connected += () => Dispatcher.Invoke(() =>
                {
                    var backend = _coordinator.ActiveBackend;
                    var simType = SimulatorType.MSFS2020;
                    AcarsContext.Runtime.SetSimulatorWaiting(backend, simType);
                });

                _coordinator.Disconnected += () => Dispatcher.Invoke(() =>
                {
                    AcarsContext.Runtime.SetSimulatorDisconnected(_coordinator?.ActiveBackend ?? "");
                });

                _coordinator.DataReceived += data =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        AcarsContext.Runtime.SetTelemetry(data, _coordinator?.ActiveBackend ?? "");
                    });

                    OnSimulatorDataReceived(data);
                };

                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                _coordinator.TryConnect(hwnd);
            }
            catch (Exception ex)
            {
                AcarsContext.Runtime.SetSimulatorDisconnected("");
                if (!silent)
                {
                    MessageBox.Show(
                        "No se pudo conectar con el simulador.\n" +
                        "Se intentó SimConnect primero y luego FSUIPC7.\n" +
                        "Asegúrate de tener MSFS abierto.\n\n" + ex.Message,
                        "Conexión Simulador",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        public void ConnectSim(bool silent = false) => ConnectSimulator(silent);

        // ── Ventana cerrada ──────────────────────────────────────────────────────

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            // Desuscribir eventos de actualización para evitar callbacks tardíos
            UpdateService.DownloadProgressChanged -= OnUpdateProgressChanged;
            UpdateService.UpdateStatusChanged     -= OnUpdateStatusChanged;

            // Detener el timer primero para evitar que dispare mientras cerramos
            _reconnectTimer?.Dispose();
            _reconnectTimer = null;

            // Disponer coordinator (desconecta SimConnect limpiamente antes de que
            // el handle de ventana sea destruido por Windows)
            _coordinator?.Dispose();
            _coordinator = null;
        }

        // ── Handlers de UI ──────────────────────────────────────────────────────

        private void SimStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (AcarsContext.Runtime.IsSimulatorConnected) return;
            ConnectSimulator(false);
        }

        private static void OnSimulatorDataReceived(PatagoniaWings.Acars.Core.Models.SimData data)
        {
            AcarsContext.FlightService.UpdateSimData(data);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) ToggleMaximize();
            else DragMove();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState.Minimized;

        private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
            ToggleMaximize();

        private void BtnClose_Click(object sender, RoutedEventArgs e) =>
            Close();

        private void ToggleMaximize() =>
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
    }
}
