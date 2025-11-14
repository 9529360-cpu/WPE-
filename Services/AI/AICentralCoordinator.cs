using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Application.Backtesting;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Models;
using 币安量化机器人.Services.AI; // 🆕 Phase 2: 智能组件
using 币安量化机器人.Services.Observability;  // 🆕 Phase 4: 可观测性
using 币安量化机器人.Services.Performance;
using 币安量化机器人.Services.Resilience;

using System.Runtime.Versioning;

namespace 币安量化机器人.Services.AI;

[SupportedOSPlatform("windows")]
public class AICentralCoordinator : IDisposable
{
    private readonly StateManager _stateManager;
    private readonly DecisionEngine _decisionEngine;
    private readonly WorkflowOrchestrator _workflowOrchestrator;
    private readonly LearningModule _learningModule;
    private readonly ResourceManager _resourceManager;

    // 🆕 Phase 2: 智能组件
    private readonly WorkflowEngine _workflowEngine;
    private readonly DecisionFactorLibrary _factorLibrary;

    // 🆕 Phase 3: 弹性和性能组件
    // Make nullable and initialize only on Windows to avoid CA1416 runtime issues on non-Windows platforms
    private readonly SystemResourceMonitor? _resourceMonitor;
    private readonly ResilienceService _resilienceService;
    private readonly PerformanceOptimizationService _performanceService;

    // 🆕 Phase 4: 可观测性组件
    private readonly ObservabilityService _observability;

    // 依赖的模块
    private readonly EnhancedBacktestEngine _backtestEngine;
    private readonly AITradingAutomation _tradingAutomation;
    private readonly TradingAccountManager _accountManager;
    private readonly PositionManager _positionManager;
    private readonly BinanceApiClient _apiClient;
    private readonly DataCacheService _cacheService;

    private CancellationTokenSource? _mainLoopCts;
    private Task? _mainLoopTask;
    private bool _isRunning;
    private readonly object _startStopLock = new();

    // 🆕 Phase 2: 自动化决策开关
    private bool _autoDecisionEnabled = true;

    // 🆕 公开EventBus以供UI访问
    public EventBus EventBus => _eventBus;
    private readonly EventBus _eventBus;

    private readonly ITradeGate _globalGate; // 新增：全局闸门

    // 新增，策略生命周期管理器
    private readonly StrategyLifecycleManager _strategyLifecycle;

