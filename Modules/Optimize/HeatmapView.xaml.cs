using System.Runtime.Versioning;
using System.Windows.Controls;
using ScottPlot;

// using ScottPlot.Plottable; // removed for v5 migration compatibility
// using ScottPlot.Colormaps; // removed to avoid type resolution issues

namespace 币安量化机器人.Modules.Optimize
{
    [SupportedOSPlatform("windows")]
    public partial class HeatmapView : UserControl
    {
        public HeatmapView()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                if (this.FindName("Plot") is ScottPlot.WPF.WpfPlot plot)
                {
                    var plt = plot.Plot;
                    double[,] z = new double[20, 20];
                    for (int i = 0; i < 20; i++)
                    {
                        for (int j = 0; j < 20; j++)
                        {
                            z[i, j] = 0.6 + 0.4 * System.Math.Sin(i * .2) * System.Math.Cos(j * .15);
                        }
                    }

                    plt.Clear();
                    var hm = plt.Add.Heatmap(z);
                    // Colormap and Colorbar APIs may vary between builds; skip to keep broad compatibility
                    plt.Title("热力图（演示）");
                    plt.XLabel("X");
                    plt.YLabel("Y");
                    plot.Refresh();
                }
            };
        }

        public void Show(double[,] z, string xLabel, string yLabel, string title)
        {
            if (this.FindName("Plot") is ScottPlot.WPF.WpfPlot plot)
            {
                var plt = plot.Plot;
                plt.Clear();
                var hm = plt.Add.Heatmap(z);
                // skip Colormap/Colorbar for compatibility
                plt.Title(title);
                plt.XLabel(xLabel);
                plt.YLabel(yLabel);
                plot.Refresh();
            }
        }
    }
}
