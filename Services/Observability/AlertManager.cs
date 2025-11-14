using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.Observability;

/// <summary>
/// 告警管理器
/// </summary>
public class AlertManager
{
    // Event for real-time UI subscription
    public event Action<AlertEvent>? AlertRaised;
    // Event when an automatic protection action was taken for an alert
    public event Action<AlertEvent, string>? AlertActionTaken;

    private readonly List<AlertRule> _rules;
    private readonly ConcurrentQueue<AlertEvent> _alerts;
    private readonly List<IAlertNotifier> _notifiers;
    private readonly Timer _evaluationTimer;

    private const int MaxAlerts = 1000;
    private const int EvaluationIntervalMs = 5000; // 5秒

    public AlertManager()
    {
        _rules = new List<AlertRule>();
        _alerts = new ConcurrentQueue<AlertEvent>();
        _notifiers = new List<IAlertNotifier>();

        _evaluationTimer = new Timer(
            async _ => await EvaluateRulesAsync(),
            null,
            TimeSpan.FromMilliseconds(EvaluationIntervalMs),
            TimeSpan.FromMilliseconds(EvaluationIntervalMs)
        );
    }

    /// <summary>
    /// 添加规则
    /// </summary>
    public void AddRule(AlertRule rule)
    {
        _rules.Add(rule);
    }

    /// <summary>
    /// 添加通知器
    /// </summary>
    public void AddNotifier(IAlertNotifier notifier)
    {
        _notifiers.Add(notifier);
    }

    /// <summary>
    /// 触发告警
    /// </summary>
    public async Task TriggerAlert(AlertEvent alertEvent)
    {
        _alerts.Enqueue(alertEvent);

        // 限制队列大小
        while (_alerts.Count > MaxAlerts)
        {
            _alerts.TryDequeue(out _);
        }

        // Fire in-process event for UI subscribers (non-blocking)
        try
        {
            AlertRaised?.Invoke(alertEvent);
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "AlertRaised handler failed");
        }

        try
        {
            LogService.Info("[AlertManager] Triggered alert: {Severity} - {Message}", alertEvent.Severity, alertEvent.Message);
            await NotifyAsync(alertEvent).ConfigureAwait(false);

            // 尝试持久化告警（若 DataCacheService 支持）
            try
            {
                var cache = ServiceLocator.Cache;
                if (cache != null)
                {
                    await cache.SaveAlertAsync(alertEvent.RuleName, alertEvent.Severity.ToString(), alertEvent.Message, alertEvent.Timestamp).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "Alert persistence failed");
            }
            // If critical alert, perform automatic protection actions
            if (alertEvent.Severity == AlertSeverity.Critical)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Stop automated trading to prevent further damage
                        try
                        {
                            await ServiceLocator.AutoTrader.StopAsync().ConfigureAwait(false);
                            LogService.Info("AutoTrader stopped due to critical alert: {AlertId}", alertEvent.Id);
                        }
                        catch (Exception ex)
                        {
                            LogService.Error(ex, "Failed to stop AutoTrader for critical alert");
                        }

                        // persist an action note to alerts table (append to message)
                        try
                        {
                            var cache = ServiceLocator.Cache;
                            if (cache != null)
                            {
                                string actionNote = $"AUTO_PROTECT: AutoTrader stopped at {DateTime.UtcNow:O}";
                                await cache.SaveAlertAsync($"Action:{alertEvent.RuleName}", alertEvent.Severity.ToString(), actionNote, DateTime.UtcNow).ConfigureAwait(false);
                            }
                            }
                            catch (Exception ex)
                            {
                                LogService.Error(ex, "Failed to persist auto-protect action");
                            }

                            // notify subscribers that action was taken
                            try
                            {
                                AlertActionTaken?.Invoke(alertEvent, "AutoTraderStopped");
                            }
                            catch (Exception ex)
                            {
                                LogService.Error(ex, "AlertActionTaken handler failed");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogService.Error(ex, "Automatic protection execution failed");
                        }
                    });
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "NotifyAsync failed");
        }
    }

    /// <summary>
    /// 评估规则
    /// </summary>
    private async Task EvaluateRulesAsync()
    {
        // 这里可以基于指标数据评估规则
        // 简化实现
        await Task.CompletedTask;
    }

    /// <summary>
    /// 发送通知
    /// </summary>
    private async Task NotifyAsync(AlertEvent alertEvent)
    {
        var tasks = _notifiers.Select(notifier => notifier.NotifyAsync(alertEvent));
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 获取告警
    /// </summary>
    public List<AlertEvent> GetAlerts(int count = 50)
    {
        return _alerts.TakeLast(count).ToList();
    }
}

#region 数据模型

public class AlertRule
{
    public string Name { get; init; } = string.Empty;
    public string Condition { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
    public AlertSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
    public List<string> Actions { get; init; } = new();
}

public class AlertEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string RuleName { get; init; } = string.Empty;
    public AlertSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Dictionary<string, object> Context { get; init; } = new();
    public bool IsResolved { get; set; }
}

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

public interface IAlertNotifier
{
    Task NotifyAsync(AlertEvent alertEvent);
}

/// <summary>
/// UI通知器
/// </summary>
public class UIAlertNotifier : IAlertNotifier
{
    public Task NotifyAsync(AlertEvent alertEvent)
    {
        // 可以通过事件总线通知UI
        LogService.Warning($"[ALERT] {alertEvent.Severity}: {alertEvent.Message}");
        return Task.CompletedTask;
    }
}

#endregion
