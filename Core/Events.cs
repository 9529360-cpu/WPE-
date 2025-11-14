using System;

namespace 币安量化机器人.Core
{
    // 统一事件接口，方便日志 / 回放 / 总线
    public interface IDomainEvent
    {
        Guid EventId { get; }
        DateTime OccurredAt { get; }
    }

    // 交易方向枚举
    public enum OrderSide
    {
        Buy,
        Sell
    }

    // 下单类型（简化）
    public enum OrderType
    {
        Market,
        Limit
    }

    // 原始行情消息（用于记录原文/回放）
    public sealed class MarketDataRawMessage : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

        // Legacy alias for older code
        public DateTime ReceivedAt => OccurredAt;

        public string Raw { get; init; } = string.Empty;
        public string? Symbol { get; init; }
        public string? Channel { get; init; }
    }

    // 订单已提交/成交事件（在交易所层面）
    public sealed class OrderPlacedEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

        public string OrderId { get; init; } = string.Empty;
        public string Symbol { get; init; } = string.Empty;
        public decimal Quantity { get; init; }
        public OrderSide Side { get; init; }
        public decimal? Price { get; init; }
        public string? ClientOrderId { get; init; }

        // Legacy helpers
        public double QuantityAsDouble => (double)Quantity;
        public string SideAsString => Side.ToString();
    }

    // 订单被取消事件
    public sealed class OrderCancelledEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

        public string OrderId { get; init; } = string.Empty;
        public string? Reason { get; init; }
    }

    // 策略层发出的下单意图（不代表成交）
    public sealed class OrderRequestEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredAt { get; init; } = DateTime.UtcNow;

        /// <summary>
        /// 客户端自定义幂等 ID，可为空，用于追踪/去重
        /// </summary>
        public string? ClientOrderId { get; init; }

        public string Symbol { get; init; } = string.Empty;
        public OrderSide Side { get; init; } = OrderSide.Buy;
        public decimal Quantity { get; init; }
        public OrderType OrderType { get; init; } = OrderType.Market;
        public decimal? Price { get; init; }
        public bool? ReduceOnly { get; init; }

        // Legacy helpers for older callers expecting double/string fields
        public double QuantityAsDouble => (double)Quantity;
        public string SideAsString => Side.ToString();
    }
}
