using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace 币安量化机器人.Services
{
    // Implements Core.IEventBus (renamed to avoid conflict with AI EventBus)
    public class LegacyEventBus : Core.IEventBus
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

// Add asynchronous publish compatibility for callers using PublishAsync
        public Task PublishAsync<T>(T message)
        {
            // Invoke handlers on threadpool to avoid blocking caller
            return Task.Run(() => Publish(message));
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
            private readonly LegacyEventBus _bus;
            private readonly Action<T> _handler;
            private bool _disposed;

public Unsubscriber(LegacyEventBus bus, Action<T> handler)
            {
                _bus = bus;
                _handler = handler;
            }

public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }
                _bus.Unsubscribe(_handler);
                _disposed = true;
            }
        }
    }
}
