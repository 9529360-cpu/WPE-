using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace 币安量化机器人.Modules
{
    /// <summary>
    /// 仪表盘可加载模块契约：延迟创建 View、异步初始化。
    /// </summary>
    public interface IModule
    {
        string Id { get; }
        string Title { get; }
        UserControl? View { get; }
        bool IsInitialized { get; }
        Task InitializeAsync(CancellationToken ct = default);
    }
}
