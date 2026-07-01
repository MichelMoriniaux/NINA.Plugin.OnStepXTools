using System;
using System.IO;
using Newtonsoft.Json;
using NINA.Core.Utility;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools {

    public class OnStepXOptions {
        private static readonly string OptionsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NINA", "Plugins", "OnStepXTools", "settings.json");

        // Build defaults
        public double DefaultExposureSeconds { get; set; } = 2.0;
        public double DefaultSlewSettleSeconds { get; set; } = 3.0;
        public double DefaultRmsThresholdArcsec { get; set; } = 3600.0;

        // Model build defaults
        public BuildMode DefaultBuildMode { get; set; } = BuildMode.FullSkyPointingModel;
        public bool DefaultWriteToMount { get; set; } = true;
        public bool DefaultSaveToEeprom { get; set; } = false;

        // Last session
        public string? LastSessionId { get; set; }

        public static OnStepXOptions Load() {
            try {
                if (File.Exists(OptionsPath)) {
                    var json = File.ReadAllText(OptionsPath);
                    return JsonConvert.DeserializeObject<OnStepXOptions>(json) ?? new OnStepXOptions();
                }
            } catch (Exception ex) {
                Logger.Error($"Failed to load OnStepX options: {ex.Message}");
            }
            return new OnStepXOptions();
        }

        public void Save() {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(OptionsPath)!);
                var tmp  = OptionsPath + ".tmp";
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(tmp, json);
                File.Move(tmp, OptionsPath, overwrite: true);
            } catch (Exception ex) {
                Logger.Error($"Failed to save OnStepX options: {ex.Message}");
            }
        }
    }
}
