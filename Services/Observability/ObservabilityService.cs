using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

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
    private readonly ConcurrentQueue<string> _logs = new ConcurrentQueue<string>();
    private readonly int _maxBuffer = 1000;

    public static ObservabilityService Instance { get; } = new ObservabilityService();

    public event Action<string> LogAppended;

    private ObservabilityService() { }

    public void AddLog(string message)
    {
        var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        _logs.Enqueue(entry);
        while (_logs.Count > _maxBuffer && _logs.TryDequeue(out _)) { }
        try { LogAppended?.Invoke(entry); } catch { }
    }

    public IEnumerable<string> GetRecentLogs(int max = 200)
    {
        return _logs.Reverse().Take(max);
    }

    public string ExportToFile(string filePath = null)
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
    }
}
