using System.Configuration;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NINA.Plugin.OnStepXTools.Properties {

    [CompilerGenerated]
    internal sealed partial class Settings : ApplicationSettingsBase {
        private static readonly Settings _default =
            (Settings)Synchronized(new Settings());

        public static Settings Default => _default;

        [UserScopedSetting]
        [DebuggerNonUserCode]
        [DefaultSettingValue("False")]
        public bool UpdateSettings {
            get => (bool)this[nameof(UpdateSettings)];
            set => this[nameof(UpdateSettings)] = value;
        }
    }
}
