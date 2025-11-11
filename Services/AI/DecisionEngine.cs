using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// AI决策引擎
/// </summary>
/// <remarks>
/// 核心职责：
/// 1. 分析系统状态
/// 2. 做出智能决策
/// 3. 评估各阶段结果
/// 4. 决定工作流切换
/// 5. 基于历史学习优化决策
/// </remarks>
public class DecisionEngine
{
    private readonly LearningModule _learningModule;
    private readonly StateManager _stateManager;
    private readonly List<DecisionRule> _rules;

    public DecisionEngine(LearningModule learningModule, StateManager stateManager)
    {
        _learningModule = learningModule;
        _stateManager = stateManager;
        _rules = InitializeDecisionRules();
    }

    /// <summary>
    /// 分析系统状态并做出决策
    /// </summary>
    public async Task<AIDecision> AnalyzeAsync(SystemState state, CancellationToken ct = default)
    {
        try
        {
            // 1. 基于规则的初步决策
            (DecisionAction Action, double Confidence, string Reason) ruleBasedDecision = ApplyRules(state);

            // 2. 基于历史学习的优化
            (DecisionAction Action, double Confidence, string Reason) optimizedDecision = await _learningModule.OptimizeDecisionAsync(ruleBasedDecision, state, ct);

            // 3. 风险评估
            (RiskLevel Level, string Reason) riskAssessment = AssessRisk(state);

            // 4. 生成最终决策
            var decision = new AIDecision
            {
                Timestamp = DateTime.UtcNow,
                CurrentStage = state.CurrentStage,
                PrimaryAction = optimizedDecision.Action,
                Confidence = optimizedDecision.Confidence,
                Reason = optimizedDecision.Reason,
                RiskLevel = riskAssessment.Level,
                RecommendedStage = DetermineRecommendedStage(state, optimizedDecision),
                Parameters = new Dictionary<string, object>() // 🔧 使用空字典
            };

            LogService.Info("🧠 [DecisionEngine] AI决策: {Action} (信心度={Confidence:P0}, 风险={Risk})",
                decision.PrimaryAction, decision.Confidence, decision.RiskLevel);

            return decision;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[DecisionEngine] 决策分析失败");

            // 返回保守决策
            return new AIDecision
            {
                Timestamp = DateTime.UtcNow,
                CurrentStage = state.CurrentStage,
                PrimaryAction = DecisionAction.Hold,
                Confidence = 0,
                Reason = $"决策失败: {ex.Message}",
                RiskLevel = RiskLevel.High,
                RecommendedStage = WorkflowStage.Idle,
                Parameters = new Dictionary<string, object>()
            };
        }
    }

    #region 回测评估

    /// <summary>
    /// 评估回测结果
    /// </summary>
    public BacktestEvaluation EvaluateBacktestResult(BacktestCompletedEvent evt)
    {
        // 评估标准
        double minSharpeRatio = 1.5;
        double maxDrawdown = 0.15;
        double minWinRate = 0.55;
        double minTotalReturn = 0.20; // 至少20%收益

        bool passed = evt.SharpeRatio >= minSharpeRatio
                     && evt.MaxDrawdown <= maxDrawdown
                     && evt.WinRate >= minWinRate
                     && evt.TotalReturn >= minTotalReturn;

        string reason = passed
            ? "回测指标达标"
            : $"回测未达标: 夏普比={evt.SharpeRatio:F2} (要求>={minSharpeRatio}), " +
              $"最大回撤={evt.MaxDrawdown:P2} (要求<={maxDrawdown:P0}), " +
              $"胜率={evt.WinRate:P2} (要求>={minWinRate:P0}), " +
              $"总收益={evt.TotalReturn:P2} (要求>={minTotalReturn:P0})";

        return new BacktestEvaluation
        {
            ShouldProceedToSimulation = passed,
            ShouldOptimize = !passed && evt.TotalReturn > 0,
            Reason = reason,
            Score = CalculateBacktestScore(evt)
        };
    }

    /// <summary>
    /// 计算回测评分
    /// </summary>
    private double CalculateBacktestScore(BacktestCompletedEvent evt)
    {
        // 综合评分 (0-1)
        double sharpeScore = Math.Min(evt.SharpeRatio / 3.0, 1.0);
        double drawdownScore = 1.0 - Math.Min(evt.MaxDrawdown / 0.3, 1.0);
        double winRateScore = evt.WinRate;
        double returnScore = Math.Min(evt.TotalReturn / 0.5, 1.0);

        return (sharpeScore + drawdownScore + winRateScore + returnScore) / 4.0;
    }

    #endregion

    #region 模拟交易评估

