using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using 币安量化机器人.Models;
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;
using 币安量化机器人.Modules;

namespace 币安量化机器人.Modules.AI
{
    public partial class ModelHub : UserControl, IModuleLifecycle
    {
        private readonly ObservableCollection<ModelRow> _all = new();
        private readonly ICollectionView _view;
        private readonly AiForecastService _aiService = ServiceLocator.Ai;

        // 🆕 DeepSeek AI组件
        private AITradingBot? _aiBot;
        private CancellationTokenSource? _analyzeCts;
        private readonly ObservableCollection<AISignalRow> _aiSignals = new();

        // 🆕 一键自动交易
        private readonly AutoTradingController _autoTrader = ServiceLocator.AutoTrader;

        public ModelHub()
        {
            InitializeComponent();

            foreach (ModelArtifact artifact in _aiService.Models)
            {
                _all.Add(new ModelRow
                {
                    Name = artifact.Name,
                    Version = Version.Parse(artifact.Version).Major,
                    Stage = artifact.Stage,
                    Metric = artifact.Metric,
                    UpdatedAt = artifact.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
                    Description = artifact.Description
                });
            }

            _view = CollectionViewSource.GetDefaultView(_all);
            GridModels.ItemsSource = _view;

            // 🆕 绑定AI信号列表
            AISignalsGrid.ItemsSource = _aiSignals;

            GridModels.SelectionChanged += (_, __) => UpdateDetail();
            if (_all.Any())
            {
                GridModels.SelectedIndex = 0;
            }

            // 如果配置里已存在 API Key，优先显示（避免掩码导致输入错误）
            var deepSeekKey = ConfigurationService.GetDeepSeekApiKey();
            if (!string.IsNullOrWhiteSpace(deepSeekKey))
            {
                DeepSeekApiKeyBox.Text = deepSeekKey;
            }

            StatusText.Text = $"状态：已加载 {_all.Count} 个上线模型 + DeepSeek AI交易系统";
        }

