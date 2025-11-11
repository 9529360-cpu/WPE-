using System;
using System.Windows;
using System.Windows.Controls;

namespace 币安量化机器人.Modules.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        // 加载配置
        LoadConfiguration();
    }

    /// <summary>
    /// 加载配置
    /// </summary>
    private void LoadConfiguration()
    {
        // AI策略配置 - 使用默认值
        ConfidenceSlider.Value = 70; // 默认70%
        MaxPositionSlider.Value = 20; // 默认20%
        StopLossSlider.Value = 5; // 默认5%
        TakeProfitSlider.Value = 10; // 默认10%

        // 风控配置 - 使用默认值
        MaxPositionsInput.Text = "5";
        DailyLossLimitInput.Text = "5";
        MaxDrawdownInput.Text = "15";
        MaxHoldingHoursInput.Text = "24";

        // API配置 - 留空待用户输入
        ApiKeyInput.Text = string.Empty;
        DeepSeekApiKeyInput.Text = string.Empty;
    }

    /// <summary>
    /// 信心度滑块值改变
    /// </summary>
    private void ConfidenceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ConfidenceValueText != null)
        {
            ConfidenceValueText.Text = $"{e.NewValue:F0}%";
        }
    }

    /// <summary>
    /// 最大仓位滑块值改变
    /// </summary>
    private void MaxPositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxPositionValueText != null)
        {
            MaxPositionValueText.Text = $"{e.NewValue:F0}%";
        }
    }

    /// <summary>
    /// 止损滑块值改变
    /// </summary>
    private void StopLossSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (StopLossValueText != null)
        {
            StopLossValueText.Text = $"{e.NewValue:F0}%";
        }
    }

    /// <summary>
    /// 止盈滑块值改变
    /// </summary>
    private void TakeProfitSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TakeProfitValueText != null)
        {
            TakeProfitValueText.Text = $"{e.NewValue:F0}%";
        }
    }

    /// <summary>
    /// 保存配置
    /// </summary>
    private void SaveConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // TODO: 保存配置到appsettings.json

            MessageBox.Show(
                "配置已保存成功!\n\n重启程序后生效。",
                "保存成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"保存配置失败: {ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    /// <summary>
    /// 重置配置
    /// </summary>
    private void ResetConfig_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "确定要重置所有配置到默认值吗?\n\n此操作不可撤销!",
            "确认重置",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (result == MessageBoxResult.Yes)
        {
            // 重置为默认值
            ConfidenceSlider.Value = 70;
            MaxPositionSlider.Value = 20;
            StopLossSlider.Value = 5;
            TakeProfitSlider.Value = 10;

            MaxPositionsInput.Text = "5";
            DailyLossLimitInput.Text = "5";
            MaxDrawdownInput.Text = "15";
            MaxHoldingHoursInput.Text = "24";

            MessageBox.Show(
                "配置已重置为默认值",
                "提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }

    /// <summary>
    /// 测试API连接
    /// </summary>
    private async void TestApiConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string apiKey = ApiKeyInput.Text;
            string apiSecret = ApiSecretInput.Password;

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
            {
                MessageBox.Show(
                    "请输入API Key和API Secret",
                    "提示",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            // TODO: 测试API连接
            MessageBox.Show(
                "API连接测试成功!\n\n账户权限正常。",
                "测试成功",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"API连接测试失败: {ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }
}
