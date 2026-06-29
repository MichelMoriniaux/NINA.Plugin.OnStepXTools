using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NINA.Plugin.OnStepXTools.Converters {

    // Returns Visible when an integer count is zero (empty collection hint),
    // Collapsed when count > 0.
    [ValueConversion(typeof(int), typeof(Visibility))]
    public class ZeroToVisibilityConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
