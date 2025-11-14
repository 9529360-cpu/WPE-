using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.Resilience;

/// <summary>
/// 异常检测系统
/// </summary>
/// <remarks>
/// 核心功能:
/// 1. 异常模式识别
/// 2. 异常分类和优先级
/// 3. 异常趋势分析
/// 4. 自动告警触发
/// 5. 根因分析
/// 
/// 检测范围:
/// - API异常
/// - 交易异常
/// - 系统异常
/// - 性能异常
/// - 数据异常
/// </remarks>
public class AnomalyDetectionSystem : IDisposable
{
    private readonly ConcurrentQueue<AnomalyEvent> _recentAnomalies;
    private readonly ConcurrentDictionary<string, AnomalyPattern> _patterns;
    private readonly Timer _analysisTimer;

    // 异常阈值配置
    private readonly AnomalyThresholds _thresholds;

    // 统计信息
    private long _totalAnomalies;
    private long _criticalAnomalies;
    private long _resolvedAnomalies;

    public AnomalyDetectionSystem(AnomalyThresholds? thresholds = null)
    {
        _thresholds = thresholds ?? AnomalyThresholds.Default;
        _recentAnomalies = new ConcurrentQueue<AnomalyEvent>();
        _patterns = new ConcurrentDictionary<string, AnomalyPattern>();

        // 每分钟分析一次
        _analysisTimer = new Timer(AnalysisCallback, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        LogService.Info("[AnomalyDetectionSystem] 异常检测系统已启动");
    }

    /// <summary>
    /// 异常检测事件
    /// </summary>
    public event EventHandler<AnomalyDetectedEventArgs>? AnomalyDetected;

    #region 异常检测

    /// <summary>
    /// 检测API异常
    /// </summary>
    public void DetectApiAnomaly(string endpoint, Exception exception, TimeSpan responseTime)
    {
        AnomalySeverity severity = DetermineApiSeverity(exception, responseTime);

        AnomalyEvent anomaly = new AnomalyEvent
        {
            Id = Guid.NewGuid().ToString(),
            Type = AnomalyType.ApiFailure,
            Severity = severity,
            Source = endpoint,
            Message = $"API异常: {exception.Message}",
            Details = new Dictionary<string, object>
            {
                ["Endpoint"] = endpoint,
                ["Exception"] = exception.GetType().Name,
                ["ResponseTime"] = responseTime.TotalMilliseconds,
                ["StackTrace"] = exception.StackTrace ?? string.Empty
            },
            Timestamp = DateTime.UtcNow
        };

        RecordAnomaly(anomaly);
    }

    /// <summary>
    /// 检测交易异常
    /// </summary>
    public void DetectTradingAnomaly(string symbol, string reason, Dictionary<string, object>? details = null)
    {
        AnomalySeverity severity = DetermineTradingSeverity(reason);

        AnomalyEvent anomaly = new AnomalyEvent
        {
            Id = Guid.NewGuid().ToString(),
            Type = AnomalyType.TradingError,
            Severity = severity,
            Source = symbol,
            Message = $"交易异常: {reason}",
            Details = details ?? new Dictionary<string, object>(),
            Timestamp = DateTime.UtcNow
        };

        RecordAnomaly(anomaly);
    }

    /// <summary>
    /// 检测性能异常
    /// </summary>
    public void DetectPerformanceAnomaly(string metric, double value, double threshold)
    {
        if (value <= threshold)
        {
            return;
        }

        AnomalySeverity severity = value > threshold * 2 ? AnomalySeverity.High : AnomalySeverity.Medium;

        AnomalyEvent anomaly = new AnomalyEvent
        {
            Id = Guid.NewGuid().ToString(),
            Type = AnomalyType.PerformanceDegradation,
            Severity = severity,
            Source = metric,
            Message = $"性能异常: {metric}={value:F2} (阈值={threshold:F2})",
            Details = new Dictionary<string, object>
            {
                ["Metric"] = metric,
                ["Value"] = value,
                ["Threshold"] = threshold,
                ["Deviation"] = (value - threshold) / threshold
            },
            Timestamp = DateTime.UtcNow
        };

        RecordAnomaly(anomaly);
    }

    /// <summary>
    /// 检测数据异常
    /// </summary>
    public void DetectDataAnomaly(string dataType, string reason, object? value = null)
    {
        AnomalyEvent anomaly = new AnomalyEvent
        {
            Id = Guid.NewGuid().ToString(),
            Type = AnomalyType.DataCorruption,
            Severity = AnomalySeverity.Medium,
            Source = dataType,
            Message = $"数据异常: {reason}",
            Details = new Dictionary<string, object>
            {
                ["DataType"] = dataType,
                ["Value"] = value?.ToString() ?? "null"
            },
            Timestamp = DateTime.UtcNow
        };

        RecordAnomaly(anomaly);
    }

    /// <summary>
    /// 检测系统异常
    /// </summary>
    public void DetectSystemAnomaly(string component, Exception exception)
    {
        AnomalyEvent anomaly = new AnomalyEvent
        {
            Id = Guid.NewGuid().ToString(),
            Type = AnomalyType.SystemFailure,
            Severity = AnomalySeverity.Critical,
            Source = component,
            Message = $"系统异常: {exception.Message}",
            Details = new Dictionary<string, object>
            {
                ["Component"] = component,
                ["Exception"] = exception.GetType().Name,
                ["Message"] = exception.Message,
                ["StackTrace"] = exception.StackTrace ?? string.Empty
            },
            Timestamp = DateTime.UtcNow
        };

        RecordAnomaly(anomaly);
    }

    #endregion

    #region 异常记录和分析

    /// <summary>
    /// 记录异常
    /// </summary>
    private void RecordAnomaly(AnomalyEvent anomaly)
    {
        // 添加到队列
        _recentAnomalies.Enqueue(anomaly);

        // 限制队列大小
        while (_recentAnomalies.Count > 1000)
        {
            _recentAnomalies.TryDequeue(out _);
        }

        // 更新统计
        Interlocked.Increment(ref _totalAnomalies);
        if (anomaly.Severity == AnomalySeverity.Critical)
        {
            Interlocked.Increment(ref _criticalAnomalies);
        }

        // 记录日志
        LogAnomaly(anomaly);

        // 触发事件
        AnomalyDetected?.Invoke(this, new AnomalyDetectedEventArgs(anomaly));

        // 更新模式
        UpdatePattern(anomaly);
    }

    /// <summary>
    /// 更新异常模式
    /// </summary>
    private void UpdatePattern(AnomalyEvent anomaly)
    {
        string patternKey = $"{anomaly.Type}:{anomaly.Source}";

        AnomalyPattern pattern = _patterns.GetOrAdd(patternKey, _ => new AnomalyPattern
        {
            Type = anomaly.Type,
            Source = anomaly.Source,
            FirstOccurrence = anomaly.Timestamp,
            LastOccurrence = anomaly.Timestamp,
            Count = 0
        });

        pattern.Count++;
        pattern.LastOccurrence = anomaly.Timestamp;

        // 检测频繁异常
        if (pattern.Count > _thresholds.FrequentAnomalyThreshold)
        {
            LogService.Warning("[AnomalyDetectionSystem] 检测到频繁异常: {Pattern}, Count={Count}",
                patternKey, pattern.Count);
        }
    }

    /// <summary>
    /// 分析回调
    /// </summary>
    private void AnalysisCallback(object? state)
    {
        try
        {
            // 分析异常趋势
            AnalyzeTrends();

            // 识别相关异常
            IdentifyCorrelations();

            // 清理旧的模式
            CleanupOldPatterns();
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AnomalyDetectionSystem] 分析失败");
        }
    }

