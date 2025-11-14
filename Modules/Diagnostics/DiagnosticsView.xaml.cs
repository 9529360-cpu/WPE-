using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using 币安量化机器人.Services;
using 币安量化机器人.Services.Performance;

using System.Runtime.Versioning;

namespace 币安量化机器人.Modules.Diagnostics;

[SupportedOSPlatform("windows")]
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
        try
        {
            // 应用信息
            VersionText.Text = "1.0.0";
            StartTimeText.Text = _startTime.ToString("yyyy-MM-dd HH:mm:ss");
            UpdateUptime();

            // 系统资源
            var process = Process.GetCurrentProcess();
            MemoryUsageText.Text = $"{process.WorkingSet64 / 1024 / 1024} MB";

            try
            {
                // 使用 SystemResourceMonitor 快照获取 CPU 使用率
                using var monitor = new SystemResourceMonitor();
                var snap = monitor.GetSnapshot();
                CpuUsageText.Text = snap.CpuUsagePercent >= 0 ? $"{snap.CpuUsagePercent:F1}%" : "N/A";
            }
            catch (Exception ex)
            {
                CpuUsageText.Text = "N/A";
                LogService.Warning("[DiagnosticsView] 获取 CPU 使用率失败: {0}", ex.Message);
            }

            // 缓存计数从 Cache 服务获取, 若不可用显示 N/A
            try
            {
                CacheCountText.Text = "N/A"; // DataCacheService 未提供估算计数接口, 显示 N/A
            }
            catch (Exception ex)
            {
                CacheCountText.Text = "N/A";
                LogService.Warning("[DiagnosticsView] 获取缓存计数失败: {0}", ex.Message);
            }

            // API状态 (尝试读取 Api 客户端健康信息)
            try
            {
                var api = ServiceLocator.Api;
                RestApiStatusText.Text = api != null ? "已初始化" : "未初始化";
                WebSocketStatusText.Text = ServiceLocator.Stream != null ? "已连接" : "未知";
                RequestCountText.Text = api != null ? $"SuccessRate: {api.ApiSuccessRate:P0}" : "N/A";
            }
            catch (Exception ex)
            {
                RestApiStatusText.Text = "未知";
                WebSocketStatusText.Text = "未知";
                RequestCountText.Text = "N/A";
                LogService.Warning("[DiagnosticsView] 读取 API 状态失败: {0}", ex.Message);
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[DiagnosticsView] 加载诊断信息失败");
        }
    }

    private void UpdateUptime()
    {
        TimeSpan uptime = DateTime.Now - _startTime;
        UptimeText.Text = $"{uptime.Days}天{uptime.Hours}小时{uptime.Minutes}分钟";
    }

    private void InitializePerformanceMetrics()
    {
        _performanceMetrics.Clear();

        _performanceMetrics.Add(new PerformanceMetric
        {
            Name = "API响应时间",
            CurrentValue = "N/A",
            AverageValue = "N/A",
            MaxValue = "N/A",
            Unit = "ms"
        });

        _performanceMetrics.Add(new PerformanceMetric
        {
            Name = "订单执行延迟",
            CurrentValue = "N/A",
            AverageValue = "N/A",
            MaxValue = "N/A",
            Unit = "ms"
        });

        _performance_metrics_additional();
    }

    // put any additional metric init in a small helper to keep method concise
    private void _performance_metrics_additional()
    {
        _performance_metrics_try_add(new PerformanceMetric
        {
            Name = "数据处理速度",
            CurrentValue = "N/A",
            AverageValue = "N/A",
            MaxValue = "N/A",
            Unit = "条/秒"
        });
    }

    private void _performance_metrics_try_add(PerformanceMetric m)
    {
        try
        {
            _performanceMetrics.Add(m);
        }
        catch (Exception ex)
        {
            LogService.Warning("[DiagnosticsView] 添加性能指标失败: {0}", ex.Message);
        }
    }

    private void StartLogging()
    {
        try
        {
            // 添加示例日志
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] [INFO] 系统启动成功\n");
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] [INFO] 正在连接Binance API...\n");
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] [INFO] API连接成功\n");
        }
        catch (Exception ex)
        {
            LogService.Warning("[DiagnosticsView] 启动日志写入失败: {0}", ex.Message);
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadDiagnosticInfo();
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LogTextBox.Clear();
            LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] [INFO] 日志已清空\n");
        }
        catch (Exception ex)
        {
            LogService.Warning("[DiagnosticsView] 清空日志失败: {0}", ex.Message);
        }
    }

    private void LogLevelFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        // 简单占位: 目前仅刷新显示
        LoadDiagnosticInfo();
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
