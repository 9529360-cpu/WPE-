using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using 币安量化机器人.Models;
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Modules.Trade;

public partial class PositionsOrdersView : UserControl
{
    private readonly ObservableCollection<Position> _positions = new();
    private readonly ObservableCollection<Order> _allOrders = new();
    private readonly ObservableCollection<Order> _filteredOrders = new();

    private readonly TradingAccountManager _accountManager;

    public PositionsOrdersView()
    {
        InitializeComponent();

        // 初始化服务
        DataCacheService cacheService = ServiceLocator.Cache;

        _accountManager = new TradingAccountManager(cacheService);

        // 如果没有账户,创建一个
        if (_accountManager.ActiveAccount == null)
        {
            _accountManager.CreateSimulatedAccount("模拟账户", 10000m);
        }

        // 绑定数据
        PositionsGrid.ItemsSource = _positions;
        OrdersGrid.ItemsSource = _filteredOrders;

        // 加载数据
        LoadData();
    }

    /// <summary>
    /// 加载数据
    /// </summary>
    private void LoadData()
    {
        TradingAccount? account = _accountManager.ActiveAccount;
        if (account == null)
        {
            return;
        }

        // 加载持仓
        _positions.Clear();
        foreach (Position? pos in account.Positions.Where(p => p.Status == PositionStatus.Open))
        {
            _positions.Add(pos);
        }

        // 加载订单
        _allOrders.Clear();
        _filteredOrders.Clear();
        foreach (Order order in account.OrderHistory)
        {
            _allOrders.Add(order);
            _filteredOrders.Add(order);
        }

        // 更新统计
        UpdateStatistics();
    }

    /// <summary>
    /// 更新统计数据
    /// </summary>
    private void UpdateStatistics()
    {
        TradingAccount? account = _accountManager.ActiveAccount;
        if (account == null)
        {
            return;
        }

        // Tab1: 持仓统计
        TotalPositionsText.Text = $"{account.OpenPositionCount} 个";
        TotalPositionValueText.Text = $"{account.PositionValue:N2} USDT";

        double totalUnrealizedPnL = account.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .Sum(p => p.UnrealizedPnL);
        double totalUnrealizedPercent = account.CurrentBalance > 0
            ? (double)totalUnrealizedPnL / (double)account.CurrentBalance
            : 0;

        string sign = totalUnrealizedPnL >= 0 ? "+" : "";
        TotalUnrealizedPnLText.Text = $"{sign}{totalUnrealizedPnL:N2} ({sign}{totalUnrealizedPercent:P2})";
        TotalUnrealizedPnLText.Foreground = totalUnrealizedPnL >= 0
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.Red;

        // Tab3: 交易统计
        TotalTradesCountText.Text = $"{account.TotalTrades} 笔";
        WinRateText.Text = $"{account.WinRate:P0}";
        WinningTradesText.Text = $"{account.WinningTrades} 笔";
        LosingTradesText.Text = $"{account.LosingTrades} 笔";

        // 盈亏比
        var winningPos = account.Positions.Where(p => p.RealizedPnL > 0).ToList();
        var losingPos = account.Positions.Where(p => p.RealizedPnL < 0).ToList();

        double avgWin = winningPos.Any() ? winningPos.Average(p => p.RealizedPnL) : 0;
        double avgLoss = losingPos.Any() ? Math.Abs(losingPos.Average(p => p.RealizedPnL)) : 0;
        double profitFactor = avgLoss > 0 ? avgWin / avgLoss : 0;
        ProfitFactorText.Text = $"{profitFactor:F2}";

        // 详细统计
        var closedPositions = account.Positions.Where(p => p.Status == PositionStatus.Closed).ToList();
        if (closedPositions.Any())
        {
            AvgHoldingTimeText.Text = $"{closedPositions.Average(p => p.HoldingHours):F1} 小时";
            AvgWinText.Text = $"{avgWin:N2} USDT";
            AvgLossText.Text = $"{avgLoss:N2} USDT";

            MaxWinText.Text = $"{closedPositions.Max(p => p.RealizedPnL):N2} USDT";
            MaxLossText.Text = $"{closedPositions.Min(p => p.RealizedPnL):N2} USDT";
        }

        TotalCommissionText.Text = $"{account.OrderHistory.Sum(o => o.Commission):N2} USDT";
    }

    /// <summary>
    /// 刷新数据
    /// </summary>
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadData();
    }

    /// <summary>
    /// 订单状态筛选
    /// </summary>
    private void OrderStatusFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (OrderStatusFilter.SelectedItem is ComboBoxItem item)
        {
            string? filter = item.Content.ToString();

            _filteredOrders.Clear();

            IEnumerable<Order> filtered = filter switch
            {
                "已成交" => _allOrders.Where(o => o.Status == OrderStatus.Filled),
                "部分成交" => _allOrders.Where(o => o.Status == OrderStatus.PartiallyFilled),
                "已取消" => _allOrders.Where(o => o.Status == OrderStatus.Canceled),
                _ => _allOrders
            };

            foreach (Order order in filtered)
            {
                _filteredOrders.Add(order);
            }
        }
    }

    /// <summary>
    /// 平仓 (简化版,不依赖PositionManager)
    /// </summary>
    private void ClosePosition_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is Position position)
        {
            MessageBoxResult result = MessageBox.Show(
                $"确定要平仓 {position.Symbol} {position.Side} {position.Quantity:F4} 吗?\n当前盈亏: {position.UnrealizedPnL:F2} USDT",
                "确认平仓",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                // 更新持仓状态
                position.Status = PositionStatus.Closed;
                position.ClosePrice = position.CurrentPrice;
                position.CloseTime = DateTime.UtcNow;
                position.RealizedPnL = position.UnrealizedPnL;

                MessageBox.Show("平仓成功!", "提示", MessageBoxButton.OK, MessageBoxImage.Information);

                // 刷新数据
                LoadData();
            }
        }
    }
}
