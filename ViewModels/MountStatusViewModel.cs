using System;
using System.ComponentModel.Composition;
using System.Globalization;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin.OnStepXTools.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;

namespace NINA.Plugin.OnStepXTools.ViewModels {

    [Export(typeof(IDockableVM))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class MountStatusViewModel : DockableVM, ITelescopeConsumer, IDisposable {
        private readonly ITelescopeMediator _telescope;
        private readonly IOnStepXMount      _mount;

        public override string ContentId => "OnStepX_MountStatus";

        [ImportingConstructor]
        public MountStatusViewModel(ITelescopeMediator telescope, IProfileService profile, IOnStepXMount mount)
            : base(profile) {
            Title         = "OnStepX Mount Status";
            ImageGeometry = System.Windows.Application.Current?.Resources["TelescopeSVG"] as System.Windows.Media.GeometryGroup;
            _telescope    = telescope;
            _mount        = mount;
            _telescope.RegisterConsumer(this);
            _mount.State.PropertyChanged += OnMountStateChanged;
        }

        private void OnMountStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) =>
            Dispatch(() => RaisePropertyChanged(e.PropertyName));

        // ── Connection ───────────────────────────────────────────────────────────

        private bool   _isConnected;
        private string _telescopeName    = string.Empty;
        private string _description      = string.Empty;
        private string _driverInfo       = string.Empty;
        private string _driverVersion    = string.Empty;

        public bool   IsConnected      { get => _isConnected;    private set => SetProperty(ref _isConnected,    value); }
        public string TelescopeName    { get => _telescopeName;  private set => SetProperty(ref _telescopeName,  value); }
        public string Description      { get => _description;    private set => SetProperty(ref _description,    value); }
        public string DriverInfo       { get => _driverInfo;     private set => SetProperty(ref _driverInfo,     value); }
        public string DriverVersion    { get => _driverVersion;  private set => SetProperty(ref _driverVersion,  value); }

        // ── Equatorial position ──────────────────────────────────────────────────

        private string _raString  = string.Empty;
        private string _decString = string.Empty;

        public string RightAscensionString { get => _raString;  private set => SetProperty(ref _raString,  value); }
        public string DeclinationString    { get => _decString; private set => SetProperty(ref _decString, value); }

        // ── Horizontal position ──────────────────────────────────────────────────

        private string _altString = string.Empty;
        private string _azString  = string.Empty;

        public string AltitudeString { get => _altString; private set => SetProperty(ref _altString, value); }
        public string AzimuthString  { get => _azString;  private set => SetProperty(ref _azString,  value); }

        // ── Time ────────────────────────────────────────────────────────────────

        private string _lstString            = string.Empty;
        private string _utString             = string.Empty;
        private string _hoursToMeridian      = string.Empty;
        private string _timeToMeridianFlip   = string.Empty;

        public string LstString          { get => _lstString;          private set => SetProperty(ref _lstString,          value); }
        public string UtString           { get => _utString;           private set => SetProperty(ref _utString,           value); }
        public string HoursToMeridian    { get => _hoursToMeridian;    private set => SetProperty(ref _hoursToMeridian,    value); }
        public string TimeToMeridianFlip { get => _timeToMeridianFlip; private set => SetProperty(ref _timeToMeridianFlip, value); }

        // ── Mount state ──────────────────────────────────────────────────────────

        private string _sideOfPier            = string.Empty;
        private string _trackingRateDisplay   = string.Empty;
        private bool   _trackingEnabled;
        private bool   _slewing;
        private bool   _atPark;
        private bool   _atHome;
        private string _guideRateRA           = string.Empty;
        private string _guideRateDec          = string.Empty;
        private string _alignmentMode         = string.Empty;
        private string _epoch                 = string.Empty;

        public string SideOfPier          { get => _sideOfPier;          private set => SetProperty(ref _sideOfPier,          value); }
        public string TrackingRateDisplay  { get => _trackingRateDisplay; private set => SetProperty(ref _trackingRateDisplay, value); }
        public bool   TrackingEnabled      { get => _trackingEnabled;     private set => SetProperty(ref _trackingEnabled,     value); }
        public bool   Slewing              { get => _slewing;             private set => SetProperty(ref _slewing,             value); }
        public bool   AtPark               { get => _atPark;              private set => SetProperty(ref _atPark,              value); }
        public bool   AtHome               { get => _atHome;              private set => SetProperty(ref _atHome,              value); }
        public string GuideRateRA          { get => _guideRateRA;         private set => SetProperty(ref _guideRateRA,         value); }
        public string GuideRateDec         { get => _guideRateDec;        private set => SetProperty(ref _guideRateDec,        value); }
        public string AlignmentMode        { get => _alignmentMode;       private set => SetProperty(ref _alignmentMode,       value); }
        public string Epoch                { get => _epoch;               private set => SetProperty(ref _epoch,               value); }

        // ── Site ────────────────────────────────────────────────────────────────

        private string _siteLatitude  = string.Empty;
        private string _siteLongitude = string.Empty;
        private string _siteElevation = string.Empty;

        public string SiteLatitude  { get => _siteLatitude;  private set => SetProperty(ref _siteLatitude,  value); }
        public string SiteLongitude { get => _siteLongitude; private set => SetProperty(ref _siteLongitude, value); }
        public string SiteElevation { get => _siteElevation; private set => SetProperty(ref _siteElevation, value); }

        // ── Weather (OnStepX LX200 polling, refreshed by OnStepXMount's periodic tier) ──

        public double AmbientTemperatureCelsius    => _mount.State.AmbientTemperatureCelsius;
        public double BarometricPressureMb         => _mount.State.BarometricPressureMb;
        public double RelativeHumidityPercent      => _mount.State.RelativeHumidityPercent;
        public double DewPointCelsius              => _mount.State.DewPointCelsius;
        public double ControllerTemperatureCelsius => _mount.State.ControllerTemperatureCelsius;
        public string LastError                    => _mount.State.LastError;

        // ── ITelescopeConsumer ───────────────────────────────────────────────────

        public void UpdateDeviceInfo(TelescopeInfo info) {
            IsConnected = info.Connected;

            try { TelescopeName   = info.Name;          } catch { }
            try { Description     = info.Description;   } catch { }
            try { DriverInfo      = info.DriverInfo;    } catch { }
            try { DriverVersion   = info.DriverVersion; } catch { }

            try { RightAscensionString = info.RightAscensionString; } catch { }
            try { DeclinationString    = info.DeclinationString;    } catch { }
            try { AltitudeString       = info.AltitudeString;       } catch { }
            try { AzimuthString        = info.AzimuthString;        } catch { }

            try { LstString          = info.SiderealTimeString;    } catch { }
            try { UtString           = info.UTCDate.ToString("HH:mm:ss", CultureInfo.InvariantCulture); } catch { }
            try { HoursToMeridian    = info.HoursToMeridianString; } catch { }
            try { TimeToMeridianFlip = $"{info.TimeToMeridianFlip:F1} min"; } catch { }

            try { SideOfPier         = FormatPierSide(info.SideOfPier.ToString()); } catch { }
            try { TrackingRateDisplay = info.TrackingRate.TrackingMode.ToString();    } catch { }
            try { TrackingEnabled     = info.TrackingEnabled;                       } catch { }
            try { Slewing             = info.Slewing;                               } catch { }
            try { AtPark              = info.AtPark;                                } catch { }
            try { AtHome              = info.AtHome;                                } catch { }
            try { GuideRateRA  = $"{info.GuideRateRightAscensionArcsecPerSec:F2} \"/s"; } catch { }
            try { GuideRateDec = $"{info.GuideRateDeclinationArcsecPerSec:F2} \"/s";    } catch { }
            try { AlignmentMode = info.AlignmentMode.ToString(); } catch { }
            try { Epoch         = info.EquatorialSystem.ToString(); } catch { }

            try { SiteLatitude  = FormatDMS(info.SiteLatitude,  isLongitude: false); } catch { }
            try { SiteLongitude = FormatDMS(info.SiteLongitude, isLongitude: true);  } catch { }
            try { SiteElevation = $"{info.SiteElevation:F0} m"; } catch { }
        }

        private static void Dispatch(Action a) {
            var app = System.Windows.Application.Current;
            if (app != null) app.Dispatcher.Invoke(a);
            else a();
        }

        private static string FormatPierSide(string raw) =>
            raw.Replace("pier", "", StringComparison.OrdinalIgnoreCase).Trim();

        private static string FormatDMS(double deg, bool isLongitude) {
            var sign = deg < 0 ? "-" : "+";
            var abs  = Math.Abs(deg);
            var d    = (int)abs;
            var mf   = (abs - d) * 60;
            var m    = (int)mf;
            var s    = (int)Math.Round((mf - m) * 60);
            if (s == 60) { m++; s = 0; }
            if (m == 60) { d++; m = 0; }
            var fmt  = isLongitude ? $"{d:D3}" : $"{d:D2}";
            return $"{sign}{fmt}°{m:D2}'{s:D2}\"";
        }

        public void Dispose() {
            _mount.State.PropertyChanged -= OnMountStateChanged;
            _telescope.RemoveConsumer(this);
        }
    }
}
