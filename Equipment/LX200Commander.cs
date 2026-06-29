using System;
using System.Threading;
using System.Threading.Tasks;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;

namespace NINA.Plugin.OnStepXTools.Equipment {

    // Wraps ITelescopeMediator.SendCommand*() for raw LX200 serial commands.
    // Commands follow the LX200 protocol: ":CMD<params>#" - response strings end with "#".
    // NOTE: verify exact API against NINA.Plugin 3.2.0.9001 - method names may differ slightly.
    public class LX200Commander {
        private readonly ITelescopeMediator _telescope;

        public LX200Commander(ITelescopeMediator telescope) {
            _telescope = telescope;
        }

        // Send command and receive string response; returns null on error.
        public string? SendString(string command) {
            try {
                var result = _telescope.SendCommandString(command, raw: true);
                return result?.TrimEnd('#');
            } catch (Exception ex) {
                Logger.Error($"LX200 SendString failed for '{command}': {ex.Message}");
                return null;
            }
        }

        // Send command with no expected response.
        public bool SendBlind(string command) {
            try {
                _telescope.SendCommandBlind(command, raw: true);
                return true;
            } catch (Exception ex) {
                Logger.Error($"LX200 SendBlind failed for '{command}': {ex.Message}");
                return false;
            }
        }

        // Send command, return "1" on success / "0" on failure.
        public bool SendBool(string command) {
            try {
                return _telescope.SendCommandBool(command, raw: true);
            } catch (Exception ex) {
                Logger.Error($"LX200 SendBool failed for '{command}': {ex.Message}");
                return false;
            }
        }

        // Parse double from command response, returning default on parse failure.
        public double? GetDouble(string command) {
            var raw = SendString(command);
            if (raw == null) return null;
            return double.TryParse(raw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        // Parse int from command response.
        public int? GetInt(string command) {
            var raw = SendString(command);
            if (raw == null) return null;
            return int.TryParse(raw, out var v) ? v : null;
        }
    }
}
