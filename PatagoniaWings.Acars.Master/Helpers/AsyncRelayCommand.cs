using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>
    /// ICommand asíncrono seguro para WPF. Evita async-void y maneja excepciones sin colgar el UI.
    /// </summary>
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _executeAsync;
        private readonly Func<object?, bool>? _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
            _canExecute = canExecute;
        }

        public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
            : this(_ => executeAsync(), canExecute == null ? null : _ => canExecute()) { }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) =>
            !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

        public async void Execute(object? parameter)
        {
            if (_isExecuting) return;
            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await _executeAsync(parameter).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                        MessageBox.Show(
                            "Error inesperado: " + ex.Message,
                            "Patagonia Wings ACARS",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error));
                }
                catch { /* nunca crashear */ }
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
