using System;

namespace 币安量化机器人.Models
{
    public class Order
    {
        public string OrderId { get; set; }
        public string Id
        {
            get => OrderId;
            set => OrderId = value;
        }
        public string Symbol { get; set; }
        public OrderSide Side { get; set; }
        public string Type { get; set; }
        public double Price { get; set; }
        public double Quantity { get; set; }
        public double ExecutedPrice { get; set; }
        public double ExecutedQuantity { get; set; }
        public double Commission { get; set; }
        public OrderStatus Status { get; set; }
        public DateTime CreateTime { get; set; }
        public DateTime UpdateTime { get; set; }
        public double RealizedPnL { get; set; }
        public double RealizedPnl
        {
            get => RealizedPnL;
            set => RealizedPnL = value;
        }

        public Order()
        {
            OrderId = string.Empty;
            Symbol = string.Empty;
            Type = string.Empty;
            Status = OrderStatus.New;
            CreateTime = DateTime.UtcNow;
            UpdateTime = DateTime.UtcNow;
            RealizedPnL = 0;
        }
    }
}
