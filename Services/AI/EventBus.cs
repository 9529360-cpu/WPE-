using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 事件总线
/// </summary>
/// <remarks>
/// 核心职责：
/// 1. 模块间通信
/// 2. 事件发布和订阅
/// 3. 异步事件处理
/// 4. 事件历史记录
/// </remarks>
public class EventBus : IDisposable
{
    private readonly ConcurrentDictionary<Type, List<Func<object, Task>>> _subscribers;
    private readonly ConcurrentQueue<EventRecord> _eventHistory;
    private const int MaxHistorySize = 500;

    public EventBus()
    {
        _subscribers = new ConcurrentDictionary<Type, List<Func<object, Task>>>();
        _eventHistory = new ConcurrentQueue<EventRecord>();
    }

    /// <summary>
    /// 订阅事件，返回 IDisposable 用于取消订阅
    /// </summary>
    public IDisposable Subscribe<T>(Func<T, Task> handler) where T : class
    {
        Type eventType = typeof(T);

        Func<object, Task> wrapper = evt => handler((T)evt);

        _subscribers.AddOrUpdate(
            eventType,
            _ => new List<Func<object, Task>> { wrapper },
            (_, list) =>
            {
                list.Add(wrapper);
                return list;
            });

        LogService.Debug("[EventBus] 订阅事件: {EventType}", eventType.Name);

        return new Subscription(this, eventType, wrapper);
    }

    private void Unsubscribe(Type eventType, Func<object, Task> wrapper)
    {
        if (_subscribers.TryGetValue(eventType, out var list))
        {
            lock (list)
            {
                list.RemoveAll(d => d == wrapper);
            }
            LogService.Debug("[EventBus] 取消订阅: {EventType}", eventType.Name);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly EventBus _bus;
        private readonly Type _eventType;
        private readonly Func<object, Task> _wrapper;
        private bool _disposed;

        public Subscription(EventBus bus, Type eventType, Func<object, Task> wrapper)
        {
            _bus = bus;
            _eventType = eventType;
            _wrapper = wrapper;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _bus.Unsubscribe(_eventType, _wrapper);
        }
    }

    /// <summary>
    /// 发布事件
    /// </summary>
    public async Task PublishAsync<T>(T eventData) where T : class
    {
        Type eventType = typeof(T);

        // 记录事件
        RecordEvent(eventType, eventData);

        // 通知订阅者
        if (_subscribers.TryGetValue(eventType, out List<Func<object, Task>>? handlers))
        {
            LogService.Debug("[EventBus] 发布事件: {EventType}, 订阅者数={Count}",
                eventType.Name, handlers.Count);

            List<Task> tasks = new List<Task>();
            // make a snapshot to avoid collection modification issues
            Func<object, Task>[] snapshot;
            lock (handlers)
            {
                snapshot = handlers.ToArray();
            }

            foreach (var handler in snapshot)
            {
                try
                {
                    tasks.Add(handler(eventData));
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "[EventBus] 事件处理器异常: {EventType}", eventType.Name);
                }
            }

            await Task.WhenAll(tasks);
        }
        else
        {
            LogService.Debug("[EventBus] 发布事件: {EventType}, 无订阅者", eventType.Name);
        }
    }

    /// <summary>
    /// 记录事件到历史
    /// </summary>
    private void RecordEvent(Type eventType, object eventData)
    {
        var record = new EventRecord
        {
            EventType = eventType.Name,
            Timestamp = DateTime.UtcNow,
            Data = eventData
        };

        _eventHistory.Enqueue(record);

        // 限制历史大小
        while (_eventHistory.Count > MaxHistorySize)
        {
            _eventHistory.TryDequeue(out _);
        }
    }

    /// <summary>
    /// 获取事件历史
    /// </summary>
    public List<EventRecord> GetHistory(int count = 50)
    {
        return _eventHistory
            .TakeLast(count)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <summary>
    /// 获取特定类型的事件历史
    /// </summary>
    public List<EventRecord> GetHistoryByType(string eventType, int count = 50)
    {
        return _eventHistory
            .Where(e => e.EventType == eventType)
            .TakeLast(count)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <summary>
    /// 清空订阅
    /// </summary>
    public void ClearSubscriptions()
    {
        _subscribers.Clear();
        LogService.Info("[EventBus] 已清空所有订阅");
    }

    public void Dispose()
    {
        ClearSubscriptions();
    }
}

/// <summary>
/// 事件记录
/// </summary>
public class EventRecord
{
    public required string EventType { get; init; }
    public DateTime Timestamp { get; init; }
    public required object Data { get; init; }
}
