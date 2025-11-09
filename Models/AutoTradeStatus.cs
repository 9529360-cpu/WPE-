using System;

namespace 币安量化机器人.Models;

public enum AutoTradeState
{
    Starting,
    Evaluating,
    Executing,
    Idle,
    RiskOff,
    Stopped,
    Error
}

public enum TradeSignal
{
    None,
    Long,
    Short,
    Exit
}

public record AutoTradeStatus
{
    public string StrategyName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public AutoTradeState State { get; init; }
    public TradeSignal Signal { get; init; }
    public decimal TargetPosition { get; init; }
    public decimal FilledPosition { get; init; }
    public long? LastOrderId { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime UpdatedAt { get; init; } = DateTime.UtcNow;
}
