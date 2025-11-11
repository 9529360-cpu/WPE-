using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.Performance;

/// <summary>
/// 系统性能监控器
/// </summary>
/// <remarks>
/// 核心功能:
/// 1. CPU/内存/网络监控
/// 2. 响应时间追踪
/// 3. 吞吐量统计
/// 4. 性能瓶颈识别
/// 5. 自动告警
/// 
/// 监控指标:
/// - CPU使用率
/// - 内存使用量
/// - GC统计
/// - 线程数
/// - 响应时间分位数
/// - 请求吞吐量
/// </remarks>
public class PerformanceMonitor : IDisposable
{
    private readonly Timer _monitorTimer;
    private readonly ConcurrentDictionary<string, MetricCollector> _metrics;
    private readonly Process _currentProcess;

    // 性能计数器
    private long _totalRequests;
    private long _failedRequests;
    private readonly ConcurrentBag<double> _responseTimes;

    // 系统指标
    private double _cpuUsage;
    private long _memoryUsageMB;
    private int _threadCount;
    private long _gen0Collections;
    private long _gen1Collections;
    private long _gen2Collections;

    public PerformanceMonitor()
    {
        _metrics = new ConcurrentDictionary<string, MetricCollector>();
        _currentProcess = Process.GetCurrentProcess();
        _responseTimes = new ConcurrentBag<double>();

        // 每秒监控一次
        _monitorTimer = new Timer(MonitorCallback, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        LogService.Info("[PerformanceMonitor] 性能监控器已启动");
    }

    #region 指标记录

    /// <summary>
    /// 记录操作
    /// </summary>
    public IDisposable RecordOperation(string operationName)
    {
        return new OperationTracker(this, operationName);
    }

    /// <summary>
    /// 记录请求
    /// </summary>
    internal void RecordRequest(string operation, double durationMs, bool success)
    {
        Interlocked.Increment(ref _totalRequests);
        if (!success)
        {
            Interlocked.Increment(ref _failedRequests);
        }

        _responseTimes.Add(durationMs);

        // 获取或创建指标收集器
        MetricCollector collector = _metrics.GetOrAdd(operation, _ => new MetricCollector());
        collector.Record(durationMs, success);
    }

    /// <summary>
    /// 记录自定义指标
    /// </summary>
    public void RecordMetric(string name, double value)
    {
        MetricCollector collector = _metrics.GetOrAdd(name, _ => new MetricCollector());
        collector.RecordValue(value);
    }

    #endregion

    #region 系统监控

    /// <summary>
    /// 监控回调
    /// </summary>
    private void MonitorCallback(object? state)
    {
        try
        {
            RecordSystemMetrics();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceMonitor] 监控回调失败");
        }
    }

    private void RecordSystemMetrics()
    {
        // 刷新进程信息
        _currentProcess.Refresh();

        // CPU使用率（简化计算）
        _cpuUsage = GetCpuUsage();

        // 内存使用
        _memoryUsageMB = _currentProcess.WorkingSet64 / (1024 * 1024);

        // 线程数
        _threadCount = _currentProcess.Threads.Count;

        // GC统计
        _gen0Collections = GC.CollectionCount(0);
        _gen1Collections = GC.CollectionCount(1);
        _gen2Collections = GC.CollectionCount(2);

        // 清理旧的响应时间数据（保留最近1000个）
        if (_responseTimes.Count > 1000)
        {
            var recent = _responseTimes.Take(1000).ToList();
            _responseTimes.Clear();
            foreach (double time in recent)
            {
                _responseTimes.Add(time);
            }
        }
    }

    /// <summary>
    /// 获取CPU使用率
    /// </summary>
    private double GetCpuUsage()
    {
        // 简化实现：使用TotalProcessorTime
        try
        {
            _currentProcess.Refresh();
            TimeSpan totalTime = _currentProcess.TotalProcessorTime;
            double cpuPercent = totalTime.TotalMilliseconds / (Environment.ProcessorCount * 1000.0);
            return Math.Min(cpuPercent * 100, 100);
        }
        catch
        {
            return 0;
        }
    }

    #endregion

    #region 性能报告

