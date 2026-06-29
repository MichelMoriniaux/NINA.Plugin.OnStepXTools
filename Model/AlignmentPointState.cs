namespace NINA.Plugin.OnStepXTools.Model {
    public enum AlignmentPointState {
        Pending,
        Slewing,
        Settling,
        Exposing,
        PlateSolving,
        Uploading,
        Added,
        Failed,
        FailedRMS,
        OutsideAltitudeBounds
    }
}
