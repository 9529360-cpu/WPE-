using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace 币安量化机器人.Modules
{
    public abstract class ModuleBase : IModule
    {
        public abstract string Id { get; }
        public abstract string Title { get; }

        private UserControl? _view;
        public UserControl? View => _view;
        public bool IsInitialized { get; private set; }

        /// <summary>
        /// 派生类实现实际 View 的创建（建议在 UI 线程创建 WPF 控件）。
        /// </summary>
        protected abstract Task<UserControl> CreateViewAsync(CancellationToken ct);

        public async Task InitializeAsync(CancellationToken ct = default)
        {
            if (IsInitialized)
            {
                return;
            }

            if (System.Windows.Application.Current?.Dispatcher == null)
            {
                _view = await CreateViewAsync(ct).ConfigureAwait(false);
            }
            else
            {
                // 在 UI 线程上同步调用 CreateViewAsync 并等待其完成
                var task = System.Windows.Application.Current.Dispatcher.Invoke(() => CreateViewAsync(ct));
                _view = await task.ConfigureAwait(false);
            }

            IsInitialized = true;
        }
    }
}
