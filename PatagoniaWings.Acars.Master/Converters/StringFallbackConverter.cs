using System;
using System.Globalization;
using System.Windows.Data;

namespace PatagoniaWings.Acars.Master.Converters
{
    public class StringFallbackConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return parameter?.ToString() ?? "—";
            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}