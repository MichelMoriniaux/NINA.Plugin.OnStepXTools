using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.Plugin.OnStepXTools.Resources {

    [Export(typeof(ResourceDictionary))]
    public partial class DataTemplates : ResourceDictionary {
        public DataTemplates() => InitializeComponent();
    }
}
