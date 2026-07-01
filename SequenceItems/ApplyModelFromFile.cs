using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Sequencer.SequenceItem;
using NINA.Plugin.OnStepXTools.Interfaces;
using NINA.Plugin.OnStepXTools.Model;
using NINA.Plugin.OnStepXTools.ViewModels;

namespace NINA.Plugin.OnStepXTools.SequenceItems {

    [Export(typeof(ISequenceItem))]
    [ExportMetadata("Name", "OnStepX Apply Model from File")]
    [ExportMetadata("Description", "Loads pointing model coefficients from a JSON file and uploads them to the OnStepX controller")]
    [ExportMetadata("Icon", "LoadSVG")]
    [ExportMetadata("Category", "OnStepX Tools")]
    public class ApplyModelFromFile : SequenceItem {
        private readonly IOnStepXMount _mount;

        private static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NINA", "Plugins", "OnStepXTools", "LastPointingModel.json");

        private string _modelFilePath = DefaultPath;
        private bool   _saveToEeprom  = false;

        [ImportingConstructor]
        public ApplyModelFromFile(IOnStepXMount mount) {
            _mount            = mount;
            BrowseFileCommand = new ViewModels.RelayCommand(_ => BrowseFile());
        }

        public string ModelFilePath {
            get => _modelFilePath;
            set => SetField(ref _modelFilePath, value);
        }

        public bool SaveToEeprom {
            get => _saveToEeprom;
            set => SetField(ref _saveToEeprom, value);
        }

        public ICommand BrowseFileCommand { get; }

        protected bool SetField<T>(ref T field, T value,
            [System.Runtime.CompilerServices.CallerMemberName] string? name = null) {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            RaisePropertyChanged(name);
            return true;
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            if (string.IsNullOrWhiteSpace(ModelFilePath))
                throw new InvalidOperationException("No model file path specified.");

            if (!File.Exists(ModelFilePath))
                throw new FileNotFoundException($"Model file not found: {ModelFilePath}");

            AlignmentModelCoefficients? coefficients;
            try {
                var json = await File.ReadAllTextAsync(ModelFilePath, token);
                coefficients = JsonConvert.DeserializeObject<AlignmentModelCoefficients>(json);
            } catch (Exception ex) {
                throw new InvalidOperationException($"Failed to read model file: {ex.Message}", ex);
            }

            if (coefficients == null)
                throw new InvalidOperationException("Model file is empty or not a valid coefficient JSON.");

            progress?.Report(new ApplicationStatus { Status = $"Writing {coefficients.Stars} stars model to mount…" });
            await _mount.WriteCoefficientsAsync(coefficients, token);
            Logger.Info($"ApplyModelFromFile: coefficients from '{ModelFilePath}' written to mount.");

            if (SaveToEeprom) {
                progress?.Report(new ApplicationStatus { Status = "Saving model to EEPROM…" });
                await _mount.SaveAlignmentToEepromAsync(token);
                Logger.Info("ApplyModelFromFile: model saved to EEPROM.");
            }

            progress?.Report(new ApplicationStatus {
                Status = SaveToEeprom
                    ? "Model applied and saved to EEPROM."
                    : "Model applied (RAM only - not saved to EEPROM)."
            });
        }

        private void BrowseFile() {
            var dlg = new OpenFileDialog {
                Filter           = "JSON|*.json",
                Title            = "Load Pointing Model Coefficients",
                FileName         = Path.GetFileName(ModelFilePath),
                InitialDirectory = Path.GetDirectoryName(ModelFilePath) ?? string.Empty
            };
            if (dlg.ShowDialog() == true)
                ModelFilePath = dlg.FileName;
        }

        public override object Clone() {
            var clone = new ApplyModelFromFile(_mount) {
                ModelFilePath = ModelFilePath,
                SaveToEeprom  = SaveToEeprom
            };
            clone.CopyMetaData(this);
            return clone;
        }

        public override string ToString() => "OnStepX Apply Model from File";
    }
}
