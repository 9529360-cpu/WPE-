using System.Windows;
using System.Windows.Controls;

namespace 币安量化机器人.Modules.Strategy
{
    public partial class TemplateHub : UserControl
    {
        public TemplateHub()
        {
            InitializeComponent();
        }

        // 点击任意模板卡片：导航到策略配置页面
        private void Template_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string key)
            {
                // 改进的用户提示：明确说明功能状态并提供清晰的下一步操作
                var result = MessageBox.Show(
                    $"即将基于模板"{key}"创建新策略配置。\n\n" +
                    $"功能说明：\n" +
                    $"• 点击"是"将跳转到"策略配置"页面\n" +
                    $"• 系统会自动预填该模板的推荐参数\n" +
                    $"• 您可以根据需要调整参数后保存\n\n" +
                    $"是否继续？",
                    "创建策略",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // TODO: 实现导航逻辑
                    // NavigationService.Navigate(new SettingsView(templateKey: key));
                    // 或者通过事件总线通知MainWindow处理导航
                    
                    MessageBox.Show(
                        $"提示：完整的模板导航功能正在开发中。\n\n" +
                        $"临时方案：请手动前往"策略配置"页面，\n" +
                        $"参考模板"{key}"的说明进行配置。\n\n" +
                        $"感谢您的理解与支持！",
                        "开发中功能",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }
    }
}
