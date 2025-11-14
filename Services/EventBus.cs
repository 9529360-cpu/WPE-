using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace 币安量化机器人.Services
{
    // Implements Core.IEventBus
    public class EventBus : Core.IEventBus
    {
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new ConcurrentDictionary<Type, List<Delegate>>();

        public void Publish<T>(T message)
        {
            if (_handlers.TryGetValue(typeof(T), out var list))
            {
                foreach (var d in list.ToArray())
                {
                    try
                    {
                        ((Action<T>)d)(message);
                    }
                    catch
                    {
                    }
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

            return new Unsubscriber<T>(this, handler);
        }

        private void Unsubscribe<T>(Action<T> handler)
        {
            if (_handlers.TryGetValue(typeof(T), out var list))
            {
                lock (list)
                {
                    list.Remove(handler);
                }
            }
        }

        private class Unsubscriber<T> : IDisposable
        {
            private readonly EventBus _bus;
            private readonly Action<T> _handler;
            private bool _disposed;

            public Unsubscriber(EventBus bus, Action<T> handler)
            {
                _bus = bus;
                _handler = handler;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _bus.Unsubscribe(_handler);
                _disposed = true;
            }
        }
    }
}
