using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using 币安量化机器人.Services;

namespace 币安量化机器人.ViewModels
{
    public class RiskManagerViewModel : ObservableObject
    {
        private readonly RiskManager _riskManager;

        public double MaxOrderQuantity
        {
            get => _riskManager.MaxOrderQuantity;
            set { _riskManager.MaxOrderQuantity = value; RaisePropertyChanged(); }
        }

        public double MaxPositionSize
        {
            get => _riskManager.MaxPositionSize;
            set { _riskManager.MaxPositionSize = value; RaisePropertyChanged(); }
        }

        public double DailyLossLimit
        {
            get => _riskManager.DailyLossLimit;
            set { _riskManager.DailyLossLimit = value; RaisePropertyChanged(); }
        }

        public ObservableCollection<string> Breaches { get; } = new ObservableCollection<string>();

        public ICommand SaveCommand { get; }
        public ICommand RefreshCommand { get; }

        public RiskManagerViewModel()
        {
            _riskManager = App.ServiceProvider?.GetService(typeof(RiskManager)) as RiskManager ?? new RiskManager();
            SaveCommand = new RelayCommand(async () => await SaveAsync());
            RefreshCommand = new RelayCommand(async () => await RefreshAsync());
        }

        private Task SaveAsync()
        {
            // TODO: 持久化风控规则
            return Task.CompletedTask;
        }

        private Task RefreshAsync()
        {
            // TODO: 刷新违规列表
            Breaches.Clear();
            return Task.CompletedTask;
        }
    }
}
