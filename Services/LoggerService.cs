using System;
using System.IO;
using System.Text;

namespace 币安量化机器人.Services;

/// <summary>
/// 简单的结构化日志服务
/// Simple structured logging service
/// </summary>
public interface ILogger
{
    void Debug(string message, Exception? exception = null);
    void Info(string message, Exception? exception = null);
    void Warning(string message, Exception? exception = null);
    void Error(string message, Exception? exception = null);
    void Critical(string message, Exception? exception = null);
}

/// <summary>
/// 文件日志实现
/// File-based logger implementation
/// </summary>
public class FileLogger : ILogger
{
    private readonly string _logDirectory;
    private readonly string _component;
    private readonly object _lock = new();

    public FileLogger(string component)
    {
        _component = component;
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "币安量化机器人",
            "Logs");
        
        Directory.CreateDirectory(_logDirectory);
    }

    public void Debug(string message, Exception? exception = null) 
        => Log("DEBUG", message, exception);

    public void Info(string message, Exception? exception = null) 
        => Log("INFO", message, exception);

    public void Warning(string message, Exception? exception = null) 
        => Log("WARN", message, exception);

    public void Error(string message, Exception? exception = null) 
        => Log("ERROR", message, exception);

    public void Critical(string message, Exception? exception = null) 
        => Log("CRITICAL", message, exception);

    private void Log(string level, string message, Exception? exception)
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var logMessage = new StringBuilder();
            logMessage.AppendLine($"[{timestamp}] [{level}] [{_component}] {message}");
            
            if (exception != null)
            {
                logMessage.AppendLine($"Exception: {exception.GetType().Name}");
                logMessage.AppendLine($"Message: {exception.Message}");
                logMessage.AppendLine($"StackTrace: {exception.StackTrace}");
                
                if (exception.InnerException != null)
                {
                    logMessage.AppendLine($"Inner Exception: {exception.InnerException.Message}");
                }
            }

            var logFile = Path.Combine(_logDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");
            
            lock (_lock)
            {
                File.AppendAllText(logFile, logMessage.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // 日志失败不应该影响程序运行
            // Logging failures should not affect application
        }
    }
}

/// <summary>
/// 日志工厂
/// Logger factory
/// </summary>
public static class LoggerFactory
{
    public static ILogger CreateLogger(string component)
    {
        return new FileLogger(component);
    }

    public static ILogger CreateLogger<T>()
    {
        return new FileLogger(typeof(T).Name);
    }
}
