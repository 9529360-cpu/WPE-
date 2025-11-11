using System;
using 币安量化机器人.Core.Models;

namespace 币安量化机器人.Application.Backtesting;

/// <summary>
/// 交易成本计算器 - 精确计算Binance合约的手续费和资金费用
/// </summary>
/// <remarks>
/// <para>成本组成:</para>
/// <list type="bullet">
/// <item><b>交易手续费</b>: Maker/Taker费率差异</item>
/// <item><b>资金费用</b>: 持仓过夜成本,每8小时结算</item>
/// <item><b>滑点成本</b>: 由SlippageCalculator单独计算</item>
/// </list>
/// <para>Binance合约标准费率:</para>
/// <list type="bullet">
/// <item>Maker: 0.02% (挂单等待成交)</item>
/// <item>Taker: 0.04% (立即成交)</item>
/// <item>VIP用户费率更低: 0.01% - 0.015%</item>
/// </list>
/// <para>资金费率:</para>
/// <list type="bullet">
/// <item>每8小时结算一次 (00:00, 08:00, 16:00 UTC)</item>
/// <item>多头支付给空头 (正费率) 或反之 (负费率)</item>
/// <item>典型范围: -0.05% 到 +0.05%</item>
/// </list>
/// </remarks>
public class CostCalculator
{
    /// <summary>
    /// 成本计算常量
    /// </summary>
    private static class CostConstants
    {
        /// <summary>
        /// Binance标准Maker费率 (0.02%)
        /// </summary>
        public const double DEFAULT_MAKER_FEE_RATE = 0.0002;

        /// <summary>
        /// Binance标准Taker费率 (0.04%)
        /// </summary>
        public const double DEFAULT_TAKER_FEE_RATE = 0.0004;

        /// <summary>
        /// 平均资金费率 (0.01% 每8小时)
        /// </summary>
        public const double DEFAULT_FUNDING_RATE_AVG = 0.0001;

        /// <summary>
        /// 资金费率结算间隔 (小时)
        /// </summary>
        public const double FUNDING_INTERVAL_HOURS = 8.0;
    }

    private readonly double _makerFeeRate;
    private readonly double _takerFeeRate;
    private readonly double _fundingRateAvg;

    /// <summary>
    /// 创建成本计算器
    /// </summary>
    /// <param name="makerFeeRate">Maker费率 (默认0.02%)</param>
    /// <param name="takerFeeRate">Taker费率 (默认0.04%)</param>
    /// <param name="fundingRateAvg">平均资金费率 (默认0.01%/8小时)</param>
    /// <remarks>
    /// VIP等级费率参考:
    /// <list type="bullet">
    /// <item>VIP 0: Maker 0.02%, Taker 0.04%</item>
    /// <item>VIP 1: Maker 0.016%, Taker 0.04%</item>
    /// <item>VIP 2: Maker 0.014%, Taker 0.035%</item>
    /// <item>VIP 9: Maker 0.00%, Taker 0.017%</item>
    /// </list>
    /// </remarks>
    public CostCalculator(
        double makerFeeRate = CostConstants.DEFAULT_MAKER_FEE_RATE,
        double takerFeeRate = CostConstants.DEFAULT_TAKER_FEE_RATE,
        double fundingRateAvg = CostConstants.DEFAULT_FUNDING_RATE_AVG)
    {
        _makerFeeRate = makerFeeRate;
        _takerFeeRate = takerFeeRate;
        _fundingRateAvg = fundingRateAvg;
    }

    /// <summary>
    /// 计算交易手续费
    /// </summary>
    /// <param name="quantity">交易数量</param>
    /// <param name="price">交易价格</param>
    /// <param name="isMaker">是否为Maker订单</param>
    /// <returns>手续费金额(USDT)</returns>
    /// <remarks>
    /// <para>手续费 = 名义价值 × 费率</para>
    /// <para>名义价值 = |数量 × 价格|</para>
    /// </remarks>
    public double CalculateTradingFee(double quantity, double price, bool isMaker)
    {
        double notionalValue = Math.Abs(quantity * price);
        double feeRate = isMaker ? _makerFeeRate : _takerFeeRate;
        return notionalValue * feeRate;
    }

