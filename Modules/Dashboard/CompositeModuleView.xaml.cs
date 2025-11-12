using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using 币安量化机器人.Services;
using 币安量化机器人.Modules;

namespace 币安量化机器人.Modules.Dashboard
{
    public partial class CompositeModuleView : UserControl
    {
        private readonly IReadOnlyList<IModule> _children;
        private readonly Dictionary<IModule, TabItem> _map = new();

        public CompositeModuleView(IEnumerable<IModule> children)
        {
            InitializeComponent();
            _children = children.ToArray();

            foreach (var m in _children)
            {
                var tab = new TabItem
                {
                    Header = m.Title,
                    Content = new TextBlock { Text = "准备加载…", Margin = new Thickness(8) },
                    Tag = m
                };
                TabHost.Items.Add(tab);
                _map[m] = tab;
            }

            if (TabHost.Items.Count > 0)
            {
                TabHost.SelectedIndex = 0;
            }

            this.Unloaded += CompositeModuleView_Unloaded;
        }

        private async void CompositeModuleView_Unloaded(object? sender, RoutedEventArgs e)
        {
            // Stop any started lifecycle views
            foreach (var m in _children)
            {
                if (m.IsInitialized && m.View is IModuleLifecycle lifecycle)
                {
                    try
                    {
                        await lifecycle.StopAsync();
                    }
                    catch (Exception ex)
                    {
                        LogService.Error(ex, "[CompositeModuleView] 子模块 StopAsync 失败");
                    }
                }
            }
        }

        private async void TabHost_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TabHost.SelectedItem is not TabItem ti)
            {
                return;
            }
            if (ti.Tag is not IModule module)
            {
                return;
            }

            if (!module.IsInitialized)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    await module.InitializeAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    ti.Content = new TextBlock { Text = $"初始化失败: {ex.Message}", Foreground = System.Windows.Media.Brushes.Red, Margin = new Thickness(8) };
                    LogService.Error(ex, "[CompositeModuleView] 子模块初始化失败");
                    return;
                }
            }

            if (module.View != null)
            {
                ti.Content = module.View;

                // If the view implements lifecycle, start it now
                if (module.View is IModuleLifecycle lifecycle)
                {
                    try
                    {
                        await lifecycle.StartAsync();
                    }
                    catch (Exception ex)
                    {
                        LogService.Error(ex, "[CompositeModuleView] 子模块 StartAsync 失败");
                    }
                }
            }
            else
            {
                ti.Content = new TextBlock { Text = "无可显示视图", Margin = new Thickness(8) };
            }
        }
    }
}
