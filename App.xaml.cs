using System.Windows;
using Microsoft.Extensions.Logging;
using Serilog;
using 币安量化机器人.Services;

namespace 币安量化机器人
{
    public partial class App : global::System.Windows.Application
    {
        public App()
        {
            // Initialize Serilog for basic file logging
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File("logs\\app.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            // Optional: redirect Microsoft.Extensions.Logging to Serilog
            var factory = LoggerFactory.Create(builder => builder.AddSerilog(Log.Logger, dispose: true));
            // Keep factory around if needed by other parts; ServiceLocator uses its own logging patterns currently.
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            await ServiceLocator.DisposeAsync();
            Log.CloseAndFlush();
        }
    }
}
