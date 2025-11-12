using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using 币安量化机器人.Models;
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;
using 币安量化机器人.Modules; // 引用 CombinedModule

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
    private readonly ObservableCollection<StrategyItem> _strategiesSim = new();
    private readonly ObservableCollection<StrategyItem> _strategiesLive = new();

    private readonly TradingAccountManager _accountManager;
    private readonly DataCacheService _cacheService;
    private readonly AutoTradingController _autoTrader = ServiceLocator.AutoTrader; // 🆕 一键自动交易控制器
    private bool _isAIRunning = false;

    public UnifiedDashboardView()
    {
        InitializeComponent();
        _cacheService = ServiceLocator.Cache;
        _accountManager = new TradingAccountManager(_cacheService);
        if (_accountManager.SimulatedAccount == null)
        {
            _accountManager.CreateSimulatedAccount("模拟账户", 100m);
        }

        // Only bind if controls exist in XAML (we moved primary UI into CombinedHost)
        if (this.FindName("SignalsListBox") is ListBox signalsList)
        {
            signalsList.ItemsSource = _signals;
        }
        if (this.FindName("PositionsListBox") is ListBox posList)
        {
            posList.ItemsSource = _positions;
        }

        // 订阅实时信号
        SignalBroadcaster.Instance.Subscribe("UnifiedDashboard", OnTradingSignal);

        // 监听就绪状态
        ServiceLocator.SystemReady.ReadyStateChanged += OnReadyStateChanged;
        OnReadyStateChanged();

        LoadRealData();

        _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();

        if (this.FindName("StartAIButton") is Button startBtn)
        {
            startBtn.Click += StartAI_Click;
        }
        if (this.FindName("StopAIButton") is Button stopBtn)
        {
            stopBtn.Click += StopAI_Click;
        }

        var autoToggle = this.FindName("AutoPilotToggle") as CheckBox;
        if (autoToggle != null)
        {
            autoToggle.Checked += (_, __) => ServiceLocator.AutoPilot.Enable();
            autoToggle.Unchecked += (_, __) => ServiceLocator.AutoPilot.Disable();
        }
        var liveToggle = this.FindName("LiveAutoPilotToggle") as CheckBox;
        if (liveToggle != null)
        {
            liveToggle.IsChecked = ServiceLocator.TradingConfig.Autopilot.LiveEnabled;
            liveToggle.Checked += (_, __) => ServiceLocator.AutoPilot.EnableLiveAutopilot = true;
            liveToggle.Unchecked += (_, __) => ServiceLocator.AutoPilot.EnableLiveAutopilot = false;
        }

        // 初始填充最近信号
        foreach (var s in SignalBroadcaster.Instance.GetRecentSignals(10))
        {
            _signals.Insert(0, new SignalItem
            {
                Time = s.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                Symbol = s.Symbol,
                Action = s.Action,
                Reason = s.Reason,
                Confidence = s.Confidence.ToString("P0"),
                Status = s.IsNew ? "新" : "历史"
            });
        }

        this.Unloaded += (_, __) =>
        {
            SignalBroadcaster.Instance.Unsubscribe("UnifiedDashboard");
            ServiceLocator.SystemReady.ReadyStateChanged -= OnReadyStateChanged;
        };

        // 异步加载并挂载综合模块（不阻塞 UI 线程）
        _ = LoadCombinedModuleAsync();
    }

    // 新增：异步初始化 CombinedModule 并挂载到 CombinedHost
    private async Task LoadCombinedModuleAsync()
    {
        try
        {
            var combined = new CombinedModule();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await combined.InitializeAsync(cts.Token).ConfigureAwait(true);
            // ensure UI-thread assignment
            if (this.FindName("CombinedHost") is ContentControl host)
            {
                host.Content = combined.View;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[UnifiedDashboard] CombinedModule 初始化失败");
            // 如果失败，可回退显示 LegacyContent
            if (this.FindName("LegacyContent") is FrameworkElement legacy)
            {
                legacy.Visibility = Visibility.Visible;
            }
        }
    }

    private void OnTradingSignal(TradingSignalEvent s)
    {
        Dispatcher.Invoke(() =>
        {
            _signals.Insert(0, new SignalItem
            {
                Time = s.Timestamp.ToLocalTime().ToString("HH:mm:ss"),
                Symbol = s.Symbol,
                Action = s.Action,
                Reason = s.Reason,
                Confidence = s.Confidence.ToString("P0"),
                Status = s.IsNew ? "新" : "历史"
            });
            while (_signals.Count > 10)
            {
                _signals.RemoveAt(_signals.Count - 1);
            }

            if (this.FindName("TodaySignalsText") is TextBlock todaySignals)
            {
                todaySignals.Text = _signals.Count.ToString();
            }
        });
    }

    private void OnReadyStateChanged()
    {
        Dispatcher.Invoke(() =>
        {
            var ready = ServiceLocator.SystemReady;
            if (this.FindName("SystemStatusText") is TextBlock systemStatus)
            {
                systemStatus.Text = ready.TradingReady ? "系统就绪 (交易)" : ready.DeepSeekReady ? "系统就绪 (AI)" : ready.BinanceReady ? "系统就绪 (交易所)" : "未就绪";
            }
            if (ready.TradingReady && !_isAIRunning)
            {
                // 自动预检 DeepSeek 并可选自动启动（不强制）
                _ = PreflightDeepSeekAsync();
            }
        });
    }

    /// <summary>
    /// 定时更新
    /// </summary>
    private void UpdateTimer_Tick(object? sender, EventArgs e)
    {
        if (this.FindName("CurrentTimeText") is TextBlock currentTime)
        {
            currentTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        RefreshAccountData();
        RefreshPositionsView();
        RefreshRunningStrategies();
        RefreshPortfolioStats();
    }

    private void RefreshPortfolioStats()
    {
        var stats = ServiceLocator.StrategyPortfolio.GetPortfolioStats();
        if (this.FindName("TotalStrategiesText") is TextBlock total)
        {
            total.Text = stats.TotalStrategies.ToString();
        }
        if (this.FindName("RunningStrategiesText") is TextBlock running)
        {
            running.Text = stats.RunningStrategies.ToString();
        }
        if (this.FindName("PortfolioReturnText") is TextBlock portRet)
        {
            portRet.Text = $"{stats.TotalReturn:+0.0%;-0.0%;0.0%}";
        }
        if (this.FindName("PortfolioSharpeText") is TextBlock sharpe)
        {
            sharpe.Text = stats.SharpeRatio.ToString("F2");
        }
        if (this.FindName("BestStrategyText") is TextBlock best)
        {
            best.Text = stats.BestPerformer != null ? $"最佳：{stats.BestPerformer.Name} ({stats.BestPerformer.TotalReturn:+0.0%;-0.0%;0.0%})" : "最佳：-";
        }
        if (this.FindName("WorstStrategyText") is TextBlock worst)
        {
            worst.Text = stats.WorstPerformer != null ? $"最差：{stats.WorstPerformer.Name} ({stats.WorstPerformer.TotalReturn:+0.0%;-0.0%;0.0%})" : "最差：-";
        }
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
            var account = _accountManager.SimulatedAccount;
            if (account != null)
            {
                if (this.FindName("NetValueText") is TextBlock netVal)
                {
                    netVal.Text = $"{account.NetValue:F2} USDT";
                }

                decimal todayPnL = account.TodayPnL;
                if (this.FindName("TodayPnLText") is TextBlock todayPnLText)
                {
                    todayPnLText.Text = $"{todayPnL:+0.00;-0.00;0.00} USDT";
                    todayPnLText.Foreground = todayPnL >= 0
                        ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                        : new SolidColorBrush(Color.FromRgb(239, 68, 68));
                }

                double todayReturn = account.NetValue > 0
                    ? (double)(todayPnL / account.NetValue)
                    : 0;
                if (this.FindName("TodayReturnText") is TextBlock todayReturnText)
                {
                    todayReturnText.Text = $"{todayReturn:+0.00%;-0.00%;0.00%}";
                    todayReturnText.Foreground = todayReturn >= 0
                        ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                        : new SolidColorBrush(Color.FromRgb(239, 68, 68));
                }

                decimal netValueChange = account.TotalPnL;
                double totalReturn = account.InitialBalance > 0
                    ? (double)((account.NetValue - account.InitialBalance) / account.InitialBalance)
                    : 0;
                if (this.FindName("NetValueChangeText") is TextBlock netChange)
                {
                    netChange.Text = $"{netValueChange:+0.00;-0.00;0.00} ({totalReturn:+0.0%;-0.0%;0.0%})";
                    netChange.Foreground = netValueChange >= 0
                        ? new SolidColorBrush(Color.FromRgb(16, 185, 129))
                        : new SolidColorBrush(Color.FromRgb(239, 68, 68));
                }

                // 持仓、信号、胜率
                if (this.FindName("PositionCountText") is TextBlock posCount)
                {
                    posCount.Text = account.Positions.Count(p => p.Status == PositionStatus.Open).ToString(); // TODO: 从持仓管理器获取
                }
                if (this.FindName("SignalExecutionText") is TextBlock sigExec)
                {
                    sigExec.Text = _isAIRunning ? "运行中" : "等待交易";
                }

                if (this.FindName("WinRateText") is TextBlock winRate)
                {
                    winRate.Text = account.TotalTrades > 0
                        ? $"{account.WinRate:P0}"
                        : "0%";
                }
                if (this.FindName("WinLossText") is TextBlock winLoss)
                {
                    winLoss.Text = $"{account.WinningTrades}/{account.TotalTrades} 笔";
                }
            }

            // 🆕 更新系统状态
            if (_isAIRunning)
            {
                if (this.FindName("SystemStatusText") is TextBlock sys)
                {
                    sys.Text = "AI交易运行中";
                }
                if (this.FindName("StatusText") is TextBlock st)
                {
                    st.Text = "AI正在分析并执行交易";
                }
            }
            else
            {
                if (this.FindName("SystemStatusText") is TextBlock sys2)
                {
                    sys2.Text = "系统就绪";
                }
                if (this.FindName("StatusText") is TextBlock st2)
                {
                    st2.Text = "点击 ▶️ 启动AI 开始智能交易";
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[UnifiedDashboard] 刷新账户数据失败");
        }
    }

    private void RefreshPositionsView()
    {
        var account = _accountManager.SimulatedAccount;
        if (account == null)
        {
            return;
        }
        _positions.Clear();
        foreach (var p in account.Positions.Where(p => p.Status == PositionStatus.Open))
        {
            _positions.Add(new PositionItem
            {
                Symbol = p.Symbol,
                EntryPrice = p.EntryPrice,
                CurrentPrice = p.CurrentPrice,
                PnL = p.UnrealizedPnL.ToString("+0.00;-0.00;0.00"),
                PnLPercent = p.PnLPercent.ToString("+0.00%;-0.00%;0.00%")
            });
        }
    }

    private void RefreshRunningStrategies()
    {
        _strategiesSim.Clear();
        _strategiesLive.Clear();
        foreach (var s in ServiceLocator.StrategyPortfolio.GetAllStrategies())
        {
            var item = new StrategyItem
            {
                Id = s.Id,
                Name = s.Name,
                Progress = s.Progress,
                Return = $"{s.TotalReturn:+0.0%;-0.0%;0.0%}",
                Sharpe = s.SharpeRatio.ToString("F2"),
                MaxDrawdown = $"{s.MaxDrawdown:P1}",
                Trades = s.TotalTrades,
                Stage = s.Stage.ToString()
            };
            if (s.AccountType == AccountType.Live)
            {
                _strategiesLive.Add(item);
            }
            else
            {
                _strategiesSim.Add(item);
            }
        }
        if (this.FindName("StrategiesList") is ListView list)
        {
            list.ItemsSource = _strategies;
        }
        if (this.FindName("StrategiesListSim") is ListView simList)
        {
            simList.ItemsSource = _strategiesSim;
        }
        if (this.FindName("StrategiesListLive") is ListView liveList)
        {
            liveList.ItemsSource = _strategiesLive;
        }
    }

    // 🆕 校验API Key
    private bool ValidateKeys(out string error)
    {
        error = string.Empty;
        var (binanceKey, binanceSecret) = ConfigurationService.GetBinanceCredentials();
        string deepSeekKey = ConfigurationService.GetDeepSeekApiKey();
        if (string.IsNullOrWhiteSpace(binanceKey) || string.IsNullOrWhiteSpace(binanceSecret))
        {
            error = "Binance API Key/Secret 未配置\n请在 [🔑 API 管理] 中保存";
            return false;
        }
        if (string.IsNullOrWhiteSpace(deepSeekKey))
        {
            error = "DeepSeek API Key 未配置\n请在 [🔑 API 管理] 中保存";
            return false;
        }
        return true;
    }

    // 🆕 DeepSeek 预检
    private async Task<bool> PreflightDeepSeekAsync()
    {
        try
        {
            string key = ConfigurationService.GetDeepSeekApiKey();
            var agent = new DeepSeekTradingAgent(key);
            await agent.ValidateAccessAsync(CancellationToken.None);
            return true;
        }
        catch (HttpRequestException httpEx)
        {
            string msg = httpEx.Message;
            if (msg.Contains("401"))
            {
                MessageBox.Show("❌ DeepSeek API Key 无效 (401)\n请在 [API 管理] 重新配置", "认证失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else if (msg.Contains("402") || msg.Contains("Payment Required", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("❌ DeepSeek 账户无有效额度 (402)\n请在 DeepSeek 控制台充值或开通额度", "额度不足", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                MessageBox.Show($"❌ DeepSeek 访问失败:\n{httpEx.Message}", "网络错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"❌ DeepSeek 预检失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
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
        if (!ValidateKeys(out string keyError))
        {
            MessageBox.Show(keyError, "缺少配置", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // DeepSeek 预检
        if (!await PreflightDeepSeekAsync())
        {
            return;
        }

        // 使用 FindName 以兼容生成字段问题
        var accountTypeCombo = this.FindName("AccountTypeCombo") as ComboBox;
        var symbolsInput = this.FindName("SymbolsInput") as TextBox;

        // 读取账户类型
        AccountType accountType = AccountType.Simulated;
        if (accountTypeCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag && tag == "Live")
        {
            accountType = AccountType.Live;
        }

        // 读取交易对
        string[] symbols = (symbolsInput?.Text ?? "BTCUSDT")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .DefaultIfEmpty("BTCUSDT").ToArray();

        MessageBoxResult result = MessageBox.Show(
            $"确定要启动AI自动交易吗？\n\n✅ 当前账户: {accountType}\n📈 交易对: {string.Join(", ", symbols)}\n💰 可用资金: {_accountManager.SimulatedAccount?.AvailableBalance:F2} USDT",
            "确认启动", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (this.FindName("StartAIButton") is Button startB)
            {
                startB.IsEnabled = false;
            }
            if (this.FindName("StatusText") is TextBlock status)
            {
                status.Text = "启动中...";
            }
            await _autoTrader.StartAsync(symbols, accountType);
            _isAIRunning = true;
            if (this.FindName("StopAIButton") is Button stopB)
            {
                stopB.IsEnabled = true;
            }
            _signals.Insert(0, new SignalItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Symbol = "系统",
                Action = "启动成功",
                Reason = "AI自动交易已开启",
                Confidence = "-",
                Status = "运行中"
            });
            RefreshAccountData();
            MessageBox.Show("✅ AI自动交易已启动", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[UnifiedDashboard] 启动AI交易失败");
            MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            if (this.FindName("StartAIButton") is Button sb)
            {
                sb.IsEnabled = true;
            }
        }
    }

    /// <summary>
    /// 停止AI交易
    /// </summary>
    private async void StopAI_Click(object sender, RoutedEventArgs e)
    {
        if (!_isAIRunning)
        {
            return;
        }
        MessageBoxResult result = MessageBox.Show("确定要停止AI交易吗？", "确认停止", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            await _autoTrader.StopAsync();
            _isAIRunning = false;
            if (this.FindName("StartAIButton") is Button sb)
            {
                sb.IsEnabled = true;
            }
            if (this.FindName("StopAIButton") is Button stopB2)
            {
                stopB2.IsEnabled = false;
            }
            _signals.Insert(0, new SignalItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Symbol = "系统",
                Action = "已停止",
                Reason = "用户停止AI交易",
                Confidence = "-",
                Status = "已停止"
            });
            RefreshAccountData();
            MessageBox.Show("✅ AI交易已停止", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[UnifiedDashboard] 停止AI交易失败");
            MessageBox.Show($"停止失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Strategy_Start_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            _ = ServiceLocator.StrategyPortfolio.StartStrategyAsync(id);
        }
    }

    private void Strategy_Stop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string id)
        {
            ServiceLocator.StrategyPortfolio.StopStrategy(id);
        }
    }

    private void CreateStrategyButton_Click(object sender, RoutedEventArgs e)
    {
        var name = (this.FindName("NewStrategyName") as TextBox)?.Text ?? string.Empty;
        var type = ((this.FindName("NewStrategyType") as ComboBox)?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Momentum";
        var symbolsText = (this.FindName("NewStrategySymbols") as TextBox)?.Text ?? string.Empty;
        var weightText = (this.FindName("NewStrategyWeight") as TextBox)?.Text ?? "0.25";
        var acctSel = (this.FindName("NewStrategyAccount") as ComboBox)?.SelectedItem as ComboBoxItem;
        var acct = (acctSel?.Tag?.ToString() == "Live") ? AccountType.Live : AccountType.Simulated;
        double.TryParse(weightText, out double weight);
        var symbols = symbolsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var s = ServiceLocator.StrategyPortfolio.CreateStrategy(name, type, symbols, weight);
        s.AccountType = acct;
        ServiceLocator.StrategyPortfolio.SaveToDisk();
        RefreshRunningStrategies();
        RefreshPortfolioStats();
        MessageBox.Show($"策略已创建：{s.Name}", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
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
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double Progress { get; set; }
        public string Return { get; set; } = string.Empty;
        public string Sharpe { get; set; } = string.Empty;
        public string MaxDrawdown { get; set; } = string.Empty;
        public int Trades { get; set; }
        public string Stage { get; set; } = string.Empty;
    }
}
