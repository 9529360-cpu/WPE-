using System;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core;

namespace 币安量化机器人.Services
{
    public class ExampleStrategy : IStrategy
    {
        public string Name => "ExampleStrategy";

        private IEventBus? _eventBus;

        public async Task InitializeAsync(IServiceProvider services)
        {
            _eventBus = services.GetService(typeof(IEventBus)) as IEventBus;
            if (_eventBus == null)
            {
                LogService.Warning("[ExampleStrategy] IEventBus not available during Initialize");
            }
            await Task.CompletedTask;
        }

        public Task OnMarketDataAsync(MarketDataRawMessage message)
        {
            // 简单示例：收到行情后可能发起下单请求
            // 这里不自动下单，仅作为示例
            return Task.CompletedTask;
        }

        public Task OnOrderUpdateAsync(OrderPlacedEvent orderEvent)
        {
            // 处理下单回报（示例）
            return Task.CompletedTask;
        }

        public Task StartAsync(CancellationToken token)
        {
            // 启动逻辑（示例）
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
