using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using 币安量化机器人.Services;
using 币安量化机器人.Models;

namespace 币安量化机器人.Modules.Strategy;

public partial class StrategyPortfolioView : UserControl
{
    private readonly StrategyPortfolioManager _portfolioManager;
    private readonly TradingAccountManager _account_manager;
    private readonly ObservableCollection<StrategyRow> _strategies = new();
    private readonly DataCacheService _cacheService;

    // 新增：仅显示合约策略开关（可与XAML绑定）
    private bool _futuresOnly = true;

    public StrategyPortfolioView()
    {
        InitializeComponent();

        _cacheService = ServiceLocator.Cache;
        _account_manager = new TradingAccountManager(_cacheService);
        _portfolioManager = ServiceLocator.StrategyPortfolio; // 使用全局实例，包含磁盘数据

        StrategiesGrid.ItemsSource = _strategies;

        // 订阅策略变化事件，自动刷新
        _portfolioManager.StrategiesChanged += () => Dispatcher.Invoke(UpdateUI);

        // 订阅全局账户类型变更（来自 RuntimeState）
        RuntimeState.OnAccountTypeChanged += (acct) => Dispatcher.Invoke(() => AccountModeComboBox.SelectedIndex = acct == AccountType.Live ? 1 : 0);

        // 初始加载
        _portfolioManager.LoadFromDisk();
        UpdateUI();

        // 初始化 AccountModeComboBox 默认值
        if (AccountModeComboBox != null)
        {
            AccountModeComboBox.SelectedIndex = RuntimeState.CurrentAccountType == AccountType.Live ? 1 : 0;
        }
    }

