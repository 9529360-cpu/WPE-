using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.AI;

/// <summary>
/// 资源管理器
/// </summary>
/// <remarks>
/// 核心职责：
/// 1. 监控系统资源使用
/// 2. 基于优先级分配资源
/// 3. 防止资源耗尽
/// 4. 优化资源利用率
/// </remarks>
public class ResourceManager
{
    private readonly Dictionary<WorkflowStage, int> _stagePriorities;
    private readonly SemaphoreSlim _executionSemaphore;
    private int _activeTaskCount;
    private const int MaxConcurrentTasks = 5;

    public ResourceManager()
    {
        // 定义各阶段优先级 (数字越小优先级越高)
        _stagePriorities = new Dictionary<WorkflowStage, int>
        {
            [WorkflowStage.Emergency] = 0,  // 最高优先级
            [WorkflowStage.Live] = 1,       // 实盘交易
            [WorkflowStage.Simulation] = 2, // 模拟交易
            [WorkflowStage.Backtest] = 3,   // 回测
            [WorkflowStage.Optimization] = 4, // 优化
            [WorkflowStage.Idle] = 5        // 空闲
        };

        _executionSemaphore = new SemaphoreSlim(MaxConcurrentTasks, MaxConcurrentTasks);
    }

    /// <summary>
    /// 检查资源是否充足
    /// </summary>
    public async Task<bool> CheckResourcesAsync(AIDecision decision, CancellationToken ct = default)
    {
        try
        {
            // 1. 检查系统资源
            SystemResources systemResources = GetSystemResources();

            if (!systemResources.IsHealthy)
            {
                LogService.Warning("[ResourceManager] 系统资源不足: CPU={Cpu:P0}, Memory={Memory:P0}",
                    systemResources.CpuUsage, systemResources.MemoryUsage);
                return false;
            }

            // 2. 检查并发任务数
            if (_activeTaskCount >= MaxConcurrentTasks)
            {
                LogService.Warning("[ResourceManager] 并发任务数达到上限: {Count}/{Max}",
                    _activeTaskCount, MaxConcurrentTasks);
                return false;
            }

            // 3. 检查优先级
            int priority = GetPriority(decision.CurrentStage);
            if (priority > 3) // 低优先级任务在高负载时跳过
            {
                if (systemResources.CpuUsage > 0.6 || systemResources.MemoryUsage > 0.6)
                {
                    LogService.Debug("[ResourceManager] 低优先级任务在高负载时跳过: {Stage}",
                        decision.CurrentStage.GetDisplayName());
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[ResourceManager] 资源检查失败");
            return false;
        }
    }

    /// <summary>
    /// 请求执行资源
    /// </summary>
    public async Task<IDisposable> AcquireExecutionTokenAsync(
        WorkflowStage stage,
        CancellationToken ct = default)
    {
        await _executionSemaphore.WaitAsync(ct);

        Interlocked.Increment(ref _activeTaskCount);

        LogService.Debug("[ResourceManager] 获取执行令牌: {Stage}, 活动任务数={Count}",
            stage.GetDisplayName(), _activeTaskCount);

        return new ExecutionToken(this);
    }

    /// <summary>
    /// 释放执行资源
    /// </summary>
    private void ReleaseExecutionToken()
    {
        Interlocked.Decrement(ref _activeTaskCount);
        _executionSemaphore.Release();

        LogService.Debug("[ResourceManager] 释放执行令牌, 活动任务数={Count}", _activeTaskCount);
    }

    /// <summary>
    /// 获取阶段优先级
    /// </summary>
    public int GetPriority(WorkflowStage stage)
    {
        return _stagePriorities.TryGetValue(stage, out int priority) ? priority : 5;
    }

    /// <summary>
    /// 获取系统资源使用情况
    /// </summary>
    private SystemResources GetSystemResources()
    {
        try
        {
            // 获取CPU使用率
            double cpuUsage = GetCpuUsage();

            // 获取内存使用率
            double memoryUsage = GetMemoryUsage();

            // 简化：网络延迟暂时返回0
            double networkLatency = 0.0;

            return new SystemResources
            {
                CpuUsage = cpuUsage,
                MemoryUsage = memoryUsage,
                NetworkLatency = networkLatency,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[ResourceManager] 获取系统资源失败");

            // 返回保守估计
            return new SystemResources
            {
                CpuUsage = 0.5,
                MemoryUsage = 0.5,
                NetworkLatency = 100,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// 获取CPU使用率
    /// </summary>
    private double GetCpuUsage()
    {
        try
        {
            // 使用Process获取当前进程CPU使用率
            var process = Process.GetCurrentProcess();
            DateTime startTime = DateTime.UtcNow;
            TimeSpan startCpuUsage = process.TotalProcessorTime;

            Thread.Sleep(100); // 采样100ms

            DateTime endTime = DateTime.UtcNow;
            TimeSpan endCpuUsage = process.TotalProcessorTime;

            double cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            double totalMsPassed = (endTime - startTime).TotalMilliseconds;
            double cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            return Math.Max(0, Math.Min(1.0, cpuUsageTotal));
        }
        catch
        {
            return 0.3; // 默认值
        }
    }

    /// <summary>
    /// 获取内存使用率
    /// </summary>
    private double GetMemoryUsage()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            double usedMemoryMB = process.WorkingSet64 / (1024.0 * 1024.0);

            // 假设系统有8GB可用内存
            double totalMemoryMB = 8192.0;
            double usage = usedMemoryMB / totalMemoryMB;

            return Math.Max(0, Math.Min(1.0, usage));
        }
        catch
        {
            return 0.4; // 默认值
        }
    }

    /// <summary>
    /// 获取资源统计信息
    /// </summary>
    public ResourceStatistics GetStatistics()
    {
        SystemResources resources = GetSystemResources();

        return new ResourceStatistics
        {
            CpuUsage = resources.CpuUsage,
            MemoryUsage = resources.MemoryUsage,
            NetworkLatency = resources.NetworkLatency,
            ActiveTaskCount = _activeTaskCount,
            MaxConcurrentTasks = MaxConcurrentTasks,
            AvailableTokens = MaxConcurrentTasks - _activeTaskCount,
            Timestamp = DateTime.UtcNow
        };
    }

    #region ExecutionToken

    /// <summary>
    /// 执行令牌（用于自动释放资源）
    /// </summary>
    private class ExecutionToken : IDisposable
    {
        private readonly ResourceManager _manager;
        private bool _disposed;

        public ExecutionToken(ResourceManager manager)
        {
            _manager = manager;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _manager.ReleaseExecutionToken();
                _disposed = true;
            }
        }
    }

    #endregion
}

#region 资源统计

/// <summary>
/// 资源统计信息
/// </summary>
public class ResourceStatistics
{
    public double CpuUsage { get; init; }
    public double MemoryUsage { get; init; }
    public double NetworkLatency { get; init; }
    public int ActiveTaskCount { get; init; }
    public int MaxConcurrentTasks { get; init; }
    public int AvailableTokens { get; init; }
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 系统是否健康
    /// </summary>
    public bool IsHealthy => CpuUsage < 0.8 && MemoryUsage < 0.8 && NetworkLatency < 200;

    /// <summary>
    /// 负载等级
    /// </summary>
    public string LoadLevel
    {
        get
        {
            double avgLoad = (CpuUsage + MemoryUsage) / 2.0;
            return avgLoad switch
            {
                < 0.3 => "低负载",
                < 0.6 => "正常",
                < 0.8 => "高负载",
                _ => "过载"
            };
        }
    }
}

#endregion
