using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace 币安量化机器人.Services;

/// <summary>
/// 日志服务 - 基于Serilog的结构化日志系统
/// </summary>
/// <remarks>
/// 提供统一的日志记录接口,支持多种输出目标:
/// - 控制台 (Console)
/// - 文件 (File,按天滚动)
/// - 调试输出 (Debug)
/// </remarks>
public static class LogService
{
    private static bool _initialized = false;

    /// <summary>
    /// 初始化日志系统
    /// </summary>
    /// <param name="minimumLevel">最小日志级别 (默认: Information)</param>
    /// <param name="logFilePath">日志文件路径 (默认: Logs/app-.log)</param>
    /// <remarks>
    /// 只能初始化一次,重复调用将被忽略
    /// </remarks>
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

        // 确保日志目录存在
        string? logDirectory = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning) // 减少Microsoft库的日志
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30, // 保留30天的日志
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.Debug()
            .CreateLogger();

        _initialized = true;

        Log.Information("=== 币安量化机器人日志系统启动 ===");
        Log.Information("日志级别: {MinimumLevel}", minimumLevel);
        Log.Information("日志文件: {LogFilePath}", logFilePath);
    }

    /// <summary>
    /// 记录调试信息
    /// </summary>
    public static void Debug(string messageTemplate, params object[] propertyValues)
    {
        Log.Debug(messageTemplate, propertyValues);
    }

    /// <summary>
    /// 记录一般信息
    /// </summary>
    public static void Info(string messageTemplate, params object[] propertyValues)
    {
        Log.Information(messageTemplate, propertyValues);
    }

    /// <summary>
    /// 记录警告信息
    /// </summary>
    public static void Warning(string messageTemplate, params object[] propertyValues)
    {
        Log.Warning(messageTemplate, propertyValues);
    }

    /// <summary>
    /// 记录错误信息
    /// </summary>
    public static void Error(string messageTemplate, params object[] propertyValues)
    {
        Log.Error(messageTemplate, propertyValues);
    }

    /// <summary>
    /// 记录异常
    /// </summary>
    public static void Error(Exception exception, string messageTemplate, params object[] propertyValues)
    {
        Log.Error(exception, messageTemplate, propertyValues);
    }

    /// <summary>
    /// 记录致命错误
    /// </summary>
    public static void Fatal(string messageTemplate, params object[] propertyValues)
    {
        Log.Fatal(messageTemplate, propertyValues);
    }

    /// <summary>
    /// 记录致命异常
    /// </summary>
    public static void Fatal(Exception exception, string messageTemplate, params object[] propertyValues)
    {
        Log.Fatal(exception, messageTemplate, propertyValues);
    }

    /// <summary>
    /// 关闭日志系统并刷新缓冲区
    /// </summary>
    public static void Shutdown()
    {
        Log.Information("=== 币安量化机器人日志系统关闭 ===");
        Log.CloseAndFlush();
    }
}

/// <summary>
/// 日志级别说明
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Verbose</b>: 最详细的日志,用于调试</item>
/// <item><b>Debug</b>: 调试信息</item>
/// <item><b>Information</b>: 常规信息 (推荐)</item>
/// <item><b>Warning</b>: 警告信息</item>
/// <item><b>Error</b>: 错误信息</item>
/// <item><b>Fatal</b>: 致命错误</item>
/// </list>
/// </remarks>
public static class LogLevels
{
    public const string Verbose = "Verbose";
    public const string Debug = "Debug";
    public const string Information = "Information";
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Fatal = "Fatal";
}
