using System;
using System.Globalization;
using System.Windows.Data;

namespace 币安量化机器人.Modules.Strategy
{
    public class TimeSpanToShortStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan ts)
            {
                if (ts.TotalHours >= 1)
                {
                    return $"{ts.TotalHours:F1}h";
                }
                return $"{ts.TotalMinutes:F0}m";
            }
            return "0m";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
