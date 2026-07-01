namespace NINA.Plugin.OnStepXTools.Model {

    public class ModelBuilderOptions {
        private static readonly OnStepXOptions _defaults = OnStepXOptions.Load();

        public BuildMode Mode { get; set; } = BuildMode.FullSkyPointingModel;
        public double ExposureTimeSeconds    { get; set; } = _defaults.DefaultExposureSeconds;
        public double SlewSettleSeconds      { get; set; } = _defaults.DefaultSlewSettleSeconds;
        // Pre-model outlier rejection: only discard plate solves where the error
        // exceeds 3 × this value.  Setting it to 3600" (1°) means we reject solves
        // that are >3° off - i.e. truly wrong fields - while accepting all valid
        // pointing errors (even severely misaligned mounts rarely exceed 1°).
        // The previous default of 60" rejected everything >180" which filtered out
        // every point from a misaligned mount and prevented the model from being built.
        public double RmsErrorThresholdArcsec { get; set; } = _defaults.DefaultRmsThresholdArcsec;
        public bool WriteModelToMountOnCompletion { get; set; } = true;
        public bool SaveToEepromOnCompletion { get; set; } = false;
        public string? ResumeSessionId { get; set; }
    }
}
