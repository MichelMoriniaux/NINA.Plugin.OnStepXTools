namespace NINA.Plugin.OnStepXTools.Model {

    public class ModelBuilderOptions {
        public BuildMode Mode { get; set; } = BuildMode.FullSkyPointingModel;
        public double ExposureTimeSeconds { get; set; } = 2.0;
        public double SlewSettleSeconds { get; set; } = 3.0;
        // Pre-model outlier rejection: only discard plate solves where the error
        // exceeds 3 × this value.  Setting it to 3600" (1°) means we reject solves
        // that are >3° off - i.e. truly wrong fields - while accepting all valid
        // pointing errors (even severely misaligned mounts rarely exceed 1°).
        // The previous default of 60" rejected everything >180" which filtered out
        // every point from a misaligned mount and prevented the model from being built.
        public double RmsErrorThresholdArcsec { get; set; } = 3600.0;
        public bool WriteModelToMountOnCompletion { get; set; } = true;
        public bool SaveToEepromOnCompletion { get; set; } = false;
        public string? ResumeSessionId { get; set; }
    }
}
