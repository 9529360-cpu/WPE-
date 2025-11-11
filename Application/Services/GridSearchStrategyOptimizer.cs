using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;

namespace 币安量化机器人.Application.Services;

public class GridSearchStrategyOptimizer : IStrategyOptimizer
{
    private readonly Channel<OptimizationProgress> _progressChannel = Channel.CreateUnbounded<OptimizationProgress>();

    public async ValueTask<OptimizationResult> OptimizeAsync(ITradingStrategy strategy, OptimizationRequest request, CancellationToken cancellationToken = default)
    {
        var candidates = new ConcurrentBag<OptimizationCandidate>();
        var parameterCombinations = GenerateCombinations(request.ParameterSpace).ToList();
        int total = parameterCombinations.Count;
        int completed = 0;

        await Parallel.ForEachAsync(parameterCombinations, cancellationToken, async (parameters, token) =>
        {
            var strategyParameters = new StrategyParameters(parameters);
            var clone = CloneStrategy(strategy, strategyParameters);
            var backtestResult = await request.BacktestEngine.RunAsync(new BacktestRequest(request.Symbol, request.TrainingStart, request.TrainingEnd, clone), token);
            var candidate = new OptimizationCandidate(strategyParameters, backtestResult);
            candidates.Add(candidate);
            double score = backtestResult.Sharpe;
            Interlocked.Increment(ref completed);
            await _progressChannel.Writer.WriteAsync(new OptimizationProgress(strategyParameters, score, completed, total), token);
        });

        _progressChannel.Writer.Complete();
        var best = candidates.OrderByDescending(c => c.Result.Sharpe).First();
        return new OptimizationResult(best.Parameters, best.Result, candidates.OrderByDescending(c => c.Result.Sharpe).ToList());
    }

    public async IAsyncEnumerable<OptimizationProgress> StreamProgressAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (await _progressChannel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_progressChannel.Reader.TryRead(out var progress))
            {
                yield return progress;
            }
        }
    }

    private static ITradingStrategy CloneStrategy(ITradingStrategy strategy, StrategyParameters parameters)
    {
        return strategy switch
        {
            Core.Strategies.MeanReversionStrategy meanReversion => new Core.Strategies.MeanReversionStrategy(meanReversionAnalyzer(meanReversion), meanReversionMl(meanReversion), meanReversionFeatureStore(meanReversion), parameters),
            Core.Strategies.MomentumStrategy momentum => new Core.Strategies.MomentumStrategy(momentumAnalyzer(momentum), parameters),
            _ => throw new NotSupportedException($"Strategy {strategy.Name} cloning not supported")
        };

        static IMultiTimeframeAnalyzer meanReversionAnalyzer(Core.Strategies.MeanReversionStrategy strategy)
            => strategy.GetType().GetField("_analyzer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(strategy) as IMultiTimeframeAnalyzer
               ?? throw new InvalidOperationException("Analyzer unavailable");

        static IMachineLearningSignalGenerator meanReversionMl(Core.Strategies.MeanReversionStrategy strategy)
            => strategy.GetType().GetField("_mlSignalGenerator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(strategy) as IMachineLearningSignalGenerator
               ?? throw new InvalidOperationException("ML generator unavailable");

        static IFeatureStore meanReversionFeatureStore(Core.Strategies.MeanReversionStrategy strategy)
            => strategy.GetType().GetField("_featureStore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(strategy) as IFeatureStore
               ?? throw new InvalidOperationException("Feature store unavailable");

        static IMultiTimeframeAnalyzer momentumAnalyzer(Core.Strategies.MomentumStrategy strategy)
            => strategy.GetType().GetField("_analyzer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(strategy) as IMultiTimeframeAnalyzer
               ?? throw new InvalidOperationException("Analyzer unavailable");
    }

    private static IEnumerable<IReadOnlyDictionary<string, double>> GenerateCombinations(IReadOnlyDictionary<string, IReadOnlyList<double>> parameterSpace)
    {
        string[] keys = parameterSpace.Keys.ToArray();
        int[] indices = new int[keys.Length];
        int[] lengths = keys.Select(k => parameterSpace[k].Count).ToArray();
        int total = lengths.Aggregate(1, (acc, len) => acc * len);

        for (int i = 0; i < total; i++)
        {
            var combination = new Dictionary<string, double>();
            for (int j = 0; j < keys.Length; j++)
            {
                combination[keys[j]] = parameterSpace[keys[j]][indices[j]];
            }

            yield return combination;

            for (int j = keys.Length - 1; j >= 0; j--)
            {
                indices[j]++;
                if (indices[j] < lengths[j])
                {
                    break;
                }

                indices[j] = 0;
            }
        }
    }
}
