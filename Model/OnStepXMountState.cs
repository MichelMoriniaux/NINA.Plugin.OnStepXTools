using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NINA.Plugin.OnStepXTools.Model {

    // Cached OnStepX-specific mount state, owned by OnStepXMount. Single source of truth that
    // ViewModels read from instead of each independently querying/parsing the driver. Mutated in
    // place (never replaced) so subscribers only need to attach PropertyChanged once.
    public sealed class OnStepXMountState : INotifyPropertyChanged {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void RaisePropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null) {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            RaisePropertyChanged(name);
            return true;
        }

        // ── Connection ───────────────────────────────────────────────────────────

        private bool _isConnected;
        public bool IsConnected { get => _isConnected; set => SetField(ref _isConnected, value); }

        // ── Connect-once / manual-reload tier ───────────────────────────────────────

        private MountType _mountType = MountType.GEM;
        public MountType MountType { get => _mountType; set => SetField(ref _mountType, value); }

        private string _axis1DriverId = string.Empty;
        public string Axis1DriverId { get => _axis1DriverId; set => SetField(ref _axis1DriverId, value); }

        private string _axis2DriverId = string.Empty;
        public string Axis2DriverId { get => _axis2DriverId; set => SetField(ref _axis2DriverId, value); }

        public ObservableCollection<AxisParameter> Axis1Params { get; } = new();
        public ObservableCollection<AxisParameter> Axis2Params { get; } = new();

        private bool _runtimeAxisConfigEnabled;
        public bool RuntimeAxisConfigEnabled { get => _runtimeAxisConfigEnabled; set => SetField(ref _runtimeAxisConfigEnabled, value); }

        private FirmwareVersion? _firmwareVersion;
        public FirmwareVersion? FirmwareVersion { get => _firmwareVersion; set => SetField(ref _firmwareVersion, value); }

        private bool _isOldFormat;
        public bool IsOldFormat { get => _isOldFormat; set => SetField(ref _isOldFormat, value); }

        private bool _isServoCalibrationSupported;
        public bool IsServoCalibrationSupported { get => _isServoCalibrationSupported; set => SetField(ref _isServoCalibrationSupported, value); }

        private bool _trackingEnabled;
        public bool TrackingEnabled { get => _trackingEnabled; set => SetField(ref _trackingEnabled, value); }

        private TrackingRate _trackingRate = TrackingRate.Sidereal;
        public TrackingRate TrackingRate { get => _trackingRate; set => SetField(ref _trackingRate, value); }

        private CompensatedTracking _compensatedTracking = CompensatedTracking.Off;
        public CompensatedTracking CompensatedTracking { get => _compensatedTracking; set => SetField(ref _compensatedTracking, value); }

        private CompensatedTrackingAxis _compensatedTrackingAxis = CompensatedTrackingAxis.Single;
        public CompensatedTrackingAxis CompensatedTrackingAxis { get => _compensatedTrackingAxis; set => SetField(ref _compensatedTrackingAxis, value); }

        private int _guideRateIndex = 2; // 0-9; index 2 = 1x sidereal
        public int GuideRateIndex { get => _guideRateIndex; set => SetField(ref _guideRateIndex, value); }

        private PreferredPierSide _preferredPierSide = PreferredPierSide.Best;
        public PreferredPierSide PreferredPierSide { get => _preferredPierSide; set => SetField(ref _preferredPierSide, value); }

        private int _backlashAxis1;
        public int BacklashAxis1Arcsec { get => _backlashAxis1; set => SetField(ref _backlashAxis1, value); }

        private int _backlashAxis2;
        public int BacklashAxis2Arcsec { get => _backlashAxis2; set => SetField(ref _backlashAxis2, value); }

        private int _limitMinAlt;
        public int LimitMinAltDeg { get => _limitMinAlt; set => SetField(ref _limitMinAlt, value); }

        private int _limitMaxAlt = 85;
        public int LimitMaxAltDeg { get => _limitMaxAlt; set => SetField(ref _limitMaxAlt, value); }

        private double _limitEastDeg;
        public double LimitEastPastMeridian { get => _limitEastDeg; set => SetField(ref _limitEastDeg, value); }

        private double _limitWestDeg = 15;
        public double LimitWestPastMeridian { get => _limitWestDeg; set => SetField(ref _limitWestDeg, value); }

        private string _latitudeDMS = string.Empty;
        public string LatitudeDMS { get => _latitudeDMS; set => SetField(ref _latitudeDMS, value); }

        private string _longitudeDMS = string.Empty;
        public string LongitudeDMS { get => _longitudeDMS; set => SetField(ref _longitudeDMS, value); }

        private string _elevation = string.Empty;
        public string Elevation { get => _elevation; set => SetField(ref _elevation, value); }

        private bool _autoMeridianFlip;
        public bool AutoMeridianFlip { get => _autoMeridianFlip; set => SetField(ref _autoMeridianFlip, value); }

        private bool _pauseAtHome;
        public bool PauseAtHome { get => _pauseAtHome; set => SetField(ref _pauseAtHome, value); }

        private bool _buzzerEnabled;
        public bool BuzzerEnabled { get => _buzzerEnabled; set => SetField(ref _buzzerEnabled, value); }

        // ── Periodic telemetry tier (30s cadence) ───────────────────────────────────

        private double _ambientTemp;
        public double AmbientTemperatureCelsius { get => _ambientTemp; set => SetField(ref _ambientTemp, value); }

        private double _pressure;
        public double BarometricPressureMb { get => _pressure; set => SetField(ref _pressure, value); }

        private double _humidity;
        public double RelativeHumidityPercent { get => _humidity; set => SetField(ref _humidity, value); }

        private double _dewPoint;
        public double DewPointCelsius { get => _dewPoint; set => SetField(ref _dewPoint, value); }

        private double _controllerTemp;
        public double ControllerTemperatureCelsius { get => _controllerTemp; set => SetField(ref _controllerTemp, value); }

        private string _lastError = string.Empty;
        public string LastError { get => _lastError; set => SetField(ref _lastError, value); }

        // Connect-tier fields are cleared on disconnect; telemetry is left at its last known
        // value rather than reset, matching prior per-ViewModel behavior.
        public void ResetOnDisconnect() {
            IsConnected = false;
            Axis1Params.Clear();
            Axis2Params.Clear();
        }
    }
}
