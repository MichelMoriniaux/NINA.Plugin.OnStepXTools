using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using NINA.Plugin.OnStepXTools.Interfaces;

namespace NINA.Plugin.OnStepXTools.SequenceItems {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "Save OnStepX Alignment to EEPROM")]
    [ExportMetadata("Description", "Persists the current alignment model to non-volatile memory (sends :AW#)")]
    [ExportMetadata("Icon", "SaveSVG")]
    [ExportMetadata("Category", "OnStepX Tools")]
    public class WriteAlignmentToNv : SequenceItem {
        private readonly IOnStepXMount _mount;

        [ImportingConstructor]
        public WriteAlignmentToNv(IOnStepXMount mount) {
            _mount = mount;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            progress?.Report(new ApplicationStatus { Status = "Saving alignment model to EEPROM…" });
            await _mount.SaveAlignmentToEepromAsync(token);
            progress?.Report(new ApplicationStatus { Status = "Alignment model saved." });
        }

        public override object Clone() {
            var clone = new WriteAlignmentToNv(_mount);
            clone.CopyMetaData(this);
            return clone;
        }

        public override string ToString() => "Save OnStepX Alignment to EEPROM";
    }
}
