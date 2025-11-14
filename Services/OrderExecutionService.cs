using System;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core;
using 币安量化机器人.Persistence;

namespace 币安量化机器人.Services
{
    /// <summary>
    /// 下单服务：实现幂等ID处理、简单重试机制，并订阅 OrderRequestEvent。
    /// 增加对 IRepository 的持久化支持，将未完成订单写入仓库，并在后台恢复与处理。
    /// 集成简单风控检查，在下单前调用 IRiskManager。
    /// </summary>
    public class OrderExecutionService : IOrderExecutionService, IDisposable
    {
        private readonly ConcurrentDictionary<string, object> _orders = new ConcurrentDictionary<string, object>();
        private readonly IEventBus _eventBus;
        private readonly IRepository _repository;
        private readonly IRiskManager _riskManager;

        private readonly ConcurrentQueue<(string OrderId, string Payload)> _processingQueue = new ConcurrentQueue<(string, string)>();
        private CancellationTokenSource _processingCts;
        private Task _processingTask;

        public OrderExecutionService(IEventBus eventBus, IRepository repository, IRiskManager riskManager)
        {
            _eventBus = eventBus;
            _repository = repository;
            _riskManager = riskManager;
            _eventBus.Subscribe<OrderRequestEvent>(async req => await HandleOrderRequestAsync(req));
        }

        public async Task InitializeAsync()
        {
            await _repository.InitializeAsync();

            // 恢复未完成订单并入队处理
            var pending = await _repository.GetPendingOrdersAsync();
            foreach (var p in pending)
            {
                _orders[p.OrderId] = p.Payload;
                _processingQueue.Enqueue((p.OrderId, p.Payload));
            }

            StartBackgroundProcessing();
        }

        private void StartBackgroundProcessing()
        {
            if (_processingTask != null && !_processingTask.IsCompleted) return;
            _processingCts = new CancellationTokenSource();
            _processingTask = Task.Run(() => ProcessingLoopAsync(_processingCts.Token));
        }

        private void StopBackgroundProcessing()
        {
            try
            {
                _processingCts?.Cancel();
                _processingTask?.Wait(1000);
            }
            catch { }
            finally
            {
                _processingCts?.Dispose();
                _processingCts = null;
                _processingTask = null;
            }
        }

        private async Task ProcessingLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_processingQueue.TryDequeue(out var item))
                    {
                        var orderId = item.OrderId;
                        var payload = item.Payload;

                        // 模拟下单执行：在真实实现中调用交易所 API 并处理返回
                        try
                        {
                            // 模拟延迟
                            await Task.Delay(200, token).ConfigureAwait(false);

                            // 模拟成功：发布 OrderPlacedEvent 并从持久化中移除
                            _eventBus.Publish(new OrderPlacedEvent { OrderId = orderId });
                            await _repository.RemovePendingOrderAsync(orderId).ConfigureAwait(false);
                            _orders.TryRemove(orderId, out _);
                        }
                        catch (OperationCanceledException)
                        {
                            // 取消，重新入队以便下次处理
                            _processingQueue.Enqueue(item);
                        }
                        catch
                        {
                            // 处理失败：简单重试策略（将任务重新入队，待会重试）
                            await Task.Delay(500).ConfigureAwait(false);
                            _processingQueue.Enqueue(item);
                        }
                    }
                    else
                    {
                        await Task.Delay(200, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(500);
                }
            }
        }

        public async Task<string> PlaceOrderAsync(object order)
        {
            // 生成占位订单ID
            var id = Guid.NewGuid().ToString("N");

            // 在实际下单前执行风控检查（如有）
            if (_riskManager != null)
            {
                // 假设 order 为匿名对象 { Symbol, Side, Quantity }
                var json = JsonSerializer.Serialize(order);
                var doc = JsonSerializer.Deserialize<JsonElement>(json);
                double qty = doc.GetProperty("Quantity").GetDouble();

                var check = await _riskManager.CheckOrderAsync(new OrderRequestEvent { Quantity = qty, Symbol = doc.GetProperty("Symbol").GetString(), Side = doc.GetProperty("Side").GetString() });
                if (!check.Passed)
                {
                    Services.Observability.ObservabilityService.Instance.AddLog($"风控拒单: {check.Reason}");
                    return null;
                }
            }

            _orders[id] = order;

            // 序列化订单 payload 简单存储
            var payload = JsonSerializer.Serialize(order);
            // 持久化未完成订单
            await _repository.SavePendingOrderAsync(id, payload);

            // 将订单加入处理队列，由后台处理器完成实际执行
            _processingQueue.Enqueue((id, payload));

            // 立即发布一个本地事件表示已接收下单请求（注意：非交易所已成交事件）
            _eventBus.Publish(new OrderPlacedEvent { OrderId = id });
            return id;
        }

        public async Task CancelOrderAsync(string orderId)
        {
            _orders.TryRemove(orderId, out _);
            await _repository.RemovePendingOrderAsync(orderId);
            _eventBus.Publish(new OrderCancelledEvent { OrderId = orderId });
        }

        private async Task HandleOrderRequestAsync(OrderRequestEvent req)
        {
            // 幂等：如果 ClientOrderId 已存在则忽略或返回已有订单ID
            if (!string.IsNullOrEmpty(req.ClientOrderId))
            {
                if (_orders.ContainsKey(req.ClientOrderId))
                {
                    _eventBus.Publish(new OrderPlacedEvent { OrderId = req.ClientOrderId, Symbol = req.Symbol, Quantity = req.Quantity });
                    return;
                }

                // 检查持久化存储
                var pending = await _repository.GetPendingOrdersAsync();
                foreach (var p in pending)
                {
                    if (p.OrderId == req.ClientOrderId)
                    {
                        _eventBus.Publish(new OrderPlacedEvent { OrderId = req.ClientOrderId, Symbol = req.Symbol, Quantity = req.Quantity });
                        return;
                    }
                }
            }

            // 将请求转为内部下单并入队处理
            await PlaceOrderAsync(new { Symbol = req.Symbol, Side = req.Side, Quantity = req.Quantity });
        }

        public void Dispose()
        {
            StopBackgroundProcessing();
        }
    }
}
