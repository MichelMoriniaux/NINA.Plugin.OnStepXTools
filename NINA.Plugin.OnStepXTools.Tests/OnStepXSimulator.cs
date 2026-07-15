using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NINA.Plugin.OnStepXTools.Interfaces;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.Tests {

    internal sealed class OnStepXSimulator : ILx200Transport {
        private readonly Dictionary<string, int> _model = new() {
            ["ax1"] = 0, ["ax2"] = 0, ["alt"] = 0, ["azm"] = 0,
            ["do"] = 0, ["pd"] = 0, ["df"] = 0, ["tf"] = 0,
            ["hcp"] = 0, ["hca"] = 0, ["dcp"] = 0, ["dca"] = 0
        };

        private readonly List<(string actualHa, string actualDec, string mountHa, string mountDec, int side)> _stars = new();
        private string? _actualHa;
        private string? _actualDec;
        private string? _mountHa;
        private string? _mountDec;
        private int _uploadStar;
        private int _currentStar;
        private int _lastStar;

        public MountType MountType { get; set; } = MountType.GEM;
        public int MaxStars { get; set; } = 9;
        public bool ModelIsReady { get; private set; }
        public bool FocuserHomed { get; private set; }
        public int PecWormSteps { get; private set; } = 25600;
        public int AwCount { get; private set; }
        public List<string> Commands { get; } = new();
        public List<string> Faults { get; } = new();

        public string? SendString(string command) => Send(command, expectReply: true);

        public bool SendBlind(string command) {
            var reply = Send(command, expectReply: false);
            return reply != "0";
        }

        public bool SendBool(string command) => Send(command, expectReply: true) == "1";

        public void SendAck(string command) {
            if (!SendBool(command))
                throw new InvalidOperationException($"OnStepXSimulator rejected command {command}");
        }

        private string Send(string command, bool expectReply) {
            Commands.Add(command);
            if (!command.StartsWith(":", StringComparison.Ordinal) || !command.EndsWith("#", StringComparison.Ordinal))
                return Unknown(command);

            var body = command[1..^1];

            if (body == "GXEM") return ((int)MountType).ToString(CultureInfo.InvariantCulture);
            if (body == "A?") return $"{MaxStars}{_currentStar}{_lastStar}";
            if (body == "AW") { AwCount++; return "1"; }
            if (body == "hF") return expectReply ? "" : "";
            if (body == "FH") {
                FocuserHomed = true;
                Faults.Add(":FH# homes focuser");
                return "1";
            }
            if (body == "MN" || body == "hQ") return "1";
            if (body == "Mf") return Unknown(body);
            if (body.StartsWith("SXE7,", StringComparison.Ordinal)) {
                PecWormSteps = Atol(body[5..]);
                Faults.Add(":SXE7# overwrote PEC worm steps");
                return "1";
            }

            if (body.StartsWith("GX0", StringComparison.Ordinal) && body.Length == 4)
                return ReadGx0(body[3]);

            if (body.StartsWith("SX0", StringComparison.Ordinal))
                return WriteSx0(body);

            if (body.StartsWith("SX9", StringComparison.Ordinal) ||
                body.StartsWith("SXE9,", StringComparison.Ordinal) ||
                body.StartsWith("SXEA,", StringComparison.Ordinal) ||
                body.StartsWith("SXA", StringComparison.Ordinal) ||
                body.StartsWith("SX4E,", StringComparison.Ordinal) ||
                body.StartsWith("St", StringComparison.Ordinal) ||
                body.StartsWith("Sg", StringComparison.Ordinal) ||
                body.StartsWith("Sv", StringComparison.Ordinal) ||
                body.StartsWith("R", StringComparison.Ordinal) ||
                body.StartsWith("$B", StringComparison.Ordinal) ||
                body.StartsWith("Sh", StringComparison.Ordinal) ||
                body.StartsWith("So", StringComparison.Ordinal) ||
                body is "Te" or "Td" or "TQ" or "TL" or "TS" or "TK" or "To" or "Tr" or "Tn" or "T1" or "T2" or "T+" or "T-" or "TR" or "ERESET") {
                return "1";
            }

            return Unknown(body);
        }

        private string ReadGx0(char reg) => reg switch {
            '0' => _model["ax1"].ToString(CultureInfo.InvariantCulture),
            '1' => _model["ax2"].ToString(CultureInfo.InvariantCulture),
            '2' => _model["alt"].ToString(CultureInfo.InvariantCulture),
            '3' => _model["azm"].ToString(CultureInfo.InvariantCulture),
            '4' => _model["do"].ToString(CultureInfo.InvariantCulture),
            '5' => _model["pd"].ToString(CultureInfo.InvariantCulture),
            '6' => IsForkOrAltAz ? _model["df"].ToString(CultureInfo.InvariantCulture) : "0",
            '7' => IsForkOrAltAz ? "0" : _model["df"].ToString(CultureInfo.InvariantCulture),
            '8' => _model["tf"].ToString(CultureInfo.InvariantCulture),
            '9' => _currentStar > _lastStar ? _lastStar.ToString(CultureInfo.InvariantCulture) : "0",
            'a' => _model["hcp"].ToString(CultureInfo.InvariantCulture),
            'b' => _model["hca"].ToString(CultureInfo.InvariantCulture),
            'c' => _model["dcp"].ToString(CultureInfo.InvariantCulture),
            'd' => _model["dca"].ToString(CultureInfo.InvariantCulture),
            _ => Unknown($"GX0{reg}")
        };

        private string WriteSx0(string body) {
            var reg = body[3];
            if (body.Length < 6 || body[4] != ',') return "0";
            var value = body[5..];
            switch (reg) {
                case '0': _model["ax1"] = Atol(value); return "1";
                case '1': _model["ax2"] = Atol(value); return "1";
                case '2': _model["alt"] = Atol(value); return "1";
                case '3': _model["azm"] = Atol(value); return "1";
                case '4': _model["do"] = Atol(value); return "1";
                case '5': _model["pd"] = Atol(value); return "1";
                case '6': if (IsForkOrAltAz) _model["df"] = Atol(value); return "1";
                case '7': if (!IsForkOrAltAz) _model["df"] = Atol(value); return "1";
                case '8': _model["tf"] = Atol(value); return "1";
                case 'a': _model["hcp"] = Atol(value); return "1";
                case 'b': _model["hca"] = Atol(value); return "1";
                case 'c': _model["dcp"] = Atol(value); return "1";
                case 'd': _model["dca"] = Atol(value); return "1";
                case '9': return Control(value);
                case 'A': _actualHa = value; return "1";
                case 'B': _actualDec = value; return "1";
                case 'C': _mountHa = value; return "1";
                case 'D': _mountDec = value; return "1";
                case 'E': CommitStar(Atol(value)); return "1";
                default: return Unknown(body);
            }
        }

        private string Control(string value) {
            var n = Atol(value);
            if (n == 0) {
                _uploadStar = 0;
                _stars.Clear();
                _currentStar = 0;
                _lastStar = 0;
                ModelIsReady = false;
                foreach (var key in _model.Keys.ToArray()) _model[key] = 0;
                return "1";
            }
            if (n == 1) {
                if (_uploadStar > 0) {
                    _lastStar = _uploadStar;
                    _currentStar = _uploadStar + 1;
                    ComputeModel(_uploadStar);
                }
                return "1";
            }
            if (n == 2) {
                ModelIsReady = true;
                return "1";
            }
            return "0";
        }

        private void CommitStar(int side) {
            _stars.Add((_actualHa ?? "", _actualDec ?? "", _mountHa ?? "", _mountDec ?? "", side));
            _uploadStar++;
        }

        private void ComputeModel(int stars) {
            _model["ax1"] = 100 + stars;
            _model["ax2"] = -50 - stars;
            _model["alt"] = 10 * stars;
            ModelIsReady = true;
        }

        private bool IsForkOrAltAz =>
            MountType is MountType.Fork or MountType.Fork_TA or MountType.Fork_TAC or
                         MountType.AltAz or MountType.AltAz_Unlimited;

        private static int Atol(string value) {
            var i = 0;
            var sign = 1;
            if (value.StartsWith("-", StringComparison.Ordinal)) { sign = -1; i++; }
            else if (value.StartsWith("+", StringComparison.Ordinal)) { i++; }
            var result = 0;
            for (; i < value.Length && char.IsDigit(value[i]); i++)
                result = result * 10 + value[i] - '0';
            return sign * result;
        }

        private string Unknown(string body) {
            Faults.Add($"Unknown command {body}");
            return "0";
        }
    }
}
