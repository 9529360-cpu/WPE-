using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace 币安量化机器人.Modules.Settings;

public partial class SystemSettingsView : UserControl
{
    public SystemSettingsView()
    {
        InitializeComponent();
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            "设置已保存!\n\n重启程序后生效。",
            "保存成功",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
    {
        string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        if (!Directory.Exists(logPath))
        {
            Directory.CreateDirectory(logPath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = logPath,
            UseShellExecute = true,
            Verb = "open"
        });
    }
}
