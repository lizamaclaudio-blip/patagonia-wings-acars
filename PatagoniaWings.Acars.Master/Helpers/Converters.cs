using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PatagoniaWings.Acars.Master.Helpers
{
    /// <summary>Convierte string vacío a Collapsed.</summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Convierte bool negado a Visibility.</summary>
    public class BoolInverseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b ? !b : value;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Convierte count==0 a Visible (para mensajes de "vacío").</summary>
    public class ZeroToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is int i && i == 0 ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Convierte la nota de vuelo a color.</summary>
    public class GradeToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value as string) switch
            {
                "A+" or "A" => new SolidColorBrush(Color.FromRgb(63, 185, 80)),
                "B" => new SolidColorBrush(Color.FromRgb(170, 204, 68)),
                "C" => new SolidColorBrush(Color.FromRgb(204, 170, 68)),
                "D" => new SolidColorBrush(Color.FromRgb(204, 119, 68)),
                _ => new SolidColorBrush(Color.FromRgb(204, 68, 68))
            };
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Convierte velocidad vertical a color (verde arriba, rojo descenso fuerte).</summary>
    public class VSToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double vs)
            {
                if (vs > 100) return new SolidColorBrush(Color.FromRgb(63, 185, 80));
                if (vs < -1500) return new SolidColorBrush(Color.FromRgb(255, 68, 68));
                return new SolidColorBrush(Color.FromRgb(139, 148, 158));
            }
            return new SolidColorBrush(Color.FromRgb(139, 148, 158));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Convierte bool a color según parámetro "colorTrue|colorFalse".</summary>
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var parts = (parameter as string ?? "#3FB950|#FF4444").Split('|');
            var colorStr = (value is bool b && b) ? parts[0] : (parts.Length > 1 ? parts[1] : "#8B949E");
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorStr));
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>Para tabs: muestra/oculta según el índice seleccionado.</summary>
    public class TabToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (int.TryParse(value?.ToString(), out int selected) &&
                int.TryParse(parameter?.ToString(), out int target))
                return selected == target ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
