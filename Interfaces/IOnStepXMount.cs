using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.Interfaces {

    public interface IOnStepXMount {
        // --- Alignment model data ---
        Task<AlignmentModelCoefficients?> GetCoefficientsAsync(CancellationToken ct = default);
        Task<int> GetAlignmentStarCountAsync(CancellationToken ct = default);
        Task<AlignmentControllerStatus> GetAlignmentControllerStatusAsync(CancellationToken ct = default);
        Task<MountType> GetMountTypeAsync(CancellationToken ct = default);
        Task<bool> EnsureModelActivatedAsync(CancellationToken ct = default);

        // --- Axis motor configuration (needs hardware verification for exact command format) ---
        Task<AxisConfig?> GetAxisConfigAsync(int axis, CancellationToken ct = default);
        Task SetAxisConfigAsync(int axis, AxisConfig config, CancellationToken ct = default);
        Task<PidConfig?> GetTrackingPidAsync(int axis, CancellationToken ct = default);
        Task SetTrackingPidAsync(int axis, PidConfig pid, CancellationToken ct = default);
        Task<PidConfig?> GetSlewingPidAsync(int axis, CancellationToken ct = default);
        Task SetSlewingPidAsync(int axis, PidConfig pid, CancellationToken ct = default);
        Task SetMountTypeAsync(MountType type, CancellationToken ct = default);
        Task RebootAsync(CancellationToken ct = default);
        Task ServoCalibrationAsync(ServoCalibrationCommand cmd, CancellationToken ct = default);

        // --- Mount settings ---
        Task SetLocationAsync(double longitudeDeg, double latitudeDeg, double elevationM, CancellationToken ct = default);
        Task SetTrackingAsync(bool enabled, CancellationToken ct = default);
        Task SetTrackingRateAsync(TrackingRate rate, CancellationToken ct = default);
        Task SetCompensatedTrackingAsync(CompensatedTracking mode, bool dualAxis, CancellationToken ct = default);
        Task AdjustTrackingFrequencyAsync(int direction, CancellationToken ct = default); // direction: +1 or -1
        Task ResetTrackingFrequencyAsync(CancellationToken ct = default);
        Task ResetMountAtHomeAsync(CancellationToken ct = default);
        Task SetParkPositionAsync(CancellationToken ct = default);
        Task SetGuideRateAsync(int rateIndex, CancellationToken ct = default);
        Task SetSlewSpeedAsync(SlewSpeed speed, CancellationToken ct = default);
        Task SetGotoBuzzerAsync(bool enabled, CancellationToken ct = default);
        Task SetAutoMeridianFlipAsync(bool enabled, CancellationToken ct = default);
        Task TriggerMeridianFlipAsync(CancellationToken ct = default);
        Task SetPauseAtHomeAsync(bool enabled, CancellationToken ct = default);
        Task SetPreferredPierSideAsync(PreferredPierSide side, CancellationToken ct = default);
        Task SetBacklashAsync(int axis1Arcsec, int axis2Arcsec, CancellationToken ct = default);
        Task SetLimitsAsync(double minAltDeg, double maxAltDeg, double eastPastMeridianDeg, double westPastMeridianDeg, CancellationToken ct = default);

        // --- Alignment ---
        Task ClearAlignmentModelAsync(CancellationToken ct = default);
        Task UploadAlignmentStarAsync(
            double actualHAHours, double actualDecDeg,
            double mountHAHours,  double mountDecDeg,
            int pierSide, CancellationToken ct = default);
        Task ComputeAlignmentOnControllerAsync(CancellationToken ct = default);
        Task SaveAlignmentToEepromAsync(CancellationToken ct = default);
        Task WriteCoefficientsAsync(AlignmentModelCoefficients coefficients, CancellationToken ct = default);
        Task ForceModelActivationAsync(CancellationToken ct = default);
    }
}
