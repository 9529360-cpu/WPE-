using System;
using System.Threading;

namespace 币安量化机器人.Services;

/// <summary>
/// API熔断器 - 实现熔断器模式防止连续失败导致雪崩效应
/// </summary>
/// <remarks>
/// <para>状态机转换:</para>
/// <list type="bullet">
/// <item><b>Closed</b> → <b>Open</b>: 连续失败次数达到阈值</item>
/// <item><b>Open</b> → <b>HalfOpen</b>: 冷却时间到达,尝试恢复</item>
/// <item><b>HalfOpen</b> → <b>Closed</b>: 测试请求成功,完全恢复</item>
/// <item><b>HalfOpen</b> → <b>Open</b>: 测试请求失败,重新熔断</item>
/// </list>
/// <para>使用场景: 当API服务出现故障时,避免大量请求持续失败,给服务恢复时间</para>
/// </remarks>
/// <example>
/// <code>
/// var breaker = new ApiCircuitBreaker(failureThreshold: 5, cooldown: TimeSpan.FromMinutes(1));
/// 
/// if (breaker.AllowRequest())
/// {
///     try
///     {
///         await CallApiAsync();
///         breaker.RecordSuccess();
///     }
///     catch
///     {
///         breaker.RecordFailure();
///     }
/// }
/// </code>
/// </example>
public class ApiCircuitBreaker
{
    /// <summary>
    /// 熔断器配置常量
    /// </summary>
    private static class CircuitBreakerConstants
    {
        /// <summary>
        /// 默认失败阈值
        /// </summary>
        public const int DEFAULT_FAILURE_THRESHOLD = 5;

        /// <summary>
        /// 默认超时时间(秒)
        /// </summary>
        public const int DEFAULT_TIMEOUT_SECONDS = 30;

        /// <summary>
        /// 默认冷却时间(分钟)
        /// </summary>
        public const int DEFAULT_COOLDOWN_MINUTES = 1;
    }

    private readonly int _failureThreshold;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _cooldown;

    private CircuitBreakerState _state;
    private int _consecutiveFailures;
    private DateTime _lastFailureTime;
    private DateTime _openedAt;
    private readonly object _lock = new();

    /// <summary>
    /// 创建熔断器实例
    /// </summary>
    /// <param name="failureThreshold">触发熔断的连续失败次数阈值 (默认5次)</param>
    /// <param name="timeout">请求超时时间 (默认30秒,暂未使用)</param>
    /// <param name="cooldown">熔断后的冷却时间 (默认1分钟)</param>
    public ApiCircuitBreaker(int failureThreshold = CircuitBreakerConstants.DEFAULT_FAILURE_THRESHOLD,
                             TimeSpan? timeout = null,
                             TimeSpan? cooldown = null)
    {
        _failureThreshold = failureThreshold;
        _timeout = timeout ?? TimeSpan.FromSeconds(CircuitBreakerConstants.DEFAULT_TIMEOUT_SECONDS);
        _cooldown = cooldown ?? TimeSpan.FromMinutes(CircuitBreakerConstants.DEFAULT_COOLDOWN_MINUTES);
        _state = CircuitBreakerState.Closed;
        _consecutiveFailures = 0;
    }

    /// <summary>
    /// 获取当前熔断器状态
    /// </summary>
    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    /// <summary>
    /// 获取当前连续失败次数
    /// </summary>
    public int ConsecutiveFailures => _consecutiveFailures;