    /// <summary>
    /// 分析异常趋势
    /// </summary>
    private void AnalyzeTrends()
    {
        AnomalyEvent[] recent = _recentAnomalies.ToArray();
        if (recent.Length < 10)
        {
            return;
        }

        // 计算最近1小时的异常率
        DateTime oneHourAgo = DateTime.UtcNow.AddHours(-1);
        int recentCount = recent.Count(a => a.Timestamp > oneHourAgo);

        double anomalyRate = recentCount / 60.0; // 每分钟异常数

        if (anomalyRate > _thresholds.AnomalyRateThreshold)
        {
            LogService.Warning("[AnomalyDetectionSystem] 异常率过高: {Rate:F2}/分钟 (阈值={Threshold})",
                anomalyRate, _thresholds.AnomalyRateThreshold);
        }
    }

    /// <summary>
    /// 识别相关异常
    /// </summary>
    private void IdentifyCorrelations()
    {
        // 简化实现：识别同时发生的异常
        AnomalyEvent[] recent = _recentAnomalies
            .Where(a => a.Timestamp > DateTime.UtcNow.AddMinutes(-5))
            .ToArray();

        if (recent.Length < 2)
        {
            return;
        }

        // 按类型分组
        var groups = recent
            .GroupBy(a => a.Type)
            .Where(g => g.Count() > 1)
            .ToArray();

        foreach (var group in groups)
        {
            LogService.Info("[AnomalyDetectionSystem] 检测到相关异常: Type={Type}, Count={Count}",
                group.Key, group.Count());
        }
    }

