using System;
using System.Collections.Generic;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Models;

namespace 币安量化机器人.Application.Backtesting;

/// <summary>
/// 订单撮合引擎 - 模拟真实交易所的订单撮合逻辑
/// </summary>
/// <remarks>
/// <para>支持的订单类型:</para>
/// <list type="bullet">
/// <item><b>市价单 (Market)</b>: 立即成交,有滑点</item>
/// <item><b>限价单 (Limit)</b>: 价格到达时成交,无滑点</item>
/// <item><b>止损单 (Stop)</b>: 价格触发后按市价成交</item>
/// <item><b>止损限价单 (StopLimit)</b>: 价格触发后按限价成交</item>
/// </list>
/// <para>撮合规则:</para>
/// <list type="number">
/// <item>检查订单类型和价格条件</item>
/// <item>判断是否满足成交条件</item>
/// <item>计算滑点 (Taker订单)</item>
/// <item>确定最终成交价格</item>
/// </list>
/// </remarks>
public class OrderMatcher
{
    private readonly SlippageCalculator _slippageCalculator;

    /// <summary>
    /// 创建订单撮合引擎
    /// </summary>
    /// <param name="slippageCalculator">滑点计算器</param>
    public OrderMatcher(SlippageCalculator slippageCalculator)
    {
        _slippageCalculator = slippageCalculator;
    }

    /// <summary>
    /// 撮合订单
    /// </summary>
    /// <param name="order">待撮合的订单</param>
    /// <param name="market">当前市场快照</param>
    /// <returns>订单成交结果</returns>
    public OrderFillResult MatchOrder(SimulatedOrder order, MarketObservation market)
    {
        return order.Type switch
        {
            SimulatedOrderType.Market => MatchMarketOrder(order, market),
            SimulatedOrderType.Limit => MatchLimitOrder(order, market),
            SimulatedOrderType.Stop => MatchStopOrder(order, market),
            SimulatedOrderType.StopLimit => MatchStopLimitOrder(order, market),
            _ => new OrderFillResult { Filled = false, Reason = "Unsupported order type" }
        };
    }

    /// <summary>
    /// 市价单撮合 - 立即成交,有滑点
    /// </summary>
    /// <param name="order">市价订单</param>
    /// <param name="market">市场快照</param>
    /// <returns>成交结果</returns>
    /// <remarks>
    /// 市价单特点:
    /// <list type="bullet">
    /// <item>立即成交,不等待</item>
    /// <item>作为Taker,有滑点成本</item>
    /// <item>买入时价格向上滑,卖出时价格向下滑</item>
    /// </list>
    /// </remarks>
    private OrderFillResult MatchMarketOrder(SimulatedOrder order, MarketObservation market)
    {
        // 计算滑点
        double slippage = _slippageCalculator.CalculateSlippage(
            order.Quantity,
            market.Volume,
            isBuy: order.Side == OrderSide.Buy
        );

        // 成交价格 = 当前价 + 滑点
        double fillPrice = order.Side == OrderSide.Buy
            ? market.Close * (1 + slippage)  // 买入时价格更高
            : market.Close * (1 - slippage); // 卖出时价格更低

        return new OrderFillResult
        {
            Filled = true,
            FilledQuantity = order.Quantity,
            AvgFillPrice = fillPrice,
            FillTime = market.Timestamp,
            Slippage = slippage,
            Reason = "Market order filled immediately"
        };
    }

    /// <summary>
    /// 限价单撮合 - 价格到达才成交
    /// </summary>
    /// <param name="order">限价订单</param>
    /// <param name="market">市场快照</param>
    /// <returns>成交结果</returns>
    /// <remarks>
    /// 限价单特点:
    /// <list type="bullet">
    /// <item>买入: 市场最低价≤限价时成交</item>
    /// <item>卖出: 市场最高价≥限价时成交</item>
    /// <item>作为Maker,无滑点</item>
    /// <item>以限价成交,价格确定</item>
    /// </list>
    /// </remarks>
    private OrderFillResult MatchLimitOrder(SimulatedOrder order, MarketObservation market)
    {
        double limitPrice = order.Price ?? throw new InvalidOperationException("Limit order missing price");

        bool canFill = order.Side == OrderSide.Buy
            ? market.Low <= limitPrice  // 买入限价单: 市场最低价触及或低于限价
            : market.High >= limitPrice; // 卖出限价单: 市场最高价触及或高于限价

        if (!canFill)
        {
            return new OrderFillResult
            {
                Filled = false,
                Reason = $"Price not reached (limit={limitPrice:F4}, low={market.Low:F4}, high={market.High:F4})"
            };
        }

        // 限价单在价格到达时以限价成交(无滑点,因为是maker)
        return new OrderFillResult
        {
            Filled = true,
            FilledQuantity = order.Quantity,
            AvgFillPrice = limitPrice,
            FillTime = market.Timestamp,
            Slippage = 0, // Maker订单无滑点
            Reason = "Limit order filled at limit price"
        };
    }

