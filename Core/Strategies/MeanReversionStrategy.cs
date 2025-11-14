using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;

namespace 币安量化机器人.Core.Strategies;

public class MeanReversionStrategy : ITradingStrategy
{
    // Optional dependencies kept for compatibility with legacy constructors
    private readonly IMultiTimeframeAnalyzer? _analyzer;
    private readonly object? _mlGen;
    private readonly IFeatureStore? _featureStore;

    private IStrategyContext? _context;
    private readonly StrategyParameters _parameters;

    // New minimal constructor used in current codebase
    public MeanReversionStrategy(StrategyParameters parameters)
    {
        _parameters = parameters;
    }

    // Backwards-compatible constructor expected by callers in other parts of the solution
    public MeanReversionStrategy(object analyzer, object mlGen, object featureStore, StrategyParameters parameters)
    {
        // Store references for potential future use; avoid forcing concrete types here to preserve compatibility
        _analyzer = analyzer as IMultiTimeframeAnalyzer;
        _mlGen = mlGen;
        _featureStore = featureStore as IFeatureStore;
        _parameters = parameters;
    }

    public string Name => "MeanReversion";

    public StrategyParameters Parameters => _parameters;

    public void Initialize(IStrategyContext context)
    {
        _context = context;
    }

    public async ValueTask<StrategyDecision> EvaluateAsync(MarketObservation observation, CancellationToken cancellationToken = default)
    {
        if (_context is null)
        {
            throw new InvalidOperationException("Strategy not initialized");
        }

        double meanWindow = _parameters.Get("mean_window", 20);
        var closes = new List<double> { observation.Close };
        double mean = closes.Average();
        double std = 0;
        if (closes.Count > 1)
        {
            double avg = mean;
            std = Math.Sqrt(closes.Average(v => Math.Pow(v - avg, 2)));
        }

        TradeActionType action = TradeActionType.Hold;
        double qty = _parameters.Get("base_quantity", 1d);
        string reason = "No signal";

        if (observation.Close < mean - _parameters.Get("entry_z", 1.5) * std)
        {
            action = TradeActionType.EnterLong;
            reason = "Reversion bottom";
        }
        else if (observation.Close > mean + _parameters.Get("entry_z", 1.5) * std)
        {
            action = TradeActionType.EnterShort;
            reason = "Reversion top";
        }

        var tradeAction = new TradeAction(action, qty, reason);

        // Map to risk domain
        var riskAction = new 币安量化机器人.Core.Risk.TradeAction
        {
            ActionType = action == TradeActionType.EnterLong ? 币安量化机器人.Core.Risk.TradeActionType.Buy : (action == TradeActionType.EnterShort ? 币安量化机器人.Core.Risk.TradeActionType.Sell : 币安量化机器人.Core.Risk.TradeActionType.Hold),
            Quantity = qty
        };

        if (!_context.RiskManager.Approve(riskAction))
        {
            tradeAction = new TradeAction(TradeActionType.Hold, 0, "Risk rejected");
        }

        var decision = new StrategyDecision(tradeAction, 0.5, new CompositeSignal(observation.Symbol, 0, 0, 0, new Dictionary<TimeSpan, double>()), new MachineLearningSignal(observation.Symbol, 0.5, 0.5, "N/A"));
        await _context.PublishSignalAsync(new TradeSignal(observation.Symbol, tradeAction, decision.Confidence, decision.MlSignal), cancellationToken);
        return decision;
    }

    public async IAsyncEnumerable<StrategyDecision> RunAsync(IAsyncEnumerable<MarketObservation> observations, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var o in observations.WithCancellation(cancellationToken))
        {
            yield return await EvaluateAsync(o, cancellationToken);
        }
    }
}