    public AICentralCoordinator(
        EnhancedBacktestEngine backtestEngine,
        AITradingAutomation tradingAutomation,
        TradingAccountManager accountManager,
        PositionManager positionManager,
        BinanceApiClient apiClient,
        DataCacheService cacheService)
    {
        // Validate required dependencies early to avoid nullability warnings at call sites
        _backtestEngine = backtestEngine ?? throw new ArgumentNullException(nameof(backtestEngine));
        _tradingAutomation = tradingAutomation ?? throw new ArgumentNullException(nameof(tradingAutomation));
        _accountManager = accountManager ?? throw new ArgumentNullException(nameof(accountManager));
        _positionManager = positionManager ?? throw new ArgumentNullException(nameof(positionManager));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));

        // 初始化核心组件
        _stateManager = new StateManager();
        _eventBus = new EventBus();
        _resourceManager = new ResourceManager();
        _learningModule = new LearningModule(_cacheService);
        _decisionEngine = new DecisionEngine(_learningModule, _stateManager);
        _workflowOrchestrator = new WorkflowOrchestrator(
            _stateManager,
            _decisionEngine,
            _eventBus,
            _resourceManager);

        // 🆕 Phase 2: 初始化智能组件
        _factorLibrary = new DecisionFactorLibrary();
        _workflowEngine = new WorkflowEngine(_stateManager, _workflowOrchestrator, _eventBus);

        // 🆕 Phase 3: 初始化弹性和性能组件
        _resilienceService = new ResilienceService();
        _performanceService = new PerformanceOptimizationService();
        if (OperatingSystem.IsWindows())
        {
            _resourceMonitor = new SystemResourceMonitor();
        }
        else
        {
            _resourceMonitor = null;
        }

        // 🆕 Phase 4: 初始化可观测性服务
        _observability = new ObservabilityService("AICentralCoordinator");

        // 策略生命周期管理器
        _strategyLifecycle = new StrategyLifecycleManager(ServiceLocator.StrategyPortfolio, ServiceLocator.AIStrategyGenerator, _backtestEngine, _eventBus);

        // 关键：把 EventBus 和 全局闸门 注入交易流水
        _tradingAutomation.SetEventBus(_eventBus);
        _globalGate = new GlobalTradeGate(_stateManager);
        ServiceLocator.AutoTrader?.ToString(); // no-op keep ref

        // 订阅事件
        SubscribeToEvents();

        LogService.Info("🧠 [AICentralCoordinator] 增强版中央AI协调器已初始化");
        LogService.Info("   ✅ 智能工作流引擎已加载");
        LogService.Info("   ✅ 决策因子库已加载 ({Count}个因子)", _factorLibrary.GetFactors().Count);
        LogService.Info("   ✅ 弹性恢复服务已加载");
        LogService.Info("   ✅ 性能优化服务已加载");
        if (_resourceMonitor != null)
        {
            LogService.Info("   ✅ 系统资源监控器已加载");
        }
        else
        {
            LogService.Info("   ℹ️ 系统资源监控器未在当前平台初始化（仅在 Windows 上启用）");
        }
        LogService.Info("   ✅ 可观测性系统已加载");  // 🆕

        // 🆕 Phase 4: 记录初始化指标
        try
        {
            _observability.IncrementCounter("ai_coordinator_init_count");
            _observability.LogInfo("AI协调器初始化完成", new
            {
                factorCount = _factorLibrary.GetFactors().Count,
                components = "WorkflowEngine+DecisionEngine+FactorLibrary+Resilience+Performance+Observability"
            });
        }
        catch (Exception ex)
        {
            LogService.Warning("记录初始化指标失败: {Message}", ex.Message);
        }
    }

    #region 状态访问

    /// <summary>
    /// 是否正在运行
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// 当前系统状态
    /// </summary>
    public SystemState CurrentState => _stateManager.CurrentState;

    /// <summary>
    /// 当前工作流阶段
    /// </summary>
    public WorkflowStage CurrentStage => _stateManager.CurrentStage;

    /// <summary>
    /// 🆕 是否启用自动决策
    /// </summary>
    public bool AutoDecisionEnabled
    {
        get => _autoDecisionEnabled;
        set
        {
            _autoDecisionEnabled = value;
            LogService.Info("[AICentralCoordinator] 自动决策: {Status}", value ? "已启用" : "已禁用");
        }
    }

    /// <summary>
    /// 🆕 获取工作流转换历史
    /// </summary>
    public List<WorkflowTransition> GetWorkflowHistory(int count = 50)
    {
        return _workflowEngine.GetTransitionHistory(count);
    }

    /// <summary>
    /// 🆕 获取当前适用的工作流规则
    /// </summary>
    public List<WorkflowRule> GetApplicableRules()
    {
        return _workflowEngine.GetApplicableRules();
    }

    #endregion

    #region 主控制流程

    /// <summary>
    /// 启动中央AI协调器主循环
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        using var activity = _observability?.StartTrace("AICentralCoordinator.Start");

        lock (_startStopLock)
        {
            if (_isRunning)
            {
                _observability?.LogWarning("协调器已在运行中");
                return;
            }
            _isRunning = true;
        }

        try
        {
            if (ct.IsCancellationRequested)
            {
                _observability?.LogWarning("StartAsync 被取消的 token");
                lock (_startStopLock) { _isRunning = false; }
                return;
            }

            _mainLoopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            _observability?.LogInfo("🚀 启动增强版中央AI协调器");
            _observability?.IncrementCounter("ai_coordinator_start_count");
            _observability?.SetGauge("ai_coordinator_running", 1);

            // 初始化系统状态
            await _stateManager.InitializeAsync().ConfigureAwait(false);

            // 启动主循环
            _mainLoopTask = MainControlLoopAsync(_mainLoopCts.Token);

            await _mainLoopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _observability?.LogInfo("StartAsync 取消");
        }
        catch (Exception ex)
        {
            _observability?.LogError("启动AI协调器失败", ex);
            _observability?.IncrementCounter("ai_coordinator_start_error_count");

            // cleanup state on failure
            try
            {
                _mainLoopCts?.Cancel();
                _mainLoopTask = null;
            }
            catch { }

            lock (_startStopLock) { _isRunning = false; }

            throw;
        }
    }

    /// <summary>
    /// 停止中央AI协调器 (Phase 4: 增强可观测性)
    /// </summary>
    public async Task StopAsync()
    {
        using var activity = _observability?.StartTrace("AICentralCoordinator.Stop");

        lock (_startStopLock)
        {
            if (!_isRunning)
            {
                return;
            }
            _isRunning = false;
        }

        _observability?.LogInfo("🛑 停止中央AI协调器");
        _observability?.SetGauge("ai_coordinator_running", 0);

        try
        {
            _mainLoopCts?.Cancel();

            if (_mainLoopTask != null)
            {
                await Task.WhenAny(_mainLoopTask, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _observability?.LogError("停止 AI 调度器时发生错误", ex);
        }
        finally
        {
            try
            {
                _mainLoopCts?.Dispose();
                _mainLoopCts = null;
                _mainLoopTask = null;
            }
            catch { }

            _observability?.LogInfo("✅ AI协调器已停止");
        }
    }

    /// <summary>
    /// 🆕 增强版 AI大脑主控制循环 (Phase 4: 全链路可观测)
    /// </summary>
    private async Task MainControlLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 🆕 Phase 4: 使用可观测性服务的自动化观测
                try
                {
                    await _observability.ObserveOperationAsync(
                        "MainControlLoopIteration",
                        async () =>
                        {
                            // 使用性能监控记录操作（防御式以避免 null 引发的分析器警告）
                            var __perfScope = _performanceService?.RecordOperation("MainControlLoop");
                            try
                            {
                                // 1. 收集全系统状态
                                var systemState = await CollectSystemStateWithObservabilityAsync(ct).ConfigureAwait(false);
                                _stateManager.UpdateState(systemState);

                                // 2. 计算决策因子得分
                                var factorScores = await CalculateDecisionFactorsWithObservabilityAsync(systemState, ct).ConfigureAwait(false);
                                var weightedScore = _factorLibrary.CalculateWeightedScore(factorScores);

                                // 🆕 记录指标
                                _observability.SetGauge("ai_decision_factor_score", (double)weightedScore);
                                _observability.RecordHistogram("ai_decision_factor_count", factorScores.Count);

                                // 3. AI决策分析
                                var decisions = await _decisionEngine.AnalyzeAsync(systemState, ct).ConfigureAwait(false);
                                decisions.FactorScore = weightedScore;
                                decisions.FactorBreakdown = factorScores;

                                // 参数自适应（基于因子）
                                AdaptStrategyParameters(decisions.FactorBreakdown);

                                // 🆕 记录决策指标
                                _observability.IncrementCounter("ai_decision_count");
                                _observability.SetGauge("ai_decision_confidence", (double)decisions.Confidence);

                                // 4. 自动化工作流转换
                                if (_autoDecisionEnabled)
                                {
                                    var transitioned = await _workflowEngine.EvaluateAndTransitionAsync(ct).ConfigureAwait(false);
                                    if (transitioned)
                                    {
                                        _observability.LogInfo("✅ 工作流自动转换成功");
                                        _observability.IncrementCounter("workflow_transition_success_count");
                                    }
                                }

                                // 5. 执行工作流编排
                                await _workflowOrchestrator.ExecuteAsync(decisions, ct).ConfigureAwait(false);

                                // 6. 学习优化
                                await _learningModule.UpdateKnowledgeAsync(systemState, decisions, ct).ConfigureAwait(false);
                                await UpdateFactorWeightsAsync(systemState, decisions, ct).ConfigureAwait(false);

                                // 7. 策略生命周期管理
                                await _strategyLifecycle.TickAsync(systemState, ct).ConfigureAwait(false);

                                // 8. 定期清理
                                _workflowEngine.CleanupHistory(100);
                            }
                            finally
                            {
                                try { __perfScope?.Dispose(); } catch { }
                            }

                            return true; // 成功完成一次循环
                        },
                        new
                        {
                            stage = _stateManager.CurrentStage.ToString(),
                            autoDecisionEnabled = _autoDecisionEnabled
                        }
                    ).ConfigureAwait(false);

                    // 9. 等待下一个周期
                    var interval = GetControlLoopInterval();
                    await Task.Delay(interval, ct).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // 🆕 Phase 4: 增强异常处理和告警
                    _observability?.LogError("主循环迭代异常", ex, new
                    {
                        stage = _stateManager.CurrentStage.ToString(),
                        iteration = "main_loop"
                    });
                    _observability?.IncrementCounter("ai_coordinator_error_count");

                    // 触发告警
                    if (_observability != null)
                    {
                        await _observability.TriggerAlert("MainLoopError", Core.Risk.AlertSeverity.Error, $"主循环异常: {ex.Message}");
                    }

                    // 报告故障给弹性服务
                    var recovered = await _resilienceService.ReportFailureAsync(
                        "AICentralCoordinator.MainLoop",
                        ex
                    ).ConfigureAwait(false);

                    if (!recovered)
                    {
                        _resilienceService.DetectSystemAnomaly("AICentralCoordinator", ex);

                        var health = _resilienceService.GetSystemHealth();
                        if (!health.IsHealthy)
                        {
                            _observability?.LogCritical("🚨 系统健康异常，触发紧急停止", ex);
                            if (_observability != null)
                            {
                                await _observability.TriggerAlert(
                                    "SystemHealthCritical",
                                    Core.Risk.AlertSeverity.Critical,
                                    "系统健康检查失败，紧急停止"
                                ).ConfigureAwait(false);
                            }
                            await EmergencyStopAsync("系统健康检查失败").ConfigureAwait(false);
                            break;
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _observability?.LogInfo("主循环已取消");
        }
        catch (Exception ex)
        {
            _observability?.LogCritical("主循环致命异常", ex);
            _observability?.IncrementCounter("ai_coordinator_fatal_error_count");

            _resilienceService.DetectSystemAnomaly("AICentralCoordinator", ex);

            if (_observability != null)
            {
                await _observability.TriggerAlert(
                    "MainLoopFatalError",
                    Core.Risk.AlertSeverity.Critical,
                    $"主循环致命异常: {ex.Message}"
                ).ConfigureAwait(false);
            }

            throw;
        }
    }

    /// <summary>
    /// 获取控制循环间隔（根据当前阶段动态调整）
    /// </summary>
    private TimeSpan GetControlLoopInterval()
    {
        return _stateManager.CurrentStage switch
        {
            WorkflowStage.Backtest => TimeSpan.FromSeconds(30),      // 回测阶段：30秒
            WorkflowStage.Optimization => TimeSpan.FromMinutes(1),   // 优化阶段：1分钟
            WorkflowStage.Simulation => TimeSpan.FromSeconds(10),    // 模拟阶段：10秒
            WorkflowStage.Live => TimeSpan.FromSeconds(5),           // 实盘阶段：5秒
            WorkflowStage.Emergency => TimeSpan.FromSeconds(2),      // 紧急状态：2秒
            _ => TimeSpan.FromSeconds(30)
        };
    }

    #endregion

    #region 🆕 Phase 2: 决策因子计算

    /// <summary>
    /// 计算所有决策因子
    /// </summary>
    private async Task<Dictionary<string, decimal>> CalculateDecisionFactorsAsync(
        SystemState systemState,
        CancellationToken ct)
    {
        try
        {
            // 准备市场数据
            var marketData = await PrepareMarketDataAsync(ct).ConfigureAwait(false);

            // 计算所有因子
            var factorScores = _factorLibrary.CalculateAllFactors(marketData);

            LogService.Debug("[AICentralCoordinator] 已计算 {Count} 个决策因子", factorScores.Count);

            return factorScores;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AICentralCoordinator] 计算决策因子失败");
            return new Dictionary<string, decimal>();
        }
    }

    /// <summary>
    /// 准备市场数据供因子计算使用 (Phase 3: 增强降级策略)
    /// </summary>
    private async Task<MarketData> PrepareMarketDataAsync(CancellationToken ct)
    {
        try
        {
            // 使用智能缓存
            var marketData = await _performanceService.GetOrCreateCachedAsync(
                "market_data_btcusdt",
                async () =>
                {
                    // 获取BTC价格数据（作为市场基准）
                    var closes = await _apiClient.GetKlineClosesAsync("BTCUSDT", "1h", 100, ct);
                    var highs = await _apiClient.GetKlineHighsAsync("BTCUSDT", "1h", 100, ct);
                    var lows = await _apiClient.GetKlineLowsAsync("BTCUSDT", "1h", 100, ct);
                    var volumes = await _apiClient.GetKlineVolumesAsync("BTCUSDT", "1h", 100, ct);

                    return new MarketData
                    {
                        ClosePrices = closes.ToList(),
                        HighPrices = highs.ToList(),
                        LowPrices = lows.ToList(),
                        Volumes = volumes.ToList(),
                        MarketCap = 1_000_000_000_000,
                        LongShortRatio = 1.2m,
                        InterestRate = 0.045m,
                        InterestRateChange = 0,
                        DataQuality = 1.0m  // 完整数据
                    };
                },
                TimeSpan.FromMinutes(5)  // 缓存5分钟
            );

            return marketData!;
        }
        catch (Exception ex)
        {
            LogService.Warning("[AICentralCoordinator] 获取市场数据失败，使用降级策略");
            LogService.Error(ex, "[AICentralCoordinator] 市场数据获取异常详情");

            // 🆕 Phase 3: 数据降级策略
            // 直接返回默认安全数据（DataCacheService不支持Get方法）
            LogService.Warning("[AICentralCoordinator] 使用默认市场数据");
            return new MarketData
            {
                ClosePrices = new List<decimal> { 50000m },  // 默认BTC价格
                HighPrices = new List<decimal> { 51000m },
                LowPrices = new List<decimal> { 49000m },
                Volumes = new List<decimal> { 1000m },
                DataQuality = 0.1m  // 最低质量
            };
        }
    }

    /// <summary>
    /// 更新因子权重（学习反馈） - Phase 3增强版
    /// </summary>
    private async Task UpdateFactorWeightsAsync(
        SystemState systemState,
        AIDecision decision,
        CancellationToken ct)
    {
        try
        {
            // 🆕 Phase 3: 实现因子权重的动态调整

            // 1. 获取最近的决策结果
            var lastOutcome = await GetLastDecisionOutcomeAsync(ct);

            // 2. 获取当前因子权重
            var currentWeights = _factorLibrary.GetFactorWeights();

            // 3. 使用学习模块优化权重
            var optimizedWeights = await _learningModule.OptimizeFactorWeightsAsync(
                currentWeights,
                systemState,
                decision,
                lastOutcome,
                ct
            );

            // 4. 如果权重有变化，更新因子库
            if (!WeightsAreEqual(currentWeights, optimizedWeights))
            {
                _factorLibrary.UpdateFactorWeights(optimizedWeights);
                LogService.Info("✅ [AICentralCoordinator] 因子权重已更新");
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AICentralCoordinator] 更新因子权重失败");
        }
    }

    /// <summary>
    /// 🆕 获取最近的决策结果
    /// </summary>
    private async Task<DecisionOutcome?> GetLastDecisionOutcomeAsync(CancellationToken ct)
    {
        // TODO: 从交易历史获取最近一笔交易的结果
        // 这里返回模拟数据
        await Task.CompletedTask;

        var account = _accountManager.ActiveAccount;
        if (account == null || account.TotalTrades == 0)
        {
            return null;
        }

        // 简化实现：基于最近的盈亏
        return new DecisionOutcome
        {
            Success = account.TodayPnL >= 0,
            ProfitPercent = account.TodayReturnPercent,
            Duration = 1.0, // 假设持续1小时
            Message = account.TodayPnL >= 0 ? "盈利" : "亏损"
        };
    }

    /// <summary>
    /// 🆕 比较权重是否相等
    /// </summary>
    private bool WeightsAreEqual(
        Dictionary<string, decimal> weights1,
        Dictionary<string, decimal> weights2)
    {
        if (weights1.Count != weights2.Count)
        {
            return false;
        }

        foreach (var (key, value1) in weights1)
        {
            if (!weights2.TryGetValue(key, out var value2))
            {
                return false;
            }

            // 权重差异小于0.001视为相等
            if (Math.Abs(value1 - value2) > 0.001m)
            {
                return false;
            }
        }

        return true;
    }

    #endregion

    #region 状态收集

    /// <summary>
    /// 收集全系统状态
    /// </summary>
    private async Task<SystemState> CollectSystemStateAsync(CancellationToken ct)
    {
        try
        {
            // 收集市场状态
            var marketCondition = await CollectMarketConditionAsync(ct);

            // 收集账户状态
            var accountStatus = CollectAccountStatus();

            // 收集策略状态
            var strategyStatus = await CollectStrategyStatusAsync(ct);

            // 收集风险指标
            var riskMetrics = await CollectRiskMetricsAsync(ct);

            // 收集系统资源
            var systemResources = CollectSystemResources();

            var state = new SystemState
            {
                Timestamp = DateTime.UtcNow,
                CurrentStage = _stateManager.CurrentStage,
                MarketCondition = marketCondition,
                AccountStatus = accountStatus,
                StrategyStatus = strategyStatus,
                RiskMetrics = riskMetrics,
                SystemResources = systemResources
            };

            // 🆕 设置模拟和实盘开始时间
            if (state.CurrentStage == WorkflowStage.Simulation && state.SimulationStartTime == default)
            {
                state.SimulationStartTime = DateTime.UtcNow;
            }

            if (state.CurrentStage == WorkflowStage.Live && state.LiveTradingStartTime == default)
            {
                state.LiveTradingStartTime = DateTime.UtcNow;
            }

            return state;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AICentralCoordinator] 收集系统状态失败");
            throw;
        }
    }

    /// <summary>
    /// 收集市场状况
    /// </summary>
    private async Task<MarketCondition> CollectMarketConditionAsync(CancellationToken ct)
    {
        // 获取市场波动性、流动性等指标
        // 简化实现：基于BTC价格波动
        try
        {
            var klines = await _apiClient.GetKlineClosesAsync("BTCUSDT", "1h", 24, ct);
            var prices = klines.Select(k => (double)k).ToArray();

            var volatility = CalculateVolatility(prices);
            var trend = CalculateTrend(prices);

            return new MarketCondition
            {
                Volatility = volatility,
                Trend = trend,
                Liquidity = 1.0, // 简化：假设流动性正常
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AICentralCoordinator] 收集市场状况失败");
            return new MarketCondition
            {
                Volatility = 0.5,
                Trend = 0,
                Liquidity = 1.0,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 收集账户状态
    /// </summary>
    private AccountStatus CollectAccountStatus()
    {
        var account = _accountManager.ActiveAccount;

        if (account == null)
        {
            return new AccountStatus
            {
                Type = AccountType.Simulated,
                NetValue = 0,
                AvailableBalance = 0,
                PositionValue = 0,
                TodayPnL = 0,
                TotalPnL = 0,
                WinRate = 0,
                OpenPositionCount = 0,
                Timestamp = DateTime.UtcNow
            };
        }

        return new AccountStatus
        {
            Type = account.Type,
            NetValue = account.NetValue,
            AvailableBalance = account.AvailableBalance,
            PositionValue = account.PositionValue,
            TodayPnL = account.TodayPnL,
            TotalPnL = account.TotalPnL,
            WinRate = account.WinRate,
            OpenPositionCount = account.OpenPositionCount,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 收集策略状态
    /// </summary>
    private async Task<StrategyStatus> CollectStrategyStatusAsync(CancellationToken ct)
    {
        // 简化实现：返回基本策略状态
        await Task.CompletedTask;

        return new StrategyStatus
        {
            IsActive = _tradingAutomation.IsRunning,
            PerformanceScore = 0.7, // TODO: 从历史数据计算
            RecentSignalCount = 0,
            AverageConfidence = 0.75,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 收集风险指标
    /// </summary>
    private async Task<RiskMetrics> CollectRiskMetricsAsync(CancellationToken ct)
    {
        await Task.CompletedTask;

        var account = _accountManager.ActiveAccount;
        if (account == null)
        {
            return new RiskMetrics
            {
                MaxDrawdown = 0,
                CurrentDrawdown = 0,
                DailyLoss = 0,
                Leverage = 1.0m,
                Timestamp = DateTime.UtcNow
            };
        }

        return new RiskMetrics
        {
            MaxDrawdown = 0, // TODO: 计算最大回撤
            CurrentDrawdown = 0,
            DailyLoss = account.TodayPnL < 0 ? Math.Abs(account.TodayPnL) : 0,
            Leverage = 1.0m,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 收集系统资源 (Phase 3: 实现真实监控)
    /// </summary>
    private SystemResources CollectSystemResources()
    {
        try
        {
            // 🆕 Phase 3: 使用SystemResourceMonitor获取真实数据
            if (OperatingSystem.IsWindows())
            {
                var snapshot = _resourceMonitor!.GetSnapshot();
                var healthStatus = _resourceMonitor.GetHealthStatus();

                return new SystemResources
                {
                    CpuUsage = snapshot.CpuUsagePercent / 100.0,  // 转换为0-1范围
                    MemoryUsage = snapshot.MemoryUsageMB / 1024.0,  // MB转GB
                    NetworkLatency = (int)snapshot.NetworkLatencyMs,
                    ActiveTasks = snapshot.ThreadCount,
                    Timestamp = DateTime.UtcNow
                };
            }
            else
            {
                // 非 Windows 环境，返回降级默认值
                return new SystemResources
                {
                    CpuUsage = 0,
                    MemoryUsage = 0,
                    NetworkLatency = 0,
                    ActiveTasks = 0,
                    Timestamp = DateTime.UtcNow
                };
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AICentralCoordinator] 收集系统资源失败");

            // 返回默认值
            return new SystemResources
            {
                CpuUsage = 0,
                MemoryUsage = 0,
                NetworkLatency = 0,
                ActiveTasks = 0,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 🆕 Phase 4: 带可观测性的状态收集
    /// </summary>
    private async Task<SystemState> CollectSystemStateWithObservabilityAsync(CancellationToken ct)
    {
        return await _observability.ObserveOperationAsync(
            "CollectSystemState",
            async () => await CollectSystemStateAsync(ct)
        );
    }

    /// <summary>
    /// 🆕 Phase 4: 带可观测性的因子计算
    /// </summary>
    private async Task<Dictionary<string, decimal>> CalculateDecisionFactorsWithObservabilityAsync(
        SystemState systemState,
        CancellationToken ct)
    {
        return await _observability.ObserveOperationAsync(
            "CalculateDecisionFactors",
            async () =>
            {
                var factors = await CalculateDecisionFactorsAsync(systemState, ct);

                // 记录因子计算详情
                _observability.LogDebug("决策因子计算完成", new
                {
                    factorCount = factors.Count,
                    topFactors = factors.OrderByDescending(f => Math.Abs(f.Value))
                        .Take(5)
                        .Select(f => $"{f.Key}={f.Value:F3}")
                        .ToList()
                });

                return factors;
            }
        );
    }
    #endregion

    #region 辅助方法

    /// <summary>
    /// 计算价格波动率
    /// </summary>
    private double CalculateVolatility(double[] prices)
    {
        if (prices.Length < 2)
        {
            return 0;
        }

        var returns = new List<double>();
        for (var i = 1; i < prices.Length; i++)
        {
            returns.Add((prices[i] - prices[i - 1]) / prices[i - 1]);
        }

        var mean = returns.Average();
        var variance = returns.Sum(r => Math.Pow(r - mean, 2)) / returns.Count;
        return Math.Sqrt(variance);
    }

    /// <summary>
    /// 计算价格趋势
    /// </summary>
    private double CalculateTrend(double[] prices)
    {
        if (prices.Length < 2)
        {
            return 0;
        }

        // 简单线性回归斜率
        var n = prices.Length;
        var sumX = 0.0;
        var sumY = 0.0;
        var sumXY = 0.0;
        var sumX2 = 0.0;

        for (var i = 0; i < n; i++)
        {
            sumX += i;
            sumY += prices[i];
            sumXY += i * prices[i];
            sumX2 += i * i;
        }

        var slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
        return slope / prices.Average(); // 归一化斜率
    }

    /// <summary>
    /// 订阅模块事件
    /// </summary>
    private void SubscribeToEvents()
    {
        // 订阅回测完成事件
        _eventBus.Subscribe<BacktestCompletedEvent>(OnBacktestCompleted);

        // 订阅模拟交易事件
        _eventBus.Subscribe<SimulationUpdateEvent>(OnSimulationUpdate);

        // 订阅实盘交易事件
        _eventBus.Subscribe<LiveTradeEvent>(OnLiveTrade);

        // 订阅风险警报事件
        _eventBus.Subscribe<RiskAlertEvent>(OnRiskAlert);

        // 新事件：信号生命周期
        _eventBus.Subscribe<AITradingSignalGeneratedEvent>(async e =>
        {
            // 可在此应用额外规则，如黑名单交易对、交易时段过滤等
            _observability.LogInfo("SignalGenerated", new { e.Signal.Symbol, e.Signal.Action, e.Signal.Confidence });
            await Task.CompletedTask;
        });

        _eventBus.Subscribe<AITradingSignalExecutedEvent>(async e =>
        {
            // 把结果反馈给学习模块
            var outcome = new DecisionOutcome
            {
                Success = e.Result.IsSuccess,
                ProfitPercent = e.Result.IsSuccess ? 0.01 : -0.01, // TODO: 从成交和后续PnL计算
                Duration = 1,
                Message = e.Result.IsSuccess ? "success" : e.Result.Error ?? "failed"
            };
            await _learningModule.UpdateDecisionOutcomeAsync(DateTime.UtcNow, outcome);
        });
    }

    // expose设置闸门到执行引擎的入口
    public void AttachGlobalGateTo(AIOrderExecutionEngine exec)
    {
        exec.SetGlobalGate(_globalGate);
    }

    #endregion

    #region 手动干预控制

    /// <summary>
    /// 手动切换到指定阶段
    /// </summary>
    public async Task<bool> ManualTransitionToStageAsync(WorkflowStage targetStage)
    {
        try
        {
            LogService.Info($"👤 [AICentralCoordinator] 手动切换到阶段: {targetStage.GetDisplayName()}");

            // 验证状态转换是否有效
            if (!_stateManager.CanTransitionTo(targetStage))
            {
                LogService.Warning($"❌ [AICentralCoordinator] 无法从 {CurrentStage.GetDisplayName()} 切换到 {targetStage.GetDisplayName()}");
                return false;
            }

            await _workflowOrchestrator.TransitionToStageAsync(targetStage);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AICentralCoordinator] 手动阶段切换失败");
            return false;
        }
    }

    /// <summary>
    /// 手动暂停交易
    /// </summary>
    public async Task PauseAutomationAsync()
    {
        try
        {
            LogService.Info("⏸️ [AICentralCoordinator] 手动暂停自动交易");

            if (_tradingAutomation.IsRunning)
            {
                await _tradingAutomation.StopAsync();
            }

            await _eventBus.PublishAsync(new ManualInterventionEvent
            {
                Action = "Pause",
                Reason = "用户手动暂停",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AICentralCoordinator] 手动暂停失败");
        }
    }

    /// <summary>
    /// 手动恢复交易
    /// </summary>
    public async Task ResumeAutomationAsync()
    {
        try
        {
            LogService.Info("▶️ [AICentralCoordinator] 手动恢复自动交易");

            if (!_tradingAutomation.IsRunning && CurrentStage.CanTrade())
            {
                // TODO: 从配置中获取交易对列表
                var symbols = new[] { "BTCUSDT" };
                var accountType = _accountManager.ActiveAccount?.Type ?? AccountType.Simulated;
                await _tradingAutomation.StartAsync(symbols, accountType);
            }

            await _eventBus.PublishAsync(new ManualInterventionEvent
            {
                Action = "Resume",
                Reason = "用户手动恢复",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AICentralCoordinator] 手动恢复失败");
        }
    }

    /// <summary>
    /// 手动触发紧急停止
    /// </summary>
    public async Task EmergencyStopAsync(string reason)
    {
        try
        {
            LogService.Error($"🚨 [AICentralCoordinator] 紧急停止: {reason}");

            // 停止自动交易
            await _tradingAutomation.StopAsync();

            // 关闭所有持仓
            await _positionManager.CloseAllPositionsAsync($"紧急停止: {reason}");

            // 切换到Emergency阶段
            await _workflowOrchestrator.TransitionToStageAsync(WorkflowStage.Emergency);

            // 发布风险警报
            await _eventBus.PublishAsync(new RiskAlertEvent
            {
                RiskType = "Emergency",
                Severity = RiskSeverity.Critical,
                Message = $"手动紧急停止: {reason}",
                CurrentValue = 0,
                Threshold = 0,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AICentralCoordinator] 紧急停止失败");
        }
    }

    /// <summary>
    /// 手动清零账户（危险操作）
    /// </summary>
    public async Task<bool> ManualResetAccountAsync(bool confirmed = false)
    {
        if (!confirmed)
        {
            LogService.Warning("⚠️ [AICentralCoordinator] 账户重置需要二次确认");
            return false;
        }

        try
        {
            LogService.Warning("🔄 [AICentralCoordinator] 重置账户状态");

            // 关闭所有持仓
            await _positionManager.CloseAllPositionsAsync("账户重置");

            // 重置账户（如果是模拟账户）
            var account = _accountManager.ActiveAccount;
            if (account != null && account.Type == AccountType.Simulated)
            {
                // 创建新的模拟账户
                _accountManager.CreateSimulatedAccount("模拟账户", 10000m);
                LogService.Info("✅ [AICentralCoordinator] 模拟账户已重置");
                return true;
            }
            else
            {
                LogService.Error("❌ [AICentralCoordinator] 无法重置实盘账户");
                return false;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AICentralCoordinator] 账户重置失败");
            return false;
        }
    }

    /// <summary>
    /// 手动触发优化
    /// </summary>
    public async Task TriggerOptimizationAsync()
    {
        try
        {
            LogService.Info("🔧 [AICentralCoordinator] 手动触发参数优化");

            await _workflowOrchestrator.TransitionToStageAsync(WorkflowStage.Optimization);

            await _eventBus.PublishAsync(new ManualInterventionEvent
            {
                Action = "TriggerOptimization",
                Reason = "用户手动触发优化",
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AICentralCoordinator] 触发优化失败");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _observability.LogInfo("释放AI协调器资源");

        _mainLoopCts?.Cancel();
        _mainLoopCts?.Dispose();
        _eventBus.Dispose();
        _resourceMonitor?.Dispose();
        _observability?.Dispose();  // 🆕 释放可观测性服务

        LogService.Info("[AICentralCoordinator] 协调器已释放资源");
    }

    #endregion

    // Handlers added at bottom of file
    private async Task OnBacktestCompleted(BacktestCompletedEvent evt)
    {
        LogService.Info("[Coordinator] 回测完成: {Strategy} Sharpe={Sharpe:F2}", evt.StrategyName, evt.SharpeRatio);
        await Task.CompletedTask;
    }

    private async Task OnSimulationUpdate(SimulationUpdateEvent evt)
    {
        LogService.Info("[Coordinator] 模拟更新 Pct={Pct:F2} WinRate={WinRate:P2}", evt.ProfitPercent, evt.WinRate);
        await Task.CompletedTask;
    }

    private async Task OnLiveTrade(LiveTradeEvent evt)
    {
        LogService.Debug("[Coordinator] 实盘成交 {Symbol} {Action} Qty={Qty}", evt.Symbol, evt.Action, evt.Quantity);
        await Task.CompletedTask;
    }

    private async Task OnRiskAlert(RiskAlertEvent evt)
    {
        LogService.Warning("[Coordinator] 风险警报 {Type} {Msg}", evt.RiskType, evt.Message);
        await Task.CompletedTask;
    }

    #region 策略参数适配

    /// <summary>
    /// 根据因子得分调整策略参数
    /// </summary>
    private void AdaptStrategyParameters(Dictionary<string, decimal> factorScores)
    {
        // 简单因子→参数映射示例（可从配置扩展）
        var mapping = new Dictionary<string, string>
        {
            ["MOMENTUM"] = "entry_z_score",
            ["VOLATILITY"] = "stop_multiplier",
            ["LIQUIDITY"] = "base_quantity"
        };
        var running = ServiceLocator.StrategyPortfolio.GetRunningStrategies();
        foreach (var s in running)
        {
            foreach (var kv in mapping)
            {
                if (!factorScores.TryGetValue(kv.Key, out var score))
                {
                    continue;
                }
                double current = s.Parameters.TryGetValue(kv.Value, out var p) ? p : 1.0;
                double target = current * (1.0 + (double)score * 0.05); // 微调5% * 因子
                // 限幅
                target = Math.Clamp(target, 0.1, 10.0);
                s.Parameters[kv.Value] = target;
            }
        }
    }

    #endregion
}