        public Task StartAsync()
        {
            // no-op for now, resources are created on demand in Analyze/StartAIBot
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            try
            {
                // Cancel any in-flight single-shot analysis
                _analyzeCts?.Cancel();
                _analyzeCts?.Dispose();
                _analyzeCts = null;

                // If a single AI bot instance is running, request stop
                try
                {
                    if (_aiBot != null)
                    {
                        if (_aiBot.IsRunning)
                        {
                            // AITradingBot exposes Stop() synchronous method
                            _aiBot.Stop();
                        }

                        _aiBot = null;
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "[ModelHub] 停止 AITradingBot 时发生异常");
                }

                // If auto-trader is running, await its StopAsync to ensure clean shutdown
                if (_autoTrader.IsRunning)
                {
                    try
                    {
                        await _autoTrader.StopAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogService.Error(ex, "[ModelHub] 停止 AutoTradingController 失败");
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[ModelHub] StopAsync 失败");
            }
        }

        private void UpdateDetail()
        {
            if (GridModels.SelectedItem is ModelRow r)
            {
                DetailTitle.Text = $"{r.Name} v{r.Version} · {r.Stage}";
                DetailInfo.Text = $"主指标：{r.Metric}\n更新时间：{r.UpdatedAt}\n\n{r.Description}";
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string kw = SearchBox.Text?.Trim() ?? "";
            _view.Filter = o =>
            {
                if (o is not ModelRow r)
                {
                    return false;
                }

                return string.IsNullOrEmpty(kw)
                       || r.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                       || r.Stage.Contains(kw, StringComparison.OrdinalIgnoreCase);
            };
            _view.Refresh();
        }

        private void Train_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "状态：训练由外部流水线负责，请在 CICD 中触发";
            MessageBox.Show("模型训练由离线管道执行，请在 MLFlow/CI 中触发任务。", "训练任务", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("模型注册由自动化部署完成，此处仅展示线上模型状态。", "模型注册", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PromoteStaging_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("上线流程由 Release 系统控制，此处仅同步展示模型阶段。", "模型阶段", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PromoteProd_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("请在部署中心执行 Production 切换。", "模型上线", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Rollback_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("回滚操作请在部署流水线执行。", "回滚", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CreateAB_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("A/B 实验占位：后续接入流量分配与对照指标。", "A/B 实验", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void CreateCanary_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("金丝雀占位：小流量灰度，指标稳定再放量。", "金丝雀", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void CreateShadow_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Shadow 占位：新模型仅收请求不出结果，用于对齐与观测。", "Shadow", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 🆕 DeepSeek AI功能

        private async void AnalyzeWithAI_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string apiKey = GetDeepSeekApiKeyFromInputOrConfig();
                if (string.IsNullOrEmpty(apiKey))
                {
                    MessageBox.Show("请在右侧输入框或[设置>API管理]中保存 DeepSeek API Key", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string symbol = string.IsNullOrWhiteSpace(AISymbolBox.Text) ? "BTCUSDT" : AISymbolBox.Text.Trim();
                AIStatusText.Text = $"正在分析 {symbol}...";
                AnalyzeButton.IsEnabled = false;

                _analyzeCts = new CancellationTokenSource();
                _aiBot = ServiceLocator.GetAITradingBot(apiKey);

                AITradingSignal signal = await _aiBot.AnalyzeOnceAsync(symbol, _analyzeCts.Token);

                // 显示结果
                _aiSignals.Insert(0, new AISignalRow
                {
                    Timestamp = signal.Timestamp.ToString("HH:mm:ss"),
                    Symbol = signal.Symbol,
                    Action = signal.Action.ToString(),
                    Confidence = $"{signal.Confidence:P0}",
                    EntryPrice = $"{signal.EntryPrice:F4}",
                    TargetPrice = $"{signal.TargetPrice:F4}",
                    StopLoss = $"{signal.StopLoss:F4}",
                    Reason = signal.Reason
                });

                while (_aiSignals.Count > 20)
                {
                    _aiSignals.RemoveAt(_aiSignals.Count - 1);
                }

                AIStatusText.Text = $"{symbol} 分析完成: {signal.Action} (信心度: {signal.Confidence:P0})";

                MessageBox.Show(
                    $"交易信号: {signal.Action}\n" +
                    $"信心度: {signal.Confidence:P0}\n" +
                    $"入场价: {signal.EntryPrice:F4}\n" +
                    $"目标价: {signal.TargetPrice:F4}\n" +
                    $"止损价: {signal.StopLoss:F4}\n" +
                    $"建议仓位: {signal.PositionSize:P0}\n" +
                    $"时间框架: {signal.Timeframe}\n" +
                    $"风险等级: {signal.RiskLevel}\n\n" +
                    $"分析理由:\n{signal.Reason}",
                    "AI分析结果",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                AIStatusText.Text = $"分析失败: {ex.Message}";
                MessageBox.Show($"分析失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzeButton.IsEnabled = true;
                _analyzeCts?.Dispose();
                _analyzeCts = null;
            }
        }

        private async void StartAIBot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string apiKey = GetDeepSeekApiKeyFromInputOrConfig();
                if (string.IsNullOrEmpty(apiKey))
                {
                    MessageBox.Show("请在右侧输入框或[设置>API管理]中保存 DeepSeek API Key", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string symbol = string.IsNullOrWhiteSpace(AISymbolBox.Text) ? "BTCUSDT" : AISymbolBox.Text.Trim();

                if (_autoTrader.IsRunning)
                {
                    MessageBox.Show("AI自动交易已在运行中", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                AIStatusText.Text = "AI自动交易启动中...";
                StartBotButton.IsEnabled = false;
                StopBotButton.IsEnabled = true;

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _autoTrader.StartAsync(new[] { symbol }, AccountType.Simulated);
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AIStatusText.Text = $"启动失败: {ex.Message}";
                            StartBotButton.IsEnabled = true;
                            StopBotButton.IsEnabled = false;
                        });
                    }
                });

                AIStatusText.Text = $"AI自动交易运行中 ({symbol})";
            }
            catch (Exception ex)
            {
                AIStatusText.Text = $"启动失败: {ex.Message}";
                MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                StartBotButton.IsEnabled = true;
                StopBotButton.IsEnabled = false;
            }
        }

        private void StopAIBot_Click(object sender, RoutedEventArgs e)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _autoTrader.StopAsync();
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show($"停止失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                finally
                {
                    Dispatcher.Invoke(() =>
                    {
                        AIStatusText.Text = "AI自动交易已停止";
                        StartBotButton.IsEnabled = true;
                        StopBotButton.IsEnabled = false;
                    });
                }
            });
        }

        private void ViewAIPerformance_Click(object sender, RoutedEventArgs e)
        {
            if (_aiBot == null)
            {
                MessageBox.Show("请先运行AI分析", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AIPerformanceTracker tracker = _aiBot.PerformanceTracker;
            MessageBox.Show(
                $"AI绩效统计:\n\n" +
                $"总信号数: {tracker.TotalSignals}\n" +
                $"执行交易数: {tracker.ExecutedTrades}\n" +
                $"胜率: {tracker.WinRate:P0}\n" +
                $"平均信心度: {tracker.AverageConfidence:P0}",
                "AI绩效",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }

        private static string GetDeepSeekApiKeyFromInputOrConfig()
        {
            try
            {
                // 优先读取输入框，如包含掩码符号则读取配置
                // 实际读取在调用处通过 DeepSeekApiKeyBox.Text 传入，这里通过配置服务兜底
                string key = ConfigurationService.GetDeepSeekApiKey();
                return key;
            }
            catch
            {
                return string.Empty;
            }
        }
    }

    public class ModelRow : INotifyPropertyChanged
    {
        private string _stage = "Draft";

        public string Name { get; set; } = "";
        public int Version { get; set; }
        public string Stage
        {
            get => _stage;
            set { _stage = value; OnPropertyChanged(nameof(Stage)); }
        }
        public string Metric { get; set; } = "N/A";
        public string UpdatedAt { get; set; } = "";
        public string Description { get; set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // 🆕 AI信号显示行
    public class AISignalRow
    {
        public string Timestamp { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Confidence { get; set; } = string.Empty;
        public string EntryPrice { get; set; } = string.Empty;
        public string TargetPrice { get; set; } = string.Empty;
        public string StopLoss { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }
}
