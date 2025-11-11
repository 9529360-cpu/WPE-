using System;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 工作流编排器
/// </summary>
/// <remarks>
/// 核心职责：
/// 1. 执行AI决策
/// 2. 管理工作流阶段切换
/// 3. 协调各模块执行
/// 4. 处理异常和回滚
/// </remarks>
public class WorkflowOrchestrator
{
    private readonly StateManager _stateManager;
    private readonly DecisionEngine _decisionEngine;
    private readonly EventBus _eventBus;
    private readonly ResourceManager _resourceManager;

    public WorkflowOrchestrator(
        StateManager stateManager,
        DecisionEngine decisionEngine,
        EventBus eventBus,
        ResourceManager resourceManager)
    {
        _stateManager = stateManager;
        _decisionEngine = decisionEngine;
        _eventBus = eventBus;
        _resourceManager = resourceManager;
    }

    /// <summary>
    /// 执行AI决策
    /// </summary>
    public async Task ExecuteAsync(AIDecision decision, CancellationToken ct = default)
    {
        try
        {
            LogService.Info("🔧 [WorkflowOrchestrator] 执行决策: {Action}", decision.PrimaryAction);

            // 1. 检查资源
            if (!await _resourceManager.CheckResourcesAsync(decision, ct))
            {
                LogService.Warning("[WorkflowOrchestrator] 资源不足，跳过执行");
                return;
            }

            // 2. 执行主要动作
            await ExecuteActionAsync(decision, ct);

            // 3. 如果推荐切换阶段
            if (decision.RecommendedStage != decision.CurrentStage)
            {
                await TransitionToStageAsync(decision.RecommendedStage, ct);
            }

            // 4. 发布执行完成事件
            await _eventBus.PublishAsync(new WorkflowExecutedEvent
            {
                Decision = decision,
                Timestamp = DateTime.UtcNow,
                Success = true
            });
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[WorkflowOrchestrator] 执行失败");

            await _eventBus.PublishAsync(new WorkflowExecutedEvent
            {
                Decision = decision,
                Timestamp = DateTime.UtcNow,
                Success = false,
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// 执行具体动作
    /// </summary>
    private async Task ExecuteActionAsync(AIDecision decision, CancellationToken ct)
    {
        switch (decision.PrimaryAction)
        {
            case DecisionAction.Continue:
                LogService.Debug("[WorkflowOrchestrator] 继续当前流程");
                break;

            case DecisionAction.Pause:
                LogService.Info("[WorkflowOrchestrator] 暂停交易");
                await _eventBus.PublishAsync(new PauseRequestEvent());
                break;

            case DecisionAction.Stop:
                LogService.Error("[WorkflowOrchestrator] 紧急停止");
                await _eventBus.PublishAsync(new EmergencyStopEvent
                {
                    Reason = decision.Reason,
                    Timestamp = DateTime.UtcNow
                });
                break;

            case DecisionAction.Upgrade:
                LogService.Info("[WorkflowOrchestrator] 账户升级");
                await _eventBus.PublishAsync(new AccountUpgradeRequestEvent());
                break;

            case DecisionAction.Downgrade:
                LogService.Warning("[WorkflowOrchestrator] 账户降级");
                await _eventBus.PublishAsync(new AccountDowngradeRequestEvent());
                break;

            case DecisionAction.Optimize:
                LogService.Info("[WorkflowOrchestrator] 启动参数优化");
                await _eventBus.PublishAsync(new OptimizationRequestEvent());
                break;

            case DecisionAction.ReduceRisk:
                LogService.Warning("[WorkflowOrchestrator] 降低风险敞口");
                await _eventBus.PublishAsync(new ReduceRiskRequestEvent());
                break;

            case DecisionAction.Hold:
                LogService.Debug("[WorkflowOrchestrator] 观望");
                break;
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 切换到指定工作流阶段
    /// </summary>
    public async Task TransitionToStageAsync(WorkflowStage targetStage, CancellationToken ct = default)
    {
        WorkflowStage currentStage = _stateManager.CurrentStage;

        if (currentStage == targetStage)
        {
            return;
        }

        LogService.Info("🔄 [WorkflowOrchestrator] 阶段切换: {From} → {To}",
            currentStage.GetDisplayName(), targetStage.GetDisplayName());

        try
        {
            // 1. 离开当前阶段
            await ExitStageAsync(currentStage, ct);

            // 2. 进入新阶段
            await EnterStageAsync(targetStage, ct);

            // 3. 更新状态
            _stateManager.SetStage(targetStage);

            // 4. 发布阶段切换事件
            await _eventBus.PublishAsync(new StageTransitionEvent
            {
                FromStage = currentStage,
                ToStage = targetStage,
                Timestamp = DateTime.UtcNow
            });

            LogService.Info("✅ [WorkflowOrchestrator] 阶段切换完成");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[WorkflowOrchestrator] 阶段切换失败，保持当前阶段");
        }
    }

    /// <summary>
    /// 离开当前阶段
    /// </summary>
    private async Task ExitStageAsync(WorkflowStage stage, CancellationToken ct)
    {
        switch (stage)
        {
            case WorkflowStage.Backtest:
                // 停止回测
                break;

            case WorkflowStage.Simulation:
                // 停止模拟交易
                await _eventBus.PublishAsync(new StopSimulationEvent());
                break;

            case WorkflowStage.Live:
                // 停止实盘交易（不平仓）
                await _eventBus.PublishAsync(new StopLiveTradeEvent { ClosePositions = false });
                break;

            case WorkflowStage.Optimization:
                // 停止优化
                break;
        }

        LogService.Debug("[WorkflowOrchestrator] 已离开阶段: {Stage}", stage.GetDisplayName());
    }

    /// <summary>
    /// 进入新阶段
    /// </summary>
    private async Task EnterStageAsync(WorkflowStage stage, CancellationToken ct)
    {
        switch (stage)
        {
            case WorkflowStage.Idle:
                // 空闲状态，不需要特殊操作
                break;

            case WorkflowStage.Backtest:
                // 启动回测
                await _eventBus.PublishAsync(new StartBacktestEvent());
                break;

            case WorkflowStage.Optimization:
                // 启动参数优化
                await _eventBus.PublishAsync(new StartOptimizationEvent());
                break;

            case WorkflowStage.Simulation:
                // 启动模拟交易
                await _eventBus.PublishAsync(new StartSimulationEvent());
                break;

            case WorkflowStage.Live:
                // 启动实盘交易（需要确认）
                await _eventBus.PublishAsync(new StartLiveTradeEvent());
                break;

            case WorkflowStage.Emergency:
                // 紧急状态：平仓所有持仓
                await _eventBus.PublishAsync(new EmergencyStopEvent
                {
                    Reason = "进入紧急状态",
                    Timestamp = DateTime.UtcNow
                });
                break;
        }

        LogService.Info("[WorkflowOrchestrator] 已进入阶段: {Stage} {Icon}",
            stage.GetDisplayName(), stage.GetIcon());
    }
}

#region 工作流事件

/// <summary>
/// 工作流执行事件
/// </summary>
public class WorkflowExecutedEvent
{
    public required AIDecision Decision { get; init; }
    public DateTime Timestamp { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// 暂停请求事件
/// </summary>
public class PauseRequestEvent { }

/// <summary>
/// 紧急停止事件
/// </summary>
public class EmergencyStopEvent
{
    public string Reason { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// 账户升级请求事件
/// </summary>
public class AccountUpgradeRequestEvent { }

/// <summary>
/// 账户降级请求事件
/// </summary>
public class AccountDowngradeRequestEvent { }

/// <summary>
/// 优化请求事件
/// </summary>
public class OptimizationRequestEvent { }

/// <summary>
/// 降低风险请求事件
/// </summary>
public class ReduceRiskRequestEvent { }

/// <summary>
/// 停止模拟交易事件
/// </summary>
public class StopSimulationEvent { }

/// <summary>
/// 停止实盘交易事件
/// </summary>
public class StopLiveTradeEvent
{
    public bool ClosePositions { get; init; }
}

/// <summary>
/// 启动回测事件
/// </summary>
public class StartBacktestEvent { }

/// <summary>
/// 启动优化事件
/// </summary>
public class StartOptimizationEvent { }

/// <summary>
/// 启动模拟交易事件
/// </summary>
public class StartSimulationEvent { }

/// <summary>
/// 启动实盘交易事件
/// </summary>
public class StartLiveTradeEvent { }

#endregion
