using System.Windows.Controls;
using ScottPlot;

namespace 币安量化机器人.Modules.Optimize
{
    public partial class HeatmapView : UserControl
    {
        public HeatmapView()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                // Only attempt to draw if a real ScottPlot control exists at runtime
                if (this.FindName("Plot") is ScottPlot.WpfPlot plot)
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
                    var hm = plt.AddHeatmap(z);
                    // v4 API: do not assign hm.Colormap here (read-only in some builds)
                    // Optionally configure color mapping if supported by the version in use
                    plt.Title("热力图（演示）");
                    plt.XLabel("X");
                    plt.YLabel("Y");
                    plot.Refresh();
                }
            };
        }

        public void Show(double[,] z, string xLabel, string yLabel, string title)
        {
            if (this.FindName("Plot") is ScottPlot.WpfPlot plot)
            {
                var plt = plot.Plot;
                plt.Clear();
                var hm = plt.AddHeatmap(z);
                // avoid assigning hm.Colormap to remain compatible with multiple ScottPlot v4 builds
                plt.Title(title);
                plt.XLabel(xLabel);
                plt.YLabel(yLabel);
                plot.Refresh();
            }
        }
    }
}
