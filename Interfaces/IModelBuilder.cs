using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Model;
using NINA.Plugin.OnStepXTools.Model;


namespace NINA.Plugin.OnStepXTools.Interfaces {

    public interface IModelBuilder {
        Task<AlignmentModelCoefficients?> BuildModelAsync(
            IReadOnlyList<AlignmentPoint> points,
            ModelBuilderOptions options,
            IProgress<ApplicationStatus>? progress,
            CancellationToken ct);
    }
}
