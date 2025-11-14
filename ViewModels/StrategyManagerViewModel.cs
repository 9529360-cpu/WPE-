using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using 币安量化机器人.Core;
using 币安量化机器人.Services;

namespace 币安量化机器人.ViewModels
{
    public class StrategyItem
    {
        public string Name { get; set; }
        public string Status { get; set; }
    }

    public class StrategyManagerViewModel : ObservableObject
    {
        private readonly StrategyHost _strategyHost;

        public ObservableCollection<StrategyItem> Strategies { get; } = new ObservableCollection<StrategyItem>();

        public ICommand LoadStrategyCommand { get; }
        public ICommand StartAllCommand { get; }
        public ICommand StopAllCommand { get; }
        public ICommand BacktestCommand { get; }

        public StrategyManagerViewModel()
        {
            _strategyHost = App.ServiceProvider?.GetService(typeof(StrategyHost)) as StrategyHost;

            LoadStrategyCommand = new RelayCommand(async () => await LoadStrategyAsync());
            StartAllCommand = new RelayCommand(async () => await StartAllAsync());
            StopAllCommand = new RelayCommand(async () => await StopAllAsync());
            BacktestCommand = new RelayCommand(async () => await BacktestAsync());

            RefreshStrategyList();
        }

        private void RefreshStrategyList()
        {
            Strategies.Clear();
            var infos = _strategyHost?.GetStrategyInfos();
            if (infos != null)
            {
                foreach (var i in infos)
                {
                    Strategies.Add(new StrategyItem { Name = i.Name, Status = i.Status });
                }
            }
        }

        private async Task LoadStrategyAsync()
        {
            // For now load ExampleStrategy from current assembly
            var example = new Services.ExampleStrategy();
            await _strategyHost.LoadStrategyAsync(example, App.ServiceProvider);
            RefreshStrategyList();
        }

        private async Task StartAllAsync()
        {
            if (_strategyHost != null)
            {
                await _strategyHost.StartAllAsync(System.Threading.CancellationToken.None);
                RefreshStrategyList();
            }
        }

        private async Task StopAllAsync()
        {
            if (_strategyHost != null)
            {
                await _strategyHost.StopAllAsync();
                RefreshStrategyList();
            }
        }

        private Task BacktestAsync()
        {
            // TODO: 启动回测流程（调用 Backtest service）
            return Task.CompletedTask;
        }
    }
}