    private void AccountModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountModeComboBox == null)
        {
            return;
        }
        var idx = AccountModeComboBox.SelectedIndex;
        RuntimeState.CurrentAccountType = idx == 1 ? AccountType.Live : AccountType.Simulated;
    }

    private void FuturesOnlyCheck_Checked(object sender, RoutedEventArgs e)
    {
        _futuresOnly = true;
        UpdateUI();
    }

    private void FuturesOnlyCheck_Unchecked(object sender, RoutedEventArgs e)
    {
        _futuresOnly = false;
        UpdateUI();
    }

    private void UpdateUI()
    {
        try
        {
            _strategies.Clear();
            var all = _portfolioManager.GetAllStrategies();
            var filtered = _futuresOnly ? all.Where(s => s.Market == MarketType.Futures) : all;

            foreach (var strategy in filtered)
            {
                _strategies.Add(new StrategyRow
                {
                    Id = strategy.Id,
                    Name = strategy.Name,
                    Type = strategy.Type,
                    Weight = strategy.Weight,
                    IsRunning = strategy.IsRunning,
                    TotalReturn = strategy.TotalReturn,
                    SharpeRatio = strategy.SharpeRatio,
                    TotalTrades = strategy.TotalTrades,
                    WinningTrades = strategy.WinningTrades,
                    WinRate = strategy.WinRate,
                    RunningTime = strategy.RunningTime,
                    Market = strategy.Market,
                    Stage = strategy.Stage,
                    AuditSource = strategy.Audit?.Source ?? string.Empty,
                    AuditTimestampUtc = strategy.Audit?.CreatedUtc
                });
            }

            var stats = _portfolioManager.GetPortfolioStats();
            PortfolioReturnText.Text = $"{stats.TotalReturn:P2}";
            PortfolioReturnText.Foreground = stats.TotalReturn >= 0
                ? new SolidColorBrush(Color.FromRgb(5, 150, 105))
                : new SolidColorBrush(Color.FromRgb(239, 68, 68));

            PortfolioSharpeText.Text = $"{stats.SharpeRatio:F2}";
            RunningCountText.Text = $"{stats.RunningStrategies}/{stats.TotalStrategies}";
            StrategyCountText.Text = $"({stats.TotalStrategies} 个策略)";

            if (stats.BestPerformer != null)
            {
                BestPerformerName.Text = stats.BestPerformer.Name;
                BestPerformerReturn.Text = $"{stats.BestPerformer.TotalReturn:P2}";
                BestPerformerSharpe.Text = $"{stats.BestPerformer.SharpeRatio:F2}";
            }

            if (stats.WorstPerformer != null)
            {
                WorstPerformerName.Text = stats.WorstPerformer.Name;
                WorstPerformerReturn.Text = $"{stats.WorstPerformer.TotalReturn:P2}";
                WorstPerformerSharpe.Text = $"{stats.WorstPerformer.SharpeRatio:F2}";
            }

            StatusText.Text = $"组合管理 | 总收益: {stats.TotalReturn:P2} | 夏普: {stats.SharpeRatio:F2}";
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[StrategyPortfolioView] 更新UI失败");
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("添加策略功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Balance_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "确定要自动平衡所有策略的权重吗？\n\n每个策略将获得相等的资金分配。",
            "确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _portfolioManager.AutoBalanceWeights();
            UpdateUI();
            MessageBox.Show("权重已自动平衡！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _portfolioManager.LoadFromDisk();
        UpdateUI();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string strategyId)
        {
            try
            {
                // 使用 UI 上选择的账户模式作为运行时选择
                var selectedMode = AccountModeComboBox != null && AccountModeComboBox.SelectedIndex == 1 ? AccountType.Live : AccountType.Simulated;

                if (selectedMode == AccountType.Live)
                {
                    var confirm = MessageBox.Show("你选择了实盘模式，真实下单可能造成资金损失。确认要以实盘模式启动此策略吗？", "确认实盘启动", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (confirm != MessageBoxResult.Yes)
                    {
                        return;
                    }
                }

                // 切换到用户选择的账户类型（不改变策略的存储 AccountType 字段）
                RuntimeState.CurrentAccountType = selectedMode;
                var acctType = selectedMode;

                // 启动策略（传入 runAs 以确保实际执行所用账户与 UI 选择一致）
                await _portfolioManager.StartStrategyAsync(strategyId, StrategyStage.LiveRunning, acctType);

                // 如果执行器依赖于 ActiveAccount，请确保切换账户
                try
                {
                    if (acctType == AccountType.Live)
                    {
                        _account_manager.SwitchToLive();
                    }
                    else
                    {
                        _account_manager.SwitchToSimulated();
                    }
                }
                catch (Exception exSwitch)
                {
                    LogService.Warning("切换账户失败: {Message}", exSwitch.Message);
                }

                UpdateUI();

                var strategy = _strategies.FirstOrDefault(s => s.Id == strategyId);
                MessageBox.Show($"策略 '{strategy?.Name}' 已启动（模式: {acctType}）！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string strategyId)
        {
            try
            {
                _portfolioManager.StopStrategy(strategyId);
                UpdateUI();

                var strategy = _strategies.FirstOrDefault(s => s.Id == strategyId);
                MessageBox.Show($"策略 '{strategy?.Name}' 已停止！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"停止失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("编辑策略功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string strategyId)
        {
            var strategy = _strategies.FirstOrDefault(s => s.Id == strategyId);
            if (strategy == null)
            {
                return;
            }

            var result = MessageBox.Show(
                $"确定要删除策略 '{strategy.Name}' 吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _portfolioManager.RemoveStrategy(strategyId);
                UpdateUI();
            }
        }
    }

    private async void ContextStart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is string id)
        {
            try
            {
                var acct = RuntimeState.CurrentAccountType;
                var confirm = MessageBoxResult.Yes;
                if (acct == AccountType.Live)
                {
                    confirm = MessageBox.Show("你选择了实盘模式，真实下单可能造成资金损失。确认要以实盘模式启动此策略吗？", "确认实盘启动", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                }
                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }

                await _portfolioManager.StartStrategyAsync(id, StrategyStage.LiveRunning, acct);
                UpdateUI();
                MessageBox.Show("策略已启动", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void ContextStop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is string id)
        {
            ServiceLocator.StrategyPortfolio.StopStrategy(id);
            UpdateUI();
            MessageBox.Show("策略已停止", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ContextEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is string id)
        {
            // reuse Edit_Click flow
            Edit_Click(sender, new RoutedEventArgs());
        }
    }

    private void ContextDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.CommandParameter is string id)
        {
            var row = _strategies.FirstOrDefault(s => s.Id == id);
            if (row == null)
            {
                return;
            }
            var result = MessageBox.Show($"确定要删除策略 '{row.Name}' 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                _portfolioManager.RemoveStrategy(id);
                UpdateUI();
            }
        }
    }
}

public class StrategyRow
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public double Weight { get; set; }
    public bool IsRunning { get; set; }
    public decimal TotalReturn { get; set; }
    public double SharpeRatio { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public double WinRate { get; set; }
    public TimeSpan RunningTime { get; set; }

    // 新增显示字段
    public MarketType Market { get; set; }
    public StrategyStage Stage { get; set; }
    public string AuditSource { get; set; } = string.Empty;
    public DateTime? AuditTimestampUtc { get; set; }

    public string StatusText => IsRunning ? "运行中" : "已停止";
    public Brush StatusColor => IsRunning
        ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
        : new SolidColorBrush(Color.FromRgb(107, 114, 128));

    public string WeightText => $"{Weight:P0}";
    public double WeightPercent => Weight * 100;

    public string ReturnText => $"{TotalReturn:P2}";
    public string SharpeText => $"{SharpeRatio:F2}";
    public string TradesText => $"{TotalTrades} 笔";
    public string WinRateText => $"{WinRate:P0}";
    public string RunningTimeText => RunningTime.TotalHours >= 1
        ? $"{RunningTime.TotalHours:F1}h"
        : $"{RunningTime.TotalMinutes:F0}m";
}
