using System;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 回测完成事件
/// </summary>
public class BacktestCompletedEvent
{
    public required string StrategyName { get; init; }
    public string Symbol { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public double TotalReturn { get; init; }
    public double SharpeRatio { get; init; }
    public double MaxDrawdown { get; init; }
    public double WinRate { get; init; }
    public int TotalTrades { get; init; }
    public double ProfitFactor { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// 模拟交易更新事件
/// </summary>
public class SimulationUpdateEvent
{
    public decimal CurrentBalance { get; init; }
    public double ProfitPercent { get; init; }
    public double MaxDrawdown { get; init; }
    public double WinRate { get; init; }
    public int TotalTrades { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// 实盘交易事件
/// </summary>
public class LiveTradeEvent
{
    public required string Symbol { get; init; }
    public required string Action { get; init; } // Buy/Sell
    public decimal Price { get; init; }
    public decimal Quantity { get; init; }
    public decimal TodayLoss { get; init; }
    public decimal MaxDrawdown { get; init; }
    public int ConsecutiveLosses { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// 风险警报事件
/// </summary>
public class RiskAlertEvent
{
    public RiskSeverity Severity { get; init; }
    public required string Message { get; init; }
    public required string RiskType { get; init; } // Drawdown/Loss/Leverage
    public double CurrentValue { get; init; }
    public double Threshold { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// 手动干预事件
/// </summary>
public class ManualInterventionEvent
{
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 阶段切换事件
/// </summary>
public class StageTransitionEvent
{
    public WorkflowStage FromStage { get; set; }
    public WorkflowStage ToStage { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// 风险严重程度
/// </summary>
public enum RiskSeverity
{
    Low,
    Medium,
    High,
    Critical
}
