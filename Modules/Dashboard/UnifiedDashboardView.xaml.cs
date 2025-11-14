using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    public UnifiedDashboardView()
    {
        InitializeComponent();

        if (this.FindName("RunningStrategiesList") is ListView runList)
        {
            runList.ItemsSource = _runningStrategies;
        }
        if (this.FindName("RecentOrdersList") is ListView ordList)
        {
            ordList.ItemsSource = _recentOrders;
        }

        // Subscribe to strategy changes
        ServiceLocator.StrategyPortfolio.StrategiesChanged += () => Dispatcher.Invoke(UpdateRunningStrategies);

        // Periodically refresh recent orders
        _sidebarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _sidebarTimer.Tick += (_, __) => _ = RefreshRecentOrdersAsync();
        _sidebarTimer.Start();

        // Initial fill
        UpdateRunningStrategies();
        _ = RefreshRecentOrdersAsync();

        RefreshFactorStats();
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
}
