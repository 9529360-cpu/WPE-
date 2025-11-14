using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using 币安量化机器人.Models;
using 币安量化机器人.Modules;
using 币安量化机器人.Services;
using System.Reflection;

namespace 币安量化机器人
{
    public partial class MainWindow : Window
    {
        // DI-provided service provider (optional)
        private readonly IServiceProvider? _provider;
        private readonly ILogger<MainWindow>? _logger;

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

        // Nav items for data-driven left navigation
        public ObservableCollection<NavMenuItem> NavItems { get; } = new();

        private DispatcherTimer? _heartbeat;

        // Track currently active lifecycle
        private IModuleLifecycle? _currentLifecycle;
        private UserControl? _currentView;

        // Parameterless constructor - keep for designer/back-compat
        public MainWindow()
        {
            InitializeComponent();

            // set DataContext so bindings in XAML can access NavItems
            DataContext = this;

            StartHeartbeat();

            // Try load from config first
            try
            {
                var configItems = NavConfigService.LoadFromConfig(AppDomain.CurrentDomain.BaseDirectory);
                var any = false;
                foreach (var ni in configItems)
                {
                    NavItems.Add(ni);
                    any = true;
                }

                if (!any)
                {
                    PopulateDefaultNavItems();
                }
            }
            catch
            {
                PopulateDefaultNavItems();
            }

            // Setup grouping view for NavItems
            var view = CollectionViewSource.GetDefaultView(NavItems) as ListCollectionView;
            if (view != null)
            {
                view.GroupDescriptions?.Clear();
                view.GroupDescriptions?.Add(new PropertyGroupDescription("Group"));
            }

            // 默认打开仪表盘
            LoadViewByTag("仪表盘");

            // Diagnostic startup log to help identify running build/version
            try
            {
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
                var ver = asm.GetName().Version?.ToString() ?? "n/a";
                LogService.Info("[MainWindow] Starting - Assembly: {Asm} Version: {Ver}", asm.GetName().Name, ver);
            }
            catch (Exception ex)
            {
                LogService.Warning("[MainWindow] Failed to log assembly version: {0}", ex.Message);
            }
        }

        // DI constructor used when resolved from Host
        public MainWindow(IServiceProvider provider, ILogger<MainWindow> logger) : this()
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _logger.LogInformation("MainWindow constructed with DI provider");
        }