    /// <summary>
    /// 获取当前系统指标
    /// </summary>
    public SystemMetrics GetSystemMetrics()
    {
        return new SystemMetrics
        {
            CpuUsagePercent = _cpuUsage,
            MemoryUsageMB = _memoryUsageMB,
            ThreadCount = _threadCount,
            Gen0Collections = _gen0Collections,
            Gen1Collections = _gen1Collections,
            Gen2Collections = _gen2Collections,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 获取请求统计
    /// </summary>
    public RequestStatistics GetRequestStatistics()
    {
        long total = Interlocked.Read(ref _totalRequests);
        long failed = Interlocked.Read(ref _failedRequests);

        double[] times = _responseTimes.ToArray();

        return new RequestStatistics
        {
            TotalRequests = total,
            FailedRequests = failed,
            SuccessRate = total > 0 ? (double)(total - failed) / total : 1.0,
            AverageResponseTime = times.Length > 0 ? times.Average() : 0,
            MedianResponseTime = CalculatePercentile(times, 0.5),
            P95ResponseTime = CalculatePercentile(times, 0.95),
            P99ResponseTime = CalculatePercentile(times, 0.99),
            MinResponseTime = times.Length > 0 ? times.Min() : 0,
            MaxResponseTime = times.Length > 0 ? times.Max() : 0
        };
    }

    /// <summary>
    /// 获取操作指标
    /// </summary>
    public Dictionary<string, OperationMetrics> GetOperationMetrics()
    {
        var result = new Dictionary<string, OperationMetrics>();

        foreach (KeyValuePair<string, MetricCollector> kvp in _metrics)
        {
            MetricCollector collector = kvp.Value;
            MetricSnapshot snapshot = collector.GetSnapshot();

            result[kvp.Key] = new OperationMetrics
            {
                OperationName = kvp.Key,
                Count = snapshot.Count,
                SuccessCount = snapshot.SuccessCount,
                FailureCount = snapshot.FailureCount,
                AverageDuration = snapshot.AverageDuration,
                MinDuration = snapshot.MinDuration,
                MaxDuration = snapshot.MaxDuration,
                P95Duration = snapshot.P95Duration
            };
        }

        return result;
    }

    /// <summary>
    /// 获取完整性能报告
    /// </summary>
    public PerformanceReport GetReport()
    {
        return new PerformanceReport
        {
            SystemMetrics = GetSystemMetrics(),
            RequestStatistics = GetRequestStatistics(),
            OperationMetrics = GetOperationMetrics(),
            GeneratedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 打印性能报告
    /// </summary>
    public void PrintReport()
    {
        PerformanceReport report = GetReport();

        LogService.Info("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        LogService.Info("📊 [PerformanceMonitor] 性能报告");
        LogService.Info("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        // 系统指标
        LogService.Info("🖥️  系统指标:");
        LogService.Info("   CPU使用率: {CpuUsage:F2}%", report.SystemMetrics.CpuUsagePercent);
        LogService.Info("   内存使用: {Memory:F2} MB", report.SystemMetrics.MemoryUsageMB);
        LogService.Info("   线程数: {Threads}", report.SystemMetrics.ThreadCount);
        LogService.Info("   GC: Gen0={Gen0}, Gen1={Gen1}, Gen2={Gen2}",
            report.SystemMetrics.Gen0Collections,
            report.SystemMetrics.Gen1Collections,
            report.SystemMetrics.Gen2Collections);

        // 请求统计
        LogService.Info("📈 请求统计:");
        LogService.Info("   总请求数: {Total}", report.RequestStatistics.TotalRequests);
        LogService.Info("   成功率: {SuccessRate:P2}", report.RequestStatistics.SuccessRate);
        LogService.Info("   平均响应: {Avg:F2} ms", report.RequestStatistics.AverageResponseTime);
        LogService.Info("   P95响应: {P95:F2} ms", report.RequestStatistics.P95ResponseTime);
        LogService.Info("   P99响应: {P99:F2} ms", report.RequestStatistics.P99ResponseTime);

        // Top 5 操作
        IEnumerable<KeyValuePair<string, OperationMetrics>> top5 = report.OperationMetrics
            .OrderByDescending(kvp => kvp.Value.Count)
            .Take(5);

        LogService.Info("🔝 Top 5 操作:");
        foreach (KeyValuePair<string, OperationMetrics> kvp in top5)
        {
            LogService.Info("   {Operation}: Count={Count}, Avg={Avg:F2}ms",
                kvp.Key, kvp.Value.Count, kvp.Value.AverageDuration);
        }

        LogService.Info("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 计算百分位数
    /// </summary>
    private double CalculatePercentile(double[] values, double percentile)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        double[] sorted = values.OrderBy(v => v).ToArray();
        int index = (int)(sorted.Length * percentile);
        index = Math.Min(index, sorted.Length - 1);

        return sorted[index];
    }

    #endregion

    public void Dispose()
    {
        _monitorTimer?.Dispose();
        LogService.Info("[PerformanceMonitor] 已停止监控");
    }
}

#region 指标收集器

/// <summary>
/// 指标收集器
/// </summary>
internal class MetricCollector
{
    private long _count;
    private long _successCount;
    private long _failureCount;
    private readonly ConcurrentBag<double> _durations;

    public MetricCollector()
    {
        _durations = new ConcurrentBag<double>();
    }

    public void Record(double duration, bool success)
    {
        Interlocked.Increment(ref _count);
        if (success)
        {
            Interlocked.Increment(ref _successCount);
        }
        else
        {
            Interlocked.Increment(ref _failureCount);
        }

        _durations.Add(duration);

        // 限制数据量
        if (_durations.Count > 1000)
        {
            var recent = _durations.Take(1000).ToList();
            _durations.Clear();
            foreach (double d in recent)
            {
                _durations.Add(d);
            }
        }
    }

    public void RecordValue(double value)
    {
        Interlocked.Increment(ref _count);
        _durations.Add(value);
    }

    public MetricSnapshot GetSnapshot()
    {
        double[] durations = _durations.ToArray();
        double[] sorted = durations.OrderBy(d => d).ToArray();

        return new MetricSnapshot
        {
            Count = Interlocked.Read(ref _count),
            SuccessCount = Interlocked.Read(ref _successCount),
            FailureCount = Interlocked.Read(ref _failureCount),
            AverageDuration = durations.Length > 0 ? durations.Average() : 0,
            MinDuration = durations.Length > 0 ? durations.Min() : 0,
            MaxDuration = durations.Length > 0 ? durations.Max() : 0,
            P95Duration = sorted.Length > 0 ? sorted[(int)(sorted.Length * 0.95)] : 0
        };
    }
}

/// <summary>
/// 指标快照
/// </summary>
internal class MetricSnapshot
{
    public long Count { get; init; }
    public long SuccessCount { get; init; }
    public long FailureCount { get; init; }
    public double AverageDuration { get; init; }
    public double MinDuration { get; init; }
    public double MaxDuration { get; init; }
    public double P95Duration { get; init; }
}

#endregion

#region 操作追踪器

/// <summary>
/// 操作追踪器
/// </summary>
internal class OperationTracker : IDisposable
{
    private readonly PerformanceMonitor _monitor;
    private readonly string _operationName;
    private readonly Stopwatch _stopwatch;
    private bool _disposed;

    public OperationTracker(PerformanceMonitor monitor, string operationName)
    {
        _monitor = monitor;
        _operationName = operationName;
        _stopwatch = Stopwatch.StartNew();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopwatch.Stop();
        _monitor.RecordRequest(_operationName, _stopwatch.Elapsed.TotalMilliseconds, true);
    }
}

#endregion

#region 数据模型

/// <summary>
/// 系统指标
/// </summary>
public class SystemMetrics
{
    public double CpuUsagePercent { get; init; }
    public long MemoryUsageMB { get; init; }
    public int ThreadCount { get; init; }
    public long Gen0Collections { get; init; }
    public long Gen1Collections { get; init; }
    public long Gen2Collections { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// 请求统计
/// </summary>
public class RequestStatistics
{
    public long TotalRequests { get; init; }
    public long FailedRequests { get; init; }
    public double SuccessRate { get; init; }
    public double AverageResponseTime { get; init; }
    public double MedianResponseTime { get; init; }
    public double P95ResponseTime { get; init; }
    public double P99ResponseTime { get; init; }
    public double MinResponseTime { get; init; }
    public double MaxResponseTime { get; init; }
}

/// <summary>
/// 操作指标
/// </summary>
public class OperationMetrics
{
    public string OperationName { get; init; } = string.Empty;
    public long Count { get; init; }
    public long SuccessCount { get; init; }
    public long FailureCount { get; init; }
    public double AverageDuration { get; init; }
    public double MinDuration { get; init; }
    public double MaxDuration { get; init; }
    public double P95Duration { get; init; }
}

/// <summary>
/// 性能报告
/// </summary>
public class PerformanceReport
{
    public SystemMetrics SystemMetrics { get; init; } = null!;
    public RequestStatistics RequestStatistics { get; init; } = null!;
    public Dictionary<string, OperationMetrics> OperationMetrics { get; init; } = new();
    public DateTime GeneratedAt { get; init; }
}

#endregion
