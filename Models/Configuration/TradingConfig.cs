namespace 币安量化机器人.Models.Configuration;

/// <summary>
/// 交易配置 - 控制交易行为和风险参数
/// </summary>
/// <remarks>
/// 用于配置AI交易系统的核心参数，包括信心度阈值、仓位限制等
/// </remarks>
public class TradingConfig
{
    /// <summary>
    /// 最低信心度阈值 (默认70%)
    /// </summary>
    /// <remarks>
    /// AI信号低于此阈值将被拒绝，推荐范围: 0.60 - 0.80
    /// </remarks>
    public double MinConfidence { get; init; } = 0.70;

    /// <summary>
    /// 最大单次仓位占比 (默认10%)
    /// </summary>
    /// <remarks>
    /// 单笔交易最多使用总资金的百分比，防止过度集中
    /// </remarks>
    public double MaxPositionSize { get; init; } = 0.10;

    /// <summary>
    /// 每日最大亏损限制 (默认5%)
    /// </summary>
    /// <remarks>
    /// 达到此亏损比例后当日停止交易，每日UTC 00:00重置
    /// </remarks>
    public double MaxDailyLoss { get; init; } = 0.05;

    /// <summary>
    /// 最大止损百分比 (默认3%)
    /// </summary>
    /// <remarks>
    /// 单笔交易的最大止损幅度，超过此值的信号将被拒绝
    /// </remarks>
    public double StopLossLimit { get; init; } = 0.03;

    /// <summary>
    /// 默认交易数量
    /// </summary>
    /// <remarks>
    /// 未指定数量时的默认交易量，单位取决于交易对
    /// </remarks>
    public double DefaultQuantity { get; init; } = 0.01;

    /// <summary>
    /// 启用滑点保护
    /// </summary>
    public bool EnableSlippageProtection { get; init; } = true;

    /// <summary>
    /// 最大可接受滑点 (默认0.5%)
    /// </summary>
    public double MaxAcceptableSlippage { get; init; } = 0.005;

    /// <summary>
    /// 自动驾驶配置
    /// </summary>
    public AutopilotConfig Autopilot { get; init; } = new();
}

public class AutopilotConfig
{
    public bool LiveEnabled { get; init; } = false;
    public double WeightStopThreshold { get; init; } = 0.05;
    public double DrawdownStopThreshold { get; init; } = 0.25;
    public int ConsecutiveDrawdownLimit { get; init; } = 3;
    public OptimizationConfig Optimization { get; init; } = new();
}

public class OptimizationConfig
{
    public int PeriodMinutes { get; init; } = 240;
    public double MinPerfImprPct { get; init; } = 0.02;
    public double MinSharpe { get; init; } = 1.2;
    public double MinVolatilityDropPct { get; init; } = 0.05;
}
