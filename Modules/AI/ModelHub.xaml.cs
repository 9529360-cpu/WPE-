using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using 币安量化机器人.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace 币安量化机器人.Modules.AI
{
    public partial class ModelHub : UserControl
    {
        private readonly ObservableCollection<ModelRow> _all = new();
        private readonly ICollectionView _view;
        private readonly AiForecastService _aiService = ServiceLocator.Ai;

        public ModelHub()
        {
            InitializeComponent();

            foreach (var artifact in _aiService.Models)
            {
                _all.Add(new ModelRow
                {
                    Name = artifact.Name,
                    Version = Version.Parse(artifact.Version).Major,
                    Stage = artifact.Stage,
                    Metric = artifact.Metric,
                    UpdatedAt = artifact.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
                    Description = artifact.Description
                });
            }

            _view = CollectionViewSource.GetDefaultView(_all);
            GridModels.ItemsSource = _view;

            GridModels.SelectionChanged += (_, __) => UpdateDetail();
            if (_all.Any()) GridModels.SelectedIndex = 0;
            StatusText.Text = $"状态：已加载 {_all.Count} 个上线模型";
        }

        private void UpdateDetail()
        {
            if (GridModels.SelectedItem is ModelRow r)
            {
                DetailTitle.Text = $"{r.Name} v{r.Version} · {r.Stage}";
                DetailInfo.Text = $"主指标：{r.Metric}\n更新时间：{r.UpdatedAt}\n\n{r.Description}";
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var kw = SearchBox.Text?.Trim() ?? "";
            _view.Filter = o =>
            {
                if (o is not ModelRow r) return false;
                return string.IsNullOrEmpty(kw)
                       || r.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)
                       || r.Stage.Contains(kw, StringComparison.OrdinalIgnoreCase);
            };
            _view.Refresh();
        }

        private void Train_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "提示：模型训练功能";
            MessageBox.Show(
                "📊 模型训练说明\n\n" +
                "模型训练由离线管道执行，此处仅展示线上模型状态。\n\n" +
                "操作指引：\n" +
                "• 训练任务请在 MLFlow/CI/CD 中触发\n" +
                "• 训练完成后模型会自动同步到此列表\n" +
                "• 建议使用独立的训练环境以避免影响实盘\n\n" +
                "如需集成在线训练功能，请联系开发团队。",
                "模型训练",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "📝 模型注册说明\n\n" +
                "模型注册由自动化部署完成。\n\n" +
                "工作流程：\n" +
                "• 训练完成→自动评估→符合标准→自动注册\n" +
                "• 注册后的模型会显示在上方列表中\n" +
                "• 此界面用于查看和管理已注册的模型\n\n" +
                "手动注册功能规划中。",
                "模型注册",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void PromoteStaging_Click(object sender, RoutedEventArgs e)
        {
            if (GridModels.SelectedItem == null)
            {
                MessageBox.Show("请先选择要操作的模型。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "⚠️ 模型阶段提升\n\n" +
                "将选中的模型提升到 Staging 阶段。\n\n" +
                "说明：\n" +
                "• Staging 模型会用于预生产测试\n" +
                "• 建议先完成充分的离线验证\n" +
                "• 此操作需要 Release 系统权限\n\n" +
                "完整的阶段管理功能开发中。\n是否了解操作流程？",
                "模型阶段",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                MessageBox.Show(
                    "请在部署中心的 Release 系统中执行模型阶段提升。\n\n" +
                    "系统会在后续版本中支持一键操作。",
                    "操作指引",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void PromoteProd_Click(object sender, RoutedEventArgs e)
        {
            if (GridModels.SelectedItem == null)
            {
                MessageBox.Show("请先选择要操作的模型。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                "🚀 生产环境上线\n\n" +
                "⚠️ 重要操作警告 ⚠️\n\n" +
                "将选中的模型切换到 Production。\n\n" +
                "上线前检查清单：\n" +
                "✓ Staging 环境已充分测试\n" +
                "✓ 性能指标达标\n" +
                "✓ A/B 测试结果良好\n" +
                "✓ 回滚方案已准备\n\n" +
                "建议：请在部署中心执行 Production 切换，\n" +
                "并设置流量灰度以降低风险。\n\n" +
                "是否继续？",
                "模型上线",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                MessageBox.Show(
                    "请前往部署中心执行生产环境切换。\n\n" +
                    "建议配置：\n" +
                    "• 初始流量：5-10%\n" +
                    "• 观察期：24-48小时\n" +
                    "• 监控关键指标\n" +
                    "• 准备好回滚预案",
                    "部署指引",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void Rollback_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "⏪ 模型回滚\n\n" +
                "回滚到上一个稳定版本。\n\n" +
                "说明：\n" +
                "• 回滚操作会立即生效\n" +
                "• 建议先确认目标版本\n" +
                "• 回滚后请验证系统状态\n\n" +
                "完整的回滚功能开发中。\n是否了解回滚流程？",
                "回滚",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                MessageBox.Show(
                    "请在部署流水线中执行回滚操作。\n\n" +
                    "后续版本将支持一键回滚。",
                    "操作指引",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void CreateAB_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "🔬 A/B 实验\n\n" +
                "创建A/B实验以对比不同模型版本的效果。\n\n" +
                "实验配置：\n" +
                "• 对照组（A）：当前生产模型\n" +
                "• 实验组（B）：新版本模型\n" +
                "• 流量分配：可自定义比例\n" +
                "• 关键指标：自动收集和对比\n\n" +
                "功能开发中：将接入流量分配与对照指标系统。\n" +
                "预计下版本发布。",
                "A/B 实验",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void CreateCanary_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "🐤 金丝雀发布\n\n" +
                "使用金丝雀策略进行小流量灰度。\n\n" +
                "发布流程：\n" +
                "• 初始流量：1-5%\n" +
                "• 观察指标稳定后逐步放量\n" +
                "• 10% → 25% → 50% → 100%\n" +
                "• 任何异常立即回滚\n\n" +
                "功能开发中：将支持自动化灰度控制。\n" +
                "手动灰度可在部署中心配置。",
                "金丝雀发布",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void CreateShadow_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "👥 Shadow 模式\n\n" +
                "新模型以Shadow模式运行：\n\n" +
                "工作原理：\n" +
                "• 接收真实流量的副本\n" +
                "• 模型进行预测但不影响线上结果\n" +
                "• 用于验证模型行为和性能\n" +
                "• 观察延迟、错误率等指标\n\n" +
                "适用场景：\n" +
                "• 新模型上线前的最后验证\n" +
                "• 生产环境真实数据测试\n\n" +
                "功能开发中：将支持Shadow流量镜像。",
                "Shadow 模式",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    public class ModelRow : INotifyPropertyChanged
    {
        private string _stage = "Draft";

        public string Name { get; set; } = "";
        public int Version { get; set; }
        public string Stage
        {
            get => _stage;
            set { _stage = value; OnPropertyChanged(nameof(Stage)); }
        }
        public string Metric { get; set; } = "N/A";
        public string UpdatedAt { get; set; } = "";
        public string Description { get; set; } = "";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
