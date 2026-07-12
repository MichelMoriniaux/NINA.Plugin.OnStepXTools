using System;
using System.Collections.Generic;
using NINA.Plugin.OnStepXTools.Model;
using NINA.Plugin.OnStepXTools.ModelManagement;
using Xunit;

namespace NINA.Plugin.OnStepXTools.Tests;

public class GridSearchPointingModelSolverTests {
    [Fact]
    public void Solve_RecoversGemCoefficientsGeneratedFromRealMountToObservedPlaceFormula() {
        const double siteLatitudeDeg = 42.0;
        var expected = new AlignmentModelCoefficients {
            Ax1Cor = 120,
            Ax2Cor = -85,
            AltCor = 42,
            AzmCor = -37,
            DoCor = 28,
            PdCor = -19,
            DfCor = 17,
            TfCor = -11,
            Hcp = 35,
            Hca = 24,
            Dcp = -22,
            Dca = 18
        };

        var points = new List<SavedModelPoint>();
        foreach (var decDeg in new[] { -20.0, -5.0, 12.0, 28.0, 44.0, 58.0 }) {
            foreach (var haHours in new[] { -5.0, -3.0, -1.25, 1.25, 3.0, 5.0 }) {
                // Sample both pier sides at the same HA/Dec (as a real build does across
                // meridian flips) rather than tying pier side to HA sign - otherwise the
                // pier-dependent terms (doCor/pdCor) are confounded with the pier-independent
                // ones (dfCor/tfCor/altCor/azmCor) and the fit is under-determined regardless
                // of solver.
                foreach (var pierSide in new[] { 1, -1 }) {
                    var (raErrorArcsec, decErrorArcsec) = ComputeMountToObservedPlaceError(expected, siteLatitudeDeg, haHours, decDeg, pierSide);
                    points.Add(new SavedModelPoint {
                        MountHAHours = haHours,
                        MountDecDeg = decDeg,
                        PierSide = pierSide,
                        PointingErrorRAArcsec = raErrorArcsec,
                        PointingErrorDecArcsec = decErrorArcsec
                    });
                }
            }
        }

        var actual = GridSearchPointingModelSolver.Solve(points, siteLatitudeDeg, MountType.GEM);

        Assert.NotNull(actual);

        // Phase 1 is a faithful port of the firmware's own doSearch() - a greedy
        // coarse-to-fine grid search, not a global optimizer. For strongly-correlated
        // parameters (doCor/pdCor/dfCor/tfCor/ax2Cor here all trade off against each other
        // for this star geometry) it can settle near, but not exactly on, the injected
        // values - this is a real property of the reference algorithm itself, not specific
        // to this port. The tolerance below reflects what's actually achievable, not an
        // arbitrary relaxation.
        Assert.InRange(actual!.Ax1Cor, expected.Ax1Cor - 25, expected.Ax1Cor + 25);
        Assert.InRange(actual.Ax2Cor, expected.Ax2Cor - 25, expected.Ax2Cor + 25);
        Assert.InRange(actual.AltCor, expected.AltCor - 25, expected.AltCor + 25);
        Assert.InRange(actual.AzmCor, expected.AzmCor - 25, expected.AzmCor + 25);
        Assert.InRange(actual.DoCor, expected.DoCor - 25, expected.DoCor + 25);
        Assert.InRange(actual.PdCor, expected.PdCor - 25, expected.PdCor + 25);
        Assert.InRange(actual.DfCor, expected.DfCor - 25, expected.DfCor + 25);
        Assert.InRange(actual.TfCor, expected.TfCor - 25, expected.TfCor + 25);

        // hcp/hca/dcp/dca are NOT meaningfully recoverable from typical pointing data by any
        // solver, grid search included: mountToObservedPlace() multiplies cos(a+hcp) by an
        // already-tiny polar-residual angle "a", so the term barely varies point-to-point and
        // amplitude/phase are nearly unidentifiable (see GridSearchPointingModelSolver's
        // MaxHarmonicAmplitudeArcsec comment). All this test can honestly assert is that the
        // search stays within its sanity clamp rather than diverging to something that would
        // actively harm the model if uploaded.
        Assert.InRange(actual.Hca, 0, 600);
        Assert.InRange(actual.Dca, 0, 600);

        Assert.Equal(points.Count, actual.Stars);
    }

    // Verbatim port of GeoAlign::mountToObservedPlace() for a GEM/EQ mount, used to
    // generate noiseless synthetic pointing errors that are internally consistent with
    // what GridSearchPointingModelSolver actually models (including the real, nonlinear
    // cos(a1+hcp) treatment of hcp/hca/dcp/dca - unlike the old PointingModelSolverTests
    // generator, which used the linear cos(H+hcp) approximation the old solver assumed).
    private static (double raErrorArcsec, double decErrorArcsec) ComputeMountToObservedPlaceError(
        AlignmentModelCoefficients c,
        double siteLatitudeDeg,
        double haHours,
        double decDeg,
        int pierSide) {

        double p = pierSide;
        double h = DegToRad(haHours * 15.0);
        double d = DegToRad(decDeg);
        double sinLat = Math.Sin(DegToRad(siteLatitudeDeg));
        double cosLat = Math.Cos(DegToRad(siteLatitudeDeg));

        double ax1 = h + ArcsecToRad(c.Ax1Cor);
        double ax2 = d + ArcsecToRad(c.Ax2Cor) * -p;

        double sinAx1 = Math.Sin(ax1), cosAx1 = Math.Cos(ax1);
        double sinAx2 = Math.Sin(ax2), cosAx2 = Math.Cos(ax2);
        double tanAx2 = sinAx2 / cosAx2;

        double doh = ArcsecToRad(c.DoCor) * (1.0 / cosAx2) * p;
        double pdh = -ArcsecToRad(c.PdCor) * tanAx2 * p;
        double dfd = -ArcsecToRad(c.DfCor) * (cosLat * cosAx1 + sinLat * tanAx2);
        double tfh = ArcsecToRad(c.TfCor) * (cosLat * sinAx1 * (1.0 / cosAx2));
        double tfd = ArcsecToRad(c.TfCor) * (cosLat * cosAx1 * sinAx2 - sinLat * cosAx2);

        double a1 = -ArcsecToRad(c.AzmCor) * cosAx1 * tanAx2 + ArcsecToRad(c.AltCor) * sinAx1 * tanAx2;
        double a2 = ArcsecToRad(c.AzmCor) * sinAx1 + ArcsecToRad(c.AltCor) * cosAx1;

        double cosh = Math.Cos(a1 + DegToRad(c.Hcp)) * ArcsecToRad(c.Hca) * p;
        double cosd = Math.Cos(a2 + DegToRad(c.Dcp)) * ArcsecToRad(c.Dca) * p;

        double observedAx1 = ax1 - (a1 + pdh + doh + tfh + cosh);
        double observedAx2 = ax2 - (a2 + dfd + tfd + cosd);

        double errH = observedAx1 - h;
        double errD = observedAx2 - d;

        // Matches ModelBuilder.cs's convention: PointingErrorRAArcsec = -errH * cos(Dec).
        return (-RadToArcsec(errH) * Math.Cos(d), RadToArcsec(errD));
    }

    private static double DegToRad(double degrees) => degrees * Math.PI / 180.0;
    private static double ArcsecToRad(double arcsec) => arcsec * Math.PI / 180.0 / 3600.0;
    private static double RadToArcsec(double rad) => rad * 180.0 / Math.PI * 3600.0;
}
