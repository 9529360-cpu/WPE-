using System.Windows.Controls;
using 币安量化机器人.Services.Observability;

namespace 币安量化机器人.Modules.Observability
{
    public partial class ObservabilityView : UserControl
    {
        public ObservabilityView()
        {
            InitializeComponent();
            if (!System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                var svc = ObservabilityService.Instance;
                foreach (var l in svc.GetRecentLogs(200))
                {
                    LogsList.Items.Add(l);
                }

                svc.LogAppended += (s) =>
                {
                    _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        LogsList.Items.Insert(0, s);
                        if (LogsList.Items.Count > 500) LogsList.Items.RemoveAt(LogsList.Items.Count - 1);
                    });
                };

                ExportBtn.Click += (s, e) =>
                {
                    var path = svc.ExportToFile();
                    System.Windows.MessageBox.Show($"日志导出：{path}");
                };
            }
        }
    }
}
