using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NINA.Plugin.OnStepXTools.Converters {

    [ValueConversion(typeof(object), typeof(Visibility))]
    public class NullToVisibilityConverter : IValueConverter {
        public bool Invert { get; set; } = false;

        public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) {
            bool isNull = value == null;
            bool visible = Invert ? isNull : !isNull;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
