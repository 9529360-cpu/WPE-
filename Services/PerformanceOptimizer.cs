using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace 币安量化机器人.Services
{
    /// <summary>
    /// 性能监控和优化服务
    /// 提供内存管理、缓存、和性能分析功能
    /// </summary>
    public class PerformanceOptimizer
    {
        private readonly ILogger _logger = LoggerFactory.CreateLogger<PerformanceOptimizer>();
        private readonly Dictionary<string, Stopwatch> _timers = new();
        private readonly Dictionary<string, List<long>> _metrics = new();
        
        // 缓存管理
        private readonly Dictionary<string, CacheEntry> _cache = new();
        private readonly TimeSpan _defaultCacheExpiration = TimeSpan.FromMinutes(5);

        #region 性能计时

        /// <summary>
        /// 开始性能计时
        /// </summary>
        public void StartTiming(string operationName)
        {
            if (!_timers.ContainsKey(operationName))
            {
                _timers[operationName] = new Stopwatch();
            }
            _timers[operationName].Restart();
        }

        /// <summary>
        /// 停止性能计时并记录
        /// </summary>
        public long StopTiming(string operationName)
        {
            if (_timers.TryGetValue(operationName, out var timer))
            {
                timer.Stop();
                var elapsed = timer.ElapsedMilliseconds;
                
                RecordMetric(operationName, elapsed);
                
                if (elapsed > 1000) // 超过1秒记录警告
                {
                    _logger.Warning($"性能警告: {operationName} 耗时 {elapsed}ms");
                }
                
                return elapsed;
            }
            return 0;
        }

        /// <summary>
        /// 测量操作执行时间
        /// </summary>
        public T MeasurePerformance<T>(string operationName, Func<T> operation)
        {
            StartTiming(operationName);
            try
            {
                return operation();
            }
            finally
            {
                var elapsed = StopTiming(operationName);
                _logger.Debug($"{operationName} 耗时: {elapsed}ms");
            }
        }

        /// <summary>
        /// 测量异步操作执行时间
        /// </summary>
        public async System.Threading.Tasks.Task<T> MeasurePerformanceAsync<T>(string operationName, System.Threading.Tasks.Task<T> operation)
        {
            StartTiming(operationName);
            try
            {
                return await operation;
            }
            finally
            {
                var elapsed = StopTiming(operationName);
                _logger.Debug($"{operationName} 耗时: {elapsed}ms");
            }
        }

        #endregion

        #region 性能指标

        /// <summary>
        /// 记录性能指标
        /// </summary>
        private void RecordMetric(string metricName, long value)
        {
            if (!_metrics.ContainsKey(metricName))
            {
                _metrics[metricName] = new List<long>();
            }
            _metrics[metricName].Add(value);
            
            // 保留最近100个数据点
            if (_metrics[metricName].Count > 100)
            {
                _metrics[metricName].RemoveAt(0);
            }
        }

        /// <summary>
        /// 获取性能统计
        /// </summary>
        public PerformanceStats GetStats(string metricName)
        {
            if (!_metrics.TryGetValue(metricName, out var values) || values.Count == 0)
            {
                return new PerformanceStats();
            }

            return new PerformanceStats
            {
                Count = values.Count,
                Average = values.Average(),
                Min = values.Min(),
                Max = values.Max(),
                P50 = GetPercentile(values, 0.5),
                P95 = GetPercentile(values, 0.95),
                P99 = GetPercentile(values, 0.99)
            };
        }

        private static double GetPercentile(List<long> values, double percentile)
        {
            var sorted = values.OrderBy(v => v).ToList();
            var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
            return sorted[Math.Max(0, Math.Min(index, sorted.Count - 1))];
        }

        #endregion

        #region 缓存管理

        /// <summary>
        /// 添加到缓存
        /// </summary>
        public void AddToCache<T>(string key, T value, TimeSpan? expiration = null)
        {
            var entry = new CacheEntry
            {
                Value = value,
                ExpiresAt = DateTime.UtcNow + (expiration ?? _defaultCacheExpiration)
            };
            _cache[key] = entry;
            _logger.Debug($"缓存已添加: {key}");
        }

        /// <summary>
        /// 从缓存获取
        /// </summary>
        public T? GetFromCache<T>(string key)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.ExpiresAt > DateTime.UtcNow)
                {
                    _logger.Debug($"缓存命中: {key}");
                    return (T?)entry.Value;
                }
                else
                {
                    // 过期，移除
                    _cache.Remove(key);
                    _logger.Debug($"缓存过期: {key}");
                }
            }
            return default;
        }

        /// <summary>
        /// 清除缓存
        /// </summary>
        public void ClearCache()
        {
            var count = _cache.Count;
            _cache.Clear();
            _logger.Info($"已清除 {count} 个缓存项");
        }

        /// <summary>
        /// 清除过期缓存
        /// </summary>
        public void ClearExpiredCache()
        {
            var now = DateTime.UtcNow;
            var expiredKeys = _cache.Where(kv => kv.Value.ExpiresAt <= now)
                                    .Select(kv => kv.Key)
                                    .ToList();
            
            foreach (var key in expiredKeys)
            {
                _cache.Remove(key);
            }
            
            if (expiredKeys.Count > 0)
            {
                _logger.Info($"已清除 {expiredKeys.Count} 个过期缓存项");
            }
        }

        #endregion

        #region 内存优化

        /// <summary>
        /// 强制垃圾回收（谨慎使用）
        /// </summary>
        public void ForceGarbageCollection()
        {
            var before = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            var after = GC.GetTotalMemory(false) / 1024.0 / 1024.0;
            var freed = before - after;
            
            _logger.Info($"垃圾回收完成: 释放 {freed:F2} MB (从 {before:F2} MB 到 {after:F2} MB)");
        }

        /// <summary>
        /// 获取当前内存使用情况
        /// </summary>
        public MemoryInfo GetMemoryInfo()
        {
            var process = Process.GetCurrentProcess();
            return new MemoryInfo
            {
                WorkingSet = process.WorkingSet64 / 1024.0 / 1024.0,
                PrivateMemory = process.PrivateMemorySize64 / 1024.0 / 1024.0,
                ManagedMemory = GC.GetTotalMemory(false) / 1024.0 / 1024.0,
                GCCollections = new[]
                {
                    GC.CollectionCount(0),
                    GC.CollectionCount(1),
                    GC.CollectionCount(2)
                }
            };
        }

        #endregion

        private class CacheEntry
        {
            public object? Value { get; set; }
            public DateTime ExpiresAt { get; set; }
        }
    }

    public class PerformanceStats
    {
        public int Count { get; set; }
        public double Average { get; set; }
        public long Min { get; set; }
        public long Max { get; set; }
        public double P50 { get; set; } // 中位数
        public double P95 { get; set; }
        public double P99 { get; set; }

        public override string ToString()
        {
            return $"Count={Count}, Avg={Average:F2}ms, Min={Min}ms, Max={Max}ms, P50={P50:F2}ms, P95={P95:F2}ms, P99={P99:F2}ms";
        }
    }

    public class MemoryInfo
    {
        public double WorkingSet { get; set; } // MB
        public double PrivateMemory { get; set; } // MB
        public double ManagedMemory { get; set; } // MB
        public int[] GCCollections { get; set; } = Array.Empty<int>();

        public override string ToString()
        {
            return $"WorkingSet={WorkingSet:F2}MB, Private={PrivateMemory:F2}MB, Managed={ManagedMemory:F2}MB, GC=[{string.Join(",", GCCollections)}]";
        }
    }
}
