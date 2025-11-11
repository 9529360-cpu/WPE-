namespace 币安量化机器人.Models.Configuration;

/// <summary>
/// 风控配置 - 风险管理规则开关和参数
/// </summary>
/// <remarks>
/// 统一管理所有风控规则的启用状态和关键参数
/// </remarks>
public class RiskConfig
{
    /// <summary>
    /// 启用动态ATR止损
    /// </summary>
    /// <remarks>
    /// 基于ATR指标动态调整止损位置
    /// </remarks>
    public bool EnableDynamicStopLoss { get; init; } = true;

    /// <summary>
    /// ATR倍数 (默认2.0)
    /// </summary>
    /// <remarks>
    /// 止损距离 = ATR × 倍数，推荐范围: 1.5 - 3.0
    /// </remarks>
    public double AtrMultiplier { get; init; } = 2.0;

    /// <summary>
    /// 启用时间止损
    /// </summary>
    /// <remarks>
    /// 持仓超过指定时间自动平仓，防止长期被套
    /// </remarks>
    public bool EnableTimeBasedExit { get; init; } = true;

    /// <summary>
    /// 时间止损阈值(小时)
    /// </summary>
    /// <remarks>
    /// 持仓超过此时长自动平仓，推荐: 12-48小时
    /// </remarks>
    public double TimeBasedExitHours { get; init; } = 24.0;

    /// <summary>
    /// 启用每日亏损限制
    /// </summary>
    /// <remarks>
    /// 达到每日最大亏损后停止交易
    /// </remarks>
    public bool EnableDailyLossLimit { get; init; } = true;

    /// <summary>
    /// 启用移动止损
    /// </summary>
    /// <remarks>
    /// 盈利达到阈值后自动启用移动止损锁定利润
    /// </remarks>
    public bool EnableTrailingStopLoss { get; init; } = true;

    /// <summary>
    /// 移动止损触发盈利比例 (默认2%)
    /// </summary>
    /// <remarks>
    /// 盈利超过此比例后启用移动止损
    /// </remarks>
    public double TrailingStopTrigger { get; init; } = 0.02;

    /// <summary>
    /// 移动止损回撤比例 (默认1%)
    /// </summary>
    /// <remarks>
    /// 从最高点回撤此比例时触发止损
    /// </remarks>
    public double TrailingStopDistance { get; init; } = 0.01;

    /// <summary>
    /// 启用最大回撤保护
    /// </summary>
    public bool EnableMaxDrawdownProtection { get; init; } = true;

    /// <summary>
    /// 最大回撤阈值 (默认10%)
    /// </summary>
    /// <remarks>
    /// 账户权益从峰值回撤超过此比例时停止交易
    /// </remarks>
    public double MaxDrawdownThreshold { get; init; } = 0.10;

    /// <summary>
    /// 启用最大仓位限制
    /// </summary>
    public bool EnableMaxPositionLimit { get; init; } = true;

    /// <summary>
    /// 最大总仓位占比 (默认50%)
    /// </summary>
    /// <remarks>
    /// 所有持仓的总价值不超过账户权益的此比例
    /// </remarks>
    public double MaxTotalPositionSize { get; init; } = 0.50;

    /// <summary>
    /// 启用黑名单过滤
    /// </summary>
    /// <remarks>
    /// 阻止交易黑名单中的交易对
    /// </remarks>
    public bool EnableBlacklist { get; init; } = true;
}
