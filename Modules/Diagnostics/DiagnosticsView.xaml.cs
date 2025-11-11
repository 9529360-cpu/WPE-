using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Diagnostics;

public partial class DiagnosticsView : UserControl
{
    private readonly DateTime _startTime = DateTime.Now;
    private readonly ObservableCollection<PerformanceMetric> _performanceMetrics = new();

    public DiagnosticsView()
    {
        InitializeComponent();

        PerformanceGrid.ItemsSource = _performanceMetrics;

        LoadDiagnosticInfo();
        InitializePerformanceMetrics();
        StartLogging();
    }

    private void LoadDiagnosticInfo()
    {
        // 应用信息
        VersionText.Text = "1.0.0";
        StartTimeText.Text = _startTime.ToString("yyyy-MM-dd HH:mm:ss");
        UpdateUptime();

        // 系统资源
        var process = Process.GetCurrentProcess();
        MemoryUsageText.Text = $"{process.WorkingSet64 / 1024 / 1024} MB";
        CpuUsageText.Text = "0%"; // TODO: 实际计算
        CacheCountText.Text = "0"; // TODO: 从缓存服务获取

        // API状态
        RestApiStatusText.Text = "已连接";
        WebSocketStatusText.Text = "已连接";
        RequestCountText.Text = "0";
    }

    private void UpdateUptime()
    {
        var uptime = DateTime.Now - _startTime;
        UptimeText.Text = $"{uptime.Days}天{uptime.Hours}小时{uptime.Minutes}分钟";
    }

    private void InitializePerformanceMetrics()
    {
        _performanceMetrics.Add(new PerformanceMetric
        {
            Name = "API响应时间",
            CurrentValue = "125",
            AverageValue = "150",
            MaxValue = "500",
            Unit = "ms"
        });

        _performanceMetrics.Add(new PerformanceMetric
        {
            Name = "订单执行延迟",
            CurrentValue = "50",
            AverageValue = "75",
            MaxValue = "200",
            Unit = "ms"
        });

        _performanceMetrics.Add(new PerformanceMetric
        {
            Name = "数据处理速度",
            CurrentValue = "1000",
            AverageValue = "950",
            MaxValue = "1500",
            Unit = "条/秒"
        });
    }

    private void StartLogging()
    {
        // 添加示例日志
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] [INFO] 系统启动成功\n");
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] [INFO] 正在连接Binance API...\n");
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] [INFO] API连接成功\n");
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadDiagnosticInfo();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] [INFO] 日志已清空\n");
    }

    private void LogLevelFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        // TODO: 实现日志筛选
    }
}

public class PerformanceMetric
{
    public string Name { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;
    public string AverageValue { get; set; } = string.Empty;
    public string MaxValue { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}
