using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using 币安量化机器人.Core;

namespace 币安量化机器人.Services
{
    public class EventBus : IEventBus
    {
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new ConcurrentDictionary<Type, List<Delegate>>();

        public void Publish<T>(T @event)
        {
            if (_handlers.TryGetValue(typeof(T), out var list))
            {
                // 创建副本以避免订阅者在处理时修改集合
                var copy = list.ToArray();
                foreach (Action<T> handler in copy)
                {
                    try { handler(@event); } catch { }
                }
            }
        }

        public IDisposable Subscribe<T>(Action<T> handler)
        {
            var list = _handlers.GetOrAdd(typeof(T), _ => new List<Delegate>());
            lock (list)
            {
                list.Add(handler);
            }

            return new Unsubscriber<T>(_handlers, handler);
        }

        private class Unsubscriber<T> : IDisposable
        {
            private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers;
            private readonly Action<T> _handler;
            private bool _disposed;

            public Unsubscriber(ConcurrentDictionary<Type, List<Delegate>> handlers, Action<T> handler)
            {
                _handlers = handlers;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_disposed) return;
                if (_handlers.TryGetValue(typeof(T), out var list))
                {
                    lock (list)
                    {
                        list.Remove(_handler);
                    }
                }
                _disposed = true;
            }
        }
    }
}
