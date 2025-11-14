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
}
