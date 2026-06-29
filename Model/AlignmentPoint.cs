using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NINA.Plugin.OnStepXTools.Model {

    public class AlignmentPoint : INotifyPropertyChanged {
        private AlignmentPointState _state = AlignmentPointState.Pending;

        public int Index { get; set; }
        public double AltitudeDeg { get; set; }
        public double AzimuthDeg { get; set; }

        // RA/Dec set when the slew target is resolved from Alt/Az at build time
        public double TargetRAHours { get; set; }
        public double TargetDecDeg { get; set; }

        // Filled after slew completes - mount-reported position
        public double MountRAHours { get; set; }
        public double MountDecDeg { get; set; }
        public double MountHAHours { get; set; }
        public int PierSide { get; set; }  // 1=East, -1=West

        // Filled after plate solve
        public double SolvedRAHours { get; set; }
        public double SolvedDecDeg { get; set; }

        // Computed errors (arcsec)
        public double PointingErrorRAArcsec { get; set; }
        public double PointingErrorDecArcsec { get; set; }

        public AlignmentPointState State {
            get => _state;
            set { _state = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
