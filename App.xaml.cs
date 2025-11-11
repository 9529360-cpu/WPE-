using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Serilog.Events; // 🆕 Serilog引用
using 币安量化机器人.Services;

namespace 币安量化机器人
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 🆕 1. 首先初始化配置服务
            try
            {
                ConfigurationService.Initialize();
                var appInfo = ConfigurationService.GetAppInfo();
                var loggingConfig = ConfigurationService.GetLoggingConfig();

                // 🆕 2. 使用配置初始化Serilog日志系统
                LogService.Initialize(
                    minimumLevel: loggingConfig.MinimumLevel,
                    logFilePath: Path.Combine(AppContext.BaseDirectory, loggingConfig.FilePath)
                );

                // 🆕 3. 记录应用信息
                LogService.Info("=== {AppName} 启动 ===", appInfo.Name);
                LogService.Info("版本: {Version}", appInfo.Version);
                LogService.Info("环境: {Environment}", appInfo.Environment);
                LogService.Info(".NET版本: {Runtime}", Environment.Version);
            }
            catch (Exception ex)
            {
                // 配置加载失败,使用默认配置
                LogService.Initialize(
                    minimumLevel: LogEventLevel.Information,
                    logFilePath: Path.Combine(AppContext.BaseDirectory, "Logs", "app-.log")
                );

                LogService.Error(ex, "配置文件加载失败,使用默认配置");
                LogService.Info("=== 币安量化机器人启动 ===");
                LogService.Info("版本: 1.0.0 (默认)");
                LogService.Info(".NET版本: {Runtime}", Environment.Version);
            }

            // 保留原有诊断日志(向后兼容)
            StartupDiagnostics.Init();
            StartupDiagnostics.Log("App.OnStartup");

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            StartupDiagnostics.Log("App.OnExit");

            // 🆕 记录退出日志
            LogService.Info("应用程序正在退出...");

            base.OnExit(e);
            await ServiceLocator.DisposeAsync();

            // 🆕 关闭日志系统
            LogService.Shutdown();
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            StartupDiagnostics.Log($"DispatcherUnhandledException: {e.Exception}");

            // 🆕 记录异常到Serilog
            LogService.Error(e.Exception, "UI线程未处理异常");

            MessageBox.Show(
                $"发生错误:\n{e.Exception.Message}\n\n详细信息已记录到日志文件。",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            e.Handled = true;
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            StartupDiagnostics.Log($"UnhandledException: {e.ExceptionObject}");

            // 🆕 记录致命异常
            if (e.ExceptionObject is Exception ex)
            {
                LogService.Fatal(ex, "应用程序域未处理异常");
            }
            else
            {
                LogService.Fatal("未知类型的未处理异常: {ExceptionObject}", e.ExceptionObject);
            }
        }

        private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            StartupDiagnostics.Log($"UnobservedTaskException: {e.Exception}");

            // 🆕 记录Task异常
            LogService.Error(e.Exception, "未观察到的Task异常");

            e.SetObserved();
        }
    }

    internal static class StartupDiagnostics
    {
        private static readonly object _sync = new();
        private static string _logFile = string.Empty;

        public static void Init()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Data");
            Directory.CreateDirectory(dir);
            _logFile = Path.Combine(dir, "startup.log");
            Log("Diagnostics initialized");
        }

        public static void Log(string message)
        {
            lock (_sync)
            {
                string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
                File.AppendAllText(_logFile, line + Environment.NewLine);
            }
        }
    }
}
