using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using 币安量化机器人.Application.Services;
using 币安量化机器人.Core.Models;
using Xunit;

namespace 币安量化机器人.Tests
{
    public class GridSearchStrategyOptimizerTests
    {
        [Fact]
        public async Task OptimizeAsync_Should_ReturnResult_For_SmallParameterSpace()
        {
            var optimizer = new GridSearchStrategyOptimizer();
            var fakeStrategy = new FakeStrategy();
            var paramSpace = new Dictionary<string, IReadOnlyList<double>>
            {
                ["p1"] = new double[] { 1.0, 2.0 },
                ["p2"] = new double[] { 0.1 }
            };

            var request = new OptimizationRequest("BTCUSDT", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, paramSpace, new TestBacktestEngine());
            var result = await optimizer.OptimizeAsync(fakeStrategy, request);

            Assert.NotNull(result);
            Assert.NotEmpty(result.Candidates);
        }

        private class FakeStrategy : Core.Abstractions.ITradingStrategy
        {
            public string Name => "Fake";
            public StrategyParameters Parameters => new StrategyParameters(new Dictionary<string, double>());
            public void Initialize(Core.Abstractions.IStrategyContext context) { }
            public ValueTask<Core.Models.StrategyDecision> EvaluateAsync(Core.Models.MarketObservation observation, System.Threading.CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public IAsyncEnumerable<Core.Models.StrategyDecision> RunAsync(IAsyncEnumerable<Core.Models.MarketObservation> observations, System.Threading.CancellationToken cancellationToken = default) => AsyncEnumerable.Empty<Core.Models.StrategyDecision>();
        }

        private class TestBacktestEngine : Core.Abstractions.IBacktestEngine
        {
            public ValueTask<BacktestResult> RunAsync(BacktestRequest request, System.Threading.CancellationToken cancellationToken = default)
            {
                var result = new BacktestResult("fake", 1.0, 1.0, 0.5, 0.1, 2.0, 0.6, 1.2, Array.Empty<TradeSignal>());
                return ValueTask.FromResult(result);
            }
        }
    }
}
