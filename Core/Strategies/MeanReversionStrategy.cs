using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;

namespace 币安量化机器人.Core.Strategies;

public class MeanReversionStrategy : ITradingStrategy
{
    private readonly IMultiTimeframeAnalyzer _analyzer;
    private readonly IMachineLearningSignalGenerator _mlSignalGenerator;
    private readonly IFeatureStore _featureStore;
    private readonly StrategyParameters _parameters;
    private IStrategyContext? _context;

    public MeanReversionStrategy(IMultiTimeframeAnalyzer analyzer, IMachineLearningSignalGenerator mlSignalGenerator, IFeatureStore featureStore, StrategyParameters parameters)
    {
        _analyzer = analyzer;
        _mlSignalGenerator = mlSignalGenerator;
        _featureStore = featureStore;
        _parameters = parameters;
    }

    public string Name => "MeanReversion+Momentum";

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

        IReadOnlyDictionary<string, double> features = await _featureStore.GetLatestAsync(observation.Symbol, cancellationToken);
        var featureVector = new List<double>(observation.Indicators.Values);
        foreach (double value in features.Values)
        {
            featureVector.Add(value);
        }

        MachineLearningSignal mlSignal = await _mlSignalGenerator.PredictAsync(new ModelFeatureVector(observation.Symbol, observation.Timestamp, featureVector, 0), cancellationToken);

        CompositeSignal signal = await _analyzer.AnalyzeAsync(observation.Symbol, new Dictionary<TimeSpan, TimeframeSeries>
        {
            [observation.Timeframe] = new TimeframeSeries(observation.Timeframe, new List<MarketObservation> { observation })
        }, cancellationToken);

        double zScore = CalculateZScore(observation);
        double threshold = _parameters.Get("entry_z_score", 1.5);
        double quantity = _parameters.Get("base_quantity", 1);
        double stopMultiplier = _parameters.Get("stop_multiplier", 2);

        TradeActionType action = TradeActionType.Hold;
        string reason = "Hold";

        if (Math.Abs(zScore) > threshold)
        {
            action = zScore > 0 ? TradeActionType.EnterShort : TradeActionType.EnterLong;
            reason = $"Z-Score {zScore:F2} exceeded {threshold:F2}";
        }

        if (Math.Abs(signal.MomentumScore) > Math.Abs(zScore))
        {
            action = signal.MomentumScore > 0 ? TradeActionType.EnterLong : TradeActionType.EnterShort;
            reason = $"Momentum override {signal.MomentumScore:F2}";
        }

        var tradeAction = new TradeAction(action, quantity, reason);
        if (!_context.RiskManager.Approve(tradeAction))
        {
            tradeAction = new TradeAction(TradeActionType.Hold, 0, "Risk rejected");
        }

        var decision = new StrategyDecision(tradeAction, Math.Min(1, Math.Abs(zScore) / (threshold * stopMultiplier)), signal, mlSignal);
        await _context.PublishSignalAsync(new TradeSignal(observation.Symbol, tradeAction, decision.Confidence, mlSignal), cancellationToken);
        return decision;
    }

    public async IAsyncEnumerable<StrategyDecision> RunAsync(IAsyncEnumerable<MarketObservation> observations, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (MarketObservation? observation in observations.WithCancellation(cancellationToken))
        {
            yield return await EvaluateAsync(observation, cancellationToken);
        }
    }

    private double CalculateZScore(MarketObservation observation)
    {
        if (!observation.Indicators.TryGetValue("sma", out double sma) || !observation.Indicators.TryGetValue("std", out double std) || std == 0)
        {
            return 0d;
        }

        return (observation.Close - sma) / std;
    }
}
