using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using 币安量化机器人.Models;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Dashboard;

/// <summary>
/// 统一AI交易仪表盘
/// </summary>
public partial class UnifiedDashboardView : UserControl
{
    private readonly DispatcherTimer _updateTimer;
    private readonly ObservableCollection<SignalItem> _signals = new();
    private readonly ObservableCollection<PositionItem> _positions = new();
    private readonly ObservableCollection<StrategyItem> _strategies = new();

    // 🆕 添加服务引用
    private readonly TradingAccountManager _accountManager;
    private readonly DataCacheService _cacheService;
    private readonly AITradingAutomation? _aiTrading;
    private bool _isAIRunning = false;

    public UnifiedDashboardView()
    {
        InitializeComponent();

        // 🔧 初始化服务
        _cacheService = ServiceLocator.Cache;
        _accountManager = new TradingAccountManager(_cacheService);

        // 确保模拟账户存在，初始资金100 USDT
        if (_accountManager.SimulatedAccount == null)
        {
            _accountManager.CreateSimulatedAccount("模拟账户", 100m);
        }

        // 尝试初始化AI交易
        try
        {
            // AI交易需要更多依赖，暂时简化
            //_aiTrading = new AITradingAutomation(_accountManager, _cacheService);
            LogService.Info("[UnifiedDashboard] AI交易初始化已跳过(需要完整配置)");
        }
        catch (Exception ex)
        {
            LogService.Warning("[UnifiedDashboard] AI交易初始化失败: {Message}", ex.Message);
        }

        // 绑定数据
        SignalsListBox.ItemsSource = _signals;
        PositionsListBox.ItemsSource = _positions;
        StrategiesControl.ItemsSource = _strategies;

        // 🔧 加载真实数据
        LoadRealData();

        // 启动定时更新
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();

        // 绑定按钮事件
        StartAIButton.Click += StartAI_Click;
        StopAIButton.Click += StopAI_Click;
    }