    /// <summary>
    /// 清理旧模式
    /// </summary>
    private void CleanupOldPatterns()
    {
        DateTime threshold = DateTime.UtcNow.AddHours(-24);

        var oldPatterns = _patterns
            .Where(kvp => kvp.Value.LastOccurrence < threshold)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (string key in oldPatterns)
        {
            _patterns.TryRemove(key, out _);
        }
    }

    #endregion

    #region 严重程度评估

    /// <summary>
    /// 确定API异常严重程度
    /// </summary>
    private AnomalySeverity DetermineApiSeverity(Exception exception, TimeSpan responseTime)
    {
        // 超时或连接失败
        if (exception is TaskCanceledException or TimeoutException)
        {
            return AnomalySeverity.High;
        }

        // 响应时间过长
        if (responseTime.TotalSeconds > 10)
        {
            return AnomalySeverity.High;
        }

        // 认证失败
        if (exception.Message.Contains("401") || exception.Message.Contains("403"))
        {
            return AnomalySeverity.Critical;
        }

        // 速率限制
        if (exception.Message.Contains("429"))
        {
            return AnomalySeverity.Medium;
        }

        return AnomalySeverity.Low;
    }

    /// <summary>
    /// 确定交易异常严重程度
    /// </summary>
    private AnomalySeverity DetermineTradingSeverity(string reason)
    {
        string lowerReason = reason.ToLowerInvariant();

        // 资金不足
        if (lowerReason.Contains("insufficient") || lowerReason.Contains("余额"))
        {
            return AnomalySeverity.High;
        }

        // 订单被拒绝
        if (lowerReason.Contains("rejected") || lowerReason.Contains("拒绝"))
        {
            return AnomalySeverity.High;
        }

        // 风险控制
        if (lowerReason.Contains("risk") || lowerReason.Contains("风险"))
        {
            return AnomalySeverity.Medium;
        }

        return AnomalySeverity.Low;
    }

    #endregion

    #region 查询和统计

    /// <summary>
    /// 获取最近异常
    /// </summary>
    public List<AnomalyEvent> GetRecentAnomalies(int count = 50)
    {
        return _recentAnomalies.TakeLast(count).ToList();
    }

    /// <summary>
    /// 获取异常模式
    /// </summary>
    public List<AnomalyPattern> GetAnomalyPatterns(int topN = 20)
    {
        return _patterns.Values
            .OrderByDescending(p => p.Count)
            .Take(topN)
            .ToList();
    }

