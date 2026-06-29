using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json;
using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using NINA.Plugin.OnStepXTools.Model;
using NINA.Plugin.OnStepXTools.ModelManagement;

namespace NINA.Plugin.OnStepXTools.ViewModels {

    [Export(typeof(IDockableVM))]
    [Export(typeof(PointGenerationViewModel))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class PointGenerationViewModel : DockableVM {
        private readonly IProfileService     _profile;
        private readonly ModelPointGenerator _generator = new();

        private PointGenerationOptions _options = new();
        private ObservableCollection<AlignmentPoint> _points = new();
        private GenerationMethod _selectedMethod = GenerationMethod.GoldenSpiral;
        private BuildMode _selectedMode = BuildMode.FullSkyPointingModel;

        public override string ContentId => "OnStepX_PointGeneration";

        [ImportingConstructor]
        public PointGenerationViewModel(IProfileService profile)
            : base(profile) {
            Title         = "OnStepX Point Generation";
            ImageGeometry = System.Windows.Application.Current?.Resources["PolarAlignSVG"] as System.Windows.Media.GeometryGroup;
            _profile      = profile;

            // Pre-populate from NINA's meridian flip setting so the user sees the current value
            _meridianExclusionDeg = new HorizonAndMeridianFilter(profile).MeridianExclusionHalfWidthDeg();

            GenerateCommand = new RelayCommand(_ => Generate());
            SaveCommand     = new RelayCommand(_ => SavePoints(), _ => _points.Count > 0);
            LoadCommand     = new RelayCommand(_ => LoadPoints());
        }

        // ── Options and algorithm selection ──────────────────────────────────────

        public PointGenerationOptions Options {
            get => _options;
            set => SetProperty(ref _options, value);
        }

        // Separate property so the algorithm combobox can trigger IsSiderealPath update
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

        // Mode selector - adjusts MaxPointCount and clamps PointCount on switch
        public BuildMode SelectedMode {
            get => _selectedMode;
            set {
                if (SetProperty(ref _selectedMode, value)) {
                    Options.Mode = value;
                    RaisePropertyChanged(nameof(MaxPointCount));
                    RaisePropertyChanged(nameof(TickFrequency));
                    // Clamp when switching to Star Alignment
                    if (value == BuildMode.StarAlignment && Options.PointCount > 9)
                        Options.PointCount = 9;
                    // Restore sensible default when switching to Full-Sky
                    else if (value == BuildMode.FullSkyPointingModel && Options.PointCount < 10)
                        Options.PointCount = 50;
                    RaisePropertyChanged(nameof(Options));
                }
            }
        }

        public int    MaxPointCount  => _selectedMode == BuildMode.StarAlignment ? 9   : 300;
        public double TickFrequency  => _selectedMode == BuildMode.StarAlignment ? 1.0 : 20.0;

        private double _meridianExclusionDeg;
        public double MeridianExclusionDeg {
            get => _meridianExclusionDeg;
            set {
                if (SetProperty(ref _meridianExclusionDeg, Math.Max(0, value)))
                    RaisePropertyChanged(nameof(SkyPlot));
            }
        }

        public ObservableCollection<AlignmentPoint> Points {
            get => _points;
            private set => SetProperty(ref _points, value);
        }

        // SkyPlot is computed fresh on each property access (avoids OxyPlot single-owner crash)
        private IReadOnlyList<AlignmentPoint> _lastPoints = Array.Empty<AlignmentPoint>();
        public PlotModel SkyPlot => BuildSkyPlot(_lastPoints, _profile, _meridianExclusionDeg);

        public ICommand GenerateCommand { get; }
        public ICommand SaveCommand     { get; }
        public ICommand LoadCommand     { get; }

        // ── Generation ───────────────────────────────────────────────────────────

        private void Generate() {
            var filter = new HorizonAndMeridianFilter(_profile);

            Options.SiteLatitudeDeg           = _profile.ActiveProfile.AstrometrySettings.Latitude;
            Options.MeridianExclusionHalfWidthDeg = _meridianExclusionDeg;
            Options.Method                    = _selectedMethod;

            var result = _generator.Generate(Options, filter);
            _lastPoints = result;
            Points      = new ObservableCollection<AlignmentPoint>(result);
            RaisePropertyChanged(nameof(SkyPlot));
        }

        private void SavePoints() {
            var dlg = new SaveFileDialog { Filter = "JSON|*.json", Title = "Save Point List" };
            if (dlg.ShowDialog() != true) return;
            var json = JsonConvert.SerializeObject(new List<AlignmentPoint>(_points), Formatting.Indented);
            File.WriteAllText(dlg.FileName, json);
        }

        private void LoadPoints() {
            var dlg = new OpenFileDialog { Filter = "JSON|*.json", Title = "Load Point List" };
            if (dlg.ShowDialog() != true) return;
            try {
                var loaded = JsonConvert.DeserializeObject<List<AlignmentPoint>>(File.ReadAllText(dlg.FileName));
                if (loaded != null) {
                    _lastPoints = loaded;
                    Points      = new ObservableCollection<AlignmentPoint>(loaded);
                    RaisePropertyChanged(nameof(SkyPlot));
                }
            } catch { }
        }

        // ── Sky chart ─────────────────────────────────────────────────────────────

        internal static PlotModel BuildSkyPlot(
            IReadOnlyList<AlignmentPoint> points,
            IProfileService profile,
            double meridianExclusionDeg = 0) {

            var model = CreateBaseSkyModel();

            double mExcl = meridianExclusionDeg;

            // ── Custom horizon polygon ────────────────────────────────────────────
            var horizon = profile.ActiveProfile.AstrometrySettings.Horizon;
            if (horizon != null) {
                var horizonSeries = new AreaSeries {
                    Color           = OxyColor.FromAColor(80, OxyColors.Gray),
                    Fill            = OxyColor.FromAColor(50, OxyColors.DarkSlateGray),
                    StrokeThickness = 1
                };
                for (int az = 0; az <= 360; az += 2) {
                    var alt   = horizon.GetAltitude(az);
                    var r     = (90.0 - alt) / 90.0;
                    var azRad = az * Math.PI / 180.0;
                    horizonSeries.Points.Add(new DataPoint(r * Math.Sin(azRad), r * Math.Cos(azRad)));
                }
                // Close to the outer rim (alt=0)
                for (int az = 360; az >= 0; az -= 2) {
                    var azRad = az * Math.PI / 180.0;
                    horizonSeries.Points2.Add(new DataPoint(Math.Sin(azRad), Math.Cos(azRad)));
                }
                model.Series.Add(horizonSeries);
            }

            // ── Meridian exclusion zone (light blue sectors) ──────────────────────
            if (mExcl > 0) {
                // North sector (centred on Az=0)
                AddMeridianSector(model, centerAzDeg: 0,   halfWidthDeg: mExcl);
                // South sector (centred on Az=180)
                AddMeridianSector(model, centerAzDeg: 180, halfWidthDeg: mExcl);
            }

            // ── Meridian line (N → Zenith → S) ────────────────────────────────────
            var meridianLine = new LineSeries {
                Color           = OxyColors.CornflowerBlue,
                StrokeThickness = 1,
                LineStyle       = LineStyle.Dash
            };
            meridianLine.Points.Add(new DataPoint(0,  1));  // North horizon
            meridianLine.Points.Add(new DataPoint(0,  0));  // Zenith
            meridianLine.Points.Add(new DataPoint(0, -1));  // South horizon
            model.Series.Add(meridianLine);

            // ── Visit-order path (drawn before points so it sits behind them) ────
            // Connects points in Index order so the user can verify the planned slew path.
            if (points.Count > 1) {
                var path = new LineSeries {
                    Color           = OxyColor.FromAColor(80, OxyColors.CornflowerBlue),
                    StrokeThickness = 1,
                    LineStyle       = LineStyle.Solid
                };
                foreach (var p in points.OrderBy(pt => pt.Index)) {
                    double r     = (90.0 - p.AltitudeDeg) / 90.0;
                    double azRad = p.AzimuthDeg * Math.PI / 180.0;
                    path.Points.Add(new DataPoint(r * Math.Sin(azRad), r * Math.Cos(azRad)));
                }
                model.Series.Add(path);
            }

            // ── Generated points ──────────────────────────────────────────────────
            if (points.Count > 0) {
                var scatter = new ScatterSeries {
                    MarkerType = MarkerType.Circle,
                    MarkerSize = 5,
                    MarkerFill = OxyColors.Cyan
                };
                foreach (var p in points) {
                    var r     = (90.0 - p.AltitudeDeg) / 90.0;
                    var azRad = p.AzimuthDeg * Math.PI / 180.0;
                    scatter.Points.Add(new ScatterPoint(r * Math.Sin(azRad), r * Math.Cos(azRad)));
                }
                model.Series.Add(scatter);
            }

            return model;
        }

        // Draws a filled wedge from the zenith out to the horizon, centred on centerAzDeg.
        private static void AddMeridianSector(PlotModel model, double centerAzDeg, double halfWidthDeg) {
            var ann = new PolygonAnnotation {
                Fill            = OxyColor.FromAColor(40, OxyColors.CornflowerBlue),
                Stroke          = OxyColors.Transparent,
                StrokeThickness = 0
            };

            ann.Points.Add(new DataPoint(0, 0)); // Zenith
            for (double az = centerAzDeg - halfWidthDeg; az <= centerAzDeg + halfWidthDeg; az += 1) {
                double azRad = az * Math.PI / 180.0;
                ann.Points.Add(new DataPoint(Math.Sin(azRad), Math.Cos(azRad)));
            }
            model.Annotations.Add(ann);
        }

        internal static PlotModel CreateBaseSkyModel() {
            var model = new PlotModel {
                // Cartesian ensures X and Y have equal unit length → circle never becomes ellipse
                PlotType            = PlotType.Cartesian,
                Background          = OxyColor.FromRgb(0x0a, 0x0a, 0x1e),
                TextColor           = OxyColors.LightGray,
                PlotAreaBorderColor = OxyColors.Transparent
            };
            model.Axes.Add(new LinearAxis {
                Position = AxisPosition.Bottom, Minimum = -1.1, Maximum = 1.1, IsAxisVisible = false
            });
            model.Axes.Add(new LinearAxis {
                Position = AxisPosition.Left, Minimum = -1.1, Maximum = 1.1, IsAxisVisible = false
            });

            // Altitude rings at 0°, 20°, 40°, 60°, 80°
            foreach (var alt in new[] { 0, 20, 40, 60, 80 }) {
                var r    = (90.0 - alt) / 90.0;
                var ring = new LineSeries {
                    Color = OxyColor.FromAColor(60, OxyColors.Gray), StrokeThickness = 0.5
                };
                for (int a = 0; a <= 360; a += 3) {
                    var rad = a * Math.PI / 180.0;
                    ring.Points.Add(new DataPoint(r * Math.Sin(rad), r * Math.Cos(rad)));
                }
                model.Series.Add(ring);
                // Altitude label
                model.Annotations.Add(new TextAnnotation {
                    Text         = $"{alt}°",
                    TextPosition = new DataPoint(r * 0.05, r),
                    FontSize     = 9,
                    TextColor    = OxyColor.FromAColor(100, OxyColors.Gray)
                });
            }

            // Azimuth spokes with cardinal labels
            var cardinals = new[] { "N", "", "", "E", "", "", "S", "", "", "W", "", "" };
            for (int i = 0; i < 12; i++) {
                var az    = i * 30.0;
                var azRad = az * Math.PI / 180.0;
                var spoke = new LineSeries {
                    Color = OxyColor.FromAColor(60, OxyColors.Gray), StrokeThickness = 0.5
                };
                spoke.Points.Add(new DataPoint(0, 0));
                spoke.Points.Add(new DataPoint(Math.Sin(azRad), Math.Cos(azRad)));
                model.Series.Add(spoke);

                if (cardinals[i].Length > 0) {
                    model.Annotations.Add(new TextAnnotation {
                        Text         = cardinals[i],
                        TextPosition = new DataPoint(1.07 * Math.Sin(azRad), 1.07 * Math.Cos(azRad)),
                        FontSize     = 10,
                        FontWeight   = FontWeights.Bold,
                        TextColor    = OxyColors.LightGray
                    });
                }
            }
            return model;
        }
    }
}
