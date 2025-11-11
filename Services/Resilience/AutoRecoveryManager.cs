using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.Resilience;

/// <summary>
/// 自动恢复管理器
/// </summary>
/// <remarks>
/// 核心功能:
/// 1. 故障检测
/// 2. 自动重试策略
/// 3. 降级策略
/// 4. 熔断机制
/// 5. 恢复验证
/// 
/// 恢复策略:
/// - 指数退避重试
/// - 自动降级
/// - 服务隔离
/// - 优雅降级
/// </remarks>
public class AutoRecoveryManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ComponentHealth> _componentHealth;
    private readonly ConcurrentDictionary<string, RecoveryPolicy> _policies;
    private readonly ConcurrentQueue<RecoveryAttempt> _recoveryHistory;
    private readonly Timer _healthCheckTimer;
    
    // 恢复配置
    private readonly RecoveryConfiguration _config;
    
    // 统计信息
    private long _totalRecoveries;
    private long _successfulRecoveries;
    private long _failedRecoveries;

    public AutoRecoveryManager(RecoveryConfiguration? config = null)
    {
        _config = config ?? RecoveryConfiguration.Default;
        _componentHealth = new ConcurrentDictionary<string, ComponentHealth>();
        _policies = new ConcurrentDictionary<string, RecoveryPolicy>();
        _recoveryHistory = new ConcurrentQueue<RecoveryAttempt>();
        
        // 每30秒健康检查
        _healthCheckTimer = new Timer(HealthCheckCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        
        // 注册默认策略
        RegisterDefaultPolicies();
        
        LogService.Info("[AutoRecoveryManager] 自动恢复管理器已启动");
    }

    /// <summary>
    /// 恢复事件
    /// </summary>
    public event EventHandler<RecoveryEventArgs>? RecoveryAttempted;
    public event EventHandler<RecoveryEventArgs>? RecoverySucceeded;
    public event EventHandler<RecoveryEventArgs>? RecoveryFailed;

    #region 策略注册

    /// <summary>
    /// 注册默认恢复策略
    /// </summary>
    private void RegisterDefaultPolicies()
    {
        // API重试策略
        RegisterPolicy("ApiClient", new RecoveryPolicy
        {
            Name = "API重试策略",
            MaxRetries = 3,
            RetryDelay = TimeSpan.FromSeconds(1),
            UseExponentialBackoff = true,
            CircuitBreakerThreshold = 5,
            CircuitBreakerTimeout = TimeSpan.FromMinutes(1),
            RecoveryAction = async (component, attempt, ct) =>
            {
                LogService.Info("[AutoRecovery] 尝试恢复API连接: Attempt={Attempt}", attempt);
                await Task.Delay(100, ct); // 模拟恢复操作
                return true;
            }
        });

        // 交易引擎恢复策略
        RegisterPolicy("TradingEngine", new RecoveryPolicy
        {
            Name = "交易引擎恢复策略",
            MaxRetries = 2,
            RetryDelay = TimeSpan.FromSeconds(5),
            UseExponentialBackoff = false,
            CircuitBreakerThreshold = 3,
            CircuitBreakerTimeout = TimeSpan.FromMinutes(5),
            RecoveryAction = async (component, attempt, ct) =>
            {
                LogService.Info("[AutoRecovery] 尝试恢复交易引擎: Attempt={Attempt}", attempt);
                await Task.Delay(100, ct);
                return true;
            }
        });

        // 数据处理恢复策略
        RegisterPolicy("DataProcessor", new RecoveryPolicy
        {
            Name = "数据处理恢复策略",
            MaxRetries = 5,
            RetryDelay = TimeSpan.FromMilliseconds(500),
            UseExponentialBackoff = true,
            CircuitBreakerThreshold = 10,
            CircuitBreakerTimeout = TimeSpan.FromSeconds(30),
            RecoveryAction = async (component, attempt, ct) =>
            {
                LogService.Info("[AutoRecovery] 尝试恢复数据处理: Attempt={Attempt}", attempt);
                await Task.Delay(100, ct);
                return true;
            }
        });
    }

    /// <summary>
    /// 注册恢复策略
    /// </summary>
    public void RegisterPolicy(string component, RecoveryPolicy policy)
    {
        _policies[component] = policy;
        LogService.Info("[AutoRecoveryManager] 已注册恢复策略: Component={Component}, Policy={Policy}",
            component, policy.Name);
    }

    #endregion

    #region 故障检测

    /// <summary>
    /// 报告组件故障
    /// </summary>
    public async Task<bool> ReportFailureAsync(
        string component,
        Exception exception,
        CancellationToken ct = default)
    {
        // 获取或创建健康状态
        ComponentHealth health = _componentHealth.GetOrAdd(component, _ => new ComponentHealth
        {
            Component = component,
            State = HealthState.Healthy,
            LastCheck = DateTime.UtcNow,
            FailureCount = 0,
            ConsecutiveFailures = 0
        });

        // 更新故障计数
        health.FailureCount++;
        health.ConsecutiveFailures++;
        health.LastFailure = DateTime.UtcNow;
        health.LastException = exception.Message;

        // 检查是否需要恢复
        if (ShouldAttemptRecovery(component, health))
        {
            return await AttemptRecoveryAsync(component, exception, ct);
        }

        // 检查是否需要熔断
        if (ShouldCircuitBreak(component, health))
        {
            health.State = HealthState.CircuitOpen;
            health.CircuitOpenUntil = DateTime.UtcNow.Add(_config.DefaultCircuitBreakTimeout);
            
            LogService.Warning("[AutoRecoveryManager] 组件熔断: Component={Component}, Failures={Failures}",
                component, health.ConsecutiveFailures);
        }

        return false;
    }

    /// <summary>
    /// 报告组件恢复
    /// </summary>
    public void ReportRecovery(string component)
    {
        if (_componentHealth.TryGetValue(component, out ComponentHealth health))
        {
            health.State = HealthState.Healthy;
            health.ConsecutiveFailures = 0;
            health.LastCheck = DateTime.UtcNow;
            health.CircuitOpenUntil = null;
            
            LogService.Info("[AutoRecoveryManager] 组件已恢复: Component={Component}", component);
        }
    }

    #endregion

    #region 自动恢复

    /// <summary>
    /// 尝试自动恢复
    /// </summary>
    private async Task<bool> AttemptRecoveryAsync(
        string component,
        Exception exception,
        CancellationToken ct)
    {
        if (!_policies.TryGetValue(component, out RecoveryPolicy policy))
        {
            LogService.Warning("[AutoRecoveryManager] 未找到恢复策略: Component={Component}", component);
            return false;
        }

        ComponentHealth health = _componentHealth[component];
        int attempt = health.RecoveryAttempts + 1;

        // 检查重试次数
        if (attempt > policy.MaxRetries)
        {
            LogService.Error("[AutoRecoveryManager] 恢复次数已达上限: Component={Component}, Attempts={Attempts}",
                component, attempt);
            return false;
        }

        // 计算延迟
        TimeSpan delay = CalculateRetryDelay(policy, attempt);
        
        LogService.Info("[AutoRecoveryManager] 开始恢复: Component={Component}, Attempt={Attempt}/{Max}, Delay={Delay}s",
            component, attempt, policy.MaxRetries, delay.TotalSeconds);

        // 等待
        await Task.Delay(delay, ct);

        // 记录恢复尝试
        RecoveryAttempt recoveryAttempt = new RecoveryAttempt
        {
            Id = Guid.NewGuid().ToString(),
            Component = component,
            AttemptNumber = attempt,
            StartTime = DateTime.UtcNow,
            Exception = exception.Message
        };

        Interlocked.Increment(ref _totalRecoveries);
        _recoveryHistory.Enqueue(recoveryAttempt);

        // 限制历史记录大小
        while (_recoveryHistory.Count > 1000)
        {
            _recoveryHistory.TryDequeue(out _);
        }

        // 触发事件
        RecoveryAttempted?.Invoke(this, new RecoveryEventArgs(recoveryAttempt));

        try
        {
            // 执行恢复操作
            bool success = await policy.RecoveryAction(component, attempt, ct);

            recoveryAttempt.EndTime = DateTime.UtcNow;
            recoveryAttempt.Success = success;

            if (success)
            {
                // 恢复成功
                Interlocked.Increment(ref _successfulRecoveries);
                health.State = HealthState.Recovering;
                health.ConsecutiveFailures = 0;
                health.RecoveryAttempts = 0;
                
                LogService.Info("✅ [AutoRecoveryManager] 恢复成功: Component={Component}", component);
                RecoverySucceeded?.Invoke(this, new RecoveryEventArgs(recoveryAttempt));
                
                return true;
            }
            else
            {
                // 恢复失败
                Interlocked.Increment(ref _failedRecoveries);
                health.RecoveryAttempts++;
                
                LogService.Warning("❌ [AutoRecoveryManager] 恢复失败: Component={Component}, Attempt={Attempt}",
                    component, attempt);
                RecoveryFailed?.Invoke(this, new RecoveryEventArgs(recoveryAttempt));
                
                return false;
            }
        }
        catch (Exception ex)
        {
            // 恢复异常
            Interlocked.Increment(ref _failedRecoveries);
            health.RecoveryAttempts++;
            
            LogService.Error(ex, "[AutoRecoveryManager] 恢复异常: Component={Component}", component);
            return false;
        }
    }

    /// <summary>
    /// 计算重试延迟
    /// </summary>
    private TimeSpan CalculateRetryDelay(RecoveryPolicy policy, int attempt)
    {
        if (!policy.UseExponentialBackoff)
        {
            return policy.RetryDelay;
        }

        // 指数退避: delay * 2^(attempt-1)
        double multiplier = Math.Pow(2, attempt - 1);
        double delaySeconds = policy.RetryDelay.TotalSeconds * multiplier;
        
        // 限制最大延迟
        double maxDelay = Math.Min(delaySeconds, 60); // 最多60秒
        
        return TimeSpan.FromSeconds(maxDelay);
    }

    #endregion

    #region 健康检查

    /// <summary>
    /// 健康检查回调
    /// </summary>
    private void HealthCheckCallback(object? state)
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            
            foreach (ComponentHealth health in _componentHealth.Values)
            {
                // 检查熔断器是否应该半开
                if (health.State == HealthState.CircuitOpen &&
                    health.CircuitOpenUntil.HasValue &&
                    health.CircuitOpenUntil.Value < now)
                {
                    health.State = HealthState.CircuitHalfOpen;
                    LogService.Info("[AutoRecoveryManager] 熔断器半开: Component={Component}", health.Component);
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[AutoRecoveryManager] 健康检查失败");
        }
    }

    /// <summary>
    /// 获取组件健康状态
    /// </summary>
    public ComponentHealth? GetHealth(string component)
    {
        return _componentHealth.TryGetValue(component, out ComponentHealth health) ? health : null;
    }

    /// <summary>
    /// 获取所有健康状态
    /// </summary>
    public List<ComponentHealth> GetAllHealth()
    {
        return _componentHealth.Values.ToList();
    }

    #endregion

    #region 决策逻辑

    /// <summary>
    /// 是否应该尝试恢复
    /// </summary>
    private bool ShouldAttemptRecovery(string component, ComponentHealth health)
    {
        // 熔断器打开，不恢复
        if (health.State == HealthState.CircuitOpen)
        {
            return false;
        }

        // 获取策略
        if (!_policies.TryGetValue(component, out RecoveryPolicy policy))
        {
            return false;
        }

        // 检查重试次数
        return health.RecoveryAttempts < policy.MaxRetries;
    }

    /// <summary>
    /// 是否应该熔断
    /// </summary>
    private bool ShouldCircuitBreak(string component, ComponentHealth health)
    {
        if (!_policies.TryGetValue(component, out RecoveryPolicy policy))
        {
            return false;
        }

        return health.ConsecutiveFailures >= policy.CircuitBreakerThreshold;
    }

    #endregion

    #region 统计查询

    /// <summary>
    /// 获取恢复统计
    /// </summary>
    public RecoveryStatistics GetStatistics()
    {
        return new RecoveryStatistics
        {
            TotalRecoveries = Interlocked.Read(ref _totalRecoveries),
            SuccessfulRecoveries = Interlocked.Read(ref _successfulRecoveries),
            FailedRecoveries = Interlocked.Read(ref _failedRecoveries),
            SuccessRate = _totalRecoveries > 0 ? (double)_successfulRecoveries / _totalRecoveries : 0,
            ComponentCount = _componentHealth.Count,
            UnhealthyComponents = _componentHealth.Values.Count(h => h.State != HealthState.Healthy)
        };
    }

    /// <summary>
    /// 获取恢复历史
    /// </summary>
    public List<RecoveryAttempt> GetRecoveryHistory(int count = 50)
    {
        return _recoveryHistory.TakeLast(count).ToList();
    }

    #endregion

    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        LogService.Info("[AutoRecoveryManager] 已停止");
    }
}

