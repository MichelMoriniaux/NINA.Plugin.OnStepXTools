using System;
using System.Collections.Generic;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.ModelManagement {

    // Brute-force coordinate-descent grid search matching OnStepX firmware's own
    // GeoAlign::doSearch() / autoModel() / correct() (src/telescope/mount/coordinates/
    // Align.hs.cpp on the OnStepX repo), kept deliberately side-by-side with
    // PointingModelSolver (linear least-squares) rather than replacing it, so real-world
    // builds can be compared and the better-performing solver picked later - see
    // ModelBuilderOptions.SolverMethod.
    //
    // Phase 1 (Solve8CoreParameters) is a faithful, line-for-line port of the firmware's
    // own search: same parameter search order, same coarse-to-fine step schedule
    // (16384" down to 4"), same residual metric. It only ever searches
    // ax1Cor/ax2Cor/altCor/azmCor/doCor/pdCor/dfCor/tfCor - exactly what autoModel()
    // itself solves for.
    //
    // Phase 2 (SolveHarmonicTerms) is our own addition: the firmware's autoModel() never
    // solves hcp/hca/dcp/dca at all (always zeroed - see Align.hs.cpp), and
    // PointingModelSolver's old linear treatment of them didn't match how
    // mountToObservedPlace() actually applies them (cos(a1+hcp) where a1 is the *solved*
    // polar-misalignment term, not the raw axis angle - see PointingModelSolver's class
    // comment for the full writeup). Phase 2 grid-searches hcp/hca and dcp/dca against
    // that real nonlinear formula, holding the Phase 1 result fixed.
    //
    // Which nonlinear formula Phase 2 targets is controlled by HarmonicTermConvention.
    // mountToObservedPlace()'s cos(a1+hcp) is very likely an upstream firmware bug: "a1" is
    // meant to track the axis angle (that's how observedPlaceToMount()'s iterative loop uses
    // the same variable name), but mountToObservedPlace() instead computes it as only the
    // tiny polar-misalignment residual - see HarmonicTermConvention's comment and the
    // upstream bug report this was reported as. Default to PolarResidualLegacy since that's
    // what every shipped firmware actually runs today; switch a given mount over to
    // AxisAngleFixed once its firmware is confirmed to include the fix.
    public static class GridSearchPointingModelSolver {

        private const double Deg180 = Math.PI;
        private const double Deg360 = 2.0 * Math.PI;

        private struct StarPoint {
            public double MountAx1, MountAx2;   // mount-reported HA/Dec, radians
            public double ActualAx1, ActualAx2; // plate-solved true HA/Dec, radians
            public int Side;                    // +1 = east of mount (OnStep "side"), -1 = west
        }

        private sealed class Best {
            public double Dist = 3600.0 * 180.0; // sentinel, matches autoModel()'s init
            public double Deo, Pd, Pz, Pe, Tf, Ff, Df;
            public double Ode, Ohe, Odw, Ohw;
        }

        public static AlignmentModelCoefficients? Solve(
            IReadOnlyList<SavedModelPoint> points,
            double siteLatitudeDeg = 45.0,
            MountType mountType = MountType.GEM,
            HarmonicTermConvention harmonicTermConvention = HarmonicTermConvention.PolarResidualLegacy) {

            int n = points.Count;
            if (n < 6) return null;

            double cosLat = Math.Cos(siteLatitudeDeg * Math.PI / 180.0);
            double sinLat = Math.Sin(siteLatitudeDeg * Math.PI / 180.0);

            var stars = new StarPoint[n];
            for (int i = 0; i < n; i++) {
                var pt = points[i];
                double mountH = pt.MountHAHours * 15.0 * Math.PI / 180.0;
                double mountD = pt.MountDecDeg * Math.PI / 180.0;
                double cosD = Math.Cos(mountD);

                // Same errH/errD convention as PointingModelSolver: errRA already carries a
                // cos(Dec) factor, divide it out; errH = observed_H - mount_H.
                double errH = -pt.PointingErrorRAArcsec / Math.Max(cosD, 0.01) * ArcsecToRadScale;
                double errD = pt.PointingErrorDecArcsec * ArcsecToRadScale;

                stars[i] = new StarPoint {
                    MountAx1  = mountH,
                    MountAx2  = mountD,
                    ActualAx1 = mountH + errH,
                    ActualAx2 = mountD + errD,
                    Side      = pt.PierSide >= 0 ? 1 : -1
                };
            }

            var best = Solve8CoreParameters(stars, cosLat, sinLat, mountType);

            bool useFf = IsForkLike(mountType) || IsAltAzLike(mountType);

            var coeffs = new AlignmentModelCoefficients {
                Ax1Cor = (int)Math.Round(best.Ohw),
                Ax2Cor = (int)Math.Round(best.Odw),
                AltCor = (int)Math.Round(best.Pe),
                AzmCor = (int)Math.Round(best.Pz),
                DoCor  = (int)Math.Round(best.Deo),
                PdCor  = (int)Math.Round(best.Pd),
                DfCor  = (int)Math.Round(useFf ? best.Ff : best.Df),
                TfCor  = (int)Math.Round(best.Tf),
                Stars  = n
            };

            SolveHarmonicTerms(stars, coeffs, cosLat, sinLat, harmonicTermConvention);

            return coeffs;
        }

        // ── Phase 1: faithful port of GeoAlign::autoModel() + doSearch() ───────────

        private static Best Solve8CoreParameters(
            StarPoint[] stars, double cosLat, double sinLat, MountType mountType) {

            int n = stars.Length;
            var best = new Best();

            // "figure out the average Axis1 offset as a starting point"
            double ohSum = 0;
            for (int l = 0; l < n; l++) {
                double diff = stars[l].ActualAx1 - stars[l].MountAx1;
                if (diff > Deg180) diff -= Deg360;
                if (diff < -Deg180) diff += Deg360;
                ohSum += diff;
            }
            double ohe0 = ohSum / n;
            best.Ohe = Math.Round(RadToArcsec(ohe0));
            best.Ohw = best.Ohe;

            // "fork flex or dec axis flex, as appropriate"
            bool ffSearch = IsForkLike(mountType);
            bool dfSearch = !IsForkLike(mountType) && !IsAltAzLike(mountType);
            int ff = ffSearch ? 1 : 0;
            int df = dfSearch ? 1 : 0;

            // "only search for cone error if > 2 stars" - always true here (n >= 6 is enforced
            // by Solve()), kept for fidelity with the source.
            int pDo = n > 2 ? 1 : 0;

            // search, this can handle about 9 degrees of polar misalignment, and 4 degrees of
            // cone error.  Step schedule matches autoModel()'s num>4 / HAL_FAST+VFAST branch
            // (n >= 6 here always satisfies num>4, so that's the only branch we need).
            //              sf     Do  Pd Pz Pe Tf Ff  Df  Od Oh
            DoSearch(stars, cosLat, sinLat, best, 16384, 0,   0, 1, 1, 0,  0,  0, 1, 1);
            DoSearch(stars, cosLat, sinLat, best,  8192, pDo, 0, 1, 1, 0,  0,  0, 1, 1);
            DoSearch(stars, cosLat, sinLat, best,  4096, pDo, 0, 1, 1, 0,  0,  0, 1, 1);
            DoSearch(stars, cosLat, sinLat, best,  2048, pDo, 0, 1, 1, 0,  0,  0, 1, 1);
            DoSearch(stars, cosLat, sinLat, best,  1024, pDo, 0, 1, 1, 0,  0,  0, 1, 1);
            DoSearch(stars, cosLat, sinLat, best,   512, pDo, 0, 1, 1, 0,  0,  0, 1, 1);
            DoSearch(stars, cosLat, sinLat, best,   256, pDo, 1, 1, 1, 0, ff, df, 1, 1);
            DoSearch(stars, cosLat, sinLat, best,   128, pDo, 1, 1, 1, 1, ff, df, 1, 1);
            DoSearch(stars, cosLat, sinLat, best,    64, pDo, 1, 1, 1, 1, ff, df, 1, 1);
            DoSearch(stars, cosLat, sinLat, best,    32, pDo, 1, 1, 1, 1, ff, df, 1, 1);
            DoSearch(stars, cosLat, sinLat, best,    16, pDo, 1, 1, 1, 1, ff, df, 1, 1);
            DoSearch(stars, cosLat, sinLat, best,     8, pDo, 1, 1, 1, 1, ff, df, 1, 1);
            DoSearch(stars, cosLat, sinLat, best,     4, pDo, 1, 1, 1, 1, ff, df, 1, 1);

            return best;
        }

        // Faithful port of GeoAlign::doSearch(). sf is the current step size in arcseconds;
        // pXxx is how many steps (0 or 1 in every call above) either side of the current best
        // to search for that parameter - 0 means "hold fixed at the current best".
        private static void DoSearch(
            StarPoint[] stars, double cosLat, double sinLat, Best best,
            double sf, int pDo, int pPd, int pPz, int pPe, int pTf, int pFf, int pDf,
            int pOd, int pOh) {

            double sf1 = ArcsecToRad(sf);
            int n = stars.Length;

            int deoM = Steps(best.Deo, sf, -pDo), deoP = Steps(best.Deo, sf, pDo);
            int pdM  = Steps(best.Pd,  sf, -pPd), pdP  = Steps(best.Pd,  sf, pPd);
            int pzM  = Steps(best.Pz,  sf, -pPz), pzP  = Steps(best.Pz,  sf, pPz);
            int peM  = Steps(best.Pe,  sf, -pPe), peP  = Steps(best.Pe,  sf, pPe);
            int tfM  = Steps(best.Tf,  sf, -pTf), tfP  = Steps(best.Tf,  sf, pTf);
            int ffM  = Steps(best.Ff,  sf, -pFf), ffP  = Steps(best.Ff,  sf, pFf);
            int dfM  = Steps(best.Df,  sf, -pDf), dfP  = Steps(best.Df,  sf, pDf);
            int odM  = Steps(best.Ode, sf, -pOd), odP  = Steps(best.Ode, sf, pOd);
            int ohM  = Steps(best.Ohe, sf, -pOh), ohP  = Steps(best.Ohe, sf, pOh);

            var ma1 = new double[n]; var ma2 = new double[n];
            var sinA1 = new double[n]; var cosA1 = new double[n];
            var sinA2 = new double[n]; var cosA2 = new double[n];
            var tanA2 = new double[n];

            for (int ohSteps = ohM; ohSteps <= ohP; ohSteps++) {
                for (int odSteps = odM; odSteps <= odP; odSteps++) {
                    double ode = odSteps * sf1;
                    double odw = -ode;
                    double ohe = ohSteps * sf1;
                    double ohw = ohe;

                    for (int l = 0; l < n; l++) {
                        double a1 = stars[l].MountAx1 + (stars[l].Side == -1 ? ohw : ohe);
                        double a2 = stars[l].MountAx2 + (stars[l].Side == -1 ? odw : ode);
                        ma1[l] = a1; ma2[l] = a2;
                        sinA1[l] = Math.Sin(a1); cosA1[l] = Math.Cos(a1);
                        sinA2[l] = Math.Sin(a2); cosA2[l] = Math.Cos(a2);
                        tanA2[l] = sinA2[l] / cosA2[l];
                    }

                    for (int deoSteps = deoM; deoSteps <= deoP; deoSteps++)
                    for (int pdSteps = pdM; pdSteps <= pdP; pdSteps++)
                    for (int pzSteps = pzM; pzSteps <= pzP; pzSteps++)
                    for (int peSteps = peM; peSteps <= peP; peSteps++)
                    for (int dfSteps = dfM; dfSteps <= dfP; dfSteps++)
                    for (int ffSteps = ffM; ffSteps <= ffP; ffSteps++)
                    for (int tfSteps = tfM; tfSteps <= tfP; tfSteps++) {

                        double deo = deoSteps * sf1, pd = pdSteps * sf1;
                        double pz  = pzSteps  * sf1, pe = peSteps * sf1;
                        double df  = dfSteps  * sf1, ff = ffSteps * sf1, tf = tfSteps * sf1;

                        double sum1 = 0, sum2 = 0;
                        for (int l = 0; l < n; l++) {
                            Correct(cosLat, sinLat, cosA1[l], sinA1[l], cosA2[l], sinA2[l], tanA2[l],
                                stars[l].Side, deo, pd, pz, pe, df, ff, tf, out double a1r, out double a2r);

                            double d1 = stars[l].ActualAx1 - (ma1[l] - a1r);
                            if (d1 > Deg180) d1 -= Deg360; else if (d1 < -Deg180) d1 += Deg360;
                            double d2 = stars[l].ActualAx2 - (ma2[l] - a2r);

                            double t1 = d1 * Math.Cos(stars[l].ActualAx2);
                            sum1 += t1 * t1;
                            sum2 += d2 * d2;
                        }
                        double a = sum1 / (n - 1);
                        double b = sum2 / (n - 1);
                        double dist = Math.Sqrt(a + b);

                        if (dist < best.Dist) {
                            best.Dist = dist;
                            best.Deo = deoSteps * sf;
                            best.Pd  = pdSteps  * sf;
                            best.Pz  = pzSteps  * sf;
                            best.Pe  = peSteps  * sf;
                            best.Tf  = tfSteps  * sf;
                            best.Ff  = ffSteps  * sf;
                            best.Df  = dfSteps  * sf;

                            if (pOd != 0) { best.Odw = RadToArcsec(odw); best.Ode = RadToArcsec(ode); }
                            else          { best.Odw = best.Pe / 2.0;   best.Ode = -best.Pe / 2.0; }
                            if (pOh != 0) { best.Ohw = RadToArcsec(ohw); best.Ohe = RadToArcsec(ohe); }
                        }
                    }
                }
            }
        }

        // Faithful port of GeoAlign::correct(). All angles in radians; deo/pd/pz/pe/df/ff/tf
        // are already the candidate's actual angle (steps * step-size), not raw step counts.
        private static void Correct(
            double cosLat, double sinLat,
            double cosA1, double sinA1, double cosA2, double sinA2, double tanA2, int side,
            double deo, double pd, double pz, double pe, double df, double ff, double tf,
            out double a1r, out double a2r) {

            double doh = deo * (1.0 / cosA2) * side;
            double pdh = -pd * tanA2 * side;

            double dfd = -df * (cosLat * cosA1 + sinLat * tanA2);
            double ffd = ff * cosA1;

            double tfh = tf * (cosLat * sinA1 * (1.0 / cosA2));
            double tfd = tf * (cosLat * cosA1 * sinA2 - sinLat * cosA2);

            a1r = -pz * cosA1 * tanA2 + pe * sinA1 * tanA2 + doh + pdh + tfh;
            a2r =  pz * sinA1          + pe * cosA1          + dfd + ffd + tfd;
        }

        // ── Phase 2 (our own extension): hcp/hca/dcp/dca ────────────────────────────
        //
        // mountToObservedPlace() computes, using the mount reading with ax1Cor/ax2Cor
        // already applied:
        //   a1 = -azmCor*cosAx1*tanAx2 + altCor*sinAx1*tanAx2   (the same polar term Phase 1
        //                                                        already solved via Pz/Pe)
        //   COSh = hca * cos(a1 + hcp) * side
        // and symmetrically a2/COSd for Dec using cosAx1 instead. Grid-search hcp (phase,
        // degrees) and hca (amplitude, arcsec) - and separately dcp/dca - against that
        // real formula, holding everything Phase 1 solved fixed. HA and Dec are independent
        // here (COSh only affects the HA residual, COSd only the Dec residual), so they're
        // solved as two independent 2-D searches rather than one combined one.
        private static void SolveHarmonicTerms(
            StarPoint[] stars, AlignmentModelCoefficients coeffs, double cosLat, double sinLat,
            HarmonicTermConvention convention) {

            int n = stars.Length;
            var a1 = new double[n]; var a2 = new double[n];
            var resid1 = new double[n]; var resid2 = new double[n]; // residual with hc/dc = 0
            var cosActualAx2 = new double[n];
            var side = new int[n];

            double azmCor = ArcsecToRad(coeffs.AzmCor);
            double altCor = ArcsecToRad(coeffs.AltCor);
            double doCor  = ArcsecToRad(coeffs.DoCor);
            double pdCor  = ArcsecToRad(coeffs.PdCor);
            double dfCor  = ArcsecToRad(coeffs.DfCor);
            double tfCor  = ArcsecToRad(coeffs.TfCor);
            double ax1Cor = ArcsecToRad(coeffs.Ax1Cor);
            double ax2Cor = ArcsecToRad(coeffs.Ax2Cor);

            for (int l = 0; l < n; l++) {
                int p = stars[l].Side;
                side[l] = p;
                double mAx1 = stars[l].MountAx1 + ax1Cor;
                double mAx2 = stars[l].MountAx2 + ax2Cor * -p;

                double sinAx1 = Math.Sin(mAx1), cosAx1 = Math.Cos(mAx1);
                double sinAx2 = Math.Sin(mAx2), cosAx2 = Math.Cos(mAx2);
                double tanAx2 = sinAx2 / cosAx2;

                double thisA1 = -azmCor * cosAx1 * tanAx2 + altCor * sinAx1 * tanAx2;
                double thisA2 = azmCor * sinAx1 + altCor * cosAx1;

                // The polar-residual term (thisA1/thisA2) always contributes to the rest of
                // the correction regardless of convention - only the cos()'s phase argument
                // changes between the two conventions.
                a1[l] = convention == HarmonicTermConvention.AxisAngleFixed ? mAx1 : thisA1;
                a2[l] = convention == HarmonicTermConvention.AxisAngleFixed ? mAx2 : thisA2;

                double doh = doCor * (1.0 / cosAx2) * p;
                double pdh = -pdCor * tanAx2 * p;
                double dfd = -dfCor * (cosLat * cosAx1 + sinLat * tanAx2);
                double tfh = tfCor * (cosLat * sinAx1 * (1.0 / cosAx2));
                double tfd = tfCor * (cosLat * cosAx1 * sinAx2 - sinLat * cosAx2);

                // Residual assuming hca/dca = 0 - Phase 2 only ever adds the cos() term on top.
                resid1[l] = mAx1 - (thisA1 + pdh + doh + tfh);
                resid2[l] = mAx2 - (thisA2 + dfd + tfd);
                cosActualAx2[l] = Math.Cos(stars[l].ActualAx2);
            }

            var (hcp, hca) = SearchCosTerm(stars, a1, resid1, side, isAx1: true, cosActualAx2);
            var (dcp, dca) = SearchCosTerm(stars, a2, resid2, side, isAx1: false, cosActualAx2);

            coeffs.Hcp = (int)Math.Round(hcp);
            coeffs.Hca = (int)Math.Round(hca);
            coeffs.Dcp = (int)Math.Round(dcp);
            coeffs.Dca = (int)Math.Round(dca);
        }

        // hcp/hca (and dcp/dca) multiply cos(a + phase) where a is the *already-solved*
        // polar-misalignment residual - typically well under a degree, often arcseconds.
        // Since a barely varies point-to-point, cos(a + phase) is nearly constant for most
        // phases, so amplitude and phase are close to unidentifiable: a phase that makes
        // cos(a + phase) ≈ 0 lets the search drive amplitude arbitrarily high for almost no
        // residual cost. Cap the search range so a degenerate direction can't produce a
        // meaningless multi-thousand-arcsecond amplitude that would actively harm the model
        // if uploaded - this is a real limitation of these two parameters given how the
        // firmware applies them, not something a better search algorithm can fix.
        private const double MaxHarmonicAmplitudeArcsec = 600.0;

        // Coarse-to-fine 2-D grid search over (phaseDeg, ampArcsec) minimizing the same
        // sum-of-squares residual metric doSearch() uses, evaluated against the real
        // cos(a + phase)*amp*side formula.
        private static (double phaseDeg, double ampArcsec) SearchCosTerm(
            StarPoint[] stars, double[] a, double[] residNoHarmonic, int[] side, bool isAx1,
            double[] cosActualAx2) {

            double bestPhaseDeg = 0, bestAmpArcsec = 0, bestDist = double.MaxValue;

            // A ±3-step coarse-to-fine refinement starting from (0,0) can anchor on the wrong
            // neighborhood before it ever gets close to the true (phase, amplitude) and then
            // never escape it (halving only ever narrows around the current best). Do one
            // dense full sweep first to find a reasonable starting neighborhood, then refine.
            for (double phaseDeg = -180.0; phaseDeg < 180.0; phaseDeg += 15.0) {
                double phaseRad = phaseDeg * Math.PI / 180.0;
                for (double ampArcsec = -MaxHarmonicAmplitudeArcsec; ampArcsec <= MaxHarmonicAmplitudeArcsec; ampArcsec += 25.0) {
                    double dist = Residual(stars, a, residNoHarmonic, side, isAx1, cosActualAx2, phaseRad, ArcsecToRad(ampArcsec));
                    if (dist < bestDist) {
                        bestDist = dist;
                        bestPhaseDeg = phaseDeg;
                        bestAmpArcsec = ampArcsec;
                    }
                }
            }

            double phaseStep = 15.0, ampStep = 25.0;
            double phaseCenter = bestPhaseDeg, ampCenter = bestAmpArcsec;

            for (int pass = 0; pass < 8; pass++) {
                double bestPassPhase = phaseCenter, bestPassAmp = ampCenter, bestPassDist = double.MaxValue;

                for (int pi = -3; pi <= 3; pi++) {
                    double phaseDeg = phaseCenter + pi * phaseStep;
                    double phaseRad = phaseDeg * Math.PI / 180.0;

                    for (int ai = -3; ai <= 3; ai++) {
                        double ampArcsec = Math.Clamp(ampCenter + ai * ampStep,
                            -MaxHarmonicAmplitudeArcsec, MaxHarmonicAmplitudeArcsec);

                        double dist = Residual(stars, a, residNoHarmonic, side, isAx1, cosActualAx2,
                            phaseRad, ArcsecToRad(ampArcsec));

                        if (dist < bestPassDist) {
                            bestPassDist = dist;
                            bestPassPhase = phaseDeg;
                            bestPassAmp = ampArcsec;
                        }
                    }
                }

                phaseCenter = bestPassPhase;
                ampCenter = bestPassAmp;
                phaseStep /= 2.0;
                ampStep /= 2.0;

                if (bestPassDist < bestDist) {
                    bestDist = bestPassDist;
                    bestPhaseDeg = bestPassPhase;
                    bestAmpArcsec = bestPassAmp;
                }
            }

            return (NormalizeDeg(bestPhaseDeg), Math.Abs(bestAmpArcsec));
        }

        // Sum-of-squares residual (same metric doSearch() uses) for a single candidate
        // (phaseRad, amp) evaluated against the real cos(a + phase)*amp*side formula.
        private static double Residual(
            StarPoint[] stars, double[] a, double[] residNoHarmonic, int[] side, bool isAx1,
            double[] cosActualAx2, double phaseRad, double amp) {

            int n = stars.Length;
            double sum = 0;
            for (int l = 0; l < n; l++) {
                double cos = amp * Math.Cos(a[l] + phaseRad) * side[l];
                // residNoHarmonic = mount - (restOfCorrection); full delta matches doSearch()'s
                // "actual - (mount - correction)" pattern with the cos() term folded in.
                double d = isAx1
                    ? WrapPi(stars[l].ActualAx1 - (residNoHarmonic[l] - cos))
                    : stars[l].ActualAx2 - (residNoHarmonic[l] - cos);
                double weighted = isAx1 ? d * cosActualAx2[l] : d;
                sum += weighted * weighted;
            }
            return sum / Math.Max(1, n - 1);
        }

        private static double WrapPi(double x) {
            if (x > Deg180) return x - Deg360;
            if (x < -Deg180) return x + Deg360;
            return x;
        }

        private static double NormalizeDeg(double deg) {
            while (deg > 180.0) deg -= 360.0;
            while (deg <= -180.0) deg += 360.0;
            return deg;
        }

        // ── Small helpers ────────────────────────────────────────────────────────

        private static bool IsForkLike(MountType t) =>
            t is MountType.Fork or MountType.Fork_TA or MountType.Fork_TAC;

        private static bool IsAltAzLike(MountType t) =>
            t is MountType.AltAz or MountType.AltAz_Unlimited or MountType.AltAlt;

        private static int Steps(double bestArcsec, double sf, int p) =>
            p + (int)Math.Round(bestArcsec / sf);

        private const double ArcsecToRadScale = Math.PI / 180.0 / 3600.0;

        private static double ArcsecToRad(double arcsec) => arcsec * ArcsecToRadScale;

        private static double RadToArcsec(double rad) => rad * 180.0 / Math.PI * 3600.0;
    }
}