    /// <summary>
    /// 评估模拟交易表现
    /// </summary>
    public SimulationEvaluation EvaluateSimulationPerformance(SimulationUpdateEvent evt)
    {
        // 评估标准
        double minProfitPercent = 0.15; // 至少15%盈利
        double maxDrawdown = 0.10; // 最大回撤不超过10%
        double minWinRate = 0.60; // 胜率至少60%
        int minTradeDays = 7; // 至少模拟7天

        double duration = (DateTime.UtcNow - evt.StartTime).TotalDays;

        bool passed = evt.ProfitPercent >= minProfitPercent
                     && evt.MaxDrawdown <= maxDrawdown
                     && evt.WinRate >= minWinRate
                     && duration >= minTradeDays;

        bool shouldReturnToBacktest = evt.ProfitPercent < -0.05 || evt.MaxDrawdown > 0.20;

        string reason = passed
            ? "模拟交易表现优秀，可进入实盘"
            : shouldReturnToBacktest
                ? "模拟交易表现不佳，需返回回测优化"
                : $"模拟交易中: 收益={evt.ProfitPercent:P2}, 回撤={evt.MaxDrawdown:P2}, 胜率={evt.WinRate:P2}, 天数={duration:F1}";

        return new SimulationEvaluation
        {
            ShouldProceedToLive = passed,
            ShouldReturnToBacktest = shouldReturnToBacktest,
            Reason = reason,
            Score = CalculateSimulationScore(evt)
        };
    }

    /// <summary>
    /// 计算模拟交易评分
    /// </summary>
    private double CalculateSimulationScore(SimulationUpdateEvent evt)
    {
        double profitScore = Math.Max(0, Math.Min(evt.ProfitPercent / 0.3, 1.0));
        double drawdownScore = 1.0 - Math.Min(evt.MaxDrawdown / 0.2, 1.0);
        double winRateScore = evt.WinRate;

        return (profitScore + drawdownScore + winRateScore) / 3.0;
    }

    #endregion

    #region 实盘评估

    /// <summary>
    /// 评估实盘表现
    /// </summary>
    public LiveEvaluation EvaluateLivePerformance(LiveTradeEvent evt)
    {
        // 实盘监控更严格
        bool shouldAdjust = evt.TodayLoss > 500m // 单日亏损超过500
                           || evt.MaxDrawdown > 0.12m // 最大回撤超过12%
                           || evt.ConsecutiveLosses > 3; // 连续3次亏损

        bool shouldPause = evt.TodayLoss > 1000m // 单日亏损超过1000
                          || evt.MaxDrawdown > 0.20m // 最大回撤超过20%
                          || evt.ConsecutiveLosses > 5; // 连续5次亏损

        string reason = shouldPause
            ? "实盘风险过高，建议暂停交易"
            : shouldAdjust
                ? "实盘表现异常，建议调整策略"
                : "实盘运行正常";

        return new LiveEvaluation
        {
            ShouldAdjustStrategy = shouldAdjust,
            ShouldPause = shouldPause,
            Reason = reason
        };
    }

    #endregion

    #region 决策规则

    /// <summary>
    /// 初始化决策规则
    /// </summary>
    private List<DecisionRule> InitializeDecisionRules()
    {
        return new List<DecisionRule>
        {
            // 规则1: 市场高波动时保守
            new DecisionRule
            {
                Name = "高波动保守规则",
                Condition = state => state.MarketCondition.IsHighVolatility,
                Action = DecisionAction.ReduceRisk,
                Priority = 1
            },
            
            // 规则2: 风险警报时停止
            new DecisionRule
            {
                Name = "风险警报规则",
                Condition = state => state.RiskMetrics.HasRiskAlert,
                Action = DecisionAction.Stop,
                Priority = 0 // 最高优先级
            },
            
            // 规则3: 策略表现良好时继续
            new DecisionRule
            {
                Name = "策略良好规则",
                Condition = state => state.StrategyStatus.IsPerformingWell
                                     && state.RiskMetrics.IsSafe,
                Action = DecisionAction.Continue,
                Priority = 2
            },
            
            // 规则4: 系统资源不足时暂停
            new DecisionRule
            {
                Name = "资源不足规则",
                Condition = state => !state.SystemResources.IsHealthy,
                Action = DecisionAction.Pause,
                Priority = 1
            },
            
            // 规则5: 账户盈利时可升级
            new DecisionRule
            {
                Name = "账户升级规则",
                Condition = state => state.AccountStatus.IsProfitable
                                     && state.AccountStatus.WinRate > 0.65
                                     && state.CurrentStage == WorkflowStage.Simulation,
                Action = DecisionAction.Upgrade,
                Priority = 2
            }
        };
    }

    /// <summary>
    /// 应用决策规则
    /// </summary>
    private (DecisionAction Action, double Confidence, string Reason) ApplyRules(SystemState state)
    {
        // 按优先级排序
        var sortedRules = _rules.OrderBy(r => r.Priority).ToList();

        foreach (DecisionRule? rule in sortedRules)
        {
            if (rule.Condition(state))
            {
                LogService.Debug("[DecisionEngine] 触发规则: {Rule} -> {Action}",
                    rule.Name, rule.Action);

                return (rule.Action, 0.9, $"触发规则: {rule.Name}");
            }
        }

        // 默认：继续
        return (DecisionAction.Continue, 0.5, "无特殊规则触发，继续当前状态");
    }

