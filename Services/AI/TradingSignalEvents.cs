using System;
using 币安量化机器人.Models; // added for OrderExecutionResult
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Services.AI
{
    /// <summary>
    /// AI 生成交易信号事件
    /// </summary>
    public class AITradingSignalGeneratedEvent
    {
        public required AITradingSignal Signal { get; init; }
    public string Source { get; init; } = string.Empty; // 例如: AITradingAutomation
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 交易信号执行完成事件
/// </summary>
public class AITradingSignalExecutedEvent
{
    public required AITradingSignal Signal { get; init; }
public required OrderExecutionResult Result
{ get; init; }
public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    }

    /// <summary>
    /// 策略绩效更新事件
    /// </summary>
    public class StrategyPerformanceUpdatedEvent
{
    public required string StrategyName { get; init; }
    public required string Symbol { get; init; }
    public double WinRate { get; init; }
    public double ProfitFactor { get; init; }
    public double SharpeRatio { get; init; }
    public double MaxDrawdown { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 策略替换事件（旧策略被新策略替换）
/// </summary>
public class StrategyReplacementEvent
{
    public required string OldStrategyId { get; init; }
    public required string NewStrategyId { get; init; }
    public required string Symbol { get; init; }
    public string Reason { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
}
