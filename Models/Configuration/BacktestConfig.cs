namespace 币安量化机器人.Models.Configuration;

/// <summary>
/// 回测配置 - 回测引擎参数
/// </summary>
/// <remarks>
/// 配置回测初始资金、手续费、滑点等真实交易环境模拟参数
/// </remarks>
public class BacktestConfig
{
    /// <summary>
    /// 初始资金(USDT)
    /// </summary>
    /// <remarks>
    /// 回测开始时的账户余额
    /// </remarks>
    public double InitialCapital { get; init; } = 10000.0;

    /// <summary>
    /// Maker手续费率
    /// </summary>
    /// <remarks>
    /// Binance合约标准: 0.02%
    /// VIP等级更低: 0.01% - 0.015%
    /// </remarks>
    public double MakerFeeRate { get; init; } = 0.0002;

    /// <summary>
    /// Taker手续费率
    /// </summary>
    /// <remarks>
    /// Binance合约标准: 0.04%
    /// VIP等级更低: 0.03% - 0.035%
    /// </remarks>
    public double TakerFeeRate { get; init; } = 0.0004;

    /// <summary>
    /// 平均资金费率(每8小时)
    /// </summary>
    /// <remarks>
    /// 多头支付,空头收取
    /// 典型范围: -0.05% 到 +0.05%
    /// 保守估计: 0.01%
    /// </remarks>
    public double AvgFundingRate { get; init; } = 0.0001;

    /// <summary>
    /// 基础滑点
    /// </summary>
    /// <remarks>
    /// 市价单的最小滑点，推荐: 0.01% - 0.05%
    /// </remarks>
    public double BaseSlippage { get; init; } = 0.0001;

    /// <summary>
    /// 市场冲击系数
    /// </summary>
    /// <remarks>
    /// 订单大小对价格的影响系数
    /// 滑点 = 基础滑点 + (订单量/市场成交量) × 冲击系数
    /// </remarks>
    public double ImpactFactor { get; init; } = 0.00001;

    /// <summary>
    /// 点差倍数
    /// </summary>
    /// <remarks>
    /// 买卖价差影响因子
    /// </remarks>
    public double SpreadMultiplier { get; init; } = 0.5;

    /// <summary>
    /// 使用VIP费率
    /// </summary>
    /// <remarks>
    /// 如果账户有VIP等级，可降低手续费
    /// </remarks>
    public bool UseVipFeeRates { get; init; } = false;

    /// <summary>
    /// VIP等级 (0-9)
    /// </summary>
    public int VipLevel { get; init; } = 0;

    /// <summary>
    /// 启用压力测试模式
    /// </summary>
    /// <remarks>
    /// 使用最坏情况参数(3倍滑点、最高手续费)
    /// </remarks>
    public bool EnableStressTest { get; init; } = false;

    /// <summary>
    /// 每笔交易最小间隔(秒)
    /// </summary>
    /// <remarks>
    /// 模拟真实交易时的订单提交间隔
    /// </remarks>
    public int MinTradeIntervalSeconds { get; init; } = 1;

    /// <summary>
    /// 启用真实订单撮合
    /// </summary>
    /// <remarks>
    /// 使用市场深度数据进行精确撮合，需要完整历史数据
    /// </remarks>
    public bool EnableRealisticOrderMatching { get; init; } = true;
}
