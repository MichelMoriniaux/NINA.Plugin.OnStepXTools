using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.ModelManagement {

    public class ModelPointGenerator {

        public IReadOnlyList<AlignmentPoint> Generate(
            PointGenerationOptions opts,
            HorizonAndMeridianFilter? filter = null) {

            bool isSidereal = opts.Method == GenerationMethod.SiderealPath;

            var raw = opts.Method switch {
                GenerationMethod.GoldenSpiral  => GoldenSpiral(opts),
                GenerationMethod.SiderealPath  => SiderealPath(opts, filter),
                GenerationMethod.AutoGrid      => AutoGrid(opts, filter),
                GenerationMethod.Random        => Random(opts),
                _                              => GoldenSpiral(opts)
            };

            // SiderealPath: altitude is determined by HA/Dec - skip the altitude range filter.
            // Only the custom horizon (inside IsVisible) still applies.
            var filtered = raw
                .Where(p => isSidereal ||
                            (p.AltitudeDeg >= opts.MinAltitudeDeg &&
                             p.AltitudeDeg <= opts.MaxAltitudeDeg))
                .Where(p => filter == null || filter.IsVisible(p.AltitudeDeg, p.AzimuthDeg))
                .ToList();

            if (!isSidereal) {
                if (filtered.Count > opts.PointCount) {
                    // Stride-sample so every altitude in the filtered list is represented.
                    double step    = (double)filtered.Count / opts.PointCount;
                    var    sampled = new List<AlignmentPoint>(opts.PointCount);
                    for (int i = 0; i < opts.PointCount; i++)
                        sampled.Add(filtered[(int)(i * step)]);
                    filtered = sampled;
                }
            }

            // Apply meridian-crossing-aware ordering to ALL algorithms:
            // group East-of-meridian and West-of-meridian points separately, apply
            // nearest-neighbour within each group, then concatenate (East first).
            // This guarantees at most ONE meridian crossing for the whole run.
            filtered = OrderMinimizingMeridianCrossings(filtered, opts.SiteLatitudeDeg);

            for (int i = 0; i < filtered.Count; i++)
                filtered[i].Index = i;

            return filtered;
        }

        // ── Generation algorithms ─────────────────────────────────────────────────

        // Fibonacci lattice (golden-angle spiral).
        // Points are distributed with uniform area density inside [MinAlt, MaxAlt]
        // by sampling sin(alt) uniformly - this is the sphere's natural area element.
        // Oversampled 3× to allow for custom-horizon filtering; Take() trims to PointCount.
        private static List<AlignmentPoint> GoldenSpiral(PointGenerationOptions opts) {
            const double PHI = Math.PI * (3.0 - 2.2360679774997896); // golden angle ≈ 137.5°

            double sinMin = Math.Sin(opts.MinAltitudeDeg * Math.PI / 180.0);
            double sinMax = Math.Sin(opts.MaxAltitudeDeg * Math.PI / 180.0);

            int n = opts.PointCount * 3; // oversample to survive horizon filtering
            var points = new List<AlignmentPoint>(n);

            for (int i = 0; i < n; i++) {
                // Uniform in sin(alt) → uniform area on the sphere sector
                double sinAlt = sinMin + (i + 0.5) / n * (sinMax - sinMin);
                double altDeg = Math.Asin(sinAlt) * 180.0 / Math.PI;
                double azDeg  = ((i * PHI * 180.0 / Math.PI) % 360.0 + 360.0) % 360.0;
                points.Add(new AlignmentPoint { AltitudeDeg = altDeg, AzimuthDeg = azDeg });
            }
            return points;
        }

        // Equal-area grid.
        // The number of altitude bands and per-band azimuth points are both derived from
        // PointCount so that every point occupies approximately the same solid angle.
        //
        // Formula:
        //   • Sphere sector area ∝ Σ cos(alt_i)
        //   • n_alt = round( sqrt(n × altRange / 360) )
        //   • n_az_i = round( n × cos(alt_i) / Σ cos(alt_j) )   ← equal-area weighting
        //   • Alternating rows are offset by half a step for better coverage.
        private static List<AlignmentPoint> AutoGrid(PointGenerationOptions opts, HorizonAndMeridianFilter? filter) {
            double altMin   = opts.MinAltitudeDeg;
            double altMax   = opts.MaxAltitudeDeg;
            double altRange = altMax - altMin;
            int    n        = opts.PointCount;

            // Number of altitude bands
            int n_alt = Math.Max(1, (int)Math.Round(Math.Sqrt(n * altRange / 360.0)));
            double delta_alt = altRange / n_alt;

            // Pre-compute band centre altitudes and their cosines for normalisation
            var bandAlts = new double[n_alt];
            var bandCos  = new double[n_alt];
            double cosSum = 0;
            for (int i = 0; i < n_alt; i++) {
                bandAlts[i] = altMin + (i + 0.5) * delta_alt;
                bandCos[i]  = Math.Cos(bandAlts[i] * Math.PI / 180.0);
                cosSum      += bandCos[i];
            }

            // Start each row just outside the meridian exclusion zone (Az=0°) rather than
            // at an arbitrary angle, so no point is wasted landing inside the excluded band.
            double azStart = filter?.MeridianExclusionHalfWidthDeg() ?? 0.0;

            var points = new List<AlignmentPoint>(n + n_alt);
            for (int i = 0; i < n_alt; i++) {
                // Equal-area: weight by circumference at this altitude
                int n_az = Math.Max(1, (int)Math.Round(n * bandCos[i] / cosSum));

                for (int j = 0; j < n_az; j++) {
                    // Offset alternating rows by half a step - breaks up the grid lines
                    double offset = (i % 2 == 0) ? 0.0 : 0.5;
                    double azDeg  = (360.0 / n_az * (j + offset) + azStart) % 360.0;
                    points.Add(new AlignmentPoint {
                        AltitudeDeg = bandAlts[i],
                        AzimuthDeg  = azDeg
                    });
                }
            }
            return points;
        }

        // Uniform random on the sphere sector [MinAlt, MaxAlt].
        // Samples sin(alt) uniformly (equal-area) and az uniformly.
        // Oversampled 3× to survive filtering; Take() trims to PointCount.
        private static List<AlignmentPoint> Random(PointGenerationOptions opts) {
            double sinMin = Math.Sin(opts.MinAltitudeDeg * Math.PI / 180.0);
            double sinMax = Math.Sin(opts.MaxAltitudeDeg * Math.PI / 180.0);

            var rng    = new System.Random(opts.RandomSeed);
            int n      = opts.PointCount * 3;
            var points = new List<AlignmentPoint>(n);

            for (int i = 0; i < n; i++) {
                double sinAlt = sinMin + rng.NextDouble() * (sinMax - sinMin);
                double altDeg = Math.Asin(sinAlt) * 180.0 / Math.PI;
                double azDeg  = rng.NextDouble() * 360.0;
                points.Add(new AlignmentPoint { AltitudeDeg = altDeg, AzimuthDeg = azDeg });
            }
            return points;
        }

        // SiderealPath: three Dec bands (targetDec − step, targetDec, targetDec + step),
        // each swept from StartHA to EndHA in a serpentine pattern.
        // Altitude is not filtered here - only the custom horizon and meridian exclusion apply.
        private static List<AlignmentPoint> SiderealPath(
            PointGenerationOptions opts,
            HorizonAndMeridianFilter? filter) {

            var    points  = new List<AlignmentPoint>();
            double lat     = opts.SiteLatitudeDeg;
            double target  = opts.SiderealPathTargetDeclinationDeg;
            double step    = opts.SiderealPathDecStepDeg;
            double haStart = opts.SiderealPathStartHours;
            double haEnd   = opts.SiderealPathEndHours;
            double haRange = haEnd - haStart;

            double[] decs = { target - step, target, target + step };

            int    haSteps = Math.Max(0, (int)Math.Round((double)opts.PointCount / decs.Length) - 1);
            double haInc   = haSteps > 0 ? haRange / haSteps : 0.0;

            for (int bandIdx = 0; bandIdx < decs.Length; bandIdx++) {
                double dec     = decs[bandIdx];
                bool   forward = (bandIdx % 2 == 0);
                double haFrom  = forward ? haStart : haEnd;
                double haDelta = forward ? haInc   : -haInc;

                for (int s = 0; s <= haSteps; s++) {
                    double ha = haFrom + s * haDelta;

                    if (filter != null && !filter.IsHAVisible(ha)) continue;

                    var (alt, az) = HADecToAltAz(ha, dec, lat);
                    points.Add(new AlignmentPoint { AltitudeDeg = alt, AzimuthDeg = az });
                }
            }
            return points;
        }

        // ── Shared helpers ────────────────────────────────────────────────────────

        // HA (hours, positive=West) + Dec (degrees) → Alt/Az (degrees) for given latitude.
        internal static (double altDeg, double azDeg) HADecToAltAz(
            double haHours, double decDeg, double latDeg) {

            double ha  = haHours * 15.0 * Math.PI / 180.0;
            double dec = decDeg  * Math.PI / 180.0;
            double lat = latDeg  * Math.PI / 180.0;

            double sinAlt = Math.Sin(dec) * Math.Sin(lat) +
                            Math.Cos(dec) * Math.Cos(lat) * Math.Cos(ha);
            double altRad = Math.Asin(Math.Clamp(sinAlt, -1.0, 1.0));
            double altDeg = altRad * 180.0 / Math.PI;

            double cosAlt = Math.Cos(altRad);
            if (cosAlt < 1e-9) return (altDeg, 0);

            double sinAz = -Math.Sin(ha)  * Math.Cos(dec) / cosAlt;
            double cosAz =  (Math.Sin(dec) - sinAlt * Math.Sin(lat)) / (cosAlt * Math.Cos(lat));
            double azDeg = Math.Atan2(sinAz, cosAz) * 180.0 / Math.PI;
            if (azDeg < 0) azDeg += 360.0;

            return (altDeg, azDeg);
        }

        // ── Meridian-crossing-aware ordering ─────────────────────────────────────

        // Splits points into East-of-meridian (HA < 0) and West-of-meridian (HA ≥ 0)
        // groups, applies nearest-neighbour within each group, then concatenates
        // East-first.  Result: at most ONE meridian crossing for the entire sequence.
        private static List<AlignmentPoint> OrderMinimizingMeridianCrossings(
            List<AlignmentPoint> points, double siteLatDeg) {

            if (points.Count <= 1) return points;

            double latRad = siteLatDeg * Math.PI / 180.0;
            var east = new List<AlignmentPoint>(); // HA < 0  (rising, East of meridian)
            var west = new List<AlignmentPoint>(); // HA ≥ 0  (setting, West of meridian)

            foreach (var p in points) {
                double ha = ComputeHA(p.AltitudeDeg, p.AzimuthDeg, latRad);
                if (ha < 0) east.Add(p);
                else        west.Add(p);
            }

            var result = new List<AlignmentPoint>(points.Count);
            if (east.Count > 0) result.AddRange(NearestNeighbour(east));
            if (west.Count > 0) result.AddRange(NearestNeighbour(west));
            return result;
        }

        // Hour angle from Alt/Az + site latitude.  Returns hours; negative = East, positive = West.
        // Derived from the same geometry as HADecToAltAz (inverse transform).
        private static double ComputeHA(double altDeg, double azDeg, double latRad) {
            double alt  = altDeg * Math.PI / 180.0;
            double az   = azDeg  * Math.PI / 180.0;
            // HA Y-component: -sin(Az)*cos(Alt)
            double haY  = -Math.Sin(az) * Math.Cos(alt);
            // HA X-component: cos(lat)*sin(alt) - sin(lat)*cos(alt)*cos(Az)
            double haX  = Math.Cos(latRad) * Math.Sin(alt) - Math.Sin(latRad) * Math.Cos(alt) * Math.Cos(az);
            return Math.Atan2(haY, haX) * 12.0 / Math.PI; // hours
        }

        // Nearest-neighbour greedy tour within a single hemisphere group.
        private static List<AlignmentPoint> NearestNeighbour(List<AlignmentPoint> points) {
            if (points.Count <= 2) return points;
            var ordered   = new List<AlignmentPoint>(points.Count) { points[0] };
            var remaining = new HashSet<int>(Enumerable.Range(1, points.Count - 1));

            while (remaining.Count > 0) {
                var last    = ordered[^1];
                var nearest = remaining.MinBy(i => AngularDist(last, points[i]));
                ordered.Add(points[nearest]);
                remaining.Remove(nearest);
            }
            return ordered;
        }

        private static double AngularDist(AlignmentPoint a, AlignmentPoint b) {
            double dAz  = (a.AzimuthDeg - b.AzimuthDeg + 540) % 360 - 180;
            double dAlt = a.AltitudeDeg - b.AltitudeDeg;
            return Math.Sqrt(dAlt * dAlt + dAz * dAz);
        }
    }
}
