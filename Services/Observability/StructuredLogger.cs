using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace 币安量化机器人.Services.Observability;

public class StructuredLogger : IDisposable
{
    private readonly string _loggerName;
    private readonly LogLevel _minimumLevel;
    private readonly ConcurrentQueue<LogEntry> _logQueue;
    private readonly List<ILogOutput> _outputs;
    private readonly Timer _flushTimer;
    private readonly SemaphoreSlim _flushLock;

    private const int MaxQueueSize = 10000;
    private const int FlushIntervalMs = 1000;
    private const int MaxBatchSize = 100;

    public StructuredLogger(
        string loggerName,
        LogLevel minimumLevel = LogLevel.Info)
    {
        _loggerName = loggerName;
        _minimumLevel = minimumLevel;
        _logQueue = new ConcurrentQueue<LogEntry>();
        _outputs = new List<ILogOutput>();
        _flushLock = new SemaphoreSlim(1, 1);

        _flushTimer = new Timer(
            async _ => await FlushAsync(),
            null,
            TimeSpan.FromMilliseconds(FlushIntervalMs),
            TimeSpan.FromMilliseconds(FlushIntervalMs)
        );
    }

    #region 日志记录

    /// <summary>
    /// 记录Trace级别日志
    /// </summary>
    public void Trace(string message, object? properties = null) => Log(LogLevel.Trace, message, properties, null);

    /// <summary>
    /// 记录Debug级别日志
    /// </summary>
    public void Debug(string message, object? properties = null) => Log(LogLevel.Debug, message, properties, null);

    /// <summary>
    /// 记录Info级别日志
    /// </summary>
    public void Info(string message, object? properties = null) => Log(LogLevel.Info, message, properties, null);

    /// <summary>
    /// 记录Warning级别日志
    /// </summary>
    public void Warning(string message, object? properties = null) => Log(LogLevel.Warning, message, properties, null);

    /// <summary>
    /// 记录Error级别日志
    /// </summary>
    public void Error(string message, Exception? exception = null, object? properties = null) => Log(LogLevel.Error, message, properties, exception);

    /// <summary>
    /// 记录Critical级别日志
    /// </summary>
    public void Critical(string message, Exception? exception = null, object? properties = null) => Log(LogLevel.Critical, message, properties, exception);

    /// <summary>
    /// 核心日志记录方法
    /// </summary>
    private void Log(
        LogLevel level,
        string message,
        object? properties,
        Exception? exception)
    {
        // 检查日志等级
        if (level < _minimumLevel)
            return;

        // 检查队列大小
        if (_logQueue.Count >= MaxQueueSize)
            _logQueue.TryDequeue(out _);

        // 获取当前Activity（追踪信息）
        var activity = Activity.Current;

        // 创建日志条目
        var logEntry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Logger = _loggerName,
            Message = message,
            TraceId = activity?.TraceId.ToString() ?? string.Empty,
            SpanId = activity?.SpanId.ToString(),
            Operation = activity?.OperationName ?? _loggerName,
            Duration = activity?.Duration.TotalMilliseconds,
            Properties = ConvertToDict(properties),
            Exception = exception
        };

        // 入队
        _logQueue.Enqueue(logEntry);

        // 如果是Critical级别，立即刷新
        if (level == LogLevel.Critical)
            Task.Run(async () => await FlushAsync());
    }

    /// <summary>
    /// 转换属性为字典
    /// </summary>
    private Dictionary<string, object>? ConvertToDict(object? properties)
    {
        if (properties == null)
            return null;
        if (properties is Dictionary<string, object> dict)
            return dict;

        // 使用反射转换匿名对象
        var result = new Dictionary<string, object>();
        foreach (var prop in properties.GetType().GetProperties())
            result[prop.Name] = prop.GetValue(properties) ?? "null";
        return result;
    }

    #endregion

    #region 日志输出

    /// <summary>
    /// 添加输出目标
    /// </summary>
    public void AddOutput(ILogOutput output) => _outputs.Add(output);

    /// <summary>
    /// 刷新日志到输出目标
    /// </summary>
    private async Task FlushAsync()
    {
        if (_logQueue.IsEmpty)
            return;

        await _flushLock.WaitAsync();
        try
        {
            var batch = new List<LogEntry>();
            // 批量出队
            while (batch.Count < MaxBatchSize && _logQueue.TryDequeue(out var entry))
                batch.Add(entry);
            if (batch.Count == 0)
                return;

            // 写入所有输出目标
            var tasks = _outputs.Select(output => output.WriteAsync(batch));
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            // 避免日志系统异常影响主程序
            Console.WriteLine($"[StructuredLogger] Flush failed: {ex.Message}");
        }
        finally
        {
            _flushLock.Release();
        }
    }

    /// <summary>
    /// 手动刷新
    /// </summary>
    public async Task FlushNowAsync() => await FlushAsync();

    #endregion

    #region IDisposable

    public void Dispose()
    {
        _flushTimer?.Dispose();
        FlushAsync().Wait();
        _flushLock?.Dispose();

        foreach (var output in _outputs)
            if (output is IDisposable d)
                d.Dispose();
    }

    #endregion
}

