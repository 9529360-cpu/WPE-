using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace 币安量化机器人.Modules.Strategy
{
    public class ValueToBrushConverter : IValueConverter
    {
        public Brush PositiveBrush { get; set; } = new SolidColorBrush(Color.FromRgb(5, 150, 105));
        public Brush NegativeBrush { get; set; } = new SolidColorBrush(Color.FromRgb(239, 68, 68));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal d)
            {
                return d >= 0 ? PositiveBrush : NegativeBrush;
            }
            if (value is double db)
            {
                return db >= 0 ? PositiveBrush : NegativeBrush;
            }
            if (decimal.TryParse(value?.ToString(), out var parsed))
            {
                return parsed >= 0 ? PositiveBrush : NegativeBrush;
            }
            return PositiveBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
