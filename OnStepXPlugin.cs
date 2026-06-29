using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;

namespace NINA.Plugin.OnStepXTools {

    [Export(typeof(IPluginManifest))]
    public class OnStepXPlugin : PluginBase, INotifyPropertyChanged {
        // MEF constructs and shares all plugin services - no manual instantiation needed.
        private OnStepXOptions _options = OnStepXOptions.Load();

        [ImportingConstructor]
        public OnStepXPlugin(IProfileService profileService) : base() {
            if (Properties.Settings.Default.UpdateSettings) {
                Properties.Settings.Default.Upgrade();
                Properties.Settings.Default.UpdateSettings = false;
                Properties.Settings.Default.Save();
            }
        }

        public OnStepXOptions PluginOptions {
            get => _options;
            private set { _options = value; RaisePropertyChanged(); }
        }

        public override Task Initialize() {
            Logger.Info("OnStepX Alignment Tools: Initialize()");
            return base.Initialize();
        }

        public override Task Teardown() {
            Logger.Info("OnStepX Alignment Tools: Teardown()");
            _options.Save();
            return base.Teardown();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
