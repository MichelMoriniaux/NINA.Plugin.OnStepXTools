using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using NINA.Plugin.OnStepXTools.ModelManagement;

namespace NINA.Plugin.OnStepXTools.ViewModels {

    [Export(typeof(IDockableVM))]
    [Export(typeof(PointGenerationViewModel))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class PointGenerationViewModel : DockableVM, ITelescopeConsumer, IDisposable {
        private readonly IProfileService         _profile;
        private readonly IOnStepXMount           _mount;
        private readonly ITelescopeMediator      _telescope;
        private readonly ICameraMediator         _camera;
        private readonly IModelBuilderMediator   _builderMediator;
        private readonly IModelBuilder           _builder;
        private readonly ModelPointGenerator     _generator = new();

        private CancellationTokenSource? _buildCts;
        private bool                     _isBuilding;

        // ── Point generation state ───────────────────────────────────────────────
        private PointGenerationOptions               _options       = new();
        private ObservableCollection<AlignmentPoint> _points        = new();
        private GenerationMethod                     _selectedMethod = GenerationMethod.GoldenSpiral;
        private BuildMode                            _selectedMode   = BuildMode.FullSkyPointingModel;
        private double                               _meridianExclusionDeg;

        // ── Build / model state ──────────────────────────────────────────────────
        // Planned points from Generate() or Load — shown as Cyan before a build starts.
        private IReadOnlyList<AlignmentPoint> _plannedPoints = Array.Empty<AlignmentPoint>();
        // Live points updated by build mediator events — shown with state colours during/after a build.
        private IReadOnlyList<AlignmentPoint> _buildPoints   = Array.Empty<AlignmentPoint>();
        private IReadOnlyList<ResidualPoint>  _residuals     = Array.Empty<ResidualPoint>();
        private AlignmentModelCoefficients?   _coefficients;

        // ── Mount position (ITelescopeConsumer) ──────────────────────────────────
        private double _mountAltDeg;
        private double _mountAzDeg;
        private bool   _mountConnected;

        private double _errorArrowScale = 1.0;
        private string _buildStatusMessage = string.Empty;

        public override string ContentId => "OnStepX_ModelBuilder";

        [ImportingConstructor]
        public PointGenerationViewModel(
            IProfileService       profile,
            IOnStepXMount         mount,
            ITelescopeMediator    telescope,
            ICameraMediator       camera,
            IModelBuilderMediator builderMediator,
            IModelBuilder         builder)
            : base(profile) {
            Title         = "OnStepX Model Builder";
            ImageGeometry = System.Windows.Application.Current?.Resources["PolarAlignSVG"] as System.Windows.Media.GeometryGroup;
            _profile         = profile;
            _mount           = mount;
            _telescope       = telescope;
            _camera          = camera;
            _builderMediator = builderMediator;
            _builder         = builder;

            _meridianExclusionDeg = new HorizonAndMeridianFilter(profile).MeridianExclusionHalfWidthDeg();

            _telescope.RegisterConsumer(this);
            _builderMediator.PointsLoaded    += OnPointsLoaded;
            _builderMediator.BuildStarted    += OnBuildStarted;
            _builderMediator.ProgressChanged += OnProgressChanged;
            _builderMediator.BuildCompleted  += OnBuildCompleted;

            GenerateCommand          = new RelayCommand(_ => Generate());
            SavePointsCommand        = new RelayCommand(_ => SavePoints(),      _ => _points.Count > 0);
            LoadPointsCommand        = new RelayCommand(_ => LoadPoints());
            StartBuildCommand        = new RelayCommand(async _ => await StartBuildAsync(),
                                           _ => _plannedPoints.Count > 0 && !_isBuilding
                                                && _mountConnected && _camera.GetInfo().Connected);
            CancelBuildCommand       = new RelayCommand(_ => _buildCts?.Cancel(), _ => _isBuilding);
            WriteToMountCommand      = new RelayCommand(async _ => await WriteCoefficientsAsync(), _ => _coefficients != null);
            WriteToEepromCommand     = new RelayCommand(async _ => await WriteToEepromAsync(),     _ => _coefficients != null && _mountConnected);
            SaveModelCommand         = new RelayCommand(_ => SaveModel(),       _ => _coefficients != null);
            LoadModelCommand         = new RelayCommand(_ => LoadModel());
            ForceActivationCommand   = new RelayCommand(async _ => await ForceModelActivationAsync(), _ => _mountConnected);
        }

        // ── Point generation options ─────────────────────────────────────────────

        public PointGenerationOptions Options {
            get => _options;
            set => SetProperty(ref _options, value);
        }

        public GenerationMethod SelectedMethod {
            get => _selectedMethod;
            set {
                if (SetProperty(ref _selectedMethod, value)) {
                    Options.Method = value;
                    RaisePropertyChanged(nameof(IsSiderealPath));
                }
            }
        }

        public bool IsSiderealPath => _selectedMethod == GenerationMethod.SiderealPath;

        public BuildMode SelectedMode {
            get => _selectedMode;
            set {
                if (SetProperty(ref _selectedMode, value)) {
                    Options.Mode = value;
                    RaisePropertyChanged(nameof(MaxPointCount));
                    RaisePropertyChanged(nameof(TickFrequency));
                    if (value == BuildMode.StarAlignment && Options.PointCount > 9)
                        Options.PointCount = 9;
                    else if (value == BuildMode.FullSkyPointingModel && Options.PointCount < 10)
                        Options.PointCount = 50;
                    RaisePropertyChanged(nameof(Options));
                }
            }
        }

        public int    MaxPointCount => _selectedMode == BuildMode.StarAlignment ? 9   : 300;
        public double TickFrequency => _selectedMode == BuildMode.StarAlignment ? 1.0 : 20.0;

        public double MeridianExclusionDeg {
            get => _meridianExclusionDeg;
            set {
                if (SetProperty(ref _meridianExclusionDeg, Math.Max(0, value)))
                    RaisePropertyChanged(nameof(SkyPlot));
            }
        }

        public ObservableCollection<AlignmentPoint> Points {
            get => _points;
            private set {
                SetProperty(ref _points, value);
                RaisePropertyChanged(nameof(HasPoints));
            }
        }

        public bool HasPoints => _points.Count > 0;

        // ── Model coefficients ───────────────────────────────────────────────────

        public AlignmentModelCoefficients? Coefficients {
            get => _coefficients;
            private set {
                SetProperty(ref _coefficients, value);
                RaisePropertyChanged(nameof(HasModel));
            }
        }

        public bool HasModel => _coefficients != null;

        public double ErrorArrowScale {
            get => _errorArrowScale;
            set {
                if (SetProperty(ref _errorArrowScale, Math.Max(0.1, value)))
                    RaisePropertyChanged(nameof(SkyPlot));
            }
        }

        public string BuildStatusMessage {
            get => _buildStatusMessage;
            private set => SetProperty(ref _buildStatusMessage, value);
        }

        // ── Plots ────────────────────────────────────────────────────────────────

        public PlotModel SkyPlot {
            get {
                // During/after a build: use build points with state colours.
                // Before any build: use planned points as Cyan.
                var pts    = _buildPoints.Count > 0 ? _buildPoints : _plannedPoints;
                var inBuild = _buildPoints.Count > 0;
                return BuildSkyPlot(pts, _profile, _meridianExclusionDeg,
                                    _mountAltDeg, _mountAzDeg, _mountConnected,
                                    _residuals.Count > 0 ? _residuals : null,
                                    _errorArrowScale, inBuild);
            }
        }

        public PlotModel ResidualPlot => BuildResidualScatter(_residuals);

        // Called by the view's SizeChanged handler to force OxyPlot to re-render
        // the sky chart at the new container dimensions.
        public void RefreshSkyPlot() => RaisePropertyChanged(nameof(SkyPlot));

        // ── Build settings (exposed to UI) ───────────────────────────────────────

        private double _exposureTimeSec  = 5.0;
        private double _settleTimeSec    = 3.0;

        public double ExposureTimeSec {
            get => _exposureTimeSec;
            set => SetProperty(ref _exposureTimeSec, Math.Max(0.5, value));
        }

        public double SettleTimeSec {
            get => _settleTimeSec;
            set => SetProperty(ref _settleTimeSec, Math.Max(0, value));
        }

        public bool IsBuilding {
            get => _isBuilding;
            private set {
                if (SetProperty(ref _isBuilding, value))
                    RaisePropertyChanged(nameof(IsNotBuilding));
            }
        }

        public bool IsNotBuilding => !_isBuilding;

        // ── Commands ─────────────────────────────────────────────────────────────

        public ICommand GenerateCommand        { get; }
        public ICommand SavePointsCommand      { get; }
        public ICommand LoadPointsCommand      { get; }
        public ICommand StartBuildCommand      { get; }
        public ICommand CancelBuildCommand     { get; }
        public ICommand WriteToMountCommand    { get; }
        public ICommand WriteToEepromCommand   { get; }
        public ICommand SaveModelCommand       { get; }
        public ICommand LoadModelCommand       { get; }
        public ICommand ForceActivationCommand { get; }

        // ── ITelescopeConsumer ───────────────────────────────────────────────────

        public void UpdateDeviceInfo(TelescopeInfo info) {
            try {
                _mountConnected = info.Connected;
                _mountAltDeg    = info.Altitude;
                _mountAzDeg     = info.Azimuth;
            } catch { }
            System.Windows.Application.Current?.Dispatcher.Invoke(
                () => RaisePropertyChanged(nameof(SkyPlot)));
        }

        // ── Build mediator events ────────────────────────────────────────────────

        private void OnPointsLoaded(object? sender, PointsLoadedEventArgs e) {
            System.Windows.Application.Current?.Dispatcher.Invoke(() => {
                _buildPoints = Array.Empty<AlignmentPoint>();
                _plannedPoints = e.Points;
                _residuals     = Array.Empty<ResidualPoint>();
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
                _residuals   = e.AllPoints
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
                RaisePropertyChanged(nameof(SkyPlot));
                RaisePropertyChanged(nameof(ResidualPlot));
            });
        }

        // ── Point generation ─────────────────────────────────────────────────────

        private void Generate() {
            var filter = new HorizonAndMeridianFilter(_profile);
            Options.SiteLatitudeDeg           = _profile.ActiveProfile.AstrometrySettings.Latitude;
            Options.MeridianExclusionHalfWidthDeg = _meridianExclusionDeg;
            Options.Method                    = _selectedMethod;

            var result = _generator.Generate(Options, filter);
            _plannedPoints = result;
            _buildPoints   = Array.Empty<AlignmentPoint>();
            Points         = new ObservableCollection<AlignmentPoint>(result);
            RaisePropertyChanged(nameof(SkyPlot));
        }

        private void SavePoints() {
            var dlg = new SaveFileDialog { Filter = "JSON|*.json", Title = "Save Point List" };
            if (dlg.ShowDialog() != true) return;
            File.WriteAllText(dlg.FileName,
                JsonConvert.SerializeObject(new List<AlignmentPoint>(_points), Formatting.Indented));
        }

        private void LoadPoints() {
            var dlg = new OpenFileDialog { Filter = "JSON|*.json", Title = "Load Point List" };
            if (dlg.ShowDialog() != true) return;
            try {
                var loaded = JsonConvert.DeserializeObject<List<AlignmentPoint>>(File.ReadAllText(dlg.FileName));
                if (loaded != null) {
                    _plannedPoints = loaded;
                    _buildPoints   = Array.Empty<AlignmentPoint>();
                    Points         = new ObservableCollection<AlignmentPoint>(loaded);
                    RaisePropertyChanged(nameof(SkyPlot));
                }
            } catch { }
        }

        // ── Model coefficient commands ────────────────────────────────────────────

        private async Task StartBuildAsync() {
            if (_plannedPoints.Count == 0) return;
            _buildCts = new CancellationTokenSource();
            IsBuilding = true;
            BuildStatusMessage = $"Starting {_selectedMode} build with {_plannedPoints.Count} points…";
            try {
                var opts = new ModelBuilderOptions {
                    Mode                       = _selectedMode,
                    ExposureTimeSeconds        = _exposureTimeSec,
                    SlewSettleSeconds          = _settleTimeSec,
                    WriteModelToMountOnCompletion = true,
                };
                var result = await _builder.BuildModelAsync(_plannedPoints, opts, null, _buildCts.Token);
                if (result != null) {
                    Coefficients       = result;
                    BuildStatusMessage = $"Build complete — {result.Stars} stars.";
                } else {
                    BuildStatusMessage = "Build completed without a model result.";
                }
            } catch (OperationCanceledException) {
                BuildStatusMessage = "Build cancelled.";
            } catch (Exception ex) {
                Logger.Error($"StartBuild: {ex.Message}");
                BuildStatusMessage = $"Build error: {ex.Message}";
            } finally {
                IsBuilding = false;
                _buildCts?.Dispose();
                _buildCts = null;
            }
        }

        private async Task WriteCoefficientsAsync() {
            if (_coefficients == null) return;
            try {
                BuildStatusMessage = "Writing coefficients to mount…";
                await _mount.WriteCoefficientsAsync(_coefficients);
                BuildStatusMessage = "Coefficients written.";
            } catch (Exception ex) {
                Logger.Error($"WriteCoefficients: {ex.Message}");
                BuildStatusMessage = $"Error: {ex.Message}";
            }
        }

        private async Task WriteToEepromAsync() {
            if (_coefficients == null) return;
            try {
                BuildStatusMessage = "Writing coefficients…";
                await _mount.WriteCoefficientsAsync(_coefficients);
                BuildStatusMessage = "Saving to EEPROM…";
                await _mount.SaveAlignmentToEepromAsync();
                BuildStatusMessage = "Model saved to EEPROM.";
            } catch (Exception ex) {
                Logger.Error($"WriteToEeprom: {ex.Message}");
                BuildStatusMessage = $"Error: {ex.Message}";
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

        private async Task ForceModelActivationAsync() {
            try {
                await _mount.ForceModelActivationAsync();
                BuildStatusMessage = "Model activation forced (:SX09,2#).";
            } catch (Exception ex) {
                Logger.Error($"ForceModelActivation: {ex.Message}");
                BuildStatusMessage = $"Error: {ex.Message}";
            }
        }

        // ── Sky plot ─────────────────────────────────────────────────────────────

        internal static PlotModel BuildSkyPlot(
            IReadOnlyList<AlignmentPoint> points,
            IProfileService               profile,
            double                        meridianExclusionDeg = 0,
            double                        mountAltDeg          = 0,
            double                        mountAzDeg           = 0,
            bool                          mountConnected       = false,
            IReadOnlyList<ResidualPoint>? residuals            = null,
            double                        errorArrowScale      = 1.0,
            bool                          inBuildMode          = false) {

            var model = CreateBaseSkyModel();

            // ── Horizon polygon ───────────────────────────────────────────────────
            var horizon = profile.ActiveProfile.AstrometrySettings.Horizon;
            if (horizon != null) {
                var hs = new AreaSeries {
                    Color = OxyColor.FromAColor(80, OxyColors.Gray),
                    Fill  = OxyColor.FromAColor(50, OxyColors.DarkSlateGray),
                    StrokeThickness = 1
                };
                for (int az = 0; az <= 360; az += 2) {
                    double alt = horizon.GetAltitude(az);
                    double r   = (90.0 - alt) / 90.0;
                    double ar  = az * Math.PI / 180.0;
                    hs.Points.Add(new DataPoint(r * Math.Sin(ar), r * Math.Cos(ar)));
                }
                for (int az = 360; az >= 0; az -= 2) {
                    double ar = az * Math.PI / 180.0;
                    hs.Points2.Add(new DataPoint(Math.Sin(ar), Math.Cos(ar)));
                }
                model.Series.Add(hs);
            }

            // ── Meridian exclusion sectors ────────────────────────────────────────
            if (meridianExclusionDeg > 0) {
                AddMeridianSector(model, 0,   meridianExclusionDeg);
                AddMeridianSector(model, 180, meridianExclusionDeg);
            }

            // ── Meridian line ─────────────────────────────────────────────────────
            var ml = new LineSeries { Color = OxyColors.CornflowerBlue, StrokeThickness = 1, LineStyle = LineStyle.Dash };
            ml.Points.Add(new DataPoint(0,  1));
            ml.Points.Add(new DataPoint(0,  0));
            ml.Points.Add(new DataPoint(0, -1));
            model.Series.Add(ml);

            // ── Visit-order path ──────────────────────────────────────────────────
            if (points.Count > 1) {
                var path = new LineSeries {
                    Color = OxyColor.FromAColor(80, OxyColors.CornflowerBlue),
                    StrokeThickness = 1, LineStyle = LineStyle.Solid
                };
                foreach (var p in points.OrderBy(pt => pt.Index)) {
                    double r  = (90.0 - p.AltitudeDeg) / 90.0;
                    double ar = p.AzimuthDeg * Math.PI / 180.0;
                    path.Points.Add(new DataPoint(r * Math.Sin(ar), r * Math.Cos(ar)));
                }
                model.Series.Add(path);
            }

            // ── Points — colour by state (build mode) or flat cyan (planning mode) ─
            if (points.Count > 0) {
                if (inBuildMode) {
                    // Group into one scatter per colour so the model is compact
                    var byColour = points.GroupBy(p => PointColour(p.State));
                    foreach (var grp in byColour) {
                        var sc = new ScatterSeries {
                            MarkerType            = MarkerType.Circle,
                            MarkerSize            = 6,
                            MarkerFill            = grp.Key,
                            MarkerStroke          = OxyColors.Black,
                            MarkerStrokeThickness = 0.5
                        };
                        foreach (var p in grp) {
                            double r  = (90.0 - p.AltitudeDeg) / 90.0;
                            double ar = p.AzimuthDeg * Math.PI / 180.0;
                            sc.Points.Add(new ScatterPoint(r * Math.Sin(ar), r * Math.Cos(ar)));
                        }
                        model.Series.Add(sc);
                    }
                } else {
                    var sc = new ScatterSeries { MarkerType = MarkerType.Circle, MarkerSize = 5, MarkerFill = OxyColors.Cyan };
                    foreach (var p in points) {
                        double r  = (90.0 - p.AltitudeDeg) / 90.0;
                        double ar = p.AzimuthDeg * Math.PI / 180.0;
                        sc.Points.Add(new ScatterPoint(r * Math.Sin(ar), r * Math.Cos(ar)));
                    }
                    model.Series.Add(sc);
                }
            }

            // ── Residual error arrows ─────────────────────────────────────────────
            if (residuals != null && residuals.Count > 0) {
                double maxErr = residuals.Max(p => p.TotalErrorArcsec);
                double scale  = maxErr > 0 ? 0.4 / maxErr * errorArrowScale : 0;
                foreach (var rp in residuals) {
                    double r  = (90.0 - rp.AltitudeDeg) / 90.0;
                    double ar = rp.AzimuthDeg * Math.PI / 180.0;
                    double x  = r * Math.Sin(ar);
                    double y  = r * Math.Cos(ar);
                    var arrow = new LineSeries { Color = OxyColors.Yellow, StrokeThickness = 1.5 };
                    arrow.Points.Add(new DataPoint(x, y));
                    arrow.Points.Add(new DataPoint(x + rp.ErrorRAArcsec  * scale * 0.001,
                                                   y + rp.ErrorDecArcsec * scale * 0.001));
                    model.Series.Add(arrow);
                }
            }

            // ── Mount crosshair ───────────────────────────────────────────────────
            if (mountConnected && mountAltDeg > 0) {
                double r  = (90.0 - mountAltDeg) / 90.0;
                double ar = mountAzDeg * Math.PI / 180.0;
                double mx = r * Math.Sin(ar);
                double my = r * Math.Cos(ar);

                var ring = new LineSeries { Color = OxyColors.White, StrokeThickness = 1.5 };
                for (int a = 0; a <= 360; a += 5) {
                    double ra = a * Math.PI / 180.0;
                    ring.Points.Add(new DataPoint(mx + 0.04 * Math.Cos(ra), my + 0.04 * Math.Sin(ra)));
                }
                model.Series.Add(ring);

                var ch = new LineSeries { Color = OxyColors.White, StrokeThickness = 1.5 };
                ch.Points.Add(new DataPoint(mx - 0.09,  my));   ch.Points.Add(new DataPoint(mx - 0.045, my));
                ch.Points.Add(new DataPoint(double.NaN, double.NaN));
                ch.Points.Add(new DataPoint(mx + 0.045, my));   ch.Points.Add(new DataPoint(mx + 0.09,  my));
                ch.Points.Add(new DataPoint(double.NaN, double.NaN));
                ch.Points.Add(new DataPoint(mx, my - 0.09));    ch.Points.Add(new DataPoint(mx, my - 0.045));
                ch.Points.Add(new DataPoint(double.NaN, double.NaN));
                ch.Points.Add(new DataPoint(mx, my + 0.045));   ch.Points.Add(new DataPoint(mx, my + 0.09));
                model.Series.Add(ch);
            }

            return model;
        }

        // Green = solved/visited (user requested), Cyan = pending, Yellow = in-progress, Red = failed
        private static OxyColor PointColour(AlignmentPointState s) => s switch {
            AlignmentPointState.Added                                          => OxyColors.LimeGreen,
            AlignmentPointState.Failed or AlignmentPointState.FailedRMS       => OxyColors.Red,
            AlignmentPointState.Slewing  or AlignmentPointState.Settling  or
            AlignmentPointState.Exposing or AlignmentPointState.PlateSolving or
            AlignmentPointState.Uploading                                      => OxyColors.Yellow,
            _                                                                  => OxyColors.Cyan
        };

        private static void AddMeridianSector(PlotModel model, double centerAzDeg, double halfWidthDeg) {
            var ann = new PolygonAnnotation { Fill = OxyColor.FromAColor(40, OxyColors.CornflowerBlue),
                                             Stroke = OxyColors.Transparent, StrokeThickness = 0 };
            ann.Points.Add(new DataPoint(0, 0));
            for (double az = centerAzDeg - halfWidthDeg; az <= centerAzDeg + halfWidthDeg; az += 1) {
                double ar = az * Math.PI / 180.0;
                ann.Points.Add(new DataPoint(Math.Sin(ar), Math.Cos(ar)));
            }
            model.Annotations.Add(ann);
        }

        // ── Residuals scatter plot ────────────────────────────────────────────────

        private static PlotModel BuildResidualScatter(IReadOnlyList<ResidualPoint> residuals) {
            var model = new PlotModel {
                Title      = "Residuals",
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
                Text = $"RMS {rms:F1}\"", TextPosition = new DataPoint(0, 0),
                TextColor = OxyColors.OrangeRed, FontSize = 10
            });

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

        // ── Base sky model (rings, cardinals, axes) ───────────────────────────────

        internal static PlotModel CreateBaseSkyModel() {
            var model = new PlotModel {
                PlotType            = PlotType.Cartesian,
                Background          = OxyColor.FromRgb(0x0a, 0x0a, 0x1e),
                TextColor           = OxyColors.LightGray,
                PlotAreaBorderColor = OxyColors.Transparent
            };
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Minimum = -1.1, Maximum = 1.1, IsAxisVisible = false });
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Left,   Minimum = -1.1, Maximum = 1.1, IsAxisVisible = false });

            foreach (var alt in new[] { 0, 20, 40, 60, 80 }) {
                double r    = (90.0 - alt) / 90.0;
                var    ring = new LineSeries { Color = OxyColor.FromAColor(60, OxyColors.Gray), StrokeThickness = 0.5 };
                for (int a = 0; a <= 360; a += 3) {
                    double rad = a * Math.PI / 180.0;
                    ring.Points.Add(new DataPoint(r * Math.Sin(rad), r * Math.Cos(rad)));
                }
                model.Series.Add(ring);
                model.Annotations.Add(new TextAnnotation {
                    Text         = $"{alt}°",
                    TextPosition = new DataPoint(r * 0.05, r),
                    FontSize     = 9,
                    TextColor    = OxyColor.FromAColor(100, OxyColors.Gray)
                });
            }

            var cardinals = new[] { "N", "", "", "E", "", "", "S", "", "", "W", "", "" };
            for (int i = 0; i < 12; i++) {
                double az    = i * 30.0;
                double azRad = az * Math.PI / 180.0;
                var    spoke = new LineSeries { Color = OxyColor.FromAColor(60, OxyColors.Gray), StrokeThickness = 0.5 };
                spoke.Points.Add(new DataPoint(0, 0));
                spoke.Points.Add(new DataPoint(Math.Sin(azRad), Math.Cos(azRad)));
                model.Series.Add(spoke);
                if (cardinals[i].Length > 0)
                    model.Annotations.Add(new TextAnnotation {
                        Text         = cardinals[i],
                        TextPosition = new DataPoint(1.07 * Math.Sin(azRad), 1.07 * Math.Cos(azRad)),
                        FontSize     = 10, FontWeight = FontWeights.Bold,
                        TextColor    = OxyColors.LightGray
                    });
            }
            return model;
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
