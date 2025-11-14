using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using 币安量化机器人.Models;
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Modules.Dashboard;

public partial class UnifiedDashboardView : UserControl
{
    private readonly ObservableCollection<RunningStrategyItem> _runningStrategies = new();
    private readonly ObservableCollection<OrderItem> _recentOrders = new();
    private readonly DispatcherTimer _sidebarTimer;
    private readonly DispatcherTimer _clockTimer;

    public UnifiedDashboardView()
    {
        InitializeComponent();

        if (this.FindName("RunningStrategiesList") is ListView runList)
        {
            runList.ItemsSource = _runningStrategies;
        }
        if (this.FindName("HistoryOrdersList") is ListView ordList)
        {
            ordList.ItemsSource = _recentOrders;
        }

        // Bind AI start/stop buttons if present
        if (FindName("StartAIButton") is Button sb)
        {
            sb.IsEnabled = true;
        }
        if (FindName("StopAIButton") is Button stb)
        {
            stb.IsEnabled = false;
        }

        // Subscribe to strategy changes
        ServiceLocator.StrategyPortfolio.StrategiesChanged += () => Dispatcher.Invoke(UpdateRunningStrategies);

        // Periodically refresh recent orders
        _sidebarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _sidebarTimer.Tick += (_, __) => _ = RefreshRecentOrdersAsync();
        _sidebarTimer.Start();

        // Live clock
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, __) => UpdateClock();
        _clockTimer.Start();

        // Initial fill
        UpdateRunningStrategies();
        _ = RefreshRecentOrdersAsync();

        RefreshFactorStats();

        // Wire up SymbolsInput change to validate
        if (FindName("SymbolsInput") is TextBox tb)
        {
            tb.TextChanged += SymbolsInput_TextChanged;
        }

        // subscribe to Unloaded to cleanup
        this.Unloaded += OnViewUnloaded;

        // subscribe to runtime log buffer
        Services.Runtime.InMemoryLogBuffer.LogAppended += OnLogAppended;

        // Initial validation
        ValidateSymbolsInput();
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        if (FindName("CurrentTimeText") is TextBlock tb)
        {
            tb.Text = now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    private void SymbolsInput_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ValidateSymbolsInput();
    }

    private void ValidateSymbolsInput()
    {
        var startBtn = FindName("StartAIButton") as Button;
        var symbolsText = string.Empty;
        if (FindName("SymbolsInput") is TextBox tb)
        {
            symbolsText = tb.Text ?? string.Empty;
        }

        // basic validation: non-empty, alphanumeric + commas
        bool valid = false;
        if (!string.IsNullOrWhiteSpace(symbolsText))
        {
            var parts = symbolsText.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());
            valid = parts.All(p => p.Length >= 3 && p.All(c => char.IsLetterOrDigit(c)));
        }

