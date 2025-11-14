using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events; // 🆕 Serilog引用
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;
using 币安量化机器人.Services.Observability;
using 币安量化机器人.Services.Resilience;
using 币安量化机器人.Services.Performance;
using 币安量化机器人.Core;
using 币安量化机器人.Persistence;

namespace 币安量化机器人
{
    public partial class App : System.Windows.Application
    {
        private IHost? _host;

        public static IServiceProvider ServiceProvider { get; private set; }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 单实例保护
            bool createdNew = false;
            string mutexName = "Global\\AlphaArena_9529360_cpu"; // 程序唯一名称
            try
            {
                var mutex = new System.Threading.Mutex(true, mutexName, out createdNew);
                if (!createdNew)
                {
                    MessageBox.Show("应用已在运行，不能启动多个实例。若要强制启动，请先关闭现有实例。", "已运行", MessageBoxButton.OK, MessageBoxImage.Information);
                    Environment.Exit(0);
                    return;
                }
                // 将mutex存储在App属性中，确保其生命周期与应用一致
                this.Properties["AppMutex"] = mutex;
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "创建单实例 Mutex 失败");
            }

            // 初始化配置与日志（保持原有逻辑）
            try
            {
                ConfigurationService.Initialize();
                AppInfo appInfo = ConfigurationService.GetAppInfo();
                LoggingConfig loggingConfig = ConfigurationService.GetLoggingConfig();

                LogService.Initialize(
                    minimumLevel: loggingConfig.MinimumLevel,
                    logFilePath: Path.Combine(AppContext.BaseDirectory, loggingConfig.FilePath)
                );

                LogService.Info("=== {AppName} 启动 ===", appInfo.Name);
                LogService.Info("版本: {Version}", appInfo.Version);
                LogService.Info("环境: {Environment}", appInfo.Environment);
                LogService.Info(".NET版本: {Runtime}", Environment.Version);
            }
            catch (Exception ex)
            {
                // 配置加载失败, 使用默认配置
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

            // 🆕 使用 Generic Host + DI 启动应用
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // 将 Serilog 与 Microsoft.Extensions.Logging 集成
                    services.AddLogging(builder => builder.AddSerilog(dispose: false));

                    // 注意: 项目中存在一个静态的 LogService (Serilog 封装), 不要将其当作实例类型注册

                    // 仅注册实际存在的具体服务类型，按需后续再调整为接口映射
                    services.AddSingleton<DataCacheService>();

                    services.AddSingleton<BinanceApiClient>();
                    services.AddSingleton<BinanceStreamClient>();
                    services.AddSingleton<ApiHealthMonitor>();

                    services.AddSingleton<ResilienceService>();
                    services.AddSingleton<AutoRecoveryManager>();

                    services.AddSingleton<ObservabilityService>();
                    services.AddSingleton<MetricsCollector>();
                    services.AddSingleton<StructuredLogger>();

                    services.AddSingleton<SmartCacheManager>();
                    services.AddSingleton<PerformanceMonitor>();
                    services.AddSingleton<PerformanceOptimizationService>();

                    // AI 与策略相关（具体实现类）
                    services.AddSingleton<AIStrategySuggestionService>();
                    services.AddSingleton<AIStrategyGenerator>();
                    services.AddSingleton<AICentralCoordinator>();
                    services.AddSingleton<WorkflowEngine>();

                    // 交易网关（接口实现）
                    services.AddSingleton<ITradeGate, GlobalTradeGate>();

                    services.AddSingleton<AutoTradingController>();
                    services.AddSingleton<LiveOrderExecutor>();
                    services.AddSingleton<OrderHistoryService>();

                    services.AddSingleton<StrategyFactory>();
                    services.AddSingleton<StrategyPortfolioManager>();
                    services.AddSingleton<StrategyTemplateLibrary>();

                    // 注册主窗口（其他窗口按需延迟解析）
                    services.AddSingleton<MainWindow>();

                    // 注册核心服务
                    services.AddSingleton<Core.IEventBus, EventBus>();
                    services.AddSingleton<Core.IMarketDataService, MarketDataService>();
                    services.AddSingleton<Core.IOrderExecutionService, OrderExecutionService>();
                    services.AddSingleton<Persistence.IRepository, LiteDbRepository>();
                    services.AddSingleton<Core.IRiskManager, RiskManager>();
                    services.AddSingleton<StrategyHost>();

                    // ViewModels
                    services.AddSingleton<ViewModels.MainWindowViewModel>();
                    services.AddSingleton<ViewModels.StrategyManagerViewModel>();
                    services.AddSingleton<ViewModels.OrderExecutionViewModel>();
                })
                .Build();

            try
            {
                await _host.StartAsync();

                // 从容器中解析 MainWindow 并显示
                var main = _host.Services.GetRequiredService<MainWindow>();
                main.Show();
            }
            catch (Exception ex)
            {
                LogService.Fatal(ex, "Host 启动失败");
                throw;
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            StartupDiagnostics.Log("App.OnExit");

            // 🆕 记录退出日志
            LogService.Info("应用程序正在退出...");

            base.OnExit(e);

            if (_host != null)
            {
                try
                {
                    await _host.StopAsync();
                }
                catch (Exception ex)
                {
                    LogService.Error(ex, "停止 Host 出现错误");
                }
                finally
                {
                    _host.Dispose();
                    _host = null;
                }
            }

            // 释放 ServiceLocator（如果存在未释放的资源）
            try
            {
                await ServiceLocator.DisposeAsync();
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "ServiceLocator 释放时出错");
            }

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
