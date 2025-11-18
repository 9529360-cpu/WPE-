using System;
using System.Windows;

namespace 币安量化机器人.Services;

/// <summary>
/// 全局异常处理服务
/// Global exception handling service
/// </summary>
public static class GlobalExceptionHandler
{
    private static readonly ILogger _logger = LoggerFactory.CreateLogger("GlobalExceptionHandler");
    private static bool _isInitialized = false;

    /// <summary>
    /// 初始化全局异常处理
    /// Initialize global exception handling
    /// </summary>
    public static void Initialize()
    {
        if (_isInitialized)
            return;

        _logger.Info("初始化全局异常处理器");

        // 捕获应用程序域中未处理的异常
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        
        // 捕获WPF UI线程中未处理的异常
        Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;

        _isInitialized = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            _logger.Critical("应用程序域未处理异常", ex);
            
            var message = BuildErrorMessage(
                "应用程序遇到严重错误",
                ex,
                "应用程序可能无法继续运行。建议立即保存工作。");

            var result = MessageBox.Show(
                message,
                "严重错误 - 应用程序可能崩溃",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // 如果异常是终止性的，给用户机会保存工作
            if (e.IsTerminating)
            {
                _logger.Critical("应用程序即将终止");
            }
        }
    }

    private static void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        _logger.Error("UI线程未处理异常", e.Exception);
        
        var message = BuildErrorMessage(
            "界面操作时发生错误",
            e.Exception,
            "应用程序已自动恢复，建议保存当前工作。");

        MessageBox.Show(
            message,
            "界面错误",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        // 标记为已处理，防止应用崩溃
        e.Handled = true;
        
        _logger.Info("UI异常已处理，应用继续运行");
    }

    private static string BuildErrorMessage(string title, Exception ex, string suggestion)
    {
        return $"{title}\n\n" +
               $"━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
               $"错误类型：{ex.GetType().Name}\n" +
               $"错误消息：{ex.Message}\n" +
               (ex.InnerException != null ? $"内部异常：{ex.InnerException.Message}\n" : "") +
               $"━━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
               $"{suggestion}\n\n" +
               $"详细信息已记录到日志文件：\n" +
               $"%AppData%\\币安量化机器人\\Logs\\{DateTime.Now:yyyy-MM-dd}.log";
    }

    /// <summary>
    /// 安全执行操作，捕获并记录异常
    /// Safely execute an action, catching and logging exceptions
    /// </summary>
    public static void SafeExecute(Action action, string operationName)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _logger.Error($"执行操作失败: {operationName}", ex);
            MessageBox.Show(
                $"操作失败：{operationName}\n\n{ex.Message}\n\n详细信息已记录到日志。",
                "操作错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// 安全执行操作并返回结果
    /// Safely execute an operation and return result
    /// </summary>
    public static T SafeExecute<T>(Func<T> func, string operationName, T defaultValue = default!)
    {
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            _logger.Error($"执行操作失败: {operationName}", ex);
            MessageBox.Show(
                $"操作失败：{operationName}\n\n{ex.Message}\n\n详细信息已记录到日志。",
                "操作错误",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return defaultValue;
        }
    }
}
