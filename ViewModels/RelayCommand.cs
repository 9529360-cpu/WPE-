using System;
using System.Windows.Input;

namespace 币安量化机器人.ViewModels
{
    public class RelayCommand<T> : ICommand
    {
        private readonly Func<T, System.Threading.Tasks.Task> _execute;
        private readonly Predicate<T> _canExecute;

        public RelayCommand(Func<T, System.Threading.Tasks.Task> execute, Predicate<T> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute((T)parameter);

        public event EventHandler CanExecuteChanged;

        public async void Execute(object parameter)
        {
            await _execute((T)parameter);
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Func<System.Threading.Tasks.Task> _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Func<System.Threading.Tasks.Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();

        public event EventHandler CanExecuteChanged;

        public async void Execute(object parameter)
        {
            await _execute();
        }
    }
}
