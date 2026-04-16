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

            Loaded += OnWindowLoaded;
            Closed += OnWindowClosed;
        }

        private async void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            await System.Threading.Tasks.Task.Delay(1500);
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
        }

        private void TryConnectQuiet()
        {
            if (_isConnecting)
            {
                return;
            }

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

        public void ConnectSim(bool silent = false)
        {
            ConnectSimulator(silent);
        }

        private void OnWindowClosed(object? sender, EventArgs e)
        {
            _reconnectTimer?.Dispose();
            _coordinator?.Dispose();
        }

        private void SimStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (AcarsContext.Runtime.IsSimulatorConnected)
            {
                return;
            }

            ConnectSimulator(false);
        }

        private static void OnSimulatorDataReceived(PatagoniaWings.Acars.Core.Models.SimData data)
        {
            AcarsContext.FlightService.UpdateSimData(data);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }
}
