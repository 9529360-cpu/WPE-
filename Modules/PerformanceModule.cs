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
            var view = new PerformanceDashboardView();
            return Task.FromResult<UserControl>(view);
        }
    }
}
