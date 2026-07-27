using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using NINA.Plugin.OnStepXTools.Interfaces;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.Equipment {

    // Typed LX200 command layer for OnStepX firmware, and the single mediator between the
    // plugin's views and the ASCOM driver. Owns State (cached settings/telemetry) and refreshes
    // it at different cadences: once on connect / on demand for settings that are also edit
    // targets, and periodically for pure read-only telemetry (weather). All transport access is
    // funneled through a single-slot gate so the periodic refresh and user-invoked commands never
    // collide on the wire.
    // TODO: verify all command strings against https://github.com/hjd1964/OnStepX firmware source.
    [Export(typeof(IOnStepXMount))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class OnStepXMount : IOnStepXMount, ITelescopeConsumer, IDisposable {
        private readonly ILx200Transport _cmd;
        private readonly ITelescopeMediator? _telescope;
        private readonly IProfileService? _profile;
        private readonly SemaphoreSlim _ioGate = new(1, 1);

        private bool _wasConnected;
        private long _tick;
        private int _weatherRefreshBusy;
        private char _axis1DriverType;
        private char _axis2DriverType;

        public OnStepXMountState State { get; } = new();

        [ImportingConstructor]
        public OnStepXMount(ITelescopeMediator telescope, IProfileService profile) {
            _cmd       = new LX200Commander(telescope);
            _telescope = telescope;
            _profile   = profile;
            _telescope.RegisterConsumer(this);
        }

        public OnStepXMount(ILx200Transport transport) {
            _cmd = transport;
        }

        public void Dispose() {
            _telescope?.RemoveConsumer(this);
        }

        // ── ITelescopeConsumer ───────────────────────────────────────────────────
        //
        // Fires once per NINA poll tick (DeviceUpdateTimer.Run() calls GetValuesFunc() then
        // UpdateValuesFunc(), which Broadcasts to consumers synchronously, strictly after that
        // tick's driver poll has fully completed - see NINA.WPF.Base's TelescopeVM/DeviceMediator).
        // Keep this handler cheap and non-blocking: any actual I/O is dispatched as a background
        // Task so we never delay delivery to other consumers or the next tick.

        public void UpdateDeviceInfo(TelescopeInfo info) {
            State.IsConnected = info.Connected;

            if (info.Connected && !_wasConnected) {
                _wasConnected = true;
                _ = Task.Run(() => OnConnectedAsync());
                return;
            }
            if (!info.Connected && _wasConnected) {
                _wasConnected = false;
                Dispatch(() => State.ResetOnDisconnect());
                return;
            }
            if (!info.Connected) return;

            _tick++;
            var interval = Math.Max(0.1, _profile?.ActiveProfile.ApplicationSettings.DevicePollingInterval ?? 2.0);
            var weatherEveryTicks = Math.Max(1, (int)Math.Round(30.0 / interval));
            if (_tick % weatherEveryTicks == 0) TryRefreshWeatherTier();
        }

        private async Task OnConnectedAsync() {
            try { State.FirmwareVersion = await GetFirmwareVersionAsync(); } catch { }
            Dispatch(() => {
                State.IsOldFormat = State.FirmwareVersion != null && !State.FirmwareVersion.IsAtLeast(10, 26, 'b');
                State.IsServoCalibrationSupported =
                    State.FirmwareVersion != null &&
                    State.FirmwareVersion.IsAtLeast(10, 23, 'a') &&
                    !State.FirmwareVersion.IsAtLeast(10, 26, 'c');
            });
            Logger.Info($"OnStepX firmware {FirmwareVersionText} - axis format: {(State.IsOldFormat ? "OLD" : "NEW")}");

            _ = EnsureModelActivatedAsync();
            await ReloadSettingsAsync();

            // The ASCOM.OnStep driver's IP transport never actually uploads Elevation to the
            // mount, it only updates the NINA representation - correct that here on every connect.
            if (_profile != null) {
                try {
                    var lat  = _profile.ActiveProfile.AstrometrySettings.Latitude;
                    var lon  = _profile.ActiveProfile.AstrometrySettings.Longitude;
                    var elev = _profile.ActiveProfile.AstrometrySettings.Elevation;
                    await SetLocationAsync(lon, lat, elev);
                } catch (Exception ex) {
                    Logger.Error($"OnStepXMount SetMountLocation: {ex.Message}");
                }
            }
        }

        private void TryRefreshWeatherTier() {
            if (Interlocked.CompareExchange(ref _weatherRefreshBusy, 1, 0) != 0) return; // previous run still in flight - skip this tick
            _ = Task.Run(async () => {
                try { await RefreshWeatherAsync(CancellationToken.None); }
                finally { Interlocked.Exchange(ref _weatherRefreshBusy, 0); }
            });
        }

        private async Task RefreshWeatherAsync(CancellationToken ct) {
            try {
                var temp = ParseDouble(await GatedSendStringAsync(OnStepXProtocol.GetWeatherTemperature(), ct));
                var pres = ParseDouble(await GatedSendStringAsync(OnStepXProtocol.GetWeatherPressure(), ct));
                var hum  = ParseDouble(await GatedSendStringAsync(OnStepXProtocol.GetWeatherHumidity(), ct));
                var dew  = ParseDouble(await GatedSendStringAsync(OnStepXProtocol.GetWeatherDewpoint(), ct));
                var ctmp = ParseDouble(await GatedSendStringAsync(OnStepXProtocol.GetControllerTemperature(), ct));
                var gu   = await GatedSendStringAsync(OnStepXProtocol.GetStatus(), ct);
                var err  = !string.IsNullOrWhiteSpace(gu) ? gu[^1].ToString(CultureInfo.InvariantCulture) : null;

                Dispatch(() => {
                    if (temp.HasValue) State.AmbientTemperatureCelsius    = temp.Value;
                    if (pres.HasValue) State.BarometricPressureMb         = pres.Value;
                    if (hum.HasValue)  State.RelativeHumidityPercent      = hum.Value;
                    if (dew.HasValue)  State.DewPointCelsius              = dew.Value;
                    if (ctmp.HasValue) State.ControllerTemperatureCelsius = ctmp.Value;
                    if (err != null)   State.LastError                    = err;
                });
            } catch (Exception ex) {
                Logger.Debug($"OnStepX weather poll: {ex.Message}");
            }
        }

        // ── Reload / save (connect + manual-reload tier) ──────────────────────────

        public async Task ReloadSettingsAsync(CancellationToken ct = default) {
            try {
                var gu = await GatedSendStringAsync(OnStepXProtocol.GetStatus(), ct);
                if (!string.IsNullOrEmpty(gu)) {
                    var n = gu.Length;
                    int? grIdx = null;
                    if (n >= 2) {
                        var d = gu[n - 2] - '0';
                        if (d is >= 0 and <= 9) grIdx = d;
                    }
                    Dispatch(() => {
                        if (grIdx.HasValue) State.GuideRateIndex = grIdx.Value;
                        State.TrackingEnabled  = !gu.Contains('n');
                        State.AutoMeridianFlip = gu.Contains('a');
                        State.PauseAtHome      = gu.Contains('u');
                        State.BuzzerEnabled    = gu.Contains('z');
                        State.TrackingRate = gu.Contains('(') ? TrackingRate.Lunar
                            : gu.Contains('O') ? TrackingRate.Solar
                            : gu.Contains('k') ? TrackingRate.King
                            : TrackingRate.Sidereal;
                        State.CompensatedTracking = gu.Contains('t') ? CompensatedTracking.Full
                            : gu.Contains('r') ? CompensatedTracking.RefractionOnly
                            : CompensatedTracking.Off;
                        State.CompensatedTrackingAxis = gu.Contains('s') ? CompensatedTrackingAxis.Single : CompensatedTrackingAxis.Dual;
                        State.LastError = gu[^1].ToString(CultureInfo.InvariantCulture);
                    });
                }

                var mtRaw = await GatedSendStringAsync(OnStepXProtocol.GetMountType(), ct);
                if (int.TryParse(mtRaw, out var mt) && Enum.IsDefined(typeof(MountType), mt))
                    Dispatch(() => State.MountType = ToWritableMountType((MountType)mt));

                var ps = await GatedSendStringAsync(OnStepXProtocol.GetPreferredPierSide(), ct);
                if (!string.IsNullOrWhiteSpace(ps))
                    Dispatch(() => State.PreferredPierSide = PierSideFromChar(ps));

                var bl1Raw = await GatedSendStringAsync(OnStepXProtocol.GetBacklashRa(), ct);
                if (int.TryParse(bl1Raw, out var bl1)) Dispatch(() => State.BacklashAxis1Arcsec = bl1);
                var bl2Raw = await GatedSendStringAsync(OnStepXProtocol.GetBacklashDec(), ct);
                if (int.TryParse(bl2Raw, out var bl2)) Dispatch(() => State.BacklashAxis2Arcsec = bl2);

                var altMinRaw = await GatedSendStringAsync(OnStepXProtocol.GetHorizonLimit(), ct);
                if (!string.IsNullOrWhiteSpace(altMinRaw) &&
                    int.TryParse(altMinRaw.Replace("*", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var minAlt))
                    Dispatch(() => State.LimitMinAltDeg = minAlt);
                var altMaxRaw = await GatedSendStringAsync(OnStepXProtocol.GetOverheadLimit(), ct);
                if (!string.IsNullOrWhiteSpace(altMaxRaw) &&
                    int.TryParse(altMaxRaw.Replace("*", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var maxAlt))
                    Dispatch(() => State.LimitMaxAltDeg = maxAlt);

                var meRaw = await GatedSendStringAsync(OnStepXProtocol.GetMeridianLimitEast(), ct);
                if (double.TryParse(meRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var me))
                    Dispatch(() => State.LimitEastPastMeridian = me / 4.0);
                var mwRaw = await GatedSendStringAsync(OnStepXProtocol.GetMeridianLimitWest(), ct);
                if (double.TryParse(mwRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var mw))
                    Dispatch(() => State.LimitWestPastMeridian = mw / 4.0);

                var latStr  = await GatedSendStringAsync(OnStepXProtocol.GetLatitude(), ct);
                var lonStr  = await GatedSendStringAsync(OnStepXProtocol.GetLongitude(), ct);
                var elevStr = await GatedSendStringAsync(OnStepXProtocol.GetElevation(), ct);
                if (!string.IsNullOrWhiteSpace(latStr))  Dispatch(() => State.LatitudeDMS  = latStr);
                if (!string.IsNullOrWhiteSpace(lonStr))  Dispatch(() => State.LongitudeDMS = lonStr);
                if (!string.IsNullOrWhiteSpace(elevStr)) Dispatch(() => State.Elevation    = elevStr);
            } catch (Exception ex) {
                Logger.Error($"OnStepXMount ReloadSettingsAsync: {ex.Message}");
            }

            await LoadAxisConfigAsync(ct);
        }

        // TODO support more axes , here we do 2 but OnStep supports up to 9
        private async Task LoadAxisConfigAsync(CancellationToken ct) {
            try {
                if (State.IsOldFormat) {
                    await LoadAxisParamsOldFormatAsync(1, State.Axis1Params, ct);
                    await LoadAxisParamsOldFormatAsync(2, State.Axis2Params, ct);
                } else {
                    var d1 = await GatedSendStringAsync(OnStepXProtocol.GetAxisName(1), ct) ?? "-";
                    var d2 = await GatedSendStringAsync(OnStepXProtocol.GetAxisName(2), ct) ?? "-";
                    Dispatch(() => { State.Axis1DriverId = d1; State.Axis2DriverId = d2; });
                    await LoadAxisParamsNewFormatAsync(1, State.Axis1Params, ct);
                    await LoadAxisParamsNewFormatAsync(2, State.Axis2Params, ct);
                }
                var total = State.Axis1Params.Count + State.Axis2Params.Count;
                Dispatch(() => State.RuntimeAxisConfigEnabled = total > 0);
            } catch (Exception ex) {
                Logger.Error($"OnStepXMount LoadAxisConfigAsync: {ex.Message}");
            }
        }

        private async Task LoadAxisParamsOldFormatAsync(int axis, ObservableCollection<AxisParameter> collection, CancellationToken ct) {
            var raw = await GatedSendStringAsync(OnStepXProtocol.GetAxisParamsOldFormat(axis), ct);
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
            if (axis == 1) _axis1DriverType = driverType; else _axis2DriverType = driverType;

            Dispatch(() => {
                if (axis == 1) State.Axis1DriverId = driverId; else State.Axis2DriverId = driverId;
                collection.Clear();
                foreach (var p in loaded) collection.Add(p);
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

        private async Task LoadAxisParamsNewFormatAsync(int axis, ObservableCollection<AxisParameter> collection, CancellationToken ct) {
            var countRaw = await GatedSendStringAsync(OnStepXProtocol.GetAxisParamsCount(axis), ct);
            var count = ParseInt(countRaw) ?? 0;
            Logger.Debug($"OnStepX :GXA{axis},0# raw: '{countRaw}'");

            var loaded = new List<AxisParameter>();
            for (int i = 1; i <= count; i++) {
                var r = await GatedSendStringAsync(OnStepXProtocol.GetAxisParameter(axis, i), ct);
                Logger.Debug($"OnStepX :GXA{axis},{i}# raw: '{r}'");
                var p = ParseNewFormatParam(i, r);
                if (p != null) loaded.Add(p);
            }
            Dispatch(() => { collection.Clear(); foreach (var p in loaded) collection.Add(p); });
        }

        public async Task SaveAxisAsync(int axis, CancellationToken ct = default) {
            var collection = axis == 1 ? State.Axis1Params : State.Axis2Params;
            bool isOld = State.IsOldFormat;
            char dtype = axis == 1 ? _axis1DriverType : _axis2DriverType;
            try {
                if (isOld) {
                    var values = new List<string>();
                    foreach (var p in collection) values.Add(p.EditValue);
                    if (values.Count > 0 && dtype != '\0') values[^1] += dtype;
                    var cmd = OnStepXProtocol.SetAxisParamsOldFormat(axis, string.Join(",", values));
                    await GatedSendBoolAsync(cmd, ct);
                } else {
                    foreach (var param in collection) {
                        if (param.EditValue == param.CurrentValue) continue;
                        var cmd = OnStepXProtocol.SetAxisParameter(axis, param.Index, param.EditValue);
                        await GatedSendBoolAsync(cmd, ct);
                    }
                }
            } catch (Exception ex) {
                Logger.Error($"OnStepXMount SaveAxisAsync axis {axis}: {ex.Message}");
            }

            if (isOld) await LoadAxisParamsOldFormatAsync(axis, collection, ct);
            else       await LoadAxisParamsNewFormatAsync(axis, collection, ct);
        }

        // OnStepX's axis-parameter name label table (Extended.Constants.h L_AXPN_*). The
        // firmware sends "$N" instead of the full label to save bandwidth; index N (1-based)
        // into this array to recover it.
        private static readonly string[] AxisParamLabels = {
            "Steps/degree",                        // $1  (first axis parameter)
            "Min limit, degs",                     // $2
            "Max limit, degs",                     // $3
            "Steps/um",                            // $4
            "Min limit, um",                       // $5
            "Max limit, um",                       // $6
            "Reverse",                             // $7  (first motor parameter)
            "Microsteps",                          // $8
            "Microsteps Goto",                     // $9
            "Decay mode",                          // $10
            "Decay mode Goto",                     // $11
            "mA Hold",                             // $12
            "mA Run",                              // $13
            "mA Goto",                             // $14
            "256x Interpolate",                    // $15
            "P Tracking",                          // $16
            "I Tracking",                          // $17
            "D Tracking",                          // $18
            "P Slewing",                           // $19
            "I Slewing",                           // $20
            "D Slewing",                           // $21
            "Rads/count",                          // $22
            "Steps/count ratio",                   // $23
            "Max accel, %/s/s",                    // $24
            "Min power, %",                        // $25
            "Max power, %"                         // $26
        };

        // Resolves a "$N" axis-parameter-name placeholder to its label; any other string
        // (including one that just happens not to start with '$') is returned unchanged.
        private static string ResolveParamName(string raw) {
            if (!raw.StartsWith('$')) return raw;
            if (int.TryParse(raw.AsSpan(1), out var n) && n >= 1 && n <= AxisParamLabels.Length)
                return AxisParamLabels[n - 1];
            return raw;
        }

        private static AxisParameter? ParseNewFormatParam(int index, string? response) {
            if (string.IsNullOrWhiteSpace(response)) return null;
            var parts = response.Split(',', 5);
            if (parts.Length < 4) return null;
            int.TryParse(parts[3].Trim(), out var typeCode);
            var name = parts.Length >= 5 ? ResolveParamName(parts[4].Trim()) : $"Parameter {index}";
            var value = parts[0].Trim();
            return new AxisParameter { Index = index, Name = name, CurrentValue = value,
                Min = parts.Length > 1 ? parts[1].Trim() : "", Max = parts.Length > 2 ? parts[2].Trim() : "",
                TypeCode = typeCode, IsImmediate = typeCode % 2 == 0 && typeCode > 0, EditValue = value };
        }

        private static MountType ToWritableMountType(MountType type) => type switch {
            MountType.GEM_TA or MountType.GEM_TAC => MountType.GEM,
            MountType.Fork_TA or MountType.Fork_TAC => MountType.Fork,
            MountType.AltAz_Unlimited => MountType.AltAz,
            MountType.AltAlt => MountType.Default,
            _ => type
        };

        // Mount encodes preferred pier side as a single character: B=Best, W=West, E=East, A=Auto
        private static PreferredPierSide PierSideFromChar(string? s) => s?.Trim() switch {
            "W" => PreferredPierSide.West,
            "E" => PreferredPierSide.East,
            "A" => PreferredPierSide.Auto,
            _   => PreferredPierSide.Best
        };

        private string FirmwareVersionText => State.FirmwareVersion != null
            ? $"{State.FirmwareVersion.Major}.{State.FirmwareVersion.Minor}{(State.FirmwareVersion.Patch == '\0' ? "" : State.FirmwareVersion.Patch.ToString())}"
            : "unknown";

        // Marshals to the UI thread when a WPF Application is running (required for
        // ObservableCollection changes like Axis1Params/Axis2Params); runs inline otherwise
        // (e.g. unit tests, which have no Application.Current) rather than silently dropping the
        // update.
        private static void Dispatch(Action a) {
            var app = System.Windows.Application.Current;
            if (app != null) app.Dispatcher.Invoke(a);
            else a();
        }

        // ── Alignment model ──────────────────────────────────────────────────────

        public async Task<AlignmentModelCoefficients?> GetCoefficientsAsync(CancellationToken ct = default) {
            var c = new AlignmentModelCoefficients();
            var mountType = await GetMountTypeAsync(ct);
            c.Ax1Cor = await ReadCoefficientAsync(0x00, ct);
            c.Ax2Cor = await ReadCoefficientAsync(0x01, ct);
            c.AltCor = await ReadCoefficientAsync(0x02, ct);
            c.AzmCor = await ReadCoefficientAsync(0x03, ct);
            c.DoCor  = await ReadCoefficientAsync(0x04, ct);
            c.PdCor  = await ReadCoefficientAsync(0x05, ct);
            c.DfCor  = await ReadCoefficientAsync(OnStepXProtocol.IsForkOrAltAz(mountType) ? 0x06 : 0x07, ct);
            c.TfCor  = await ReadCoefficientAsync(0x08, ct);
            c.Hcp    = await ReadCoefficientAsync(0x0a, ct);
            c.Hca    = await ReadCoefficientAsync(0x0b, ct);
            c.Dcp    = await ReadCoefficientAsync(0x0c, ct);
            c.Dca    = await ReadCoefficientAsync(0x0d, ct);
            // removing this for the time being as for the mount the star count is only relevant to the alignment model
            // this code is only relevant to the sky model. Additionally calling this resets the star count
            // c.Stars remain in the Object as it may be useful for model management
            // c.Stars  = ParseInt(await GatedSendStringAsync(OnStepXProtocol.GetStarCount(), ct)) ?? 0;
            return c;
        }

        public async Task<int> GetAlignmentStarCountAsync(CancellationToken ct = default) =>
            ParseInt(await GatedSendStringAsync(OnStepXProtocol.GetStarCount(), ct)) ?? 0;

        public async Task<AlignmentControllerStatus> GetAlignmentControllerStatusAsync(CancellationToken ct = default) =>
            ParseAlignmentStatus(await GatedSendStringAsync(OnStepXProtocol.AlignmentStatus(), ct));

        public async Task<bool> EnsureModelActivatedAsync(CancellationToken ct = default) {
            var coefficients = await GetCoefficientsAsync(ct);
            if (coefficients == null || !HasModelData(coefficients)) return false;
            await ForceModelActivationAsync(ct);   // wonder if this should be forced, what if the user does not want the model to be forcibly activated?
            return true;
        }

        // ── Motor config ─────────────────────────────────────────────────────────

        public async Task SetMountTypeAsync(MountType type, CancellationToken ct = default) {
            await GatedSendAckAsync(OnStepXProtocol.SetMountType(type), ct);
            Dispatch(() => State.MountType = type);
        }

        public async Task<MountType> GetMountTypeAsync(CancellationToken ct = default) =>
            (MountType)(ParseInt(await GatedSendStringAsync(OnStepXProtocol.GetMountType(), ct)) ?? 0);

        public async Task<FirmwareVersion?> GetFirmwareVersionAsync(CancellationToken ct = default) =>
            ParseFirmwareVersion(await GatedSendStringAsync(OnStepXProtocol.GetFirmwareVersion(), ct));

        public Task RebootAsync(CancellationToken ct = default) =>
            GatedSendBlindAsync(OnStepXProtocol.Reboot(), ct);

        public Task ClearEepromAsync(CancellationToken ct = default) =>
            GatedSendBlindAsync(OnStepXProtocol.ClearEeprom(), ct);

        public Task RevertAxisAsync(int axis, CancellationToken ct = default) =>
            GatedSendBlindAsync(OnStepXProtocol.RevertAxis(axis), ct);

        public Task SetEncoderOriginAsync(CancellationToken ct = default) =>
            GatedSendBlindAsync(OnStepXProtocol.SetEncoderOrigin(), ct);

        public async Task SetRuntimeAxisConfigAsync(bool enabled, CancellationToken ct = default) {
            await GatedSendBlindAsync(OnStepXProtocol.SetRuntimeAxisConfig(enabled), ct);
            Dispatch(() => State.RuntimeAxisConfigEnabled = enabled);
        }

        public async Task ServoCalibrationAsync(ServoCalibrationCommand cmd, CancellationToken ct = default) {
            // TODO: verify calibration command strings against firmware
            var lx = cmd switch {
                ServoCalibrationCommand.TrackNormally       => OnStepXProtocol.ServoTrackNormal(),
                ServoCalibrationCommand.TrackFixedRate      => OnStepXProtocol.ServoTrackFixed(),
                ServoCalibrationCommand.RecordCalibration   => OnStepXProtocol.ServoRecord(),
                ServoCalibrationCommand.StopRecording       => OnStepXProtocol.ServoStop(),
                ServoCalibrationCommand.ClearBuffer         => OnStepXProtocol.ServoClear(),
                ServoCalibrationCommand.LoadCalibration     => OnStepXProtocol.ServoLoadCal(),
                ServoCalibrationCommand.SaveCalibration     => OnStepXProtocol.ServoSaveCal(),
                ServoCalibrationCommand.LoadBackup          => OnStepXProtocol.ServoLoadBackup(),
                ServoCalibrationCommand.SaveBackup          => OnStepXProtocol.ServoSaveBackup(),
                ServoCalibrationCommand.HighPassFilter      => OnStepXProtocol.ServoHpf(),
                ServoCalibrationCommand.LowPassFilter       => OnStepXProtocol.ServoLpf(),
                _ => null
            };
            if (lx != null) await GatedSendAckAsync(lx, ct);
        }

        // ── Settings ─────────────────────────────────────────────────────────────

        public async Task SetLocationAsync(double longitudeDeg, double latitudeDeg, double elevationM, CancellationToken ct = default) {
            // The ASCOM.OnStep driver's IP transport can drop the reply to raw commands sent
            // back-to-back ("SendMessageIP failed: no command/response after 3 attempts") - the
            // gate gives each round-trip time to complete before the next one starts.
            await GatedSendAckAsync(OnStepXProtocol.Latitude(latitudeDeg), ct);
            await GatedSendAckAsync(OnStepXProtocol.LongitudeFromEastPositive(longitudeDeg), ct);
            await GatedSendAckAsync(OnStepXProtocol.Elevation(elevationM), ct);
        }

        public async Task SetTrackingAsync(bool enabled, CancellationToken ct = default) {
            await GatedSendAckAsync(OnStepXProtocol.Tracking(enabled), ct);
            Dispatch(() => State.TrackingEnabled = enabled);
        }

        public async Task SetTrackingRateAsync(TrackingRate rate, CancellationToken ct = default) {
            await GatedSendBlindAsync(OnStepXProtocol.TrackingRate(rate), ct);
            Dispatch(() => State.TrackingRate = rate);
        }

        public async Task SetCompensatedTrackingAsync(CompensatedTracking mode, bool dualAxis, CancellationToken ct = default) {
            await GatedSendAckAsync(OnStepXProtocol.CompensatedTracking(mode), ct);
            if (mode != CompensatedTracking.Off)
                await GatedSendAckAsync(OnStepXProtocol.CompensatedTrackingAxis(
                    dualAxis ? CompensatedTrackingAxis.Dual : CompensatedTrackingAxis.Single), ct);
            Dispatch(() => {
                State.CompensatedTracking = mode;
                if (mode != CompensatedTracking.Off)
                    State.CompensatedTrackingAxis = dualAxis ? CompensatedTrackingAxis.Dual : CompensatedTrackingAxis.Single;
            });
        }

        public async Task SetCompensatedTrackingAxisAsync(CompensatedTrackingAxis axis, CancellationToken ct = default) {
            await GatedSendAckAsync(OnStepXProtocol.CompensatedTrackingAxis(axis), ct);
            Dispatch(() => State.CompensatedTrackingAxis = axis);
        }

        public Task AdjustTrackingFrequencyAsync(int direction, CancellationToken ct = default) =>
            GatedSendBlindAsync(OnStepXProtocol.TrackingFrequencyAdjust(direction), ct);

        public Task ResetTrackingFrequencyAsync(CancellationToken ct = default) =>
            GatedSendBlindAsync(OnStepXProtocol.TrackingFrequencyReset(), ct);

        public Task SetHomePositionAsync(CancellationToken ct = default) =>
            GatedSendBlindAsync(OnStepXProtocol.SetHomePosition(), ct);

        public Task SetParkPositionAsync(CancellationToken ct = default) =>
            GatedSendAckAsync(OnStepXProtocol.SetParkPosition(), ct);

        public Task ContinueGotoAfterPauseAsync(CancellationToken ct = default) =>
            GatedSendBlindAsync(OnStepXProtocol.ContinueGotoAfterPause(), ct);

        public async Task SetGuideRateAsync(int rateIndex, CancellationToken ct = default) {
            var idx = Math.Clamp(rateIndex, 0, 9);
            await GatedSendBlindAsync(OnStepXProtocol.GuideRatePreset(idx), ct);
            Dispatch(() => State.GuideRateIndex = idx);
        }

        public Task SetSlewSpeedAsync(SlewSpeed speed, CancellationToken ct = default) =>
            GatedSendBlindAsync(OnStepXProtocol.SlewSpeedPreset(speed), ct);

        public async Task SetGotoBuzzerAsync(bool enabled, CancellationToken ct = default) {
            await GatedSendAckAsync(OnStepXProtocol.GotoBuzzer(enabled), ct);
            Dispatch(() => State.BuzzerEnabled = enabled);
        }

        public async Task SetAutoMeridianFlipAsync(bool enabled, CancellationToken ct = default) {
            await GatedSendAckAsync(OnStepXProtocol.AutoMeridianFlip(enabled), ct);
            Dispatch(() => State.AutoMeridianFlip = enabled);
        }

        public Task TriggerMeridianFlipAsync(CancellationToken ct = default) =>
            GatedSendBlindAsync(OnStepXProtocol.MeridianFlipNow(), ct);

        public async Task SetPauseAtHomeAsync(bool enabled, CancellationToken ct = default) {
            await GatedSendAckAsync(OnStepXProtocol.PauseAtHome(enabled), ct);
            Dispatch(() => State.PauseAtHome = enabled);
        }

        public async Task SetPreferredPierSideAsync(PreferredPierSide side, CancellationToken ct = default) {
            await GatedSendAckAsync(OnStepXProtocol.PreferredPierSide(side), ct);
            Dispatch(() => State.PreferredPierSide = side);
        }

        public async Task SetBacklashAsync(int axis1, int axis2, CancellationToken ct = default) {
            await GatedSendAckAsync(OnStepXProtocol.BacklashRa(axis1), ct);
            await GatedSendAckAsync(OnStepXProtocol.BacklashDec(axis2), ct);
            Dispatch(() => { State.BacklashAxis1Arcsec = axis1; State.BacklashAxis2Arcsec = axis2; });
        }

        public async Task SetLimitsAsync(double minAlt, double maxAlt, double east, double west, CancellationToken ct = default) {
            await GatedSendAckAsync(OnStepXProtocol.HorizonLimit(minAlt), ct);
            await GatedSendAckAsync(OnStepXProtocol.OverheadLimit(maxAlt), ct);
            await GatedSendAckAsync(OnStepXProtocol.MeridianLimitEast(east), ct);
            await GatedSendAckAsync(OnStepXProtocol.MeridianLimitWest(west), ct);
            Dispatch(() => {
                State.LimitMinAltDeg = (int)Math.Round(minAlt);
                State.LimitMaxAltDeg = (int)Math.Round(maxAlt);
                State.LimitEastPastMeridian = east;
                State.LimitWestPastMeridian = west;
            });
        }

        // ── Alignment ────────────────────────────────────────────────────────────

        public Task ClearAlignmentModelAsync(CancellationToken ct = default) =>
            GatedSendAckAsync(OnStepXProtocol.AlignmentReset(), ct);

        public async Task UploadAlignmentStarAsync(
            double actualHAHours, double actualDecDeg,
            double mountHAHours,  double mountDecDeg,
            int pierSide, CancellationToken ct = default) {
            await GatedSendAckAsync(OnStepXProtocol.StarActualHa(actualHAHours), ct);
            await GatedSendAckAsync(OnStepXProtocol.StarActualDec(actualDecDeg), ct);
            await GatedSendAckAsync(OnStepXProtocol.StarMountHa(mountHAHours), ct);
            await GatedSendAckAsync(OnStepXProtocol.StarMountDec(mountDecDeg), ct);
            await GatedSendAckAsync(OnStepXProtocol.StarCommit(pierSide), ct);
        }

        public Task ComputeAlignmentOnControllerAsync(CancellationToken ct = default) =>
            GatedSendAckAsync(OnStepXProtocol.AlignmentCompute(), ct);

        public Task SaveAlignmentToEepromAsync(CancellationToken ct = default) =>
            GatedSendAckAsync(OnStepXProtocol.AlignmentWriteNv(), ct);

        public Task ForceModelActivationAsync(CancellationToken ct = default) =>
            GatedSendAckAsync(OnStepXProtocol.AlignmentActivate(), ct);

        public async Task WriteCoefficientsAsync(AlignmentModelCoefficients c, CancellationToken ct = default) {
            var expected = RoundCoefficients(c);
            var mountType = await GetMountTypeAsync(ct);
            await GatedSendAckAsync(OnStepXProtocol.SetCoefficient(0x00, expected.Ax1Cor), ct);
            await GatedSendAckAsync(OnStepXProtocol.SetCoefficient(0x01, expected.Ax2Cor), ct);
            await GatedSendAckAsync(OnStepXProtocol.SetCoefficient(0x02, expected.AltCor), ct);
            await GatedSendAckAsync(OnStepXProtocol.SetCoefficient(0x03, expected.AzmCor), ct);
            await GatedSendAckAsync(OnStepXProtocol.SetCoefficient(0x04, expected.DoCor), ct);
            await GatedSendAckAsync(OnStepXProtocol.SetCoefficient(0x05, expected.PdCor), ct);
            await GatedSendAckAsync(OnStepXProtocol.SetCoefficient(OnStepXProtocol.IsForkOrAltAz(mountType) ? 0x06 : 0x07, expected.DfCor), ct);
            await GatedSendAckAsync(OnStepXProtocol.SetCoefficient(0x08, expected.TfCor), ct);
            await GatedSendAckAsync(OnStepXProtocol.SetCoefficient(0x0a, expected.Hcp), ct);
            await GatedSendAckAsync(OnStepXProtocol.SetCoefficient(0x0b, expected.Hca), ct);
            await GatedSendAckAsync(OnStepXProtocol.SetCoefficient(0x0c, expected.Dcp), ct);
            await GatedSendAckAsync(OnStepXProtocol.SetCoefficient(0x0d, expected.Dca), ct);
            await GatedSendAckAsync(OnStepXProtocol.AlignmentActivate(), ct);

            // reread from the mount to verify that the coefficients were properly written
            var actual = await GetCoefficientsAsync(ct);
            if (actual == null || !CoefficientsMatch(expected, RoundCoefficients(actual))) {
                throw new InvalidOperationException("OnStepX coefficient write verification failed.");
            }
        }

        // ── Gated transport access ──────────────────────────────────────────────
        //
        // Every individual command acquires the gate for the duration of exactly one round-trip,
        // then releases it - never held across an await spanning multiple commands, so composite
        // operations (e.g. WriteCoefficientsAsync's ~13 commands) can't deadlock against each
        // other, and the periodic weather refresh can only ever interleave between two complete
        // commands, never overlap one.

        private async Task<string?> GatedSendStringAsync(string command, CancellationToken ct) {
            await _ioGate.WaitAsync(ct).ConfigureAwait(false);
            try { return await Task.Run(() => _cmd.SendString(command), ct).ConfigureAwait(false); }
            finally { _ioGate.Release(); }
        }

        private async Task<bool> GatedSendBlindAsync(string command, CancellationToken ct) {
            await _ioGate.WaitAsync(ct).ConfigureAwait(false);
            try { return await Task.Run(() => _cmd.SendBlind(command), ct).ConfigureAwait(false); }
            finally { _ioGate.Release(); }
        }

        private async Task<bool> GatedSendBoolAsync(string command, CancellationToken ct) {
            await _ioGate.WaitAsync(ct).ConfigureAwait(false);
            try { return await Task.Run(() => _cmd.SendBool(command), ct).ConfigureAwait(false); }
            finally { _ioGate.Release(); }
        }

        private async Task GatedSendAckAsync(string command, CancellationToken ct) {
            await _ioGate.WaitAsync(ct).ConfigureAwait(false);
            try { await Task.Run(() => _cmd.SendAck(command), ct).ConfigureAwait(false); }
            finally { _ioGate.Release(); }
        }

        private async Task<int> ReadCoefficientAsync(int hexRegister, CancellationToken ct) =>
            ParseInt(await GatedSendStringAsync(OnStepXProtocol.GetCoefficient(hexRegister), ct)) ?? 0;

        // ── Parsing helpers ──────────────────────────────────────────────────────

        private static double? ParseHMS(string? s) {
            if (s == null) return null;
            var p = s.Split(':');
            if (p.Length < 3) return null;
            if (!double.TryParse(p[0], out var h) ||
                !double.TryParse(p[1], out var m) ||
                !double.TryParse(p[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var sec)) return null;
            return h + m / 60.0 + sec / 3600.0;
        }

        private static double? ParseDMS(string? s) {
            if (s == null) return null;
            var sign = s.StartsWith('-') ? -1 : 1;
            s = s.TrimStart('+', '-');
            var p = s.Split(':', '*', '#', '°', '\'', '"');
            if (p.Length < 2) return null;
            if (!double.TryParse(p[0], out var d)) return null;
            double.TryParse(p.Length > 1 ? p[1] : "0", out var m);
            double.TryParse(p.Length > 2 ? p[2] : "0", NumberStyles.Any, CultureInfo.InvariantCulture, out var sec);
            return sign * (d + m / 60.0 + sec / 3600.0);
        }

        private static double? ParseSignedDeg(string? s) => ParseDMS(s);

        private static double? ParseDouble(string? s) {
            if (s == null) return null;
            return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        private static int? ParseInt(string? s) {
            if (s == null) return null;
            return int.TryParse(s, out var v) ? v : null;
        }

        // ":GVN#" replies with something like "10.28t#" (major.minor followed by an optional
        // single patch letter) - LX200Commander.SendString already strips the trailing '#'.
        private static readonly Regex FirmwareVersionPattern = new(@"(\d+)\.(\d+)([a-zA-Z]?)");

        private static FirmwareVersion? ParseFirmwareVersion(string? s) {
            if (s == null) return null;
            var m = FirmwareVersionPattern.Match(s);
            if (!m.Success) return null;
            if (!int.TryParse(m.Groups[1].Value, out var major)) return null;
            if (!int.TryParse(m.Groups[2].Value, out var minor)) return null;
            var patch = m.Groups[3].Value.Length > 0 ? m.Groups[3].Value[0] : '\0';
            return new FirmwareVersion { Major = major, Minor = minor, Patch = patch };
        }

        private static AlignmentControllerStatus ParseAlignmentStatus(string? raw) {
            raw ??= string.Empty;
            var max = raw.Length > 0 && char.IsDigit(raw[0]) ? raw[0] - '0' : 0;
            var current = raw.Length > 1 && char.IsDigit(raw[1]) ? raw[1] - '0' : 0;
            var last = raw.Length > 2 && char.IsDigit(raw[2]) ? raw[2] - '0' : 0;
            return new AlignmentControllerStatus {
                MaxStars = max,
                CurrentStar = current,
                LastStar = last
            };
        }

        private static RoundedCoefficients RoundCoefficients(AlignmentModelCoefficients c) => new() {
            Ax1Cor = OnStepXProtocol.RoundToInt(c.Ax1Cor),
            Ax2Cor = OnStepXProtocol.RoundToInt(c.Ax2Cor),
            AltCor = OnStepXProtocol.RoundToInt(c.AltCor),
            AzmCor = OnStepXProtocol.RoundToInt(c.AzmCor),
            DoCor = OnStepXProtocol.RoundToInt(c.DoCor),
            PdCor = OnStepXProtocol.RoundToInt(c.PdCor),
            DfCor = OnStepXProtocol.RoundToInt(c.DfCor),
            TfCor = OnStepXProtocol.RoundToInt(c.TfCor),
            Hcp = OnStepXProtocol.RoundToInt(c.Hcp),
            Hca = OnStepXProtocol.RoundToInt(c.Hca),
            Dcp = OnStepXProtocol.RoundToInt(c.Dcp),
            Dca = OnStepXProtocol.RoundToInt(c.Dca)
        };

        private static bool CoefficientsMatch(RoundedCoefficients expected, RoundedCoefficients actual) =>
            expected.Ax1Cor == actual.Ax1Cor &&
            expected.Ax2Cor == actual.Ax2Cor &&
            expected.AltCor == actual.AltCor &&
            expected.AzmCor == actual.AzmCor &&
            expected.DoCor == actual.DoCor &&
            expected.PdCor == actual.PdCor &&
            expected.DfCor == actual.DfCor &&
            expected.TfCor == actual.TfCor &&
            expected.Hcp == actual.Hcp &&
            expected.Hca == actual.Hca &&
            expected.Dcp == actual.Dcp &&
            expected.Dca == actual.Dca;

        private static bool HasModelData(AlignmentModelCoefficients c) {
            var rounded = RoundCoefficients(c);
            return c.Stars > 0 ||
                   rounded.Ax1Cor != 0 || rounded.Ax2Cor != 0 ||
                   rounded.AltCor != 0 || rounded.AzmCor != 0 ||
                   rounded.DoCor != 0 || rounded.PdCor != 0 ||
                   rounded.DfCor != 0 || rounded.TfCor != 0 ||
                   rounded.Hcp != 0 || rounded.Hca != 0 ||
                   rounded.Dcp != 0 || rounded.Dca != 0;
        }

        // TODO: why? we have AlignmentModelCoefficients that is very similar
        private sealed class RoundedCoefficients {
            public int Ax1Cor { get; init; }
            public int Ax2Cor { get; init; }
            public int AltCor { get; init; }
            public int AzmCor { get; init; }
            public int DoCor { get; init; }
            public int PdCor { get; init; }
            public int DfCor { get; init; }
            public int TfCor { get; init; }
            public int Hcp { get; init; }
            public int Hca { get; init; }
            public int Dcp { get; init; }
            public int Dca { get; init; }
        }
    }
}
