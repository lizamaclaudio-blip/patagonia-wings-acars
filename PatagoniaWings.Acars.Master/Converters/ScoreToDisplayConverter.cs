using System;
using System.Globalization;
using System.Windows.Data;

namespace PatagoniaWings.Acars.Master.Converters
{
    public class ScoreToDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return "—";
            if (value is int i) return i > 0 ? i.ToString() : "—";
            if (value is double d) return d > 0 ? d.ToString("F0") : "—";
            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}