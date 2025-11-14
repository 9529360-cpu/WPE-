using System;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 状态管理器
/// </summary>
/// <remarks>
/// 核心职责：
/// 1. 管理当前工作流阶段
/// 2. 维护系统状态
/// 3. 状态转换验证
/// 4. 状态持久化
/// </remarks>
public class StateManager
{
    private WorkflowStage _currentStage;
    private SystemState? _currentState;
    private readonly object _lock = new();

    public StateManager()
    {
        _currentStage = WorkflowStage.Idle;
    }

    /// <summary>
    /// 当前工作流阶段
    /// </summary>
    public WorkflowStage CurrentStage
    {
        get
        {
            lock (_lock)
            {
                return _currentStage;
            }
        }
    }

    /// <summary>
    /// 当前系统状态
    /// </summary>
    public SystemState CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _currentState ?? CreateEmptyState();
            }
        }
    }

    /// <summary>
    /// 初始化状态管理器
    /// </summary>
    public async Task InitializeAsync()
    {
        // TODO: 从数据库加载上次的状态
        LogService.Info("[StateManager] 状态管理器已初始化");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 设置工作流阶段
    /// </summary>
    public void SetStage(WorkflowStage stage)
    {
        lock (_lock)
        {
            WorkflowStage oldStage = _currentStage;
            _currentStage = stage;

            LogService.Info("[StateManager] 阶段更新: {Old} → {New}",
                oldStage.GetDisplayName(), stage.GetDisplayName());
        }
    }

    /// <summary>
    /// 更新系统状态
    /// </summary>
    public void UpdateState(SystemState state)
    {
        lock (_lock)
        {
            _currentState = state;
        }
    }

    /// <summary>
    /// 判断是否可以切换到目标阶段
    /// </summary>
    public bool CanTransitionTo(WorkflowStage targetStage)
    {
        WorkflowStage current = CurrentStage;

        // 可以从任何阶段切换到 Idle 或 Emergency
        if (targetStage is WorkflowStage.Idle or WorkflowStage.Emergency)
        {
            return true;
        }

        // 定义允许的状态转换
        return (current, targetStage) switch
        {
            // 从空闲可以进入任何阶段
            (WorkflowStage.Idle, _) => true,

            // 回测相关
            (WorkflowStage.Backtest, WorkflowStage.Simulation) => true,
            (WorkflowStage.Backtest, WorkflowStage.Optimization) => true,

            // 优化相关
            (WorkflowStage.Optimization, WorkflowStage.Backtest) => true,

            // 模拟相关
            (WorkflowStage.Simulation, WorkflowStage.Live) => true,
            (WorkflowStage.Simulation, WorkflowStage.Backtest) => true,

            // 实盘相关
            (WorkflowStage.Live, WorkflowStage.Simulation) => true,

            // 紧急状态可以回到空闲
            (WorkflowStage.Emergency, WorkflowStage.Idle) => true,

            // 其他转换不允许
            _ => false
        };
    }

    /// <summary>
    /// 获取状态摘要
    /// </summary>
    public string GetSummary()
    {
        SystemState state = CurrentState;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"当前阶段:");
        sb.AppendLine($"{CurrentStage.GetIcon()}");
        sb.AppendLine($"{CurrentStage.GetDisplayName()}");
        sb.AppendLine("市场状况:");
        sb.AppendLine($"  波动率 = {state.MarketCondition.Volatility:P0}, 趋势 = {state.MarketCondition.TrendDirection}");
        sb.AppendLine("账户状态:");
        sb.AppendLine($"  净值 = {state.AccountStatus.NetValue:N2} USDT, 盈亏 = {state.AccountStatus.TodayPnL:+N2;-N2} USDT");
        sb.AppendLine($"风险指标: 回撤 = {state.RiskMetrics.MaxDrawdown:P2}, 安全性 = {state.RiskMetrics.IsSafe}");
        sb.AppendLine("策略状态:");
        sb.AppendLine($"  激活 = {state.StrategyStatus.IsActive}, 表现 = {state.StrategyStatus.PerformanceScore:P0}");

        return sb.ToString();
    }

    /// <summary>
    /// 创建空状态
    /// </summary>
    private SystemState CreateEmptyState()
    {
        return new SystemState
        {
            Timestamp = DateTime.UtcNow,
            CurrentStage = _currentStage,
            MarketCondition = new MarketCondition
            {
                Volatility = 0,
                Trend = 0,
                Liquidity = 1.0,
                Timestamp = DateTime.UtcNow
            },
            AccountStatus = new AccountStatus
            {
                Type = Models.AccountType.Simulated,
                NetValue = 0,
                AvailableBalance = 0,
                PositionValue = 0,
                TodayPnL = 0,
                TotalPnL = 0,
                WinRate = 0,
                OpenPositionCount = 0,
                Timestamp = DateTime.UtcNow
            },
            StrategyStatus = new StrategyStatus
            {
                IsActive = false,
                PerformanceScore = 0,
                RecentSignalCount = 0,
                AverageConfidence = 0,
                Timestamp = DateTime.UtcNow
            },
            RiskMetrics = new RiskMetrics
            {
                MaxDrawdown = 0,
                CurrentDrawdown = 0,
                DailyLoss = 0,
                Leverage = 1.0m,
                Timestamp = DateTime.UtcNow
            },
            SystemResources = new SystemResources
            {
                CpuUsage = 0,
                MemoryUsage = 0,
                NetworkLatency = 0,
                Timestamp = DateTime.UtcNow
            }
        };
    }
}
