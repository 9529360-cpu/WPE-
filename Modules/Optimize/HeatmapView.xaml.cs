using System.Windows.Controls;
using ScottPlot;
using ScottPlot.Plottables;

namespace 币安量化机器人.Modules.Optimize
{
    public partial class HeatmapView : UserControl
    {
        public HeatmapView()
        {
            InitializeComponent();

            // 可选：加载时画一张小演示图，验证控件是否正常
            Loaded += (_, __) =>
            {
                double[,] z = new double[20, 20];
                for (int i = 0; i < 20; i++)
                {
                    for (int j = 0; j < 20; j++)
                    {
                        z[i, j] = 0.6 + 0.4 * System.Math.Sin(i * .2) * System.Math.Cos(j * .15);
                    }
                }

                Plot plt = Plot.Plot;                 // v5：从 WpfPlot 取 Plot
                plt.Clear();
                Heatmap hm = plt.Add.Heatmap(z);         // v5：Plot.Add.Heatmap
                hm.Colormap = new ScottPlot.Colormaps.Turbo();
                plt.Add.ColorBar(hm);
                plt.Title("热力图（演示）");
                plt.XLabel("X");
                plt.YLabel("Y");
                Plot.Refresh();
            };
        }

        // 真正使用时：调用它来显示你的矩阵
        public void Show(double[,] z, string xLabel, string yLabel, string title)
        {
            Plot plt = Plot.Plot;
            plt.Clear();
            Heatmap hm = plt.Add.Heatmap(z);
            hm.Colormap = new ScottPlot.Colormaps.Turbo();
            plt.Add.ColorBar(hm);
            plt.Title(title);
            plt.XLabel(xLabel);
            plt.YLabel(yLabel);
            Plot.Refresh();
        }
    }
}
