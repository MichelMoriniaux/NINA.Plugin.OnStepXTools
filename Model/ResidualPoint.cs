namespace NINA.Plugin.OnStepXTools.Model {

    public readonly record struct ResidualPoint(
        double AltitudeDeg,
        double AzimuthDeg,
        double ErrorRAArcsec,
        double ErrorDecArcsec) {

        public double TotalErrorArcsec =>
            Math.Sqrt(ErrorRAArcsec * ErrorRAArcsec + ErrorDecArcsec * ErrorDecArcsec);

        public static ResidualPoint FromModelPoint(AlignmentPoint p) => new(
            p.AltitudeDeg, p.AzimuthDeg,
            p.PointingErrorRAArcsec, p.PointingErrorDecArcsec);

        public static ResidualPoint FromSavedPoint(SavedModelPoint p) => new(
            p.AltitudeDeg, p.AzimuthDeg,
            p.PointingErrorRAArcsec, p.PointingErrorDecArcsec);
    }
}
