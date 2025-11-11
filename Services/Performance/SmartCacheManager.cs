using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace 币安量化机器人.Services.Performance;

/// <summary>
/// 智能缓存管理器
/// </summary>
/// <remarks>
/// 核心功能:
/// 1. 多级缓存 (L1内存)
/// 2. 智能预热
/// 3. 自动过期策略
/// 4. 热点数据识别
/// 5. 缓存命中率优化
/// 
/// 性能目标:
/// - 缓存命中率 > 90%
/// - L1访问延迟 < 1ms
/// - 内存使用 < 1GB
/// </remarks>
public class SmartCacheManager : IDisposable
{
    private readonly IMemoryCache _l1Cache;
    private readonly ConcurrentDictionary<string, CacheEntry> _metadata;
    private readonly ConcurrentDictionary<string, int> _accessFrequency;
    private readonly Timer _cleanupTimer;
    private readonly Timer _statsTimer;

    // 缓存配置
    private readonly TimeSpan _defaultExpiration;
    private readonly int _maxL1Size;

    // 统计信息
    private long _hitCount;
    private long _missCount;
    private long _evictionCount;

    public SmartCacheManager(
        TimeSpan? defaultExpiration = null,
        int maxL1SizeMB = 512)
    {
        _defaultExpiration = defaultExpiration ?? TimeSpan.FromMinutes(15);
        _maxL1Size = maxL1SizeMB * 1024 * 1024;

        _l1Cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = maxL1SizeMB * 1024 * 1024
        });
        _metadata = new ConcurrentDictionary<string, CacheEntry>();
        _accessFrequency = new ConcurrentDictionary<string, int>();

        // 定期清理
        _cleanupTimer = new Timer(CleanupCallback, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        // 定期输出统计
        _statsTimer = new Timer(StatsCallback, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

        LogService.Info("[SmartCacheManager] 智能缓存管理器已启动: MaxSize={MaxSize}MB, DefaultExpiration={Expiration}",
            maxL1SizeMB, _defaultExpiration);
    }

    #region 基础缓存操作

    /// <summary>
    /// 获取缓存值
    /// </summary>
    public T Get<T>(string key) where T : class
    {
        // 尝试从L1获取
        if (_l1Cache.TryGetValue(key, out T value))
        {
            // 记录命中
            Interlocked.Increment(ref _hitCount);
            RecordAccess(key);

            // 更新元数据
            if (_metadata.TryGetValue(key, out CacheEntry entry))
            {
                entry.LastAccess = DateTime.UtcNow;
                entry.AccessCount++;
            }

            return value;
        }

        // 记录未命中
        Interlocked.Increment(ref _missCount);
        return null!;
    }

    /// <summary>
    /// 异步获取或创建缓存值
    /// </summary>
    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiration = null) where T : class
    {
        // 尝试从缓存获取
        T value = Get<T>(key);
        if (value != null)
        {
            return value;
        }

        // 创建新值
        value = await factory();
        if (value != null)
        {
            Set(key, value, expiration);
        }

        return value;
    }

    /// <summary>
    /// 设置缓存值
    /// </summary>
    public void Set<T>(string key, T value, TimeSpan? expiration = null) where T : class
    {
        TimeSpan exp = expiration ?? _defaultExpiration;

        // 创建缓存选项
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = exp,
            Size = 1
        };

        options.RegisterPostEvictionCallback((k, v, reason, state) =>
        {
            Interlocked.Increment(ref _evictionCount);
            LogService.Debug("[SmartCacheManager] 缓存项被移除: Key={Key}, Reason={Reason}", k, reason);
        });

        // 添加到L1
        _l1Cache.Set(key, value, options);

        // 更新元数据
        var entry = new CacheEntry
        {
            Key = key,
            Type = typeof(T).Name,
            Size = EstimateSize(value),
            CreatedAt = DateTime.UtcNow,
            LastAccess = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(exp),
            AccessCount = 0
        };

        _metadata[key] = entry;
    }

    /// <summary>
    /// 删除缓存值
    /// </summary>
    public void Remove(string key)
    {
        _l1Cache.Remove(key);
        _metadata.TryRemove(key, out _);
        _accessFrequency.TryRemove(key, out _);
    }

    /// <summary>
    /// 批量删除
    /// </summary>
    public void RemoveByPattern(string pattern)
    {
        var keysToRemove = _metadata.Keys
            .Where(k => k.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (string key in keysToRemove)
        {
            Remove(key);
        }

        LogService.Info("[SmartCacheManager] 按模式删除: Pattern={Pattern}, Count={Count}",
            pattern, keysToRemove.Count);
    }

    /// <summary>
    /// 清空所有缓存
    /// </summary>
    public void Clear()
    {
        if (_l1Cache is MemoryCache memoryCache)
        {
            memoryCache.Compact(1.0);
        }
        _metadata.Clear();
        _accessFrequency.Clear();

        LogService.Info("[SmartCacheManager] 已清空所有缓存");
    }

    #endregion

    #region 智能预热

    /// <summary>
    /// 预热热点数据
    /// </summary>
    public async Task WarmupAsync<T>(
        IEnumerable<string> keys,
        Func<string, Task<T>> factory,
        CancellationToken ct = default) where T : class
    {
        LogService.Info("[SmartCacheManager] 开始缓存预热: Keys={Count}", keys.Count());

        var stopwatch = Stopwatch.StartNew();
        int loaded = 0;

        await Parallel.ForEachAsync(keys, ct, async (key, token) =>
        {
            try
            {
                T value = await factory(key);
                if (value != null)
                {
                    Set(key, value);
                    Interlocked.Increment(ref loaded);
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[SmartCacheManager] 预热失败: Key={Key}", key);
            }
        });

        stopwatch.Stop();
        LogService.Info("[SmartCacheManager] 预热完成: Loaded={Loaded}, Duration={Duration}ms",
            loaded, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// 自动预热（基于访问频率）
    /// </summary>
    public async Task AutoWarmupAsync<T>(
        Func<string, Task<T>> factory,
        int topN = 100,
        CancellationToken ct = default) where T : class
    {
        // 获取访问频率最高的键
        var hotKeys = _accessFrequency
            .OrderByDescending(kvp => kvp.Value)
            .Take(topN)
            .Select(kvp => kvp.Key)
            .Where(k => !_l1Cache.TryGetValue(k, out _)) // 过滤已缓存的
            .ToList();

        if (hotKeys.Count > 0)
        {
            await WarmupAsync(hotKeys, factory, ct);
        }
    }

    #endregion

    #region 性能分析

    /// <summary>
    /// 记录访问
    /// </summary>
    private void RecordAccess(string key)
    {
        _accessFrequency.AddOrUpdate(key, 1, (_, count) => count + 1);
    }

    /// <summary>
    /// 获取热点数据
    /// </summary>
    public List<HotKeyInfo> GetHotKeys(int topN = 20)
    {
        return _accessFrequency
            .OrderByDescending(kvp => kvp.Value)
            .Take(topN)
            .Select(kvp => new HotKeyInfo
            {
                Key = kvp.Key,
                AccessCount = kvp.Value,
                IsInCache = _l1Cache.TryGetValue(kvp.Key, out _)
            })
            .ToList();
    }

    /// <summary>
    /// 获取缓存统计
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        long hits = Interlocked.Read(ref _hitCount);
        long misses = Interlocked.Read(ref _missCount);
        long total = hits + misses;

        long totalSize = _metadata.Values.Sum(e => e.Size);
        int entryCount = _metadata.Count;

        return new CacheStatistics
        {
            HitCount = hits,
            MissCount = misses,
            HitRate = total > 0 ? (double)hits / total : 0,
            EntryCount = entryCount,
            TotalSizeBytes = totalSize,
            EvictionCount = Interlocked.Read(ref _evictionCount),
            L1CacheCount = _l1Cache.Count()
        };
    }

    #endregion

    #region 清理和维护

    /// <summary>
    /// 定期清理回调
    /// </summary>
    private void CleanupCallback(object state)
    {
        try
        {
            // 清理过期的元数据
            DateTime now = DateTime.UtcNow;
            var expiredKeys = _metadata
                .Where(kvp => kvp.Value.ExpiresAt < now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (string key in expiredKeys)
            {
                _metadata.TryRemove(key, out _);
            }

            // 清理低频访问数据（访问频率 < 2）
            var lowFreqKeys = _accessFrequency
                .Where(kvp => kvp.Value < 2)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (string key in lowFreqKeys)
            {
                _accessFrequency.TryRemove(key, out _);
            }

            if (expiredKeys.Count > 0 || lowFreqKeys.Count > 0)
            {
                LogService.Debug("[SmartCacheManager] 清理完成: Expired={Expired}, LowFreq={LowFreq}",
                    expiredKeys.Count, lowFreqKeys.Count);
            }

            // 检查内存使用
            long totalSize = _metadata.Values.Sum(e => e.Size);
            if (totalSize > _maxL1Size * 0.9) // 超过90%触发清理
            {
                TrimCache();
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[SmartCacheManager] 清理失败");
        }
    }

    /// <summary>
    /// 修剪缓存
    /// </summary>
    private void TrimCache()
    {
        LogService.Warning("[SmartCacheManager] 缓存接近上限，开始修剪");

        // 按最后访问时间排序，移除最旧的20%
        var entriesToRemove = _metadata.Values
            .OrderBy(e => e.LastAccess)
            .Take(_metadata.Count / 5)
            .Select(e => e.Key)
            .ToList();

        foreach (string key in entriesToRemove)
        {
            Remove(key);
        }

        LogService.Info("[SmartCacheManager] 修剪完成: Removed={Count}", entriesToRemove.Count);
    }

    /// <summary>
    /// 定期统计回调
    /// </summary>
    private void StatsCallback(object state)
    {
        try
        {
            CacheStatistics stats = GetStatistics();
            LogService.Info("[SmartCacheManager] 统计: HitRate={HitRate:P2}, Entries={Entries}, Size={Size:F2}MB",
                stats.HitRate, stats.EntryCount, stats.TotalSizeBytes / (1024.0 * 1024.0));
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[SmartCacheManager] 统计失败");
        }
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 估算对象大小
    /// </summary>
    private long EstimateSize(object obj)
    {
        // 简化实现：根据类型估算
        return obj switch
        {
            string str => str.Length * 2,
            byte[] bytes => bytes.Length,
            _ => 1024 // 默认1KB
        };
    }

    #endregion

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _statsTimer?.Dispose();
        _l1Cache?.Dispose();

        LogService.Info("[SmartCacheManager] 已释放资源");
    }
}

#region 数据模型

/// <summary>
/// 缓存条目元数据
/// </summary>
public class CacheEntry
{
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccess { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int AccessCount { get; set; }
}

/// <summary>
/// 热点键信息
/// </summary>
public class HotKeyInfo
{
    public string Key { get; set; } = string.Empty;
    public int AccessCount { get; set; }
    public bool IsInCache { get; set; }
}

/// <summary>
/// 缓存统计
/// </summary>
public class CacheStatistics
{
    public long HitCount { get; init; }
    public long MissCount { get; init; }
    public double HitRate { get; init; }
    public int EntryCount { get; init; }
    public long TotalSizeBytes { get; init; }
    public long EvictionCount { get; init; }
    public long L1CacheCount { get; init; }

    public override string ToString()
    {
        return $"HitRate: {HitRate:P2}, Entries: {EntryCount}, Size: {TotalSizeBytes / (1024.0 * 1024.0):F2}MB, " +
               $"Hits: {HitCount}, Misses: {MissCount}, Evictions: {EvictionCount}";
    }
}

#endregion
