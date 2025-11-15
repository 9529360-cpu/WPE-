using System;
using System.Threading.Tasks;
using 币安量化机器人.Models;

namespace 币安量化机器人.Services
{
    // Lightweight adapter to expose SimulatedOrderExecutor via IOrderExecutionService
    public class SimpleOrderExecutionService : IOrderExecutionService
    {
        private readonly SimulatedOrderExecutor _simulator;
        private readonly DataCacheService _cache;

        public SimpleOrderExecutionService(SimulatedOrderExecutor simulator, DataCacheService cache)
        {
            _simulator = simulator ?? throw new ArgumentNullException(nameof(simulator));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public Task InitializeAsync()
        {
            // No initialization required for simulator
            return Task.CompletedTask;
        }

        public async Task<string> PlaceOrderAsync(object order)
        {
            if (order is OrderRequest req)
            {
                // Always use a local simulated TradingAccount for the simple simulator.
                // Avoid trying to map stored AccountProfile to TradingAccount to keep this adapter simple.
                var acct = new TradingAccount(AccountType.Simulated, "local-sim", 10000m);

                var result = await _simulator.ExecuteAsync(req, acct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    return result.OrderId;
                }

                throw new InvalidOperationException(result.ErrorMessage ?? "Order failed");
            }

            throw new ArgumentException("Unsupported order type", nameof(order));
        }

        public Task CancelOrderAsync(string orderId)
        {
            // Simulator doesn't support cancel; just log
            LogService.Info("SimpleOrderExecutionService.CancelOrderAsync invoked for {OrderId}", orderId);
            return Task.CompletedTask;
        }
    }
}
