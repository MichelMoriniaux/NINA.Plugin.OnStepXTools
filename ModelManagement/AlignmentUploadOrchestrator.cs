using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Plugin.OnStepXTools.Interfaces;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.ModelManagement {

    public static class AlignmentUploadOrchestrator {
        public static async Task<AlignmentModelCoefficients?> UploadAndComputeAsync(
            IOnStepXMount mount,
            IReadOnlyList<AlignmentPoint> points,
            bool saveToEeprom,
            Func<TimeSpan, CancellationToken, Task>? delayAsync,
            CancellationToken ct) {

            var uploadable = points
                .Where(IsCompleteStarRecord)
                .ToList();

            if (uploadable.Count == 0)
                throw new InvalidOperationException("No solved alignment stars are available to upload.");

            var snapshot = await mount.GetCoefficientsAsync(ct);

            try {
                var status = await mount.GetAlignmentControllerStatusAsync(ct);
                var maxStars = status.MaxStars > 0 ? status.MaxStars : 9;
                uploadable = uploadable.Take(maxStars).ToList();

                await mount.ClearAlignmentModelAsync(ct);

                foreach (var point in uploadable) {
                    ct.ThrowIfCancellationRequested();
                    point.State = AlignmentPointState.Uploading;
                    await mount.UploadAlignmentStarAsync(
                        point.ActualHAHours,
                        point.ActualDecDeg,
                        point.MountHAHours,
                        point.MountDecDeg,
                        point.PierSide,
                        ct);
                    point.State = AlignmentPointState.Added;
                    Logger.Debug("OnStepX alignment point uploaded");
                }

                await mount.ComputeAlignmentOnControllerAsync(ct);

                delayAsync ??= Task.Delay;
                await delayAsync(ComputeWait(uploadable.Count), ct);

                var stars = await mount.GetAlignmentStarCountAsync(ct);
                if (stars != uploadable.Count)
                    throw new InvalidOperationException(
                        $"OnStepX alignment verification failed: expected {uploadable.Count} stars, read back {stars}.");

                var coefficients = await GetCoefficientsWithRetryAsync(mount, delayAsync, ct);
                if (coefficients == null || !HasAnyCoefficient(coefficients))
                    throw new InvalidOperationException("OnStepX alignment verification failed: no computed coefficients were read back.");

                if (saveToEeprom)
                    await mount.SaveAlignmentToEepromAsync(ct);

                return coefficients;
            } catch {
                if (snapshot != null) {
                    try { await mount.WriteCoefficientsAsync(snapshot, CancellationToken.None); }
                    catch (Exception ex) { Logger.Error($"Failed to restore OnStepX model snapshot: {ex.Message}"); }
                }
                throw;
            }
        }

        public static TimeSpan ComputeWait(int stars) =>
            TimeSpan.FromSeconds(Math.Min(2.0 + 0.5 * Math.Max(1, stars), 15.0));

        // Retries up to 3 attempts with exponential backoff (5s, 10s), bailing out once the
        // 1-minute overall budget is exhausted so a slow/unresponsive controller can't hang the upload.
        private static async Task<AlignmentModelCoefficients?> GetCoefficientsWithRetryAsync(
            IOnStepXMount mount,
            Func<TimeSpan, CancellationToken, Task>? delayAsync,
            CancellationToken ct) {

            const int maxAttempts = 3;
            var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);
            var backoff = TimeSpan.FromSeconds(5);
            delayAsync ??= Task.Delay;

            for (var attempt = 1; attempt <= maxAttempts; attempt++) {
                var coefficients = await mount.GetCoefficientsAsync(ct);
                if (coefficients != null && HasAnyCoefficient(coefficients))
                    return coefficients;

                var timeLeft = deadline - DateTime.UtcNow;
                if (attempt == maxAttempts || timeLeft <= TimeSpan.Zero)
                    return coefficients;

                await delayAsync(backoff <= timeLeft ? backoff : timeLeft, ct);
                backoff += backoff;
            }

            return null;
        }

        private static bool HasAnyCoefficient(AlignmentModelCoefficients c) =>
            Math.Abs(c.Ax1Cor) > 0.5 ||
            Math.Abs(c.Ax2Cor) > 0.5 ||
            Math.Abs(c.AltCor) > 0.5 ||
            Math.Abs(c.AzmCor) > 0.5 ||
            Math.Abs(c.DoCor) > 0.5 ||
            Math.Abs(c.PdCor) > 0.5 ||
            Math.Abs(c.DfCor) > 0.5 ||
            Math.Abs(c.TfCor) > 0.5 ||
            Math.Abs(c.Hcp) > 0.5 ||
            Math.Abs(c.Hca) > 0.5 ||
            Math.Abs(c.Dcp) > 0.5 ||
            Math.Abs(c.Dca) > 0.5;

        private static bool IsCompleteStarRecord(AlignmentPoint point) =>
            point.State == AlignmentPointState.Added &&
            (point.PierSide == 1 || point.PierSide == -1) &&
            IsFinite(point.ActualHAHours) &&
            IsFinite(point.ActualDecDeg) &&
            IsFinite(point.MountHAHours) &&
            IsFinite(point.MountDecDeg);

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
