using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.Performance;

/// <summary>
/// 系统资源监控器
/// </summary>
/// <remarks>
/// 核心功能:
/// 1. CPU使用率监控
/// 2. 内存使用监控
/// 3. 网络延迟监控
/// 4. 磁盘I/O监控
/// 5. 线程统计
/// 
/// 监控指标:
/// - CPU使用率 (%)
/// - 内存使用 (MB)
/// - 网络延迟 (ms)
/// - 活动线程数
/// - GC回收统计
/// </remarks>
public class SystemResourceMonitor : IDisposable
{
    private readonly Process _currentProcess;
    private readonly PerformanceCounter? _cpuCounter;
    private readonly Timer _monitorTimer;
    
    private double _cpuUsage;
    private long _memoryUsage;
    private int _threadCount;
    private TimeSpan _totalProcessorTime;
    private DateTime _lastSampleTime;
    
    // 网络延迟监控
    private long _networkLatency;
    private readonly string _testEndpoint = "https://fapi.binance.com";

    public SystemResourceMonitor()
    {
        _currentProcess = Process.GetCurrentProcess();
        _lastSampleTime = DateTime.UtcNow;
        _totalProcessorTime = _currentProcess.TotalProcessorTime;

        // 尝试创建CPU性能计数器（可能在某些环境失败）
        try
        {
            _cpuCounter = new PerformanceCounter(
                "Processor",
                "% Processor Time",
                "_Total",
                true
            );
        }
        catch (Exception ex)
        {
            LogService.Warning("[SystemResourceMonitor] 无法创建CPU性能计数器");
            LogService.Error(ex, "[SystemResourceMonitor] CPU性能计数器创建失败详情");
            _cpuCounter = null;
        }

        // 启动监控定时器（每2秒更新一次）
        _monitorTimer = new Timer(
            MonitorCallback,
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2)
        );

