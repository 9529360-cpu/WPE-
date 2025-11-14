using System;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Core
{
    public interface IMarketDataService
    {
        event Action<string> RawMessageReceived;

        Task StartAsync(CancellationToken cancellationToken = default);
        Task StopAsync();
        bool IsConnected { get; }
    }
}
