using System.Collections.ObjectModel;
using System.Windows.Input;
using 币安量化机器人.Models;
using 币安量化机器人.Services;

namespace 币安量化机器人.ViewModels
{
    public class MainWindowViewModel : ObservableObject
    {
        public ObservableCollection<NavMenuItem> NavItems { get; } = new ObservableCollection<NavMenuItem>
        {
            new NavMenuItem { Title = "仪表盘", Tag = "Dashboard" },
            new NavMenuItem { Title = "策略", Tag = "Strategy" },
            new NavMenuItem { Title = "委托", Tag = "Orders" },
            new NavMenuItem { Title = "监控", Tag = "Observability" },
            new NavMenuItem { Title = "风控", Tag = "Risk" },
            new NavMenuItem { Title = "回测", Tag = "Backtest" }
        };

        private readonly IMarketDataService _marketDataService;

        public MainWindowViewModel(IMarketDataService marketDataService)
        {
            _marketDataService = marketDataService;
        }
    }
}