    /// <summary>
    /// 定时更新
    /// </summary>
    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        CurrentTimeText.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // 刷新账户数据
        RefreshAccountData();
    }

    /// <summary>
    /// 加载真实数据
    /// </summary>
    private void LoadRealData()
    {
        try
        {
            RefreshAccountData();

            // 清空示例数据
            _signals.Clear();
            _positions.Clear();
            _strategies.Clear();

            // 添加提示
            _signals.Add(new SignalItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Symbol = "系统",
                Action = "等待中",
                Reason = "点击 ▶️ 启动AI 开始交易",
                Confidence = "-",
                Status = "就绪"
            });

            LogService.Info("[UnifiedDashboard] 已加载真实数据");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[UnifiedDashboard] 加载数据失败");
        }
    }

    /// <summary>
    /// 刷新账户数据
    /// </summary>
    private void RefreshAccountData()
    {
        try
        {
            var simAccount = _accountManager.SimulatedAccount;
            var liveAccount = _accountManager.LiveAccount;

            if (simAccount != null)
            {
                // 🔧 显示真实的模拟账户数据
                NetValueText.Text = $"{simAccount.NetValue:F2} USDT";

                decimal todayPnL = simAccount.TodayPnL;
                TodayPnLText.Text = $"{todayPnL:+0.00;-0.00;0.00} USDT";
                TodayPnLText.Foreground = todayPnL >= 0
                    ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                    : new SolidColorBrush(Color.FromRgb(239, 68, 68));

                double todayReturn = simAccount.NetValue > 0
                    ? (double)(todayPnL / simAccount.NetValue)
                    : 0;
                TodayReturnText.Text = $"{todayReturn:+0.00%;-0.00%;0.00%}";
                TodayReturnText.Foreground = todayReturn >= 0
                    ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                    : new SolidColorBrush(Color.FromRgb(239, 68, 68));

                decimal netValueChange = simAccount.TotalPnL;
                double totalReturn = simAccount.NetValue > 0
                    ? (double)((simAccount.NetValue - simAccount.InitialBalance) / simAccount.InitialBalance)
                    : 0;
                NetValueChangeText.Text = $"{netValueChange:+0.00;-0.00;0.00} ({totalReturn:+0.0%;-0.0%;0.0%})";
                NetValueChangeText.Foreground = netValueChange >= 0
                    ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                    : new SolidColorBrush(Color.FromRgb(239, 68, 68));

                // 持仓、信号、胜率
                PositionCountText.Text = "0"; // TODO: 从持仓管理器获取
                TodaySignalsText.Text = "0"; // TODO: 从信号广播器获取
                SignalExecutionText.Text = "等待交易";

                WinRateText.Text = simAccount.TotalTrades > 0
                    ? $"{simAccount.WinRate:P0}"
                    : "0%";
                WinLossText.Text = $"{simAccount.WinningTrades}/{simAccount.TotalTrades} 笔";
            }

            // 🆕 更新系统状态
            if (_isAIRunning)
            {
                SystemStatusText.Text = "AI交易运行中";
                StatusText.Text = "AI正在分析市场并自动交易...";
            }
            else
            {
                SystemStatusText.Text = "系统就绪";
                StatusText.Text = "点击 ▶️ 启动AI 开始智能交易";
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[UnifiedDashboard] 刷新账户数据失败");
        }
    }

    /// <summary>
    /// 启动AI交易
    /// </summary>
    private async void StartAI_Click(object sender, RoutedEventArgs e)
    {
        if (_isAIRunning)
        {
            MessageBox.Show("AI交易已在运行中", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_aiTrading == null)
        {
            MessageBox.Show(
                "AI交易引擎初始化失败\n\n请检查:\n1. DeepSeek API Key 是否配置\n2. 网络连接是否正常",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            return;
        }

        var result = MessageBox.Show(
            "确定要启动AI自动交易吗？\n\n" +
            "✅ 当前账户: 模拟账户\n" +
            $"💰 可用资金: {_accountManager.SimulatedAccount?.AvailableBalance:F2} USDT\n\n" +
            "AI将自动:\n" +
            "• 分析市场数据\n" +
            "• 生成交易信号\n" +
            "• 自动执行交易\n" +
            "• 实时风险控制",
            "确认启动",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                if (_aiTrading != null)
                {
                    // await _aiTrading.StartAsync(new[] { "BTCUSDT" }, AccountType.Simulated);
                    MessageBox.Show(
                        "AI交易功能正在开发中\n\n" +
                        "当前版本可以:\n" +
                        "• 使用 AI智能助手 进行对话\n" +
                        "• 查看交易信号和绩效\n" +
                        "• 管理策略组合",
                        "开发中",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return;
                }

                _isAIRunning = true;

                StartAIButton.IsEnabled = false;
                StopAIButton.IsEnabled = true;

                // 添加启动信号
                _signals.Insert(0, new SignalItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Symbol = "系统",
                    Action = "启动成功",
                    Reason = "AI交易引擎已启动",
                    Confidence = "100%",
                    Status = "运行中"
                });

                LogService.Info("[UnifiedDashboard] AI交易已启动");
                MessageBox.Show("✅ AI交易已启动！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[UnifiedDashboard] 启动AI交易失败");
                MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// 停止AI交易
    /// </summary>
    private void StopAI_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAIRunning)
        {
            return;
        }

        var result = MessageBox.Show(
            "确定要停止AI交易吗？\n\n当前持仓将保留。",
            "确认停止",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                // _aiTrading?.Stop(); // AITradingAutomation 没有 Stop 方法

                // 简化处理
                _isAIRunning = false;

                StartAIButton.IsEnabled = true;
                StopAIButton.IsEnabled = false;

                // 添加停止信号
                _signals.Insert(0, new SignalItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Symbol = "系统",
                    Action = "已停止",
                    Reason = "用户手动停止AI交易",
                    Confidence = "-",
                    Status = "已停止"
                });

                LogService.Info("[UnifiedDashboard] AI交易已停止");
                MessageBox.Show("✅ AI交易已停止", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[UnifiedDashboard] 停止AI交易失败");
                MessageBox.Show($"停止失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

// 数据模型
public class SignalItem
{
    public string Time { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Confidence { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class PositionItem
{
    public string Symbol { get; set; } = string.Empty;
    public double EntryPrice { get; set; }
    public double CurrentPrice { get; set; }
    public string PnL { get; set; } = string.Empty;
    public string PnLPercent { get; set; } = string.Empty;
}

public class StrategyItem
{
    public string Name { get; set; } = string.Empty;
    public double Progress { get; set; }
    public string Return { get; set; } = string.Empty;
}
