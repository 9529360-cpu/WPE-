using System.Windows;
using 币安量化机器人.Services;

namespace 币安量化机器人
{
    public partial class App : global::System.Windows.Application
    {
        protected override async void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            await ServiceLocator.DisposeAsync();
        }
    }
}