    /// <summary>
    /// 获取异常统计
    /// </summary>
    public AnomalyStatistics GetStatistics()
    {
        AnomalyEvent[] recent = _recentAnomalies.ToArray();
        DateTime oneHourAgo = DateTime.UtcNow.AddHours(-1);

        return new AnomalyStatistics
        {
            TotalAnomalies = Interlocked.Read(ref _totalAnomalies),
            CriticalAnomalies = Interlocked.Read(ref _criticalAnomalies),
            ResolvedAnomalies = Interlocked.Read(ref _resolvedAnomalies),
            RecentHourCount = recent.Count(a => a.Timestamp > oneHourAgo),
            PatternCount = _patterns.Count,
            AnomalyRate = recent.Length > 0 ? recent.Count(a => a.Timestamp > oneHourAgo) / 60.0 : 0
        };
    }

    /// <summary>
    /// 标记异常已解决
    /// </summary>
    public void MarkResolved(string anomalyId)
    {
        Interlocked.Increment(ref _resolvedAnomalies);
        LogService.Info("[AnomalyDetectionSystem] 异常已解决: {AnomalyId}", anomalyId);
    }

    #endregion

    #region 日志

    /// <summary>
    /// 记录异常日志
    /// </summary>
    private void LogAnomaly(AnomalyEvent anomaly)
    {
        string emoji = anomaly.Severity switch
        {
            AnomalySeverity.Critical => "🔴",
            AnomalySeverity.High => "🟠",
            AnomalySeverity.Medium => "🟡",
            _ => "🔵"
        };

        LogService.Warning("{Emoji} [AnomalyDetection] {Type} | {Severity} | {Source} | {Message}",
            emoji, anomaly.Type, anomaly.Severity, anomaly.Source, anomaly.Message);
    }

    #endregion

    public void Dispose()
    {
        _analysisTimer?.Dispose();
        LogService.Info("[AnomalyDetectionSystem] 已停止");
    }
}

#region 数据模型

/// <summary>
/// 异常事件
/// </summary>
public class AnomalyEvent
{
    public string Id { get; init; } = string.Empty;
    public AnomalyType Type { get; init; }
    public AnomalySeverity Severity { get; init; }
    public string Source { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public Dictionary<string, object> Details { get; init; } = new();
    public DateTime Timestamp { get; init; }
    public bool IsResolved { get; set; }
}

/// <summary>
/// 异常类型
/// </summary>
public enum AnomalyType
{
    ApiFailure,              // API故障
    TradingError,            // 交易错误
    SystemFailure,           // 系统故障
    PerformanceDegradation,  // 性能下降
    DataCorruption,          // 数据损坏
    ResourceExhaustion       // 资源耗尽
}

/// <summary>
/// 异常严重程度
/// </summary>
public enum AnomalySeverity
{
    Low,       // 低
    Medium,    // 中
    High,      // 高
    Critical   // 严重
}

/// <summary>
/// 异常模式
/// </summary>
public class AnomalyPattern
{
    public AnomalyType Type { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime FirstOccurrence { get; set; }
    public DateTime LastOccurrence { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// 异常阈值配置
/// </summary>
public class AnomalyThresholds
{
    public double AnomalyRateThreshold { get; init; } = 5.0;      // 每分钟5个异常
    public int FrequentAnomalyThreshold { get; init; } = 10;      // 10次视为频繁
    public TimeSpan CorrelationWindow { get; init; } = TimeSpan.FromMinutes(5);

    public static AnomalyThresholds Default => new AnomalyThresholds();
}

/// <summary>
/// 异常统计
/// </summary>
public class AnomalyStatistics
{
    public long TotalAnomalies { get; init; }
    public long CriticalAnomalies { get; init; }
    public long ResolvedAnomalies { get; init; }
    public int RecentHourCount { get; init; }
    public int PatternCount { get; init; }
    public double AnomalyRate { get; init; }
}

/// <summary>
/// 异常检测事件参数
/// </summary>
public class AnomalyDetectedEventArgs : EventArgs
{
    public AnomalyEvent Anomaly { get; }

    public AnomalyDetectedEventArgs(AnomalyEvent anomaly)
    {
        Anomaly = anomaly;
    }
}

#endregion
