namespace NINA.Plugin.OnStepXTools.Model {

    public enum MountType {
        Default = 0,
        GEM = 1,
        Fork = 2,
        EQFork = Fork,
        AltAz = 3,
        AltAlt = 4,
        GEM_TA = 5,
        GEM_TAC = 6,
        Fork_TA = 7,
        Fork_TAC = 8,
        AltAz_Unlimited = 9
    }

    public enum TrackingRate { Sidereal, Lunar, Solar, King }

    public enum CompensatedTracking { Off, RefractionOnly, Full }

    public enum CompensatedTrackingAxis { Single, Dual }

    public enum SlewSpeed { VSlow, Slow, Normal, Fast, VFast }

    public enum PreferredPierSide { Best, West, East, Auto }

    // Which algorithm PointGenerationViewModel/ModelBuilder uses to turn saved
    // pointing-error points into AlignmentModelCoefficients for the Full-Sky mode.
    public enum PointingSolverMethod {
        // 12-parameter linear least-squares fit (PointingModelSolver).
        LeastSquares,
        // Brute-force coordinate-descent grid search matching OnStepX firmware's own
        // GeoAlign::doSearch()/autoModel(), extended to also solve hcp/hca/dcp/dca
        // (GridSearchPointingModelSolver).
        GridSearch
    }

    // Which formula GridSearchPointingModelSolver assumes GeoAlign::mountToObservedPlace()
    // uses to apply hcp/hca/dcp/dca. Before OnStepX 10.28t, the firmware evaluated
    // cos(a + hcp) where "a" is the tiny polar-misalignment residual term - a bug (see
    // ONSTEPX_HARMONIC_TERM_BUG_REPORT.md), since the forward transform
    // (observedPlaceToMount()) instead behaves as if it were cos(axisAngle + hcp). Fixed in
    // 10.28t (see FirmwareVersion.IsAtLeast and ModelBuilder's use of it).
    public enum HarmonicTermConvention {
        // Detect from the connected controller's reported firmware version (:GVN#): 10.28t or
        // later -> AxisAngleFixed, otherwise PolarResidualLegacy. Falls back to
        // PolarResidualLegacy if the version can't be read/parsed - safer to assume the bug is
        // still present than to assume a fix that isn't there.
        Auto,
        // Matches pre-10.28t firmware exactly: cos(polarResidual + hcp)*hca*side.
        PolarResidualLegacy,
        // Matches the corrected formula shipped in 10.28t and later: cos(axisAngle +
        // hcp)*hca*side, periodic in the actual mount axis position.
        AxisAngleFixed
    }

    public enum ServoCalibrationCommand {
        TrackNormally,
        TrackFixedRate,
        RecordCalibration,
        StopRecording,
        ClearBuffer,
        LoadCalibration,
        SaveCalibration,
        LoadBackup,
        SaveBackup,
        HighPassFilter,
        LowPassFilter
    }
}
