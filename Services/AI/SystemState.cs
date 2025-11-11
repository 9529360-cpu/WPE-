using System;
using System.Collections.Generic;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 系统全局状态
/// </summary>
public class SystemState
{
    public DateTime Timestamp { get; init; }
    public WorkflowStage CurrentStage { get; init; }
    public required MarketCondition MarketCondition { get; init; }
    public required AccountStatus AccountStatus { get; init; }
    public required StrategyStatus StrategyStatus { get; init; }
    public required RiskMetrics RiskMetrics { get; init; }
    public required SystemResources SystemResources { get; init; }

    // 新增：回测结果历史
    public List<BacktestSummary> BacktestResults { get; init; } = new();

    // 新增：优化结果历史
    public List<OptimizationSummary> OptimizationResults { get; init; } = new();

    // 新增：模拟交易开始时间
    public DateTime SimulationStartTime { get; set; }

    // 新增：实盘交易开始时间
    public DateTime LiveTradingStartTime { get; set; }

    /// <summary>
    /// 克隆状态（用于快照）
    /// </summary>
    public SystemState Clone()
    {
        return new SystemState
        {
            Timestamp = Timestamp,
            CurrentStage = CurrentStage,
            MarketCondition = MarketCondition,
            AccountStatus = AccountStatus,
            StrategyStatus = StrategyStatus,
            RiskMetrics = RiskMetrics,
            SystemResources = SystemResources,
            BacktestResults = new List<BacktestSummary>(BacktestResults),
            OptimizationResults = new List<OptimizationSummary>(OptimizationResults),
            SimulationStartTime = SimulationStartTime,
            LiveTradingStartTime = LiveTradingStartTime
        };
    }
}

/// <summary>
/// 回测结果摘要
/// </summary>
public class BacktestSummary
{
    public DateTime Timestamp { get; set; }
    public string StrategyName { get; set; } = string.Empty;
    public decimal TotalReturn { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal WinRate { get; set; }
    public int TotalTrades { get; set; }
}

/// <summary>
/// 优化结果摘要
/// </summary>
public class OptimizationSummary
{
    public DateTime Timestamp { get; set; }
    public string Method { get; set; } = string.Empty;
    public decimal BestScore { get; set; }
    public Dictionary<string, object> BestParameters { get; set; } = new();
    public int Iterations { get; set; }
}

/// <summary>
/// 市场状况
/// </summary>
public class MarketCondition
{
    /// <summary>
    /// 波动率 (0-1)
    /// </summary>
    public double Volatility { get; init; }

    /// <summary>
    /// 趋势 (-1 到 1, 负数=下跌，正数=上涨)
    /// </summary>
    public double Trend { get; init; }

    /// <summary>
    /// 流动性 (0-1)
    /// </summary>
    public double Liquidity { get; init; }

    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 判断市场是否平稳
    /// </summary>
    public bool IsStable => Volatility < 0.5 && Liquidity > 0.7;

    /// <summary>
    /// 判断市场是否高波动
    /// </summary>
    public bool IsHighVolatility => Volatility > 0.7;

    /// <summary>
    /// 判断市场趋势
    /// </summary>
    public string TrendDirection => Trend switch
    {
        > 0.2 => "上涨",
        < -0.2 => "下跌",
        _ => "震荡"
    };
}

/// <summary>
/// 账户状态
/// </summary>
public class AccountStatus
{
    public AccountType Type { get; init; }
    public decimal NetValue { get; init; }
    public decimal AvailableBalance { get; init; }
    public decimal PositionValue { get; init; }
    public decimal TodayPnL { get; init; }
    public decimal TotalPnL { get; init; }
    public double WinRate { get; init; }
    public int OpenPositionCount { get; init; }
    public DateTime Timestamp { get; init; }

    // 新增：连续亏损天数
    public int DailyLossCount { get; set; }

    /// <summary>
    /// 今日收益率
    /// </summary>
    public decimal TodayReturnPercent => NetValue == 0 ? 0 : TodayPnL / NetValue;

    /// <summary>
    /// 总收益率
    /// </summary>
    public decimal TotalReturnPercent => NetValue == 0 ? 0 : TotalPnL / NetValue;

    /// <summary>
    /// 是否盈利
    /// </summary>
    public bool IsProfitable => TotalPnL > 0;
}

/// <summary>
/// 策略状态
/// </summary>
public class StrategyStatus
{
    /// <summary>
    /// 策略是否激活
    /// </summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// 策略表现评分 (0-1)
    /// </summary>
    public double PerformanceScore { get; init; }

    /// <summary>
    /// 最近信号数量
    /// </summary>
    public int RecentSignalCount { get; init; }

    /// <summary>
    /// 平均信心度
    /// </summary>
    public double AverageConfidence { get; init; }

    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 策略是否表现良好
    /// </summary>
    public bool IsPerformingWell => PerformanceScore > 0.7 && AverageConfidence > 0.7;
}

/// <summary>
/// 风险指标
/// </summary>
public class RiskMetrics
{
    /// <summary>
    /// 最大回撤
    /// </summary>
    public decimal MaxDrawdown { get; init; }

    /// <summary>
    /// 当前回撤
    /// </summary>
    public decimal CurrentDrawdown { get; init; }

    /// <summary>
    /// 今日亏损
    /// </summary>
    public decimal DailyLoss { get; init; }

    /// <summary>
    /// 杠杆倍数
    /// </summary>
    public decimal Leverage { get; init; }

    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 是否在安全范围内
    /// </summary>
    public bool IsSafe => MaxDrawdown < 0.15m && DailyLoss < 500m && Leverage <= 3m;

    /// <summary>
    /// 是否触发风险警报
    /// </summary>
    public bool HasRiskAlert => MaxDrawdown > 0.2m || DailyLoss > 1000m || Leverage > 5m;
}

/// <summary>
/// 系统资源
/// </summary>
public class SystemResources
{
    /// <summary>
    /// CPU使用率 (0-1)
    /// </summary>
    public double CpuUsage { get; init; }

    /// <summary>
    /// 内存使用率 (0-1)
    /// </summary>
    public double MemoryUsage { get; init; }

    /// <summary>
    /// 网络延迟 (毫秒)
    /// </summary>
    public double NetworkLatency { get; init; }

    /// <summary>
    /// 活动任务数量
    /// </summary>
    public int ActiveTasks { get; init; }

    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 资源是否充足
    /// </summary>
    public bool IsHealthy => CpuUsage < 0.8 && MemoryUsage < 0.8 && NetworkLatency < 200;
}