        private void PopulateDefaultNavItems()
        {
            try
            {
                Brush? iconDash = TryFindResource("IconDashboard") as Brush;
                Brush? iconOptimize = TryFindResource("IconOptimize") as Brush;
                Brush? iconLibrary = TryFindResource("IconLibrary") as Brush;
                Brush? iconApi = TryFindResource("IconApi") as Brush;

                var geoDash = TryFindResource("Geo_Dashboard") as Geometry;
                var geoOptimize = TryFindResource("Geo_Optimize") as Geometry;
                var geoLibrary = TryFindResource("Geo_Library") as Geometry;
                var geoApi = TryFindResource("Geo_Api") as Geometry;
                var geoSettings = TryFindResource("Geo_Settings") as Geometry;
                var geoDiag = TryFindResource("Geo_Diagnostics") as Geometry;

                NavItems.Add(new NavMenuItem { Tag = "仪表盘", Title = "仪表盘", Icon = iconDash ?? Brushes.Transparent, GeometryData = geoDash });
                NavItems.Add(new NavMenuItem { Tag = "参数优化", Title = "参数优化", Icon = iconOptimize ?? Brushes.Transparent, GeometryData = geoOptimize });
                NavItems.Add(new NavMenuItem { Tag = "策略库", Title = "策略库", Icon = iconLibrary ?? Brushes.Transparent, GeometryData = geoLibrary });
                NavItems.Add(new NavMenuItem { Tag = "组合管理", Title = "策略组合", Icon = iconLibrary ?? Brushes.Transparent, GeometryData = geoLibrary });
                NavItems.Add(new NavMenuItem { Tag = "API", Title = "API 管理", Icon = iconApi ?? Brushes.Transparent, GeometryData = geoApi });
                NavItems.Add(new NavMenuItem { Tag = "设置", Title = "系统配置", Icon = iconApi ?? Brushes.Transparent, GeometryData = geoSettings });
                NavItems.Add(new NavMenuItem { Tag = "诊断", Title = "日志诊断", Icon = iconApi ?? Brushes.Transparent, GeometryData = geoDiag });
            }
            catch (Exception ex)
            {
                LogError(ex, "PopulateDefaultNavItems 失败");
            }
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

        private void NavCollapseToggle_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (LeftColumn.Width.Value > 60)
                {
                    // collapse
                    LeftColumn.Width = new GridLength(56);

                    // hide text labels inside buttons - best-effort by traversing visual tree
                    CollapseLeftNavText();
                }
                else
                {
                    LeftColumn.Width = new GridLength(220);
                    ExpandLeftNavText();
                }
            }
            catch (Exception ex)
            {
                LogError(ex, "切换侧边栏折叠状态失败");
            }
        }

        private void CollapseLeftNavText()
        {
            // No-op: label visibility handled by binding to NavCollapseToggle.IsChecked
        }

        private void ExpandLeftNavText()
        {
            // No-op: label visibility handled by binding to NavCollapseToggle.IsChecked
        }

        // Make async void so we can await lifecycle Start/Stop without changing callers
        private async void LoadViewByTag(string tag)
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

                // Stop previous lifecycle if present
                if (_currentLifecycle != null)
                {
                    try
                    {
                        await _currentLifecycle.StopAsync();
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, "[MainWindow] 停止上一个模块失败");
                    }
                }

                MainContentHost.Children.Clear();
                _currentLifecycle = null;
                _currentView = null;

                if (!_viewMap.TryGetValue(tag, out string? typeName))
                {
                    MainContentHost.Children.Add(MakePlaceholder(tag, "未注册模块"));
                    StatusText.Text = $"状态：未找到 {tag} 模块";
                    LogService.Warning("[MainWindow] 未找到模块映射: {Tag}", tag);
                    return;
                }

                LogService.Info("[MainWindow] 尝试加载模块 Tag={Tag} TypeName={TypeName}", tag, typeName);
                var type = Type.GetType(typeName, throwOnError: false);
                if (type == null || !typeof(UserControl).IsAssignableFrom(type))
                {
                    MainContentHost.Children.Add(MakePlaceholder(tag, $"未找到类型 {typeName}"));
                    StatusText.Text = $"状态：{tag} 模块加载失败";
                    LogService.Error("[MainWindow] 模块类型解析失败: {TypeName}", typeName);
                    return;
                }

                UserControl? view = null;

                // 如果通过 DI 可解析出视图实例，优先使用 DI 解析以确保依赖注入
                if (_provider != null)
                {
                    try
                    {
                        var svc = _provider.GetService(type) as UserControl;
                        if (svc != null)
                        {
                            view = svc;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, "DI 解析模块视图失败，回退到 Activator");
                    }
                }

                if (view == null)
                {
                    view = (UserControl)Activator.CreateInstance(type)!;
                    LogService.Info("[MainWindow] Activator created view instance: {ViewType}", view.GetType().FullName);
                }

                MainContentHost.Children.Add(view);
                StatusText.Text = $"状态：{tag} 模块加载成功 · {DateTime.Now:HH:mm:ss}";

                // If view supports lifecycle, start it
                if (view is IModuleLifecycle lifecycle)
                {
                    _currentLifecycle = lifecycle;
                    _currentView = view;
                    try
                    {
                        await lifecycle.StartAsync();
                        LogService.Info("[MainWindow] Started lifecycle for view {ViewType}", view.GetType().FullName);
                    }
                    catch (Exception ex)
                    {
                        LogError(ex, "[MainWindow] 启动模块生命周期失败");
                    }
                }
                else
                {
                    _currentLifecycle = null;
                    _currentView = view;
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"加载模块失败: {ex.Message}";
                MainContentHost.Children.Add(MakeErrorPlaceholder(tag, errorMsg));
                StatusText.Text = $"状态：错误 - {errorMsg}";

                // 记录到日志
                LogError(ex, $"加载模块 {tag} 时发生异常");
            }
        }

        protected override async void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Ensure we stop current lifecycle cleanly
            if (_currentLifecycle != null)
            {
                try
                {
                    await _currentLifecycle.StopAsync();
                }
                catch (Exception ex)
                {
                    LogError(ex, "[MainWindow] 关闭时停止模块失败");
                }
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

        private void LogError(Exception ex, string messageTemplate, params object[] args)
        {
            if (_logger != null)
            {
                _logger.LogError(ex, messageTemplate, args);
            }
            else
            {
                Services.LogService.Error(ex, messageTemplate, args);
            }
        }
    }
}
