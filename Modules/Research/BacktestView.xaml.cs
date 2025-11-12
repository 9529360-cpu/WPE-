using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ScottPlot;
// using System.Drawing; // removed to avoid ambiguity with ScottPlot.Color
using 币安量化机器人.Services;
using 币安量化机器人.Application.Backtesting;
using 币安量化机器人.Core.Models;

namespace 币安量化机器人.Modules.Research;

public partial class BacktestView : UserControl
{
    public BacktestView()
    {
        InitializeComponent();
    }

    private void RenderEquity(double[] equity)
    {
        var ctrl = this.FindName("EquityPlot");
        if (ctrl is ScottPlot.WPF.WpfPlot wpfPlot)
        {
            var plt = wpfPlot.Plot;
            plt.Clear();

            if (equity == null || equity.Length == 0)
            {
                plt.Title("暂无回测数据");
                wpfPlot.Refresh();
                return;
            }

            var series = plt.Add.Signal(equity);
            series.Color = ScottPlot.Color.FromHex("#10B981");
            plt.Title("回测权益曲线");
            plt.YLabel("权益");
            plt.XLabel("样本");
            plt.Legend.IsVisible = true;
            wpfPlot.Refresh();
        }
    }

    private async void RunBacktest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            double initialCapital = double.Parse(InitialCapitalBox.Text);
            int fast = int.Parse(FastPeriodBox.Text);
            int slow = int.Parse(SlowPeriodBox.Text);
            string symbol = SymbolBox.SelectedItem as string ?? "BTCUSDT";

            var engine = ServiceLocator.EnhancedBacktest; // reuse singleton engine

            var strategy = new Core.Strategies.MeanReversionStrategy(
                ServiceLocator.Analyzer,
                ServiceLocator.MachineLearning,
                ServiceLocator.FeatureStore,
                new StrategyParameters(new System.Collections.Generic.Dictionary<string, double>())
            );

            var request = new BacktestRequest(
                Symbol: symbol,
                Start: DateTime.UtcNow.AddDays(-7),
                End: DateTime.UtcNow,
                Strategy: strategy
            );

            BacktestResult result = await engine.RunAsync(request);

            await Dispatcher.InvokeAsync(() => {
                ResultText.Text = $"策略: {result.Strategy} · 净利润: {result.NetProfit:F2} · 最大回撤: {result.MaxDrawdown:P2} · 胜率: {result.WinRate:P2}";
                // Render equity if available in result (currently not provided)
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "回测错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
