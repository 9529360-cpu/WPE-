using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using 币安量化机器人.Modules.AI;

namespace 币安量化机器人.Modules
{
    /// <summary>
    /// 将现有 AICentralCoordinatorView 包装为 IModule（延迟实例化）
    /// </summary>
    public class AIModule : ModuleBase
    {
        public override string Id => "AI";
        public override string Title => "AI 智能体";

        protected override Task<UserControl> CreateViewAsync(CancellationToken ct)
        {
            var view = new AICentralCoordinatorView();
            return Task.FromResult<UserControl>(view);
        }
    }
}
