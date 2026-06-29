using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Plugin.OnStepXTools.Interfaces;
using NINA.Plugin.OnStepXTools.Model;
using NINA.Plugin.OnStepXTools.ModelManagement;
using NINA.Plugin.OnStepXTools.ViewModels;

namespace NINA.Plugin.OnStepXTools.SequenceItems {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "OnStepX Full-Sky Pointing Model")]
    [ExportMetadata("Description", "Automated plate-solve-based all-sky pointing model - fits 11 parameters via least-squares and uploads coefficients to the controller")]
    [ExportMetadata("Icon", "CrosshairSVG")]
    [ExportMetadata("Category", "OnStepX Tools")]
    public class BuildPointingModel : SequenceItem {
        private readonly IOnStepXMount          _mount;
        private readonly ITelescopeMediator     _telescope;
        private readonly IImagingMediator       _imaging;
        private readonly IPlateSolverFactory    _solverFactory;
        private readonly IProfileService        _profile;
        private readonly IModelBuilderMediator  _mediator;
        private readonly PointGenerationViewModel? _pointGenVM;

        private static readonly string DefaultCoeffsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NINA", "Plugins", "OnStepX", "LastPointingModel.json");

        private ObservableCollection<AlignmentPoint> _points = new();
        private bool   _writeToMount             = true;
        private bool   _resumeLastSession        = false;
        private bool   _saveCoefficientsToFile   = false;
        private string _coefficientsFilePath     = DefaultCoeffsPath;

        [ImportingConstructor]
        public BuildPointingModel(
            IOnStepXMount mount,
            ITelescopeMediator telescope,
            IImagingMediator imaging,
            IPlateSolverFactory solverFactory,
            IProfileService profile,
            IModelBuilderMediator mediator,
            [Import(AllowDefault = true)] PointGenerationViewModel? pointGenVM) {
            _mount         = mount;
            _telescope     = telescope;
            _imaging       = imaging;
            _solverFactory = solverFactory;
            _profile       = profile;
            _mediator      = mediator;
            _pointGenVM    = pointGenVM;

            LoadPointsCommand         = new ViewModels.RelayCommand(_ => LoadPoints());
            UseGeneratedCommand       = new ViewModels.RelayCommand(_ => UseGeneratedPoints(), _ => pointGenVM?.Points.Count > 0);
            BrowseSavePathCommand     = new ViewModels.RelayCommand(_ => BrowseSavePath());
        }

        // ── Properties ───────────────────────────────────────────────────────────

        public ObservableCollection<AlignmentPoint> Points {
            get => _points;
            set {
                if (SetField(ref _points, value))
                    _mediator.NotifyPointsLoaded(new PointsLoadedEventArgs { Points = value });
            }
        }

        public bool   WriteToMount            { get => _writeToMount;           set => SetField(ref _writeToMount,           value); }
        public bool   ResumeLastSession       { get => _resumeLastSession;      set => SetField(ref _resumeLastSession,      value); }

        public bool   SaveCoefficientsToFile  {
            get => _saveCoefficientsToFile;
            set { SetField(ref _saveCoefficientsToFile, value); RaisePropertyChanged(nameof(ShowSavePath)); }
        }

        public string CoefficientsFilePath    { get => _coefficientsFilePath;   set => SetField(ref _coefficientsFilePath,   value); }

        // Controls whether the file-path row is visible in the UI
        public bool   ShowSavePath => _saveCoefficientsToFile;

        // ── Commands ─────────────────────────────────────────────────────────────

        public ICommand LoadPointsCommand     { get; }
        public ICommand UseGeneratedCommand   { get; }
        public ICommand BrowseSavePathCommand { get; }

        // ── SetField helper (SequenceItem doesn't expose one) ────────────────────

        protected bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null) {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            RaisePropertyChanged(name);
            return true;
        }

        // ── Execute ──────────────────────────────────────────────────────────────

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (_points.Count == 0)
                throw new InvalidOperationException("No pointing model points loaded.");

            string? resumeId = null;
            if (ResumeLastSession) {
                var store = new ModelBuildSessionStore();
                var ids   = store.ListSessionIds();
                if (ids.Length > 0) resumeId = ids[^1];
            }

            var opts = new ModelBuilderOptions {
                Mode                          = BuildMode.FullSkyPointingModel,
                ExposureTimeSeconds           = _profile.ActiveProfile.PlateSolveSettings.ExposureTime,
                WriteModelToMountOnCompletion = WriteToMount,
                SaveToEepromOnCompletion      = false,
                ResumeSessionId               = resumeId
            };

            var builder = new ModelBuilder(_mount, _telescope, _imaging, _solverFactory, _profile, _mediator);
            var coefficients = await builder.BuildModelAsync(
                new List<AlignmentPoint>(_points), opts, progress, token);

            // Save coefficients to file so they can be loaded in Model Info View
            if (SaveCoefficientsToFile && coefficients != null) {
                try {
                    var dir = Path.GetDirectoryName(CoefficientsFilePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    var tmp = CoefficientsFilePath + ".tmp";
                    File.WriteAllText(tmp, JsonConvert.SerializeObject(coefficients, Formatting.Indented));
                    File.Move(tmp, CoefficientsFilePath, overwrite: true);
                    progress?.Report(new ApplicationStatus {
                        Status = $"Coefficients saved → {CoefficientsFilePath}"
                    });
                } catch (Exception ex) {
                    Logger.Error($"Failed to save pointing model coefficients: {ex.Message}");
                }
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private void LoadPoints() {
            var dlg = new OpenFileDialog { Filter = "JSON|*.json", Title = "Load Pointing Model Points" };
            if (dlg.ShowDialog() != true) return;
            try {
                var loaded = JsonConvert.DeserializeObject<List<AlignmentPoint>>(File.ReadAllText(dlg.FileName));
                if (loaded != null) Points = new ObservableCollection<AlignmentPoint>(loaded);
            } catch { }
        }

        private void UseGeneratedPoints() {
            if (_pointGenVM?.Points != null)
                Points = new ObservableCollection<AlignmentPoint>(_pointGenVM.Points);
        }

        private void BrowseSavePath() {
            var dlg = new SaveFileDialog {
                Filter           = "JSON|*.json",
                Title            = "Save Pointing Model Coefficients",
                FileName         = Path.GetFileName(CoefficientsFilePath),
                InitialDirectory = Path.GetDirectoryName(CoefficientsFilePath) ?? string.Empty
            };
            if (dlg.ShowDialog() == true)
                CoefficientsFilePath = dlg.FileName;
        }

        // ── Clone ─────────────────────────────────────────────────────────────────

        public override void ResetProgress() {
            base.ResetProgress();
            foreach (var point in _points)
                point.State = AlignmentPointState.Pending;
            // Refresh the Model Info sky chart so all points return to green
            if (_points.Count > 0)
                _mediator.NotifyPointsLoaded(new PointsLoadedEventArgs { Points = _points });
        }

        public override object Clone() {
            var clone = new BuildPointingModel(
                    _mount, _telescope, _imaging, _solverFactory, _profile, _mediator, _pointGenVM) {
                WriteToMount           = WriteToMount,
                ResumeLastSession      = ResumeLastSession,
                SaveCoefficientsToFile = SaveCoefficientsToFile,
                CoefficientsFilePath   = CoefficientsFilePath,
                Points                 = new ObservableCollection<AlignmentPoint>(_points)
            };
            clone.CopyMetaData(this);
            return clone;
        }

        public override string ToString() => "OnStepX Full-Sky Pointing Model";
    }
}
