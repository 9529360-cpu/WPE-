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

            // 占位：添加示例策略条目
            Strategies.Add(new StrategyItem { Name = "ExampleStrategy", Status = "Stopped" });
        }

        private Task LoadStrategyAsync()
        {
            // TODO: 实现文件选择并调用 StrategyHost.LoadStrategyFromAssemblyAsync
            return Task.CompletedTask;
        }

        private async Task StartAllAsync()
        {
            if (_strategyHost != null)
            {
                await _strategyHost.StartAllAsync(System.Threading.CancellationToken.None);
                foreach (var s in Strategies) s.Status = "Running";
                RaisePropertyChanged(nameof(Strategies));
            }
        }

        private async Task StopAllAsync()
        {
            if (_strategyHost != null)
            {
                await _strategyHost.StopAllAsync();
                foreach (var s in Strategies) s.Status = "Stopped";
                RaisePropertyChanged(nameof(Strategies));
            }
        }

        private Task BacktestAsync()
        {
            // TODO: 启动回测流程（调用 Backtest 服务或模块）
            return Task.CompletedTask;
        }
    }
}
