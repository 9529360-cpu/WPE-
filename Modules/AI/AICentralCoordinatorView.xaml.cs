using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using 币安量化机器人.Modules;
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Modules.AI
{
    /// <summary>
    /// 中央AI协调器视图
    /// </summary>
    public partial class AICentralCoordinatorView : UserControl, IModuleLifecycle
    {
        private readonly AICentralCoordinator _coordinator;
        private readonly ObservableCollection<string> _logMessages = new();
        private readonly DispatcherTimer _refreshTimer;
        private readonly List<IDisposable> _subscriptions = new();

        public AICentralCoordinatorView()
        {
            InitializeComponent();

            // 从 ServiceLocator 获取协调器
            _coordinator = ServiceLocator.GetAICentralCoordinator();

            LogListBox.ItemsSource = _logMessages;

            // 设置定时器（每2秒刷新一次），不在构造时启动
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;

            // 不在构造中订阅事件或启动定时器，改为显式 StartAsync

            // 初始化UI（初始状态）
            RefreshUI();

            AddLog("✅ [AICentralCoordinatorView] UI初始化完成（等待启动）");
        }

        private void SubscribeToEvents()
        {
            EventBus eventBus = _coordinator.EventBus;

            // 阶段切换事件
            var sub1 = eventBus.Subscribe<StageTransitionEvent>(async evt =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    AddLog($"🔄 [{DateTime.Now:HH:mm:ss}] 阶段切换: {evt.FromStage.GetDisplayName()} → {evt.ToStage.GetDisplayName()}");
                    RefreshUI();
                });
            });
            _subscriptions.Add(sub1);

            // 风险警报事件
            var sub2 = eventBus.Subscribe<RiskAlertEvent>(async evt =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    string icon = evt.Severity switch
                    {
                        RiskSeverity.Critical => "🚨",
                        RiskSeverity.High => "⚠️",
                        RiskSeverity.Medium => "⚡",
                        _ => "ℹ️"
                    };
                    AddLog($"{icon} [{DateTime.Now:HH:mm:ss}] 风险警报 ({evt.Severity}): {evt.Message}");
                });
            });
            _subscriptions.Add(sub2);

            // 回测完成事件
            var sub3 = eventBus.Subscribe<BacktestCompletedEvent>(async evt =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    AddLog($"📊 [{DateTime.Now:HH:mm:ss}] 回测完成: {evt.StrategyName}");
                    AddLog($"   总收益: {evt.TotalReturn:P2} | 夏普: {evt.SharpeRatio:F2} | 回撤: {evt.MaxDrawdown:P2} | 胜率: {evt.WinRate:P2}");
                });
            });
            _subscriptions.Add(sub3);

            // 模拟交易更新事件
            var sub4 = eventBus.Subscribe<SimulationUpdateEvent>(async evt =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    AddLog($"🎮 [{DateTime.Now:HH:mm:ss}] 模拟交易更新: 余额={evt.CurrentBalance:N2}, 盈利={evt.ProfitPercent:P2}, 胜率={evt.WinRate:P2}");
                });
            });
            _subscriptions.Add(sub4);

            // 实盘交易事件
            var sub5 = eventBus.Subscribe<LiveTradeEvent>(async evt =>
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    AddLog($"💰 [{DateTime.Now:HH:mm:ss}] 实盘交易: {evt.Symbol} {evt.Action} @ {evt.Price:F4} × {evt.Quantity:F4}");
                });
            });
            _subscriptions.Add(sub5);
        }

        private void UnsubscribeAll()
        {
            foreach (var s in _subscriptions)
            {
                try
                {
                    s.Dispose();
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "[AICentralCoordinatorView] 取消订阅失败");
                }
            }
            _subscriptions.Clear();
        }

        private void RefreshTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                RefreshUI();  // 使用现有的RefreshUI方法
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[AICentralCoordinatorView] 刷新失败");
            }
        }

        private void RefreshUI()
        {
            if (_coordinator == null)
            {
                return;
            }

            try
            {
                SystemState state = _coordinator.CurrentState;
                WorkflowStage stage = _coordinator.CurrentStage;

                // 工作流阶段
                StageIcon.Text = stage.GetIcon();
                StageName.Text = stage.GetDisplayName();
                StageDescription.Text = stage.GetDescription();

                // 运行状态
                if (_coordinator.IsRunning)
                {
                    RunningStatusText.Text = "运行中";
                    RunningStatusText.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // 绿色
                }
                else
                {
                    RunningStatusText.Text = "已停止";
                    RunningStatusText.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60)); // 红色
                }

                // 按钮状态
                StartButton.IsEnabled = !_coordinator.IsRunning;
                StopButton.IsEnabled = _coordinator.IsRunning;

                // 市场状况
                VolatilityText.Text = $"{state.MarketCondition.Volatility:P2}";
                TrendText.Text = state.MarketCondition.TrendDirection.ToString();
                LiquidityText.Text = $"{state.MarketCondition.Liquidity:P2}";
                MarketStateText.Text = state.MarketCondition.IsStable ? "✅ 平稳" : "⚠️ 波动";

                // 账户状态
                AccountTypeText.Text = state.AccountStatus.Type.ToString();
                NetValueText.Text = $"{state.AccountStatus.NetValue:N2} USDT";

                TodayPnLText.Text = $"{state.AccountStatus.TodayPnL:+N2;-N2} USDT ({state.AccountStatus.TodayReturnPercent:+P2;-P2})";
                TodayPnLText.Foreground = state.AccountStatus.TodayPnL >= 0
                    ? new SolidColorBrush(Color.FromRgb(39, 174, 96))  // 绿色
                    : new SolidColorBrush(Color.FromRgb(231, 76, 60));  // 红色

                TotalPnLText.Text = $"{state.AccountStatus.TotalPnL:+N2;-N2} USDT ({state.AccountStatus.TotalReturnPercent:+P2;-P2})";
                TotalPnLText.Foreground = state.AccountStatus.TotalPnL >= 0
                    ? new SolidColorBrush(Color.FromRgb(39, 174, 96))
                    : new SolidColorBrush(Color.FromRgb(231, 76, 60));

                // 风险指标
                MaxDrawdownText.Text = $"{state.RiskMetrics.MaxDrawdown:P2}";
                CurrentDrawdownText.Text = $"{state.RiskMetrics.CurrentDrawdown:P2}";
                DailyLossText.Text = $"{state.RiskMetrics.DailyLoss:N2} USDT";
                LeverageText.Text = $"{state.RiskMetrics.Leverage:F1}x";
                SafetyText.Text = state.RiskMetrics.IsSafe ? "✅ 安全" : "🚨 警报";
                SafetyText.Foreground = state.RiskMetrics.IsSafe
                    ? new SolidColorBrush(Color.FromRgb(39, 174, 96))
                    : new SolidColorBrush(Color.FromRgb(231, 76, 60));

                // 系统资源
                CpuUsageText.Text = $"{state.SystemResources.CpuUsage:P1}";
                MemoryUsageText.Text = $"{state.SystemResources.MemoryUsage:P1}";
                NetworkLatencyText.Text = $"{state.SystemResources.NetworkLatency:F0}ms";
                ActiveTasksText.Text = state.SystemResources.ActiveTasks.ToString();
                HealthStatusText.Text = state.SystemResources.IsHealthy ? "✅ 健康" : "⚠️ 异常";
                HealthStatusText.Foreground = state.SystemResources.IsHealthy
                    ? new SolidColorBrush(Color.FromRgb(39, 174, 96))
                    : new SolidColorBrush(Color.FromRgb(231, 76, 60));
            }
            catch (Exception ex)
            {
                AddLog($"❌ 刷新UI失败: {ex.Message}");
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StartButton.IsEnabled = false;
                AddLog("🚀 正在启动中央AI协调器...");

                await Task.Run(async () => await _coordinator.StartAsync());

                AddLog("✅ 中央AI协调器已启动");
                RefreshUI();
            }
            catch (Exception ex)
            {
                AddLog($"❌ 启动失败: {ex.Message}");
                StartButton.IsEnabled = true;
            }
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                StopButton.IsEnabled = false;
                AddLog("🛑 正在停止中央AI协调器...");

                await Task.Run(async () => await _coordinator.StopAsync());

                AddLog("✅ 中央AI协调器已停止");
                RefreshUI();
            }
            catch (Exception ex)
            {
                AddLog($"❌ 停止失败: {ex.Message}");
                StopButton.IsEnabled = true;
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            AddLog("🔄 手动刷新状态...");
            RefreshUI();
        }

        private void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            _logMessages.Clear();
            AddLog("🗑️ 日志已清空");
        }

        private void AddLog(string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _logMessages.Insert(0, message);

                // 保持日志数量在合理范围
                while (_logMessages.Count > 200)
                {
                    _logMessages.RemoveAt(_logMessages.Count - 1);
                }
            });
        }

        // IModuleLifecycle: explicit start/stop to control subscriptions and timer
        public async Task StartAsync()
        {
            // 订阅事件并启动定时器
            try
            {
                SubscribeToEvents();
                _refreshTimer.Start();
                AddLog("🟢 中央AI协调器视图已启动（事件订阅 & 定时器）");

                // 小延迟给UI刷新
                await Task.Delay(50);
                RefreshUI();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[AICentralCoordinatorView] StartAsync 失败");
            }
        }

        public async Task StopAsync()
        {
            try
            {
                _refreshTimer.Stop();
                UnsubscribeAll();
                AddLog("🔴 中央AI协调器视图已停止（事件取消订阅 & 定时器停止）");

                await Task.Delay(10);
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[AICentralCoordinatorView] StopAsync 失败");
            }
        }
    }
}
