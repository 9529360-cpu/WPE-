using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;

namespace 币安量化机器人.Application.Backtesting;

public class WalkForwardOptimizer
{
    private readonly IStrategyOptimizer _optimizer;
    private readonly IBacktestEngine _backtestEngine;

    public WalkForwardOptimizer(IStrategyOptimizer optimizer, IBacktestEngine backtestEngine)
    {
        _optimizer = optimizer;
        _backtestEngine = backtestEngine;
    }

    public async IAsyncEnumerable<WalkForwardResult> OptimizeAsync(
        ITradingStrategy strategy,
        string symbol,
        DateTime start,
        DateTime end,
        TimeSpan trainingWindow,
        TimeSpan testingWindow,
        IReadOnlyDictionary<string, IReadOnlyList<double>> parameterSpace,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        DateTime trainingStart = start;
        while (trainingStart < end)
        {
            DateTime trainingEnd = trainingStart + trainingWindow;
            DateTime testingEnd = trainingEnd + testingWindow;
            if (trainingEnd >= end)
            {
                yield break;
            }

            var optimizationRequest = new OptimizationRequest(symbol, trainingStart, trainingEnd, parameterSpace, _backtestEngine);
            ValueTask<OptimizationResult> optimizationTask = _optimizer.OptimizeAsync(strategy, optimizationRequest, cancellationToken);

            await foreach (OptimizationProgress progress in _optimizer.StreamProgressAsync(cancellationToken))
            {
                yield return new WalkForwardResult(trainingStart, trainingEnd, null, progress, null);
            }

            OptimizationResult optimizationResult = await optimizationTask;

            ITradingStrategy walkForwardStrategy = CloneStrategy(strategy, optimizationResult.Parameters);
            var testRequest = new BacktestRequest(symbol, trainingEnd, testingEnd, walkForwardStrategy);
            BacktestResult testResult = await _backtestEngine.RunAsync(testRequest, cancellationToken);

            yield return new WalkForwardResult(trainingStart, trainingEnd, optimizationResult, null, testResult);

            trainingStart = testingEnd;
        }
    }

    private static ITradingStrategy CloneStrategy(ITradingStrategy strategy, StrategyParameters parameters)
    {
        return strategy switch
        {
            Core.Strategies.MeanReversionStrategy meanReversion => new Core.Strategies.MeanReversionStrategy(GetMeanReversionAnalyzer(meanReversion), GetMlGenerator(meanReversion), GetFeatureStore(meanReversion), parameters),
            Core.Strategies.MomentumStrategy momentum => new Core.Strategies.MomentumStrategy(GetMomentumAnalyzer(momentum), parameters),
            _ => throw new NotSupportedException("Unsupported strategy type")
        };

        static IMultiTimeframeAnalyzer GetMeanReversionAnalyzer(Core.Strategies.MeanReversionStrategy strategy)
            => strategy.GetType().GetField("_analyzer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(strategy) as IMultiTimeframeAnalyzer
               ?? throw new InvalidOperationException("Analyzer unavailable");

        static IMachineLearningSignalGenerator GetMlGenerator(Core.Strategies.MeanReversionStrategy strategy)
            => strategy.GetType().GetField("_mlSignalGenerator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(strategy) as IMachineLearningSignalGenerator
               ?? throw new InvalidOperationException("ML generator unavailable");

        static IFeatureStore GetFeatureStore(Core.Strategies.MeanReversionStrategy strategy)
            => strategy.GetType().GetField("_featureStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(strategy) as IFeatureStore
               ?? throw new InvalidOperationException("Feature store unavailable");

        static IMultiTimeframeAnalyzer GetMomentumAnalyzer(Core.Strategies.MomentumStrategy strategy)
            => strategy.GetType().GetField("_analyzer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(strategy) as IMultiTimeframeAnalyzer
               ?? throw new InvalidOperationException("Analyzer unavailable");
    }
}

public record WalkForwardResult(
    DateTime TrainingStart,
    DateTime TrainingEnd,
    OptimizationResult? Optimization,
    OptimizationProgress? Progress,
    BacktestResult? WalkForwardTestResult);
