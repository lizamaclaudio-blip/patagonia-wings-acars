using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PatagoniaWings.Acars.Master.Converters
{
    public class BoolToGlowEffectConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool state = value is bool b && b;
            if (!state) return null;

            string colorHex = parameter as string ?? "#22C55E";
            Color color = (Color)ColorConverter.ConvertFromString(colorHex);
            return new DropShadowEffect
            {
                Color = color,
                BlurRadius = 8,
                ShadowDepth = 0,
                Opacity = 0.7
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
