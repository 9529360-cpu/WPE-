using System;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 工作流阶段枚举
/// </summary>
/// <remarks>
/// 定义系统可能处于的各个阶段：
/// - Idle: 空闲状态，等待启动
/// - Backtest: 回测验证阶段
/// - Optimization: 参数优化阶段
/// - Simulation: 模拟交易阶段
/// - Live: 实盘交易阶段
/// - Emergency: 紧急状态（风险警报）
/// </remarks>
public enum WorkflowStage
{
    /// <summary>
    /// 空闲状态
    /// </summary>
    Idle = 0,

    /// <summary>
    /// 回测验证阶段
    /// </summary>
    /// <remarks>
    /// 在此阶段：
    /// - 测试策略在历史数据上的表现
    /// - 评估收益率、夏普比率、最大回撤等指标
    /// - 决定是否进入模拟交易阶段
    /// </remarks>
    Backtest = 1,

    /// <summary>
    /// 参数优化阶段
    /// </summary>
    /// <remarks>
    /// 在此阶段：
    /// - 使用遗传算法或网格搜索优化参数
    /// - 寻找最佳参数组合
    /// - 完成后返回回测阶段验证
    /// </remarks>
    Optimization = 2,

    /// <summary>
    /// 模拟交易阶段
    /// </summary>
    /// <remarks>
    /// 在此阶段：
    /// - 使用模拟账户进行实时交易
    /// - 验证策略在真实市场中的适应性
    /// - 监控表现，决定是否进入实盘
    /// </remarks>
    Simulation = 3,

    /// <summary>
    /// 实盘交易阶段
    /// </summary>
    /// <remarks>
    /// 在此阶段：
    /// - 使用真实资金进行交易
    /// - 实时监控风险指标
    /// - 动态调整策略参数
    /// - 可能降级到模拟或暂停
    /// </remarks>
    Live = 4,

    /// <summary>
    /// 紧急状态
    /// </summary>
    /// <remarks>
    /// 触发条件：
    /// - 严重风险警报
    /// - 系统异常
    /// - 手动干预
    /// 处理：
    /// - 立即停止交易
    /// - 平仓所有持仓
    /// - 等待人工处理
    /// </remarks>
    Emergency = 999
}

/// <summary>
/// 工作流阶段扩展方法
/// </summary>
public static class WorkflowStageExtensions
{
    /// <summary>
    /// 获取阶段显示名称
    /// </summary>
    public static string GetDisplayName(this WorkflowStage stage)
    {
        return stage switch
        {
            WorkflowStage.Idle => "空闲",
            WorkflowStage.Backtest => "回测验证",
            WorkflowStage.Optimization => "参数优化",
            WorkflowStage.Simulation => "模拟交易",
            WorkflowStage.Live => "实盘交易",
            WorkflowStage.Emergency => "紧急状态",
            _ => "未知"
        };
    }

    /// <summary>
    /// 获取阶段描述
    /// </summary>
    public static string GetDescription(this WorkflowStage stage)
    {
        return stage switch
        {
            WorkflowStage.Idle => "系统空闲，等待启动",
            WorkflowStage.Backtest => "正在回测验证策略表现",
            WorkflowStage.Optimization => "正在优化策略参数",
            WorkflowStage.Simulation => "正在模拟账户中进行实时交易",
            WorkflowStage.Live => "正在真实账户中进行实盘交易",
            WorkflowStage.Emergency => "系统进入紧急状态，已暂停交易",
            _ => "未知状态"
        };
    }

    /// <summary>
    /// 获取阶段图标
    /// </summary>
    public static string GetIcon(this WorkflowStage stage)
    {
        return stage switch
        {
            WorkflowStage.Idle => "⏸️",
            WorkflowStage.Backtest => "📊",
            WorkflowStage.Optimization => "🔧",
            WorkflowStage.Simulation => "🎮",
            WorkflowStage.Live => "💰",
            WorkflowStage.Emergency => "🚨",
            _ => "❓"
        };
    }

    /// <summary>
    /// 判断是否可以进行交易
    /// </summary>
    public static bool CanTrade(this WorkflowStage stage)
    {
        return stage is WorkflowStage.Simulation or WorkflowStage.Live;
    }

    /// <summary>
    /// 判断是否需要实时监控
    /// </summary>
    public static bool NeedsRealTimeMonitoring(this WorkflowStage stage)
    {
        return stage is WorkflowStage.Simulation or WorkflowStage.Live;
    }

    /// <summary>
    /// 获取推荐的控制循环间隔
    /// </summary>
    public static TimeSpan GetRecommendedInterval(this WorkflowStage stage)
    {
        return stage switch
        {
            WorkflowStage.Idle => TimeSpan.FromMinutes(1),
            WorkflowStage.Backtest => TimeSpan.FromSeconds(30),
            WorkflowStage.Optimization => TimeSpan.FromMinutes(1),
            WorkflowStage.Simulation => TimeSpan.FromSeconds(10),
            WorkflowStage.Live => TimeSpan.FromSeconds(5),
            WorkflowStage.Emergency => TimeSpan.FromSeconds(1),
            _ => TimeSpan.FromSeconds(30)
        };
    }
}
