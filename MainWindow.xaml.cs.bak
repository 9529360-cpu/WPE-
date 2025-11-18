using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace 币安量化机器人
{
    public partial class MainWindow : Window
    {
        // Tag -> 模块 UserControl 的“类型全名, 程序集名”
        private readonly Dictionary<string, string> _viewMap = new()
        {
            // 市场与行情
            ["行情"] = "币安量化机器人.Modules.Market.RealtimeView, 币安量化机器人",
            ["资金费率"] = "币安量化机器人.Modules.Market.FundingView, 币安量化机器人",

            // 策略与 AI
            ["策略配置"] = "币安量化机器人.Modules.Strategy.SettingsView, 币安量化机器人",
            ["策略库"] = "币安量化机器人.Modules.Strategy.TemplateHub, 币安量化机器人",
            ["AI"] = "币安量化机器人.Modules.AI.ModelHub, 币安量化机器人",

            // ⛳ 关键改动：把旧的 Research.WFOView 改为 Optimize.WfoOptimizer
            ["优化"] = "币安量化机器人.Modules.Optimize.WfoOptimizer, 币安量化机器人",

            // 研发与验证
            ["回测"] = "币安量化机器人.Modules.Research.BacktestView, 币安量化机器人",
            ["纸交易"] = "币安量化机器人.Modules.Paper.PaperTradeView, 币安量化机器人",

            // 实盘与风控
            ["交易"] = "币安量化机器人.Modules.Trade.TradeView, 币安量化机器人",
            ["持仓订单"] = "币安量化机器人.Modules.Trade.PositionsOrdersView, 币安量化机器人",
            ["风控"] = "币安量化机器人.Modules.Risk.RiskCenterView, 币安量化机器人",
            ["预警"] = "币安量化机器人.Modules.Alert.AlertCenterView, 币安量化机器人",

            // 账户与连接
            ["账户"] = "币安量化机器人.Modules.Account.AccountFundsView, 币安量化机器人",
            ["API"] = "币安量化机器人.Modules.Account.ApiManagerView, 币安量化机器人",

            // 系统
            ["设置"] = "币安量化机器人.Modules.Settings.SystemSettingsView, 币安量化机器人",
            ["诊断"] = "币安量化机器人.Modules.Diagnostics.DiagnosticsView, 币安量化机器人"
        };

        public MainWindow()
        {
            InitializeComponent();
            SectionTitle.Text = "欢迎使用 · 请选择左侧功能";
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
                LoadViewByTag(tag);
        }

        private void LoadViewByTag(string tag)
        {
            SectionTitle.Text = $"当前模块：{tag}";
            StatusText.Text = $"状态：已切换到 {tag} 模块";
            MainContentHost.Children.Clear();

            if (!_viewMap.TryGetValue(tag, out var typeName))
            {
                MainContentHost.Children.Add(MakePlaceholder(tag, "未注册模块"));
                return;
            }

            // 使用“类型全名, 程序集名”解析，最稳妥
            var type = Type.GetType(typeName, throwOnError: false);
            if (type == null || !typeof(UserControl).IsAssignableFrom(type))
            {
                MainContentHost.Children.Add(MakePlaceholder(tag, $"未找到类型 {typeName}"));
                return;
            }

            var view = (UserControl)Activator.CreateInstance(type)!;
            MainContentHost.Children.Add(view);
        }

        private static UIElement MakePlaceholder(string tag, string extra)
        {
            return new TextBlock
            {
                Text = $"占位页面（{extra}：{tag}）。后续将加载真实界面。",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FontSize = 16,
                Opacity = 0.85,
                TextWrapping = TextWrapping.Wrap
            };
        }
    }
}