        if (startBtn != null)
        {
            startBtn.IsEnabled = valid;
        }
    }

    private void OnLogAppended(string message)
    {
        // append to RuntimeLogList on UI thread
        Dispatcher.Invoke(() =>
        {
            if (FindName("RuntimeLogList") is ListBox lb)
            {
                lb.Items.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
                while (lb.Items.Count > 200)
                {
                    lb.Items.RemoveAt(lb.Items.Count - 1);
                }
            }
        });
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Services.Runtime.InMemoryLogBuffer.Clear();
            if (FindName("RuntimeLogList") is ListBox lb)
            {
                lb.Items.Clear();
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "清空运行日志失败");
        }
    }

    private void OnViewUnloaded(object? sender, RoutedEventArgs e)
    {
        _sidebarTimer.Stop();
        _clockTimer.Stop();
        if (FindName("SymbolsInput") is TextBox tb)
        {
            tb.TextChanged -= SymbolsInput_TextChanged;
        }
        this.Unloaded -= OnViewUnloaded;
        Services.Runtime.InMemoryLogBuffer.LogAppended -= OnLogAppended;
    }

    private void UpdateRunningStrategies()
    {
        try
        {
            var running = ServiceLocator.StrategyPortfolio.GetAllStrategies().Where(s => s.IsRunning).ToList();
            _runningStrategies.Clear();
            foreach (var s in running)
            {
                var symbols = s.Symbols ?? Enumerable.Empty<string>();
                _runningStrategies.Add(new RunningStrategyItem { Id = s.Id, Name = s.Name, SymbolsText = string.Join(',', symbols) });
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[UnifiedDashboard] 更新运行中策略失败");
        }
    }

    private async Task RefreshRecentOrdersAsync()
    {
        try
        {
            // load orders on background thread, then update ObservableCollection on UI thread
            var orders = await Task.Run(() => ServiceLocator.Cache.LoadOrdersAsync(limit: 20)); // recent orders
            Dispatcher.Invoke(() =>
            {
                _recentOrders.Clear();
                foreach (var o in orders.Take(20))
                {
                    _recentOrders.Add(new OrderItem { Symbol = o.Symbol, ShortInfo = $"{o.Side} {o.Quantity} @{o.AvgFillPrice}", TimeText = o.UpdatedAt.ToLocalTime().ToString("HH:mm:ss") });
                }
            });
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[UnifiedDashboard] 刷新最近订单失败");
        }
    }

    private async void ExportTraining_Click(object sender, RoutedEventArgs e)
    {
        var exportBtn = this.FindName("ExportTrainingButton") as Button;
        var statusText = this.FindName("TrainingStatusText") as TextBlock;
        try
        {
            if (exportBtn != null)
            {
                exportBtn.IsEnabled = false;
            }
            if (statusText != null)
            {
                statusText.Text = "导出中...";
            }

            var lm = ServiceLocator.LearningModule;
            if (lm == null)
            {
                if (statusText != null)
                {
                    statusText.Text = "LearningModule 未就绪";
                }
                return;
            }

            // run export on background thread
            string folder = string.Empty;
            try
            {
                folder = await Task.Run(() => lm.ExportTrainingDataAsync());
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "导出训练数据失败(后台任务)");
                Dispatcher.Invoke(() =>
                {
                    if (statusText != null)
                    {
                        statusText.Text = "导出失败";
                    }
                });
                return;
            }

            if (statusText != null)
            {
                Dispatcher.Invoke(() => statusText.Text = $"导出完成: {folder}");
            }
        }
        catch (Exception ex)
        {
            if (statusText != null)
            {
                Dispatcher.Invoke(() => statusText.Text = "导出失败");
            }
            LogService.Error(ex, "导出训练数据失败");
        }
        finally
        {
            if (exportBtn != null)
            {
                exportBtn.IsEnabled = true;
            }
        }
    }

    private async void StartTraining_Click(object sender, RoutedEventArgs e)
    {
        var startBtn = this.FindName("StartTrainingButton") as Button;
        var statusText = this.FindName("TrainingStatusText") as TextBlock;
        try
        {
            // Confirm action (avoid accidental training)
            var confirm = MessageBox.Show("开始训练会使用导出的历史数据并生成模型，是否继续？", "确认训练", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            if (startBtn != null)
            {
                startBtn.IsEnabled = false;
            }
            if (statusText != null)
            {
                statusText.Text = "训练中...";
            }

            var lm = ServiceLocator.LearningModule;
            if (lm == null)
            {
                if (statusText != null)
                {
                    statusText.Text = "LearningModule 未就绪";
                }
                return;
            }

            using var cts = new System.Threading.CancellationTokenSource();

            string folder = string.Empty;
            string model = string.Empty;

            try
            {
                // run export + training on background thread to keep UI responsive
                folder = await Task.Run(() => lm.ExportTrainingDataAsync());
                model = await Task.Run(() => lm.StartTrainingAsync(folder, cts.Token));
            }
            catch (OperationCanceledException)
            {
                if (statusText != null)
                {
                    Dispatcher.Invoke(() => statusText.Text = "训练已取消");
                }

                return;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "训练失败(后台任务)");
                if (statusText != null)
                {
                    Dispatcher.Invoke(() => statusText.Text = "训练失败");
                }

                return;
            }

            if (statusText != null)
            {
                Dispatcher.Invoke(() => statusText.Text = $"训练完成，模型: {model}");
            }
            MessageBox.Show("训练已完成。模型路径已生成。", "训练完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            if (statusText != null)
            {
                Dispatcher.Invoke(() => statusText.Text = "训练失败");
            }
            LogService.Error(ex, "训练失败");
        }
        finally
        {
            if (startBtn != null)
            {
                startBtn.IsEnabled = true;
            }
        }
    }

    private async void SaveLearningState_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var btn = this.FindName("SaveLearningStateButton") as Button;
            var status = this.FindName("TrainingStatusText") as TextBlock;
            if (btn != null)
            {
                btn.IsEnabled = false;
            }
            if (status != null)
            {
                status.Text = "保存学习状态...";
            }

            var lm = ServiceLocator.LearningModule;
            if (lm == null)
            {
                if (status != null)
                {
                    status.Text = "LearningModule 未就绪";
                }
                return;
            }

            try
            {
                await Task.Run(() => lm.SaveStateAsync());
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "保存学习状态失败(后台任务)");
                Dispatcher.Invoke(() =>
                {
                    if (status != null)
                    {
                        status.Text = "保存失败";
                    }
                });
                return;
            }

            if (status != null)
            {
                Dispatcher.Invoke(() => status.Text = "已保存学习状态");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "保存学习状态失败");
            var status = this.FindName("TrainingStatusText") as TextBlock;
            if (status != null)
            {
                Dispatcher.Invoke(() => status.Text = "保存失败");
            }
        }
        finally
        {
            var btn = this.FindName("SaveLearningStateButton") as Button;
            if (btn != null)
            {
                btn.IsEnabled = true;
            }
        }
    }

    private async void LoadLearningState_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var btn = this.FindName("LoadLearningStateButton") as Button;
            var status = this.FindName("TrainingStatusText") as TextBlock;
            if (btn != null)
            {
                btn.IsEnabled = false;
            }
            if (status != null)
            {
                status.Text = "加载学习状态...";
            }

            var lm = ServiceLocator.LearningModule;
            if (lm == null)
            {
                if (status != null)
                {
                    status.Text = "LearningModule 未就绪";
                }
                return;
            }

            try
            {
                await Task.Run(() => lm.LoadStateAsync());
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "加载学习状态失败(后台任务)");
                Dispatcher.Invoke(() =>
                {
                    if (status != null)
                    {
                        status.Text = "加载失败";
                    }
                });
                return;
            }

            if (status != null)
            {
                Dispatcher.Invoke(() => status.Text = "加载完成");
            }

            // RefreshFactorStats touches UI; ensure it runs on UI thread
            Dispatcher.Invoke(RefreshFactorStats);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "加载学习状态失败");
            var status = this.FindName("TrainingStatusText") as TextBlock;
            if (status != null)
            {
                Dispatcher.Invoke(() => status.Text = "加载失败");
            }
        }
        finally
        {
            var btn = this.FindName("LoadLearningStateButton") as Button;
            if (btn != null)
            {
                btn.IsEnabled = true;
            }
        }
    }

    private void ResetLearning_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = MessageBox.Show("确认重置学习状态？此操作不可逆。", "确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            var lm = ServiceLocator.LearningModule;
            if (lm == null)
            {
                var status = this.FindName("TrainingStatusText") as TextBlock;
                if (status != null)
                {
                    status.Text = "LearningModule 未就绪";
                }
                return;
            }

            lm.ResetFactorLearning();
            var status2 = this.FindName("TrainingStatusText") as TextBlock;
            if (status2 != null)
            {
                status2.Text = "学习状态已重置";
            }

            RefreshFactorStats();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "重置学习状态失败");
        }
    }

    private void RefreshFactorStats()
    {
        try
        {
            var lm = ServiceLocator.LearningModule;
            if (lm == null)
            {
                return;
            }
            var stats = lm.GetFactorPerformanceStats();
            var list = this.FindName("FactorStatsList") as ListView;
            if (list == null)
            {
                return;
            }
            list.Items.Clear();
            foreach (var kv in stats)
            {
                list.Items.Add(new
                {
                    FactorCode = kv.Key,
                    TotalSamples = kv.Value.TotalSamples,
                    WinRateText = (kv.Value.WinRate).ToString("P1"),
                    AvgProfitText = kv.Value.AvgProfit.ToString("F4"),
                    CorrelationText = kv.Value.Correlation.ToString("F2")
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "刷新因子统计失败");
        }
    }

    public class RunningStrategyItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SymbolsText { get; set; } = string.Empty;
    }

    public class OrderItem
    {
        public string Symbol { get; set; } = string.Empty;
        public string ShortInfo { get; set; } = string.Empty;
        public string TimeText { get; set; } = string.Empty;
    }

    private async void StartAI_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var startBtn = FindName("StartAIButton") as Button;
            var stopBtn = FindName("StopAIButton") as Button;
            var progressBar = FindName("StartupProgressBar") as ProgressBar;
            var statusText = FindName("StartupStatusText") as TextBlock;

            if (startBtn != null)
            {
                startBtn.IsEnabled = false;
            }
            if (stopBtn != null)
            {
                stopBtn.IsEnabled = true;
            }

            // parse symbols from SymbolsInput
            string symbolsText = string.Empty;
            if (FindName("SymbolsInput") is TextBox tb)
            {
                symbolsText = tb.Text ?? string.Empty;
            }
            string[] symbols = symbolsText.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToUpperInvariant()).ToArray();
            if (symbols.Length == 0)
            {
                symbols = new[] { "BTCUSDT" };
            }

            // Use progress reporting and batched startup sequence to avoid UI freeze
            var progress = new Progress<int>(p =>
            {
                if (progressBar != null)
                {
                    progressBar.Value = p;
                }
                if (statusText != null)
                {
                    statusText.Text = $"启动中：{p}%";
                }
            });

            CancellationTokenSource cts = new();

            // Run startup sequence on background thread
            _ = Task.Run(async () =>
            {
                try
                {
                    // Steps: 1) Initialize data pipeline 2) Initialize Market Subscriptions 3) Start AutoTrader modules 4) Warm caches
                    var steps = new List<Func<Task>>
                    {
                        async () => { await Task.Run(() => ServiceLocator.Cache.InitializeAsync().GetAwaiter().GetResult()); },
                        async () => { /* subscribe market streams if needed */ await Task.Delay(300); },
                        async () => { await ServiceLocator.AutoTrader.StartAsync(symbols, RuntimeState.CurrentAccountType); },
                        async () => { await Task.Delay(200); /* warm caches */ }
                    };

                    int done = 0;
                    int total = steps.Count;
                    int batchSize = 1; // keep small batches to give UI time

                    for (int i = 0; i < total; i += batchSize)
                    {
                        var batch = steps.Skip(i).Take(batchSize).ToArray();
                        var tasks = batch.Select(f => f());
                        await Task.WhenAll(tasks);
                        done += batch.Length;
                        int percent = (int)((done / (double)total) * 100);
                        (progress as IProgress<int>)?.Report(percent);
                        await Task.Delay(100); // give UI thread a moment
                        if (cts.IsCancellationRequested) break;
                    }

                    // final 100%
                    (progress as IProgress<int>)?.Report(100);
                    await Task.Delay(200);
                    Dispatcher.Invoke(() =>
                    {
                        if (statusText != null)
                        {
                            statusText.Text = "已启动";
                        }
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"AI 启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                        if (startBtn != null)
                        {
                            startBtn.IsEnabled = true;
                        }
                        if (stopBtn != null)
                        {
                            stopBtn.IsEnabled = false;
                        }
                    });
                    LogService.Error(ex, "[UnifiedDashboard] 一键启动失败");
                }
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void StopAI_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var startBtn = FindName("StartAIButton") as Button;
            var stopBtn = FindName("StopAIButton") as Button;
            await ServiceLocator.AutoTrader.StopAsync();
            if (startBtn != null)
            {
                startBtn.IsEnabled = true;
            }
            if (stopBtn != null)
            {
                stopBtn.IsEnabled = false;
            }

            var progressBar = FindName("StartupProgressBar") as ProgressBar;
            var statusText = FindName("StartupStatusText") as TextBlock;
            if (progressBar != null)
            {
                progressBar.Value = 0;
            }
            if (statusText != null)
            {
                statusText.Text = "已停止";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"停止失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
