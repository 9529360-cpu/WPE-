using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Core.Risk;
using 币安量化机器人.Monitoring;

namespace 币安量化机器人.Application.Services;

public class StrategyOrchestrator
{
    private readonly IMarketDataService _marketDataService;
    private readonly IRiskManager _riskManager;
    private readonly IFeatureStore _featureStore;
    private readonly ITradeMonitoringHub _monitoringHub;

    public StrategyOrchestrator(
        IMarketDataService marketDataService,
        IRiskManager riskManager,
        IFeatureStore featureStore,
        ITradeMonitoringHub monitoringHub)
    {
        _marketDataService = marketDataService;
        _riskManager = riskManager;
        _featureStore = featureStore;
        _monitoringHub = monitoringHub;
    }

    public async Task RunAsync(ITradingStrategy strategy, string symbol, IEnumerable<TimeSpan> timeframes, CancellationToken cancellationToken = default)
    {
        var context = new StrategyContext(symbol, _marketDataService, _riskManager, _featureStore, PublishSignalAsync, RecordMetricsAsync);
        strategy.Initialize(context);

        await foreach (var observation in _marketDataService.StreamAsync(symbol, timeframes, cancellationToken))
        {
            var decision = await strategy.EvaluateAsync(observation, cancellationToken);
            await _monitoringHub.BroadcastSignalAsync(new TradeSignal(symbol, decision.Action, decision.Confidence, decision.MlSignal), cancellationToken);
        }
    }

    private ValueTask PublishSignalAsync(TradeSignal signal, CancellationToken cancellationToken)
        => _monitoringHub.BroadcastSignalAsync(signal, cancellationToken);

    private ValueTask RecordMetricsAsync(StrategyPerformanceSnapshot snapshot, CancellationToken cancellationToken)
        => _monitoringHub.BroadcastMetricsAsync(snapshot, cancellationToken);
}
