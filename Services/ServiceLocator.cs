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
using 币安量化机器人.Models;
using 币安量化机器人.Models.Configuration;
using 币安量化机器人.Monitoring;
using 币安量化机器人.Services.AI;
using 币安量化机器人.Services.Resilience;

namespace 币安量化机器人.Services;

/// <summary>
/// 服务定位器 - 提供全局单例访问
/// </summary>
public static class ServiceLocator
{
    static ServiceLocator()
    {
        AppSettingsService.Load();

        // 🆕 初始化配置服务
        try
        {
            ConfigurationService.Initialize();
            LogService.Info("配置服务初始化成功");
        }
        catch (Exception ex)
        {
            // 如果配置文件不存在,使用默认配置
            LogService.Error(ex, "配置文件加载失败,使用默认配置");
        }
        // 初始化就绪服务
        SystemReady.RefreshFromConfig();

        // 订阅策略集合变化，自动驱动 AutoTrader（当有运行中策略时启动自动交易，若无则停止）
        try
        {
            StrategyPortfolio.StrategiesChanged += () =>
            {
                try
                {
                    var running = StrategyPortfolio.GetAllStrategies().Where(s => s.IsRunning).ToList();
                    if (running.Count > 0)
                    {
                        var symbols = running.SelectMany(s => (IEnumerable<string>)(s.Symbols ?? Enumerable.Empty<string>()))
                                          .Select(x => x.Trim().ToUpperInvariant())
                                          .Where(x => x.Length > 0)
                                          .Distinct()
                                          .ToArray();
                        var acct = RuntimeState.CurrentAccountType;
                        _ = AutoTrader.StartAsync(symbols, acct, enableScalping: false);
                        LogService.Info("[ServiceLocator] AutoTrader 启动请求, 策略数={Count}, Symbols={Symbols}, Acct={Acct}", running.Count, string.Join(',', symbols), acct);
                    }
                    else
                    {
                        _ = AutoTrader.StopAsync();
                        LogService.Info("[ServiceLocator] AutoTrader 停止请求 (无运行策略)");
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "[ServiceLocator] 处理 StrategiesChanged 事件失败");
                }
            };

            // 当 UI 切换账户模式时，如果 AutoTrader 正在运行，重启以使用新账户
            RuntimeState.OnAccountTypeChanged += (acct) =>
            {
                try
                {
                    var running = StrategyPortfolio.GetAllStrategies().Where(s => s.IsRunning).ToList();
                    if (running.Count > 0)
                    {
                        var symbols = running.SelectMany(s => (IEnumerable<string>)(s.Symbols ?? Enumerable.Empty<string>()))
                                          .Select(x => x.Trim().ToUpperInvariant())
                                          .Where(x => x.Length > 0)
                                          .Distinct()
                                          .ToArray();
                        _ = AutoTrader.StartAsync(symbols, acct, enableScalping: false);
                        LogService.Info("[ServiceLocator] AutoTrader 账户切换请求, 使用账户={Acct}", acct);
                    }
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "[ServiceLocator] 处理 AccountTypeChanged 事件失败");
                }
            };
        }
        catch (Exception ex)
        {
            LogService.Warning("[ServiceLocator] 无法订阅策略集合变化或账户模式变更: {Message}", ex.Message);
        }

