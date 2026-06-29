namespace NINA.Plugin.OnStepXTools.Model {

    public class PointGenerationOptions {
        public GenerationMethod Method   { get; set; } = GenerationMethod.GoldenSpiral;
        public BuildMode        Mode     { get; set; } = BuildMode.FullSkyPointingModel;
        public int              PointCount { get; set; } = 50;
        public double           MinAltitudeDeg { get; set; } = 20.0;
        public double           MaxAltitudeDeg { get; set; } = 85.0;

        // Site location - set from IProfileService before calling Generate()
        public double SiteLatitudeDeg { get; set; } = 45.0;

        // Meridian exclusion - set from MeridianFlipSettings before calling Generate()
        public double MeridianExclusionHalfWidthDeg { get; set; } = 0.0;

        // SiderealPath options
        // HA is in hours; negative = east of meridian, positive = west.
        public double SiderealPathStartHours          { get; set; } = -3.0;
        public double SiderealPathEndHours            { get; set; } =  3.0;
        // Centre declination of the path.  Three bands are generated:
        //   targetDec − DecStepDeg,  targetDec,  targetDec + DecStepDeg
        public double SiderealPathTargetDeclinationDeg { get; set; } = 0.0;
        public double SiderealPathDecStepDeg          { get; set; } = 15.0;

        // AutoGrid options
        public double AutoGridAltStepDeg { get; set; } = 15.0;
        public double AutoGridAzStepDeg  { get; set; } = 20.0;

        // Random options
        public int RandomSeed { get; set; } = 42;
    }
}
