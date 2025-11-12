using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using 币安量化机器人.Modules.Settings;

namespace 币安量化机器人.Modules
{
    public class SettingsModule : ModuleBase
    {
        public override string Id => "Settings";
        public override string Title => "系统设置";

        protected override Task<UserControl> CreateViewAsync(CancellationToken ct)
        {
            var view = new SettingsView();
            return Task.FromResult<UserControl>(view);
        }
    }
}
