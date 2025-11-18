using System.Windows;
using 币安量化机器人.Services;

namespace 币安量化机器人
{
    public partial class App : Application
    {
        private readonly ILogger _logger = LoggerFactory.CreateLogger("Application");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // 初始化全局异常处理
            GlobalExceptionHandler.Initialize();
            
            _logger.Info("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            _logger.Info("应用程序启动");
            _logger.Info($"版本: 1.0.0");
            _logger.Info($"环境: {ServiceLocator.Settings.Environment}");
            _logger.Info("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            _logger.Info("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            _logger.Info($"应用程序退出，退出码: {e.ApplicationExitCode}");
            _logger.Info("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            
            base.OnExit(e);
            await ServiceLocator.DisposeAsync();
        }
    }
}
