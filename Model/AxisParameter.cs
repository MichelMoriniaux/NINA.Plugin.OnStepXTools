using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NINA.Plugin.OnStepXTools.Model {

    // Represents one configurable parameter returned by :GXA{axis},{index}#
    // Response format: value,min,max,typeCode,name
    public class AxisParameter : INotifyPropertyChanged {
        private string _editValue = string.Empty;

        public int    Index       { get; init; }
        public string Name        { get; init; } = string.Empty;
        public string CurrentValue { get; init; } = string.Empty;
        public string Min         { get; init; } = string.Empty;
        public string Max         { get; init; } = string.Empty;

        // Type codes from OnStepX:
        //   1/2 = boolean (off/on)
        //   3/4 = integer
        //   5/6 = float
        //   9   = power-of-2 (1,2,4,...,256)
        //   10  = decay mode
        //   Even types (2,4,6) take effect immediately (no reboot)
        public int  TypeCode    { get; init; }
        public bool IsImmediate { get; init; }

        public bool IsBoolean => TypeCode is 1 or 2;
        public bool IsFloat   => TypeCode is 5 or 6;
        public bool IsPow2    => TypeCode == 9;
        public bool IsDecay   => TypeCode == 10;

        // Plain-text editor applies whenever there's no dedicated editor (CheckBox/ComboBox).
        public bool IsPlainText => !IsBoolean && !IsDecay;

        // OnStepX Extended.Constants.h L_ADV_DECAY_* labels, in firmware enum order. Public so
        // DecayModeStringConverter and a ComboBox's ItemsSource can share this one list.
        public static readonly string[] DecayModeLabels = { "Slow", "Fast", "Mixed", "SpreadCycle", "StealthChop" };

        // CurrentValue stays the raw firmware string (compared against EditValue in
        // SaveAxisAsync to detect changes, and re-used verbatim on upload) - this is a
        // display-only decode of decay-mode parameters, e.g. "3" -> "Mixed". The firmware's
        // decay mode value is 1-indexed (confirmed against real hardware: a "Decay mode Goto"
        // parameter reporting min=4/max=5 only makes sense as {SpreadCycle, StealthChop}).
        public string CurrentValueDisplay =>
            IsDecay && int.TryParse(CurrentValue, out var m) && m >= 1 && m <= DecayModeLabels.Length
                ? DecayModeLabels[m - 1]
                : CurrentValue;

        public string RangeHint => (Min.Length > 0 && Max.Length > 0)
            ? $"  [{Min} … {Max}]"
            : string.Empty;

        public string ImmediateHint => IsImmediate ? "  (immediate - no reboot)" : string.Empty;

        // Editable copy for the user
        public string EditValue {
            get => _editValue;
            set { if (_editValue != value) { _editValue = value; OnPropertyChanged(); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
