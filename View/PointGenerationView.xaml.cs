using System.Windows;
using System.Windows.Controls;
using NINA.Plugin.OnStepXTools.ViewModels;

namespace NINA.Plugin.OnStepXTools.View {
    public partial class PointGenerationView : UserControl {
        public PointGenerationView() => InitializeComponent();

        // When the sky chart container changes size, push a fresh PlotModel into the
        // PlotView.  OxyPlot caches its rendering canvas size and does not re-render
        // automatically when the host grows; giving it a new model forces a full
        // re-render at the current PlotView dimensions.
        private void SkyContainer_SizeChanged(object sender, SizeChangedEventArgs e) {
            (DataContext as PointGenerationViewModel)?.RefreshSkyPlot();
        }
    }
}
