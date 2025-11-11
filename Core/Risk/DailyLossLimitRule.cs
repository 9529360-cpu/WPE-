using System;
using System.Collections.Concurrent;
using 币安量化机器人.Core.Models;

namespace 币安量化机器人.Core.Risk;

/// <summary>
/// 单日亏损限制规则：防止单日亏损过多
/// 每日UTC 00:00重置
/// </summary>
public sealed class DailyLossLimitRule : IRiskRule
{
    private readonly ConcurrentDictionary<string, DailyPnlTracker> _trackers = new();
    private double _maxDailyLossPercent = 0.02; // 默认2%

    public string Name => "DailyLossLimit";

    public void Configure(RiskConfiguration configuration)
    {
        _maxDailyLossPercent = configuration.MaxDailyLossPercent;
    }

    public RiskRuleResult Evaluate(in PositionSnapshot snapshot)
    {
        DateTime today = DateTime.UtcNow.Date;
        DailyPnlTracker tracker = _trackers.GetOrAdd(today.ToString("yyyy-MM-dd"), _ => new DailyPnlTracker(today));

        // 检查是否需要重置(新的一天)
        if (tracker.Date != today)
        {
            _trackers.TryRemove(tracker.Date.ToString("yyyy-MM-dd"), out _);
            tracker = new DailyPnlTracker(today);
            _trackers.TryAdd(today.ToString("yyyy-MM-dd"), tracker);
        }

        // 累加今日已实现盈亏
        tracker.AddRealizedPnl(snapshot.DailyPnl);

        double totalLoss = tracker.TotalPnl;
        double accountEquity = snapshot.Equity;

        if (accountEquity > 0)
        {
            double lossPercent = Math.Abs(totalLoss) / accountEquity;
            if (totalLoss < 0 && lossPercent > _maxDailyLossPercent)
            {
                return new RiskRuleResult(
                    false,
                    $"今日亏损 {lossPercent:P2} 超过限制 {_maxDailyLossPercent:P2},停止交易"
                );
            }
        }

        return new RiskRuleResult(true);
    }

    /// <summary>
    /// 获取今日累计盈亏(用于UI显示)
    /// </summary>
    public double GetTodayPnl()
    {
        string today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        return _trackers.TryGetValue(today, out DailyPnlTracker? tracker) ? tracker.TotalPnl : 0;
    }
}

/// <summary>
/// 每日盈亏追踪器
/// </summary>
public class DailyPnlTracker
{
    public DateTime Date { get; }
    private double _realizedPnl;

    public DailyPnlTracker(DateTime date)
    {
        Date = date.Date;
        _realizedPnl = 0;
    }

    public void AddRealizedPnl(double pnl)
    {
        _realizedPnl += pnl;
    }

    public double TotalPnl => _realizedPnl;
}
