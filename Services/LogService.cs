using System;
using System.IO;
using System.Linq;
using Serilog;
using Serilog.Events;
using 币安量化机器人.Services.Runtime;

namespace 币安量化机器人.Services;

public static class LogService
{
    private static bool _initialized = false;

    public static void Initialize(
        LogEventLevel minimumLevel = LogEventLevel.Information,
        string? logFilePath = null)
    {
        if (_initialized)
        {
            Log.Warning("LogService already initialized, skipping re-initialization");
            return;
        }

        logFilePath ??= Path.Combine(AppContext.BaseDirectory, "Logs", "app-.log");

        string? logDirectory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.Debug()
            .CreateLogger();

        _initialized = true;

        Log.Information("=== 币安量化机器人日志系统启动 ===");
        Log.Information("日志级别: {MinimumLevel}", minimumLevel);
        Log.Information("日志文件: {LogFilePath}", logFilePath);
    }

    private static string RenderForBuffer(string level, string template, object?[]? propertyValues)
    {
        if (propertyValues == null || propertyValues.Length == 0)
        {
            return $"{level}: {template}";
        }

        try
        {
            var args = string.Join(", ", propertyValues.Select(v => v?.ToString() ?? "null"));
            return $"{level}: {template} | {args}";
        }
        catch
        {
            return $"{level}: {template}";
        }
    }

    public static void Debug(string messageTemplate, params object?[]? propertyValues)
    {
        Log.Debug(messageTemplate, propertyValues ?? Array.Empty<object>());
        try { InMemoryLogBuffer.Append(RenderForBuffer("DEBUG", messageTemplate, propertyValues)); } catch { }
    }

    public static void Info(string messageTemplate, params object?[]? propertyValues)
    {
        Log.Information(messageTemplate, propertyValues ?? Array.Empty<object>());
        try { InMemoryLogBuffer.Append(RenderForBuffer("INFO", messageTemplate, propertyValues)); } catch { }
    }

    public static void Warning(string messageTemplate, params object?[]? propertyValues)
    {
        Log.Warning(messageTemplate, propertyValues ?? Array.Empty<object>());
        try { InMemoryLogBuffer.Append(RenderForBuffer("WARN", messageTemplate, propertyValues)); } catch { }
    }

    public static void Error(string messageTemplate, params object?[]? propertyValues)
    {
        Log.Error(messageTemplate, propertyValues ?? Array.Empty<object>());
        try { InMemoryLogBuffer.Append(RenderForBuffer("ERROR", messageTemplate, propertyValues)); } catch { }
    }

    public static void Error(Exception exception, string messageTemplate, params object?[]? propertyValues)
    {
        Log.Error(exception, messageTemplate, propertyValues ?? Array.Empty<object>());
        try { InMemoryLogBuffer.Append($"ERROR: {exception.Message} - {RenderForBuffer("EX", messageTemplate, propertyValues)}"); } catch { }
    }

    public static void Fatal(string messageTemplate, params object?[]? propertyValues)
    {
        Log.Fatal(messageTemplate, propertyValues ?? Array.Empty<object>());
        try { InMemoryLogBuffer.Append(RenderForBuffer("FATAL", messageTemplate, propertyValues)); } catch { }
    }

    public static void Fatal(Exception exception, string messageTemplate, params object?[]? propertyValues)
    {
        Log.Fatal(exception, messageTemplate, propertyValues ?? Array.Empty<object>());
        try { InMemoryLogBuffer.Append($"FATAL: {exception.Message} - {RenderForBuffer("EX", messageTemplate, propertyValues)}"); } catch { }
    }

    public static void Shutdown()
    {
        Log.Information("=== 币安量化机器人日志系统关闭 ===");
        Log.CloseAndFlush();
    }
}

public static class LogLevels
{
    public const string Verbose = "Verbose";
    public const string Debug = "Debug";
    public const string Information = "Information";
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Fatal = "Fatal";
}
