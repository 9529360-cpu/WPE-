using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services;

/// <summary>
/// 速率限制器 - 使用令牌桶算法防止超过Binance API限制
/// </summary>
/// <remarks>
/// <para>Binance API限制:</para>
/// <list type="bullet">
/// <item>REST API: 1200 weight/分钟 (基于请求权重)</item>
/// <item>订单API: 300 orders/10秒</item>
/// </list>
/// <para>使用令牌桶算法实现平滑的速率控制,避免突发请求导致封禁</para>
/// </remarks>
public class RateLimiter
{
    /// <summary>
    /// Binance API速率限制常量
    /// </summary>
    private static class RateLimitConstants
    {
        /// <summary>
        /// REST API令牌桶容量 (1200 weight/分钟)
        /// </summary>
        public const int REST_API_CAPACITY = 1200;

        /// <summary>
        /// REST API令牌补充速率 (20 weight/秒 = 1200/分钟)
        /// </summary>
        public const int REST_API_REFILL_RATE = 20;

        /// <summary>
        /// 订单API令牌桶容量 (300 orders/10秒)
        /// </summary>
        public const int ORDER_API_CAPACITY = 300;

        /// <summary>
        /// 订单API令牌补充速率 (30 orders/秒 = 300/10秒)
        /// </summary>
        public const int ORDER_API_REFILL_RATE = 30;

        /// <summary>
        /// 分批等待最大时长(秒)
        /// </summary>
        public const double MAX_BATCH_WAIT_SECONDS = 1.0;

        /// <summary>
        /// REST API类别名称
        /// </summary>
        public const string REST_API_CATEGORY = "rest_api";

        /// <summary>
        /// 订单API类别名称
        /// </summary>
        public const string ORDER_API_CATEGORY = "order_api";
    }

    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();

    /// <summary>
    /// 获取或创建指定类型的令牌桶
    /// </summary>
    /// <param name="category">类别名称</param>
    /// <param name="capacity">令牌桶容量</param>
    /// <param name="refillRate">每秒补充令牌数</param>
    /// <returns>令牌桶实例</returns>
    private TokenBucket GetBucket(string category, int capacity, int refillRate)
    {
        return _buckets.GetOrAdd(category, _ => new TokenBucket(capacity, refillRate));
    }

    /// <summary>
    /// 等待获取REST API权重令牌
    /// </summary>
    /// <param name="weight">请求权重 (默认1)</param>
    /// <param name="ct">取消令牌</param>
    /// <remarks>
    /// Binance限制: 1200 weight/分钟
    /// 不同API endpoint有不同权重,详见官方文档
    /// </remarks>
    public async Task WaitForRestApiAsync(int weight = 1, CancellationToken ct = default)
    {
        var bucket = GetBucket(
            RateLimitConstants.REST_API_CATEGORY,
            RateLimitConstants.REST_API_CAPACITY,
            RateLimitConstants.REST_API_REFILL_RATE
        );
        await bucket.WaitForTokensAsync(weight, ct);
    }

    /// <summary>
    /// 等待获取订单API令牌
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <remarks>
    /// Binance限制: 300 orders/10秒
    /// 包含下单、撤单、批量下单等订单相关操作
    /// </remarks>
    public async Task WaitForOrderApiAsync(CancellationToken ct = default)
    {
        var bucket = GetBucket(
            RateLimitConstants.ORDER_API_CATEGORY,
            RateLimitConstants.ORDER_API_CAPACITY,
            RateLimitConstants.ORDER_API_REFILL_RATE
        );
        await bucket.WaitForTokensAsync(1, ct);
    }

    /// <summary>
    /// 检查是否可以立即获取令牌(不等待)
    /// </summary>
    /// <param name="category">类别名称</param>
    /// <param name="tokens">令牌数量</param>
    /// <returns>true表示成功获取并消费令牌,false表示令牌不足</returns>
    public bool TryConsume(string category, int tokens = 1)
    {
        return _buckets.TryGetValue(category, out var bucket) && bucket.TryConsume(tokens);
    }

    /// <summary>
    /// 获取当前可用令牌数
    /// </summary>
    /// <param name="category">类别名称</param>
    /// <returns>可用令牌数,如果类别不存在返回0</returns>
    public int GetAvailableTokens(string category)
    {
        return _buckets.TryGetValue(category, out var bucket) ? bucket.AvailableTokens : 0;
    }

    /// <summary>
    /// 重置所有限制器(用于测试或紧急情况)
    /// </summary>
    /// <remarks>
    /// 清空所有令牌桶,下次请求将重新创建
    /// 谨慎使用,可能导致短时间内大量请求
    /// </remarks>
    public void Reset()
    {
        _buckets.Clear();
    }
}

/// <summary>
/// 令牌桶实现 - 漏桶算法的变种,允许突发流量
/// </summary>
/// <remarks>
/// 令牌以固定速率补充,请求消费令牌,令牌不足时等待
/// </remarks>
internal class TokenBucket
{
    private readonly int _capacity;
    private readonly int _refillRate;
    private int _tokens;
    private DateTime _lastRefill;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// 创建令牌桶
    /// </summary>
    /// <param name="capacity">令牌桶容量</param>
    /// <param name="refillRate">每秒补充的令牌数</param>
    public TokenBucket(int capacity, int refillRate)
    {
        _capacity = capacity;
        _refillRate = refillRate;
        _tokens = capacity;
        _lastRefill = DateTime.UtcNow;
    }

    /// <summary>
    /// 获取当前可用令牌数
    /// </summary>
    public int AvailableTokens
    {
        get
        {
            Refill();
            return _tokens;
        }
    }

    /// <summary>
    /// 等待获取指定数量的令牌
    /// </summary>
    /// <param name="count">需要的令牌数</param>
    /// <param name="ct">取消令牌</param>
    /// <exception cref="ArgumentException">请求令牌数超过容量</exception>
    /// <exception cref="OperationCanceledException">操作被取消</exception>
    public async Task WaitForTokensAsync(int count, CancellationToken ct)
    {
        if (count > _capacity)
        {
            throw new ArgumentException($"请求令牌数 {count} 超过容量 {_capacity}");
        }

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            await _semaphore.WaitAsync(ct);
            try
            {
                Refill();

                if (_tokens >= count)
                {
                    _tokens -= count;
                    return;
                }

                // 计算需要等待的时间
                int tokensNeeded = count - _tokens;
                var waitTime = TimeSpan.FromSeconds((double)tokensNeeded / _refillRate);

                // 如果等待时间过长,分批等待避免长时间持锁
                if (waitTime.TotalSeconds > 1.0)
                {
                    waitTime = TimeSpan.FromSeconds(1.0);
                }

                await Task.Delay(waitTime, ct);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

    /// <summary>
    /// 尝试立即消费令牌(不等待)
    /// </summary>
    /// <param name="count">需要的令牌数</param>
    /// <returns>true表示成功消费,false表示令牌不足</returns>
    public bool TryConsume(int count)
    {
        _semaphore.Wait();
        try
        {
            Refill();

            if (_tokens >= count)
            {
                _tokens -= count;
                return true;
            }

            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 补充令牌 - 基于时间流逝按固定速率添加令牌
    /// </summary>
    private void Refill()
    {
        var now = DateTime.UtcNow;
        double elapsed = (now - _lastRefill).TotalSeconds;

        if (elapsed >= 1.0 / _refillRate)
        {
            int tokensToAdd = (int)(elapsed * _refillRate);
            _tokens = Math.Min(_capacity, _tokens + tokensToAdd);
            _lastRefill = now;
        }
    }
}
