using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using 币安量化机器人.Models;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Risk;

public partial class RiskCenterView : UserControl
{
    private readonly TradingAccountManager _account_manager;
    private readonly RiskEngine _riskEngine = ServiceLocator.Risk;
    private readonly ObservableCollection<RiskRuleItem> _riskRules = new();
    private readonly ObservableCollection<RiskLogItem> _allRiskLogs = new();
    private readonly ObservableCollection<RiskLogItem> _filteredRiskLogs = new();

    public RiskCenterView()
    {
        InitializeComponent();

        // 初始化服务
        DataCacheService cacheService = ServiceLocator.Cache;
        _account_manager = new TradingAccountManager(cacheService);

        // 绑定数据
        RiskRulesList.ItemsSource = _riskRules;
        RiskLogsGrid.ItemsSource = _filteredRiskLogs;

        InitializeRiskRules();

        _ = LoadDataAsync();
    }

    private void InitializeRiskRules()
    {
        _riskRules.Clear();

        _riskRules.Add(new RiskRuleItem
        {
            Icon = "🛡️",
            IconBackground = new SolidColorBrush(Color.FromRgb(239, 246, 255)),
            RuleName = "最大持仓数限制",
            Description = "限制同时持有的交易对数量,防止过度分散",
            CurrentStatus = "当前: 0/5",
            IsEnabled = true
        });

        _riskRules.Add(new RiskRuleItem
        {
            Icon = "💰",
            IconBackground = new SolidColorBrush(Color.FromRgb(236, 253, 245)),
            RuleName = "单笔最大仓位",
            Description = "限制单笔交易占总资金的比例",
            CurrentStatus = "限制: 20%",
            IsEnabled = true
        });

        _riskRules.Add(new RiskRuleItem
        {
            Icon = "📉",
            IconBackground = new SolidColorBrush(Color.FromRgb(254, 242, 242)),
            RuleName = "最大回撤限制",
            Description = "当回撤超过阈值时停止交易",
            CurrentStatus = "阈值: 15%",
            IsEnabled = true
        });

        _riskRules.Add(new RiskRuleItem
        {
            Icon = "⏰",
            IconBackground = new SolidColorBrush(Color.FromRgb(255, 251, 235)),
            RuleName = "持仓时间止损",
            Description = "持仓超过指定时间自动平仓",
            CurrentStatus = "限制: 24小时",
            IsEnabled = true
        });

        _riskRules.Add(new RiskRuleItem
        {
            Icon = "🔥",
            IconBackground = new SolidColorBrush(Color.FromRgb(254, 242, 242)),
            RuleName = "极端亏损止损",
            Description = "单笔亏损超过阈值立即平仓",
            CurrentStatus = "阈值: 10%",
            IsEnabled = true
        });

        _riskRules.Add(new RiskRuleItem
        {
            Icon = "📊",
            IconBackground = new SolidColorBrush(Color.FromRgb(243, 244, 246)),
            RuleName = "每日亏损限制",
            Description = "当日累计亏损超过限制时停止交易",
            CurrentStatus = "限制: 5%",
            IsEnabled = true
        });
    }

    private async Task LoadDataAsync()
    {
        try
        {
            TradingAccount? account = _account_manager.ActiveAccount;
            if (account == null)
            {
                // 尝试从缓存加载账户配置
                await _account_manager.LoadAccountsAsync();
                account = _account_manager.ActiveAccount;

                if (account == null)
                {
                    account = _account_manager.CreateSimulatedAccount("默认账户", 10000m);
                }
            }

            if (account == null)
            {
                return;
            }

            // 将本地 Position 映射为 Models.PositionSnapshot，缺失字段用合理默认值替代
            var positions = account.Positions.Select(p => new PositionSnapshot
            {
                Symbol = p.Symbol,
                PositionAmt = (decimal)p.Quantity,
                EntryPrice = (decimal)p.EntryPrice,
                MarkPrice = (decimal)p.CurrentPrice,
                UnrealizedProfit = (decimal)p.UnrealizedPnL,
                Leverage = 0m,
                MaintenanceMargin = 0m,
                IsIsolated = false
            }).ToList();

            var rules = new System.Collections.Generic.List<RiskRule>
            {
                new RiskRule { Name = "MaxDrawdown", Threshold = 0.15M, Comparator = "DD>", IsActive = true }
            };

            var report = await _riskEngine.GenerateReportAsync(
                positions,
                rules,
                benchmarkSymbol: "BTCUSDT",
                accountEquity: account.NetValue);

            // 更新 UI
            RiskScoreText.Text = $"风险评分: {report.Metrics.Sum(m => (double)m.KellyFraction):F2}";
            AccountRiskText.Text = report.Metrics.Any() ? "查看详情" : "无数据";

            // 风控日志来自 report.StressTests 或 breached rules
            _allRiskLogs.Clear();
            foreach (var r in report.BreachedRules)
            {
                _allRiskLogs.Add(new RiskLogItem
                {
                    Timestamp = DateTime.UtcNow,
                    LogType = "拒绝",
                    RuleName = r.Name,
                    Symbol = "-",
                    Message = $"规则触发: {r.Name}"
                });
            }

            FilterRiskLogs();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[RiskCenterView] 加载风险数据失败");
        }
    }

    private void FilterRiskLogs()
    {
        _filteredRiskLogs.Clear();

        string filter = "全部";
        if (LogTypeFilter.SelectedItem is ComboBoxItem item)
        {
            filter = item.Content.ToString() ?? "全部";
        }

        var filtered = filter switch
        {
            "拒绝" => _allRiskLogs.Where(l => l.LogType == "拒绝"),
            "警告" => _allRiskLogs.Where(l => l.LogType == "警告"),
            "通过" => _allRiskLogs.Where(l => l.LogType == "通过"),
            _ => _allRiskLogs
        };

        foreach (var log in filtered.OrderByDescending(l => l.Timestamp))
        {
            _filteredRiskLogs.Add(log);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadDataAsync();
    }

    private void LogTypeFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        FilterRiskLogs();
    }
}

public class RiskRuleItem
{
    public string Icon { get; set; } = string.Empty;
    public Brush IconBackground { get; set; } = Brushes.Gray;
    public string RuleName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

public class RiskLogItem
{
    public DateTime Timestamp { get; set; }
    public string LogType { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
