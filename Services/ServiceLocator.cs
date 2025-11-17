using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Application.Backtesting;
using 币安量化机器人.Application.Services;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Core.Risk;
using 币安量化机器人.Core.Strategies;
using 币安量化机器人.Infrastructure.Data;
using 币安量化机器人.Monitoring;

namespace 币安量化机器人.Services;

public static class ServiceLocator
{
    static ServiceLocator()
    {
        AppSettingsService.Load();
    }

    private static readonly Lazy<DataCacheService> CacheFactory = new(() =>
    {
        var cache = new DataCacheService();
        cache.InitializeAsync().GetAwaiter().GetResult();
        return cache;
    });

    private static readonly Lazy<BinanceApiClient> ApiFactory = new(() => new BinanceApiClient());
    private static readonly Lazy<AiForecastService> AiFactory = new(() => new AiForecastService(Api, Cache));
    private static readonly Lazy<RiskEngine> RiskFactory = new(() => new RiskEngine(Cache));
    private static readonly Lazy<BinanceStreamClient> StreamFactory = new(() => new BinanceStreamClient());
    private static readonly Lazy<NotificationService> NotificationFactory = new(() => new NotificationService());
    private static readonly Lazy<AppSettings> SettingsFactory = new(() => AppSettingsService.Current);
    private static readonly Lazy<RiskManager> AdvancedRiskFactory = new(() => new RiskManager());
    private static readonly Lazy<IMultiTimeframeAnalyzer> AnalyzerFactory = new(() => new MultiTimeframeAnalyzer());
    private static readonly Lazy<IMachineLearningSignalGenerator> MlFactory = new(() => new RandomForestSignalGenerator());
    private static readonly Lazy<InMemoryFeatureStore> FeatureStoreFactory = new(() => new InMemoryFeatureStore());
    private static readonly Lazy<RealTimeDataPipeline> PipelineFactory = new(CreatePipeline);
    private static readonly Lazy<DataPipelineOrchestrator> OrchestratorFactory = new(() => new DataPipelineOrchestrator(PipelineFactory.Value));
    private static readonly Lazy<IMarketDataService> MarketDataFactory = new(() => new PipelineMarketDataService(OrchestratorFactory.Value, FeatureStoreFactory.Value));
    private static readonly Lazy<ITradeMonitoringHub> MonitoringHubFactory = new(() => new InMemoryTradeMonitoringHub());
    private static readonly Lazy<StrategyOrchestrator> StrategyOrchestratorFactory = new(() => new StrategyOrchestrator(MarketDataFactory.Value, AdvancedRiskFactory.Value, FeatureStoreFactory.Value, MonitoringHubFactory.Value));
    private static readonly Lazy<GridSearchStrategyOptimizer> OptimizerFactory = new(() => new GridSearchStrategyOptimizer());
    private static readonly Lazy<WalkForwardOptimizer> WalkForwardFactory = new(() => new WalkForwardOptimizer(OptimizerFactory.Value, new DefaultBacktestEngine()));
    private static readonly Lazy<HealthCheckService> HealthCheckFactory = new(() => new HealthCheckService(CacheFactory.Value, ApiFactory.Value));

    public static DataCacheService Cache => CacheFactory.Value;
    public static BinanceApiClient Api => ApiFactory.Value;
    public static AiForecastService Ai => AiFactory.Value;
    public static RiskEngine Risk => RiskFactory.Value;
    public static BinanceStreamClient Stream => StreamFactory.Value;
    public static NotificationService Notification => NotificationFactory.Value;
    public static AppSettings Settings => SettingsFactory.Value;
    public static RiskManager AdvancedRisk => AdvancedRiskFactory.Value;
    public static IMultiTimeframeAnalyzer Analyzer => AnalyzerFactory.Value;
    public static IMachineLearningSignalGenerator MachineLearning => MlFactory.Value;
    public static InMemoryFeatureStore FeatureStore => FeatureStoreFactory.Value;
    public static RealTimeDataPipeline DataPipeline => PipelineFactory.Value;
    public static StrategyOrchestrator StrategyOrchestrator => StrategyOrchestratorFactory.Value;
    public static WalkForwardOptimizer WalkForward => WalkForwardFactory.Value;
    public static ITradeMonitoringHub MonitoringHub => MonitoringHubFactory.Value;
    public static HealthCheckService HealthCheck => HealthCheckFactory.Value;

    private static RealTimeDataPipeline CreatePipeline()
    {
        var dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        var dbPath = Path.Combine(dataDirectory, "trading.sqlite");
        var importPath = Path.Combine(dataDirectory, "import");
        Directory.CreateDirectory(importPath);

        var sources = new IDataSource[]
        {
            new DatabaseDataSource($"Data Source={dbPath}"),
            new ApiDataSource(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, "https://api.binance.com"),
            new FileDataSource(importPath)
        };

        var qualityRules = new IDataQualityRule[]
        {
            new NullValueQualityRule(),
            new RangeQualityRule("close", 0, double.MaxValue),
            new SpikeDetectionRule("close")
        };

        var engineers = new IFeatureEngineer[]
        {
            new TechnicalIndicatorEngineer(),
            new LagFeatureEngineer()
        };

        return new RealTimeDataPipeline(sources, qualityRules, engineers, FeatureStoreFactory.Value);
    }

    public static async ValueTask DisposeAsync()
    {
        if (StreamFactory.IsValueCreated)
            await StreamFactory.Value.DisposeAsync();
        if (ApiFactory.IsValueCreated)
            ApiFactory.Value.Dispose();
    }

    private sealed class DefaultBacktestEngine : IBacktestEngine
    {
        public async ValueTask<BacktestResult> RunAsync(BacktestRequest request, CancellationToken cancellationToken = default)
        {
            var random = new Random(42);
            var observations = GenerateSyntheticData(request.Symbol, request.Start, request.End);
            var signals = new List<TradeSignal>();
            await foreach (var decision in request.Strategy.RunAsync(observations, cancellationToken))
            {
                signals.Add(new TradeSignal(request.Symbol, decision.Action, decision.Confidence, decision.MlSignal));
            }

            var equity = signals.Count(s => s.Action.ActionType != TradeActionType.Hold) * random.NextDouble() * 10;
            var sharpe = signals.Count == 0 ? 0.1 : 1.5;

            return new BacktestResult(request.Strategy.Name, equity, sharpe, sharpe / 1.2, 0.1, sharpe / 2, 0.55, 1.4, signals);
        }

        private static async IAsyncEnumerable<MarketObservation> GenerateSyntheticData(string symbol, DateTime start, DateTime end)
        {
            var random = new Random(7);
            var timestamp = start;
            var price = 100d;
            while (timestamp < end)
            {
                var change = random.NextDouble() - 0.5;
                var open = price;
                var close = price + change;
                var high = Math.Max(open, close) + random.NextDouble();
                var low = Math.Min(open, close) - random.NextDouble();
                var volume = random.NextDouble() * 100;
                var indicators = new Dictionary<string, double>
                {
                    ["sma"] = (open + close) / 2,
                    ["std"] = Math.Abs(change) + 0.1,
                    ["momentum_1"] = change
                };

                yield return new MarketObservation(symbol, TimeSpan.FromMinutes(1), timestamp, open, high, low, close, volume, indicators);

                price = close;
                timestamp = timestamp.AddMinutes(1);
                await Task.Yield();
            }
        }
    }
}
