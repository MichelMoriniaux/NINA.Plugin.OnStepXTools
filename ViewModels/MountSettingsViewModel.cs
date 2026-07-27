using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using NINA.Plugin.OnStepXTools.Interfaces;
using NINA.Plugin.OnStepXTools.Model;
namespace NINA.Plugin.OnStepXTools.ViewModels {

    [Export(typeof(IDockableVM))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class MountSettingsViewModel : DockableVM, IDisposable {
        private readonly IProfileService _profile;
        private readonly IOnStepXMount   _mount;

        // ── Sky model state ──────────────────────────────────────────────────────
        private AlignmentModelCoefficients? _coefficients;

        public AlignmentModelCoefficients? Coefficients {
            get => _coefficients;
            private set {
                SetProperty(ref _coefficients, value);
                RaisePropertyChanged(nameof(HasModel));
            }
        }

        public bool HasModel => _coefficients != null;

        public override string ContentId => "OnStepX_MountSettings";

        [ImportingConstructor]
        public MountSettingsViewModel(IProfileService profile, IOnStepXMount mount)
            : base(profile) {
            Title         = "OnStepX Mount Settings";
            ImageGeometry = System.Windows.Application.Current?.Resources["SettingsSVG"] as System.Windows.Media.GeometryGroup;
            _profile      = profile;
            _mount        = mount;
            _mount.State.PropertyChanged += OnMountStateChanged;
            BuildCommands();
        }

        // Settings that are also edit targets only ever get (re)synced from the mount on connect
        // or a manual reload (OnStepXMount.ReloadSettingsAsync) - never on the periodic telemetry
        // tick - so this can never clobber an edit the user hasn't submitted yet. Pure read-only
        // passthrough properties share their exact name with the State property and are just
        // re-raised as-is.
        private void OnMountStateChanged(object? sender, PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case nameof(OnStepXMountState.MountType):
                    Dispatch(() => MountType = _mount.State.MountType); break;
                case nameof(OnStepXMountState.TrackingRate):
                    Dispatch(() => TrackingRate = _mount.State.TrackingRate); break;
                case nameof(OnStepXMountState.CompensatedTracking):
                    Dispatch(() => CompensatedTracking = _mount.State.CompensatedTracking); break;
                case nameof(OnStepXMountState.CompensatedTrackingAxis):
                    Dispatch(() => CompensatedTrackingAxis = _mount.State.CompensatedTrackingAxis); break;
                case nameof(OnStepXMountState.GuideRateIndex):
                    Dispatch(() => GuideRateIndex = _mount.State.GuideRateIndex); break;
                case nameof(OnStepXMountState.AutoMeridianFlip):
                    Dispatch(() => AutoMeridianFlip = _mount.State.AutoMeridianFlip); break;
                case nameof(OnStepXMountState.PauseAtHome):
                    Dispatch(() => PauseAtHome = _mount.State.PauseAtHome); break;
                case nameof(OnStepXMountState.PreferredPierSide):
                    Dispatch(() => PreferredPierSide = _mount.State.PreferredPierSide); break;
                case nameof(OnStepXMountState.BuzzerEnabled):
                    Dispatch(() => BuzzerEnabled = _mount.State.BuzzerEnabled); break;
                case nameof(OnStepXMountState.BacklashAxis1Arcsec):
                    Dispatch(() => BacklashAxis1Arcsec = _mount.State.BacklashAxis1Arcsec); break;
                case nameof(OnStepXMountState.BacklashAxis2Arcsec):
                    Dispatch(() => BacklashAxis2Arcsec = _mount.State.BacklashAxis2Arcsec); break;
                case nameof(OnStepXMountState.LimitMinAltDeg):
                    Dispatch(() => LimitMinAltDeg = _mount.State.LimitMinAltDeg); break;
                case nameof(OnStepXMountState.LimitMaxAltDeg):
                    Dispatch(() => LimitMaxAltDeg = _mount.State.LimitMaxAltDeg); break;
                case nameof(OnStepXMountState.LimitEastPastMeridian):
                    Dispatch(() => LimitEastPastMeridian = _mount.State.LimitEastPastMeridian); break;
                case nameof(OnStepXMountState.LimitWestPastMeridian):
                    Dispatch(() => LimitWestPastMeridian = _mount.State.LimitWestPastMeridian); break;
                default:
                    // Pure passthrough properties (IsConnected, Axis1/2DriverId, Axis1/2Params,
                    // LatitudeDMS/LongitudeDMS/Elevation, RuntimeAxisConfigEnabled, TrackingEnabled,
                    // IsOldFormat, IsServoCalibrationSupported) share the State property's name.
                    Dispatch(() => RaisePropertyChanged(e.PropertyName));
                    break;
            }
        }

        // ── Connectivity ─────────────────────────────────────────────────────────

        public bool IsConnected => _mount.State.IsConnected;

        // ── Axis Config ──────────────────────────────────────────────────────────

        private MountType _mountType = MountType.GEM;
        public MountType MountType { get => _mountType; set => SetProperty(ref _mountType, value); }

        public string Axis1DriverId => _mount.State.Axis1DriverId;
        public string Axis2DriverId => _mount.State.Axis2DriverId;

        // Whether motor/axis parameters can be changed at runtime (:SXAC,0#) or are fixed at
        // their Config.h compile-time values (:SXAC,1#). The setter only sends the command when
        // the user actually toggles the checkbox - state loaded from the mount updates State
        // directly (bypassing this setter), so loading never re-sends :SXAC# to the controller.
        public bool RuntimeAxisConfigEnabled {
            get => _mount.State.RuntimeAxisConfigEnabled;
            set {
                if (_mount.State.RuntimeAxisConfigEnabled == value) return;
                _ = _mount.SetRuntimeAxisConfigAsync(value);
                StatusMessage = value
                    ? "Runtime axis configuration enabled."
                    : "Runtime axis configuration disabled - using Config.h values.";
            }
        }

        public ObservableCollection<AxisParameter> Axis1Params => _mount.State.Axis1Params;
        public ObservableCollection<AxisParameter> Axis2Params => _mount.State.Axis2Params;

        // ── Site Location ────────────────────────────────────────────────────────
        // Read-only display of what the mount currently has (populated on connect)

        public string LatitudeDMS  => _mount.State.LatitudeDMS;
        public string LongitudeDMS => _mount.State.LongitudeDMS;
        public string Elevation    => _mount.State.Elevation;

        // ── Tracking ─────────────────────────────────────────────────────────────

        public bool TrackingEnabled => _mount.State.TrackingEnabled;

        private TrackingRate _trackingRate = TrackingRate.Sidereal;
        private CompensatedTracking _compensatedTracking = CompensatedTracking.Off;
        private CompensatedTrackingAxis _compensatedTrackingAxis = CompensatedTrackingAxis.Single;

        public TrackingRate     TrackingRate        { get => _trackingRate;        set => SetProperty(ref _trackingRate,        value); }
        public CompensatedTracking CompensatedTracking { get => _compensatedTracking; set => SetProperty(ref _compensatedTracking, value); }
        public CompensatedTrackingAxis CompensatedTrackingAxis { get => _compensatedTrackingAxis; set => SetProperty(ref _compensatedTrackingAxis, value); }

        // ── Guide Rate ───────────────────────────────────────────────────────────

        private int _guideRateIndex = 2; // 0-9; index 2 = 1× sidereal
        public int GuideRateIndex { get => _guideRateIndex; set => SetProperty(ref _guideRateIndex, value); }

        public static string[] GuideRateNames { get; } =
            { "0.25×", "0.5×", "1×", "2×", "4×", "8×", "20×", "48×", "VF", "VVF" };

        // ── Slew Speed ───────────────────────────────────────────────────────────
        // Pure edit buffer - OnStepX doesn't report a "current" slew speed preset back, so there's
        // nothing in State to sync this from.

        private SlewSpeed _slewSpeed = SlewSpeed.Normal;
        public SlewSpeed SlewSpeed { get => _slewSpeed; set => SetProperty(ref _slewSpeed, value); }

        // ── Meridian Flip ────────────────────────────────────────────────────────

        private bool _autoMeridianFlip;
        private bool _pauseAtHome;
        private PreferredPierSide _preferredPierSide = PreferredPierSide.Best;

        public bool             AutoMeridianFlip  { get => _autoMeridianFlip;  set => SetProperty(ref _autoMeridianFlip,  value); }
        public bool             PauseAtHome       { get => _pauseAtHome;       set => SetProperty(ref _pauseAtHome,       value); }
        public PreferredPierSide PreferredPierSide { get => _preferredPierSide; set => SetProperty(ref _preferredPierSide, value); }

        // ── Alert ────────────────────────────────────────────────────────────────

        private bool _buzzerEnabled;
        public bool BuzzerEnabled { get => _buzzerEnabled; set => SetProperty(ref _buzzerEnabled, value); }

        // ── Backlash ─────────────────────────────────────────────────────────────

        private int _backlashAxis1;
        private int _backlashAxis2;
        public int BacklashAxis1Arcsec { get => _backlashAxis1; set => SetProperty(ref _backlashAxis1, value); }
        public int BacklashAxis2Arcsec { get => _backlashAxis2; set => SetProperty(ref _backlashAxis2, value); }

        // ── Limits ───────────────────────────────────────────────────────────────

        private int _limitMinAlt;
        private int _limitMaxAlt = 85;
        private double _limitEastDeg;
        private double _limitWestDeg = 15;

        public int LimitMinAltDeg        { get => _limitMinAlt;   set => SetProperty(ref _limitMinAlt,   value); }
        public int LimitMaxAltDeg        { get => _limitMaxAlt;   set => SetProperty(ref _limitMaxAlt,   value); }
        public double LimitEastPastMeridian { get => _limitEastDeg;  set => SetProperty(ref _limitEastDeg,  value); }
        public double LimitWestPastMeridian { get => _limitWestDeg;  set => SetProperty(ref _limitWestDeg,  value); }

        // ── Firmware / capability flags (loaded once on connect) ─────────────────

        public bool IsOldFormat => _mount.State.IsOldFormat;
        public bool IsServoCalibrationSupported => _mount.State.IsServoCalibrationSupported;

        // ── Status ───────────────────────────────────────────────────────────────

        private string _status = string.Empty;
        public string StatusMessage { get => _status; private set => SetProperty(ref _status, value); }

        // ── Commands ─────────────────────────────────────────────────────────────

        public ICommand LoadCommand              { get; private set; } = null!;
        public ICommand TrackOnCommand           { get; private set; } = null!;
        public ICommand TrackOffCommand          { get; private set; } = null!;
        public ICommand SetTrackRateCommand      { get; private set; } = null!;
        public ICommand SetCompTrackCommand      { get; private set; } = null!;
        public ICommand SetCompTrackAxisCommand  { get; private set; } = null!;
        public ICommand FreqPlusCommand          { get; private set; } = null!;
        public ICommand FreqMinusCommand         { get; private set; } = null!;
        public ICommand FreqResetCommand         { get; private set; } = null!;
        public ICommand SetGuideRateCommand      { get; private set; } = null!;
        public ICommand SetSlewSpeedCommand      { get; private set; } = null!;
        public ICommand SetMeridianSettingsCommand { get; private set; } = null!;
        public ICommand TriggerMeridianFlipCommand { get; private set; } = null!;
        public ICommand ContinueGotoAfterPauseCommand { get; private set; } = null!;
        public ICommand SetParkCommand           { get; private set; } = null!;
        public ICommand SetHomeCommand           { get; private set; } = null!;
        public ICommand SetEncoderOriginCommand  { get; private set; } = null!;
        public ICommand SetBacklashCommand       { get; private set; } = null!;
        public ICommand SetLimitsCommand         { get; private set; } = null!;
        public ICommand SyncSiteFromNinaCommand  { get; private set; } = null!;

        // Axis Config commands
        public ICommand SetMountTypeCommand     { get; private set; } = null!;
        public ICommand RebootCommand           { get; private set; } = null!;
        public ICommand ClearEeprom             { get; private set; } = null!;
        public ICommand SaveAxis1Command        { get; private set; } = null!;
        public ICommand SaveAxis2Command        { get; private set; } = null!;
        public ICommand RevertAxis1Command      { get; private set; } = null!;
        public ICommand RevertAxis2Command      { get; private set; } = null!;
        public ICommand ServoTrackNormalCommand { get; private set; } = null!;
        public ICommand ServoTrackFixedCommand  { get; private set; } = null!;
        public ICommand ServoRecordCommand      { get; private set; } = null!;
        public ICommand ServoStopCommand        { get; private set; } = null!;
        public ICommand ServoClearCommand       { get; private set; } = null!;
        public ICommand ServoLoadCalCommand     { get; private set; } = null!;
        public ICommand ServoSaveCalCommand     { get; private set; } = null!;
        public ICommand ServoLoadBackupCommand  { get; private set; } = null!;
        public ICommand ServoSaveBackupCommand  { get; private set; } = null!;
        public ICommand ServoHpfCommand         { get; private set; } = null!;
        public ICommand ServoLpfCommand         { get; private set; } = null!;

        // Sky Model Management commands
        public ICommand LoadModelFromMountCommand  { get; private set; } = null!;
        public ICommand LoadModelCommand           { get; private set; } = null!;
        public ICommand WriteToMountCommand        { get; private set; } = null!;
        public ICommand WriteToEepromCommand       { get; private set; } = null!;
        public ICommand ForceActivationCommand     { get; private set; } = null!;
        public ICommand SaveModelCommand           { get; private set; } = null!;
        public ICommand ClearModelCommand          { get; private set; } = null!;
        public ICommand ClearModelfromEepromCommand { get; private set; } = null!;

        private void BuildCommands() {
            bool Connected() => IsConnected;

            LoadCommand = new RelayCommand(async _ => {
                StatusMessage = "Reading mount settings…";
                try { await _mount.ReloadSettingsAsync(); StatusMessage = "Settings loaded."; }
                catch (Exception ex) { Logger.Error($"LoadSettings: {ex.Message}"); StatusMessage = $"Load error: {ex.Message}"; }
            }, _ => Connected());

            TrackOnCommand  = new RelayCommand(async _ => await _mount.SetTrackingAsync(true),  _ => Connected());
            TrackOffCommand = new RelayCommand(async _ => await _mount.SetTrackingAsync(false), _ => Connected());

            SetTrackRateCommand = new RelayCommand(async _ => await _mount.SetTrackingRateAsync(TrackingRate), _ => Connected());

            SetCompTrackCommand = new RelayCommand(async _ =>
                await _mount.SetCompensatedTrackingAsync(CompensatedTracking, CompensatedTrackingAxis == CompensatedTrackingAxis.Dual),
                _ => Connected());

            SetCompTrackAxisCommand = new RelayCommand(async _ =>
                await _mount.SetCompensatedTrackingAxisAsync(CompensatedTrackingAxis), _ => Connected());

            FreqPlusCommand  = new RelayCommand(async _ => await _mount.AdjustTrackingFrequencyAsync(+1), _ => Connected());
            FreqMinusCommand = new RelayCommand(async _ => await _mount.AdjustTrackingFrequencyAsync(-1), _ => Connected());
            FreqResetCommand = new RelayCommand(async _ => await _mount.ResetTrackingFrequencyAsync(),    _ => Connected());

            SetGuideRateCommand = new RelayCommand(async _ => {
                var idx = Math.Clamp(GuideRateIndex, 0, 9);
                await _mount.SetGuideRateAsync(idx);
                StatusMessage = $"Guide rate set to {GuideRateNames[idx]}";
            }, _ => Connected());

            SetSlewSpeedCommand = new RelayCommand(async _ => {
                await _mount.SetSlewSpeedAsync(SlewSpeed);
                StatusMessage = "Slew speed updated.";
            }, _ => Connected());

            SetMeridianSettingsCommand = new RelayCommand(async _ => {
                await _mount.SetGotoBuzzerAsync(BuzzerEnabled);
                await _mount.SetAutoMeridianFlipAsync(AutoMeridianFlip);
                await _mount.SetPauseAtHomeAsync(PauseAtHome);
                await _mount.SetPreferredPierSideAsync(PreferredPierSide);
                StatusMessage = "Meridian settings updated.";
            }, _ => Connected());

            TriggerMeridianFlipCommand = new RelayCommand(async _ => {
                await _mount.TriggerMeridianFlipAsync();
                StatusMessage = "Meridian flip triggered.";
            }, _ => Connected());

            SetParkCommand = new RelayCommand(async _ => {
                await _mount.SetParkPositionAsync();
                StatusMessage = "Park position set to current.";
            }, _ => Connected());

            SetHomeCommand = new RelayCommand(async _ => {
                await _mount.SetHomePositionAsync();
                StatusMessage = "Home position set to current.";
            }, _ => Connected());

            SetEncoderOriginCommand = new RelayCommand(async _ => {
                await _mount.SetEncoderOriginAsync();
                StatusMessage = "Encoder origin position set to current.";
            }, _ => Connected());

            SetBacklashCommand = new RelayCommand(async _ => {
                await _mount.SetBacklashAsync(BacklashAxis1Arcsec, BacklashAxis2Arcsec);
                StatusMessage = "Backlash updated.";
            }, _ => Connected());

            SetLimitsCommand = new RelayCommand(async _ => {
                await _mount.SetLimitsAsync(LimitMinAltDeg, LimitMaxAltDeg, LimitEastPastMeridian, LimitWestPastMeridian);
                StatusMessage = "Limits updated.";
            }, _ => Connected());

            SyncSiteFromNinaCommand = new RelayCommand(async _ => await SyncSiteFromNinaAsync(), _ => Connected());

            // ── Axis Config commands ─────────────────────────────────────────────
            SetMountTypeCommand = new RelayCommand(async _ => {
                await _mount.SetMountTypeAsync(MountType);
                StatusMessage = "Mount type set - reboot required.";
            }, _ => Connected());

            RebootCommand = new RelayCommand(async _ => {
                await _mount.RebootAsync();
                StatusMessage = "Reboot command sent.";
            }, _ => Connected());

            ClearEeprom = new RelayCommand(async _ => {
                await _mount.ClearEepromAsync();
                StatusMessage = "Clear EEPROM command sent.";
            }, _ => Connected());

            ContinueGotoAfterPauseCommand = new RelayCommand(async _ => {
                await _mount.ContinueGotoAfterPauseAsync();
                StatusMessage = "Continue Goto after Pause command sent.";
            }, _ => Connected());

            SaveAxis1Command = new RelayCommand(async _ => {
                StatusMessage = "Saving Axis 1…";
                await _mount.SaveAxisAsync(1);
                StatusMessage = "Axis 1 saved.";
            }, _ => Connected());

            SaveAxis2Command = new RelayCommand(async _ => {
                StatusMessage = "Saving Axis 2…";
                await _mount.SaveAxisAsync(2);
                StatusMessage = "Axis 2 saved.";
            }, _ => Connected());

            RevertAxis1Command = new RelayCommand(async _ => {
                await _mount.RevertAxisAsync(1);
                StatusMessage = "Axis 1 reverted to defaults - reload to verify.";
            }, _ => Connected());

            RevertAxis2Command = new RelayCommand(async _ => {
                await _mount.RevertAxisAsync(2);
                StatusMessage = "Axis 2 reverted to defaults - reload to verify.";
            }, _ => Connected());

            ServoTrackNormalCommand = new RelayCommand(async _ => { await _mount.ServoCalibrationAsync(ServoCalibrationCommand.TrackNormally);     StatusMessage = "Track normally."; },   _ => Connected() && IsServoCalibrationSupported);
            ServoTrackFixedCommand  = new RelayCommand(async _ => { await _mount.ServoCalibrationAsync(ServoCalibrationCommand.TrackFixedRate);    StatusMessage = "Track fixed rate."; }, _ => Connected() && IsServoCalibrationSupported);
            ServoRecordCommand      = new RelayCommand(async _ => { await _mount.ServoCalibrationAsync(ServoCalibrationCommand.RecordCalibration); StatusMessage = "Recording…"; },        _ => Connected() && IsServoCalibrationSupported);
            ServoStopCommand        = new RelayCommand(async _ => { await _mount.ServoCalibrationAsync(ServoCalibrationCommand.StopRecording);     StatusMessage = "Stopped."; },          _ => Connected() && IsServoCalibrationSupported);
            ServoClearCommand       = new RelayCommand(async _ => { await _mount.ServoCalibrationAsync(ServoCalibrationCommand.ClearBuffer);       StatusMessage = "Buffer cleared."; },   _ => Connected() && IsServoCalibrationSupported);
            ServoLoadCalCommand     = new RelayCommand(async _ => { await _mount.ServoCalibrationAsync(ServoCalibrationCommand.LoadCalibration);   StatusMessage = "Calibration loaded."; }, _ => Connected() && IsServoCalibrationSupported);
            ServoSaveCalCommand     = new RelayCommand(async _ => { await _mount.ServoCalibrationAsync(ServoCalibrationCommand.SaveCalibration);   StatusMessage = "Calibration saved."; },  _ => Connected() && IsServoCalibrationSupported);
            ServoLoadBackupCommand  = new RelayCommand(async _ => { await _mount.ServoCalibrationAsync(ServoCalibrationCommand.LoadBackup);        StatusMessage = "Backup loaded."; },      _ => Connected() && IsServoCalibrationSupported);
            ServoSaveBackupCommand  = new RelayCommand(async _ => { await _mount.ServoCalibrationAsync(ServoCalibrationCommand.SaveBackup);        StatusMessage = "Backup saved."; },       _ => Connected() && IsServoCalibrationSupported);
            ServoHpfCommand         = new RelayCommand(async _ => { await _mount.ServoCalibrationAsync(ServoCalibrationCommand.HighPassFilter);    StatusMessage = "High-pass filter."; },   _ => Connected() && IsServoCalibrationSupported);
            ServoLpfCommand         = new RelayCommand(async _ => { await _mount.ServoCalibrationAsync(ServoCalibrationCommand.LowPassFilter);     StatusMessage = "Low-pass filter."; },    _ => Connected() && IsServoCalibrationSupported);

            // Sky Model Management
            LoadModelFromMountCommand   = new RelayCommand(async _ => await LoadModelFromMountAsync(), _ => Connected());
            LoadModelCommand            = new RelayCommand(_ => LoadModel());
            WriteToMountCommand         = new RelayCommand(async _ => await WriteCoefficientsAsync(),  _ => _coefficients != null && Connected());
            WriteToEepromCommand        = new RelayCommand(async _ => await WriteToEepromAsync(),      _ => _coefficients != null && Connected());
            ForceActivationCommand      = new RelayCommand(async _ => await ForceModelActivationAsync(), _ => Connected());
            SaveModelCommand            = new RelayCommand(_ => SaveModel(),        _ => _coefficients != null);
            ClearModelCommand           = new RelayCommand(async _ => await ClearModelAsync(),         _ => Connected());
            ClearModelfromEepromCommand = new RelayCommand(async _ => await ClearModelFromEepromAsync(), _ => Connected());
        }

        private async Task SyncSiteFromNinaAsync() {
            try {
                var lat  = _profile.ActiveProfile.AstrometrySettings.Latitude;
                var lon  = _profile.ActiveProfile.AstrometrySettings.Longitude;
                var elev = _profile.ActiveProfile.AstrometrySettings.Elevation;
                await _mount.SetLocationAsync(lon, lat, elev);
                StatusMessage = $"Site synced from N.I.N.A.: {lat:F4}°  {lon:F4}°  {elev:F1} m";
            } catch (Exception ex) {
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static void Dispatch(Action a) {
            var app = System.Windows.Application.Current;
            if (app != null) app.Dispatcher.Invoke(a);
            else a();
        }

        // ── Sky Model Management ─────────────────────────────────────────────────

        private async Task LoadModelFromMountAsync() {
            try {
                StatusMessage = "Reading coefficients from mount…";
                var loaded = await _mount.GetCoefficientsAsync(CancellationToken.None);
                if (loaded != null) {
                    Coefficients  = loaded;
                    StatusMessage = $"Model loaded from mount.";
                } else {
                    StatusMessage = "Mount returned no coefficient data.";
                }
            } catch (Exception ex) {
                Logger.Error($"LoadModelFromMount: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void LoadModel() {
            var dlg = new OpenFileDialog { Filter = "JSON|*.json", Title = "Load Model" };
            if (dlg.ShowDialog() != true) return;
            try {
                var loaded = JsonConvert.DeserializeObject<AlignmentModelCoefficients>(File.ReadAllText(dlg.FileName));
                if (loaded != null) {
                    Coefficients  = loaded;
                    StatusMessage = $"Model loaded.";
                } else {
                    StatusMessage = "File did not contain a valid model.";
                }
            } catch (Exception ex) {
                StatusMessage = $"Load failed: {ex.Message}";
            }
        }

        private async Task WriteCoefficientsAsync() {
            if (_coefficients == null) return;
            try {
                StatusMessage = "Writing coefficients to mount…";
                await _mount.WriteCoefficientsAsync(_coefficients);
                StatusMessage = "Coefficients written.";
            } catch (Exception ex) {
                Logger.Error($"WriteCoefficients: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async Task WriteToEepromAsync() {
            if (_coefficients == null) return;
            try {
                StatusMessage = "Writing coefficients…";
                await _mount.WriteCoefficientsAsync(_coefficients);
                StatusMessage = "Saving to EEPROM…";
                await _mount.SaveAlignmentToEepromAsync();
                StatusMessage = "Model saved to EEPROM.";
            } catch (Exception ex) {
                Logger.Error($"WriteToEeprom: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async Task ForceModelActivationAsync() {
            try {
                await _mount.ForceModelActivationAsync();
                StatusMessage = "Model activation forced (:SX09,2#).";
            } catch (Exception ex) {
                Logger.Error($"ForceModelActivation: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private void SaveModel() {
            if (_coefficients == null) return;
            var dlg = new SaveFileDialog { Filter = "JSON|*.json", Title = "Save Model" };
            if (dlg.ShowDialog() != true) return;
            File.WriteAllText(dlg.FileName, JsonConvert.SerializeObject(_coefficients, Formatting.Indented));
        }

        private void ZeroCoefficients() {
            Coefficients = new AlignmentModelCoefficients();
        }

        private async Task ClearModelAsync() {
            try {
                StatusMessage = "Clearing alignment model…";
                await _mount.ClearAlignmentModelAsync();
                ZeroCoefficients();
                StatusMessage = "Alignment model cleared.";
            } catch (Exception ex) {
                Logger.Error($"ClearModel: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        private async Task ClearModelFromEepromAsync() {
            try {
                StatusMessage = "Clearing alignment model…";
                await _mount.ClearAlignmentModelAsync();
                ZeroCoefficients();
                StatusMessage = "Saving cleared model to EEPROM…";
                await _mount.SaveAlignmentToEepromAsync();
                StatusMessage = "Cleared model saved to EEPROM.";
            } catch (Exception ex) {
                Logger.Error($"ClearModelFromEeprom: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
        }

        public void Dispose() {
            _mount.State.PropertyChanged -= OnMountStateChanged;
        }
    }
}
