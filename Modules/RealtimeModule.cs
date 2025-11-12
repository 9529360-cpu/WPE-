using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using 币安量化机器人.Modules.Market;

namespace 币安量化机器人.Modules
{
    public class RealtimeModule : ModuleBase
    {
        public override string Id => "Realtime";
        public override string Title => "行情实时";

        protected override Task<UserControl> CreateViewAsync(CancellationToken ct)
        {
            var view = new RealtimeView();
            return Task.FromResult<UserControl>(view);
        }
    }
}
