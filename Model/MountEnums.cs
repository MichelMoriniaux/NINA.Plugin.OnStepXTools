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
