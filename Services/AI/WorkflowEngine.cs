using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 智能工作流引擎
/// </summary>
/// <remarks>
/// 核心功能：
/// 1. 基于规则的自动阶段切换
/// 2. 条件触发器系统
/// 3. 异常安全的状态转换
/// 4. 完整的回滚机制
/// 5. 状态持久化和恢复
/// 
/// 工作流阶段：
/// Idle → Backtest → Optimization → Simulation → Live
/// </remarks>
public class WorkflowEngine
{
    private readonly StateManager _stateManager;
    private readonly WorkflowOrchestrator _orchestrator;
    private readonly EventBus _eventBus;
    private readonly List<WorkflowRule> _rules;
    private readonly List<WorkflowTransition> _transitionHistory;
    private readonly SemaphoreSlim _transitionLock;

    public WorkflowEngine(
        StateManager stateManager,
        WorkflowOrchestrator orchestrator,
        EventBus eventBus)
    {
        _stateManager = stateManager;
        _orchestrator = orchestrator;
        _eventBus = eventBus;
        _rules = new List<WorkflowRule>();
        _transitionHistory = new List<WorkflowTransition>();
        _transitionLock = new SemaphoreSlim(1, 1);

        InitializeRules();
    }

    /// <summary>
    /// 初始化工作流规则
    /// </summary>
    private void InitializeRules()
    {
        // 规则1: 回测成功 → 参数优化
        _rules.Add(new WorkflowRule
        {
            Name = "BacktestSuccess_To_Optimization",
            FromStage = WorkflowStage.Backtest,
            ToStage = WorkflowStage.Optimization,
            Condition = (state) =>
            {
                // 回测成功且夏普比率 > 1.5
                return state.BacktestResults.Any() &&
                       state.BacktestResults.Last().SharpeRatio > 1.5m &&
                       state.BacktestResults.Last().WinRate > 0.55m;
            },
            Priority = 10,
            Description = "回测表现良好，进入参数优化阶段"
        });

        // 规则2: 优化完成 → 模拟交易
        _rules.Add(new WorkflowRule
        {
            Name = "OptimizationComplete_To_Simulation",
            FromStage = WorkflowStage.Optimization,
            ToStage = WorkflowStage.Simulation,
            Condition = (state) =>
            {
                // 优化完成且找到更优参数
                return state.OptimizationResults.Any() &&
                       state.OptimizationResults.Last().BestScore > state.BacktestResults.Last().SharpeRatio;
            },
            Priority = 10,
            Description = "参数优化完成，启动模拟交易验证"
        });

        // 规则3: 模拟交易成功 → 实盘交易
        _rules.Add(new WorkflowRule
        {
            Name = "SimulationSuccess_To_Live",
            FromStage = WorkflowStage.Simulation,
            ToStage = WorkflowStage.Live,
            Condition = (state) =>
            {
                // 模拟交易至少运行7天，表现稳定
                TimeSpan simulationDuration = DateTime.UtcNow - state.SimulationStartTime;
                return simulationDuration.TotalDays >= 7 &&
                       state.AccountStatus.TotalReturnPercent > 0.05m && // 总收益 > 5%
                       state.RiskMetrics.MaxDrawdown < 0.10m && // 最大回撤 < 10%
                       state.RiskMetrics.IsSafe;
            },
            Priority = 10,
            Description = "模拟交易表现稳定，可进入实盘"
        });

        // 规则4: 回测失败 → 重新回测
        _rules.Add(new WorkflowRule
        {
            Name = "BacktestFail_To_Idle",
            FromStage = WorkflowStage.Backtest,
            ToStage = WorkflowStage.Idle,
            Condition = (state) =>
            {
                // 回测失败或表现不佳
                return state.BacktestResults.Any() &&
                       (state.BacktestResults.Last().SharpeRatio < 0.5m ||
                        state.BacktestResults.Last().WinRate < 0.45m);
            },
            Priority = 9,
            Description = "回测表现不佳，返回策略研发"
        });

        // 规则5: 模拟交易失败 → 重新优化
        _rules.Add(new WorkflowRule
        {
            Name = "SimulationFail_To_Optimization",
            FromStage = WorkflowStage.Simulation,
            ToStage = WorkflowStage.Optimization,
            Condition = (state) =>
            {
                // 模拟交易表现不佳
                TimeSpan simulationDuration = DateTime.UtcNow - state.SimulationStartTime;
                return simulationDuration.TotalDays >= 3 &&
                       (state.AccountStatus.TotalReturnPercent < -0.02m || // 亏损 > 2%
                        state.RiskMetrics.MaxDrawdown > 0.15m); // 回撤 > 15%
            },
            Priority = 9,
            Description = "模拟交易表现不佳，重新优化参数"
        });

        // 规则6: 实盘交易风险 → 降级到模拟
        _rules.Add(new WorkflowRule
        {
            Name = "LiveRisk_To_Simulation",
            FromStage = WorkflowStage.Live,
            ToStage = WorkflowStage.Simulation,
            Condition = (state) =>
            {
                // 实盘出现严重风险
                return !state.RiskMetrics.IsSafe ||
                       state.RiskMetrics.MaxDrawdown > 0.20m || // 回撤 > 20%
                       state.AccountStatus.DailyLossCount >= 3; // 连续3天亏损
            },
            Priority = 100, // 最高优先级
            Description = "实盘风险过高，降级到模拟交易"
        });

        // 规则7: 紧急停止
        _rules.Add(new WorkflowRule
        {
            Name = "Emergency_To_Emergency",
            FromStage = WorkflowStage.Live,
            ToStage = WorkflowStage.Emergency,
            Condition = (state) =>
            {
                // 触发紧急条件
                return state.RiskMetrics.MaxDrawdown > 0.30m || // 回撤 > 30%
                       state.AccountStatus.TotalReturnPercent < -0.20m || // 总亏损 > 20%
                       !state.SystemResources.IsHealthy; // 系统异常
            },
            Priority = 1000, // 紧急优先级
            Description = "触发紧急停止，立即平仓"
        });

        // 规则8: 市场异常 → 暂停
        _rules.Add(new WorkflowRule
        {
            Name = "MarketAnomaly_To_Idle",
            FromStage = WorkflowStage.Live,
            ToStage = WorkflowStage.Idle,
            Condition = (state) =>
            {
                // 市场出现异常波动
                return state.MarketCondition.Volatility > 0.50 || // 波动率 > 50%
                       !state.MarketCondition.IsStable;
            },
            Priority = 50,
            Description = "市场异常波动，暂停交易观望"
        });

        LogService.Info("✅ [WorkflowEngine] 已初始化 {Count} 条工作流规则", _rules.Count);
    }

