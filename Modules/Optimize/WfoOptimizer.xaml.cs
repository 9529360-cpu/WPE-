using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace 币安量化机器人.Modules.Optimize;

public partial class WfoOptimizer : UserControl
{
    private readonly ObservableCollection<OptimizationResult> _results = new();

    public WfoOptimizer()
    {
        InitializeComponent();

        ResultsGrid.ItemsSource = _results;

        // 生成示例数据
        GenerateMockResults();
    }

    /// <summary>
    /// 生成模拟结果
    /// </summary>
    private void GenerateMockResults()
    {
        var random = new Random();

        for (int i = 0; i < 20; i++)
        {
            _results.Add(new OptimizationResult
            {
                Rank = i + 1,
                FastMa = 5 + i * 5,
                SlowMa = 80 + i * 20,
                SharpeRatio = 2.5 - i * 0.1 + random.NextDouble() * 0.2,
                AnnualReturn = 0.35 - i * 0.015 + random.NextDouble() * 0.05,
                MaxDrawdown = -(0.08 + i * 0.005 + random.NextDouble() * 0.02),
                WinRate = 0.6 - i * 0.01 + random.NextDouble() * 0.05,
                TotalTrades = 150 - i * 5
            });
        }

        // 更新最佳参数显示
        if (_results.Count > 0)
        {
            OptimizationResult best = _results[0];
            BestFastMaText.Text = best.FastMa.ToString();
            BestSlowMaText.Text = best.SlowMa.ToString();
            BestSharpeText.Text = best.SharpeRatio.ToString("F2");
            OosPerformanceText.Text = "稳定";
        }
    }

    /// <summary>
    /// 启动优化
    /// </summary>
    private void StartOptimization_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 验证输入
            if (!ValidateInputs())
            {
                return;
            }

            StatusText.Text = "📊 状态: 正在优化参数...";

            // TODO: 调用 WalkForwardOptimizer 进行实际优化

            MessageBox.Show(
                "参数优化已启动!\n\n这可能需要几分钟时间,请耐心等待...",
                "优化启动",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );

            StatusText.Text = "📊 状态: 优化完成";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"启动优化失败: {ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            StatusText.Text = "📊 状态: 优化失败";
        }
    }

    /// <summary>
    /// 验证输入
    /// </summary>
    private bool ValidateInputs()
    {
        // 验证窗口设置
        if (!int.TryParse(InSampleDaysInput.Text, out int inSampleDays) || inSampleDays <= 0)
        {
            MessageBox.Show("请输入有效的样本内天数!", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(OutSampleDaysInput.Text, out int outSampleDays) || outSampleDays <= 0)
        {
            MessageBox.Show("请输入有效的样本外天数!", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        // 验证参数范围
        if (!int.TryParse(FastMaMinInput.Text, out int fastMin) ||
            !int.TryParse(FastMaMaxInput.Text, out int fastMax) ||
            !int.TryParse(FastMaStepInput.Text, out int fastStep))
        {
            MessageBox.Show("请输入有效的快速MA参数!", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (fastMin >= fastMax || fastStep <= 0)
        {
            MessageBox.Show("快速MA参数设置不合理!", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }
}

/// <summary>
/// 优化结果项
/// </summary>
public class OptimizationResult
{
    public int Rank { get; set; }
    public int FastMa { get; set; }
    public int SlowMa { get; set; }
    public double SharpeRatio { get; set; }
    public double AnnualReturn { get; set; }
    public double MaxDrawdown { get; set; }
    public double WinRate { get; set; }
    public int TotalTrades { get; set; }
}
