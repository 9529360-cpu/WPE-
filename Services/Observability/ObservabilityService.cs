using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using 币安量化机器人.Core.Risk;

namespace 币安量化机器人.Services.Observability;

public class ObservabilityService : IDisposable
{
    private readonly ConcurrentQueue<string> _logs = new ConcurrentQueue<string>();
    private readonly int _maxBuffer = 5000;
    private readonly string _name;

    public static ObservabilityService Instance { get; } = new ObservabilityService("System");

    public event Action<string>? LogAppended;
    public event Action<Core.Risk.RiskEvent>? AlertRaised;

    public ObservabilityService(string name = "app")
    {
        _name = name;
    }

    // Basic logging API
    public void LogDebug(string message, object? props = null) => AddEntry("DEBUG", message, props);
    public void LogInfo(string message, object? props = null) => AddEntry("INFO", message, props);
    public void LogWarning(string message, object? props = null) => AddEntry("WARN", message, props);
    public void LogError(string message, Exception? ex = null, object? props = null) => AddEntry("ERROR", message + (ex != null ? $" | {ex.Message}" : string.Empty), props);
    public void LogCritical(string message, Exception? ex = null, object? props = null) => AddEntry("FATAL", message + (ex != null ? $" | {ex.Message}" : string.Empty), props);

    // compatibility: AddLog used by older code
    public void AddLog(string message)
    {
        LogInfo(message);
    }

    private void AddEntry(string level, string message, object? props)
    {
        var entry = $"[{DateTime.UtcNow:O}] [{_name}] {level} - {message}";
        _logs.Enqueue(entry);
        while (_logs.Count > _maxBuffer && _logs.TryDequeue(out _)) { }
        try
        {
            LogAppended?.Invoke(entry);
        }
        catch { }
    }

    // Metrics (simple in-memory counters)
    private readonly ConcurrentDictionary<string, double> _counters = new ConcurrentDictionary<string, double>();
    private readonly ConcurrentDictionary<string, double> _gauges = new ConcurrentDictionary<string, double>();

    public void IncrementCounter(string name, double value = 1.0)
    {
        _counters.AddOrUpdate(name, value, (_, old) => old + value);
    }

    public void SetGauge(string name, double value)
    {
        _gauges[name] = value;
    }

    public void RecordHistogram(string name, double value) { /* stubbed */ }
    public void RecordDuration(string name, TimeSpan duration) { /* stubbed */ }

    // Traces / operation helpers
    public IDisposable StartTrace(string name)
    {
        // simple stopwatch-based tracer
        return new TraceScope(name, this);
    }

    public async Task<T> ObserveOperationAsync<T>(string operationName, Func<Task<T>> operation, object? properties = null)
    {
        using var scope = StartTrace(operationName);
        var sw = Stopwatch.StartNew();
        try
        {
            LogDebug($"开始操作: {operationName}", properties);
            var result = await operation().ConfigureAwait(false);
            sw.Stop();
            RecordDuration(operationName + "_duration_ms", sw.Elapsed);
            IncrementCounter(operationName + "_success_count");
            LogInfo($"完成操作: {operationName} (duration={sw.ElapsedMilliseconds}ms)");
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            IncrementCounter(operationName + "_error_count");
            LogError($"操作失败: {operationName}", ex, properties);
            throw;
        }
    }

    // Alerts
    public async Task TriggerAlert(string ruleName, Core.Risk.AlertSeverity severity, string message)
    {
        try
        {
            var ev = new Core.Risk.RiskEvent("", ruleName, message, DateTime.UtcNow);
            AlertRaised?.Invoke(ev);
        }
        catch { }
        await Task.CompletedTask;
    }

    // Compatibility overload accepting local AlertSeverity enum
    public async Task TriggerAlert(string ruleName, AlertSeverity severity, string message)
    {
        Core.Risk.AlertSeverity coreSev = Core.Risk.AlertSeverity.Warning;
        switch (severity)
        {
            case AlertSeverity.Info:
                coreSev = Core.Risk.AlertSeverity.Info;
                break;
            case AlertSeverity.Warning:
                coreSev = Core.Risk.AlertSeverity.Warning;
                break;
            case AlertSeverity.Error:
                coreSev = Core.Risk.AlertSeverity.Error;
                break;
            case AlertSeverity.Critical:
                coreSev = Core.Risk.AlertSeverity.Critical;
                break;
        }

        await TriggerAlert(ruleName, coreSev, message).ConfigureAwait(false);
    }

    // Retrieve logs
    public IEnumerable<string> GetRecentLogs(int max = 200)
    {
        return _logs.Reverse().Take(max);
    }

    public string? ExportToFile(string? filePath = null)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath))
            {
                var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(dir);
                filePath = Path.Combine(dir, $"logs_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            }

            File.WriteAllLines(filePath, GetRecentLogs().Reverse());
            return filePath;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        // nothing to dispose currently
    }

    private class TraceScope : IDisposable
    {
        private readonly string _name;
        private readonly ObservabilityService _obs;
        private readonly Stopwatch _sw;

        public TraceScope(string name, ObservabilityService obs)
        {
            _name = name;
            _obs = obs;
            _sw = Stopwatch.StartNew();
            _obs.LogDebug($"Trace start: {_name}");
        }

        public void Dispose()
        {
            _sw.Stop();
            _obs.LogDebug($"Trace end: {_name} duration={_sw.ElapsedMilliseconds}ms");
        }
    }

    public enum AlertSeverity { Info, Warning, Error, Critical }

    // Compatibility overload
    public void TriggerAlert(RiskEvent ev)
    {
        if (ev == null)
        {
            return;
        }
        AlertSeverity sev = AlertSeverity.Warning;
        switch (ev.RuleName)
        {
            default:
                sev = AlertSeverity.Warning;
                break;
        }

        // Map to Core.Risk.AlertSeverity
        Core.Risk.AlertSeverity coreSev = Core.Risk.AlertSeverity.Warning;
        switch (sev)
        {
            case AlertSeverity.Info:
                coreSev = Core.Risk.AlertSeverity.Info;
                break;
            case AlertSeverity.Warning:
                coreSev = Core.Risk.AlertSeverity.Warning;
                break;
            case AlertSeverity.Error:
                coreSev = Core.Risk.AlertSeverity.Error;
                break;
            case AlertSeverity.Critical:
                coreSev = Core.Risk.AlertSeverity.Critical;
                break;
        }

        // forward explicitly (avoid unawaited task warning)
        _ = TriggerAlert(ev.RuleName, coreSev, ev.Message); // intentionally fire-and-forget (forwarding compatibility)
    }
}
