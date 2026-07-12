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
    // uses to apply hcp/hca/dcp/dca. As of the OnStepX source reviewed 2026-07, the firmware
    // evaluates cos(a + hcp) where "a" is the tiny polar-misalignment residual term - almost
    // certainly a bug (see the upstream report), since the forward transform
    // (observedPlaceToMount()) instead behaves as if it were cos(axisAngle + hcp). Reported
    // upstream; not yet fixed as of this writing.
    public enum HarmonicTermConvention {
        // Matches currently-shipped firmware exactly: cos(polarResidual + hcp)*hca*side.
        // Use this for any mount running firmware without the upstream fix.
        PolarResidualLegacy,
        // Matches the proposed/corrected formula: cos(axisAngle + hcp)*hca*side, periodic in
        // the actual mount axis position. Switch a mount to this once its firmware is
        // confirmed to include the fix (check the reported issue for the fixed version
        // number once one exists).
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
