using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using 币安量化机器人.Services;
using 币安量化机器人.Services.AI;

namespace 币安量化机器人.Modules.Strategy
{
    public partial class TemplateHub : UserControl
    {
        public TemplateHub()
        {
            InitializeComponent();

            // 根据配置决定是否启用 AI 生成按钮
            try
            {
                var aiConfig = ConfigurationService.GetAIConfig();
                if (FindName("GenerateAIStrategyButton") is Button btn)
                {
                    btn.IsEnabled = aiConfig.EnableAITrading;
                }

                // 当配置更改时更新按钮状态
                ConfigurationService.ConfigurationChanged += () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (FindName("GenerateAIStrategyButton") is Button b)
                        {
                            b.IsEnabled = ConfigurationService.GetAIConfig().EnableAITrading;
                        }
                    });
                };
            }
            catch (Exception ex)
            {
                LogService.Error(ex, "[TemplateHub] 初始化 AI 按钮状态失败");
            }
        }

        // 点击任意模板卡片（XAML 在 Border 上绑定 MouseDown)
        private void Template_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string key)
            {
                MessageBox.Show(
                    $"你点击了模板：{key}\n\n下一步：打开“新建策略向导”，按所选模板生成参数表单与默认风控。",
                    "模板占位",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        // 使用 AI 生成策略草案并保存（根据复选框决定是否加入组合）
        private async void GenerateAIStrategy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 询问用户输入交易对（逗号分隔）
                var input = Microsoft.VisualBasic.Interaction.InputBox(
                    "请输入交易对（逗号分隔，例如：BTCUSDT,ETHUSDT）：",
                    "生成AI策略",
                    "BTCUSDT,ETHUSDT");
                if (string.IsNullOrWhiteSpace(input))
                {
                    return;
                }

                string[] symbols = input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < symbols.Length; i++)
                {
                    symbols[i] = symbols[i].Trim().ToUpperInvariant();
                }

                var suggestionService = new AIStrategySuggestionService(
                    ServiceLocator.AIStrategyGenerator,
                    ServiceLocator.StrategyTemplates,
                    ServiceLocator.StrategyPortfolio);

                // 从复选框读取保存选项
                bool addToPortfolio = true;
                bool saveAsTemplate = true;
                if (FindName("AiSaveOnlyCheck") is CheckBox cb)
                {
                    addToPortfolio = !cb.IsChecked.GetValueOrDefault(false);
                }

                // 等待游标
                Mouse.OverrideCursor = Cursors.Wait;

                // 生成并保存
                var inst = await suggestionService.GenerateAndStoreAsync(
                    symbols,
                    saveAsTemplate: saveAsTemplate,
                    addToPortfolio: addToPortfolio);

                // 恢复游标
                Mouse.OverrideCursor = null;

                // 提示保存位置，便于用户查找
                string baseDir = AppContext.BaseDirectory;
                string tplPath = System.IO.Path.Combine(baseDir, "Data", "strategy_templates.json");
                string stratPath = System.IO.Path.Combine(baseDir, "Data", "strategies.json");

                // 强制刷新磁盘与日志，便于排查
                try
                {
                    var templates = ServiceLocator.StrategyTemplates.LoadAll();
                    ServiceLocator.StrategyPortfolio.SaveToDisk();
                    LogService.Info("[TemplateHub] 模板数量: {Count}; 模板路径: {Tpl}; 策略路径: {Strats}", templates.Count, tplPath, stratPath);
                }
                catch (Exception exRefresh)
                {
                    LogService.Error(exRefresh, "[TemplateHub] 刷新模板/策略持久化时出错");
                }

                MessageBox.Show(
                    $"AI 已生成策略草案并保存：{inst.Name}\n类型: {inst.Type}\n交易对: {string.Join(',', inst.Symbols)}\n权重: {inst.Weight:P2}\n保存位置：\n模板: {tplPath}\n策略: {stratPath}\n（已保存为模板并{(addToPortfolio ? "添加到策略组合" : "仅保存为模板")}, 处于非运行状态）",
                    "生成完成",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Mouse.OverrideCursor = null;
                LogService.Error(ex, "[TemplateHub] 生成AI策略失败");
                MessageBox.Show($"生成失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
