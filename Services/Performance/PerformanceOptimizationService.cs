using System;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.Performance;

/// <summary>
/// 性能优化服务集成器
/// </summary>
/// <remarks>
/// 统一管理所有性能优化组件:
/// 1. 实时数据处理器
/// 2. 智能缓存管理器
/// 3. 性能监控器
/// 
/// 提供统一的性能优化接口
/// </remarks>
public class PerformanceOptimizationService : IDisposable
{
    private readonly RealtimeDataProcessor _dataProcessor;
    private readonly SmartCacheManager _cacheManager;
    private readonly PerformanceMonitor _performanceMonitor;
    
    private bool _isInitialized;

    public PerformanceOptimizationService()
    {
        // 初始化数据处理器
        _dataProcessor = new RealtimeDataProcessor(
            batchSize: 100,
            maxQueueSize: 10000,
            parallelism: 4);

        // 初始化缓存管理器
        _cacheManager = new SmartCacheManager(
            defaultExpiration: TimeSpan.FromMinutes(15),
            maxCacheSize: 10000);

        // 初始化性能监控器
        _performanceMonitor = new PerformanceMonitor();

        _isInitialized = true;

        LogService.Info("🚀 [PerformanceOptimizationService] 性能优化服务已启动");
    }

    #region 数据处理

    /// <summary>
    /// 提交数据进行处理
    /// </summary>
    public async ValueTask<bool> SubmitDataAsync(DataItem item, CancellationToken ct = default)
    {
        using (_performanceMonitor.RecordOperation("SubmitData"))
        {
            return await _dataProcessor.SubmitAsync(item, ct);
        }
    }

    /// <summary>
    /// 批量提交数据
    /// </summary>
    public async ValueTask<int> SubmitDataBatchAsync(System.Collections.Generic.IEnumerable<DataItem> items, CancellationToken ct = default)
    {
        using (_performanceMonitor.RecordOperation("SubmitDataBatch"))
        {
            return await _dataProcessor.SubmitBatchAsync(items, ct);
        }
    }

    /// <summary>
    /// 获取数据处理统计
    /// </summary>
    public PerformanceStats GetDataProcessorStats()
    {
        return _dataProcessor.GetStats();
    }

    #endregion

    #region 缓存管理

    /// <summary>
    /// 从缓存获取数据
    /// </summary>
    public T GetCached<T>(string key) where T : class
    {
        using (_performanceMonitor.RecordOperation("CacheGet"))
        {
            return _cacheManager.Get<T>(key);
        }
    }

