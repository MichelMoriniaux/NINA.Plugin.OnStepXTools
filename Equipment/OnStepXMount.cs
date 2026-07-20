using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.OnStepXTools.Interfaces;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.Equipment {

    // Typed LX200 command layer for OnStepX firmware.
    // TODO: verify all command strings against https://github.com/hjd1964/OnStepX firmware source.
    [Export(typeof(IOnStepXMount))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class OnStepXMount : IOnStepXMount {
        private readonly ILx200Transport _cmd;

        [ImportingConstructor]
        public OnStepXMount(ITelescopeMediator telescope) {
            _cmd = new LX200Commander(telescope);
        }

        public OnStepXMount(ILx200Transport transport) {
            _cmd = transport;
        }

        // ── Alignment model ──────────────────────────────────────────────────────

        public Task<AlignmentModelCoefficients?> GetCoefficientsAsync(CancellationToken ct = default) {
            return Task.Run( async () => {
                var c = new AlignmentModelCoefficients();
                var mountType = await GetMountTypeAsync(ct);
                c.Ax1Cor = ReadCoefficient(0x00);
                c.Ax2Cor = ReadCoefficient(0x01);
                c.AltCor = ReadCoefficient(0x02);
                c.AzmCor = ReadCoefficient(0x03);
                c.DoCor  = ReadCoefficient(0x04);
                c.PdCor  = ReadCoefficient(0x05);
                c.DfCor  = ReadCoefficient(OnStepXProtocol.IsForkOrAltAz(mountType) ? 0x06 : 0x07);
                c.TfCor  = ReadCoefficient(0x08);
                c.Hcp    = ReadCoefficient(0x0a);
                c.Hca    = ReadCoefficient(0x0b);
                c.Dcp    = ReadCoefficient(0x0c);
                c.Dca    = ReadCoefficient(0x0d);
                // removing this for the time being as for the mount the star count is only relevant to the alignment model
                // this code is only relevant to the sky model. Additionally calling this resets the star count
                // c.Stars remain in the Object as it may be useful for model management
                // c.Stars  = ParseInt(_cmd.SendString(OnStepXProtocol.GetStarCount())) ?? 0;
                return (AlignmentModelCoefficients?)c;
            }, ct);
        }

        public Task<int> GetAlignmentStarCountAsync(CancellationToken ct = default) =>
            Task.Run(() => ParseInt(_cmd.SendString(OnStepXProtocol.GetStarCount())) ?? 0, ct);

        public Task<AlignmentControllerStatus> GetAlignmentControllerStatusAsync(CancellationToken ct = default) =>
            Task.Run(() => ParseAlignmentStatus(_cmd.SendString(OnStepXProtocol.AlignmentStatus())), ct);

        public async Task<bool> EnsureModelActivatedAsync(CancellationToken ct = default) {
            var coefficients = await GetCoefficientsAsync(ct);
            if (coefficients == null || !HasModelData(coefficients)) return false;
            await ForceModelActivationAsync(ct);   // wonder if this should be forced, what if the user does not want the model to be forcibly activated?
            return true;
        }

        // ── Motor config ─────────────────────────────────────────────────────────
        // TODO: verify exact command format against OnStepX firmware src/lib/commands/
        // These methods exist in MountSettingsViewModel.cs they should be pulled in here and called from there
        // and all of these parameters are called from a single SXAa,i,p call
        public Task<AxisConfig?> GetAxisConfigAsync(int axis, CancellationToken ct = default) =>
            Task.FromResult<AxisConfig?>(null); // placeholder - needs firmware source

        public Task SetAxisConfigAsync(int axis, AxisConfig config, CancellationToken ct = default) =>
            Task.Run(() => { /* TODO: implement ":SXGMn,<value>#" commands */ }, ct);

        public Task<PidConfig?> GetTrackingPidAsync(int axis, CancellationToken ct = default) =>
            Task.FromResult<PidConfig?>(null);

        public Task SetTrackingPidAsync(int axis, PidConfig pid, CancellationToken ct = default) =>
            Task.FromException(new NotSupportedException(
                "Tracking PID writes are disabled until the OnStepX firmware command form is verified."));

        public Task<PidConfig?> GetSlewingPidAsync(int axis, CancellationToken ct = default) =>
            Task.FromResult<PidConfig?>(null);

        public Task SetSlewingPidAsync(int axis, PidConfig pid, CancellationToken ct = default) =>
            Task.FromException(new NotSupportedException(
                "Slewing PID writes are disabled until the OnStepX firmware command form is verified."));
        // end useless commands

        public Task SetMountTypeAsync(MountType type, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendAck(OnStepXProtocol.SetMountType(type)), ct);

        public Task<MountType> GetMountTypeAsync(CancellationToken ct = default) =>
            Task.Run(() => (MountType)(ParseInt(_cmd.SendString(OnStepXProtocol.GetMountType())) ?? 0), ct);

        public Task<FirmwareVersion?> GetFirmwareVersionAsync(CancellationToken ct = default) =>
            Task.Run(() => ParseFirmwareVersion(_cmd.SendString(OnStepXProtocol.GetFirmwareVersion())), ct);

        public Task RebootAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(OnStepXProtocol.Reboot()), ct);

        public Task ServoCalibrationAsync(ServoCalibrationCommand cmd, CancellationToken ct = default) =>
            Task.Run(() => {
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
                if (lx != null) _cmd.SendAck(lx);
            }, ct);
        // ── Settings ─────────────────────────────────────────────────────────────

        public Task SetLocationAsync(double longitudeDeg, double latitudeDeg, double elevationM, CancellationToken ct = default) =>
            Task.Run(() => {
                // The ASCOM.OnStep driver's IP transport can drop the reply to raw commands sent
                // back-to-back ("SendMessageIP failed: no command/response after 3 attempts") -
                // pace them out to give each round-trip time to complete.
                _cmd.SendAck(OnStepXProtocol.Latitude(latitudeDeg));
                _cmd.SendAck(OnStepXProtocol.LongitudeFromEastPositive(longitudeDeg));
                _cmd.SendAck(OnStepXProtocol.Elevation(elevationM));
            }, ct);

        public Task SetTrackingAsync(bool enabled, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendAck(OnStepXProtocol.Tracking(enabled)), ct);

        public Task SetTrackingRateAsync(TrackingRate rate, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(OnStepXProtocol.TrackingRate(rate)), ct);

        public Task SetCompensatedTrackingAsync(CompensatedTracking mode, bool dualAxis, CancellationToken ct = default) =>
            Task.Run(() => {
                _cmd.SendAck(OnStepXProtocol.CompensatedTracking(mode));
                if (mode != CompensatedTracking.Off)
                    _cmd.SendAck(OnStepXProtocol.CompensatedTrackingAxis(
                        dualAxis ? CompensatedTrackingAxis.Dual : CompensatedTrackingAxis.Single));
            }, ct);

        public Task AdjustTrackingFrequencyAsync(int direction, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(OnStepXProtocol.TrackingFrequencyAdjust(direction)), ct);

        public Task ResetTrackingFrequencyAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(OnStepXProtocol.TrackingFrequencyReset()), ct);

        public Task SetHomePositionAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(OnStepXProtocol.SetHomePosition()), ct);

        public Task SetParkPositionAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendAck(OnStepXProtocol.SetParkPosition()), ct);

        public Task SetGuideRateAsync(int rateIndex, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(OnStepXProtocol.GuideRatePreset(rateIndex)), ct);

        public Task SetSlewSpeedAsync(SlewSpeed speed, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(OnStepXProtocol.SlewSpeedPreset(speed)), ct);

        public Task SetGotoBuzzerAsync(bool enabled, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendAck(OnStepXProtocol.GotoBuzzer(enabled)), ct);

        public Task SetAutoMeridianFlipAsync(bool enabled, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendAck(OnStepXProtocol.AutoMeridianFlip(enabled)), ct);

        public Task TriggerMeridianFlipAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendBlind(OnStepXProtocol.MeridianFlipNow()), ct);

        public Task SetPauseAtHomeAsync(bool enabled, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendAck(OnStepXProtocol.PauseAtHome(enabled)), ct);

        public Task SetPreferredPierSideAsync(PreferredPierSide side, CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendAck(OnStepXProtocol.PreferredPierSide(side)), ct);

        public Task SetBacklashAsync(int axis1, int axis2, CancellationToken ct = default) =>
            Task.Run(() => {
                _cmd.SendAck(OnStepXProtocol.BacklashRa(axis1));
                _cmd.SendAck(OnStepXProtocol.BacklashDec(axis2));
            }, ct);

        public Task SetLimitsAsync(double minAlt, double maxAlt, double east, double west, CancellationToken ct = default) =>
            Task.Run(() => {
                _cmd.SendAck(OnStepXProtocol.HorizonLimit(minAlt));
                _cmd.SendAck(OnStepXProtocol.OverheadLimit(maxAlt));
                _cmd.SendAck(OnStepXProtocol.MeridianLimitEast(east));
                _cmd.SendAck(OnStepXProtocol.MeridianLimitWest(west));
            }, ct);

        // ── Alignment ────────────────────────────────────────────────────────────

        public Task ClearAlignmentModelAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendAck(OnStepXProtocol.AlignmentReset()), ct);

        public Task UploadAlignmentStarAsync(
            double actualHAHours, double actualDecDeg,
            double mountHAHours,  double mountDecDeg,
            int pierSide, CancellationToken ct = default) {
            return Task.Run(() => {
                _cmd.SendAck(OnStepXProtocol.StarActualHa(actualHAHours));
                _cmd.SendAck(OnStepXProtocol.StarActualDec(actualDecDeg));
                _cmd.SendAck(OnStepXProtocol.StarMountHa(mountHAHours));
                _cmd.SendAck(OnStepXProtocol.StarMountDec(mountDecDeg));
                _cmd.SendAck(OnStepXProtocol.StarCommit(pierSide));
            }, ct);
        }

        public Task ComputeAlignmentOnControllerAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendAck(OnStepXProtocol.AlignmentCompute()), ct);

        public Task SaveAlignmentToEepromAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendAck(OnStepXProtocol.AlignmentWriteNv()), ct);

        public Task ForceModelActivationAsync(CancellationToken ct = default) =>
            Task.Run(() => _cmd.SendAck(OnStepXProtocol.AlignmentActivate()), ct);

        public Task WriteCoefficientsAsync(AlignmentModelCoefficients c, CancellationToken ct = default) {
            return Task.Run( async () => {
                var expected = RoundCoefficients(c);
                var mountType = await GetMountTypeAsync(ct);
                _cmd.SendAck(OnStepXProtocol.SetCoefficient(0x00, expected.Ax1Cor));
                _cmd.SendAck(OnStepXProtocol.SetCoefficient(0x01, expected.Ax2Cor));
                _cmd.SendAck(OnStepXProtocol.SetCoefficient(0x02, expected.AltCor));
                _cmd.SendAck(OnStepXProtocol.SetCoefficient(0x03, expected.AzmCor));
                _cmd.SendAck(OnStepXProtocol.SetCoefficient(0x04, expected.DoCor));
                _cmd.SendAck(OnStepXProtocol.SetCoefficient(0x05, expected.PdCor));
                _cmd.SendAck(OnStepXProtocol.SetCoefficient(OnStepXProtocol.IsForkOrAltAz(mountType) ? 0x06 : 0x07, expected.DfCor));
                _cmd.SendAck(OnStepXProtocol.SetCoefficient(0x08, expected.TfCor));
                _cmd.SendAck(OnStepXProtocol.SetCoefficient(0x0a, expected.Hcp));
                _cmd.SendAck(OnStepXProtocol.SetCoefficient(0x0b, expected.Hca));
                _cmd.SendAck(OnStepXProtocol.SetCoefficient(0x0c, expected.Dcp));
                _cmd.SendAck(OnStepXProtocol.SetCoefficient(0x0d, expected.Dca));
                _cmd.SendAck(OnStepXProtocol.AlignmentActivate());

                // reread from the mount to verify that the coefficients were properly written
                var actual = GetCoefficientsAsync(ct).GetAwaiter().GetResult();
                if (actual == null || !CoefficientsMatch(expected, RoundCoefficients(actual))) {
                    throw new InvalidOperationException("OnStepX coefficient write verification failed.");
                }
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

        private int ReadCoefficient(int hexRegister) =>
            ParseInt(_cmd.SendString(OnStepXProtocol.GetCoefficient(hexRegister))) ?? 0;

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
