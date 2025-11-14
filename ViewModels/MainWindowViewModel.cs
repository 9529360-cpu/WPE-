using System.Collections.ObjectModel;
using 币安量化机器人.Models;
using 币安量化机器人.Services;
using System;
using System.Windows.Input;

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

        public string MarketStatus { get; private set; }
        public DateTime LastTick { get; private set; }

        public MainWindowViewModel(IMarketDataService marketDataService, Services.IEventBus eventBus)
        {
            _marketDataService = marketDataService;
            eventBus.Subscribe<Core.MarketDataRawMessage>(m =>
            {
                LastTick = m.ReceivedAt.ToLocalTime();
                MarketStatus = _marketDataService.IsConnected ? "已连接" : "已断开";
                RaisePropertyChanged(nameof(LastTick));
                RaisePropertyChanged(nameof(MarketStatus));

                // 记录到 Observability
                Services.Observability.ObservabilityService.Instance.AddLog($"接收行情: {m.ReceivedAt:HH:mm:ss}");
            });
        }
    }
}