    /// <summary>
    /// 止损单撮合 - 价格触发后按市价成交
    /// </summary>
    /// <param name="order">止损订单</param>
    /// <param name="market">市场快照</param>
    /// <returns>成交结果</returns>
    /// <remarks>
    /// 止损单特点:
    /// <list type="bullet">
    /// <item>买入: 价格上涨至止损价时触发</item>
    /// <item>卖出: 价格下跌至止损价时触发</item>
    /// <item>触发后转为市价单,有滑点</item>
    /// <item>用于止损或追涨</item>
    /// </list>
    /// </remarks>
    private OrderFillResult MatchStopOrder(SimulatedOrder order, MarketObservation market)
    {
        double stopPrice = order.StopPrice ?? throw new InvalidOperationException("Stop order missing stop price");

        bool triggered = order.Side == OrderSide.Buy
            ? market.High >= stopPrice  // 买入止损: 价格上涨触发
            : market.Low <= stopPrice;  // 卖出止损: 价格下跌触发

        if (!triggered)
        {
            return new OrderFillResult
            {
                Filled = false,
                Reason = $"Stop not triggered (stop={stopPrice:F4}, low={market.Low:F4}, high={market.High:F4})"
            };
        }

        // 触发后按市价成交(有滑点)
        double slippage = _slippageCalculator.CalculateSlippage(
            order.Quantity,
            market.Volume,
            isBuy: order.Side == OrderSide.Buy
        );

        double fillPrice = order.Side == OrderSide.Buy
            ? stopPrice * (1 + slippage)
            : stopPrice * (1 - slippage);

        return new OrderFillResult
        {
            Filled = true,
            FilledQuantity = order.Quantity,
            AvgFillPrice = fillPrice,
            FillTime = market.Timestamp,
            Slippage = slippage,
            Reason = "Stop order triggered and filled"
        };
    }

    /// <summary>
    /// 止损限价单撮合 - 价格触发后按限价成交
    /// </summary>
    /// <param name="order">止损限价订单</param>
    /// <param name="market">市场快照</param>
    /// <returns>成交结果</returns>
    /// <remarks>
    /// 止损限价单特点:
    /// <list type="bullet">
    /// <item>第一步: 检查止损价是否触发</item>
    /// <item>第二步: 检查限价是否满足</item>
    /// <item>两个条件都满足才成交</item>
    /// <item>提供更好的价格控制,但可能不成交</item>
    /// </list>
    /// </remarks>
    private OrderFillResult MatchStopLimitOrder(SimulatedOrder order, MarketObservation market)
    {
        double stopPrice = order.StopPrice ?? throw new InvalidOperationException("Stop-limit order missing stop price");
        double limitPrice = order.Price ?? throw new InvalidOperationException("Stop-limit order missing limit price");

        // 先检查是否触发
        bool triggered = order.Side == OrderSide.Buy
            ? market.High >= stopPrice
            : market.Low <= stopPrice;

        if (!triggered)
        {
            return new OrderFillResult
            {
                Filled = false,
                Reason = $"Stop not triggered (stop={stopPrice:F4})"
            };
        }

        // 触发后检查限价是否成交
        bool canFill = order.Side == OrderSide.Buy
            ? market.Low <= limitPrice
            : market.High >= limitPrice;

        if (!canFill)
        {
            return new OrderFillResult
            {
                Filled = false,
                Reason = $"Stop triggered but limit not reached (limit={limitPrice:F4})"
            };
        }

        return new OrderFillResult
        {
            Filled = true,
            FilledQuantity = order.Quantity,
            AvgFillPrice = limitPrice,
            FillTime = market.Timestamp,
            Slippage = 0,
            Reason = "Stop-limit order triggered and filled"
        };
    }
}

/// <summary>
/// 模拟订单 - 回测中的订单表示
/// </summary>
public class SimulatedOrder
{
    /// <summary>
    /// 交易对符号
    /// </summary>
    public required string Symbol { get; init; }

    /// <summary>
    /// 订单方向 (买/卖)
    /// </summary>
    public required OrderSide Side { get; init; }

    /// <summary>
    /// 订单类型
    /// </summary>
    public required SimulatedOrderType Type { get; init; }

    /// <summary>
    /// 订单数量
    /// </summary>
    public double Quantity { get; init; }

    /// <summary>
    /// 限价 (限价单和止损限价单必填)
    /// </summary>
    public double? Price { get; init; }

    /// <summary>
    /// 止损触发价 (止损单和止损限价单必填)
    /// </summary>
    public double? StopPrice { get; init; }

    /// <summary>
    /// 订单下单时间
    /// </summary>
    public DateTime PlacedAt { get; init; }
}

/// <summary>
/// 模拟订单类型枚举
/// </summary>
public enum SimulatedOrderType
{
    /// <summary>
    /// 市价单 - 立即成交
    /// </summary>
    Market,

    /// <summary>
    /// 限价单 - 价格到达时成交
    /// </summary>
    Limit,

    /// <summary>
    /// 止损单 - 触发后市价成交
    /// </summary>
    Stop,

    /// <summary>
    /// 止损限价单 - 触发后限价成交
    /// </summary>
    StopLimit
}

/// <summary>
/// 订单成交结果
/// </summary>
public class OrderFillResult
{
    /// <summary>
    /// 是否成交
    /// </summary>
    public bool Filled { get; init; }

    /// <summary>
    /// 成交数量
    /// </summary>
    public double FilledQuantity { get; init; }

    /// <summary>
    /// 平均成交价格
    /// </summary>
    public double AvgFillPrice { get; init; }

    /// <summary>
    /// 成交时间
    /// </summary>
    public DateTime FillTime { get; init; }

    /// <summary>
    /// 滑点 (百分比)
    /// </summary>
    public double Slippage { get; init; }

    /// <summary>
    /// 成交/拒绝原因
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}
