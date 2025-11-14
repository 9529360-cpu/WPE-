using System.Windows.Media;

namespace 币安量化机器人.Models
{
    public class NavMenuItem
    {
        public string Tag { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public Brush? Icon { get; set; }
        // Optional Geometry for vector icon rendering
        public Geometry? GeometryData { get; set; }
        // Group name for UI grouping
        public string Group { get; set; } = "Default";
    }
}
