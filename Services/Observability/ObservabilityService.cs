using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.Observability;

/// <summary>
/// 统一可观测性服务
/// </summary>
/// <remarks>
/// 整合三大支柱:
/// - Logs (日志)
/// - Metrics (指标)
/// - Traces (追踪)
/// - Alerts (告警)
/// </remarks>
public class ObservabilityService : IDisposable
{
    private readonly StructuredLogger _logger;
    private readonly MetricsCollector _metrics;
    private readonly AlertManager _alerts;

    public ObservabilityService(string loggerName = "System")
    {
        // 初始化日志
        _logger = new StructuredLogger(loggerName, LogLevel.Info);
        _logger.AddOutput(new ConsoleLogOutput(useColors: true));
        _logger.AddOutput(new FileLogOutput($"Logs/{DateTime.Now:yyyy-MM-dd}.log", useJson: true));

        // 初始化指标
        _metrics = new MetricsCollector();
        _metrics.AddExporter(new PrometheusExporter("Metrics/metrics.prom"));

        // 初始化告警
        _alerts = new AlertManager();
        _alerts.AddNotifier(new UIAlertNotifier());
    }

    #region Logs

    public void LogTrace(string message, object? properties = null)
    {
        _logger.Trace(message, properties);
    }

    public void LogDebug(string message, object? properties = null)
    {
        _logger.Debug(message, properties);
    }

    public void LogInfo(string message, object? properties = null)
    {
        _logger.Info(message, properties);
    }

    public void LogWarning(string message, object? properties = null)
    {
        _logger.Warning(message, properties);
    }

    public void LogError(string message, Exception? exception = null, object? properties = null)
    {
        _logger.Error(message, exception, properties);
    }

    public void LogCritical(string message, Exception? exception = null, object? properties = null)
    {
        _logger.Critical(message, exception, properties);
    }

    #endregion

    #region Metrics

    public void IncrementCounter(string name, double value = 1.0)
    {
        _metrics.Increment(name, value);
    }

    public void SetGauge(string name, double value)
    {
        _metrics.Set(name, value);
    }

    public void RecordHistogram(string name, double value)
    {
        _metrics.Observe(name, value);
    }

    public void RecordDuration(string name, TimeSpan duration)
    {
        _metrics.RecordDuration(name, duration);
    }

    #endregion

    #region Traces

    public Activity? StartTrace(string name)
    {
        return TraceManager.StartActivity(name);
    }

    public void AddTraceTag(string key, object? value)
    {
        TraceManager.AddTag(key, value);
    }

    public string GetCurrentTraceId()
    {
        return TraceManager.GetCurrentTraceId();
    }

    #endregion

    #region Alerts

    public async Task TriggerAlert(string ruleName, AlertSeverity severity, string message)
    {
        await _alerts.TriggerAlert(new AlertEvent
        {
            RuleName = ruleName,
            Severity = severity,
            Message = message
        });
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 记录操作（自动追踪+日志+指标）
    /// </summary>
    public async Task<T> ObserveOperationAsync<T>(
        string operationName,
        Func<Task<T>> operation,
        object? properties = null)
    {
        using var activity = StartTrace(operationName);
        var sw = Stopwatch.StartNew();

        try
        {
            LogDebug($"开始操作: {operationName}", properties);

            var result = await operation();

            sw.Stop();
            RecordDuration($"{operationName}_duration_ms", sw.Elapsed);
            IncrementCounter($"{operationName}_success_count");

            LogInfo($"完成操作: {operationName}", new { duration = sw.ElapsedMilliseconds });

            TraceManager.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            IncrementCounter($"{operationName}_error_count");
            LogError($"操作失败: {operationName}", ex, properties);
            TraceManager.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    #endregion

    public void Dispose()
    {
        _logger?.Dispose();
    }
}
