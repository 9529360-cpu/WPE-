using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.AI;

public class WorkflowEngine
{
    private readonly StateManager _stateManager;
    private readonly WorkflowOrchestrator _orchestrator;
    private readonly EventBus _eventBus;
    private readonly List<WorkflowRule> _rules;
    private readonly List<WorkflowTransition> _transitionHistory;
    private readonly SemaphoreSlim _transitionLock;

    public WorkflowEngine(StateManager stateManager, WorkflowOrchestrator orchestrator, EventBus eventBus)
    {
        _stateManager = stateManager;
        _orchestrator = orchestrator;
        _eventBus = eventBus;
        _rules = new List<WorkflowRule>();
        _transitionHistory = new List<WorkflowTransition>();
        _transitionLock = new SemaphoreSlim(1, 1);
        InitializeRules();
    }

    private void InitializeRules()
    {
        _rules.Add(new WorkflowRule
        {
            Name = "BacktestSuccess_To_Optimization",
            FromStage = WorkflowStage.Backtest,
            ToStage = WorkflowStage.Optimization,
            Condition = state => state.BacktestResults.Any() &&
                                 state.BacktestResults.Last().SharpeRatio > 1.5m &&
                                 state.BacktestResults.Last().WinRate > 0.55m,
            Priority = 10,
            Description = "回测表现良好，进入参数优化阶段"
        });

        _rules.Add(new WorkflowRule
        {
            Name = "OptimizationComplete_To_Simulation",
            FromStage = WorkflowStage.Optimization,
            ToStage = WorkflowStage.Simulation,
            Condition = state => state.OptimizationResults.Any() &&
                                 (state.BacktestResults.LastOrDefault()?.SharpeRatio ?? 0m) < state.OptimizationResults.Last().BestScore,
            Priority = 10,
            Description = "参数优化完成，启动模拟交易验证"
        });

        _rules.Add(new WorkflowRule
        {
            Name = "SimulationSuccess_To_Live",
            FromStage = WorkflowStage.Simulation,
            ToStage = WorkflowStage.Live,
            Condition = state =>
            {
                TimeSpan simulationDuration = DateTime.UtcNow - state.SimulationStartTime;
                return simulationDuration.TotalDays >= 7 &&
                       state.AccountStatus.TotalReturnPercent > 0.05m &&
                       state.RiskMetrics.MaxDrawdown < 0.10m &&
                       state.RiskMetrics.IsSafe;
            },
            Priority = 10,
            Description = "模拟交易表现稳定，可进入实盘"
        });

        _rules.Add(new WorkflowRule
        {
            Name = "BacktestFail_To_Idle",
            FromStage = WorkflowStage.Backtest,
            ToStage = WorkflowStage.Idle,
            Condition = state => state.BacktestResults.Any() &&
                                 (state.BacktestResults.Last().SharpeRatio < 0.5m ||
                                  state.BacktestResults.Last().WinRate < 0.45m),
            Priority = 9,
            Description = "回测表现不佳，返回策略研发"
        });

        _rules.Add(new WorkflowRule
        {
            Name = "SimulationFail_To_Optimization",
            FromStage = WorkflowStage.Simulation,
            ToStage = WorkflowStage.Optimization,
            Condition = state =>
            {
                TimeSpan simulationDuration = DateTime.UtcNow - state.SimulationStartTime;
                return simulationDuration.TotalDays >= 3 &&
                       (state.AccountStatus.TotalReturnPercent < -0.02m ||
                        state.RiskMetrics.MaxDrawdown > 0.15m);
            },
            Priority = 9,
            Description = "模拟交易表现不佳，重新优化参数"
        });

        _rules.Add(new WorkflowRule
        {
            Name = "LiveRisk_To_Simulation",
            FromStage = WorkflowStage.Live,
            ToStage = WorkflowStage.Simulation,
            Condition = state => !state.RiskMetrics.IsSafe ||
                                 state.RiskMetrics.MaxDrawdown > 0.20m ||
                                 state.AccountStatus.DailyLossCount >= 3,
            Priority = 100,
            Description = "实盘风险过高，降级到模拟交易"
        });

        _rules.Add(new WorkflowRule
        {
            Name = "Emergency_To_Emergency",
            FromStage = WorkflowStage.Live,
            ToStage = WorkflowStage.Emergency,
            Condition = state => state.RiskMetrics.MaxDrawdown > 0.30m ||
                                 state.AccountStatus.TotalReturnPercent < -0.20m ||
                                 !state.SystemResources.IsHealthy,
            Priority = 1000,
            Description = "触发紧急停止，立即平仓"
        });

        _rules.Add(new WorkflowRule
        {
            Name = "MarketAnomaly_To_Idle",
            FromStage = WorkflowStage.Live,
            ToStage = WorkflowStage.Idle,
            Condition = state => state.MarketCondition.Volatility > 0.50 ||
                                 !state.MarketCondition.IsStable,
            Priority = 50,
            Description = "市场异常波动，暂停交易观望"
        });

        LogService.Info("✅ [WorkflowEngine] 已初始化 {Count} 条工作流规则", _rules.Count);
    }

