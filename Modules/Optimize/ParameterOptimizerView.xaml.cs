using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ScottPlot;
using 币安量化机器人.Services;
using GeneticOptimizationResult = 币安量化机器人.Services.OptimizationResult; // 使用别名避免冲突

namespace 币安量化机器人.Modules.Optimize;

/// <summary>
/// 策略参数优化器
/// </summary>
public partial class ParameterOptimizerView : UserControl
{
    private readonly GeneticAlgorithmOptimizer _geneticOptimizer;
    private readonly ObservableCollection<OptimizationResultRow> _results = new();
    private CancellationTokenSource? _cts;
    private bool _isRunning;

    public ParameterOptimizerView()
    {
        InitializeComponent();

        _geneticOptimizer = new GeneticAlgorithmOptimizer();
        ResultsGrid.ItemsSource = _results;
    }

    /// <summary>
    /// 优化方法切换
    /// </summary>
    private void Method_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (MethodCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            // 显示/隐藏遗传算法配置
            if (GeneticPanel != null)
            {
                GeneticPanel.Visibility = tag == "GA" ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    /// <summary>
    /// 开始优化
    /// </summary>
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            MessageBox.Show("优化正在进行中...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // 验证输入
            if (!ValidateInputs())
            {
                return;
            }

            _isRunning = true;
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            _results.Clear();

            StatusText.Text = "优化进行中...";
            LogService.Info("[ParameterOptimizer] 开始参数优化");

            _cts = new CancellationTokenSource();

            // 构建参数空间
            Dictionary<string, ParameterRange> parameterSpace = BuildParameterSpace();

            // 定义适应度函数 (这里使用模拟函数,实际应调用回测引擎)
            Func<Dictionary<string, double>, Task<double>> fitnessFunction = async (parameters) =>
            {
                await Task.Delay(100, _cts.Token); // 模拟回测耗时

                // 模拟夏普比率计算 (实际应运行回测)
                double fastMa = parameters["FastMA"];
                double slowMa = parameters["SlowMA"];
                double rsi = parameters["RSI"];

                // 简单的启发式评分 (仅用于演示)
                double score = 2.0;
                if (fastMa < slowMa * 0.3)
                {
                    score += 0.5;
                }

                if (rsi >= 10 && rsi <= 16)
                {
                    score += 0.3;
                }

                if (slowMa >= 100 && slowMa <= 150)
                {
                    score += 0.2;
                }

                return score + (new Random().NextDouble() - 0.5) * 0.5; // 添加随机性
            };

            // 创建进度报告器
            var progress = new Progress<OptimizationProgress>(UpdateProgress);

            // 运行优化
            int populationSize = int.Parse(PopulationSizeBox.Text);
            int generations = int.Parse(GenerationsBox.Text);
            double mutationRate = double.Parse(MutationRateBox.Text);

            GeneticOptimizationResult result = await _geneticOptimizer.OptimizeAsync(
                parameterSpace,
                fitnessFunction,
                populationSize,
                generations,
                mutationRate,
                eliteCount: 5,
                progress: progress);

            // 显示结果
            DisplayResults(result);
            RenderFitnessCurve(result);

            StatusText.Text = "优化完成！";
            MessageBox.Show("参数优化完成!", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "优化已取消";
            LogService.Info("[ParameterOptimizer] 优化已取消");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"优化失败: {ex.Message}";
            LogService.Error(ex, "[ParameterOptimizer] 优化失败");
            MessageBox.Show($"优化失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isRunning = false;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 停止优化
    /// </summary>
    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        StopButton.IsEnabled = false;
    }

    /// <summary>
    /// 构建参数空间
    /// </summary>
    private Dictionary<string, ParameterRange> BuildParameterSpace()
    {
        return new Dictionary<string, ParameterRange>
        {
            ["FastMA"] = new ParameterRange
            {
                Min = double.Parse(FastMaMinBox.Text),
                Max = double.Parse(FastMaMaxBox.Text),
                Step = double.Parse(FastMaStepBox.Text),
                IsInteger = true
            },
            ["SlowMA"] = new ParameterRange
            {
                Min = double.Parse(SlowMaMinBox.Text),
                Max = double.Parse(SlowMaMaxBox.Text),
                Step = double.Parse(SlowMaStepBox.Text),
                IsInteger = true
            },
            ["RSI"] = new ParameterRange
            {
                Min = double.Parse(RsiMinBox.Text),
                Max = double.Parse(RsiMaxBox.Text),
                Step = double.Parse(RsiStepBox.Text),
                IsInteger = true
            }
        };
    }

    /// <summary>
    /// 更新进度
    /// </summary>
    private void UpdateProgress(OptimizationProgress progress)
    {
        ProgressBar.Value = progress.ProgressPercent;
        ProgressText.Text = $"{progress.ProgressPercent:F0}%";
        ProgressDetail.Text = $"代数: {progress.CurrentGeneration}/{progress.TotalGenerations} | " +
                             $"最优适应度: {progress.BestFitness:F4} | " +
                             $"平均适应度: {progress.AverageFitness:F4}";
    }

    /// <summary>
    /// 显示结果
    /// </summary>
    private void DisplayResults(GeneticOptimizationResult result)
    {
        // 更新最优参数
        BestFastMaText.Text = result.BestParameters["FastMA"].ToString("F0");
        BestSlowMaText.Text = result.BestParameters["SlowMA"].ToString("F0");
        BestRsiText.Text = result.BestParameters["RSI"].ToString("F0");
        BestSharpeText.Text = result.BestFitness.ToString("F2");
        BestReturnText.Text = $"{(result.BestFitness * 0.15):P2}"; // 模拟收益率

        // 生成Top 20结果 (模拟数据)
        _results.Clear();
        var random = new Random(42);

        for (int i = 0; i < Math.Min(20, result.TotalEvaluations / 10); i++)
        {
            double fitness = result.BestFitness - i * 0.05 - random.NextDouble() * 0.1;

            _results.Add(new OptimizationResultRow
            {
                Rank = i + 1,
                FastMa = (int)result.BestParameters["FastMA"] + random.Next(-5, 6),
                SlowMa = (int)result.BestParameters["SlowMA"] + random.Next(-10, 11),
                Rsi = (int)result.BestParameters["RSI"] + random.Next(-2, 3),
                SharpeRatio = fitness.ToString("F2"),
                TotalReturn = $"{(fitness * 0.15):P2}",
                MaxDrawdown = $"{-(0.08 + random.NextDouble() * 0.05):P2}",
                WinRate = $"{(0.55 + random.NextDouble() * 0.1):P0}"
            });
        }
    }

    /// <summary>
    /// 渲染适应度曲线
    /// </summary>
    private void RenderFitnessCurve(GeneticOptimizationResult result)
    {
        try
        {
            // 安全地获取控件（支持占位 Border 或真实 ScottPlot.WpfPlot）
            var ctrl = this.FindName("FitnessPlot");
            if (ctrl is ScottPlot.WpfPlot wpfPlot)
            {
                var plt = wpfPlot.Plot;

                plt.Clear();

                if (!result.GenerationHistory.Any())
                {
                    plt.Title("暂无数据");
                    wpfPlot.Refresh();
                    return;
                }

                double[] generations = result.GenerationHistory.Select(g => (double)g.Generation).ToArray();
                double[] bestFitness = result.GenerationHistory.Select(g => g.BestFitness).ToArray();
                double[] avgFitness = result.GenerationHistory.Select(g => g.AverageFitness).ToArray();

                // 兼容 v4 API
                plt.AddScatter(generations, bestFitness, lineWidth: 3, color: System.Drawing.ColorTranslator.FromHtml("#10B981"));
                plt.AddScatter(generations, avgFitness, lineWidth: 2, color: System.Drawing.ColorTranslator.FromHtml("#3B82F6"));

                plt.Title("遗传算法进化曲线");
                plt.YLabel("适应度 (夏普比率)");
                plt.XLabel("代数");
                plt.Legend(true);
                wpfPlot.Refresh();
            }
            else
            {
                // placeholder: do nothing
            }
        }
        catch (Exception ex)
        {
            LogService.Error(ex, "[ParameterOptimizer] 渲染适应度曲线失败");
        }
    }

    /// <summary>
    /// 验证输入
    /// </summary>
    private bool ValidateInputs()
    {
        try
        {
            // 验证参数范围
            double fastMin = double.Parse(FastMaMinBox.Text);
            double fastMax = double.Parse(FastMaMaxBox.Text);
            double slowMin = double.Parse(SlowMaMinBox.Text);
            double slowMax = double.Parse(SlowMaMaxBox.Text);

            if (fastMin >= fastMax || slowMin >= slowMax)
            {
                MessageBox.Show("参数范围设置不合理！", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (fastMax >= slowMin)
            {
                MessageBox.Show("快速MA最大值应小于慢速MA最小值！", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            // 验证遗传算法参数
            int popSize = int.Parse(PopulationSizeBox.Text);
            int gens = int.Parse(GenerationsBox.Text);
            double mutRate = double.Parse(MutationRateBox.Text);

            if (popSize < 10 || popSize > 200)
            {
                MessageBox.Show("种群大小应在 10-200 之间！", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (gens < 10 || gens > 500)
            {
                MessageBox.Show("迭代代数应在 10-500 之间！", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (mutRate < 0 || mutRate > 1)
            {
                MessageBox.Show("变异率应在 0-1 之间！", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }
        catch (FormatException)
        {
            MessageBox.Show("请输入有效的数值！", "输入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}

/// <summary>
/// 优化结果行
/// </summary>
public class OptimizationResultRow
{
    public int Rank { get; set; }
    public int FastMa { get; set; }
    public int SlowMa { get; set; }
    public int Rsi { get; set; }
    public string SharpeRatio { get; set; } = string.Empty;
    public string TotalReturn { get; set; } = string.Empty;
    public string MaxDrawdown { get; set; } = string.Empty;
    public string WinRate { get; set; } = string.Empty;
}
