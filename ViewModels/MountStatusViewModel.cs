using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin.OnStepXTools.Equipment;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;

namespace NINA.Plugin.OnStepXTools.ViewModels {

    [Export(typeof(IDockableVM))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class MountStatusViewModel : DockableVM, ITelescopeConsumer, IDisposable {
        private readonly ITelescopeMediator _telescope;
        private CancellationTokenSource? _weatherCts;

        public override string ContentId => "OnStepX_MountStatus";

        [ImportingConstructor]
        public MountStatusViewModel(ITelescopeMediator telescope, IProfileService profile)
            : base(profile) {
            Title         = "OnStepX Mount Status";
            ImageGeometry = System.Windows.Application.Current?.Resources["TelescopeSVG"] as System.Windows.Media.GeometryGroup;
            _telescope    = telescope;
            _telescope.RegisterConsumer(this);
            StartWeatherPolling();
        }

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

        // ── Weather (OnStepX LX200 polling) ─────────────────────────────────────

        private double _ambientTemp;
        private double _pressure;
        private double _humidity;
        private double _dewPoint;
        private double _controllerTemp;
        private string _lastError = string.Empty;

        public double AmbientTemperatureCelsius    { get => _ambientTemp;     private set => SetProperty(ref _ambientTemp,     value); }
        public double BarometricPressureMb         { get => _pressure;        private set => SetProperty(ref _pressure,        value); }
        public double RelativeHumidityPercent      { get => _humidity;        private set => SetProperty(ref _humidity,        value); }
        public double DewPointCelsius              { get => _dewPoint;        private set => SetProperty(ref _dewPoint,        value); }
        public double ControllerTemperatureCelsius { get => _controllerTemp;  private set => SetProperty(ref _controllerTemp,  value); }
        public string LastError                    { get => _lastError;        private set => SetProperty(ref _lastError,       value); }

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

        // ── Weather polling (OnStepX LX200 commands) ─────────────────────────────

        private void StartWeatherPolling() {
            _weatherCts = new CancellationTokenSource();
            _ = Task.Run(() => WeatherLoopAsync(_weatherCts.Token));
        }

        private async Task WeatherLoopAsync(CancellationToken ct) {
            await Task.Delay(5000, ct).ContinueWith(_ => { }, CancellationToken.None);
            while (!ct.IsCancellationRequested) {
                PollWeather();
                try { await Task.Delay(30_000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        private void PollWeather() {
            try {
                var temp = GetDouble(OnStepXProtocol.GetWeatherTemperature());
                var pres = GetDouble(OnStepXProtocol.GetWeatherPressure());
                var hum  = GetDouble(OnStepXProtocol.GetWeatherHumidity());
                var dew  = GetDouble(OnStepXProtocol.GetWeatherDewpoint());
                var ctmp = GetDouble(OnStepXProtocol.GetControllerTemperature());
                var err  = GetLastErrorFromGu();

                System.Windows.Application.Current?.Dispatcher.Invoke(() => {
                    if (temp.HasValue) AmbientTemperatureCelsius    = temp.Value;
                    if (pres.HasValue) BarometricPressureMb         = pres.Value;
                    if (hum.HasValue)  RelativeHumidityPercent      = hum.Value;
                    if (dew.HasValue)  DewPointCelsius              = dew.Value;
                    if (ctmp.HasValue) ControllerTemperatureCelsius = ctmp.Value;
                    if (err != null)   LastError                    = err;
                });
            } catch (Exception ex) {
                Logger.Debug($"OnStepX weather poll: {ex.Message}");
            }
        }

        private double? GetDouble(string cmd) {
            try {
                var r = _telescope.SendCommandString(cmd, raw: true);
                if (string.IsNullOrWhiteSpace(r)) return null;
                r = r.TrimEnd('#').Trim();
                return double.TryParse(r, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
            } catch { return null; }
        }

        private string? GetString(string cmd) {
            try {
                var r = _telescope.SendCommandString(cmd, raw: true);
                return string.IsNullOrWhiteSpace(r) ? null : r.TrimEnd('#').Trim();
            } catch { return null; }
        }

        // maybe get this from OnStepXMount.cs (merge with struff from settings pane)
        private string? GetLastErrorFromGu() {
            var gu = GetString(OnStepXProtocol.GetStatus());
            if (string.IsNullOrWhiteSpace(gu)) return null;
            return gu[^1].ToString(CultureInfo.InvariantCulture);
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
            _weatherCts?.Cancel();
            _telescope.RemoveConsumer(this);
        }
    }
}
