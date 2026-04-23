using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace PatagoniaWings.Acars.Master.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool state = value is bool b && b;
            string colors = parameter as string ?? "#59D98B|#304355";
            string[] parts = colors.Split('|');
            string colorHex = state ? parts[0] : (parts.Length > 1 ? parts[1] : "#304355");
            return (SolidColorBrush)new BrushConverter().ConvertFrom(colorHex)!;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}