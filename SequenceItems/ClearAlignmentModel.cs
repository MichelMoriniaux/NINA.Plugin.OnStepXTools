using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using NINA.Plugin.OnStepXTools.Interfaces;

namespace NINA.Plugin.OnStepXTools.SequenceItems {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "Clear OnStepX Alignment Model")]
    [ExportMetadata("Description", "Clears the current alignment model from the mount controller RAM (sends :SX09,0#)")]
    [ExportMetadata("Icon", "TrashCanSVG")]
    [ExportMetadata("Category", "OnStepX Tools")]
    public class ClearAlignmentModel : SequenceItem {
        private readonly IOnStepXMount _mount;

        [ImportingConstructor]
        public ClearAlignmentModel(IOnStepXMount mount) {
            _mount = mount;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            progress?.Report(new ApplicationStatus { Status = "Clearing alignment model…" });
            await _mount.ClearAlignmentModelAsync(token);
            progress?.Report(new ApplicationStatus { Status = "Alignment model cleared." });
        }

        public override object Clone() {
            var clone = new ClearAlignmentModel(_mount);
            clone.CopyMetaData(this);
            return clone;
        }

        public override string ToString() => "Clear OnStepX Alignment Model";
    }
}
