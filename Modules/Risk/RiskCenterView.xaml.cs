using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using 币安量化机器人.Models;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Risk;

public partial class RiskCenterView : UserControl
{
    private readonly TradingAccountManager _accountManager;
    private readonly ObservableCollection<RiskRuleItem> _riskRules = new();
    private readonly ObservableCollection<RiskLogItem> _allRiskLogs = new();
    private readonly ObservableCollection<RiskLogItem> _filteredRiskLogs = new();

    public RiskCenterView()
    {
        InitializeComponent();

        // 初始化服务
        var cacheService = ServiceLocator.Cache;
        _accountManager = new TradingAccountManager(cacheService);

        // 如果没有账户,创建默认账户
        if (_accountManager.ActiveAccount == null)
        {
            _accountManager.CreateSimulatedAccount("模拟账户", 10000m);
        }

        // 绑定数据
        RiskRulesList.ItemsSource = _riskRules;
        RiskLogsGrid.ItemsSource = _filteredRiskLogs;

        // 初始化风控规则
        InitializeRiskRules();

        // 加载数据
        LoadData();
    }

    /// <summary>
    /// 初始化风控规则
    /// </summary>
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

    /// <summary>
    /// 加载数据
    /// </summary>
    private void LoadData()
    {
        var account = _accountManager.ActiveAccount;
        if (account == null)
        {
            return;
        }

        // 计算风险指标
        decimal netValue = account.NetValue;
        int positionCount = account.OpenPositionCount;
        int maxPositions = 5;
        double positionUsage = maxPositions > 0 ? (double)positionCount / maxPositions : 0;

        double totalRisk = account.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .Sum(p => Math.Abs(p.Quantity * p.EntryPrice));

        double riskExposure = totalRisk;
        double exposurePercent = netValue > 0 ? (double)riskExposure / (double)netValue : 0;

        // 计算风险评分 (0-100)
        double riskScore = 0.0;
        if (positionUsage > 0.8)
        {
            riskScore += 30; // 仓位使用率高
        }

        if (exposurePercent > 0.5)
        {
            riskScore += 30; // 风险暴露高
        }

        if (account.TodayPnL < 0)
        {
            riskScore += 20; // 今日亏损
        }

        if (Math.Abs(account.TotalReturnPercent) > 0.1)
        {
            riskScore += 20; // 大幅波动
        }

        // 更新UI
        if (riskScore < 30)
        {
            AccountRiskText.Text = "低";
            AccountRiskText.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94));
        }
        else if (riskScore < 60)
        {
            AccountRiskText.Text = "中";
            AccountRiskText.Foreground = new SolidColorBrush(Color.FromRgb(251, 146, 60));
        }
        else
        {
            AccountRiskText.Text = "高";
            AccountRiskText.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
        }

        RiskScoreText.Text = $"风险评分: {riskScore:F0}/100";

        PositionUsageText.Text = $"{positionUsage:P0}";
        PositionDetailText.Text = $"{positionCount}/{maxPositions} 持仓";

        TodayTriggersText.Text = "0 次"; // TODO: 实际统计

        RiskExposureText.Text = $"{riskExposure:N0} USDT";
        ExposurePercentText.Text = $"占净值 {exposurePercent:P0}";

        // VaR分析 (简化版)
        double dailyReturn = account.TodayReturnPercent;
        double var1Day = Math.Abs(dailyReturn) * (double)netValue * 1.65; // 95%置信度
        double cvar = var1Day * 1.3; // CVaR通常是VaR的1.2-1.5倍
        double maxDrawdown = 0.0; // TODO: 实际计算

        VaR1DayText.Text = $"{var1Day:N2} USDT";
        CVaRText.Text = $"{cvar:N2} USDT";
        MaxDrawdownText.Text = $"{maxDrawdown:P2}";

        // 生成模拟日志
        GenerateMockLogs();
    }

    /// <summary>
    /// 生成模拟风控日志
    /// </summary>
    private void GenerateMockLogs()
    {
        _allRiskLogs.Clear();

        // 添加一些示例日志
        _allRiskLogs.Add(new RiskLogItem
        {
            Timestamp = DateTime.Now.AddMinutes(-5),
            LogType = "通过",
            RuleName = "最大持仓数限制",
            Symbol = "BTCUSDT",
            Message = "持仓数量检查通过 (1/5)"
        });

        _allRiskLogs.Add(new RiskLogItem
        {
            Timestamp = DateTime.Now.AddMinutes(-10),
            LogType = "警告",
            RuleName = "单笔最大仓位",
            Symbol = "ETHUSDT",
            Message = "仓位占比接近限制 (18%/20%)"
        });

        FilterRiskLogs();
    }

    /// <summary>
    /// 筛选风控日志
    /// </summary>
    private void FilterRiskLogs()
    {
        _filteredRiskLogs.Clear();

        string filter = "全部";
        if (LogTypeFilter.SelectedItem is ComboBoxItem item)
        {
            filter = item.Content.ToString() ?? "全部";
        }

        IEnumerable<RiskLogItem> filtered = filter switch
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

    /// <summary>
    /// 刷新
    /// </summary>
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadData();
    }

    /// <summary>
    /// 日志筛选
    /// </summary>
    private void LogTypeFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        FilterRiskLogs();
    }
}

/// <summary>
/// 风控规则项
/// </summary>
public class RiskRuleItem
{
    public string Icon { get; set; } = string.Empty;
    public Brush IconBackground { get; set; } = Brushes.Gray;
    public string RuleName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

/// <summary>
/// 风控日志项
/// </summary>
public class RiskLogItem
{
    public DateTime Timestamp { get; set; }
    public string LogType { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
