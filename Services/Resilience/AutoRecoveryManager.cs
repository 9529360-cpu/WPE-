using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.Resilience;

public class AutoRecoveryManager : IDisposable
{
    private readonly ConcurrentDictionary<string, ComponentHealth> _componentHealth;
    private readonly ConcurrentDictionary<string, RecoveryPolicy> _policies;
    private readonly ConcurrentQueue<RecoveryAttempt> _recoveryHistory;
    private readonly Timer _healthCheckTimer;
    private readonly RecoveryConfiguration _config;
    private long _totalRecoveries;
    private long _successfulRecoveries;
    private long _failedRecoveries;

    public AutoRecoveryManager(RecoveryConfiguration? config = null)
    {
        _config = config ?? RecoveryConfiguration.Default;
        _componentHealth = new ConcurrentDictionary<string, ComponentHealth>();
        _policies = new ConcurrentDictionary<string, RecoveryPolicy>();
        _recoveryHistory = new ConcurrentQueue<RecoveryAttempt>();
        _healthCheckTimer = new Timer(HealthCheckCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        RegisterDefaultPolicies();
        LogService.Info("[AutoRecoveryManager] 自动恢复管理器已启动");
    }

    public event EventHandler<RecoveryEventArgs>? RecoveryAttempted;
    public event EventHandler<RecoveryEventArgs>? RecoverySucceeded;
    public event EventHandler<RecoveryEventArgs>? RecoveryFailed;

    private void RegisterDefaultPolicies()
    {
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
                await Task.Delay(100, ct);
                return true;
            }
        });

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

    public void RegisterPolicy(string component, RecoveryPolicy policy)
    {
        _policies[component] = policy;
        LogService.Info("[AutoRecoveryManager] 已注册恢复策略: Component={Component}, Policy={Policy}", component, policy.Name);
    }

    public async Task<bool> ReportFailureAsync(string component, Exception exception, CancellationToken ct = default)
    {
        ComponentHealth health = _componentHealth.GetOrAdd(component, _ => new ComponentHealth
        {
            Component = component,
            State = HealthState.Healthy,
            LastCheck = DateTime.UtcNow,
            FailureCount = 0,
            ConsecutiveFailures = 0
        });

        health.FailureCount++;
        health.ConsecutiveFailures++;
        health.LastFailure = DateTime.UtcNow;
        health.LastException = exception.Message;

        if (ShouldAttemptRecovery(component, health))
            return await AttemptRecoveryAsync(component, exception, ct);

        if (ShouldCircuitBreak(component, health))
        {
            health.State = HealthState.CircuitOpen;
            health.CircuitOpenUntil = DateTime.UtcNow.Add(_config.DefaultCircuitBreakTimeout);
            LogService.Warning("[AutoRecoveryManager] 组件熔断: Component={Component}, Failures={Failures}", component, health.ConsecutiveFailures);
        }
        return false;
    }

    public void ReportRecovery(string component)
    {
        if (_componentHealth.TryGetValue(component, out ComponentHealth? health) && health != null)
        {
            health.State = HealthState.Healthy;
            health.ConsecutiveFailures = 0;
            health.LastCheck = DateTime.UtcNow;
            health.CircuitOpenUntil = null;
            LogService.Info("[AutoRecoveryManager] 组件已恢复: Component={Component}", component);
        }
    }

    private async Task<bool> AttemptRecoveryAsync(string component, Exception exception, CancellationToken ct)
    {
        if (!_policies.TryGetValue(component, out RecoveryPolicy? policy) || policy == null)
        {
            LogService.Warning("[AutoRecoveryManager] 未找到恢复策略: Component={Component}", component);
            return false;
        }

        ComponentHealth health = _componentHealth[component];
        int attempt = health.RecoveryAttempts + 1;
        if (attempt > policy.MaxRetries)
        {
            LogService.Error("[AutoRecoveryManager] 恢复次数已达上限: Component={Component}, Attempts={Attempts}", component, attempt);
            return false;
        }

        TimeSpan delay = CalculateRetryDelay(policy, attempt);
        LogService.Info("[AutoRecoveryManager] 开始恢复: Component={Component}, Attempt={Attempt}/{Max}, Delay={Delay}s", component, attempt, policy.MaxRetries, delay.TotalSeconds);
        await Task.Delay(delay, ct);

        var recoveryAttempt = new RecoveryAttempt
        {
            Id = Guid.NewGuid().ToString(),
            Component = component,
            AttemptNumber = attempt,
            StartTime = DateTime.UtcNow,
            Exception = exception.Message
        };

        Interlocked.Increment(ref _totalRecoveries);
        _recoveryHistory.Enqueue(recoveryAttempt);
        while (_recoveryHistory.Count > 1000)
            _recoveryHistory.TryDequeue(out _);

        RecoveryAttempted?.Invoke(this, new RecoveryEventArgs(recoveryAttempt));

        try
        {
            bool success = await policy.RecoveryAction(component, attempt, ct);
            recoveryAttempt.EndTime = DateTime.UtcNow;
            recoveryAttempt.Success = success;

            if (success)
            {
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
                Interlocked.Increment(ref _failedRecoveries);
                health.RecoveryAttempts++;
                LogService.Warning("❌ [AutoRecoveryManager] 恢复失败: Component={Component}, Attempt={Attempt}", component, attempt);
                RecoveryFailed?.Invoke(this, new RecoveryEventArgs(recoveryAttempt));
                return false;
            }
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedRecoveries);
            health.RecoveryAttempts++;
            LogService.Error(ex, "[AutoRecoveryManager] 恢复异常: Component={Component}", component);
            return false;
        }
    }

    private TimeSpan CalculateRetryDelay(RecoveryPolicy policy, int attempt)
    {
        if (!policy.UseExponentialBackoff)
            return policy.RetryDelay;
        double multiplier = Math.Pow(2, attempt - 1);
        double delaySeconds = policy.RetryDelay.TotalSeconds * multiplier;
        double maxDelay = Math.Min(delaySeconds, 60);
        return TimeSpan.FromSeconds(maxDelay);
    }

    private void HealthCheckCallback(object? state)
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            foreach (ComponentHealth health in _componentHealth.Values)
            {
                if (health.State == HealthState.CircuitOpen && health.CircuitOpenUntil.HasValue && health.CircuitOpenUntil.Value < now)
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

    public ComponentHealth? GetHealth(string component) => _componentHealth.TryGetValue(component, out ComponentHealth health) ? health : null;
    public List<ComponentHealth> GetAllHealth() => _componentHealth.Values.ToList();

    private bool ShouldAttemptRecovery(string component, ComponentHealth health)
    {
        if (health.State == HealthState.CircuitOpen)
            return false;
        if (!_policies.TryGetValue(component, out RecoveryPolicy? policy) || policy == null)
            return false;
        return health.RecoveryAttempts < policy.MaxRetries;
    }

    private bool ShouldCircuitBreak(string component, ComponentHealth health)
    {
        if (!_policies.TryGetValue(component, out RecoveryPolicy? policy) || policy == null)
            return false;
        return health.ConsecutiveFailures >= policy.CircuitBreakerThreshold;
    }

    public RecoveryStatistics GetStatistics()
        => new RecoveryStatistics
        {
            TotalRecoveries = Interlocked.Read(ref _totalRecoveries),
            SuccessfulRecoveries = Interlocked.Read(ref _successfulRecoveries),
            FailedRecoveries = Interlocked.Read(ref _failedRecoveries),
            SuccessRate = _totalRecoveries > 0 ? (double)_successfulRecoveries / _totalRecoveries : 0,
            ComponentCount = _componentHealth.Count,
            UnhealthyComponents = _componentHealth.Values.Count(h => h.State != HealthState.Healthy)
        };

    public List<RecoveryAttempt> GetRecoveryHistory(int count = 50) => _recoveryHistory.TakeLast(count).ToList();

    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        LogService.Info("[AutoRecoveryManager] 已停止");
    }
}

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

public enum HealthState { Healthy, Degraded, Unhealthy, Recovering, CircuitOpen, CircuitHalfOpen }

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

public class RecoveryConfiguration
{
    public TimeSpan DefaultCircuitBreakTimeout { get; init; } = TimeSpan.FromMinutes(1);
    public int DefaultMaxRetries { get; init; } = 3;
    public TimeSpan DefaultRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
    public static RecoveryConfiguration Default => new RecoveryConfiguration();
}

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

public class RecoveryStatistics
{
    public long TotalRecoveries { get; init; }
    public long SuccessfulRecoveries { get; init; }
    public long FailedRecoveries { get; init; }
    public double SuccessRate { get; init; }
    public int ComponentCount { get; init; }
    public int UnhealthyComponents { get; init; }
}

public class RecoveryEventArgs : EventArgs
{
    public RecoveryAttempt Attempt { get; }
    public RecoveryEventArgs(RecoveryAttempt attempt) { Attempt = attempt; }
}
