using ScottPlot;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using 币安量化机器人.Services;

namespace 币安量化机器人.Modules.Optimize
{
    public partial class WfoOptimizer : UserControl
    {
        // 配置化参数：可通过UI或配置文件设置
        private const int DefaultRngSeed = -1; // -1 表示随机种子
        private const int MinIsLength = 30;
        private const int MinOosLength = 10;
        private const double BaselineP1 = 15.0;  // 快均线基准值
        private const double BaselineP2 = 120.0; // 慢均线基准值
        private const double P1Scale = 10.0;
        private const double P2Scale = 40.0;
        private const int MaxGridSize = 10000;  // 最大搜索空间限制
        
        private readonly Random _rng;
        private readonly DataTable _table = new();
        private readonly ILogger _logger = LoggerFactory.CreateLogger<WfoOptimizer>();
        private CancellationTokenSource? _cts;

        public WfoOptimizer()
        {
            InitializeComponent();
            
            // 根据配置决定使用固定种子还是随机种子
            int seed = Environment.TickCount;
            _rng = new Random(seed);

            // 摘要表结构
            _table.Columns.Add("窗口序号", typeof(int));
            _table.Columns.Add("IS期", typeof(string));
            _table.Columns.Add("OOS期", typeof(string));
            _table.Columns.Add("最优参数", typeof(string));
            _table.Columns.Add("目标值", typeof(double));
            GridSummary.ItemsSource = _table.DefaultView;

            // 先清空两张图
            StabPlot.Plot.Clear();
            EquityPlot.Plot.Clear();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            // 如果正在运行，先取消
            _cts?.Cancel();
            
            _table.Rows.Clear();
            StabPlot.Plot.Clear(); StabPlot.Refresh();
            EquityPlot.Plot.Clear(); EquityPlot.Refresh();
            StatusText.Text = "状态：已清空结果";
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            // 如果已经在运行，则取消当前运行
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                StatusText.Text = "状态：正在取消...";
                return;
            }

            // 更新按钮状态
            if (sender is Button btn)
            {
                btn.Content = "取消运行";
            }

            // 创建新的取消令牌
            _cts = new CancellationTokenSource();
            _ = RunOptimizationAsync(sender, _cts.Token);
        }

