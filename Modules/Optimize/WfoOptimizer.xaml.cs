using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using 币安量化机器人.Core.Models;
using 币安量化机器人.Services;
using BacktestResultModel = 币安量化机器人.Core.Models.BacktestResult;
using ProgressModel = 币安量化机器人.Core.Models.OptimizationProgress;
using WFOptimizationResult = 币安量化机器人.Core.Models.OptimizationResult;

namespace 币安量化机器人.Modules.Optimize;

public partial class WfoOptimizer : UserControl
{
    private readonly ObservableCollection<OptimizationResult> _results = new();

    public WfoOptimizer()
    {
        InitializeComponent();

        ResultsGrid.ItemsSource = _results;

        // 不再在构造中使用模拟数据，UI 将在 StartOptimization_Click 中展示真实优化结果
    }

    /// <summary>
    /// 启动优化
    /// </summary>
    private async void StartOptimization_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ValidateInputs())
            {
                return;
            }

            StatusText.Text = "状态: 正在优化参数...";

            // 参数空间
            int fastMin = int.Parse(FastMaMinInput.Text);
            int fastMax = int.Parse(FastMaMaxInput.Text);
            int fastStep = int.Parse(FastMaStepInput.Text);

            var fastValues = Enumerable.Range(0, (fastMax - fastMin) / fastStep + 1)
                .Select(i => (double)(fastMin + i * fastStep))
                .ToArray();

            var parameterSpace = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.IReadOnlyList<double>>
            {
                ["ma_fast"] = fastValues,
                ["ma_slow"] = new double[] { 55, 89, 144 }
            };

            DateTime end = DateTime.UtcNow;
            int inSampleDays = int.Parse(InSampleDaysInput.Text);
            int outSampleDays = int.Parse(OutSampleDaysInput.Text);
            DateTime start = end.AddDays(-(inSampleDays + outSampleDays) * 4);

            var optimizer = ServiceLocator.WalkForward;
            var momentumParams = new StrategyParameters(new System.Collections.Generic.Dictionary<string, double>
            {
                ["momentum_window"] = 5,
                ["enter_threshold"] = 0.5,
                ["base_quantity"] = 1
            });
            var strategy = new 币安量化机器人.Core.Strategies.MomentumStrategy(ServiceLocator.Analyzer, momentumParams);

            _results.Clear();

            await foreach (var wf in optimizer.OptimizeAsync(
                strategy,
                symbol: "BTCUSDT",
                start: start,
                end: end,
                trainingWindow: TimeSpan.FromDays(inSampleDays),
                testingWindow: TimeSpan.FromDays(outSampleDays),
                parameterSpace: parameterSpace))
            {
                if (wf.Progress is ProgressModel prog)
                {
                    StatusText.Text = $"进度: {prog.Completed}/{prog.Total} - score={prog.Score:F2}";
                }
                if (wf.Optimization is WFOptimizationResult opt)
                {
                    var m = opt.Metrics;
                    _results.Add(new OptimizationResult
                    {
                        Rank = _results.Count + 1,
                        FastMa = (int)opt.Parameters.Get("ma_fast", 21),
                        SlowMa = (int)opt.Parameters.Get("ma_slow", 55),
                        SharpeRatio = m.Sharpe,
                        AnnualReturn = m.NetProfit, // 简化替代
                        MaxDrawdown = m.MaxDrawdown,
                        WinRate = m.WinRate,
                        TotalTrades = opt.Candidates.Count
                    });
                }

                if (wf.WalkForwardTestResult is BacktestResultModel test)
                {
                    OosPerformanceText.Text = $"样本外夏普: {test.Sharpe:F2}  回撤: {test.MaxDrawdown:P2}";
                }
            }

            if (_results.Count > 0)
            {
                var best = _results.OrderByDescending(r => r.SharpeRatio).First();
                BestFastMaText.Text = best.FastMa.ToString();
                BestSlowMaText.Text = best.SlowMa.ToString();
                BestSharpeText.Text = best.SharpeRatio.ToString("F2");
            }

            StatusText.Text = "状态: 优化完成";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"启动优化失败: {ex.Message}",
                "错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            StatusText.Text = "状态: 优化失败";
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
