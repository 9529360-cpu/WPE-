using System;

namespace 币安量化机器人.Core
{
    public class MarketDataRawMessage
    {
        public string Raw { get; set; }
        public DateTime ReceivedAt { get; set; }
    }

    public class OrderPlacedEvent
    {
        public string OrderId { get; set; }
        public string Symbol { get; set; }
        public double Quantity { get; set; }
    }

    public class OrderCancelledEvent
    {
        public string OrderId { get; set; }
    }

    // 下单请求事件，策略发布此事件请求下单，OrderExecutionService 订阅并执行下单。
    public class OrderRequestEvent
    {
        public string ClientOrderId { get; set; } // 客户端自定义幂等 ID，可为空
        public string Symbol { get; set; }
        public string Side { get; set; }
        public double Quantity { get; set; }
    }
}
