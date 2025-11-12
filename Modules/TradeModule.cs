using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using 币安量化机器人.Modules.Trade;

namespace 币安量化机器人.Modules
{
    public class TradeModule : ModuleBase
    {
        public override string Id => "Trade";
        public override string Title => "下单交易";

        protected override Task<UserControl> CreateViewAsync(CancellationToken ct)
        {
            var view = new TradeView();
            return Task.FromResult<UserControl>(view);
        }
    }
}
