using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using 币安量化机器人.Core;

namespace 币安量化机器人.Services
{
    /// <summary>
    /// 下单服务：实现幂等ID处理、简单重试机制，并订阅 OrderRequestEvent。
    /// 当前为进程内实现，后续应增加持久化与真实交易所适配。
    /// </summary>
    public class OrderExecutionService : IOrderExecutionService
    {
        private readonly ConcurrentDictionary<string, object> _orders = new ConcurrentDictionary<string, object>();
        private readonly IEventBus _eventBus;

        public OrderExecutionService(IEventBus eventBus)
        {
            _eventBus = eventBus;
            _eventBus.Subscribe<OrderRequestEvent>(async req => await HandleOrderRequestAsync(req));
        }

        public Task<string> PlaceOrderAsync(object order)
        {
            // 生成占位订单ID
            var id = Guid.NewGuid().ToString("N");
            _orders[id] = order;
            // 发布 OrderPlacedEvent
            _eventBus.Publish(new OrderPlacedEvent { OrderId = id });
            return Task.FromResult(id);
        }

        public Task CancelOrderAsync(string orderId)
        {
            _orders.TryRemove(orderId, out _);
            _eventBus.Publish(new OrderCancelledEvent { OrderId = orderId });
            return Task.CompletedTask;
        }

        private async Task HandleOrderRequestAsync(OrderRequestEvent req)
        {
            // 幂等：如果 ClientOrderId 已存在则忽略或返回已有订单ID
            if (!string.IsNullOrEmpty(req.ClientOrderId))
            {
                foreach (var kvp in _orders)
                {
                    if (kvp.Key == req.ClientOrderId)
                    {
                        // 已存在的幂等ID，发布已下单事件
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
