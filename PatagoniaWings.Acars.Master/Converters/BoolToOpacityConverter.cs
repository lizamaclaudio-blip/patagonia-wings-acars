using System;
using System.Globalization;
using System.Windows.Data;

namespace PatagoniaWings.Acars.Master.Converters
{
    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isEnabled = value is bool b && b;
            return isEnabled ? 1.0 : 0.38;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
