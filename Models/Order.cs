namespace 币安量化机器人.Models
{
    public class Order
    {
        public string Id { get; set; }
        public string Symbol { get; set; }
        public string Side { get; set; }
        public double Quantity { get; set; }
        public string Status { get; set; }
    }
}
