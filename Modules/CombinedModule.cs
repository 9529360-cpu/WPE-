using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using 币安量化机器人.Modules.Dashboard;

namespace 币安量化机器人.Modules
{
    /// <summary>
    /// 把 AI/Realtime/Performance/Trade/Settings 五个模块打包成一个综合模块
    /// </summary>
    public class CombinedModule : ModuleBase
    {
        public override string Id => "Combined";
        public override string Title => "综合AI仪表盘";

        protected override Task<UserControl> CreateViewAsync(CancellationToken ct)
        {
            IModule[] children = new IModule[]
            {
                new AIModule(),
                new RealtimeModule(),
                new PerformanceModule(),
                new TradeModule(),
                new SettingsModule()
            };

            var view = new CompositeModuleView(children);
            return Task.FromResult<UserControl>(view);
        }
    }
}