    /// <summary>
    /// 获取或创建缓存数据
    /// </summary>
    public async Task<T> GetOrCreateCachedAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiration = null) where T : class
    {
        using (_performanceMonitor.RecordOperation("CacheGetOrCreate"))
        {
            return await _cacheManager.GetOrCreateAsync(key, factory, expiration);
        }
    }

    /// <summary>
    /// 设置缓存数据
    /// </summary>
    public void SetCached<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        using (_performanceMonitor.RecordOperation("CacheSet"))
        {
            _cacheManager.Set(key, value, expiration);
        }
    }

    /// <summary>
    /// 删除缓存数据
    /// </summary>
    public void RemoveCached(string key)
    {
        _cacheManager.Remove(key);
    }

    /// <summary>
    /// 预热缓存
    /// </summary>
    public async Task WarmupCacheAsync<T>(
        System.Collections.Generic.IEnumerable<string> keys,
        Func<string, Task<T>> factory,
        CancellationToken ct = default) where T : class
    {
        using (_performanceMonitor.RecordOperation("CacheWarmup"))
        {
            await _cacheManager.WarmupAsync(keys, factory, ct);
        }
    }

    /// <summary>
    /// 获取缓存统计
    /// </summary>
    public CacheStatistics GetCacheStatistics()
    {
        return _cacheManager.GetStatistics();
    }

    /// <summary>
    /// 获取热点键
    /// </summary>
    public System.Collections.Generic.List<HotKeyInfo> GetHotKeys(int topN = 20)
    {
        return _cacheManager.GetHotKeys(topN);
    }

    #endregion

    #region 性能监控

    /// <summary>
    /// 记录操作
    /// </summary>
    public IDisposable RecordOperation(string operationName)
    {
        return _performanceMonitor.RecordOperation(operationName);
    }

    /// <summary>
    /// 记录自定义指标
    /// </summary>
    public void RecordMetric(string name, double value)
    {
        _performanceMonitor.RecordMetric(name, value);
    }

    /// <summary>
    /// 获取系统指标
    /// </summary>
    public SystemMetrics GetSystemMetrics()
    {
        return _performanceMonitor.GetSystemMetrics();
    }

    /// <summary>
    /// 获取请求统计
    /// </summary>
    public RequestStatistics GetRequestStatistics()
    {
        return _performanceMonitor.GetRequestStatistics();
    }

    /// <summary>
    /// 获取完整性能报告
    /// </summary>
    public PerformanceReport GetPerformanceReport()
    {
        return _performanceMonitor.GetReport();
    }

    /// <summary>
    /// 打印性能报告
    /// </summary>
    public void PrintPerformanceReport()
    {
        _performanceMonitor.PrintReport();
    }

    #endregion

    #region 健康检查

    /// <summary>
    /// 执行健康检查
    /// </summary>
    public HealthCheckResult PerformHealthCheck()
    {
        SystemMetrics systemMetrics = _performanceMonitor.GetSystemMetrics();
        CacheStatistics cacheStats = _cacheManager.GetStatistics();
        PerformanceStats dataStats = _dataProcessor.GetStats();

        bool isHealthy = systemMetrics.CpuUsagePercent < 80 &&
                        systemMetrics.MemoryUsageMB < 2000 &&
                        cacheStats.HitRate > 0.7 &&
                        dataStats.DropRate < 0.01;

        return new HealthCheckResult
        {
            IsHealthy = isHealthy,
            CpuUsage = systemMetrics.CpuUsagePercent,
            MemoryUsageMB = systemMetrics.MemoryUsageMB,
            CacheHitRate = cacheStats.HitRate,
            DataDropRate = dataStats.DropRate,
            Timestamp = DateTime.UtcNow
        };
    }

    #endregion

    #region 优雅关闭

    /// <summary>
    /// 优雅关闭所有组件
    /// </summary>
    public async Task ShutdownAsync(TimeSpan timeout = default)
    {
        if (!_isInitialized)
        {
            return;
        }

        LogService.Info("🛑 [PerformanceOptimizationService] 开始优雅关闭...");

        try
        {
            // 关闭数据处理器
            await _dataProcessor.ShutdownAsync(timeout);

            // 打印最终统计
            PrintPerformanceReport();

            _isInitialized = false;

            LogService.Info("✅ [PerformanceOptimizationService] 已完成优雅关闭");
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[PerformanceOptimizationService] 关闭失败");
        }
    }

    #endregion

    public void Dispose()
    {
        if (_isInitialized)
        {
            ShutdownAsync().GetAwaiter().GetResult();
        }

        _dataProcessor?.Dispose();
        _cacheManager?.Dispose();
        _performanceMonitor?.Dispose();

        LogService.Info("[PerformanceOptimizationService] 已释放资源");
    }
}

#region 数据模型

/// <summary>
/// 健康检查结果
/// </summary>
public class HealthCheckResult
{
    public bool IsHealthy { get; init; }
    public double CpuUsage { get; init; }
    public long MemoryUsageMB { get; init; }
    public double CacheHitRate { get; init; }
    public double DataDropRate { get; init; }
    public DateTime Timestamp { get; init; }

    public override string ToString()
    {
        string status = IsHealthy ? "✅ 健康" : "⚠️ 异常";
        return $"{status} - CPU: {CpuUsage:F2}%, Memory: {MemoryUsageMB}MB, " +
               $"CacheHit: {CacheHitRate:P2}, DropRate: {DataDropRate:P2}";
    }
}

#endregion
