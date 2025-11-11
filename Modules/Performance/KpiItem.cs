using System.Windows.Media;

namespace 币安量化机器人.Modules.Performance;

public class KpiItem
{
    public string Title { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public string SubText { get; set; } = string.Empty;
    public Brush ValueBrush { get; set; } = Brushes.Black;
    public Brush SubBrush { get; set; } = Brushes.Gray;
}
