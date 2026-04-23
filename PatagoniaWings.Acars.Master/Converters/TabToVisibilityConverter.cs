using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PatagoniaWings.Acars.Master.Converters
{
    public class TabToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int tab && parameter is string p && int.TryParse(p, out int expected))
                return tab == expected ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}