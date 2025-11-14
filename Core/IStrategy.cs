using System;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core;

namespace 币安量化机器人.Core
{
    /// <summary>
    /// 策略接口：定义策略的生命周期和事件回调。
    /// </summary>
    public interface IStrategy : IDisposable
    {
        string Name { get; }
        Task InitializeAsync(IServiceProvider services);
        Task OnMarketDataAsync(MarketDataRawMessage message);
        Task OnOrderUpdateAsync(OrderPlacedEvent orderEvent);
        Task StartAsync(CancellationToken token);
        Task StopAsync();
    }
}
