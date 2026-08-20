using System;
using System.Collections.Generic;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.ModelManagement {

    // 12-parameter least-squares pointing model matching OnStepX Align.hs.cpp.
    // All design-matrix entries and sign conventions are derived directly from
    // the correct() / mountToObservedPlace() functions in Align.hs.cpp so that
    // the solved coefficients can be uploaded to OnStep unchanged.
    //
    // Unknowns (12):
    //   [ax1Cor, ax2Cor, altCor, azmCor, doCor, pdCor, dfCor, tfCor,
    //    hcA, hcB, dcA, dcB]
    //
    // Observation vector:
    //   dH  = HA error in arcseconds  = -errRA / cos(Dec)
    //         (errRA already has cosD factor, divide it out to get HA arcsec)
    //   dD  = Dec error in arcseconds = errDec  (unchanged)
    public static class PointingModelSolver {

        // siteLatitudeDeg is required for the latitude-aware dfCor and tfCor formulas.
        public static AlignmentModelCoefficients? Solve(
            IReadOnlyList<SavedModelPoint> points,
            double siteLatitudeDeg = 45.0) {

            int n = points.Count;
            if (n < 6) return null;

            const int NParams = 12;
            int rows = 2 * n;

            double cosLat = Math.Cos(siteLatitudeDeg * Math.PI / 180.0);
            double sinLat = Math.Sin(siteLatitudeDeg * Math.PI / 180.0);

            var A = new double[rows, NParams];
            var b = new double[rows];

            for (int i = 0; i < n; i++) {
                var pt   = points[i];
                double H = pt.MountHAHours * 15.0 * Math.PI / 180.0;
                double D = pt.MountDecDeg        * Math.PI / 180.0;

                double sinH = Math.Sin(H), cosH = Math.Cos(H);
                double sinD = Math.Sin(D), cosD = Math.Cos(D);
                double tanD = Math.Tan(D);
                // Clamp sec(D) to avoid singularity within ~1° of the pole
                double secD = 1.0 / Math.Max(cosD, 1e-4);

                // p = pier-side multiplier, matching OnStep convention:
                //   p = +1  pierEast  (OTA East, target West of meridian, HA > 0)
                //   p = -1  pierWest  (OTA West, target East of meridian, HA < 0)
                double p = pt.PierSide;

                // ── ΔH row ───────────────────────────────────────────────────────────
                // From Align.hs.cpp correct() / mountToObservedPlace():
                //   mount_H = observed_H + ax1c + DOh + PDh + TFh + COSh - ax1Cor
                //   errH = observed_H - mount_H = ax1Cor - ax1c - DOh - PDh - TFh - COSh
                //   ax1c = −azmCor·cosH·tanD + altCor·sinH·tanD
                //   DOh  =  doCor · secD · p
                //   PDh  = −pdCor · tanD · p
                //   TFh  =  tfCor · cosLat · sinH · secD
                //   COSh =  hca · cos(H + hcp) · p  → linearised: p·(hcA·cosH + hcB·sinH)
                //
                // Observation: dH = errH = −errRA / cos(Dec)
                //   (errRA already carries a cos(Dec) factor; divide it out to get HA arcsec)
                double dH = -pt.PointingErrorRAArcsec / Math.Max(cosD, 0.01);

                A[2*i,  0] =  1.0;                    // ax1Cor
                A[2*i,  1] =  0.0;                    // ax2Cor  (no HA effect)
                A[2*i,  2] = -sinH * tanD;            // altCor  (-PA·sinH·tanD)
                A[2*i,  3] =  cosH * tanD;            // azmCor  (+PZ·cosH·tanD)
                A[2*i,  4] = -p * secD;               // doCor   (-DO·secD·p)
                A[2*i,  5] =  p * tanD;               // pdCor   (+PD·tanD·p)
                A[2*i,  6] =  0.0;                    // dfCor   (no HA effect)
                A[2*i,  7] = -cosLat * sinH * secD;   // tfCor   (-TF·cosLat·sinH·secD)
                A[2*i,  8] = -p * cosH;               // hcA     (negative pier-signed cosine)
                A[2*i,  9] = -p * sinH;               // hcB     (negative pier-signed sine)
                A[2*i, 10] =  0.0;                    // dcA
                A[2*i, 11] =  0.0;                    // dcB
                b[2*i]     = dH;

                // ── ΔD row ───────────────────────────────────────────────────────────
                // From Align.hs.cpp correct() / mountToObservedPlace():
                //   mount_D = observed_D + ax2c + DFd + TFd + COSd + ax2Cor*p
                //   errD = observed_D - mount_D = -ax2Cor*p - ax2c - DFd - TFd - COSd
                //   ax2c = +azmCor·sinH + altCor·cosH
                //   DFd  = −dfCor·(cosLat·cosH + sinLat·tanD)  [GEM mount]
                //   TFd  =  tfCor·(cosLat·cosH·sinD − sinLat·cosD)
                //   COSd =  dca·cos(D + dcp)·p  → linearised: p·(dcA·cosD + dcB·sinD)
                //
                // Observation: dD = errDec (already in Dec arcseconds)
                double dD = pt.PointingErrorDecArcsec;

                A[2*i+1,  0] =  0.0;                                       // ax1Cor
                A[2*i+1,  1] = -p;                                         // ax2Cor  (-ax2Cor*p)
                A[2*i+1,  2] = -cosH;                                      // altCor  (-PA*cosH)
                A[2*i+1,  3] = -sinH;                                      // azmCor  (-PZ*sinH)
                A[2*i+1,  4] =  0.0;                                       // doCor   (no Dec effect)
                A[2*i+1,  5] =  0.0;                                       // pdCor   (no Dec effect)
                A[2*i+1,  6] =  cosLat * cosH + sinLat * tanD;            // dfCor   (GEM: +DF*(cosLat*cosH+sinLat*tanD))
                A[2*i+1,  7] = -(cosLat * cosH * sinD - sinLat * cosD);   // tfCor   (-TF*(cosLat*cosH*sinD-sinLat*cosD))
                A[2*i+1,  8] =  0.0;                                       // hcA
                A[2*i+1,  9] =  0.0;                                       // hcB
                A[2*i+1, 10] = -p * cosD;                                  // dcA     (negative pier-signed cosine)
                A[2*i+1, 11] = -p * sinD;                                  // dcB     (negative pier-signed sine)
                b[2*i+1]     = dD;
            }

            // Normal equations: (AᵀA + λI)x = Aᵀb  - Tikhonov regularisation
            var AtA = new double[NParams, NParams];
            var Atb = new double[NParams];
            const double lambda = 1e-6;

            for (int r = 0; r < rows; r++) {
                for (int j = 0; j < NParams; j++) {
                    Atb[j] += A[r, j] * b[r];
                    for (int k = 0; k < NParams; k++)
                        AtA[j, k] += A[r, j] * A[r, k];
                }
            }
            for (int j = 0; j < NParams; j++)
                AtA[j, j] += lambda;

            var x = GaussianElimination(AtA, Atb);
            if (x == null) return null;

            // Recover harmonic amplitudes and phases from linearised components
            double hcA = x[8],  hcB = x[9];
            double dcA = x[10], dcB = x[11];
            double hca = Math.Sqrt(hcA * hcA + hcB * hcB);
            double hcp = Math.Atan2(-hcB, hcA) * 180.0 / Math.PI;
            double dca = Math.Sqrt(dcA * dcA + dcB * dcB);
            double dcp = Math.Atan2(-dcB, dcA) * 180.0 / Math.PI;

            return new AlignmentModelCoefficients {
                Ax1Cor = Convert.ToInt32(x[0]),
                Ax2Cor = Convert.ToInt32(x[1]),
                AltCor = Convert.ToInt32(x[2]),
                AzmCor = Convert.ToInt32(x[3]),
                DoCor  = Convert.ToInt32(x[4]),
                PdCor  = Convert.ToInt32(x[5]),   // now solved, not hardcoded to 0
                DfCor  = Convert.ToInt32(x[6]),
                TfCor  = Convert.ToInt32(x[7]),
                Hcp    = Convert.ToInt32(hcp),
                Hca    = Convert.ToInt32(hca),
                Dcp    = Convert.ToInt32(dcp),
                Dca    = Convert.ToInt32(dca),
                Stars  = n
            };
        }

        // Evaluates what pointing residual would remain at this point if the given coefficients
        // were applied - the non-linearised form (using hca/hcp directly, matching README's
        // "Pointing Model Mathematics") of the same errH/errD formula the design matrix above
        // encodes, so this is the exact inverse of what Solve() just fit: given x (coefficients),
        // predict b, and return the leftover (observed - predicted) in the same on-sky
        // ΔRA/ΔDec arcsecond convention as ResidualPoint / PointingErrorRAArcsec.
        public static ResidualPoint EvaluateResidual(
            SavedModelPoint pt, AlignmentModelCoefficients c, double siteLatitudeDeg = 45.0) {

            double H = pt.MountHAHours * 15.0 * Math.PI / 180.0;
            double D = pt.MountDecDeg        * Math.PI / 180.0;

            double cosLat = Math.Cos(siteLatitudeDeg * Math.PI / 180.0);
            double sinLat = Math.Sin(siteLatitudeDeg * Math.PI / 180.0);
            double sinH = Math.Sin(H), cosH = Math.Cos(H);
            double sinD = Math.Sin(D), cosD = Math.Cos(D);
            double tanD = Math.Tan(D);
            double secD = 1.0 / Math.Max(cosD, 1e-4);
            double p = pt.PierSide;

            double dH = -pt.PointingErrorRAArcsec / Math.Max(cosD, 0.01);
            double dD = pt.PointingErrorDecArcsec;

            double hcpRad = c.Hcp * Math.PI / 180.0;
            double dcpRad = c.Dcp * Math.PI / 180.0;

            double dHPredicted = c.Ax1Cor - c.AltCor * sinH * tanD + c.AzmCor * cosH * tanD
                                - c.DoCor * p * secD + c.PdCor * p * tanD
                                - c.TfCor * cosLat * sinH * secD
                                - c.Hca * Math.Cos(H + hcpRad) * p;

            double dDPredicted = -c.Ax2Cor * p - c.AltCor * cosH - c.AzmCor * sinH
                                + c.DfCor * (cosLat * cosH + sinLat * tanD)
                                - c.TfCor * (cosLat * cosH * sinD - sinLat * cosD)
                                - c.Dca * Math.Cos(D + dcpRad) * p;

            double residualDH = dH - dHPredicted;
            double residualDD = dD - dDPredicted;

            return new ResidualPoint(
                pt.AltitudeDeg, pt.AzimuthDeg,
                -residualDH * cosD, residualDD);
        }

        // Gaussian elimination with partial pivoting.
        private static double[]? GaussianElimination(double[,] A, double[] b) {
            int n = b.Length;
            var M = new double[n, n + 1];
            for (int i = 0; i < n; i++) {
                for (int j = 0; j < n; j++) M[i, j] = A[i, j];
                M[i, n] = b[i];
            }

            for (int col = 0; col < n; col++) {
                int pivotRow = col;
                double maxVal = Math.Abs(M[col, col]);
                for (int row = col + 1; row < n; row++) {
                    if (Math.Abs(M[row, col]) > maxVal) {
                        maxVal = Math.Abs(M[row, col]);
                        pivotRow = row;
                    }
                }
                if (pivotRow != col)
                    for (int j = 0; j <= n; j++)
                        (M[col, j], M[pivotRow, j]) = (M[pivotRow, j], M[col, j]);

                double pivot = M[col, col];
                if (Math.Abs(pivot) < 1e-15) return null;

                for (int row = col + 1; row < n; row++) {
                    double factor = M[row, col] / pivot;
                    for (int j = col; j <= n; j++)
                        M[row, j] -= factor * M[col, j];
                }
            }

            var x = new double[n];
            for (int i = n - 1; i >= 0; i--) {
                x[i] = M[i, n];
                for (int j = i + 1; j < n; j++)
                    x[i] -= M[i, j] * x[j];
                x[i] /= M[i, i];
            }
            return x;
        }
    }
}