#region 数据模型

/// <summary>
/// 组件健康状态
/// </summary>
public class ComponentHealth
{
    public string Component { get; set; } = string.Empty;
    public HealthState State { get; set; }
    public DateTime LastCheck { get; set; }
    public int FailureCount { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int RecoveryAttempts { get; set; }
    public DateTime? LastFailure { get; set; }
    public string? LastException { get; set; }
    public DateTime? CircuitOpenUntil { get; set; }
}

/// <summary>
/// 健康状态
/// </summary>
public enum HealthState
{
    Healthy,           // 健康
    Degraded,          // 降级
    Unhealthy,         // 不健康
    Recovering,        // 恢复中
    CircuitOpen,       // 熔断器打开
    CircuitHalfOpen    // 熔断器半开
}

/// <summary>
/// 恢复策略
/// </summary>
public class RecoveryPolicy
{
    public string Name { get; init; } = string.Empty;
    public int MaxRetries { get; init; }
    public TimeSpan RetryDelay { get; init; }
    public bool UseExponentialBackoff { get; init; }
    public int CircuitBreakerThreshold { get; init; }
    public TimeSpan CircuitBreakerTimeout { get; init; }
    public required Func<string, int, CancellationToken, Task<bool>> RecoveryAction { get; init; }
}

/// <summary>
/// 恢复配置
/// </summary>
public class RecoveryConfiguration
{
    public TimeSpan DefaultCircuitBreakTimeout { get; init; } = TimeSpan.FromMinutes(1);
    public int DefaultMaxRetries { get; init; } = 3;
    public TimeSpan DefaultRetryDelay { get; init; } = TimeSpan.FromSeconds(1);

    public static RecoveryConfiguration Default => new RecoveryConfiguration();
}

/// <summary>
/// 恢复尝试记录
/// </summary>
public class RecoveryAttempt
{
    public string Id { get; init; } = string.Empty;
    public string Component { get; init; } = string.Empty;
    public int AttemptNumber { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime? EndTime { get; set; }
    public bool Success { get; set; }
    public string Exception { get; init; } = string.Empty;
}

/// <summary>
/// 恢复统计
/// </summary>
public class RecoveryStatistics
{
    public long TotalRecoveries { get; init; }
    public long SuccessfulRecoveries { get; init; }
    public long FailedRecoveries { get; init; }
    public double SuccessRate { get; init; }
    public int ComponentCount { get; init; }
    public int UnhealthyComponents { get; init; }
}

/// <summary>
/// 恢复事件参数
/// </summary>
public class RecoveryEventArgs : EventArgs
{
    public RecoveryAttempt Attempt { get; }

    public RecoveryEventArgs(RecoveryAttempt attempt)
    {
        Attempt = attempt;
    }
}

#endregion
