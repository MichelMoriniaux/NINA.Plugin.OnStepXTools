using System;
using System.ComponentModel.Composition;
using NINA.Plugin.OnStepXTools.Interfaces;

namespace NINA.Plugin.OnStepXTools.Equipment {

    [Export(typeof(IModelBuilderMediator))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class ModelBuilderMediator : IModelBuilderMediator {
        private bool _isBuilding;

        public bool IsBuilding => _isBuilding;

        public event EventHandler<PointsLoadedEventArgs>?  PointsLoaded;
        public event EventHandler<BuildStartedEventArgs>?  BuildStarted;
        public event EventHandler<BuildProgressEventArgs>? ProgressChanged;
        public event EventHandler<BuildCompletedEventArgs>? BuildCompleted;

        public void NotifyPointsLoaded(PointsLoadedEventArgs args) {
            PointsLoaded?.Invoke(this, args);
        }

        public void NotifyStarted(BuildStartedEventArgs args) {
            _isBuilding = true;
            BuildStarted?.Invoke(this, args);
        }

        public void NotifyProgress(BuildProgressEventArgs args) {
            _isBuilding = true;
            ProgressChanged?.Invoke(this, args);
        }

        public void NotifyCompleted(BuildCompletedEventArgs args) {
            _isBuilding = false;
            BuildCompleted?.Invoke(this, args);
        }
    }
}
