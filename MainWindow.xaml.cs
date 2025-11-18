using System;
using System.Windows;
using System.Windows.Controls;
using 币安量化机器人.Services;

namespace 币安量化机器人
{
    public partial class MainWindow : Window
    {
        private readonly ILogger _logger = LoggerFactory.CreateLogger<MainWindow>();
        private readonly IModuleRegistry _moduleRegistry;

        public MainWindow()
        {
            InitializeComponent();
            
            // 初始化模块注册表
            _moduleRegistry = new ModuleRegistry();
            
            SectionTitle.Text = "欢迎使用 · 请选择左侧功能";
            _logger.Info("主窗口已初始化");
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag)
            {
                GlobalExceptionHandler.SafeExecute(
                    () => LoadViewByTag(tag),
                    $"加载模块 {tag}");
            }
        }

        private void LoadViewByTag(string tag)
        {
            _logger.Debug($"正在加载模块: {tag}");
            
            SectionTitle.Text = $"当前模块：{tag}";
            StatusText.Text = $"状态：正在加载 {tag} 模块...";
            MainContentHost.Children.Clear();

            if (!_moduleRegistry.IsModuleRegistered(tag))
            {
                _logger.Warning($"模块未注册: {tag}");
                MainContentHost.Children.Add(MakePlaceholder(tag, "未注册模块"));
                StatusText.Text = $"状态：模块 {tag} 未注册";
                return;
            }

            var view = _moduleRegistry.CreateModule(tag);
            
            if (view == null)
            {
                _logger.Error($"创建模块失败: {tag}");
                MainContentHost.Children.Add(MakePlaceholder(tag, "模块创建失败"));
                StatusText.Text = $"状态：{tag} 模块加载失败";
                return;
            }

            MainContentHost.Children.Add(view);
            
            _logger.Info($"成功加载模块: {tag}");
            StatusText.Text = $"状态：已切换到 {tag} 模块";
        }

        private static UIElement MakePlaceholder(string tag, string extra)
        {
            return new TextBlock
            {
                Text = $"⚠️ 模块加载问题\n\n" +
                       $"模块名称：{tag}\n" +
                       $"详细信息：{extra}\n\n" +
                       $"建议：\n" +
                       $"• 检查模块是否已正确实现\n" +
                       $"• 查看日志文件获取详细错误信息\n" +
                       $"• 联系技术支持",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                FontSize = 14,
                Opacity = 0.85,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20)
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            _logger.Info("主窗口已关闭");
            base.OnClosed(e);
        }
    }
}