        private async Task RunOptimizationAsync(object sender, CancellationToken cancellationToken)
        {
            try
            {
                StatusText.Text = "状态：正在读取参数...";

                // 读取窗口与搜索空间
                int isLen = Math.Max(MinIsLength, int.Parse(TbIsLen.Text));
                int oosLen = Math.Max(MinOosLength, int.Parse(TbOosLen.Text));

                int p1Min = int.Parse(TbP1Min.Text);
                int p1Max = int.Parse(TbP1Max.Text);
                int p1Step = Math.Max(1, int.Parse(TbP1Step.Text));

                int p2Min = int.Parse(TbP2Min.Text);
                int p2Max = int.Parse(TbP2Max.Text);
                int p2Step = Math.Max(1, int.Parse(TbP2Step.Text));

                // 使用ConfigurationValidator进行全面验证
                var validator = new ConfigurationValidator();
                if (!validator.ValidateWfoParameters(
                    isLen, oosLen, p1Min, p1Max, p1Step, p2Min, p2Max, p2Step, MaxGridSize))
                {
                    _logger.Warning("WFO参数验证失败");
                    var errorMsg = validator.GetAllMessages();
                    MessageBox.Show(
                        $"参数验证失败：\n\n{errorMsg}\n\n请修正后再试。",
                        "参数错误",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // 显示警告（如果有）
                if (validator.Warnings.Count > 0)
                {
                    var warningMsg = validator.GetWarningMessages();
                    var result = MessageBox.Show(
                        $"参数验证通过，但有以下警告：\n\n{warningMsg}\n\n是否继续？",
                        "参数警告",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result != MessageBoxResult.Yes)
                    {
                        _logger.Info("用户取消优化（由于参数警告）");
                        return;
                    }
                }

                // 改进的网格生成：确保包含端点
                var p1Vals = GenerateGrid(p1Min, p1Max, p1Step);
                var p2Vals = GenerateGrid(p2Min, p2Max, p2Step);

                _logger.Info($"开始WFO优化: IS={isLen}, OOS={oosLen}, 网格={p1Vals.Length}×{p2Vals.Length}");
                StatusText.Text = $"状态：开始优化 ({p1Vals.Length}×{p2Vals.Length} 组合)...";

                // 在后台线程执行计算密集型操作
                var result = await Task.Run(() => PerformOptimization(isLen, oosLen, p1Vals, p2Vals, cancellationToken), cancellationToken);

                // 更新UI（必须在UI线程）
                _table.Rows.Clear();
                foreach (var row in result.TableRows)
                {
                    _table.Rows.Add(row.WindowIndex, row.IsText, row.OosText, row.BestParams, row.BestScore);
                }

                // 绘制图表
                PlotHeatmap(result.Heatmap, p1Vals, p2Vals);
                PlotEquityCurve(result.EquityCurve);

                _logger.Info($"WFO优化完成: {result.TableRows.Count} 个窗口");
                StatusText.Text = $"状态：已完成 {result.TableRows.Count} 个滚动窗口的优化。";
                Tabs.SelectedIndex = 0;
            }
            catch (OperationCanceledException)
            {
                _logger.Info("WFO优化被用户取消");
                StatusText.Text = "状态：优化已取消";
            }
            catch (Exception ex)
            {
                _logger.Error($"WFO优化失败: {ex.Message}", ex);
                StatusText.Text = "状态：运行出错";
                MessageBox.Show($"优化失败：{ex.Message}\n\n请检查参数设置。", "运行错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 恢复按钮状态
                if (sender is Button btn)
                {
                    btn.Content = "开始优化";
                }
                _cts?.Dispose();
                _cts = null;
            }
        }

        // 改进的网格生成方法：确保包含端点
        private static int[] GenerateGrid(int min, int max, int step)
        {
            var values = new List<int>();
            for (int val = min; val <= max; val += step)
            {
                values.Add(val);
            }
            // 确保包含最大值
            if (values.Count > 0 && values[^1] != max)
            {
                values.Add(max);
            }
            return values.ToArray();
        }

        private OptimizationResult PerformOptimization(int isLen, int oosLen, int[] p1Vals, int[] p2Vals, CancellationToken cancellationToken)
        {
            // —— 演示评分函数（近似"夏普"） —— //
            double Score(int fast, int slow)
            {
                double dx = (fast - BaselineP1) / P1Scale;
                double dy = (slow - BaselineP2) / P2Scale;
                double baseScore = Math.Exp(-(dx * dx + dy * dy));   // [0,1]
                double noise = _rng.NextDouble() * 0.15 - 0.075;
                return 0.8 * baseScore + noise;
            }

            // 演示滚动次数
            int runs = Math.Clamp((int)Math.Round(1.0 * isLen / oosLen) + 7, 8, 12);

            var tableRows = new List<OptimizationRow>();
            var equity = new List<double>();
            double eq = 1.0;

            // 累计稳定性热力图的平均分
            double[,] heat = new double[p1Vals.Length, p2Vals.Length];

            for (int k = 0; k < runs; k++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                double bestScore = double.NegativeInfinity;
                (int f, int s) best = (0, 0);

                for (int i = 0; i < p1Vals.Length; i++)
                {
                    for (int j = 0; j < p2Vals.Length; j++)
                    {
                        double sc = Score(p1Vals[i], p2Vals[j]);
                        heat[i, j] += sc;
                        if (sc > bestScore)
                        {
                            bestScore = sc;
                            best = (p1Vals[i], p2Vals[j]);
                        }
                    }
                }

                string isTxt = $"IS: {isLen} 天";
                string oosTxt = $"OOS: {oosLen} 天";
                
                tableRows.Add(new OptimizationRow
                {
                    WindowIndex = k + 1,
                    IsText = isTxt,
                    OosText = oosTxt,
                    BestParams = $"{best.f},{best.s}",
                    BestScore = Math.Round(bestScore, 3)
                });

                // 用 bestScore 合成一小段 OOS 权益并拼接
                int oosBars = oosLen / 3;
                for (int t = 0; t < oosBars; t++)
                {
                    double r = 0.001 + Math.Max(0, bestScore) * 0.005 + (_rng.NextDouble() - 0.5) * 0.002;
                    eq *= (1.0 + r);
                    equity.Add(eq);
                }
            }

            // —— 平均热力图 —— //
            for (int i = 0; i < p1Vals.Length; i++)
                for (int j = 0; j < p2Vals.Length; j++)
                    heat[i, j] /= runs;

            return new OptimizationResult
            {
                Heatmap = heat,
                EquityCurve = equity.ToArray(),
                TableRows = tableRows
            };
        }

        private void PlotHeatmap(double[,] heat, int[] p1Vals, int[] p2Vals)
        {
            StabPlot.Plot.Clear();

            // ScottPlot 5：直接添加热力图
            var hm = StabPlot.Plot.Add.Heatmap(heat);

            // 自定义坐标刻度（把索引映射为参数值）
            double[] xPos = Enumerable.Range(0, p2Vals.Length).Select(i => (double)i).ToArray();
            string[] xLbl = p2Vals.Select(v => v.ToString()).ToArray();
            StabPlot.Plot.Axes.Bottom.SetTicks(xPos, xLbl);   // X 对应 p2（慢均线）

            double[] yPos = Enumerable.Range(0, p1Vals.Length).Select(i => (double)i).ToArray();
            string[] yLbl = p1Vals.Select(v => v.ToString()).ToArray();
            StabPlot.Plot.Axes.Left.SetTicks(yPos, yLbl);     // Y 对应 p1（快均线）

            StabPlot.Plot.Title("参数稳定性热力图（数值越高越好）");
            StabPlot.Plot.Axes.Left.Label.Text = TbP1Name.Text;
            StabPlot.Plot.Axes.Bottom.Label.Text = TbP2Name.Text;
            StabPlot.Refresh();
        }

        private void PlotEquityCurve(double[] equity)
        {
            EquityPlot.Plot.Clear();
            double[] xs = Enumerable.Range(0, equity.Length).Select(i => (double)i).ToArray();
            EquityPlot.Plot.Add.Scatter(xs, equity);
            EquityPlot.Plot.Title("拼接 OOS 权益曲线（演示）");
            EquityPlot.Plot.Axes.Left.Label.Text = "权益";
            EquityPlot.Plot.Axes.Bottom.Label.Text = "样本外序列";
            EquityPlot.Refresh();
        }

        // 辅助类
        private class OptimizationResult
        {
            public double[,] Heatmap { get; set; } = new double[0, 0];
            public double[] EquityCurve { get; set; } = Array.Empty<double>();
            public List<OptimizationRow> TableRows { get; set; } = new();
        }

        private class OptimizationRow
        {
            public int WindowIndex { get; set; }
            public string IsText { get; set; } = "";
            public string OosText { get; set; } = "";
            public string BestParams { get; set; } = "";
            public double BestScore { get; set; }
        }
    }
}
