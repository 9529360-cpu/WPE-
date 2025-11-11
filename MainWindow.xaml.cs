using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace 币安量化机器人
{
    public partial class MainWindow : Window
    {
        // Tag -> 模块 UserControl 的"类型全名, 程序集名"
        private readonly Dictionary<string, string> _viewMap = new()
        {
            // 🚀 仪表盘 (推荐)
            ["仪表盘"] = "币安量化机器人.Modules.Dashboard.UnifiedDashboardView, 币安量化机器人",

            // 市场与行情
            ["行情"] = "币安量化机器人.Modules.Market.RealtimeView, 币安量化机器人",
            ["资金费率"] = "币安量化机器人.Modules.Market.FundingView, 币安量化机器人",

            // 智能交易
            ["AI助手"] = "币安量化机器人.Modules.AI.AIAssistantView, 币安量化机器人",
            ["信号"] = "币安量化机器人.Modules.Signal.SignalVisualizationView, 币安量化机器人",
            ["绩效"] = "币安量化机器人.Modules.Performance.PerformanceDashboardView, 币安量化机器人",
            ["AI"] = "币安量化机器人.Modules.AI.ModelHub, 币安量化机器人",

            // 策略管理
            ["组合管理"] = "币安量化机器人.Modules.Strategy.StrategyPortfolioView, 币安量化机器人",
            ["参数优化"] = "币安量化机器人.Modules.Optimize.ParameterOptimizerView, 币安量化机器人",
            ["策略库"] = "币安量化机器人.Modules.Strategy.TemplateHub, 币安量化机器人",

            // 交易执行
            ["交易"] = "币安量化机器人.Modules.Trade.TradeView, 币安量化机器人",
            ["持仓订单"] = "币安量化机器人.Modules.Trade.PositionsOrdersView, 币安量化机器人",
            ["纸交易"] = "币安量化机器人.Modules.Paper.PaperTradeView, 币安量化机器人",

            // 风险与账户
            ["风控"] = "币安量化机器人.Modules.Risk.RiskCenterView, 币安量化机器人",
            ["账户"] = "币安量化机器人.Modules.Account.AccountFundsView, 币安量化机器人",
            ["预警"] = "币安量化机器人.Modules.Alert.AlertCenterView, 币安量化机器人",

            // 系统设置
            ["设置"] = "币安量化机器人.Modules.Settings.SystemSettingsView, 币安量化机器人",
            ["API"] = "币安量化机器人.Modules.Settings.ApiManagerView, 币安量化机器人",
            ["诊断"] = "币安量化机器人.Modules.Diagnostics.DiagnosticsView, 币安量化机器人"
        };

        private DispatcherTimer? _heartbeat;

        public MainWindow()
        {
            InitializeComponent();
            StartHeartbeat();

            // 默认打开仪表盘
            LoadViewByTag("仪表盘");
        }

        private void StartHeartbeat()
        {
            _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _heartbeat.Tick += (_, __) =>
            {
                StatusText.Text = $"状态：就绪 · {DateTime.Now:HH:mm:ss}";
            };
            _heartbeat.Start();
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                LoadViewByTag(tag);
            }
        }

        private void LoadViewByTag(string tag)
        {
            // 🆕 兼容旧标签：统一仪表盘 / 统一AI仪表盘
            if (tag == "统一仪表盘" || tag == "统一AI仪表盘" || tag == "统一—AI仪表盘")
            {
                tag = "仪表盘";
            }
            try
            {
                SectionTitle.Text = $"当前模块：{tag}";
                StatusText.Text = $"状态：正在加载 {tag} 模块...";
                MainContentHost.Children.Clear();

                if (!_viewMap.TryGetValue(tag, out string? typeName))
                {
                    MainContentHost.Children.Add(MakePlaceholder(tag, "未注册模块"));
                    StatusText.Text = $"状态：未找到 {tag} 模块";
                    return;
                }

                // 使用"类型全名, 程序集名"解析，最稳妥
                var type = Type.GetType(typeName, throwOnError: false);
                if (type == null || !typeof(UserControl).IsAssignableFrom(type))
                {
                    MainContentHost.Children.Add(MakePlaceholder(tag, $"未找到类型 {typeName}"));
                    StatusText.Text = $"状态：{tag} 模块加载失败";
                    return;
                }

                var view = (UserControl)Activator.CreateInstance(type)!;
                MainContentHost.Children.Add(view);
                StatusText.Text = $"状态：{tag} 模块加载成功 · {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex)
            {
                string errorMsg = $"加载模块失败: {ex.Message}";
                MainContentHost.Children.Add(MakeErrorPlaceholder(tag, errorMsg));
                StatusText.Text = $"状态：错误 - {errorMsg}";

                // 记录到日志
                Services.LogService.Error(ex, $"加载模块 {tag} 时发生异常");
            }
        }

        private static UIElement MakePlaceholder(string tag, string extra)
        {
            return new TextBlock
            {
                Text = $"占位页面（{extra}：{tag}）。\n\n后续将加载真实界面。",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FontSize = 16,
                Opacity = 0.85,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20)
            };
        }

        private static UIElement MakeErrorPlaceholder(string tag, string error)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(254, 242, 242)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 600,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"❌ 模块加载失败: {tag}",
                            FontSize = 18,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(153, 27, 27)),
                            Margin = new Thickness(0, 0, 0, 12)
                        },
                        new TextBlock
                        {
                            Text = error,
                            FontSize = 14,
                            Foreground = new SolidColorBrush(Color.FromRgb(127, 29, 29)),
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };
        }
    }
}
