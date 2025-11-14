using System;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core;

namespace 币安量化机器人.Services
{
    public class MarketDataService : IMarketDataService
    {
        public event Action<string> RawMessageReceived;

        private bool _isConnected;

        public bool IsConnected => _isConnected;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            _isConnected = true;
            // TODO: 实现 WebSocket 连接与订阅逻辑
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            _isConnected = false;
            // TODO: 断开连接
            return Task.CompletedTask;
        }
    }
}
