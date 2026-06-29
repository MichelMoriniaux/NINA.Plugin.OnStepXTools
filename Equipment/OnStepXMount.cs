using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.OnStepXTools.Interfaces;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.Equipment {

    // Typed LX200 command layer for OnStepX firmware.
    // TODO: verify all command strings against https://github.com/hjd1964/OnStepX firmware source.
    [Export(typeof(IOnStepXMount))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class OnStepXMount : IOnStepXMount {
        private readonly LX200Commander _cmd;

        [ImportingConstructor]
        public OnStepXMount(ITelescopeMediator telescope) {
            _cmd = new LX200Commander(telescope);
        }

        // ── Alignment model ──────────────────────────────────────────────────────

        public Task<AlignmentModelCoefficients?> GetCoefficientsAsync(CancellationToken ct = default) {
            return Task.Run(() => {
                // Coefficient hex indices 0–b; 8=hcp(°), a=dcp(°), others arcseconds
                var c = new AlignmentModelCoefficients();
                c.Ax1Cor = ParseDouble(_cmd.SendString(":GX00#")) ?? 0;
                c.Ax2Cor = ParseDouble(_cmd.SendString(":GX01#")) ?? 0;
                c.AltCor = ParseDouble(_cmd.SendString(":GX02#")) ?? 0;
                c.AzmCor = ParseDouble(_cmd.SendString(":GX03#")) ?? 0;
                c.DoCor  = ParseDouble(_cmd.SendString(":GX04#")) ?? 0;
                c.PdCor  = ParseDouble(_cmd.SendString(":GX05#")) ?? 0;
                c.DfCor  = ParseDouble(_cmd.SendString(":GX06#")) ?? 0;
                c.TfCor  = ParseDouble(_cmd.SendString(":GX07#")) ?? 0;
                c.Hcp    = ParseDouble(_cmd.SendString(":GX08#")) ?? 0;
                c.Hca    = ParseDouble(_cmd.SendString(":GX09#")) ?? 0;
                c.Dcp    = ParseDouble(_cmd.SendString(":GX0a#")) ?? 0;
                c.Dca    = ParseDouble(_cmd.SendString(":GX0b#")) ?? 0;
                c.Stars  = ParseInt(_cmd.SendString(":GX09#")) ?? 0; // :GX09# also returns star count
                return (AlignmentModelCoefficients?)c;
            }, ct);
        }

        public Task<int> GetAlignmentStarCountAsync(CancellationToken ct = default) =>
            Task.Run(() => ParseInt(_cmd.SendString(":GX09#")) ?? 0, ct);

        // ── Motor config ─────────────────────────────────────────────────────────
        // TODO: verify exact command format against OnStepX firmware src/lib/commands/

        public Task<AxisConfig?> GetAxisConfigAsync(int axis, CancellationToken ct = default) =>
            Task.FromResult<AxisConfig?>(null); // placeholder - needs firmware source

        public Task SetAxisConfigAsync(int axis, AxisConfig config, CancellationToken ct = default) =>
            Task.Run(() => { /* TODO: implement ":SXGMn,<value>#" commands */ }, ct);

        public Task<PidConfig?> GetTrackingPidAsync(int axis, CancellationToken ct = default) =>
            Task.FromResult<PidConfig?>(null);

        public Task SetTrackingPidAsync(int axis, PidConfig pid, CancellationToken ct = default) =>
            Task.Run(() => {
                // TODO: verify command format against firmware PID section
                _cmd.SendBlind($":SXPt{axis},{pid.P.ToString(CultureInfo.InvariantCulture)}:{pid.I.ToString(CultureInfo.InvariantCulture)}:{pid.D.ToString(CultureInfo.InvariantCulture)}#");
            }, ct);

        public Task<PidConfig?> GetSlewingPidAsync(int axis, CancellationToken ct = default) =>
            Task.FromResult<PidConfig?>(null);

        public Task SetSlewingPidAsync(int axis, PidConfig pid, CancellationToken ct = default) =>
            Task.Run(() => {
                _cmd.SendBlind($":SXPs{axis},{pid.P.ToString(CultureInfo.InvariantCulture)}:{pid.I.ToString(CultureInfo.InvariantCulture)}:{pid.D.ToString(CultureInfo.InvariantCulture)}#");
            }, ct);

        public Task SetMountTypeAsync(MountType type, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind($":SXEM,{(int)type}#"), ct);

        public Task RebootAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(":ERESET#"), ct);

        public Task ServoCalibrationAsync(ServoCalibrationCommand cmd, CancellationToken ct = default) =>
            Task.Run(() => {
                // TODO: verify calibration command strings against firmware
                var lx = cmd switch {
                    ServoCalibrationCommand.TrackNormally       => ":TOA#",
                    ServoCalibrationCommand.TrackFixedRate      => ":TOF#",
                    ServoCalibrationCommand.RecordCalibration   => ":SXC1,1#",
                    ServoCalibrationCommand.StopRecording       => ":SXC1,0#",
                    ServoCalibrationCommand.ClearBuffer         => ":SXC2,0#",
                    ServoCalibrationCommand.LoadCalibration     => ":SXC3,0#",
                    ServoCalibrationCommand.SaveCalibration     => ":SXC3,1#",
                    ServoCalibrationCommand.LoadBackup          => ":SXC4,0#",
                    ServoCalibrationCommand.SaveBackup          => ":SXC4,1#",
                    ServoCalibrationCommand.HighPassFilter      => ":SXC5,1#",
                    ServoCalibrationCommand.LowPassFilter       => ":SXC5,0#",
                    _ => null
                };
                if (lx != null) _cmd.SendBlind(lx);
            }, ct);

        // ── Settings ─────────────────────────────────────────────────────────────

        public Task SetLocationAsync(double lonDeg, double latDeg, double elevM, CancellationToken ct = default) =>
            Task.Run(() => {
                _cmd.SendBlind($":St{FormatDMS(latDeg)}#");
                _cmd.SendBlind($":Sg{FormatDMS(lonDeg)}#");
                _cmd.SendBlind($":Sc{((int)elevM).ToString(CultureInfo.InvariantCulture)}#");
            }, ct);

        public Task SetTrackingAsync(bool enabled, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(enabled ? ":Te#" : ":Td#"), ct);

        public Task SetTrackingRateAsync(TrackingRate rate, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(rate switch {
                TrackingRate.Lunar    => ":TL#",
                TrackingRate.Solar    => ":TS#",
                TrackingRate.King     => ":TK#",
                _                     => ":TQ#"
            }), ct);

        public Task SetCompensatedTrackingAsync(CompensatedTracking mode, bool dualAxis, CancellationToken ct = default) =>
            Task.Run(() => {
                // TODO: verify command against firmware - :SXE1,n# or similar
                var v = mode switch {
                    CompensatedTracking.Full          => dualAxis ? 2 : 1,
                    CompensatedTracking.RefractionOnly => dualAxis ? 4 : 3,
                    _                                  => 0
                };
                _cmd.SendBlind($":SXE1,{v}#");
            }, ct);

        public Task AdjustTrackingFrequencyAsync(int direction, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(direction >= 0 ? ":T+#" : ":T-#"), ct);

        public Task ResetTrackingFrequencyAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(":TR#"), ct);

        public Task SetParkPositionAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(":SP#"), ct);

        public Task SetGuideRateAsync(int rateIndex, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind($":SXF3,{rateIndex}#"), ct);

        public Task SetSlewSpeedAsync(SlewSpeed speed, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(speed switch {
                SlewSpeed.VFast  => ":RS4#",
                SlewSpeed.Fast   => ":RS3#",
                SlewSpeed.Normal => ":RS2#",
                SlewSpeed.Slow   => ":RS1#",
                _                => ":RS0#"
            }), ct);

        public Task SetGotoBuzzerAsync(bool enabled, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind($":SXE5,{(enabled ? 1 : 0)}#"), ct);

        public Task SetAutoMeridianFlipAsync(bool enabled, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind($":SXE6,{(enabled ? 1 : 0)}#"), ct);

        public Task TriggerMeridianFlipAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(":Mf#"), ct);

        public Task SetPauseAtHomeAsync(bool enabled, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind($":SXE7,{(enabled ? 1 : 0)}#"), ct);

        public Task SetPreferredPierSideAsync(PreferredPierSide side, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind($":SXE8,{(int)side}#"), ct);

        public Task SetBacklashAsync(int axis1, int axis2, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind($":SXF5,{axis1}:{axis2}#"), ct);

        public Task SetLimitsAsync(double minAlt, double maxAlt, double east, double west, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(
                $":SXF6,{F(minAlt)}:{F(maxAlt)}:{F(east)}:{F(west)}#"), ct);

        // ── Alignment ────────────────────────────────────────────────────────────

        public Task ClearAlignmentModelAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(":SX09,0#"), ct);

        public Task UploadAlignmentStarAsync(
            long actualHAArcsec, long actualDecArcsec,
            long mountHAArcsec, long mountDecArcsec,
            int pierSide, CancellationToken ct = default) {
            return Task.Run(() => {
                // Protocol: :SX0A (actual HA), :SX0B (actual Dec),
                //           :SX0C (mount HA),  :SX0D (mount Dec),
                //           :SX0E (pier side and commit)
                _cmd.SendBlind($":SX0A,{actualHAArcsec}#");
                _cmd.SendBlind($":SX0B,{actualDecArcsec}#");
                _cmd.SendBlind($":SX0C,{mountHAArcsec}#");
                _cmd.SendBlind($":SX0D,{mountDecArcsec}#");
                _cmd.SendBlind($":SX0E,{pierSide}#");
            }, ct);
        }

        public Task ComputeAlignmentOnControllerAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(":SX09,1#"), ct);

        public Task SaveAlignmentToEepromAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(":AW#"), ct);

        public Task ForceModelActivationAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(":SX09,2#"), ct);

        public Task WriteCoefficientsAsync(AlignmentModelCoefficients c, CancellationToken ct = default) {
            return Task.Run(() => {
                static string V(double v) => v.ToString("F6", CultureInfo.InvariantCulture);
                _cmd.SendBlind($":SX00,{V(c.Ax1Cor)}#");
                _cmd.SendBlind($":SX01,{V(c.Ax2Cor)}#");
                _cmd.SendBlind($":SX02,{V(c.AltCor)}#");
                _cmd.SendBlind($":SX03,{V(c.AzmCor)}#");
                _cmd.SendBlind($":SX04,{V(c.DoCor)}#");
                _cmd.SendBlind($":SX05,{V(c.PdCor)}#");
                _cmd.SendBlind($":SX06,{V(c.DfCor)}#");
                _cmd.SendBlind($":SX07,{V(c.TfCor)}#");
                _cmd.SendBlind($":SX08,{V(c.Hcp)}#");
                _cmd.SendBlind($":SX09,{V(c.Hca)}#");
                _cmd.SendBlind($":SX0a,{V(c.Dcp)}#");
                _cmd.SendBlind($":SX0b,{V(c.Dca)}#");
            }, ct);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

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

        private static string FormatDMS(double deg) {
            var sign = deg < 0 ? "-" : "+";
            deg = Math.Abs(deg);
            var d = (int)deg;
            var m = (int)((deg - d) * 60);
            var sec = (deg - d - m / 60.0) * 3600;
            return $"{sign}{d:D2}:{m:D2}:{sec:F0}";
        }

        private static string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);
    }
}
