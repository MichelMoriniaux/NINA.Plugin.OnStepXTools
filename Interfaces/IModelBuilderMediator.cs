using System;
using System.Collections.Generic;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.Interfaces {

    // Fired when a sequencer item's point list is loaded (before the build starts),
    // so the Model Info panel can preview the planned visit path immediately.
    public class PointsLoadedEventArgs : EventArgs {
        public IReadOnlyList<AlignmentPoint> Points { get; init; } = Array.Empty<AlignmentPoint>();
    }

    // Fired when a build session begins - carries the full point list so the UI
    // can display all points before any are visited.
    public class BuildStartedEventArgs : EventArgs {
        public IReadOnlyList<AlignmentPoint> AllPoints { get; init; } = Array.Empty<AlignmentPoint>();
        public BuildMode Mode { get; init; }
    }

    public class BuildProgressEventArgs : EventArgs {
        public int CompletedPoints { get; init; }
        public int TotalPoints     { get; init; }
        public AlignmentPoint? CurrentPoint { get; init; }
        public string StatusMessage { get; init; } = string.Empty;
        // Full point list with up-to-date State values - lets the polar display
        // colour each point by its current status.
        public IReadOnlyList<AlignmentPoint> AllPoints { get; init; } = Array.Empty<AlignmentPoint>();
    }

    public class BuildCompletedEventArgs : EventArgs {
        public bool Success { get; init; }
        public AlignmentModelCoefficients? Coefficients { get; init; }
        public IReadOnlyList<ResidualPoint> Residuals { get; init; } = Array.Empty<ResidualPoint>();
        // Same points as Residuals, recomputed with Coefficients applied - shows the
        // improvement the model provides. Empty when no model was produced.
        public IReadOnlyList<ResidualPoint> ResidualsAfterModel { get; init; } = Array.Empty<ResidualPoint>();
        public string? ErrorMessage { get; init; }
    }

    public interface IModelBuilderMediator {
        bool IsBuilding { get; }
        event EventHandler<PointsLoadedEventArgs>  PointsLoaded;
        event EventHandler<BuildStartedEventArgs>  BuildStarted;
        event EventHandler<BuildProgressEventArgs> ProgressChanged;
        event EventHandler<BuildCompletedEventArgs> BuildCompleted;
        void NotifyPointsLoaded(PointsLoadedEventArgs args);
        void NotifyStarted(BuildStartedEventArgs args);
        void NotifyProgress(BuildProgressEventArgs args);
        void NotifyCompleted(BuildCompletedEventArgs args);
    }
}
