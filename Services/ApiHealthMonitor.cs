using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace 币安量化机器人.Services;

/// <summary>
/// API健康监控器 - 实时追踪API调用的成功率、响应时间和错误统计
/// </summary>
/// <remarks>
/// <para>监控指标包括:</para>
/// <list type="bullet">
/// <item>成功率 (Success Rate)</item>
/// <item>平均/P95/P99响应时间</item>
/// <item>错误统计和分类</item>
/// <item>慢请求追踪 (>2秒)</item>
/// <item>按端点的详细统计</item>
/// </list>
/// <para>用于实时监控API健康状态,辅助熔断器决策</para>
/// </remarks>
public class ApiHealthMonitor
{
    /// <summary>
    /// 健康监控配置常量
    /// </summary>
    private static class HealthMonitorConstants
    {
        /// <summary>
        /// 保留的最大指标数量
        /// </summary>
        public const int MAX_METRICS = 1000;

        /// <summary>
        /// 慢请求阈值(秒)
        /// </summary>
        public const double SLOW_REQUEST_THRESHOLD_SECONDS = 2.0;

        /// <summary>
        /// 健康状态成功率阈值
        /// </summary>
        public const double HEALTHY_SUCCESS_RATE = 0.95;

        /// <summary>
        /// 健康状态平均延迟阈值(秒)
        /// </summary>
        public const double HEALTHY_LATENCY_THRESHOLD_SECONDS = 2.0;

        /// <summary>
        /// 错误摘要Top N
        /// </summary>
        public const int TOP_ERRORS_COUNT = 5;

        /// <summary>
        /// P95百分位数
        /// </summary>
        public const double P95_PERCENTILE = 0.95;

        /// <summary>
        /// P99百分位数
        /// </summary>
        public const double P99_PERCENTILE = 0.99;
    }

    private readonly ConcurrentQueue<ApiCallMetric> _metrics = new();
    private readonly int _maxMetrics = HealthMonitorConstants.MAX_METRICS;
    private long _totalCalls;
    private long _successfulCalls;
    private long _failedCalls;

    /// <summary>
    /// 获取总调用次数
    /// </summary>
    public long TotalCalls => Interlocked.Read(ref _totalCalls);

    /// <summary>
    /// 获取成功调用次数
    /// </summary>
    public long SuccessfulCalls => Interlocked.Read(ref _successfulCalls);

    /// <summary>
    /// 获取失败调用次数
    /// </summary>
    public long FailedCalls => Interlocked.Read(ref _failedCalls);

    /// <summary>
    /// 获取成功率 (0.0 - 1.0)
    /// </summary>
    public double SuccessRate => TotalCalls > 0 ? (double)SuccessfulCalls / TotalCalls : 0;

    /// <summary>
    /// 记录API调用
    /// </summary>
    /// <param name="endpoint">API端点路径</param>
    /// <param name="success">是否成功</param>
    /// <param name="duration">请求耗时</param>
    /// <param name="errorMessage">错误消息 (失败时)</param>
    /// <param name="httpStatusCode">HTTP状态码 (可选)</param>
    public void RecordCall(string endpoint, bool success, TimeSpan duration, string? errorMessage = null, int? httpStatusCode = null)
    {
        Interlocked.Increment(ref _totalCalls);
        if (success)
        {
            Interlocked.Increment(ref _successfulCalls);
        }
        else
        {
            Interlocked.Increment(ref _failedCalls);
        }

        var metric = new ApiCallMetric
        {
            Endpoint = endpoint,
            Timestamp = DateTime.UtcNow,
            Success = success,
            Duration = duration,
            ErrorMessage = errorMessage,
            HttpStatusCode = httpStatusCode
        };

        _metrics.Enqueue(metric);

        // 限制队列大小,移除最旧的记录
        while (_metrics.Count > _maxMetrics)
        {
            _metrics.TryDequeue(out _);
        }

        // 记录慢请求
        if (duration.TotalSeconds > HealthMonitorConstants.SLOW_REQUEST_THRESHOLD_SECONDS)
        {
            StartupDiagnostics.Log($"SlowRequest: {endpoint} took {duration.TotalMilliseconds:F0}ms");
        }

        // 记录失败请求
        if (!success)
        {
            StartupDiagnostics.Log($"FailedRequest: {endpoint} - {errorMessage ?? "Unknown"} (HTTP {httpStatusCode ?? 0})");
        }
    }

