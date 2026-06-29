using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using NINA.Plugin.OnStepXTools.Interfaces;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.ViewModels {

    // Functionality merged into PointGenerationViewModel (OnStepX Model Builder) — no longer a standalone dock panel.
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class ModelInfoViewModel : DockableVM, ITelescopeConsumer, IDisposable {
        private readonly IOnStepXMount          _mount;
        private readonly ITelescopeMediator     _telescope;
        private readonly IModelBuilderMediator  _builderMediator;

        // Build session state
        private IReadOnlyList<AlignmentPoint> _buildPoints = Array.Empty<AlignmentPoint>();
        private IReadOnlyList<ResidualPoint>  _residuals   = Array.Empty<ResidualPoint>();
        private AlignmentModelCoefficients?   _coefficients;

        // Real-time mount position (from ITelescopeConsumer)
        private double _mountAltDeg;
        private double _mountAzDeg;
        private bool   _mountConnected;

        // User-controlled multiplier on the auto-scaled residual arrow lengths
        private double _errorArrowScale = 1.0;

        private string _statusMessage = string.Empty;

        public override string ContentId => "OnStepX_ModelInfo";

        [ImportingConstructor]
        public ModelInfoViewModel(
            IOnStepXMount mount,
            ITelescopeMediator telescope,
            IModelBuilderMediator builderMediator,
            IProfileService profile)
            : base(profile) {
            Title            = "OnStepX Model Info";
            ImageGeometry    = System.Windows.Application.Current?.Resources["StarSVG"] as System.Windows.Media.GeometryGroup;
            _mount           = mount;
            _telescope       = telescope;
            _builderMediator = builderMediator;

            _telescope.RegisterConsumer(this);
            _builderMediator.PointsLoaded    += OnPointsLoaded;
            _builderMediator.BuildStarted    += OnBuildStarted;
            _builderMediator.ProgressChanged += OnProgressChanged;
            _builderMediator.BuildCompleted  += OnBuildCompleted;

            WriteToMountCommand         = new RelayCommand(async _ => await WriteCoefficentsAsync(), _ => _coefficients != null);
            WriteToEepromCommand        = new RelayCommand(async _ => await WriteToEepromAsync(),    _ => _coefficients != null && _mountConnected);
            SaveModelCommand            = new RelayCommand(_ => SaveModel(), _ => _coefficients != null);
            LoadModelCommand            = new RelayCommand(_ => LoadModel());
            ForceModelActivationCommand = new RelayCommand(_ => ForceModelActivation(), _ => _mountConnected);
        }

        // ── Coefficients (displayed at the top) ──────────────────────────────────

        public AlignmentModelCoefficients? Coefficients {
            get => _coefficients;
            private set { SetProperty(ref _coefficients, value); RaisePropertyChanged(nameof(HasModel)); }
        }

        public bool HasModel => _coefficients != null;

        // Multiplier on the auto-scaled residual error arrows (slider in the view)
        public double ErrorArrowScale {
            get => _errorArrowScale;
            set {
                if (SetProperty(ref _errorArrowScale, Math.Max(0.1, value)))
                    RaisePropertyChanged(nameof(SkyPlot));
            }
        }

        // ── Charts (all computed - fresh PlotModel per access avoids OxyPlot crash) ──

        public PlotModel SkyPlot      => BuildSkyPlot(_buildPoints, _mountAltDeg, _mountAzDeg, _mountConnected,
                                                      _residuals.Count > 0 ? _residuals : null,
                                                      _errorArrowScale);
        public PlotModel ResidualPlot => BuildResidualScatter(_residuals);

        public string StatusMessage {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand WriteToMountCommand         { get; }
        public ICommand WriteToEepromCommand        { get; }
        public ICommand SaveModelCommand            { get; }
        public ICommand LoadModelCommand            { get; }
        public ICommand ForceModelActivationCommand { get; }

        // ── ITelescopeConsumer - real-time mount position ────────────────────────

        public void UpdateDeviceInfo(TelescopeInfo info) {
            try {
                _mountConnected = info.Connected;
                _mountAltDeg    = info.Altitude;
                _mountAzDeg     = info.Azimuth;
            } catch { }
            // Refresh the sky plot so the mount crosshair moves
            System.Windows.Application.Current?.Dispatcher.Invoke(
                () => RaisePropertyChanged(nameof(SkyPlot)));
        }

        // ── Build events ─────────────────────────────────────────────────────────

        // Fired when a sequencer item loads points - shows the planned visit path
        // before the build starts, so the user can verify coverage.
        private void OnPointsLoaded(object? sender, PointsLoadedEventArgs e) {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => {
                _buildPoints = e.Points;
                _residuals   = Array.Empty<ResidualPoint>();
                RaisePropertyChanged(nameof(SkyPlot));
                RaisePropertyChanged(nameof(ResidualPlot));
            });
        }

        private void OnBuildStarted(object? sender, BuildStartedEventArgs e) {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => {
                _buildPoints = e.AllPoints;
                _residuals   = Array.Empty<ResidualPoint>();
                RaisePropertyChanged(nameof(SkyPlot));
                RaisePropertyChanged(nameof(ResidualPlot));
            });
        }

        private void OnProgressChanged(object? sender, BuildProgressEventArgs e) {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => {
                _buildPoints = e.AllPoints;

                // Derive residuals from every point that has been successfully solved.
                // This lets the residual / error-vs-altitude plots update after each
                // point rather than waiting for the whole run to finish.
                _residuals = e.AllPoints
                    .Where(p => p.State == AlignmentPointState.Added)
                    .Select(ResidualPoint.FromModelPoint)
                    .ToList();

                RaisePropertyChanged(nameof(SkyPlot));
                RaisePropertyChanged(nameof(ResidualPlot));
            });
        }

        private void OnBuildCompleted(object? sender, BuildCompletedEventArgs e) {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => {
                Coefficients = e.Coefficients;
                _residuals   = e.Residuals;
                // Keep _buildPoints so the sky chart continues showing all points
                // with their final state colours (green=skipped, red=solved, dark-red=failed).
                // _residuals now drives the error arrow overlay (in SkyPlot builder).
                RaisePropertyChanged(nameof(SkyPlot));
                RaisePropertyChanged(nameof(ResidualPlot));
            });
        }

        // ── Commands ─────────────────────────────────────────────────────────────

        private async System.Threading.Tasks.Task WriteCoefficentsAsync() {
            if (_coefficients == null) return;
            StatusMessage = "Writing coefficients to mount…";
            await _mount.WriteCoefficientsAsync(_coefficients);
            StatusMessage = "Coefficients written.";
        }

        private async System.Threading.Tasks.Task WriteToEepromAsync() {
            if (_coefficients == null) return;
            try {
                StatusMessage = "Writing coefficients to mount…";
                await _mount.WriteCoefficientsAsync(_coefficients);
                StatusMessage = "Saving to EEPROM…";
                await _mount.SaveAlignmentToEepromAsync();
                StatusMessage = "Model saved to EEPROM (:SX0n# + :AW#).";
            } catch (Exception ex) {
                Logger.Error($"WriteToEeprom: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void SaveModel() {
            if (_coefficients == null) return;
            var dlg = new SaveFileDialog { Filter = "JSON|*.json", Title = "Save Model" };
            if (dlg.ShowDialog() != true) return;
            File.WriteAllText(dlg.FileName, JsonConvert.SerializeObject(_coefficients, Formatting.Indented));
        }

        private void LoadModel() {
            var dlg = new OpenFileDialog { Filter = "JSON|*.json", Title = "Load Model" };
            if (dlg.ShowDialog() != true) return;
            try { Coefficients = JsonConvert.DeserializeObject<AlignmentModelCoefficients>(File.ReadAllText(dlg.FileName)); }
            catch { }
        }

        private async void ForceModelActivation() {
            try {
                await _mount.ForceModelActivationAsync();
                StatusMessage = "Model activation forced (:SX09,2#).";
            } catch (Exception ex) {
                Logger.Error($"ForceModelActivation: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        // ── Plot builders ────────────────────────────────────────────────────────

        private static PlotModel BuildSkyPlot(
            IReadOnlyList<AlignmentPoint> points,
            double mountAltDeg, double mountAzDeg, bool mountConnected,
            IReadOnlyList<ResidualPoint>? residuals = null,
            double errorArrowScale = 1.0) {

            var model = PointGenerationViewModel.CreateBaseSkyModel();

            // ── Visit-order path (drawn first so it sits behind the point markers) ──
            // Connected in Index order so the user can see the planned slew sequence.
            if (points.Count > 1) {
                var ordered = points.OrderBy(pt => pt.Index).ToList();
                var path = new LineSeries {
                    Color           = OxyColor.FromAColor(80, OxyColors.CornflowerBlue),
                    StrokeThickness = 1,
                    LineStyle       = LineStyle.Solid
                };
                foreach (var pt in ordered) {
                    double r     = (90.0 - pt.AltitudeDeg) / 90.0;
                    double azRad = pt.AzimuthDeg * Math.PI / 180.0;
                    path.Points.Add(new DataPoint(r * Math.Sin(azRad), r * Math.Cos(azRad)));
                }
                model.Series.Add(path);
            }

            // ── Color-coded build points ──────────────────────────────────────────
            if (points.Count > 0) {
                foreach (var p in points) {
                    var colour = PointColour(p.State);
                    var r      = (90.0 - p.AltitudeDeg) / 90.0;
                    var azRad  = p.AzimuthDeg * Math.PI / 180.0;
                    var sc = new ScatterSeries {
                        MarkerType   = MarkerType.Circle,
                        MarkerSize   = 6,
                        MarkerFill   = colour,
                        MarkerStroke = OxyColors.Black,
                        MarkerStrokeThickness = 0.5
                    };
                    sc.Points.Add(new ScatterPoint(r * Math.Sin(azRad), r * Math.Cos(azRad)));
                    model.Series.Add(sc);
                }
            }

            // Post-build: residual error arrows (yellow lines from measured to expected).
            // Auto-scaled so the longest arrow is 0.4 chart units × the user-controlled multiplier.
            if (residuals != null && residuals.Count > 0) {
                double maxErr = residuals.Max(p => p.TotalErrorArcsec);
                double scale  = maxErr > 0 ? 0.4 / maxErr * errorArrowScale : 0;
                foreach (var rp in residuals) {
                    var r     = (90.0 - rp.AltitudeDeg) / 90.0;
                    var azRad = rp.AzimuthDeg * Math.PI / 180.0;
                    var x     = r * Math.Sin(azRad);
                    var y     = r * Math.Cos(azRad);
                    var arrow = new LineSeries { Color = OxyColors.Yellow, StrokeThickness = 1.5 };
                    arrow.Points.Add(new DataPoint(x, y));
                    arrow.Points.Add(new DataPoint(x + rp.ErrorRAArcsec  * scale * 0.001,
                                                   y + rp.ErrorDecArcsec * scale * 0.001));
                    model.Series.Add(arrow);
                }
            }

            // Mount position crosshair (real-time)
            if (mountConnected && mountAltDeg > 0) {
                double r     = (90.0 - mountAltDeg) / 90.0;
                double azRad = mountAzDeg * Math.PI / 180.0;
                double mx    = r * Math.Sin(azRad);
                double my    = r * Math.Cos(azRad);

                // Bullseye outer ring
                var ring = new LineSeries { Color = OxyColors.White, StrokeThickness = 1.5 };
                for (int a = 0; a <= 360; a += 5) {
                    double ar = a * Math.PI / 180.0;
                    ring.Points.Add(new DataPoint(mx + 0.04 * Math.Cos(ar), my + 0.04 * Math.Sin(ar)));
                }
                model.Series.Add(ring);

                // Crosshair arms (gap at bullseye ring)
                var ch = new LineSeries { Color = OxyColors.White, StrokeThickness = 1.5 };
                ch.Points.Add(new DataPoint(mx - 0.09, my));
                ch.Points.Add(new DataPoint(mx - 0.045, my));
                ch.Points.Add(new DataPoint(double.NaN, double.NaN));
                ch.Points.Add(new DataPoint(mx + 0.045, my));
                ch.Points.Add(new DataPoint(mx + 0.09, my));
                ch.Points.Add(new DataPoint(double.NaN, double.NaN));
                ch.Points.Add(new DataPoint(mx, my - 0.09));
                ch.Points.Add(new DataPoint(mx, my - 0.045));
                ch.Points.Add(new DataPoint(double.NaN, double.NaN));
                ch.Points.Add(new DataPoint(mx, my + 0.045));
                ch.Points.Add(new DataPoint(mx, my + 0.09));
                model.Series.Add(ch);
            }

            return model;
        }

        // Pending/unvisited → green   In-progress → yellow   Done → red   Failed → dark-red
        private static OxyColor PointColour(AlignmentPointState s) => s switch {
            AlignmentPointState.Added                          => OxyColors.Red,
            AlignmentPointState.Failed or
            AlignmentPointState.FailedRMS                     => OxyColor.FromRgb(0x8B, 0x00, 0x00),
            AlignmentPointState.Slewing or
            AlignmentPointState.Settling or
            AlignmentPointState.Exposing or
            AlignmentPointState.PlateSolving or
            AlignmentPointState.Uploading                     => OxyColors.Yellow,
            _                                                  => OxyColors.LimeGreen
        };

        private static PlotModel BuildResidualScatter(IReadOnlyList<ResidualPoint> residuals) {
            var model = new PlotModel {
                Title      = "Residuals  ΔRA vs ΔDec",
                Background = OxyColor.FromRgb(0x0a, 0x0a, 0x1e),
                TextColor  = OxyColors.LightGray
            };
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "ΔRA (\")",  TextColor = OxyColors.LightGray });
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Left,   Title = "ΔDec (\")", TextColor = OxyColors.LightGray });
            model.Annotations.Add(new LineAnnotation { Type = LineAnnotationType.Horizontal, Y = 0, Color = OxyColors.DimGray });
            model.Annotations.Add(new LineAnnotation { Type = LineAnnotationType.Vertical,   X = 0, Color = OxyColors.DimGray });

            if (residuals.Count == 0) return model;

            double rmsRA  = Rms(residuals, p => p.ErrorRAArcsec);
            double rmsDec = Rms(residuals, p => p.ErrorDecArcsec);
            double rms    = Math.Sqrt(rmsRA * rmsRA + rmsDec * rmsDec);

            model.Annotations.Add(new TextAnnotation {
                Text         = $"RMS {rms:F1}\"",
                TextPosition = new DataPoint(0, 0),
                TextColor    = OxyColors.OrangeRed,
                FontSize     = 10
            });

            // RMS circle
            var rmsCircle = new LineSeries { Color = OxyColors.OrangeRed, StrokeThickness = 1, LineStyle = LineStyle.Dash };
            for (int a = 0; a <= 360; a += 3)
                rmsCircle.Points.Add(new DataPoint(rms * Math.Cos(a * Math.PI / 180), rms * Math.Sin(a * Math.PI / 180)));
            model.Series.Add(rmsCircle);

            model.Axes.Add(new LinearColorAxis { Key = "rms", Position = AxisPosition.Right, Palette = OxyPalettes.Jet(64) });
            var scatter = new ScatterSeries { MarkerType = MarkerType.Circle, MarkerSize = 4, ColorAxisKey = "rms" };
            foreach (var p in residuals)
                scatter.Points.Add(new ScatterPoint(p.ErrorRAArcsec, p.ErrorDecArcsec, value: p.TotalErrorArcsec));
            model.Series.Add(scatter);
            return model;
        }

        private static double Rms(IReadOnlyList<ResidualPoint> pts, Func<ResidualPoint, double> sel) {
            if (pts.Count == 0) return 0;
            return Math.Sqrt(pts.Average(p => sel(p) * sel(p)));
        }

        public void Dispose() {
            _telescope.RemoveConsumer(this);
            _builderMediator.PointsLoaded    -= OnPointsLoaded;
            _builderMediator.BuildStarted    -= OnBuildStarted;
            _builderMediator.ProgressChanged -= OnProgressChanged;
            _builderMediator.BuildCompleted  -= OnBuildCompleted;
        }
    }
}
