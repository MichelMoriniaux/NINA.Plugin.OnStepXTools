using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.OnStepXTools {

    [Export(typeof(ResourceDictionary))]
    public partial class Options : ResourceDictionary {
        public Options() => InitializeComponent();
    }
}
