using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Diagnostics;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($"运行目录: {AppContext.BaseDirectory}");
        sb.AppendLine($".NET: {Environment.Version}");
        sb.AppendLine($"环境: {ServiceLocator.Settings.Environment}");
        sb.AppendLine($"自动重连: {ServiceLocator.Settings.AutoReconnect}");
        EnvironmentText.Text = sb.ToString();

        var logPath = Path.Combine(AppContext.BaseDirectory, "Data", "terminal.log");
        if (File.Exists(logPath))
            LogBox.Text = File.ReadAllText(logPath);
        else
            LogBox.Text = "暂无日志文件（Data/terminal.log）";

        StatusText.Text = "状态：诊断信息已刷新";
    }
}
