using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.Interfaces {

    public interface IOnStepXMount {
        // Cached OnStepX-specific mount state - single source of truth ViewModels read from
        // instead of independently querying/parsing the driver. Refreshed by ReloadSettingsAsync
        // (connect / manual reload tier) and an internal periodic loop (telemetry tier).
        OnStepXMountState State { get; }

        // Re-reads all connect-tier settings and axis parameters from the mount into State.
        // Runs automatically once on connect; also callable on demand (e.g. a "Load" button).
        Task ReloadSettingsAsync(CancellationToken ct = default);

        // Pushes State.Axis{axis}Params' EditValue entries to the mount, then re-reads that
        // axis's parameters back into State.
        Task SaveAxisAsync(int axis, CancellationToken ct = default);

        // --- Alignment model data ---
        Task<AlignmentModelCoefficients?> GetCoefficientsAsync(CancellationToken ct = default);
        Task<int> GetAlignmentStarCountAsync(CancellationToken ct = default);
        Task<AlignmentControllerStatus> GetAlignmentControllerStatusAsync(CancellationToken ct = default);
        Task<MountType> GetMountTypeAsync(CancellationToken ct = default);
        Task<bool> EnsureModelActivatedAsync(CancellationToken ct = default);
        Task<FirmwareVersion?> GetFirmwareVersionAsync(CancellationToken ct = default);

        // --- Axis motor configuration ---
        Task SetMountTypeAsync(MountType type, CancellationToken ct = default);
        Task RebootAsync(CancellationToken ct = default);
        Task ServoCalibrationAsync(ServoCalibrationCommand cmd, CancellationToken ct = default);
        Task RevertAxisAsync(int axis, CancellationToken ct = default);
        Task ClearEepromAsync(CancellationToken ct = default);
        Task SetEncoderOriginAsync(CancellationToken ct = default);
        Task SetRuntimeAxisConfigAsync(bool enabled, CancellationToken ct = default);

        // --- Mount settings ---
        Task SetLocationAsync(double longitudeDeg, double latitudeDeg, double elevationM, CancellationToken ct = default);
        Task SetTrackingAsync(bool enabled, CancellationToken ct = default);
        Task SetTrackingRateAsync(TrackingRate rate, CancellationToken ct = default);
        Task SetCompensatedTrackingAsync(CompensatedTracking mode, bool dualAxis, CancellationToken ct = default);
        Task SetCompensatedTrackingAxisAsync(CompensatedTrackingAxis axis, CancellationToken ct = default);
        Task AdjustTrackingFrequencyAsync(int direction, CancellationToken ct = default); // direction: +1 or -1
        Task ResetTrackingFrequencyAsync(CancellationToken ct = default);
        Task SetHomePositionAsync(CancellationToken ct = default);
        Task SetParkPositionAsync(CancellationToken ct = default);
        Task ContinueGotoAfterPauseAsync(CancellationToken ct = default);
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
