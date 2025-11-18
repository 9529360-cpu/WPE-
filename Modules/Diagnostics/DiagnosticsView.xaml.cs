using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Diagnostics;

public partial class DiagnosticsView : UserControl
{
    private readonly ILogger _logger = LoggerFactory.CreateLogger<DiagnosticsView>();

    public DiagnosticsView()
    {
        InitializeComponent();
        _logger.Info("诊断视图已初始化");
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        try
        {
            _logger.Debug("刷新诊断信息");
            
            var sb = new StringBuilder();
            sb.AppendLine($"系统信息");
            sb.AppendLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"操作系统: {Environment.OSVersion}");
            sb.AppendLine($"机器名称: {Environment.MachineName}");
            sb.AppendLine($"用户名称: {Environment.UserName}");
            sb.AppendLine($"系统目录: {Environment.SystemDirectory}");
            sb.AppendLine($"处理器数: {Environment.ProcessorCount}");
            sb.AppendLine($"工作内存: {Environment.WorkingSet / 1024 / 1024} MB");
            sb.AppendLine();
            sb.AppendLine($"应用程序信息");
            sb.AppendLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"运行目录: {AppContext.BaseDirectory}");
            sb.AppendLine($"用户数据: {Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\币安量化机器人");
            sb.AppendLine($".NET版本: {Environment.Version}");
            sb.AppendLine($"运行时间: {TimeSpan.FromMilliseconds(Environment.TickCount64):dd\\:hh\\:mm\\:ss}");
            sb.AppendLine();
            sb.AppendLine($"配置信息");
            sb.AppendLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━");
            sb.AppendLine($"环境: {ServiceLocator.Settings.Environment}");
            sb.AppendLine($"自动重连: {ServiceLocator.Settings.AutoReconnect}");
            sb.AppendLine();
            
            // 添加日志目录信息
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "币安量化机器人",
                "Logs");
            
            if (Directory.Exists(logDir))
            {
                var logFiles = Directory.GetFiles(logDir, "*.log")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Take(5)
                    .ToList();
                
                sb.AppendLine($"日志文件 (最近5个)");
                sb.AppendLine($"━━━━━━━━━━━━━━━━━━━━━━━━━━");
                foreach (var file in logFiles)
                {
                    sb.AppendLine($"• {file.Name} ({file.Length / 1024} KB, {file.LastWriteTime:yyyy-MM-dd HH:mm})");
                }
            }
            
            EnvironmentText.Text = sb.ToString();

            // 读取最新的日志文件内容
            var latestLog = Path.Combine(logDir, $"{DateTime.Now:yyyy-MM-dd}.log");
            if (File.Exists(latestLog))
            {
                // 只读取最后1000行
                var lines = File.ReadLines(latestLog).Reverse().Take(1000).Reverse().ToList();
                LogBox.Text = string.Join(Environment.NewLine, lines);
                _logger.Debug($"已加载日志文件: {latestLog}, {lines.Count} 行");
            }
            else
            {
                LogBox.Text = $"今日日志文件尚未创建：{latestLog}\n\n" +
                             $"日志会在首次记录时自动创建。";
            }

            StatusText.Text = $"状态：诊断信息已刷新 ({DateTime.Now:HH:mm:ss})";
            _logger.Info("诊断信息刷新成功");
        }
        catch (Exception ex)
        {
            _logger.Error("刷新诊断信息失败", ex);
            StatusText.Text = "状态：刷新失败";
            MessageBox.Show($"刷新诊断信息时出错：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
