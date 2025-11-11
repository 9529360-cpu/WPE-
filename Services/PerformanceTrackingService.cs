using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

/// <summary>
/// 策略绩效追踪服务：自动计算和更新策略统计指标
/// </summary>
public class PerformanceTrackingService
{
    private readonly DataCacheService _cache;

    public PerformanceTrackingService(DataCacheService cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// 记录交易并更新策略绩效
    /// </summary>
    public async Task RecordTradeAsync(string strategyName, TradeRecord trade)
    {
        // 保存交易记录
        await _cache.SaveTradeAsync(trade);

        // 计算本次交易是否盈利
        bool isWin = trade.RealizedPnl.HasValue && trade.RealizedPnl.Value > 0;

        // 更新策略绩效
        var update = new StrategyPerformanceUpdate
        {
            TradeCount = 1,
            WinningTrades = isWin ? 1 : 0,
            LosingTrades = isWin ? 0 : 1,
            PnlDelta = trade.RealizedPnl ?? 0,
            WinRate = isWin ? 1.0 : 0.0,
            ProfitFactor = 0, // 需要从历史数据计算
            SharpeRatio = 0,  // 需要从历史数据计算
            MaxDrawdown = 0   // 需要从历史数据计算
        };

        await _cache.UpdateStrategyPerformanceAsync(strategyName, trade.Symbol, update);
    }

    /// <summary>
    /// 获取策略详细绩效报告
    /// </summary>
    public async Task<StrategyPerformanceReport?> GetPerformanceReportAsync(string strategyName, string symbol)
    {
        var perf = await _cache.LoadStrategyPerformanceAsync(strategyName, symbol);
        if (perf == null)
        {
            return null;
        }

        // 加载最近100笔订单计算详细指标
        var orders = await _cache.LoadOrdersAsync(symbol, 100);
        var strategyOrders = orders
            .Where(o => o.StrategyName == strategyName && o.Status == "FILLED")
            .OrderBy(o => o.CreatedAt)
            .ToList();

        if (strategyOrders.Count == 0)
        {
            return new StrategyPerformanceReport
            {
                Basic = perf,
                AvgWinSize = 0,
                AvgLossSize = 0,
                LargestWin = 0,
                LargestLoss = 0,
                ConsecutiveWins = 0,
                ConsecutiveLosses = 0,
                AvgHoldingTime = TimeSpan.Zero
            };
        }

        // 计算盈利交易统计
        var wins = strategyOrders
            .Where(o => o.AvgFillPrice.HasValue && o.Price.HasValue &&
                        (o.Side == "BUY" ? o.AvgFillPrice < o.Price : o.AvgFillPrice > o.Price))
            .ToList();

        var losses = strategyOrders
            .Where(o => o.AvgFillPrice.HasValue && o.Price.HasValue &&
                        (o.Side == "BUY" ? o.AvgFillPrice >= o.Price : o.AvgFillPrice <= o.Price))
            .ToList();

        double avgWin = wins.Any()
            ? wins.Average(o => Math.Abs((o.AvgFillPrice!.Value - o.Price!.Value) * o.FilledQuantity))
            : 0;

        double avgLoss = losses.Any()
            ? losses.Average(o => Math.Abs((o.AvgFillPrice!.Value - o.Price!.Value) * o.FilledQuantity))
            : 0;

        double largestWin = wins.Any()
            ? wins.Max(o => Math.Abs((o.AvgFillPrice!.Value - o.Price!.Value) * o.FilledQuantity))
            : 0;

        double largestLoss = losses.Any()
            ? losses.Max(o => Math.Abs((o.AvgFillPrice!.Value - o.Price!.Value) * o.FilledQuantity))
            : 0;

        // 计算连续胜/负次数
        var (maxConsecutiveWins, maxConsecutiveLosses) = CalculateStreaks(strategyOrders);

        // 计算平均持仓时间
        double avgHoldingTime = strategyOrders
            .Where(o => o.FilledAt.HasValue)
            .Select(o => o.FilledAt!.Value - o.CreatedAt)
            .DefaultIfEmpty(TimeSpan.Zero)
            .Average(ts => ts.TotalSeconds);

        return new StrategyPerformanceReport
        {
            Basic = perf,
            AvgWinSize = avgWin,
            AvgLossSize = avgLoss,
            LargestWin = largestWin,
            LargestLoss = largestLoss,
            ConsecutiveWins = maxConsecutiveWins,
            ConsecutiveLosses = maxConsecutiveLosses,
            AvgHoldingTime = TimeSpan.FromSeconds(avgHoldingTime)
        };
    }

    /// <summary>
    /// 计算最大连续胜/负次数
    /// </summary>
    private static (int maxWins, int maxLosses) CalculateStreaks(List<OrderHistoryRecord> orders)
    {
        int currentWins = 0, currentLosses = 0;
        int maxWins = 0, maxLosses = 0;

        foreach (var order in orders)
        {
            if (!order.AvgFillPrice.HasValue || !order.Price.HasValue)
            {
                continue;
            }

            bool isWin = order.Side == "BUY"
                ? order.AvgFillPrice < order.Price
                : order.AvgFillPrice > order.Price;

            if (isWin)
            {
                currentWins++;
                currentLosses = 0;
                maxWins = Math.Max(maxWins, currentWins);
            }
            else
            {
                currentLosses++;
                currentWins = 0;
                maxLosses = Math.Max(maxLosses, currentLosses);
            }
        }

        return (maxWins, maxLosses);
    }

    /// <summary>
    /// 获取每日盈亏趋势
    /// </summary>
    public async Task<IReadOnlyList<DailyPnlSnapshot>> GetDailyPnlTrendAsync(int days = 30)
    {
        return await _cache.LoadDailyPnlAsync(days);
    }
}

/// <summary>
/// 策略绩效详细报告
/// </summary>
public class StrategyPerformanceReport
{
    public StrategyPerformanceRecord Basic { get; init; } = null!;
    public double AvgWinSize { get; init; }
    public double AvgLossSize { get; init; }
    public double LargestWin { get; init; }
    public double LargestLoss { get; init; }
    public int ConsecutiveWins { get; init; }
    public int ConsecutiveLosses { get; init; }
    public TimeSpan AvgHoldingTime { get; init; }

    public double ExpectancyPerTrade => (Basic.WinRate * AvgWinSize) - ((1 - Basic.WinRate) * AvgLossSize);
}
