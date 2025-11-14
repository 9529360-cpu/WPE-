using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using 币安量化机器人.Modules.Performance;

namespace 币安量化机器人.Modules
{
    public class PerformanceModule : ModuleBase
    {
        public override string Id => "Performance";
        public override string Title => "绩效分析";

        protected override Task<UserControl> CreateViewAsync(CancellationToken ct)
        {
            if (OperatingSystem.IsWindows())
            {
                var view = new PerformanceDashboardView();
                return Task.FromResult<UserControl>(view);
            }

            // Non-Windows fallback: simple placeholder view
            var placeholder = new UserControl
            {
                Content = new TextBlock
                {
                    Text = "Performance dashboard is only available on Windows.",
                    TextWrapping = System.Windows.TextWrapping.Wrap,
                    Margin = new System.Windows.Thickness(12)
                }
            };

            return Task.FromResult<UserControl>(placeholder);
        }
    }
}
