using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core;

namespace 币安量化机器人.Services
{
    /// <summary>
    /// StrategyHost 提供简单的策略加载与托管能力。
    /// 当前实现支持从已知类型实例化策略或从 DLL 加载策略（约定类型实现 IStrategy）。
    /// </summary>
    public class StrategyHost
    {
        private readonly List<IStrategy> _strategies = new List<IStrategy>();
        private readonly IEventBus _eventBus;

        public StrategyHost(IEventBus eventBus)
        {
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
            // Use synchronous Action<T> handlers that explicitly discard the Task returned by the async helpers
            _eventBus.Subscribe<MarketDataRawMessage>(m => { _ = BroadcastMarketDataAsync(m); });
            _eventBus.Subscribe<OrderPlacedEvent>(e => { _ = BroadcastOrderUpdateAsync(e); });
        }

        public async Task LoadStrategyAsync(IStrategy strategy, IServiceProvider services)
        {
            if (strategy == null)
            {
                throw new ArgumentNullException(nameof(strategy));
            }

            await strategy.InitializeAsync(services);
            _strategies.Add(strategy);
        }

        public async Task LoadStrategyFromAssemblyAsync(string assemblyPath, string typeName, IServiceProvider services)
        {
            if (!File.Exists(assemblyPath))
            {
                throw new FileNotFoundException(assemblyPath);
            }

            var asm = Assembly.LoadFrom(assemblyPath);
            var type = asm.GetType(typeName);
            if (type == null)
            {
                throw new InvalidOperationException("类型未找到: " + typeName);
            }

            if (!typeof(IStrategy).IsAssignableFrom(type))
            {
                throw new InvalidOperationException("类型未实现 IStrategy: " + typeName);
            }

            // Use 'as' cast to avoid converting a possible null to a non-nullable reference
            var strat = Activator.CreateInstance(type) as IStrategy ?? throw new InvalidOperationException($"无法创建策略实例: {typeName}");
            await LoadStrategyAsync(strat, services);
        }

        private async Task BroadcastMarketDataAsync(MarketDataRawMessage m)
        {
            // Snapshot to avoid enumeration issues if strategies are added/removed concurrently
            var snapshot = _strategies.ToArray();
            foreach (var s in snapshot)
            {
                try { await s.OnMarketDataAsync(m); } catch { }
            }
        }

        private async Task BroadcastOrderUpdateAsync(OrderPlacedEvent e)
        {
            var snapshot = _strategies.ToArray();
            foreach (var s in snapshot)
            {
                try { await s.OnOrderUpdateAsync(e); } catch { }
            }
        }

        public async Task StartAllAsync(CancellationToken token)
        {
            var tasks = new List<Task>();
            foreach (var s in _strategies)
            {
                tasks.Add(s.StartAsync(token));
            }
            await Task.WhenAll(tasks);
        }

        public async Task StopAllAsync()
        {
            foreach (var s in _strategies)
            {
                try { await s.StopAsync(); } catch { }
                try { s.Dispose(); } catch { }
            }
            _strategies.Clear();
        }

        // New helper to return simple strategy info for UI
        public IReadOnlyList<(string Name, string Status)> GetStrategyInfos()
        {
            var list = new List<(string, string)>();
            foreach (var s in _strategies)
            {
                // status is not tracked per strategy; default to "Loaded"
                list.Add((s.Name, "Loaded"));
            }
            return list.AsReadOnly();
        }
    }
}