    /// <summary>
    /// 检查是否允许请求通过
    /// </summary>
    /// <returns>true表示允许请求,false表示请求被熔断</returns>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Closed状态: 始终返回true</item>
    /// <item>Open状态: 检查冷却时间,到达则转为HalfOpen并返回true,否则返回false</item>
    /// <item>HalfOpen状态: 返回true,允许单个测试请求</item>
    /// </list>
    /// </remarks>
    public bool AllowRequest()
    {
        lock (_lock)
        {
            switch (_state)
            {
                case CircuitBreakerState.Closed:
                    return true;

                case CircuitBreakerState.Open:
                    // 检查是否到达冷却时间
                    if (DateTime.UtcNow - _openedAt >= _cooldown)
                    {
                        _state = CircuitBreakerState.HalfOpen;
                        StartupDiagnostics.Log($"CircuitBreaker: Open -> HalfOpen (尝试恢复)");
                        return true;
                    }
                    return false;

                case CircuitBreakerState.HalfOpen:
                    // 半开状态下只允许单个请求测试
                    return true;

                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// 记录成功请求
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>重置连续失败计数器</item>
    /// <item>如果当前是HalfOpen状态,转为Closed状态(完全恢复)</item>
    /// </list>
    /// </remarks>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            _consecutiveFailures = 0;
            _lastFailureTime = default;

            if (_state == CircuitBreakerState.HalfOpen)
            {
                _state = CircuitBreakerState.Closed;
                StartupDiagnostics.Log($"CircuitBreaker: HalfOpen -> Closed (已恢复)");
            }
        }
    }

    /// <summary>
    /// 记录失败请求
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>增加连续失败计数</item>
    /// <item>如果在HalfOpen状态下失败,立即转为Open状态</item>
    /// <item>如果在Closed状态下失败次数达到阈值,触发熔断</item>
    /// </list>
    /// </remarks>
    public void RecordFailure()
    {
        lock (_lock)
        {
            _consecutiveFailures++;
            _lastFailureTime = DateTime.UtcNow;

            if (_state == CircuitBreakerState.HalfOpen)
            {
                // 半开状态下失败,立即重新打开
                Trip();
                return;
            }

            if (_state == CircuitBreakerState.Closed && _consecutiveFailures >= _failureThreshold)
            {
                Trip();
            }
        }
    }

    /// <summary>
    /// 手动触发熔断
    /// </summary>
    /// <remarks>
    /// 强制将熔断器状态设置为Open,通常用于:
    /// <list type="bullet">
    /// <item>检测到外部服务故障</item>
    /// <item>主动降级</item>
    /// <item>紧急维护</item>
    /// </list>
    /// </remarks>
    public void Trip()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Open;
            _openedAt = DateTime.UtcNow;
            StartupDiagnostics.Log($"CircuitBreaker: -> Open (连续失败 {_consecutiveFailures} 次)");
        }
    }

    /// <summary>
    /// 手动重置熔断器
    /// </summary>
    /// <remarks>
    /// 强制将熔断器恢复为正常状态,通常用于:
    /// <list type="bullet">
    /// <item>确认外部服务已恢复</item>
    /// <item>完成维护后重新启用</item>
    /// <item>测试或调试</item>
    /// </list>
    /// </remarks>
    public void Reset()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Closed;
            _consecutiveFailures = 0;
            _lastFailureTime = default;
            StartupDiagnostics.Log($"CircuitBreaker: -> Closed (手动重置)");
        }
    }

    /// <summary>
    /// 获取距离下次尝试的剩余时间
    /// </summary>
    /// <returns>
    /// 如果熔断器不在Open状态,返回null
    /// 如果在Open状态,返回距离可以尝试的剩余时间
    /// </returns>
    public TimeSpan? TimeUntilRetry()
    {
        lock (_lock)
        {
            if (_state != CircuitBreakerState.Open)
            {
                return null;
            }

            var remaining = _cooldown - (DateTime.UtcNow - _openedAt);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
}

/// <summary>
/// 熔断器状态枚举
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>
    /// 关闭状态 - 正常运行,所有请求通过
    /// </summary>
    Closed,

    /// <summary>
    /// 打开状态 - 熔断激活,拒绝所有请求
    /// </summary>
    Open,

    /// <summary>
    /// 半开状态 - 尝试恢复,允许单个测试请求
    /// </summary>
    HalfOpen
}