    /// <summary>
    /// 获取健康报告
    /// </summary>
    /// <param name="window">时间窗口 (null表示全部历史)</param>
    /// <returns>API健康报告</returns>
    /// <remarks>
    /// 健康状态判定标准:
    /// <list type="bullet">
    /// <item>成功率 ≥ 95%</item>
    /// <item>平均延迟 &lt; 2秒</item>
    /// </list>
    /// </remarks>
    public ApiHealthReport GetHealthReport(TimeSpan? window = null)
    {
        DateTime cutoff = window.HasValue ? DateTime.UtcNow - window.Value : DateTime.MinValue;
        var recentMetrics = _metrics.Where(m => m.Timestamp >= cutoff).ToList();

        if (recentMetrics.Count == 0)
        {
            return new ApiHealthReport
            {
                IsHealthy = true,
                SuccessRate = 1.0,
                AverageLatency = TimeSpan.Zero,
                P95Latency = TimeSpan.Zero,
                P99Latency = TimeSpan.Zero,
                ErrorCount = 0,
                SlowRequestCount = 0,
                TopErrors = Array.Empty<ErrorSummary>()
            };
        }

        int successCount = recentMetrics.Count(m => m.Success);
        double successRate = (double)successCount / recentMetrics.Count;

        double[] durations = recentMetrics.Select(m => m.Duration.TotalMilliseconds).OrderBy(d => d).ToArray();
        var avgLatency = TimeSpan.FromMilliseconds(durations.Average());
        var p95Latency = TimeSpan.FromMilliseconds(durations[(int)(durations.Length * HealthMonitorConstants.P95_PERCENTILE)]);
        var p99Latency = TimeSpan.FromMilliseconds(durations[(int)(durations.Length * HealthMonitorConstants.P99_PERCENTILE)]);

        ErrorSummary[] errors = recentMetrics
            .Where(m => !m.Success && m.ErrorMessage != null)
            .GroupBy(m => m.ErrorMessage)
            .Select(g => new ErrorSummary
            {
                ErrorMessage = g.Key!,
                Count = g.Count(),
                LastOccurrence = g.Max(m => m.Timestamp)
            })
            .OrderByDescending(e => e.Count)
            .Take(HealthMonitorConstants.TOP_ERRORS_COUNT)
            .ToArray();

        int slowRequestCount = recentMetrics.Count(m => m.Duration.TotalSeconds > HealthMonitorConstants.SLOW_REQUEST_THRESHOLD_SECONDS);

        return new ApiHealthReport
        {
            IsHealthy = successRate >= HealthMonitorConstants.HEALTHY_SUCCESS_RATE &&
                        avgLatency.TotalSeconds < HealthMonitorConstants.HEALTHY_LATENCY_THRESHOLD_SECONDS,
            SuccessRate = successRate,
            AverageLatency = avgLatency,
            P95Latency = p95Latency,
            P99Latency = p99Latency,
            ErrorCount = recentMetrics.Count - successCount,
            SlowRequestCount = slowRequestCount,
            TopErrors = errors
        };
    }

