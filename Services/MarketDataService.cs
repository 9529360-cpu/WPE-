using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core;

namespace 币安量化机器人.Services
{
    /// <summary>
    /// 简单的行情订阅服务骨架。提供 WebSocket 连接、接收循环与自动重连。
    /// 目前为本地模拟/占位实现；后续可替换为真实交易所地址与订阅逻辑。
    /// </summary>
    public class MarketDataService : IMarketDataService
    {
        public event Action<string> RawMessageReceived;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly Uri _endpoint;
        private readonly int _reconnectDelayMs = 3000;
        private bool _running;

        public MarketDataService() : this(new Uri("wss://stream.binance.com:9443/ws/!ticker@arr"))
        {
        }

        public MarketDataService(Uri endpoint)
        {
            _endpoint = endpoint;
        }

        public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            if (_running) return Task.CompletedTask;
            _running = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            Task.Run(() => RunAsync(_cts.Token));
            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            _running = false;
            try
            {
                _cts?.Cancel();
                if (_ws != null)
                {
                    if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", CancellationToken.None).ConfigureAwait(false);
                    }
                    _ws.Dispose();
                    _ws = null;
                }
            }
            catch
            {
                // 忽略关闭时的异常
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (_running && !token.IsCancellationRequested)
            {
                try
                {
                    _ws?.Dispose();
                    _ws = new ClientWebSocket();
                    await _ws.ConnectAsync(_endpoint, token).ConfigureAwait(false);

                    // 接收循环
                    var buffer = new byte[8192];
                    while (_ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                    {
                        var seg = new ArraySegment<byte>(buffer);
                        var result = await _ws.ReceiveAsync(seg, token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "close", CancellationToken.None).ConfigureAwait(false);
                            break;
                        }

                        int count = result.Count;
                        while (!result.EndOfMessage)
                        {
                            if (count >= buffer.Length)
                            {
                                // 消息太大，丢弃
                                break;
                            }
                            seg = new ArraySegment<byte>(buffer, count, buffer.Length - count);
                            result = await _ws.ReceiveAsync(seg, token).ConfigureAwait(false);
                            count += result.Count;
                        }

                        var message = Encoding.UTF8.GetString(buffer, 0, count);
                        try
                        {
                            RawMessageReceived?.Invoke(message);
                        }
                        catch
                        {
                            // UI/上层处理异常不应影响接收循环
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break; // 取消请求
                }
                catch
                {
                    // 连接或接收错误，等待后重连
                }

                if (!_running || token.IsCancellationRequested) break;
                await Task.Delay(_reconnectDelayMs, token).ContinueWith(_ => { });
            }

            // 清理
            try
            {
                _ws?.Dispose();
                _ws = null;
            }
            catch { }
        }
    }
}
