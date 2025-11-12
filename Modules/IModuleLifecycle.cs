using System.Threading.Tasks;

namespace 币安量化机器人.Modules
{
    public interface IModuleLifecycle
    {
        Task StartAsync();
        Task StopAsync();
    }
}
