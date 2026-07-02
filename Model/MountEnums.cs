namespace NINA.Plugin.OnStepXTools.Model {

    public enum MountType { GEM = 0, EQFork = 1, AltAz = 2 }

    public enum TrackingRate { Sidereal, Lunar, Solar, King }

    public enum CompensatedTracking { Off, RefractionOnly, Full }

    public enum CompensatedTrackingAxis { Single, Dual }

    public enum SlewSpeed { VSlow, Slow, Normal, Fast, VFast }

    public enum PreferredPierSide { Best, West, East, Auto }

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