    public async Task<bool> EvaluateAndTransitionAsync(CancellationToken ct = default)
    {
        await _transitionLock.WaitAsync(ct);
        try
        {
            WorkflowStage currentStage = _stateManager.CurrentStage;
            SystemState currentState = _stateManager.CurrentState;

            var applicableRules = _rules
                .Where(r => r.FromStage == currentStage && r.Condition(currentState))
                .OrderByDescending(r => r.Priority)
                .ToList();

            if (!applicableRules.Any())
            {
                LogService.Debug("[WorkflowEngine] 当前阶段 {Stage} 无可触发规则", currentStage.GetDisplayName());
                return false;
            }

            WorkflowRule selectedRule = applicableRules.First();

            LogService.Info("🎯 [WorkflowEngine] 触发规则: {Rule}", selectedRule.Name);
            LogService.Info("   从 {From} → 到 {To}", currentStage.GetDisplayName(), selectedRule.ToStage.GetDisplayName());
            LogService.Info("   原因: {Reason}", selectedRule.Description);

            var transition = new WorkflowTransition
            {
                TransitionId = Guid.NewGuid(),
                FromStage = currentStage,
                ToStage = selectedRule.ToStage,
                RuleName = selectedRule.Name,
                Reason = selectedRule.Description,
                Timestamp = DateTime.UtcNow,
                StateSnapshot = currentState.Clone()
            };

            try
            {
                bool canTransition = await PreTransitionCheckAsync(transition, ct);
                if (!canTransition)
                {
                    LogService.Warning("[WorkflowEngine] 前置检查失败，取消转换");
                    transition.Status = TransitionStatus.Failed;
                    transition.Error = "前置检查失败";
                    _transitionHistory.Add(transition);
                    return false;
                }

                await _orchestrator.TransitionToStageAsync(selectedRule.ToStage, ct);

                transition.Status = TransitionStatus.Success;
                _transitionHistory.Add(transition);

                await _eventBus.PublishAsync(new WorkflowTransitionCompletedEvent
                {
                    Transition = transition,
                    Success = true
                });

                LogService.Info("✅ [WorkflowEngine] 工作流转换成功");
                return true;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[WorkflowEngine] 工作流转换失败");

                transition.Status = TransitionStatus.Failed;
                transition.Error = ex.Message;
                _transitionHistory.Add(transition);

                await RollbackTransitionAsync(transition, ct);

                await _eventBus.PublishAsync(new WorkflowTransitionCompletedEvent
                {
                    Transition = transition,
                    Success = false,
                    Error = ex.Message
                });

                return false;
            }
        }
        finally
        {
            _transitionLock.Release();
        }
    }