    /// <summary>
    /// 评估并执行工作流转换
    /// </summary>
    public async Task<bool> EvaluateAndTransitionAsync(CancellationToken ct = default)
    {
        await _transitionLock.WaitAsync(ct);
        try
        {
            WorkflowStage currentStage = _stateManager.CurrentStage;
            SystemState currentState = _stateManager.CurrentState;

            // 查找适用的规则（按优先级排序）
            var applicableRules = _rules
                .Where(r => r.FromStage == currentStage && r.Condition(currentState))
                .OrderByDescending(r => r.Priority)
                .ToList();

            if (!applicableRules.Any())
            {
                LogService.Debug("[WorkflowEngine] 当前阶段 {Stage} 无可触发规则", currentStage.GetDisplayName());
                return false;
            }

            // 选择优先级最高的规则
            WorkflowRule selectedRule = applicableRules.First();

            LogService.Info("🎯 [WorkflowEngine] 触发规则: {Rule}", selectedRule.Name);
            LogService.Info("   从 {From} → 到 {To}", currentStage.GetDisplayName(), selectedRule.ToStage.GetDisplayName());
            LogService.Info("   原因: {Reason}", selectedRule.Description);

            // 记录转换历史
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
                // 执行前置检查
                bool canTransition = await PreTransitionCheckAsync(transition, ct);
                if (!canTransition)
                {
                    LogService.Warning("[WorkflowEngine] 前置检查失败，取消转换");
                    transition.Status = TransitionStatus.Failed;
                    transition.Error = "前置检查失败";
                    _transitionHistory.Add(transition);
                    return false;
                }

                // 执行转换
                await _orchestrator.TransitionToStageAsync(selectedRule.ToStage, ct);

                transition.Status = TransitionStatus.Success;
                _transitionHistory.Add(transition);

                // 发布转换成功事件
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

                // 尝试回滚
                await RollbackTransitionAsync(transition, ct);

                // 发布转换失败事件
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

    /// <summary>
    /// 转换前置检查
    /// </summary>
    private async Task<bool> PreTransitionCheckAsync(WorkflowTransition transition, CancellationToken ct)
    {
        LogService.Debug("[WorkflowEngine] 执行前置检查...");

        // 检查1: 资源可用性
        if (!_stateManager.CurrentState.SystemResources.IsHealthy)
        {
            LogService.Warning("[WorkflowEngine] 系统资源不健康");
            return false;
        }

        // 检查2: 账户状态
        if (transition.ToStage == WorkflowStage.Live)
        {
            // 进入实盘前需要确认账户状态
            if (_stateManager.CurrentState.AccountStatus.NetValue < 1000)
            {
                LogService.Warning("[WorkflowEngine] 账户资金不足，无法进入实盘");
                return false;
            }
        }

        // 检查3: 市场状态
        if (transition.ToStage == WorkflowStage.Live || transition.ToStage == WorkflowStage.Simulation)
        {
            if (!_stateManager.CurrentState.MarketCondition.IsStable)
            {
                LogService.Warning("[WorkflowEngine] 市场不稳定，暂缓进入交易");
                return false;
            }
        }

        // 检查4: 防止频繁切换
        WorkflowTransition lastTransition = _transitionHistory.LastOrDefault();
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

    /// <summary>
    /// 回滚转换
    /// </summary>
    private async Task RollbackTransitionAsync(WorkflowTransition transition, CancellationToken ct)
    {
        LogService.Warning("🔄 [WorkflowEngine] 开始回滚转换...");

        try
        {
            // 恢复到原始阶段
            await _orchestrator.TransitionToStageAsync(transition.FromStage, ct);

            // 恢复状态快照（如果需要）
            // _stateManager.RestoreState(transition.StateSnapshot);

            LogService.Info("✅ [WorkflowEngine] 回滚成功");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[WorkflowEngine] 回滚失败");
            // 发送紧急告警
            await _eventBus.PublishAsync(new EmergencyStopEvent
            {
                Reason = "工作流回滚失败",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// 获取转换历史
    /// </summary>
    public List<WorkflowTransition> GetTransitionHistory(int count = 50)
    {
        return _transitionHistory.TakeLast(count).ToList();
    }

    /// <summary>
    /// 获取当前适用的规则
    /// </summary>
    public List<WorkflowRule> GetApplicableRules()
    {
        WorkflowStage currentStage = _stateManager.CurrentStage;
        SystemState currentState = _stateManager.CurrentState;

        return _rules
            .Where(r => r.FromStage == currentStage && r.Condition(currentState))
            .OrderByDescending(r => r.Priority)
            .ToList();
    }

    /// <summary>
    /// 手动添加规则
    /// </summary>
    public void AddRule(WorkflowRule rule)
    {
        _rules.Add(rule);
        LogService.Info("[WorkflowEngine] 添加新规则: {Name}", rule.Name);
    }

    /// <summary>
    /// 清理历史记录
    /// </summary>
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

#region 工作流数据模型

/// <summary>
/// 工作流规则
/// </summary>
public class WorkflowRule
{
    /// <summary>
    /// 规则名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 起始阶段
    /// </summary>
    public WorkflowStage FromStage { get; set; }

    /// <summary>
    /// 目标阶段
    /// </summary>
    public WorkflowStage ToStage { get; set; }

    /// <summary>
    /// 触发条件
    /// </summary>
    public Func<SystemState, bool> Condition { get; set; } = null!;

    /// <summary>
    /// 优先级（数值越大优先级越高）
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// 规则描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 工作流转换记录
/// </summary>
public class WorkflowTransition
{
    /// <summary>
    /// 转换ID
    /// </summary>
    public Guid TransitionId { get; set; }

    /// <summary>
    /// 起始阶段
    /// </summary>
    public WorkflowStage FromStage { get; set; }

    /// <summary>
    /// 目标阶段
    /// </summary>
    public WorkflowStage ToStage { get; set; }

    /// <summary>
    /// 触发规则名称
    /// </summary>
    public string RuleName { get; set; } = string.Empty;

    /// <summary>
    /// 转换原因
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 转换时间
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 转换状态
    /// </summary>
    public TransitionStatus Status { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// 状态快照（用于回滚）
    /// </summary>
    public SystemState StateSnapshot { get; set; } = null!;
}

/// <summary>
/// 转换状态
/// </summary>
public enum TransitionStatus
{
    Pending,
    InProgress,
    Success,
    Failed,
    RolledBack
}

/// <summary>
/// 工作流转换完成事件
/// </summary>
public class WorkflowTransitionCompletedEvent
{
    public WorkflowTransition Transition { get; init; } = null!;
    public bool Success { get; init; }
    public string? Error { get; init; }
}

#endregion
