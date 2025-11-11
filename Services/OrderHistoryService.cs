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
            Price = response.Price > 0 ? (double)response.Price : null,
            Status = response.Status,
            FilledQuantity = (double)response.ExecutedQuantity,
            AvgFillPrice = response.AvgPrice > 0 ? (double)response.AvgPrice : null,
            Commission = 0, // 需要从trades API获取
            StrategyName = strategyName,
            CreatedAt = response.Time,
            UpdatedAt = response.Time,
            FilledAt = response.ExecutedQuantity > 0 ? response.Time : null
        };

        await _cache.SaveOrderAsync(record);
    }

    /// <summary>
    /// 更新订单状态(成交/取消)
    /// </summary>
    public async Task UpdateOrderStatusAsync(string orderId, string status, double filledQty, double? avgPrice, DateTime updateTime)
    {
        // 先查询现有订单
        IReadOnlyList<OrderHistoryRecord> existing = await _cache.LoadOrdersAsync(limit: 1); // 简化版,实际需按orderId查询
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

        await _cache.SaveOrderAsync(updated);
    }

    /// <summary>
    /// 获取订单历史(带过滤)
    /// </summary>
    public async Task<IReadOnlyList<OrderHistoryRecord>> GetOrderHistoryAsync(string? symbol = null, int limit = 100)
    {
        return await _cache.LoadOrdersAsync(symbol, limit);
    }

    /// <summary>
    /// 获取策略统计
    /// </summary>
    public async Task<StrategyStats> GetStrategyStatsAsync(string strategyName, string? symbol = null)
    {
        IReadOnlyList<OrderHistoryRecord> orders = await _cache.LoadOrdersAsync(symbol, 1000);
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