        LogService.Info("[SystemResourceMonitor] 系统资源监控器已启动");
    }

    /// <summary>
    /// 监控回调
    /// </summary>
    private void MonitorCallback(object? state)
    {
        try
        {
            // 更新CPU使用率
            UpdateCpuUsage();

            // 更新内存使用
            UpdateMemoryUsage();

            // 更新线程统计
            UpdateThreadCount();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[SystemResourceMonitor] 监控回调失败");
        }
    }

    /// <summary>
    /// 更新CPU使用率
    /// </summary>
    private void UpdateCpuUsage()
    {
        try
        {
            if (_cpuCounter != null)
            {
                // 使用性能计数器
                _cpuUsage = _cpuCounter.NextValue();
            }
            else
            {
                // 手动计算CPU使用率
                DateTime currentTime = DateTime.UtcNow;
                TimeSpan currentTotalProcessorTime = _currentProcess.TotalProcessorTime;

                double elapsedMilliseconds = (currentTime - _lastSampleTime).TotalMilliseconds;
                double processorTimeMilliseconds = (currentTotalProcessorTime - _totalProcessorTime).TotalMilliseconds;

                if (elapsedMilliseconds > 0)
                {
                    // CPU使用率 = (进程CPU时间 / 实际时间) / CPU核心数
                    int processorCount = Environment.ProcessorCount;
                    _cpuUsage = (processorTimeMilliseconds / elapsedMilliseconds / processorCount) * 100;
                    _cpuUsage = Math.Clamp(_cpuUsage, 0, 100);
                }

                _lastSampleTime = currentTime;
                _totalProcessorTime = currentTotalProcessorTime;
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[SystemResourceMonitor] 更新CPU使用率失败");
            _cpuUsage = 0;
        }
    }

    /// <summary>
    /// 更新内存使用
    /// </summary>
    private void UpdateMemoryUsage()
    {
        try
        {
            _currentProcess.Refresh();
            _memoryUsage = _currentProcess.WorkingSet64;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[SystemResourceMonitor] 更新内存使用失败");
            _memoryUsage = 0;
        }
    }

    /// <summary>
    /// 更新线程统计
    /// </summary>
    private void UpdateThreadCount()
    {
        try
        {
            _currentProcess.Refresh();
            _threadCount = _currentProcess.Threads.Count;
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[SystemResourceMonitor] 更新线程统计失败");
            _threadCount = 0;
        }
    }

    /// <summary>
    /// 测量网络延迟
    /// </summary>
    public async Task<long> MeasureNetworkLatencyAsync(CancellationToken ct = default)
    {
        try
        {
            using var httpClient = new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            var stopwatch = Stopwatch.StartNew();
            using var response = await httpClient.GetAsync(_testEndpoint, ct);
            stopwatch.Stop();

            _networkLatency = stopwatch.ElapsedMilliseconds;
            return _networkLatency;
        }
        catch (Exception ex)
        {
            LogService.Warning("[SystemResourceMonitor] 测量网络延迟失败");
            LogService.Error(ex, "[SystemResourceMonitor] 网络延迟测量异常详情");
            _networkLatency = -1; // -1表示测量失败
            return _networkLatency;
        }
    }

    /// <summary>
    /// 获取系统资源快照
    /// </summary>
    public SystemResourceSnapshot GetSnapshot()
    {
        GC.Collect(0, GCCollectionMode.Optimized);
        
        return new SystemResourceSnapshot
        {
            // CPU
            CpuUsagePercent = _cpuUsage,
            ProcessorCount = Environment.ProcessorCount,
            
            // 内存
            MemoryUsageBytes = _memoryUsage,
            MemoryUsageMB = _memoryUsage / (1024.0 * 1024.0),
            TotalMemoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
            
            // GC统计
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2),
            
            // 线程
            ThreadCount = _threadCount,
            ThreadPoolAvailable = GetThreadPoolInfo(),
            
            // 网络
            NetworkLatencyMs = _networkLatency,
            
            // 进程
            ProcessUptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime(),
            
            // 时间戳
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 获取线程池信息
    /// </summary>
    private (int Worker, int IO) GetThreadPoolInfo()
    {
        ThreadPool.GetAvailableThreads(out int workerThreads, out int ioThreads);
        return (workerThreads, ioThreads);
    }

    /// <summary>
    /// 获取健康状态
    /// </summary>
    public ResourceHealthStatus GetHealthStatus()
    {
        var snapshot = GetSnapshot();
        
        // 健康判断规则
        bool isCpuHealthy = snapshot.CpuUsagePercent < 80;
        bool isMemoryHealthy = snapshot.MemoryUsageMB < 1000; // < 1GB
        bool isNetworkHealthy = snapshot.NetworkLatencyMs < 200 || snapshot.NetworkLatencyMs == -1;
        bool isThreadHealthy = snapshot.ThreadCount < 100;

        bool isHealthy = isCpuHealthy && isMemoryHealthy && isNetworkHealthy && isThreadHealthy;

        return new ResourceHealthStatus
        {
            IsHealthy = isHealthy,
            CpuHealthy = isCpuHealthy,
            MemoryHealthy = isMemoryHealthy,
            NetworkHealthy = isNetworkHealthy,
            ThreadHealthy = isThreadHealthy,
            Snapshot = snapshot,
            Issues = GetHealthIssues(isCpuHealthy, isMemoryHealthy, isNetworkHealthy, isThreadHealthy)
        };
    }

    /// <summary>
    /// 获取健康问题描述
    /// </summary>
    private string[] GetHealthIssues(bool cpu, bool memory, bool network, bool thread)
    {
        var issues = new System.Collections.Generic.List<string>();

        if (!cpu) issues.Add("CPU使用率过高");
        if (!memory) issues.Add("内存使用过高");
        if (!network) issues.Add("网络延迟过高");
        if (!thread) issues.Add("线程数过多");

        return issues.ToArray();
    }

    public void Dispose()
    {
        _monitorTimer?.Dispose();
        _cpuCounter?.Dispose();
        _currentProcess?.Dispose();
        LogService.Info("[SystemResourceMonitor] 系统资源监控器已停止");
    }
}

#region 数据模型

/// <summary>
/// 系统资源快照
/// </summary>
public class SystemResourceSnapshot
{
    // CPU
    public double CpuUsagePercent { get; init; }
    public int ProcessorCount { get; init; }
    
    // 内存
    public long MemoryUsageBytes { get; init; }
    public double MemoryUsageMB { get; init; }
    public double TotalMemoryMB { get; init; }
    
    // GC
    public int Gen0Collections { get; init; }
    public int Gen1Collections { get; init; }
    public int Gen2Collections { get; init; }
    
    // 线程
    public int ThreadCount { get; init; }
    public (int Worker, int IO) ThreadPoolAvailable { get; init; }
    
    // 网络
    public long NetworkLatencyMs { get; init; }
    
    // 进程
    public TimeSpan ProcessUptime { get; init; }
    
    // 时间戳
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// 资源健康状态
/// </summary>
public class ResourceHealthStatus
{
    public bool IsHealthy { get; init; }
    public bool CpuHealthy { get; init; }
    public bool MemoryHealthy { get; init; }
    public bool NetworkHealthy { get; init; }
    public bool ThreadHealthy { get; init; }
    public SystemResourceSnapshot Snapshot { get; init; } = null!;
    public string[] Issues { get; init; } = Array.Empty<string>();
}

#endregion
