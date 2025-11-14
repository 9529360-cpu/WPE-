using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using 币安量化机器人.Core;
using 币安量化机器人.Persistence;

namespace 币安量化机器人.Services
{
    /// <summary>
    /// 下单服务：实现幂等ID处理、简单重试机制，并订阅 OrderRequestEvent。
    /// 增加对 IRepository 的持久化支持，将未完成订单写入仓库。
    /// </summary>
    public class OrderExecutionService : IOrderExecutionService
    {
        private readonly ConcurrentDictionary<string, object> _orders = new ConcurrentDictionary<string, object>();
        private readonly IEventBus _eventBus;
        private readonly IRepository _repository;

        public OrderExecutionService(IEventBus eventBus, IRepository repository)
        {
            _eventBus = eventBus;
            _repository = repository;
            _eventBus.Subscribe<OrderRequestEvent>(async req => await HandleOrderRequestAsync(req));
        }

        public async Task InitializeAsync()
        {
            await _repository.InitializeAsync();

            // 恢复未完成订单
            var pending = await _repository.GetPendingOrdersAsync();
            foreach (var p in pending)
            {
                _orders[p.OrderId] = p.Payload;
                // TODO: 将恢复的订单进入处理队列
            }
        }

        public Task<string> PlaceOrderAsync(object order)
        {
            // 生成占位订单ID
            var id = Guid.NewGuid().ToString("N");
            _orders[id] = order;
            // 持久化未完成订单
            _repository.SavePendingOrderAsync(id, order.ToString());
            // 发布 OrderPlacedEvent
            _eventBus.Publish(new OrderPlacedEvent { OrderId = id });
            return Task.FromResult(id);
        }

        public Task CancelOrderAsync(string orderId)
        {
            _orders.TryRemove(orderId, out _);
            _repository.RemovePendingOrderAsync(orderId);
            _eventBus.Publish(new OrderCancelledEvent { OrderId = orderId });
            return Task.CompletedTask;
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

            // 简单重试示例：最多 3 次
            int attempt = 0;
            while (attempt < 3)
            {
                attempt++;
                try
                {
                    // TODO: 调用交易所下单逻辑
                    var id = await PlaceOrderAsync(new { Symbol = req.Symbol, Side = req.Side, Quantity = req.Quantity });
                    // 记录幂等ID映射
                    if (!string.IsNullOrEmpty(req.ClientOrderId))
                    {
                        _orders[req.ClientOrderId] = _orders[id];
                    }
                    return;
                }
                catch
                {
                    await Task.Delay(200 * attempt);
                }
            }

            // 如果重试失败，记录或告警（TODO）
        }
    }
}