    /// <summary>
    /// 获取按端点分组的统计信息
    /// </summary>
    /// <param name="window">时间窗口 (null表示全部历史)</param>
    /// <returns>端点统计列表,按调用次数降序排列</returns>
    public IReadOnlyList<EndpointStats> GetEndpointStats(TimeSpan? window = null)
    {
        DateTime cutoff = window.HasValue ? DateTime.UtcNow - window.Value : DateTime.MinValue;
        var recentMetrics = _metrics.Where(m => m.Timestamp >= cutoff).ToList();

        return recentMetrics
            .GroupBy(m => m.Endpoint)
            .Select(g => new EndpointStats
            {
                Endpoint = g.Key,
                TotalCalls = g.Count(),
                SuccessfulCalls = g.Count(m => m.Success),
                FailedCalls = g.Count(m => !m.Success),
                AverageLatency = TimeSpan.FromMilliseconds(g.Average(m => m.Duration.TotalMilliseconds)),
                MaxLatency = TimeSpan.FromMilliseconds(g.Max(m => m.Duration.TotalMilliseconds))
            })
            .OrderByDescending(s => s.TotalCalls)
            .ToList();
    }

    /// <summary>
    /// 重置所有统计数据
    /// </summary>
    /// <remarks>
    /// 用于测试或需要清空历史数据时调用
    /// </remarks>
    public void Reset()
    {
        _metrics.Clear();
        Interlocked.Exchange(ref _totalCalls, 0);
        Interlocked.Exchange(ref _successfulCalls, 0);
        Interlocked.Exchange(ref _failedCalls, 0);
    }
}

/// <summary>
/// API调用指标 - 单次调用的详细记录
/// </summary>
internal class ApiCallMetric
{
    /// <summary>
    /// API端点路径
    /// </summary>
    public required string Endpoint { get; init; }

    /// <summary>
    /// 调用时间戳
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 请求耗时
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// 错误消息 (失败时)
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// HTTP状态码
    /// </summary>
    public int? HttpStatusCode { get; init; }
}

/// <summary>
/// API健康报告 - 聚合的健康状态数据
/// </summary>
public class ApiHealthReport
{
    /// <summary>
    /// 是否健康 (成功率≥95% 且 平均延迟&lt;2秒)
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// 成功率 (0.0 - 1.0)
    /// </summary>
    public double SuccessRate { get; init; }

    /// <summary>
    /// 平均响应时间
    /// </summary>
    public TimeSpan AverageLatency { get; init; }

    /// <summary>
    /// P95响应时间 (95%的请求响应时间)
    /// </summary>
    public TimeSpan P95Latency { get; init; }

    /// <summary>
    /// P99响应时间 (99%的请求响应时间)
    /// </summary>
    public TimeSpan P99Latency { get; init; }

    /// <summary>
    /// 错误总数
    /// </summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// 慢请求数量 (>2秒)
    /// </summary>
    public int SlowRequestCount { get; init; }

    /// <summary>
    /// Top 5错误摘要
    /// </summary>
    public IReadOnlyList<ErrorSummary> TopErrors { get; init; } = Array.Empty<ErrorSummary>();
}

/// <summary>
/// 错误摘要 - 相同错误的聚合统计
/// </summary>
public class ErrorSummary
{
    /// <summary>
    /// 错误消息
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// 出现次数
    /// </summary>
    public int Count { get; init; }

    /// <summary>
    /// 最后一次出现时间
    /// </summary>
    public DateTime LastOccurrence { get; init; }
}

/// <summary>
/// 端点统计 - 单个API端点的性能统计
/// </summary>
public class EndpointStats
{
    /// <summary>
    /// API端点路径
    /// </summary>
    public required string Endpoint { get; init; }

    /// <summary>
    /// 总调用次数
    /// </summary>
    public int TotalCalls { get; init; }

    /// <summary>
    /// 成功调用次数
    /// </summary>
    public int SuccessfulCalls { get; init; }

    /// <summary>
    /// 失败调用次数
    /// </summary>
    public int FailedCalls { get; init; }

    /// <summary>
    /// 平均响应时间
    /// </summary>
    public TimeSpan AverageLatency { get; init; }

    /// <summary>
    /// 最大响应时间
    /// </summary>
    public TimeSpan MaxLatency { get; init; }

    /// <summary>
    /// 成功率 (0.0 - 1.0)
    /// </summary>
    public double SuccessRate => TotalCalls > 0 ? (double)SuccessfulCalls / TotalCalls : 0;
}