    private async Task<bool> PreTransitionCheckAsync(WorkflowTransition transition, CancellationToken ct)
    {
        LogService.Debug("[WorkflowEngine] 执行前置检查...");

        if (!_stateManager.CurrentState.SystemResources.IsHealthy)
        {
            LogService.Warning("[WorkflowEngine] 系统资源不健康");
            return false;
        }

        if (transition.ToStage == WorkflowStage.Live)
        {
            if (_stateManager.CurrentState.AccountStatus.NetValue < 1000)
            {
                LogService.Warning("[WorkflowEngine] 账户资金不足，无法进入实盘");
                return false;
            }
        }

        if (transition.ToStage == WorkflowStage.Live || transition.ToStage == WorkflowStage.Simulation)
        {
            if (!_stateManager.CurrentState.MarketCondition.IsStable)
            {
                LogService.Warning("[WorkflowEngine] 市场不稳定，暂缓进入交易");
                return false;
            }
        }

        WorkflowTransition? lastTransition = _transitionHistory.LastOrDefault();
        if (lastTransition != null)
        {
            TimeSpan timeSinceLastTransition = DateTime.UtcNow - lastTransition.Timestamp;
            if (timeSinceLastTransition.TotalMinutes < 5)
            {
                LogService.Warning("[WorkflowEngine] 距离上次转换时间过短（{Minutes}分钟），防止频繁切换",
                    timeSinceLastTransition.TotalMinutes);
                return false;
            }
        }

        LogService.Info("✅ [WorkflowEngine] 前置检查通过");
        return true;
    }

    private async Task RollbackTransitionAsync(WorkflowTransition transition, CancellationToken ct)
    {
        LogService.Warning("🔄 [WorkflowEngine] 开始回滚转换...");

        try
        {
            await _orchestrator.TransitionToStageAsync(transition.FromStage, ct);
            LogService.Info("✅ [WorkflowEngine] 回滚成功");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[WorkflowEngine] 回滚失败");
            await _eventBus.PublishAsync(new EmergencyStopEvent
            {
                Reason = "工作流回滚失败",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    public List<WorkflowTransition> GetTransitionHistory(int count = 50) => _transitionHistory.TakeLast(count).ToList();

    public List<WorkflowRule> GetApplicableRules()
    {
        WorkflowStage currentStage = _stateManager.CurrentStage;
        SystemState currentState = _stateManager.CurrentState;
        return _rules.Where(r => r.FromStage == currentStage && r.Condition(currentState))
                     .OrderByDescending(r => r.Priority)
                     .ToList();
    }

    public void AddRule(WorkflowRule rule)
    {
        _rules.Add(rule);
        LogService.Info("[WorkflowEngine] 添加新规则: {Name}", rule.Name);
    }

    public void CleanupHistory(int keepCount = 100)
    {
        if (_transitionHistory.Count > keepCount)
        {
            int removeCount = _transitionHistory.Count - keepCount;
            _transitionHistory.RemoveRange(0, removeCount);
            LogService.Debug("[WorkflowEngine] 清理了 {Count} 条历史记录", removeCount);
        }
    }
}

public class WorkflowRule
{
    public string Name { get; set; } = string.Empty;
    public WorkflowStage FromStage { get; set; }
    public WorkflowStage ToStage { get; set; }
    public Func<SystemState, bool> Condition { get; set; } = _ => true; // default non-null
    public int Priority { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public class WorkflowTransition
{
    public Guid TransitionId { get; set; }
    public WorkflowStage FromStage { get; set; }
    public WorkflowStage ToStage { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public TransitionStatus Status { get; set; }
    public string? Error { get; set; }
    public SystemState StateSnapshot { get; set; } = null!;
}

public enum TransitionStatus { Pending, InProgress, Success, Failed, RolledBack }

public class WorkflowTransitionCompletedEvent
{
    public WorkflowTransition Transition { get; init; } = null!;
    public bool Success { get; init; }
    public string? Error { get; init; }
}
