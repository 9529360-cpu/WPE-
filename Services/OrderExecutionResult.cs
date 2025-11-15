namespace 币安量化机器人.Services
{
    public sealed class OrderExecutionResult
    {
        public bool IsSuccess { get; init; }
        public string OrderId { get; init; } = string.Empty;
        public string? ErrorMessage { get; init; }
        public string? Error => ErrorMessage; // compatibility alias
        public double ExecutedPrice { get; init; }
        public double ExecutedQuantity { get; init; }
        public double Commission { get; init; }

        public static OrderExecutionResult Success(string orderId, double price, double qty, double commission)
            => new OrderExecutionResult { IsSuccess = true, OrderId = orderId, ExecutedPrice = price, ExecutedQuantity = qty, Commission = commission };

        public static OrderExecutionResult Failed(string error)
            => new OrderExecutionResult { IsSuccess = false, ErrorMessage = error };

        // Compatibility methods used across the codebase
        public static OrderExecutionResult Rejected(string reason) => Failed(reason);
    }
}
