using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace 币安量化机器人.Modules.AI
{
    public partial class ModelHub : UserControl
    {
        private readonly ObservableCollection<ModelRow> _all = new();
        private readonly ICollectionView _view;

        public ModelHub()
        {
            InitializeComponent();

            // 假数据：可跑；后续接 MLflow / 自建服务替换
            _all.Add(new ModelRow { Name = "SignalRanker", Version = 3, Stage = "Production", Metric = "F1=0.81", UpdatedAt = DateTime.Now.AddHours(-2).ToString("yyyy-MM-dd HH:mm") });
            _all.Add(new ModelRow { Name = "SignalRanker", Version = 4, Stage = "Staging", Metric = "F1=0.83", UpdatedAt = DateTime.Now.AddMinutes(-30).ToString("yyyy-MM-dd HH:mm") });
            _all.Add(new ModelRow { Name = "RiskScaler", Version = 1, Stage = "Draft", Metric = "RMSE=0.42", UpdatedAt = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd HH:mm") });

            _view = CollectionViewSource.GetDefaultView(_all);
            GridModels.ItemsSource = _view;

            GridModels.SelectionChanged += (_, __) => UpdateDetail();
            if (_all.Any()) GridModels.SelectedIndex = 0;
        }

        private void UpdateDetail()
        {
            if (GridModels.SelectedItem is ModelRow r)
            {
                DetailTitle.Text = $"{r.Name} v{r.Version} · {r.Stage}";
                DetailInfo.Text = $"主指标：{r.Metric}\n更新时间：{r.UpdatedAt}\n\n" +
                                   $"阶段说明：Draft→Staging→Production（归档为 Archived）。";
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
            StatusText.Text = "状态：已提交训练任务（占位）。";
            MessageBox.Show("已提交训练任务（占位）。后续接入训练服务/队列。", "训练任务", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            var name = (_all.FirstOrDefault()?.Name) ?? "NewModel";
            var latest = _all.Where(x => x.Name == name).Select(x => x.Version).DefaultIfEmpty(0).Max();
            var ver = latest + 1;
            _all.Add(new ModelRow { Name = name, Version = ver, Stage = "Draft", Metric = "N/A", UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
            _view.Refresh();
            StatusText.Text = $"状态：已注册 {name} v{ver}（Draft）。";
        }

        private void PromoteStaging_Click(object sender, RoutedEventArgs e)
        {
            if (GridModels.SelectedItem is not ModelRow r) return;
            r.Stage = "Staging";
            r.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            _view.Refresh();
            UpdateDetail();
            StatusText.Text = $"状态：{r.Name} v{r.Version} 提升到 Staging。";
        }

        private void PromoteProd_Click(object sender, RoutedEventArgs e)
        {
            if (GridModels.SelectedItem is not ModelRow r) return;

            // 同名模型仅允许一个 Production，其他 Production 先归档
            foreach (var x in _all.Where(x => x.Name == r.Name && x.Stage == "Production"))
                x.Stage = "Archived";

            r.Stage = "Production";
            r.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            _view.Refresh();
            UpdateDetail();
            StatusText.Text = $"状态：{r.Name} v{r.Version} 提升到 Production（旧 Prod 已归档）。";
        }

        private void Rollback_Click(object sender, RoutedEventArgs e)
        {
            if (GridModels.SelectedItem is not ModelRow r) return;
            var prev = _all.Where(x => x.Name == r.Name && x.Version < r.Version)
                           .OrderByDescending(x => x.Version).FirstOrDefault();
            if (prev == null)
            {
                MessageBox.Show("没有更早版本可回滚。", "回滚", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            r.Stage = "Archived";
            prev.Stage = "Production";
            prev.UpdatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            _view.Refresh();
            GridModels.SelectedItem = prev;
            UpdateDetail();
            StatusText.Text = $"状态：已回滚到 {prev.Name} v{prev.Version}（Production）。";
        }

        private void CreateAB_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("A/B 实验占位：后续接入流量分配与对照指标。", "A/B 实验", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void CreateCanary_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("金丝雀占位：小流量灰度，指标稳定再放量。", "金丝雀", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void CreateShadow_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Shadow 占位：新模型仅收请求不出结果，用于对齐与观测。", "Shadow", MessageBoxButton.OK, MessageBoxImage.Information);
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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
