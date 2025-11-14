using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services;

/// <summary>
/// 订单历史服务：追踪所有订单生命周期
/// </summary>
public class OrderHistoryService
{
    private readonly DataCacheService _cache;

    public OrderHistoryService(DataCacheService cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// 记录新订单
    /// </summary>
    public async Task RecordOrderPlacedAsync(OrderResponse response, string? strategyName = null)
    {
        var record = new OrderHistoryRecord
        {
            OrderId = response.OrderId.ToString(),
            Symbol = response.Symbol,
            Side = response.Status.Contains("BUY", StringComparison.OrdinalIgnoreCase) ? "BUY" : "SELL",
            Type = "LIMIT", // 从响应推断,实际应从请求获取
            Quantity = (double)response.ExecutedQuantity,
            Price = response.Price > 0 ? (double)response.Price : (double?)null,
            Status = response.Status,
            FilledQuantity = (double)response.ExecutedQuantity,
            AvgFillPrice = response.AvgPrice > 0 ? (double)response.AvgPrice : (double?)null,
            Commission = 0, // 需要从trades API获取
            StrategyName = strategyName,
            CreatedAt = response.Time,
            UpdatedAt = response.Time,
            FilledAt = response.ExecutedQuantity > 0 ? response.Time : (DateTime?)null
        };

        try
        {
            await _cache.SaveOrderAsync(record).ConfigureAwait(false);

            // 保存成交为 TradeRecord（若有成交数量）
            if (response.ExecutedQuantity > 0)
            {
                var trade = new TradeRecord
                {
                    TradeId = Guid.NewGuid().ToString("N"),
                    OrderId = response.OrderId.ToString(),
                    Symbol = response.Symbol,
                    Side = record.Side,
                    Quantity = (double)response.ExecutedQuantity,
                    Price = (double)(response.AvgPrice == 0 ? response.Price : response.AvgPrice),
                    Commission = 0,
                    RealizedPnl = null,
                    Timestamp = response.Time
                };

                try
                {
                    await _cache.SaveTradeAsync(trade).ConfigureAwait(false);

                    // 如果有已实现 pnl (nullable), 更新每日 pnl 汇总
                    if (trade.RealizedPnl.HasValue)
                    {
                        string date = trade.Timestamp.ToString("yyyy-MM-dd");
                        await _cache.UpdateDailyPnlAsync(date, trade.RealizedPnl.Value, 1, trade.RealizedPnl.Value > 0).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "保存 TradeRecord 失败");
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "RecordOrderPlacedAsync 保存订单失败");
        }
    }

    /// <summary>
    /// 记录被风控拒绝的订单（本地记录）
    /// </summary>
    public async Task RecordRejectedOrderAsync(OrderRequest request, string reason, TradingAccount? account = null)
    {
        var record = new OrderHistoryRecord
        {
            OrderId = request.ClientOrderId ?? $"local-{Guid.NewGuid():N}",
            Symbol = request.Symbol,
            Side = request.Side.ToString().ToUpperInvariant(),
            Type = request.Type.ToString().ToUpperInvariant(),
            Quantity = (double)request.Quantity,
            Price = (double)request.Price,
            StopPrice = (double)request.StopPrice,
            Status = "REJECTED",
            FilledQuantity = 0,
            AvgFillPrice = null,
            Commission = 0,
            StrategyName = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            FilledAt = null
        };

        // 可把拒单原因保存到缓存或日志，这里将其作为 StrategyName 的占位以便查看（或扩展 OrderHistoryRecord）
        try
        {
            await _cache.SaveOrderAsync(record).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "记录被拒订单失败");
        }
    }

    /// <summary>
    /// 更新订单状态(成交/取消)
    /// </summary>
    public async Task UpdateOrderStatusAsync(string orderId, string status, double filledQty, double? avgPrice, DateTime updateTime)
    {
        // 先查询现有订单
        IReadOnlyList<OrderHistoryRecord> existing = await _cache.LoadOrdersAsync(limit: 1).ConfigureAwait(false); // 简化版,实际需按orderId查询
        if (existing.Count == 0)
        {
            return;
        }

        OrderHistoryRecord order = existing[0];
        var updated = new OrderHistoryRecord
        {
            OrderId = orderId,
            Symbol = order.Symbol,
            Side = order.Side,
            Type = order.Type,
            Quantity = order.Quantity,
            Price = order.Price,
            StopPrice = order.StopPrice,
            Status = status,
            FilledQuantity = filledQty,
            AvgFillPrice = avgPrice,
            Commission = order.Commission,
            StrategyName = order.StrategyName,
            CreatedAt = order.CreatedAt,
            UpdatedAt = updateTime,
            FilledAt = filledQty >= order.Quantity ? updateTime : order.FilledAt
        };

        try
        {
            await _cache.SaveOrderAsync(updated).ConfigureAwait(false);

            // 保存交易记录并更新每日 PnL（如果 avgPrice 提供并能计算 realized pnl）
            if (filledQty > 0 && avgPrice.HasValue)
            {
                var trade = new TradeRecord
                {
                    TradeId = Guid.NewGuid().ToString("N"),
                    OrderId = orderId,
                    Symbol = order.Symbol,
                    Side = order.Side,
                    Quantity = filledQty,
                    Price = avgPrice.Value,
                    Commission = updated.Commission,
                    RealizedPnl = null,
                    Timestamp = updateTime
                };

                try
                {
                    await _cache.SaveTradeAsync(trade).ConfigureAwait(false);

                    if (trade.RealizedPnl.HasValue)
                    {
                        string date = trade.Timestamp.ToString("yyyy-MM-dd");
                        await _cache.UpdateDailyPnlAsync(date, trade.RealizedPnl.Value, 1, trade.RealizedPnl.Value > 0).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "保存成交记录失败");
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "UpdateOrderStatusAsync 保存更新失败");
        }
    }

    /// <summary>
    /// 获取订单历史(带过滤)
    /// </summary>
    public async Task<IReadOnlyList<OrderHistoryRecord>> GetOrderHistoryAsync(string? symbol = null, int limit = 100)
    {
        return await _cache.LoadOrdersAsync(symbol, limit).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取策略统计
    /// </summary>
    public async Task<StrategyStats> GetStrategyStatsAsync(string strategyName, string? symbol = null)
    {
        IReadOnlyList<OrderHistoryRecord> orders = await _cache.LoadOrdersAsync(symbol, 1000).ConfigureAwait(false);
        var strategyOrders = orders.Where(o => o.StrategyName == strategyName && o.Status == "FILLED").ToList();

        int totalTrades = strategyOrders.Count;
        int winningTrades = strategyOrders.Count(o => (o.AvgFillPrice ?? 0) > (o.Price ?? 0));
        int losingTrades = totalTrades - winningTrades;
        double winRate = totalTrades > 0 ? (double)winningTrades / totalTrades : 0;

        return new StrategyStats
        {
            StrategyName = strategyName,
            TotalTrades = totalTrades,
            WinningTrades = winningTrades,
            LosingTrades = losingTrades,
            WinRate = winRate
        };
    }

    /// <summary>
    /// 记录或保存成交记录（由执行器调用）
    /// </summary>
    public async Task RecordTradeAsync(TradeRecord trade)
    {
        if (trade == null)
        {
            return;
        }

        try
        {
            await _cache.SaveTradeAsync(trade).ConfigureAwait(false);

            if (trade.RealizedPnl.HasValue)
            {
                string date = trade.Timestamp.ToString("yyyy-MM-dd");
                await _cache.UpdateDailyPnlAsync(date, trade.RealizedPnl.Value, 1, trade.RealizedPnl.Value > 0).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "RecordTradeAsync 保存成交记录失败");
        }
    }
}

/// <summary>
/// 策略统计数据
/// </summary>
public class StrategyStats
{
    public string StrategyName { get; init; } = string.Empty;
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public double WinRate { get; init; }
    public double TotalPnl { get; init; }
    public double ProfitFactor { get; init; }
}