#region 数据模型

/// <summary>
/// 日志等级
/// </summary>
public enum LogLevel { Trace = 0, Debug = 1, Info = 2, Warning = 3, Error = 4, Critical = 5 }

/// <summary>
/// 日志条目
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Logger { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string? SpanId { get; init; }
    public string Operation { get; init; } = string.Empty;
    public double? Duration { get; init; }
    public Dictionary<string, object>? Properties { get; init; }
    public Exception? Exception { get; init; }

    /// <summary>
    /// 转换为JSON
    /// </summary>
    public string ToJson()
    {
        var obj = new Dictionary<string, object>
        {
            ["timestamp"] = Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            ["level"] = Level.ToString(),
            ["logger"] = Logger,
            ["message"] = Message,
            ["context"] = new Dictionary<string, object>
            {
                ["traceId"] = TraceId,
                ["spanId"] = SpanId ?? string.Empty,
                ["operation"] = Operation,
                ["duration"] = Duration ?? 0d // null-safe to avoid CS8601
            }
        };

        if (Properties != null && Properties.Count > 0)
            obj["properties"] = Properties;

        if (Exception != null)
        {
            obj["exception"] = new Dictionary<string, object>
            {
                ["type"] = Exception.GetType().Name,
                ["message"] = Exception.Message,
                ["stackTrace"] = Exception.StackTrace ?? string.Empty
            };
        }

        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = false });
    }

    /// <summary>
    /// 转换为可读文本
    /// </summary>
    public string ToText()
    {
        var parts = new List<string>
        {
            Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
            $"[{Level}]",
            $"[{Logger}]",
            Message
        };

        if (Properties != null && Properties.Count > 0)
        {
            var props = string.Join(", ", Properties.Select(kv => $"{kv.Key}={kv.Value}"));
            parts.Add($"({props})");
        }

        if (Exception != null)
            parts.Add($"\n  Exception: {Exception.GetType().Name}: {Exception.Message}");

        return string.Join(" ", parts);
    }
}

/// <summary>
/// 日志输出接口
/// </summary>
public interface ILogOutput { Task WriteAsync(IEnumerable<LogEntry> entries); }

#endregion

#region 输出实现

/// <summary>
/// 控制台输出
/// </summary>
public class ConsoleLogOutput : ILogOutput
{
    private readonly bool _useColors;
    public ConsoleLogOutput(bool useColors = true) { _useColors = useColors; }

    public Task WriteAsync(IEnumerable<LogEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (_useColors)
                Console.ForegroundColor = GetColor(entry.Level);
            Console.WriteLine(entry.ToText());
            if (_useColors)
                Console.ResetColor();
        }
        return Task.CompletedTask;
    }

    private ConsoleColor GetColor(LogLevel level) => level switch
    {
        LogLevel.Trace => ConsoleColor.Gray,
        LogLevel.Debug => ConsoleColor.Cyan,
        LogLevel.Info => ConsoleColor.White,
        LogLevel.Warning => ConsoleColor.Yellow,
        LogLevel.Error => ConsoleColor.Red,
        LogLevel.Critical => ConsoleColor.Magenta,
        _ => ConsoleColor.White
    };
}

/// <summary>
/// 文件输出
/// </summary>
public class FileLogOutput : ILogOutput, IDisposable
{
    private readonly string _filePath;
    private readonly bool _useJson;
    private readonly SemaphoreSlim _writeLock;
    private StreamWriter? _writer;

    public FileLogOutput(string filePath, bool useJson = true)
    {
        _filePath = filePath;
        _useJson = useJson;
        _writeLock = new SemaphoreSlim(1, 1);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
    }

    public async Task WriteAsync(IEnumerable<LogEntry> entries)
    {
        if (_writer == null)
            return;
        await _writeLock.WaitAsync();
        try
        {
            foreach (var entry in entries)
            {
                var line = _useJson ? entry.ToJson() : entry.ToText();
                await _writer.WriteLineAsync(line);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _writeLock?.Dispose();
    }
}

/// <summary>
/// 内存输出（用于测试和调试）
/// </summary>
public class MemoryLogOutput : ILogOutput
{
    private readonly ConcurrentQueue<LogEntry> _entries;
    private readonly int _maxEntries;
    public MemoryLogOutput(int maxEntries = 1000) { _entries = new ConcurrentQueue<LogEntry>(); _maxEntries = maxEntries; }

    public Task WriteAsync(IEnumerable<LogEntry> entries)
    {
        foreach (var entry in entries)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > _maxEntries)
                _entries.TryDequeue(out _);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取所有日志
    /// </summary>
    public List<LogEntry> GetEntries() => _entries.ToList();

    /// <summary>
    /// 清空日志
    /// </summary>
    public void Clear() => _entries.Clear();
}

#endregion