    /// <summary>
    /// 计算资金费用 (持仓过夜成本)
    /// </summary>
    /// <param name="quantity">持仓数量</param>
    /// <param name="price">标记价格</param>
    /// <param name="holdingHours">持仓时长(小时)</param>
    /// <returns>资金费用(USDT, 正数=支付, 负数=收取)</returns>
    /// <remarks>
    /// <para>资金费用 = 名义价值 × 资金费率 × 结算次数</para>
    /// <para>结算次数 = ⌈持仓小时数 / 8⌉</para>
    /// <para>注意: 此计算简化为多头总是支付,实际中取决于费率正负</para>
    /// </remarks>
    public double CalculateFundingCost(double quantity, double price, double holdingHours)
    {
        if (holdingHours <= 0)
        {
            return 0;
        }

        double notionalValue = Math.Abs(quantity * price);
        double fundingIntervals = Math.Ceiling(holdingHours / CostConstants.FUNDING_INTERVAL_HOURS);
        double totalFundingRate = _fundingRateAvg * fundingIntervals;

        // 多头支付,空头收取(这里简化为都支付)
        return notionalValue * totalFundingRate;
    }

    /// <summary>
    /// 计算总成本 (手续费 + 资金费用)
    /// </summary>
    /// <param name="entryQuantity">开仓数量</param>
    /// <param name="entryPrice">开仓价格</param>
    /// <param name="entryIsMaker">开仓是否Maker</param>
    /// <param name="exitQuantity">平仓数量</param>
    /// <param name="exitPrice">平仓价格</param>
    /// <param name="exitIsMaker">平仓是否Maker</param>
    /// <param name="holdingHours">持仓时长(小时)</param>
    /// <returns>交易成本明细</returns>
    public TradingCost CalculateTotalCost(
        double entryQuantity,
        double entryPrice,
        bool entryIsMaker,
        double exitQuantity,
        double exitPrice,
        bool exitIsMaker,
        double holdingHours)
    {
        double entryFee = CalculateTradingFee(entryQuantity, entryPrice, entryIsMaker);
        double exitFee = CalculateTradingFee(exitQuantity, exitPrice, exitIsMaker);
        double fundingCost = CalculateFundingCost(entryQuantity, entryPrice, holdingHours);

        return new TradingCost
        {
            EntryFee = entryFee,
            ExitFee = exitFee,
            FundingCost = fundingCost,
            TotalCost = entryFee + exitFee + fundingCost
        };
    }

    /// <summary>
    /// 计算净盈亏 (扣除所有成本)
    /// </summary>
    /// <param name="quantity">交易数量</param>
    /// <param name="entryPrice">开仓价格</param>
    /// <param name="exitPrice">平仓价格</param>
    /// <param name="isLong">是否做多</param>
    /// <param name="holdingHours">持仓时长(小时)</param>
    /// <param name="entryIsMaker">开仓是否Maker</param>
    /// <param name="exitIsMaker">平仓是否Maker</param>
    /// <returns>净盈亏(USDT)</returns>
    /// <remarks>
    /// <para>计算步骤:</para>
    /// <list type="number">
    /// <item>计算毛盈亏 = 数量 × (平仓价 - 开仓价) [做多]</item>
    /// <item>计算总成本 = 开仓手续费 + 平仓手续费 + 资金费用</item>
    /// <item>净盈亏 = 毛盈亏 - 总成本</item>
    /// </list>
    /// </remarks>
    public double CalculateNetPnL(
        double quantity,
        double entryPrice,
        double exitPrice,
        bool isLong,
        double holdingHours,
        bool entryIsMaker,
        bool exitIsMaker)
    {
        // 毛盈亏
        double grossPnL = isLong
            ? quantity * (exitPrice - entryPrice)
            : quantity * (entryPrice - exitPrice);

        // 总成本
        TradingCost cost = CalculateTotalCost(
            quantity, entryPrice, entryIsMaker,
            quantity, exitPrice, exitIsMaker,
            holdingHours
        );

        // 净盈亏 = 毛盈亏 - 总成本
        return grossPnL - cost.TotalCost;
    }
}

/// <summary>
/// 交易成本明细
/// </summary>
public class TradingCost
{
    /// <summary>
    /// 开仓手续费 (USDT)
    /// </summary>
    public double EntryFee { get; init; }

    /// <summary>
    /// 平仓手续费 (USDT)
    /// </summary>
    public double ExitFee { get; init; }

    /// <summary>
    /// 资金费用 (USDT)
    /// </summary>
    public double FundingCost { get; init; }

    /// <summary>
    /// 总成本 (USDT)
    /// </summary>
    public double TotalCost { get; init; }

    /// <summary>
    /// 返回成本明细的格式化字符串
    /// </summary>
    public override string ToString()
    {
        return $"Entry: {EntryFee:F4}, Exit: {ExitFee:F4}, Funding: {FundingCost:F4}, Total: {TotalCost:F4} USDT";
    }
}