        try
        {
            // ensure Cache is initialized before creating LearningModule
            LearningModule = new LearningModule(Cache);
            _ = LearningModule.LoadStateAsync().ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    LogService.Warning("[ServiceLocator] 恢复 LearningModule 状态失败: {0}", t.Exception?.GetBaseException().Message);
                }
                else
                {
                    LogService.Info("[ServiceLocator] LearningModule 状态恢复完成");
                }
            });
        }
        catch (Exception ex)
        {
            LogService.Warning("[ServiceLocator] 初始化 LearningModule 失败: {0}", ex.Message);
        }

        // Start a background health check to report initial health
        try
        {
            var healthService = new Services.Health.HealthCheckService();
            _ = Task.Run(async () =>
            {
                try
                {
                    var res = await healthService.RunAsync();
                    LogService.Info("[HealthCheck] 系统健康: {Healthy} (Config={Config} Api={Api} Cache={Cache})", res.IsHealthy, res.ConfigOk, res.ApiOk, res.CacheOk);
                    if (res.Notes.Length > 0)
                    {
                        foreach (var n in res.Notes)
                        {
                            LogService.Info("[HealthCheck] Note: {0}", n);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogService.Warning("Health check task failed: {Message}", ex.Message);
                }
            });
        }
        catch (Exception ex)
        {
            LogService.Warning("无法启动健康检查服务: {0}", ex.Message);
        }

    }

    private static readonly Lazy<SystemReadyService> SystemReadyFactory = new(() => new SystemReadyService());
    public static SystemReadyService SystemReady => SystemReadyFactory.Value;

    // 🆕 配置类工厂 - 从appsettings.json加载
    private static readonly Lazy<TradingConfig> TradingConfigFactory = new(() =>
    {
        try
        {
            return ConfigurationService.GetTradingConfig();
        }
        catch
        {
            LogService.Warning("交易配置加载失败,使用默认配置");
            return new TradingConfig();
        }
    });

    private static readonly Lazy<ApiConfig> ApiConfigFactory = new(() =>
    {
        try
        {
            return ConfigurationService.GetApiConfig();
        }
        catch
        {
            LogService.Warning("API配置加载失败,使用默认配置");
            return new ApiConfig();
        }
    });

    private static readonly Lazy<RiskConfig> RiskConfigFactory = new(() =>
    {
        try
        {
            return ConfigurationService.GetRiskConfig();
        }
        catch
        {
            LogService.Warning("风控配置加载失败,使用默认配置");
            return new RiskConfig();
        }
    });

    private static readonly Lazy<BacktestConfig> BacktestConfigFactory = new(() =>
    {
        try
        {
            return ConfigurationService.GetBacktestConfig();
        }
        catch
        {
            LogService.Warning("回测配置加载失败,使用默认配置");
            return new BacktestConfig();
        }
    });

    private static readonly Lazy<DataCacheService> CacheFactory = new(() =>
    {
        var cache = new DataCacheService();
        cache.InitializeAsync().GetAwaiter().GetResult();
        return cache;
    });

    // 新增 ResilienceService 工厂
    private static readonly Lazy<ResilienceService> ResilienceFactory = new(() => new ResilienceService());
    public static ResilienceService GetResilienceService() => ResilienceFactory.Value;

    private static readonly Lazy<BinanceApiClient> ApiFactory = new(() =>
    {
        var client = new BinanceApiClient(GetResilienceService());

        // 🔧 从配置文件或环境变量加载 Binance API 凭证
        try
        {
            string apiKey = Environment.GetEnvironmentVariable("BINANCE_API_KEY")
                        ?? ConfigurationService.GetValue("Api:Binance:ApiKey");
            string secretKey = Environment.GetEnvironmentVariable("BINANCE_SECRET_KEY")
                           ?? ConfigurationService.GetValue("Api:Binance:SecretKey");

            if (!string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(secretKey))
            {
                // 🔧 验证 API Key 格式
                if (!ValidateApiKeyFormat(apiKey, "Binance API Key"))
                {
                    LogService.Error("[ServiceLocator] Binance API Key 格式无效，包含非法字符");
                    LogService.Warning("[ServiceLocator] Binance API 凭证未配置或无效，部分功能可能不可用");
                    return client;
                }

                if (!ValidateApiKeyFormat(secretKey, "Binance Secret Key"))
                {
                    LogService.Error("[ServiceLocator] Binance Secret Key 格式无效，包含非法字符");
                    LogService.Warning("[ServiceLocator] Binance API 凭证未配置或无效，部分功能可能不可用");
                    return client;
                }

                client.SetApiCredentials(apiKey, secretKey);
                LogService.Info("[ServiceLocator] ✅ Binance API 凭证已加载: {MaskedKey}",
                    apiKey.Length > 8 ? $"{apiKey.Substring(0, 8)}...{apiKey.Substring(apiKey.Length - 4)}" : "****");
            }
            else
            {
                LogService.Warning("[ServiceLocator] ⚠️ Binance API 凭证未配置，部分功能可能不可用");
                LogService.Info("[ServiceLocator] 配置方法: 前往 [API 管理] 页面配置 Binance API Key");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[ServiceLocator] ❌ 加载 Binance API 凭证失败");
        }

        return client;
    });
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
    private static readonly Lazy<EnhancedBacktestEngine> EnhancedBacktestFactory = new(() => new EnhancedBacktestEngine(config: BacktestConfigFactory.Value));
    private static readonly Lazy<WalkForwardOptimizer> WalkForwardFactory = new(() => new WalkForwardOptimizer(OptimizerFactory.Value, EnhancedBacktestFactory.Value));
    private static readonly Lazy<OrderHistoryService> OrderHistoryFactory = new(() => new OrderHistoryService(CacheFactory.Value));
    private static readonly Lazy<PerformanceTrackingService> PerformanceTrackingFactory = new(() => new PerformanceTrackingService(CacheFactory.Value));
    private static readonly Lazy<Services.Observability.ObservabilityService> ObservabilityFactory = new(() => new Services.Observability.ObservabilityService());

    private static readonly Lazy<StrategyPortfolioManager> StrategyPortfolioManagerFactory = new(() =>
    {
        var mgr = new StrategyPortfolioManager(new TradingAccountManager(CacheFactory.Value), AppContext.BaseDirectory);
        try
        {
            mgr.LoadFromDisk();
        }
        catch (Exception ex)
        {
            LogService.Warning(ex.Message, "加载策略组合管理器失败");
        }
        return mgr;
    });

    // 公共策略模板和工厂（供系统其他模块使用）
    private static readonly Lazy<StrategyTemplateLibrary> StrategyTemplatesFactory = new(() => new StrategyTemplateLibrary(AppContext.BaseDirectory));
    private static readonly Lazy<StrategyFactory> StrategyFactoryFactory = new(() => new StrategyFactory(AnalyzerFactory.Value, MlFactory.Value, FeatureStoreFactory.Value));
    public static StrategyTemplateLibrary StrategyTemplates => StrategyTemplatesFactory.Value;
    public static StrategyFactory StrategyFactory => StrategyFactoryFactory.Value;

    // 公开策略组合管理器
    public static StrategyPortfolioManager StrategyPortfolio => StrategyPortfolioManagerFactory.Value;

    // 🆕 中央AI协调器工厂
    private static AICentralCoordinator? _aiCoordinator;

    private static AITradingBot? _aiTradingBot;

    // 🆕 新增：一键自动交易控制器
    private static readonly Lazy<AutoTradingController> AutoTradingControllerFactory = new(() => new AutoTradingController());

    private static readonly Lazy<AI.AIStrategyGenerator> AIStrategyGeneratorFactory = new(() => new AI.AIStrategyGenerator());
    public static AI.AIStrategyGenerator AIStrategyGenerator => AIStrategyGeneratorFactory.Value;

    private static readonly Lazy<AutoPilotService> AutoPilotFactory = new(() => new AutoPilotService(SystemReady, AIStrategyGenerator, StrategyPortfolio, AutoTrader));
    public static AutoPilotService AutoPilot => AutoPilotFactory.Value;

    // 🆕 配置类公开访问
    /// <summary>
    /// 交易配置 - 包含信心度、仓位、止损等参数
    /// </summary>
    public static TradingConfig TradingConfig => TradingConfigFactory.Value;

    /// <summary>
    /// API配置 - 包含端点、超时、速率限制等参数
    /// </summary>
    public static ApiConfig ApiConfig => ApiConfigFactory.Value;

    /// <summary>
    /// 风控配置 - 包含风控规则开关和参数
    /// </summary>
    public static RiskConfig RiskConfig => RiskConfigFactory.Value;

    /// <summary>
    /// 回测配置 - 包含资金、手续费、滑点等参数
    /// </summary>
    public static BacktestConfig BacktestConfig => BacktestConfigFactory.Value;

    public static DataCacheService Cache => CacheFactory.Value;
    public static Services.Observability.ObservabilityService Observability => ObservabilityFactory.Value;
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
    public static OrderHistoryService OrderHistory => OrderHistoryFactory.Value;
    public static PerformanceTrackingService PerformanceTracking => PerformanceTrackingFactory.Value;
    public static EnhancedBacktestEngine EnhancedBacktest => EnhancedBacktestFactory.Value;

    // 公开自动交易控制器
    public static AutoTradingController AutoTrader => AutoTradingControllerFactory.Value;

    /// <summary>
    /// 获取或创建AI交易机器人
    /// </summary>
    /// <param name="deepSeekApiKey">DeepSeek API密钥</param>
    /// <returns>AI交易机器人实例</returns>
    public static AITradingBot GetAITradingBot(string deepSeekApiKey)
    {
        // 如果尚未创建，则使用传入的key创建
        if (_aiTradingBot == null)
        {
            _aiTradingBot = new AITradingBot(deepSeekApiKey, Api, Cache);
            return _aiTradingBot;
        }

        // 如果传入了新的有效key，则重新创建实例以确保最新配置生效
        if (!string.IsNullOrWhiteSpace(deepSeekApiKey))
        {
            _aiTradingBot = new AITradingBot(deepSeekApiKey, Api, Cache);
        }
        return _aiTradingBot;
    }

    /// <summary>
    /// 🆕 获取或创建中央AI协调器
    /// </summary>
    /// <remarks>
    /// 中央AI协调器是整个系统的"大脑"，负责：
    /// - 全局状态感知和监控
    /// - 智能决策和工作流编排
    /// - 模块间协同控制
    /// - 持续学习和优化
    /// 
    /// 使用示例：
    /// <code>
    /// var coordinator = ServiceLocator.GetAICentralCoordinator();
    /// await coordinator.StartAsync();
    /// </code>
    /// </remarks>
    public static AICentralCoordinator GetAICentralCoordinator()
    {
        if (_aiCoordinator == null)
        {
            // 创建必要的依赖
            var accountManager = new TradingAccountManager(Cache);
            // 🔧 修复：统一从配置服务读取 DeepSeek API Key
            string deepSeekApiKey = ConfigurationService.GetDeepSeekApiKey();

            var dataProcessor = new MarketDataPreprocessor(Api, Cache);

            // 🆕 仅在配置启用时创建 DeepSeek agent
            bool aiEnabled = ConfigurationService.GetAIConfig().EnableAITrading;
            ITradingAgent? agent = null;
            if (aiEnabled && !string.IsNullOrWhiteSpace(deepSeekApiKey))
            {
                agent = new DeepSeekTradingAgent(deepSeekApiKey);
            }
            else
            {
                agent = new LocalAIAgentAdapter(ConfigurationService.GetAIConfig().Temperature);
                LogService.Info("[ServiceLocator] 使用本地 AI 适配器 (LocalAIAgentAdapter)");
            }

            var riskManager = new AIRiskManager();
            var executionEngine = new AIOrderExecutionEngine(accountManager, Api, Cache, riskManager);
            var positionManager = new PositionManager(accountManager, Api, executionEngine);

            var tradingAutomation = new AITradingAutomation(
                Stream,
                dataProcessor,
                agent,
                executionEngine,
                accountManager,
                positionManager
            );

            _aiCoordinator = new AICentralCoordinator(
                EnhancedBacktest,
                tradingAutomation,
                accountManager,
                positionManager,
                Api,
                Cache
            );

            // 将全局闸门接入执行引擎
            _aiCoordinator.AttachGlobalGateTo(executionEngine);

            LogService.Info("🧠 [ServiceLocator] 中央AI协调器已创建");
        }

        return _aiCoordinator;
    }

    private static RealTimeDataPipeline CreatePipeline()
    {
        string dataDirectory = Path.Combine(AppContext.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        string dbPath = Path.Combine(dataDirectory, "trading.sqlite");
        string importPath = Path.Combine(dataDirectory, "import");
        Directory.CreateDirectory(importPath);

        var sources = new Core.Data.IDataSource[]
        {
            new Infrastructure.Data.DatabaseDataSource($"Data Source={dbPath}"),
            new Infrastructure.Data.ApiDataSource(new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, "https://api.binance.com"),
            new Infrastructure.Data.FileDataSource(importPath)
        };

        var qualityRules = new Core.Data.IDataQualityRule[]
        {
            new Infrastructure.Data.NullValueQualityRule(),
            new Infrastructure.Data.RangeQualityRule("close", 0, double.MaxValue),
            new Infrastructure.Data.SpikeDetectionRule("close")
        };

        var engineers = new Core.Data.IFeatureEngineer[]
        {
            new Infrastructure.Data.TechnicalIndicatorEngineer(),
            new Infrastructure.Data.LagFeatureEngineer()
        };

        return new RealTimeDataPipeline(sources, qualityRules, engineers, FeatureStoreFactory.Value);
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public static async ValueTask DisposeAsync()
    {
        if (StreamFactory.IsValueCreated)
        {
            await StreamFactory.Value.DisposeAsync();
        }

        if (ApiFactory.IsValueCreated)
        {
            ApiFactory.Value.Dispose();
        }
    }

    /// <summary>
    /// 🔧 验证 API Key 格式（确保只包含有效字符）
    /// </summary>
    /// <param name="apiKey">API Key</param>
    /// <param name="keyName">密钥名称（用于日志）</param>
    /// <returns>是否有效</returns>
    private static bool ValidateApiKeyFormat(string apiKey, string keyName)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return false;
        }

        // 检查是否包含非 ASCII 字符或不可见字符
        foreach (char ch in apiKey)
        {
            // 允许: 英文字母、数字、横杠、下划线
            if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_')
            {
                LogService.Warning("[ServiceLocator] {KeyName} 包含非法字符: {Char} (ASCII: {Code})",
                    keyName, ch, (int)ch);
                return false;
            }
        }

        return true;
    }

    public static LearningModule? LearningModule { get; private set; }

    private static readonly Lazy<DialogService> DialogServiceFactory = new(() => new DialogService());
    public static DialogService DialogService => DialogServiceFactory.Value;
}
