using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace NINA.Plugin.OnStepXTools.Converters {

    [ValueConversion(typeof(bool), typeof(Brush))]
    public class BoolToColorConverter : IValueConverter {
        public Brush TrueColor  { get; set; } = Brushes.LimeGreen;
        public Brush FalseColor { get; set; } = Brushes.Red;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b && b ? TrueColor : FalseColor;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