    #endregion

    #region 风险评估

    /// <summary>
    /// 评估风险等级
    /// </summary>
    private (RiskLevel Level, string Reason) AssessRisk(SystemState state)
    {
        // 综合风险评分
        double riskScore = 0.0;
        var reasons = new List<string>();

        // 市场波动性风险
        if (state.MarketCondition.Volatility > 0.7)
        {
            riskScore += 0.3;
            reasons.Add($"市场高波动({state.MarketCondition.Volatility:P0})");
        }

        // 账户风险
        if (state.RiskMetrics.MaxDrawdown > 0.15m)
        {
            riskScore += 0.3;
            reasons.Add($"回撤过大({state.RiskMetrics.MaxDrawdown:P2})");
        }

        // 策略风险
        if (!state.StrategyStatus.IsPerformingWell)
        {
            riskScore += 0.2;
            reasons.Add("策略表现不佳");
        }

        // 系统资源风险
        if (!state.SystemResources.IsHealthy)
        {
            riskScore += 0.2;
            reasons.Add("系统资源紧张");
        }

        RiskLevel level = riskScore switch
        {
            < 0.3 => RiskLevel.Low,
            < 0.6 => RiskLevel.Medium,
            _ => RiskLevel.High
        };

        string reason = reasons.Count > 0
            ? string.Join(", ", reasons)
            : "风险正常";

        return (level, reason);
    }

    #endregion

    #region 阶段推荐

    /// <summary>
    /// 决定推荐的工作流阶段
    /// </summary>
    private WorkflowStage DetermineRecommendedStage(
        SystemState state,
        (DecisionAction Action, double Confidence, string Reason) decision)
    {
        return decision.Action switch
        {
            DecisionAction.Stop => WorkflowStage.Emergency,
            DecisionAction.Pause => WorkflowStage.Idle,
            DecisionAction.Upgrade when state.CurrentStage == WorkflowStage.Simulation => WorkflowStage.Live,
            DecisionAction.Downgrade when state.CurrentStage == WorkflowStage.Live => WorkflowStage.Simulation,
            DecisionAction.Optimize => WorkflowStage.Optimization,
            _ => state.CurrentStage // 保持当前阶段
        };
    }

    #endregion
}

#region 决策相关模型

/// <summary>
/// AI决策
/// </summary>
public class AIDecision
{
    public DateTime Timestamp { get; init; }
    public WorkflowStage CurrentStage { get; init; }
    public DecisionAction PrimaryAction { get; init; }
    public double Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
    public RiskLevel RiskLevel { get; init; }
    public WorkflowStage RecommendedStage { get; init; }
    public Dictionary<string, object> Parameters { get; init; } = new();

    // 🆕 Phase 2: 决策因子相关
    /// <summary>
    /// 决策因子综合得分
    /// </summary>
    public decimal FactorScore { get; set; }

    /// <summary>
    /// 各因子得分明细
    /// </summary>
    public Dictionary<string, decimal> FactorBreakdown { get; set; } = new();
}

/// <summary>
/// 决策动作
/// </summary>
public enum DecisionAction
{
    /// <summary>
    /// 继续当前操作
    /// </summary>
    Continue,

    /// <summary>
    /// 暂停
    /// </summary>
    Pause,

    /// <summary>
    /// 停止（紧急）
    /// </summary>
    Stop,

    /// <summary>
    /// 升级（模拟→实盘）
    /// </summary>
    Upgrade,

    /// <summary>
    /// 降级（实盘→模拟）
    /// </summary>
    Downgrade,

    /// <summary>
    /// 优化参数
    /// </summary>
    Optimize,

    /// <summary>
    /// 降低风险
    /// </summary>
    ReduceRisk,

    /// <summary>
    /// 持有观望
    /// </summary>
    Hold
}

/// <summary>
/// 风险等级
/// </summary>
public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

/// <summary>
/// 决策规则
/// </summary>
public class DecisionRule
{
    public required string Name { get; init; }
    public required Func<SystemState, bool> Condition { get; init; }
    public required DecisionAction Action { get; init; }
    public int Priority { get; init; } // 0 = 最高优先级
}

/// <summary>
/// 回测评估结果
/// </summary>
public class BacktestEvaluation
{
    public bool ShouldProceedToSimulation { get; init; }
    public bool ShouldOptimize { get; init; }
    public string Reason { get; init; } = string.Empty;
    public double Score { get; init; }
}

/// <summary>
/// 模拟交易评估结果
/// </summary>
public class SimulationEvaluation
{
    public bool ShouldProceedToLive { get; init; }
    public bool ShouldReturnToBacktest { get; init; }
    public string Reason { get; init; } = string.Empty;
    public double Score { get; init; }
}

/// <summary>
/// 实盘评估结果
/// </summary>
public class LiveEvaluation
{
    public bool ShouldAdjustStrategy { get; init; }
    public bool ShouldPause { get; init; }
    public string Reason { get; init; } = string.Empty;
}

#endregion
