using System;

namespace 币安量化机器人.Models;

/// <summary>
/// 每日盈亏快照
/// </summary>
public class DailyPnlSnapshot
{
    public DateTime Date { get; init; }
    public double RealizedPnl { get; init; }
    public double UnrealizedPnl { get; init; }
    public double TotalPnl { get; init; }
    public int TradeCount { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }

    public double WinRate => TradeCount > 0 ? (double)WinningTrades / TradeCount : 0;
}

/// <summary>
/// 交易记录(用于数据库持久化)
/// </summary>
public class TradeRecord
{
    public string TradeId { get; init; } = string.Empty;
    public string OrderId { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Side { get; init; } = string.Empty;
    public double Quantity { get; init; }
    public double Price { get; init; }
    public double Commission { get; init; }
    public double? RealizedPnl { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// 策略绩效记录
/// </summary>
public class StrategyPerformanceRecord
{
    public string StrategyName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public double TotalPnl { get; init; }
    public double WinRate { get; init; }
    public double ProfitFactor { get; init; }
    public double SharpeRatio { get; init; }
    public double MaxDrawdown { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// 策略绩效增量更新
/// </summary>
public class StrategyPerformanceUpdate
{
    public int TradeCount { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public double PnlDelta { get; init; }
    public double WinRate { get; init; }
    public double ProfitFactor { get; init; }
    public double SharpeRatio { get; init; }
    public double MaxDrawdown { get; init; }
}
