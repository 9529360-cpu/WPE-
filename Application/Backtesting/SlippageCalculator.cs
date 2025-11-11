using System;

namespace 币安量化机器人.Application.Backtesting;

/// <summary>
/// 滑点计算器 - 基于订单大小和市场流动性计算真实滑点
/// </summary>
/// <remarks>
/// <para>滑点来源:</para>
/// <list type="bullet">
/// <item><b>基础滑点</b>: 最小交易成本</item>
/// <item><b>市场冲击</b>: 订单大小对价格的影响</item>
/// <item><b>点差成本</b>: 买卖价差</item>
/// </list>
/// <para>计算公式:</para>
/// <code>
/// 总滑点 = 基础滑点 + (订单量/市场成交量) × 冲击系数 + 基础滑点 × 点差倍数
/// </code>
/// <para>不同订单类型的滑点:</para>
/// <list type="bullet">
/// <item>市价单: 1.5倍滑点 (Taker,立即成交)</item>
/// <item>限价单: 0滑点 (Maker,挂单等待)</item>
/// <item>止损单: 2.0倍滑点 (紧急成交)</item>
/// <item>止损限价单: 0.5倍滑点 (有价格保护)</item>
/// </list>
/// </remarks>
public class SlippageCalculator
{
    /// <summary>
    /// 滑点计算常量
    /// </summary>
    private static class SlippageConstants
    {
        /// <summary>
        /// 默认基础滑点 (0.01%)
        /// </summary>
        public const double DEFAULT_BASE_SLIPPAGE = 0.0001;

        /// <summary>
        /// 默认市场冲击系数
        /// </summary>
        public const double DEFAULT_IMPACT_FACTOR = 0.00001;

        /// <summary>
        /// 默认点差倍数
        /// </summary>
        public const double DEFAULT_SPREAD_MULTIPLIER = 0.5;

        /// <summary>
        /// 最大滑点限制 (1%)
        /// </summary>
        public const double MAX_SLIPPAGE = 0.01;

        /// <summary>
        /// 市价单滑点倍数
        /// </summary>
        public const double MARKET_ORDER_MULTIPLIER = 1.5;

        /// <summary>
        /// 止损单滑点倍数
        /// </summary>
        public const double STOP_ORDER_MULTIPLIER = 2.0;

        /// <summary>
        /// 止损限价单滑点倍数
        /// </summary>
        public const double STOP_LIMIT_MULTIPLIER = 0.5;

        /// <summary>
        /// 最坏情况滑点倍数
        /// </summary>
        public const double WORST_CASE_MULTIPLIER = 3.0;
    }

    private readonly double _baseSlippage;
    private readonly double _impactFactor;
    private readonly double _spreadMultiplier;

    /// <summary>
    /// 创建滑点计算器
    /// </summary>
    /// <param name="baseSlippage">基础滑点 (默认0.01%)</param>
    /// <param name="impactFactor">市场冲击系数 (默认0.001%)</param>
    /// <param name="spreadMultiplier">点差倍数 (默认0.5)</param>
    public SlippageCalculator(
        double baseSlippage = SlippageConstants.DEFAULT_BASE_SLIPPAGE,
        double impactFactor = SlippageConstants.DEFAULT_IMPACT_FACTOR,
        double spreadMultiplier = SlippageConstants.DEFAULT_SPREAD_MULTIPLIER)
    {
        _baseSlippage = baseSlippage;
        _impactFactor = impactFactor;
        _spreadMultiplier = spreadMultiplier;
    }

    /// <summary>
    /// 计算滑点百分比
    /// </summary>
    /// <param name="orderSize">订单数量(张数或币数)</param>
    /// <param name="marketVolume">市场成交量</param>
    /// <param name="isBuy">是否买入方向</param>
    /// <returns>滑点百分比 (例: 0.0005 = 0.05%)</returns>
    /// <remarks>
    /// <para>滑点计算步骤:</para>
    /// <list type="number">
    /// <item>计算订单占市场成交量的比例</item>
    /// <item>计算市场冲击 = 比例 × 冲击系数</item>
    /// <item>计算点差成本 = 基础滑点 × 点差倍数</item>
    /// <item>总和并限制在最大滑点以内</item>
    /// </list>
    /// </remarks>
    public double CalculateSlippage(double orderSize, double marketVolume, bool isBuy)
    {
        if (orderSize <= 0 || marketVolume <= 0)
        {
            return _baseSlippage;
        }

        // 订单占市场成交量的比例
        double orderRatio = Math.Abs(orderSize) / marketVolume;

        // 市场冲击 = 订单比例 × 冲击系数
        double marketImpact = orderRatio * _impactFactor;

        // 点差成本 = 基础滑点 × 点差倍数
        double spreadCost = _baseSlippage * _spreadMultiplier;

        // 总滑点 = 基础滑点 + 市场冲击 + 点差成本
        double totalSlippage = _baseSlippage + marketImpact + spreadCost;

        // 限制滑点在合理范围内 (最多1%)
        return Math.Min(totalSlippage, SlippageConstants.MAX_SLIPPAGE);
    }

    /// <summary>
    /// 根据订单类型调整滑点
    /// </summary>
    /// <param name="baseSlippage">基础滑点</param>
    /// <param name="orderType">订单类型</param>
    /// <returns>调整后的滑点</returns>
    /// <remarks>
    /// <para>不同订单类型的滑点倍数:</para>
    /// <list type="bullet">
    /// <item>市价单: 1.5倍 (立即成交,价格不确定)</item>
    /// <item>限价单: 0 (Maker,无滑点)</item>
    /// <item>止损单: 2.0倍 (紧急止损,滑点最大)</item>
    /// <item>止损限价单: 0.5倍 (有价格保护)</item>
    /// </list>
    /// </remarks>
    public double AdjustForOrderType(double baseSlippage, SimulatedOrderType orderType)
    {
        return orderType switch
        {
            SimulatedOrderType.Market => baseSlippage * SlippageConstants.MARKET_ORDER_MULTIPLIER,
            SimulatedOrderType.Limit => 0,
            SimulatedOrderType.Stop => baseSlippage * SlippageConstants.STOP_ORDER_MULTIPLIER,
            SimulatedOrderType.StopLimit => baseSlippage * SlippageConstants.STOP_LIMIT_MULTIPLIER,
            _ => baseSlippage
        };
    }

    /// <summary>
    /// 计算最坏情况滑点 (用于压力测试和风险评估)
    /// </summary>
    /// <param name="orderSize">订单数量</param>
    /// <param name="marketVolume">市场成交量</param>
    /// <returns>最坏情况的滑点 (正常滑点的3倍)</returns>
    /// <remarks>
    /// 用于:
    /// <list type="bullet">
    /// <item>极端市场条件下的回测</item>
    /// <item>风险管理的保守估计</item>
    /// <item>策略的稳健性测试</item>
    /// </list>
    /// </remarks>
    public double CalculateWorstCaseSlippage(double orderSize, double marketVolume)
    {
        double normalSlippage = CalculateSlippage(orderSize, marketVolume, isBuy: true);
        return normalSlippage * SlippageConstants.WORST_CASE_MULTIPLIER;
    }
}
