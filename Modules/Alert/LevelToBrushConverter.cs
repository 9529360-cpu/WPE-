using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace 币安量化机器人.Modules.Alert
{
    public class LevelToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string level = value?.ToString() ?? string.Empty;
            return level switch
            {
                "Critical" => new SolidColorBrush(Color.FromRgb(220, 38, 38)), // red-600
                "Error" => new SolidColorBrush(Color.FromRgb(234, 88, 12)), // orange-600
                "Warning" => new SolidColorBrush(Color.FromRgb(245, 158, 11)), // amber-500
                "Info" => new SolidColorBrush(Color.FromRgb(59, 130, 246)), // blue-500
                _ => new SolidColorBrush(Color.FromRgb(107, 114, 128)), // gray-500
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
