using System;
using System.Collections.Generic;
using NINA.Plugin.OnStepXTools.Model;
using NINA.Plugin.OnStepXTools.ModelManagement;
using Xunit;

namespace NINA.Plugin.OnStepXTools.Tests;

public class PointingModelSolverTests {
    [Fact]
    public void Solve_RecoversGemCoefficientsGeneratedFromOnStepXEquations() {
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
                var pierSide = haHours >= 0.0 ? 1 : -1;
                var (raErrorArcsec, decErrorArcsec) = ComputeGemPointingError(expected, siteLatitudeDeg, haHours, decDeg, pierSide);
                points.Add(new SavedModelPoint {
                    MountHAHours = haHours,
                    MountDecDeg = decDeg,
                    PierSide = pierSide,
                    PointingErrorRAArcsec = raErrorArcsec,
                    PointingErrorDecArcsec = decErrorArcsec
                });
            }
        }

        var actual = PointingModelSolver.Solve(points, siteLatitudeDeg);

        Assert.NotNull(actual);
        Assert.InRange(actual!.Ax1Cor, expected.Ax1Cor - 1, expected.Ax1Cor + 1);
        Assert.InRange(actual.Ax2Cor, expected.Ax2Cor - 1, expected.Ax2Cor + 1);
        Assert.InRange(actual.AltCor, expected.AltCor - 1, expected.AltCor + 1);
        Assert.InRange(actual.AzmCor, expected.AzmCor - 1, expected.AzmCor + 1);
        Assert.InRange(actual.DoCor, expected.DoCor - 1, expected.DoCor + 1);
        Assert.InRange(actual.PdCor, expected.PdCor - 1, expected.PdCor + 1);
        Assert.InRange(actual.DfCor, expected.DfCor - 1, expected.DfCor + 1);
        Assert.InRange(actual.TfCor, expected.TfCor - 1, expected.TfCor + 1);
        Assert.InRange(actual.Hca, expected.Hca - 1, expected.Hca + 1);
        Assert.InRange(actual.Dca, expected.Dca - 1, expected.Dca + 1);
        AssertPhaseNear(expected.Hcp, actual.Hcp);
        AssertPhaseNear(expected.Dcp, actual.Dcp);
        Assert.Equal(points.Count, actual.Stars);
    }

    private static (double raErrorArcsec, double decErrorArcsec) ComputeGemPointingError(
        AlignmentModelCoefficients c,
        double siteLatitudeDeg,
        double haHours,
        double decDeg,
        int pierSide) {
        var h = DegToRad(haHours * 15.0);
        var d = DegToRad(decDeg);
        var sinH = Math.Sin(h);
        var cosH = Math.Cos(h);
        var sinD = Math.Sin(d);
        var cosD = Math.Cos(d);
        var tanD = Math.Tan(d);
        var secD = 1.0 / cosD;
        var sinLat = Math.Sin(DegToRad(siteLatitudeDeg));
        var cosLat = Math.Cos(DegToRad(siteLatitudeDeg));
        var p = pierSide;

        var ax1c = -c.AzmCor * cosH * tanD + c.AltCor * sinH * tanD;
        var doh = c.DoCor * secD * p;
        var pdh = -c.PdCor * tanD * p;
        var tfh = c.TfCor * cosLat * sinH * secD;
        var cosh = c.Hca * Math.Cos(h + DegToRad(c.Hcp)) * p;
        var dH = c.Ax1Cor - ax1c - doh - pdh - tfh - cosh;

        var ax2c = c.AzmCor * sinH + c.AltCor * cosH;
        var dfd = -c.DfCor * (cosLat * cosH + sinLat * tanD);
        var tfd = c.TfCor * (cosLat * cosH * sinD - sinLat * cosD);
        var cosd = c.Dca * Math.Cos(d + DegToRad(c.Dcp)) * p;
        var dD = -c.Ax2Cor * p - ax2c - dfd - tfd - cosd;

        return (-dH * cosD, dD);
    }

    private static double DegToRad(double degrees) => degrees * Math.PI / 180.0;

    private static void AssertPhaseNear(int expectedDeg, int actualDeg) {
        var delta = Math.Abs(NormalizeDegrees(actualDeg - expectedDeg));
        Assert.InRange(delta, 0.0, 1.0);
    }

    private static double NormalizeDegrees(double degrees) {
        while (degrees > 180.0) degrees -= 360.0;
        while (degrees <= -180.0) degrees += 360.0;
        return degrees;
    }
}
