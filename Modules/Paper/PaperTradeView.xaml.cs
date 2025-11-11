using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using 币安量化机器人.Models;
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Modules.Paper;

public partial class PaperTradeView : UserControl
{
    private readonly TradingAccountManager _accountManager;
    private readonly AITradingAutomation _aiAutomation;
    private readonly PositionManager _positionManager;
    private DispatcherTimer? _updateTimer;

    private readonly ObservableCollection<Position> _positions = new();
    private readonly ObservableCollection<string> _logs = new();

    public PaperTradeView()
    {
        InitializeComponent();

        // 初始化服务 (直接使用ServiceLocator)
        DataCacheService cacheService = ServiceLocator.Cache;
        BinanceApiClient apiClient = ServiceLocator.Api;
        BinanceStreamClient streamClient = ServiceLocator.Stream;

        _accountManager = new TradingAccountManager(cacheService);

        // 创建模拟账户
        _accountManager.CreateSimulatedAccount("模拟账户", 10000m);

        // 初始化AI组件
        var dataProcessor = new MarketDataPreprocessor(apiClient, cacheService);
        var aiAgent = new DeepSeekTradingAgent("sk-xxx"); // TODO: 从配置读取
        var riskManager = new AIRiskManager();
        var executionEngine = new AIOrderExecutionEngine(
            _accountManager,
            apiClient,
            cacheService,
            riskManager
        );

        _positionManager = new PositionManager(
            _accountManager,
            apiClient,
            executionEngine
        );

        _aiAutomation = new AITradingAutomation(
            streamClient,
            dataProcessor,
            aiAgent,
            executionEngine,
            _accountManager,
            _positionManager
        );

        // 绑定数据
        PositionsGrid.ItemsSource = _positions;
        LogList.ItemsSource = _logs;

        // 启动更新定时器
        StartUpdateTimer();

        // 初始更新
        UpdateUI();

        AddLog("✅ AI模拟交易中心已就绪");
    }

    /// <summary>
    /// 启动更新定时器 (每秒刷新)
    /// </summary>
    private void StartUpdateTimer()
    {
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _updateTimer.Tick += (_, __) => UpdateUI();
        _updateTimer.Start();
    }

    /// <summary>
    /// 更新UI
    /// </summary>
    private void UpdateUI()
    {
        TradingAccount? account = _accountManager.ActiveAccount;
        if (account == null)
        {
            return;
        }

        // 更新账户概览
        NetValueText.Text = $"{account.NetValue:N2} USDT";
        AvailableBalanceText.Text = $"{account.AvailableBalance:N2} USDT";
        PositionValueText.Text = $"{account.PositionValue:N2} USDT";

        // 更新盈亏 (带颜色)
        UpdatePnLText(TodayPnLText, account.TodayPnL, account.TodayReturnPercent);
        UpdatePnLText(TotalPnLText, account.TotalPnL, account.TotalReturnPercent);

        // 更新交易统计
        PositionCountText.Text = $"{account.OpenPositionCount} 个";
        TotalTradesText.Text = $"{account.TotalTrades} 笔";
        WinRateText.Text = $"{account.WinRate:P0}";
        WinningTradesText.Text = $"{account.WinningTrades} 笔";
        LosingTradesText.Text = $"{account.LosingTrades} 笔";

        // 更新持仓列表
        _positions.Clear();
        foreach (Position? pos in account.Positions.Where(p => p.Status == PositionStatus.Open))
        {
            _positions.Add(pos);
        }

        // 更新状态指示
        if (_aiAutomation.IsRunning)
        {
            StatusIndicator.Text = "运行中";
        }
        else
        {
            StatusIndicator.Text = "就绪";
        }
    }

    /// <summary>
    /// 更新盈亏文本和颜色
    /// </summary>
    private void UpdatePnLText(TextBlock textBlock, decimal pnl, double percent)
    {
        string sign = pnl >= 0 ? "+" : "";
        textBlock.Text = $"{sign}{pnl:N2} ({sign}{percent:P2})";
        textBlock.Foreground = pnl >= 0
            ? System.Windows.Media.Brushes.Green
            : System.Windows.Media.Brushes.Red;
    }

    /// <summary>
    /// 添加日志
    /// </summary>
    private void AddLog(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss");
        _logs.Insert(0, $"[{timestamp}] {message}");

        // 只保留最近10条
        while (_logs.Count > 10)
        {
            _logs.RemoveAt(_logs.Count - 1);
        }
    }

    /// <summary>
    /// 启动AI交易
    /// </summary>
    private async void StartAI_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartAIButton.IsEnabled = false;
            StopAIButton.IsEnabled = true;

            // 解析交易对
            string[] symbols = SymbolsInput.Text
                .Split(',')
                .Select(s => s.Trim().ToUpperInvariant())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            if (symbols.Length == 0)
            {
                MessageBox.Show("请输入至少一个交易对", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                StartAIButton.IsEnabled = true;
                StopAIButton.IsEnabled = false;
                return;
            }

            AddLog($"🚀 启动AI自动交易: {string.Join(", ", symbols)}");

            // 启动AI自动交易 (异步)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _aiAutomation.StartAsync(symbols, AccountType.Simulated);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        AddLog($"❌ AI交易异常: {ex.Message}");
                        StartAIButton.IsEnabled = true;
                        StopAIButton.IsEnabled = false;
                    });
                }
            });

            AddLog("✅ AI自动交易已启动");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            StartAIButton.IsEnabled = true;
            StopAIButton.IsEnabled = false;
        }
    }

    /// <summary>
    /// 停止AI交易
    /// </summary>
    private async void StopAI_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AddLog("⏹ 停止AI自动交易...");

            await _aiAutomation.StopAsync();

            StartAIButton.IsEnabled = true;
            StopAIButton.IsEnabled = false;

            AddLog("✅ AI自动交易已停止");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"停止失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// 刷新持仓
    /// </summary>
    private void RefreshPositions_Click(object sender, RoutedEventArgs e)
    {
        UpdateUI();
        AddLog("🔄 持仓已刷新");
    }

    /// <summary>
    /// 平仓
    /// </summary>
    private async void ClosePosition_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is Position position)
        {
            try
            {
                MessageBoxResult result = MessageBox.Show(
                    $"确定要平仓 {position.Symbol} {position.Side} {position.Quantity:F4} 吗?",
                    "确认平仓",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    AddLog($"📤 手动平仓: {position.Symbol} {position.Side} {position.Quantity:F4}");

                    await _positionManager.ClosePositionManuallyAsync(position);

                    AddLog($"✅ 平仓成功: {position.Symbol}");
                    UpdateUI();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"平仓失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                AddLog($"❌ 平仓失败: {ex.Message}");
            }
        }
    }
}
