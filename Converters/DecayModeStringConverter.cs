using System;
using System.Globalization;
using System.Windows.Data;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.Converters {

    // Converts between OnStepX's raw 1-indexed decay-mode value string ("1".."5") and its
    // display label ("Slow".."StealthChop"), for a ComboBox's SelectedItem bound to EditValue.
    [ValueConversion(typeof(string), typeof(string))]
    public class DecayModeStringConverter : IValueConverter {
        public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) {
            if (value is string s && int.TryParse(s, out var i) && i >= 1 && i <= AxisParameter.DecayModeLabels.Length)
                return AxisParameter.DecayModeLabels[i - 1];
            return value ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            if (value is string label) {
                var index = Array.IndexOf(AxisParameter.DecayModeLabels, label);
                if (index >= 0) return (index + 1).ToString(CultureInfo.InvariantCulture);
            }
            return value;
        }
    }
}
