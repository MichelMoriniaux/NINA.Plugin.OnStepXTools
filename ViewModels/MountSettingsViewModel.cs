using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using NINA.Plugin.OnStepXTools.Equipment;
using NINA.Plugin.OnStepXTools.Interfaces;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.ViewModels {

    [Export(typeof(IDockableVM))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class MountSettingsViewModel : DockableVM, ITelescopeConsumer, IDisposable {
        private readonly ITelescopeMediator _telescope;
        private readonly IProfileService    _profile;
        private readonly IOnStepXMount      _mount;
        private bool _wasConnected;

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

        // ── Axis config - firmware version and format detection ────────────────────
        private int  _fwMajor;
        private int  _fwMinor;
        private bool _fwVersionKnown;
        private char _axis1DriverType;
        private char _axis2DriverType;
        private bool _axis1OldFormat;
        private bool _axis2OldFormat;

        [ImportingConstructor]
        public MountSettingsViewModel(ITelescopeMediator telescope, IProfileService profile, IOnStepXMount mount)
            : base(profile) {
            Title         = "OnStepX Mount Settings";
            ImageGeometry = System.Windows.Application.Current?.Resources["SettingsSVG"] as System.Windows.Media.GeometryGroup;
            _telescope    = telescope;
            _profile      = profile;
            _mount        = mount;
            _telescope.RegisterConsumer(this);
            BuildCommands();
        }

        // ── ITelescopeConsumer ───────────────────────────────────────────────────
        // TODO: verify how often this is run, most of the values polled should only be pulled once or on demand, not every 2 seconds
        public void UpdateDeviceInfo(TelescopeInfo info) {
            IsConnected = info.Connected;
            if (info.Connected && !_wasConnected) {
                // Detect firmware version so axis config uses the right command format
                try { ParseFirmwareVersion(info.Description ?? string.Empty); } catch { }
                _ = _mount.EnsureModelActivatedAsync();
                _ = LoadAllSettingsAsync();
            }
            _wasConnected = info.Connected;
        }

        // ── Connectivity ─────────────────────────────────────────────────────────

        private bool _isConnected;
        public bool IsConnected { get => _isConnected; private set => SetProperty(ref _isConnected, value); }

        // ── Axis Config ──────────────────────────────────────────────────────────

        private MountType _mountType    = MountType.GEM;
        private string _axis1DriverId  = string.Empty;
        private string _axis2DriverId  = string.Empty;

        public MountType MountType     { get => _mountType;     set => SetProperty(ref _mountType,     value); }
        public string    Axis1DriverId { get => _axis1DriverId; private set => SetProperty(ref _axis1DriverId, value); }
        public string    Axis2DriverId { get => _axis2DriverId; private set => SetProperty(ref _axis2DriverId, value); }

        public ObservableCollection<AxisParameter> Axis1Params { get; } = new();
        public ObservableCollection<AxisParameter> Axis2Params { get; } = new();

        // ── Site Location ────────────────────────────────────────────────────────
        // Read-only display of what the mount currently has (populated on connect)

        private string _latitudeDMS   = string.Empty;
        private string _longitudeDMS  = string.Empty;
        private string _elevation  = string.Empty;

        public string LatitudeDMS  { get => _latitudeDMS;  private set => SetProperty(ref _latitudeDMS,  value); }
        public string LongitudeDMS { get => _longitudeDMS; private set => SetProperty(ref _longitudeDMS, value); }
        public string Elevation { get => _elevation; private set => SetProperty(ref _elevation, value); }

        // ── Tracking ─────────────────────────────────────────────────────────────

        private bool _trackingEnabled;
        private TrackingRate _trackingRate = TrackingRate.Sidereal;
        private CompensatedTracking _compensatedTracking = CompensatedTracking.Off;
        private CompensatedTrackingAxis _compensatedTrackingAxis = CompensatedTrackingAxis.Single;

        public bool             TrackingEnabled     { get => _trackingEnabled;     private set => SetProperty(ref _trackingEnabled,     value); }
        public TrackingRate     TrackingRate        { get => _trackingRate;        set => SetProperty(ref _trackingRate,        value); }
        public CompensatedTracking CompensatedTracking { get => _compensatedTracking; set => SetProperty(ref _compensatedTracking, value); }
        public CompensatedTrackingAxis CompensatedTrackingAxis { get => _compensatedTrackingAxis; set => SetProperty(ref _compensatedTrackingAxis, value); }

        // ── Guide Rate ───────────────────────────────────────────────────────────

        private int _guideRateIndex = 2; // 0-9; index 2 = 1× sidereal
        public int GuideRateIndex { get => _guideRateIndex; set => SetProperty(ref _guideRateIndex, value); }

        public static string[] GuideRateNames { get; } =
            { "0.25×", "0.5×", "1×", "2×", "4×", "8×", "20×", "48×", "VF", "VVF" };

        // ── Slew Speed ───────────────────────────────────────────────────────────

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
            bool Connected() => _isConnected;
            void SendBlind(string cmd) { try { _telescope.SendCommandBlind(cmd, raw: true); } catch { } }

            LoadCommand = new RelayCommand(
                async _ => await LoadAllSettingsAsync(), _ => Connected());

            TrackOnCommand  = new RelayCommand(_ => SendBlind(OnStepXProtocol.Tracking(true)), _ => Connected());
            TrackOffCommand = new RelayCommand(_ => SendBlind(OnStepXProtocol.Tracking(false)), _ => Connected());

            SetTrackRateCommand = new RelayCommand(_ => {
                SendBlind(OnStepXProtocol.TrackingRate(TrackingRate));
                // Status(":GU#");
            }, _ => Connected());

            SetCompTrackCommand = new RelayCommand(_ => {
                SendBlind(OnStepXProtocol.CompensatedTracking(CompensatedTracking));
            }, _ => Connected());

            SetCompTrackAxisCommand = new RelayCommand(_ => {
                SendBlind(OnStepXProtocol.CompensatedTrackingAxis(CompensatedTrackingAxis));
            }, _ => Connected());

            FreqPlusCommand  = new RelayCommand(_ => SendBlind(OnStepXProtocol.TrackingFrequencyAdjust(+1)), _ => Connected());
            FreqMinusCommand = new RelayCommand(_ => SendBlind(OnStepXProtocol.TrackingFrequencyAdjust(-1)), _ => Connected());
            FreqResetCommand = new RelayCommand(_ => SendBlind(OnStepXProtocol.TrackingFrequencyReset()), _ => Connected());

            SetGuideRateCommand = new RelayCommand(_ => {
                var idx = Math.Clamp(GuideRateIndex, 0, 9);
                SendBlind(OnStepXProtocol.GuideRatePreset(idx));
                StatusMessage = $"Guide rate set to {GuideRateNames[idx]}";
            }, _ => Connected());

            SetSlewSpeedCommand = new RelayCommand(_ => {
                SendBlind(OnStepXProtocol.SlewSpeedPreset(SlewSpeed));
                StatusMessage = "Slew speed updated.";
            }, _ => Connected());

            SetMeridianSettingsCommand = new RelayCommand(_ => {
                SendBlind(OnStepXProtocol.GotoBuzzer(BuzzerEnabled));
                SendBlind(OnStepXProtocol.AutoMeridianFlip(AutoMeridianFlip));
                SendBlind(OnStepXProtocol.PauseAtHome(PauseAtHome));
                SendBlind(OnStepXProtocol.PreferredPierSide(PreferredPierSide));
                StatusMessage = "Meridian settings updated.";
            }, _ => Connected());

            TriggerMeridianFlipCommand = new RelayCommand(_ => {
                SendBlind(OnStepXProtocol.MeridianFlipNow());
                StatusMessage = "Meridian flip triggered.";
            }, _ => Connected());

            SetParkCommand = new RelayCommand(_ => {
                SendBlind(OnStepXProtocol.SetParkPosition());
                StatusMessage = "Park position set to current.";
            }, _ => Connected());

            SetHomeCommand = new RelayCommand(_ => {
                SendBlind(OnStepXProtocol.ResetMountAtHome());
                StatusMessage = "Home position set to current.";
            }, _ => Connected());

            SetEncoderOriginCommand = new RelayCommand(_ => {
                SendBlind(":SEO#");
                StatusMessage = "Encoder origin position set to current.";
            }, _ => Connected());

            SetBacklashCommand = new RelayCommand(_ => {
                SendBlind(OnStepXProtocol.BacklashRa(BacklashAxis1Arcsec));
                SendBlind(OnStepXProtocol.BacklashDec(BacklashAxis2Arcsec));
                StatusMessage = "Backlash updated.";
            }, _ => Connected());

            SetLimitsCommand = new RelayCommand(_ => {
                SendBlind(OnStepXProtocol.HorizonLimit(LimitMinAltDeg));
                SendBlind(OnStepXProtocol.OverheadLimit(LimitMaxAltDeg));
                SendBlind(OnStepXProtocol.MeridianLimitEast(LimitEastPastMeridian));
                SendBlind(OnStepXProtocol.MeridianLimitWest(LimitWestPastMeridian));
                StatusMessage = "Limits updated.";
            }, _ => Connected());

            SyncSiteFromNinaCommand = new RelayCommand(async _ => {
                try {
                    var lat  = _profile.ActiveProfile.AstrometrySettings.Latitude;
                    var lon  = _profile.ActiveProfile.AstrometrySettings.Longitude;
                    var elev = _telescope.GetInfo().SiteElevation; // mount's current elevation
                    await _mount.SetLocationAsync(lon, lat, elev);
                    StatusMessage = $"Site synced from N.I.N.A.: {lat:F4}°  {lon:F4}°  {elev:F0} m";
                    await Task.Delay(200);
                    await LoadAllSettingsAsync();
                } catch (Exception ex) {
                    StatusMessage = $"Error: {ex.Message}";
                }
            }, _ => Connected());

            // void Status(string cmd) { try { _telescope.SendCommandString(cmd, raw: true); } catch { } }

            // ── Axis Config commands ─────────────────────────────────────────────
            SetMountTypeCommand = new RelayCommand(_ => { SendBlind(OnStepXProtocol.SetMountType(MountType)); StatusMessage = "Mount type set - reboot required."; }, _ => Connected());
            RebootCommand       = new RelayCommand(_ => { SendBlind(OnStepXProtocol.Reboot()); StatusMessage = "Reboot command sent."; }, _ => Connected());
            ClearEeprom         = new RelayCommand(_ => { SendBlind(":ENVRESET#"); StatusMessage = "Clear EEPROM command sent."; }, _ => Connected());
            ContinueGotoAfterPauseCommand = new RelayCommand(_ => { SendBlind(":SX99,1#"); StatusMessage = "Continue Goto after Pause command sent."; }, _ => Connected());

            SaveAxis1Command   = new RelayCommand(async _ => await SaveAxisAsync(1, Axis1Params), _ => Connected());
            SaveAxis2Command   = new RelayCommand(async _ => await SaveAxisAsync(2, Axis2Params), _ => Connected());
            RevertAxis1Command = new RelayCommand(_ => { SendBlind(":SXA1,R#"); StatusMessage = "Axis 1 reverted to defaults - reload to verify."; }, _ => Connected());
            RevertAxis2Command = new RelayCommand(_ => { SendBlind(":SXA2,R#"); StatusMessage = "Axis 2 reverted to defaults - reload to verify."; }, _ => Connected());

            ServoTrackNormalCommand = new RelayCommand(_ => StatusMessage = "Servo calibration commands are not supported by verified mainline OnStepX protocol.", _ => Connected());
            ServoTrackFixedCommand  = new RelayCommand(_ => StatusMessage = "Servo calibration commands are not supported by verified mainline OnStepX protocol.", _ => Connected());
            ServoRecordCommand      = new RelayCommand(_ => StatusMessage = "Servo calibration commands are not supported by verified mainline OnStepX protocol.", _ => Connected());
            ServoStopCommand        = new RelayCommand(_ => StatusMessage = "Servo calibration commands are not supported by verified mainline OnStepX protocol.", _ => Connected());
            ServoClearCommand       = new RelayCommand(_ => StatusMessage = "Servo calibration commands are not supported by verified mainline OnStepX protocol.", _ => Connected());
            ServoLoadCalCommand     = new RelayCommand(_ => StatusMessage = "Servo calibration commands are not supported by verified mainline OnStepX protocol.", _ => Connected());
            ServoSaveCalCommand     = new RelayCommand(_ => StatusMessage = "Servo calibration commands are not supported by verified mainline OnStepX protocol.", _ => Connected());
            ServoLoadBackupCommand  = new RelayCommand(_ => StatusMessage = "Servo calibration commands are not supported by verified mainline OnStepX protocol.", _ => Connected());
            ServoSaveBackupCommand  = new RelayCommand(_ => StatusMessage = "Servo calibration commands are not supported by verified mainline OnStepX protocol.", _ => Connected());
            ServoHpfCommand         = new RelayCommand(_ => StatusMessage = "Servo calibration commands are not supported by verified mainline OnStepX protocol.", _ => Connected());
            ServoLpfCommand         = new RelayCommand(_ => StatusMessage = "Servo calibration commands are not supported by verified mainline OnStepX protocol.", _ => Connected());

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

        // ── Load settings ────────────────────────────────────────────────────────

        private async Task LoadAllSettingsAsync() {
            StatusMessage = "Reading mount settings…";
            try {
                await Task.Run(() => {
                    // ─ GU# packed status ─────────────────────────────────────────
                    var gu = GetStr(":GU#");
                    if (!string.IsNullOrEmpty(gu)) {
                        var n = gu.Length;
                        // End-of-string indices: [...][guideRatePulse][guideRate][errorCode]
                        if (n >= 2) {
                            var grIdx = gu[n - 2] - '0';
                            if (grIdx >= 0 && grIdx <= 9)
                                Dispatch(() => GuideRateIndex = grIdx);
                        }
                        Dispatch(() => {
                            TrackingEnabled   = !gu.Contains('n');
                            AutoMeridianFlip  = gu.Contains('a');
                            PauseAtHome       = gu.Contains('u');
                            BuzzerEnabled     = gu.Contains('z');
                            // Tracking rate flags
                            if      (gu.Contains('(')) TrackingRate = TrackingRate.Lunar;
                            else if (gu.Contains('O')) TrackingRate = TrackingRate.Solar;
                            else if (gu.Contains('k')) TrackingRate = TrackingRate.King;
                            else                       TrackingRate = TrackingRate.Sidereal;
                            // Rate compensation flags
                            if      (gu.Contains('t')) CompensatedTracking = CompensatedTracking.Full;
                            else if (gu.Contains('r')) CompensatedTracking = CompensatedTracking.RefractionOnly;
                            else                       CompensatedTracking = CompensatedTracking.Off;
                            // compensation axis flags
                            if      (gu.Contains('s')) CompensatedTrackingAxis = CompensatedTrackingAxis.Single;
                            else                       CompensatedTrackingAxis = CompensatedTrackingAxis.Dual;

                        });
                    }

                    if (int.TryParse(GetStr(OnStepXProtocol.GetMountType()), out var mt) &&
                        Enum.IsDefined(typeof(MountType), mt))
                        Dispatch(() => MountType = ToWritableMountType((MountType)mt));

                    // ─ Preferred pier side (:GX96#) ─────────────────────────────
                    var ps = GetStr(":GX96#");
                    if (!string.IsNullOrWhiteSpace(ps))
                        Dispatch(() => PreferredPierSide = PierSideFromChar(ps));

                    // ─ Backlash ──────────────────────────────────────────────────
                    if (int.TryParse(GetStr(":%BR#"), out var bl1))
                        Dispatch(() => BacklashAxis1Arcsec = bl1);
                    if (int.TryParse(GetStr(":%BD#"), out var bl2))
                        Dispatch(() => BacklashAxis2Arcsec = bl2);

                    // ─ Altitude limits ───────────────────────────────────────────
                    var altString = GetStr(":Gh#");
                    if (!string.IsNullOrWhiteSpace(altString))
                        if (int.TryParse(altString.Replace("*", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var minAlt))
                            Dispatch(() => LimitMinAltDeg = minAlt);
                    altString = GetStr(":Go#");
                    if (!string.IsNullOrWhiteSpace(altString))
                        if (int.TryParse(altString.Replace("*", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var maxAlt))
                            Dispatch(() => LimitMaxAltDeg = maxAlt);

                    // ─ Meridian limits ───────────────────────────────────────────
                    if (double.TryParse(GetStr(":GXE9#"), NumberStyles.Any, CultureInfo.InvariantCulture, out var me))
                        Dispatch(() => LimitEastPastMeridian = me / 4.0);
                    if (double.TryParse(GetStr(":GXEA#"), NumberStyles.Any, CultureInfo.InvariantCulture, out var mw))
                        Dispatch(() => LimitWestPastMeridian = mw / 4.0);

                    // ─ Site location (display only - edit via Sync button) ────────
                    var latStr = GetStr(":GtH#");
                    var lonStr = GetStr(":GgH#");
                    if (!string.IsNullOrWhiteSpace(latStr))
                        Dispatch(() => LatitudeDMS  = latStr);
                    if (!string.IsNullOrWhiteSpace(lonStr))
                        Dispatch(() => LongitudeDMS = lonStr);
                    var elev = _telescope.GetInfo().SiteElevation;
                    Dispatch(() => Elevation = elev.ToString());
                });
                StatusMessage = "Settings loaded.";
            } catch (Exception ex) {
                Logger.Error($"LoadSettings: {ex.Message}");
                StatusMessage = $"Load error: {ex.Message}";
            }

            // Load axis configuration after mount settings
            await LoadAxisConfigAsync();
        }

        // ── Axis config load ─────────────────────────────────────────────────────
        // TODO support more axis , here we do 2 but OnStep supports up to 9
        private async Task LoadAxisConfigAsync() {
            bool useOld = IsOldFormat;
            StatusMessage = useOld
                ? $"Firmware v{_fwMajor}.{_fwMinor} - reading axis config (pre-10.26 format)…"
                : _fwVersionKnown
                    ? $"Firmware v{_fwMajor}.{_fwMinor} - reading axis config (per-parameter format)…"
                    : "Firmware version unknown - reading axis config (per-parameter format)…";
            try {
                await Task.Run(async () => {
                    if (useOld) {
                        await LoadAxisParamsOldFormatAsync(1, Axis1Params);
                        await LoadAxisParamsOldFormatAsync(2, Axis2Params);
                    } else {
                        var d1 = GetStr(":GXA1,M#") ?? "-";
                        var d2 = GetStr(":GXA2,M#") ?? "-";
                        Dispatch(() => { Axis1DriverId = d1; Axis2DriverId = d2; });
                        await LoadAxisParamsNewFormatAsync(1, Axis1Params);
                        await LoadAxisParamsNewFormatAsync(2, Axis2Params);
                    }
                });

                int total = Axis1Params.Count + Axis2Params.Count;
                StatusMessage = total > 0
                    ? $"Axis 1: {Axis1Params.Count} params, Axis 2: {Axis2Params.Count} params loaded."
                    : "Firmware reports 0 runtime-configurable axis parameters (compile-time motor constants).";
            } catch (Exception ex) {
                Logger.Error($"LoadAxisConfig: {ex.Message}");
                StatusMessage = $"Axis config load error: {ex.Message}";
            }
        }

        private Task LoadAxisParamsOldFormatAsync(int axis, ObservableCollection<AxisParameter> collection) {
            return Task.Run(() => {
                var raw = GetStr($":GXA{axis}#");
                Logger.Debug($"OnStepX :GXA{axis}# raw: '{raw}'");
                if (string.IsNullOrWhiteSpace(raw)) return;

                char driverType = raw.Length > 0 && char.IsLetter(raw[^1]) ? char.ToUpperInvariant(raw[^1]) : '\0';
                var body  = driverType != '\0' ? raw[..^1] : raw;
                var parts = body.Split(',');
                var loaded = BuildOldFormatParams(parts, driverType);

                string driverId = driverType switch {
                    'P' => $"Servo dual-PID ({driverType})",
                    'T' => $"Step/Dir TMC-SPI ({driverType})",
                    _   => $"Step/Dir ({driverType})"
                };
                if (axis == 1) { _axis1DriverType = driverType; _axis1OldFormat = true; }
                else           { _axis2DriverType = driverType; _axis2OldFormat = true; }

                Dispatch(() => {
                    if (axis == 1) Axis1DriverId = driverId;
                    else           Axis2DriverId = driverId;
                    collection.Clear();
                    foreach (var p in loaded) collection.Add(p);
                });
            });
        }

        private static List<AxisParameter> BuildOldFormatParams(string[] parts, char type) {
            var result = new List<AxisParameter>();
            int idx = 1;
            void Add(string name, int fi, string min = "", string max = "", bool isFloat = false) {
                if (fi >= parts.Length) return;
                var v = parts[fi].Trim();
                result.Add(new AxisParameter { Index = idx++, Name = name, CurrentValue = v, Min = min, Max = max,
                                               TypeCode = isFloat ? 5 : 3, IsImmediate = false, EditValue = v });
            }
            Add("Steps per measure",    0, "1", "360000", isFloat: true);
            Add("Reverse direction",    1, "0", "1");
            Add("Minimum position (°)", 2, "-360", "360");
            Add("Maximum position (°)", 3, "-360", "360");
            switch (type) {
                case 'P':
                    Add("P - tracking", 4, isFloat: true); Add("I - tracking", 5, isFloat: true);
                    Add("D - tracking", 6, isFloat: true); Add("P - goto", 7, isFloat: true);
                    Add("I - goto", 8, isFloat: true);     Add("D - goto", 9, isFloat: true); break;
                case 'T':
                    Add("Microsteps (tracking)", 4); Add("Microsteps (goto)", 5);
                    Add("Current Hold (mA)", 6, "0", "3000"); Add("Current Run (mA)", 7, "0", "3000");
                    Add("Current Goto (mA)", 8, "0", "3000"); break;
                default:
                    Add("Microsteps (tracking)", 4); Add("Microsteps (goto)", 5); break;
            }
            return result;
        }

        private Task LoadAxisParamsNewFormatAsync(int axis, ObservableCollection<AxisParameter> collection) {
            return Task.Run(() => {
                var rawCount = _telescope.SendCommandString($":GXA{axis},0#", raw: true);
                Logger.Debug($"OnStepX :GXA{axis},0# raw: '{rawCount}'");
                if (string.IsNullOrWhiteSpace(rawCount)) return;
                var countStr = rawCount.TrimEnd('#').Trim();
                if (!int.TryParse(countStr, out var count) || count <= 0) return;

                var loaded = new List<AxisParameter>();
                for (int i = 1; i <= count; i++) {
                    var r = GetStr($":GXA{axis},{i}#");
                    var p = ParseNewFormatParam(i, r);
                    if (p != null) loaded.Add(p);
                }
                Dispatch(() => { collection.Clear(); foreach (var p in loaded) collection.Add(p); });
            });
        }

        private async Task SaveAxisAsync(int axis, ObservableCollection<AxisParameter> collection) {
            StatusMessage = $"Saving Axis {axis}…";
            bool isOld = axis == 1 ? _axis1OldFormat : _axis2OldFormat;
            char dtype = axis == 1 ? _axis1DriverType : _axis2DriverType;
            int saved = 0, failed = 0;
            await Task.Run(() => {
                if (isOld) {
                    var values = new List<string>();
                    foreach (var p in collection) values.Add(p.EditValue);
                    if (values.Count > 0 && dtype != '\0') values[^1] += dtype;
                    var cmd = $":SXA{axis},{string.Join(",", values)}#";
                    try { var ok = _telescope.SendCommandBool(cmd, raw: true); if (ok) saved = values.Count; else failed = 1; } catch { failed = 1; }
                } else {
                    foreach (var param in collection) {
                        if (param.EditValue == param.CurrentValue) continue;
                        var cmd = $":SXA{axis},{param.Index},{param.EditValue}#";
                        try { var ok = _telescope.SendCommandBool(cmd, raw: true); if (ok) saved++; else failed++; } catch { failed++; }
                    }
                }
            });
            StatusMessage = saved + failed == 0 ? "No changes." : $"Axis {axis}: {saved} saved, {failed} failed.";
            if (saved > 0) {
                if (isOld) await LoadAxisParamsOldFormatAsync(axis, collection);
                else       await LoadAxisParamsNewFormatAsync(axis, axis == 1 ? Axis1Params : Axis2Params);
            }
        }

        private bool IsOldFormat => _fwVersionKnown && (_fwMajor < 10 || (_fwMajor == 10 && _fwMinor < 26));

        private void ParseFirmwareVersion(string description) {
            _fwMajor = 0; _fwMinor = 0;
            _fwVersionKnown = false;
            var m = Regex.Match(description, @"(\d+)\.(\d+)", RegexOptions.IgnoreCase);
            if (!m.Success) return;
            int.TryParse(m.Groups[1].Value, out _fwMajor);
            int.TryParse(m.Groups[2].Value, out _fwMinor);
            _fwVersionKnown = true;
            Logger.Info($"OnStepX firmware v{_fwMajor}.{_fwMinor} - axis format: {(IsOldFormat ? "OLD" : "NEW")}");
        }

        private static AxisParameter? ParseNewFormatParam(int index, string? response) {
            if (string.IsNullOrWhiteSpace(response)) return null;
            var parts = response.Split(',', 5);
            if (parts.Length < 4) return null;
            int.TryParse(parts[3].Trim(), out var typeCode);
            var name = parts.Length >= 5 ? parts[4].Trim() : $"Parameter {index}";
            var value = parts[0].Trim();
            return new AxisParameter { Index = index, Name = name, CurrentValue = value,
                Min = parts.Length > 1 ? parts[1].Trim() : "", Max = parts.Length > 2 ? parts[2].Trim() : "",
                TypeCode = typeCode, IsImmediate = typeCode % 2 == 0 && typeCode > 0, EditValue = value };
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private string? GetStr(string cmd) {
            try {
                var r = _telescope.SendCommandString(cmd, raw: true);
                return string.IsNullOrWhiteSpace(r) ? null : r.TrimEnd('#').Trim();
            } catch { return null; }
        }

        private static void Dispatch(Action a) =>
            System.Windows.Application.Current?.Dispatcher.Invoke(a);

        private static MountType ToWritableMountType(MountType type) => type switch {
            MountType.GEM_TA or MountType.GEM_TAC => MountType.GEM,
            MountType.Fork_TA or MountType.Fork_TAC => MountType.Fork,
            MountType.AltAz_Unlimited => MountType.AltAz,
            MountType.AltAlt => MountType.Default,
            _ => type
        };

        // Mount encodes preferred pier side as a single character: B=Best, W=West, E=East, A=Auto
        private static char PierSideToChar(PreferredPierSide s) => s switch {
            PreferredPierSide.West => 'W',
            PreferredPierSide.East => 'E',
            PreferredPierSide.Auto => 'A',
            _                      => 'B'
        };

        private static PreferredPierSide PierSideFromChar(string? s) => s?.Trim() switch {
            "W" => PreferredPierSide.West,
            "E" => PreferredPierSide.East,
            "A" => PreferredPierSide.Auto,
            _   => PreferredPierSide.Best
        };

        // Format decimal degrees as ±DD*MM:SS (degrees=2) or ±DDD*MM:SS (degrees=3)
        private static string FormatDMSCommand(double decDeg, int degrees) {
            var sign = decDeg < 0 ? "-" : "+";
            decDeg = Math.Abs(decDeg);
            var d = (int)decDeg;
            var mFrac = (decDeg - d) * 60;
            var m = (int)mFrac;
            var s = (int)Math.Round((mFrac - m) * 60);
            if (s == 60) { m++; s = 0; }
            if (m == 60) { d++; m = 0; }
            var fmt = degrees == 3 ? $"{d:D3}" : $"{d:D2}";
            return $"{sign}{fmt}*{m:D2}:{s:D2}";
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
            _telescope.RemoveConsumer(this);
        }
    }
}
