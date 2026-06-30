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
    [ExportMetadata("Name", "OnStepX Star Alignment")]
    [ExportMetadata("Description", "Automated plate-solve-based star alignment - uploads corrected star pairs to the OnStepX controller (1–9 stars)")]
    [ExportMetadata("Icon", "PolarAlignSVG")]
    [ExportMetadata("Category", "OnStepX Tools")]
    public class BuildStarAlignment : SequenceItem {
        private readonly IOnStepXMount          _mount;
        private readonly ITelescopeMediator     _telescope;
        private readonly IImagingMediator       _imaging;
        private readonly IPlateSolverFactory    _solverFactory;
        private readonly IProfileService        _profile;
        private readonly IModelBuilderMediator  _mediator;
        private readonly IWeatherDataMediator   _weather;
        private readonly PointGenerationViewModel? _pointGenVM;

        private ObservableCollection<AlignmentPoint> _points = new();
        private bool _saveToEeprom = true;

        [ImportingConstructor]
        public BuildStarAlignment(
            IOnStepXMount mount,
            ITelescopeMediator telescope,
            IImagingMediator imaging,
            IPlateSolverFactory solverFactory,
            IProfileService profile,
            IModelBuilderMediator mediator,
            IWeatherDataMediator weather,
            [Import(AllowDefault = true)] PointGenerationViewModel? pointGenVM) {
            _mount         = mount;
            _telescope     = telescope;
            _imaging       = imaging;
            _solverFactory = solverFactory;
            _profile       = profile;
            _mediator      = mediator;
            _weather       = weather;
            _pointGenVM    = pointGenVM;

            LoadPointsCommand   = new RelayCommand(_ => LoadPoints());
            UseGeneratedCommand = new RelayCommand(_ => UseGeneratedPoints(), _ => pointGenVM?.Points.Count > 0);
        }

        public ObservableCollection<AlignmentPoint> Points {
            get => _points;
            set => SetField(ref _points, value);
        }

        public bool SaveToEeprom {
            get => _saveToEeprom;
            set => SetField(ref _saveToEeprom, value);
        }

        public ICommand LoadPointsCommand   { get; }
        public ICommand UseGeneratedCommand { get; }

        protected bool SetField<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null) {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            RaisePropertyChanged(name);
            return true;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (_points.Count == 0) throw new InvalidOperationException("No alignment points loaded.");

            var opts = new ModelBuilderOptions {
                Mode                     = BuildMode.StarAlignment,
                ExposureTimeSeconds      = _profile.ActiveProfile.PlateSolveSettings.ExposureTime,
                SaveToEepromOnCompletion = SaveToEeprom
            };

            var builder = new ModelBuilder(_mount, _telescope, _imaging, _solverFactory, _profile, _mediator, _weather);
            await builder.BuildModelAsync(new List<AlignmentPoint>(_points), opts, progress, token);
        }

        private void LoadPoints() {
            var dlg = new OpenFileDialog { Filter = "JSON|*.json", Title = "Load Alignment Points" };
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

        public override void ResetProgress() {
            base.ResetProgress();
            foreach (var point in _points)
                point.State = AlignmentPointState.Pending;
            if (_points.Count > 0)
                _mediator.NotifyPointsLoaded(new PointsLoadedEventArgs { Points = _points });
        }

        public override object Clone() {
            var clone = new BuildStarAlignment(_mount, _telescope, _imaging, _solverFactory, _profile, _mediator, _weather, _pointGenVM) {
                SaveToEeprom = SaveToEeprom,
                Points       = new ObservableCollection<AlignmentPoint>(_points)
            };
            clone.CopyMetaData(this);
            return clone;
        }

        public override string ToString() => "OnStepX Star Alignment";
    }
}
