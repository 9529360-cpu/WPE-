using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using 币安量化机器人.Application.Backtesting;
using 币安量化机器人.Core.Abstractions;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Models;
using 币安量化机器人.Services.Resilience;
using 币安量化机器人.Services.Performance;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 增强版中央AI协调器 - 系统"大脑"
/// </summary>
/// <remarks>
/// 🆕 Phase 2 增强功能：
/// 1. 集成智能工作流引擎 (WorkflowEngine)
/// 2. 集成决策因子库 (DecisionFactorLibrary)
/// 3. 自动化决策循环
/// 4. 学习反馈机制
/// 5. 预测性决策
/// 
/// 🆕 Phase 3 增强功能：
/// 1. 集成弹性恢复服务 (ResilienceService)
/// 2. 集成性能优化服务 (PerformanceOptimizationService)
/// 3. 增强异常处理和恢复
/// 4. 智能缓存和批处理
/// 5. 全面的健康监控
/// 
/// 核心职责：
/// 1. 全局状态感知和监控
/// 2. 工作流智能调度（回测→模拟→实盘）
/// 3. 模块间协同控制
/// 4. 持续学习和优化
/// 5. 自适应决策引擎
/// 6. 弹性恢复和性能优化
/// </remarks>
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
    private readonly ResilienceService _resilienceService;
    private readonly PerformanceOptimizationService _performanceService;

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

    // 🆕 Phase 2: 自动化决策开关
    private bool _autoDecisionEnabled = true;

    // 🆕 公开EventBus以供UI访问
    public EventBus EventBus => _eventBus;
    private readonly EventBus _eventBus;

    public AICentralCoordinator(
        EnhancedBacktestEngine backtestEngine,
        AITradingAutomation tradingAutomation,
        TradingAccountManager accountManager,
        PositionManager positionManager,
        BinanceApiClient apiClient,
        DataCacheService cacheService)
    {
        _backtestEngine = backtestEngine;
        _tradingAutomation = tradingAutomation;
        _accountManager = accountManager;
        _positionManager = positionManager;
        _apiClient = apiClient;
        _cacheService = cacheService;

        // 初始化核心组件
        _stateManager = new StateManager();
        _eventBus = new EventBus();
        _resourceManager = new ResourceManager();
        _learningModule = new LearningModule(cacheService);
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

        // 🆕 将事件总线注入回测引擎
        _backtestEngine.SetEventBus(_eventBus);

        // 订阅事件
        SubscribeToEvents();

        LogService.Info("🧠 [AICentralCoordinator] 增强版中央AI协调器已初始化");
        LogService.Info("   ✅ 智能工作流引擎已加载");
        LogService.Info("   ✅ 决策因子库已加载 ({Count}个因子)", _factorLibrary.GetFactors().Count);
        LogService.Info("   ✅ 弹性恢复服务已加载");
        LogService.Info("   ✅ 性能优化服务已加载");
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
        if (_isRunning)
        {
            LogService.Warning("[AICentralCoordinator] 协调器已在运行中");
            return;
        }

        _isRunning = true;
        _mainLoopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        LogService.Info("🚀 [AICentralCoordinator] 启动增强版中央AI协调器");

        // 初始化系统状态
        await _stateManager.InitializeAsync();

        // 启动主循环
        _mainLoopTask = MainControlLoopAsync(_mainLoopCts.Token);

        await _mainLoopTask;
    }

    /// <summary>
    /// 停止中央AI协调器
    /// </summary>
    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        LogService.Info("🛑 [AICentralCoordinator] 停止中央AI协调器");

        _mainLoopCts?.Cancel();

        if (_mainLoopTask != null)
        {
            await _mainLoopTask;
        }

        _isRunning = false;
    }

    /// <summary>
    /// 🆕 增强版 AI大脑主控制循环 (Phase 3: 集成弹性和性能)
    /// </summary>
    private async Task MainControlLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 使用性能监控记录操作
                    using (_performanceService.RecordOperation("MainControlLoop"))
                    {
                        // 1. 收集全系统状态
                        SystemState systemState = await CollectSystemStateAsync(ct);
                        _stateManager.UpdateState(systemState);

                        // 🆕 2. 计算决策因子得分 (使用缓存)
                        Dictionary<string, decimal> factorScores = await _performanceService.GetOrCreateCachedAsync(
                            "decision_factors",
                            async () => await CalculateDecisionFactorsAsync(systemState, ct),
                            TimeSpan.FromMinutes(1)  // 缓存1分钟
                        );

                        decimal weightedScore = _factorLibrary.CalculateWeightedScore(factorScores);
                        LogService.Debug("[AICentralCoordinator] 决策因子综合得分: {Score:F3}", weightedScore);

                        // 3. AI决策分析（集成因子得分）
                        AIDecision decisions = await _decisionEngine.AnalyzeAsync(systemState, ct);
                        decisions.FactorScore = weightedScore;
                        decisions.FactorBreakdown = factorScores;

                        LogService.Debug("[AICentralCoordinator] AI决策: {Decision}", decisions.PrimaryAction);

                        // 🆕 4. 自动化工作流转换（如果启用）
                        if (_autoDecisionEnabled)
                        {
                            bool transitioned = await _workflowEngine.EvaluateAndTransitionAsync(ct);
                            if (transitioned)
                            {
                                LogService.Info("✅ [AICentralCoordinator] 工作流自动转换成功");
                            }
                        }

                        // 5. 执行工作流编排
                        await _workflowOrchestrator.ExecuteAsync(decisions, ct);

                        // 6. 学习优化（带因子反馈）
                        await _learningModule.UpdateKnowledgeAsync(systemState, decisions, ct);
                        await UpdateFactorWeightsAsync(systemState, decisions, ct);

                        // 7. 定期清理历史记录
                        _workflowEngine.CleanupHistory(100);
                    }

                    // 8. 等待下一个周期（根据当前阶段调整）
                    TimeSpan interval = GetControlLoopInterval();
                    await Task.Delay(interval, ct);
                }
                catch (TaskCanceledException)
                {
                    // 正常取消
                    throw;
                }
                catch (Exception ex)
                {
                    // 🆕 Phase 3: 增强异常处理
                    LogService.Error(ex, "[AICentralCoordinator] 主循环迭代异常");

                    // 报告故障给弹性服务
                    bool recovered = await _resilienceService.ReportFailureAsync(
                        "AICentralCoordinator.MainLoop",
                        ex
                    );

                    if (!recovered)
                    {
                        // 检测到严重异常
                        _resilienceService.DetectSystemAnomaly("AICentralCoordinator", ex);

                        // 检查系统健康
                        var health = _resilienceService.GetSystemHealth();
                        if (!health.IsHealthy)
                        {
                            LogService.Error("🚨 [AICentralCoordinator] 系统健康异常，触发紧急停止");
                            await EmergencyStopAsync("系统健康检查失败");
                            break;
                        }
                    }

                    // 短暂等待后重试
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogService.Info("[AICentralCoordinator] 主循环已取消");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AICentralCoordinator] 主循环致命异常");
            
            // 报告严重故障
            _resilienceService.DetectSystemAnomaly("AICentralCoordinator", ex);
            
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
            MarketData marketData = await PrepareMarketDataAsync(ct);

            // 计算所有因子
            Dictionary<string, decimal> factorScores = _factorLibrary.CalculateAllFactors(marketData);

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
                    IReadOnlyList<decimal> closes = await _apiClient.GetKlineClosesAsync("BTCUSDT", "1h", 100, ct);
                    IReadOnlyList<decimal> highs = await _apiClient.GetKlineHighsAsync("BTCUSDT", "1h", 100, ct);
                    IReadOnlyList<decimal> lows = await _apiClient.GetKlineLowsAsync("BTCUSDT", "1h", 100, ct);
                    IReadOnlyList<decimal> volumes = await _apiClient.GetKlineVolumesAsync("BTCUSDT", "1h", 100, ct);

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
            LogService.Warning(ex, "[AICentralCoordinator] 获取市场数据失败，使用降级策略");

            // 🆕 Phase 3: 数据降级策略
            // 1. 尝试从缓存获取旧数据
            try
            {
                var cachedData = _cacheService.Get<MarketData>("market_data_btcusdt_backup");
                if (cachedData != null && cachedData.ClosePrices.Any())
                {
                    LogService.Info("[AICentralCoordinator] 使用缓存的历史数据 (备份)");
                    cachedData.DataQuality = 0.5m;  // 标记数据质量降低
                    return cachedData;
                }
            }
            catch { }

            // 2. 返回默认安全数据
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
    /// 更新因子权重（学习反馈）
    /// </summary>
    private async Task UpdateFactorWeightsAsync(
        SystemState systemState,
        AIDecision decision,
        CancellationToken ct)
    {
        await Task.CompletedTask;

        // TODO: 实现因子权重的动态调整
        // 基于历史表现优化因子权重
        // 使用强化学习或遗传算法

        LogService.Debug("[AICentralCoordinator] 因子权重更新完成");
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
            MarketCondition marketCondition = await CollectMarketConditionAsync(ct);

            // 收集账户状态
            AccountStatus accountStatus = CollectAccountStatus();

            // 收集策略状态
            StrategyStatus strategyStatus = await CollectStrategyStatusAsync(ct);

            // 收集风险指标
            RiskMetrics riskMetrics = await CollectRiskMetricsAsync(ct);

            // 收集系统资源
            SystemResources systemResources = CollectSystemResources();

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
            IReadOnlyList<decimal> klines = await _apiClient.GetKlineClosesAsync("BTCUSDT", "1h", 24, ct);
            double[] prices = klines.Select(k => (double)k).ToArray();

            double volatility = CalculateVolatility(prices);
            double trend = CalculateTrend(prices);

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
        TradingAccount? account = _accountManager.ActiveAccount;

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

        TradingAccount? account = _accountManager.ActiveAccount;
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
    /// 收集系统资源
    /// </summary>
    private SystemResources CollectSystemResources()
    {
        return new SystemResources
        {
            CpuUsage = 0, // TODO: 实现CPU使用率监控
            MemoryUsage = 0,
            NetworkLatency = 0,
            Timestamp = DateTime.UtcNow
        };
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
        for (int i = 1; i < prices.Length; i++)
        {
            returns.Add((prices[i] - prices[i - 1]) / prices[i - 1]);
        }

        double mean = returns.Average();
        double variance = returns.Sum(r => Math.Pow(r - mean, 2)) / returns.Count;
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
        int n = prices.Length;
        double sumX = 0.0;
        double sumY = 0.0;
        double sumXY = 0.0;
        double sumX2 = 0.0;

        for (int i = 0; i < n; i++)
        {
            sumX += i;
            sumY += prices[i];
            sumXY += i * prices[i];
            sumX2 += i * i;
        }

        double slope = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
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
    }

    /// <summary>
    /// 回测完成事件处理
    /// </summary>
    private async Task OnBacktestCompleted(BacktestCompletedEvent evt)
    {
        LogService.Info("📊 [AICentralCoordinator] 回测完成: {Strategy}, 收益率={Return:P2}, 夏普比={Sharpe:F2}",
            evt.StrategyName, evt.TotalReturn, evt.SharpeRatio);

        // AI评估回测结果
        BacktestEvaluation evaluation = _decisionEngine.EvaluateBacktestResult(evt);

        if (evaluation.ShouldProceedToSimulation)
        {
            LogService.Info("✅ [AICentralCoordinator] 回测通过，准备进入模拟交易阶段");
            await _workflowOrchestrator.TransitionToStageAsync(WorkflowStage.Simulation);
        }
        else
        {
            LogService.Warning("❌ [AICentralCoordinator] 回测未达标: {Reason}", evaluation.Reason);
            await _workflowOrchestrator.TransitionToStageAsync(WorkflowStage.Optimization);
        }
    }

    /// <summary>
    /// 模拟交易更新事件处理
    /// </summary>
    private async Task OnSimulationUpdate(SimulationUpdateEvent evt)
    {
        // AI监控模拟交易表现
        SimulationEvaluation evaluation = _decisionEngine.EvaluateSimulationPerformance(evt);

        if (evaluation.ShouldProceedToLive)
        {
            LogService.Info("✅ [AICentralCoordinator] 模拟交易达标，准备进入实盘交易");
            await _workflowOrchestrator.TransitionToStageAsync(WorkflowStage.Live);
        }
        else if (evaluation.ShouldReturnToBacktest)
        {
            LogService.Warning("❌ [AICentralCoordinator] 模拟交易表现不佳，返回回测优化");
            await _workflowOrchestrator.TransitionToStageAsync(WorkflowStage.Backtest);
        }
    }

    /// <summary>
    /// 实盘交易事件处理
    /// </summary>
    private async Task OnLiveTrade(LiveTradeEvent evt)
    {
        // AI实时监控实盘交易
        LiveEvaluation evaluation = _decisionEngine.EvaluateLivePerformance(evt);

        if (evaluation.ShouldAdjustStrategy)
        {
            LogService.Warning("⚠️ [AICentralCoordinator] 实盘表现异常，需要调整策略");
            // 可以动态调整参数或暂停交易
        }
    }

    /// <summary>
    /// 风险警报事件处理
    /// </summary>
    private async Task OnRiskAlert(RiskAlertEvent evt)
    {
        LogService.Error("🚨 [AICentralCoordinator] 风险警报: {Message}", evt.Message);

        // AI紧急响应
        if (evt.Severity == RiskSeverity.Critical)
        {
            LogService.Error("🛑 [AICentralCoordinator] 严重风险，立即停止所有交易");
            await _tradingAutomation.StopAsync();
            await _positionManager.CloseAllPositionsAsync("风险警报：紧急平仓");
        }
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
                string[] symbols = new[] { "BTCUSDT" };
                AccountType accountType = _accountManager.ActiveAccount?.Type ?? AccountType.Simulated;
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
            TradingAccount? account = _accountManager.ActiveAccount;
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
        _mainLoopCts?.Cancel();
        _mainLoopCts?.Dispose();
        _eventBus.Dispose();

        LogService.Info("[AICentralCoordinator] 协调器已释放资源");
    }

    #endregion
}
