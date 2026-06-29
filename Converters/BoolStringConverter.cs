using System;
using System.Globalization;
using System.Windows.Data;

namespace NINA.Plugin.OnStepXTools.Converters {

    // Converts between bool and the string values OnStep uses: "0"=false, "1"=true.
    // Also handles "true"/"false" strings for robustness.
    [ValueConversion(typeof(string), typeof(bool))]
    public class BoolStringConverter : IValueConverter {
        public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) {
            if (value is not string s) return false;
            return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is bool b && b ? "1" : "0";
    }
}
