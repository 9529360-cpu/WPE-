using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Strategy;

/// <summary>
/// 多策略组合管理视图
/// </summary>
public partial class StrategyPortfolioView : UserControl
{
    private readonly StrategyPortfolioManager _portfolioManager;
    private readonly TradingAccountManager _accountManager;
    private readonly ObservableCollection<StrategyRow> _strategies = new();
    private readonly DataCacheService _cacheService;

    public StrategyPortfolioView()
    {
        InitializeComponent();

        // 初始化服务
        _cacheService = ServiceLocator.Cache;
        _accountManager = new TradingAccountManager(_cacheService);
        _portfolioManager = new StrategyPortfolioManager(_accountManager);

        StrategiesGrid.ItemsSource = _strategies;

        // 加载示例策略
        LoadSampleStrategies();
        UpdateUI();
    }

    /// <summary>
    /// 加载示例策略
    /// </summary>
    private void LoadSampleStrategies()
    {
        try
        {
            // 添加示例策略
            StrategyInstance[] strategies = new[]
            {
                new StrategyInstance
                {
                    Name = "均值回归策略",
                    Type = "MeanReversion",
                    Description = "基于价格偏离均线的回归策略",
                    Weight = 0.3,
                    IsRunning = false,
                    TotalReturn = 0.15m,
                    SharpeRatio = 1.8,
                    MaxDrawdown = -0.08,
                    TotalTrades = 45,
                    WinningTrades = 28,
                    Symbols = new() { "BTCUSDT", "ETHUSDT" }
                },
                new StrategyInstance
                {
                    Name = "动量突破策略",
                    Type = "Momentum",
                    Description = "追踪强势突破趋势",
                    Weight = 0.25,
                    IsRunning = false,
                    TotalReturn = 0.22m,
                    SharpeRatio = 2.1,
                    MaxDrawdown = -0.12,
                    TotalTrades = 32,
                    WinningTrades = 20,
                    Symbols = new() { "BTCUSDT", "BNBUSDT" }
                },
                new StrategyInstance
                {
                    Name = "网格交易策略",
                    Type = "Grid",
                    Description = "震荡市场网格套利",
                    Weight = 0.25,
                    IsRunning = false,
                    TotalReturn = 0.08m,
                    SharpeRatio = 1.2,
                    MaxDrawdown = -0.05,
                    TotalTrades = 68,
                    WinningTrades = 45,
                    Symbols = new() { "ETHUSDT" }
                },
                new StrategyInstance
                {
                    Name = "AI智能策略",
                    Type = "AI",
                    Description = "DeepSeek AI驱动的智能交易",
                    Weight = 0.2,
                    IsRunning = false,
                    TotalReturn = 0.28m,
                    SharpeRatio = 2.5,
                    MaxDrawdown = -0.10,
                    TotalTrades = 38,
                    WinningTrades = 26,
                    Symbols = new() { "BTCUSDT", "ETHUSDT", "SOLUSDT" }
                }
            };

            foreach (StrategyInstance? strategy in strategies)
            {
                _portfolioManager.AddStrategy(strategy);
            }

            LogService.Info("[StrategyPortfolioView] 已加载 {Count} 个示例策略", strategies.Length);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[StrategyPortfolioView] 加载示例策略失败");
        }
    }

    /// <summary>
    /// 更新UI
    /// </summary>
    private void UpdateUI()
    {
        try
        {
            // 更新策略列表
            _strategies.Clear();
            IReadOnlyList<StrategyInstance> allStrategies = _portfolioManager.GetAllStrategies();

            foreach (StrategyInstance strategy in allStrategies)
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
                    RunningTime = strategy.RunningTime
                });
            }

            // 更新组合统计
            PortfolioStats stats = _portfolioManager.GetPortfolioStats();

            PortfolioReturnText.Text = $"{stats.TotalReturn:P2}";
            PortfolioReturnText.Foreground = stats.TotalReturn >= 0
                ? new SolidColorBrush(Color.FromRgb(5, 150, 105))
                : new SolidColorBrush(Color.FromRgb(239, 68, 68));

            PortfolioSharpeText.Text = $"{stats.SharpeRatio:F2}";
            RunningCountText.Text = $"{stats.RunningStrategies}/{stats.TotalStrategies}";
            StrategyCountText.Text = $"({stats.TotalStrategies} 个策略)";

            // 更新最佳/最差表现
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

    /// <summary>
    /// 添加策略
    /// </summary>
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 打开添加策略对话框
        MessageBox.Show("添加策略功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// 平衡权重
    /// </summary>
    private void Balance_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult result = MessageBox.Show(
            "确定要自动平衡所有策略的权重吗？\n\n每个策略将获得相等的资金分配。",
            "确认",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result == MessageBoxResult.Yes)
        {
            _portfolioManager.AutoBalanceWeights();
            UpdateUI();
            MessageBox.Show("权重已自动平衡！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// 刷新
    /// </summary>
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        UpdateUI();
    }

    /// <summary>
    /// 启动策略
    /// </summary>
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string strategyId)
        {
            try
            {
                await _portfolioManager.StartStrategyAsync(strategyId);
                UpdateUI();

                StrategyRow? strategy = _strategies.FirstOrDefault(s => s.Id == strategyId);
                MessageBox.Show($"策略 '{strategy?.Name}' 已启动！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// 停止策略
    /// </summary>
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string strategyId)
        {
            try
            {
                _portfolioManager.StopStrategy(strategyId);
                UpdateUI();

                StrategyRow? strategy = _strategies.FirstOrDefault(s => s.Id == strategyId);
                MessageBox.Show($"策略 '{strategy?.Name}' 已停止！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"停止失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// 编辑策略
    /// </summary>
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        // TODO: 打开编辑对话框
        MessageBox.Show("编辑策略功能开发中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// 删除策略
    /// </summary>
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string strategyId)
        {
            StrategyRow? strategy = _strategies.FirstOrDefault(s => s.Id == strategyId);
            if (strategy == null)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                $"确定要删除策略 '{strategy.Name}' 吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                _portfolioManager.RemoveStrategy(strategyId);
                UpdateUI();
            }
        }
    }
}

/// <summary>
/// 策略行数据
/// </summary>
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

    // UI 绑定属性
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
