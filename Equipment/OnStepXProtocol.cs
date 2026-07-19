using System;
using System.Globalization;
using NINA.Plugin.OnStepXTools.Model;
using Model = NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.Equipment {

    public static class OnStepXProtocol {
        public static string GetStatus() => ":GU#";
        public static string GetMountType() => ":GXEM#";
        public static string GetFirmwareVersion() => ":GVN#";
        public static string AlignmentStatus() => ":A?#";
        public static string AlignmentReset() => ":SX09,0#";
        public static string AlignmentCompute() => ":SX09,1#";
        public static string AlignmentActivate() => ":SX09,2#";
        public static string AlignmentWriteNv() => ":AW#";
        public static string GetStarCount() => ":GX09#";

        // this whole hex register thing seems awfully complicated for a simple index but ok
        public static string GetCoefficient(int hexRegister) =>
            $":GX0{HexDigit(hexRegister)}#";

        public static string SetCoefficient(int hexRegister, int value) =>
            $":SX0{HexDigit(hexRegister)},{value.ToString(CultureInfo.InvariantCulture)}#";

        public static string StarActualHa(double haHours) => $":SX0A,{FormatHa(haHours)}#";
        public static string StarActualDec(double decDeg) => $":SX0B,{FormatSignedDms(decDeg, 2)}#";
        public static string StarMountHa(double haHours) => $":SX0C,{FormatHa(haHours)}#";
        public static string StarMountDec(double decDeg) => $":SX0D,{FormatSignedDms(decDeg, 2)}#";
        public static string StarCommit(int pierSide) {
            if (pierSide != 1 && pierSide != -1)
                throw new ArgumentException("Pier side must be +1 east or -1 west.", nameof(pierSide));
            return $":SX0E,{pierSide.ToString(CultureInfo.InvariantCulture)}#";
        }

        public static string Tracking(bool enabled) => enabled ? ":Te#" : ":Td#";
        public static string TrackingRate(Model.TrackingRate rate) => rate switch {
            Model.TrackingRate.Lunar => ":TL#",
            Model.TrackingRate.Solar => ":TS#",
            Model.TrackingRate.King => ":TK#",
            _ => ":TQ#"
        };
        public static string CompensatedTracking(Model.CompensatedTracking mode) => mode switch {
            Model.CompensatedTracking.RefractionOnly => ":Tr#",
            Model.CompensatedTracking.Full => ":To#",
            _ => ":Tn#"
        };
        public static string CompensatedTrackingAxis(Model.CompensatedTrackingAxis axis) =>
            axis == Model.CompensatedTrackingAxis.Single ? ":T1#" : ":T2#";
        public static string TrackingFrequencyAdjust(int direction) => direction >= 0 ? ":T+#" : ":T-#";
        public static string TrackingFrequencyReset() => ":TR#";
        public static string RevertAxis(int axis) => $":SXA{axis.ToString()},R#";

        public static string GetAxisName(int axis) => $":GXA{axis.ToString()},M#";
        public static string GetAxisParamsOldFormat(int axis) => $":GXA{axis.ToString()}#";
        public static string SetAxisParamsOldFormat(int axis, string values) => $":SXA{axis.ToString()},{values}#";
        public static string GetAxisParamsCount(int axis) => $":GXA{axis.ToString()},0#";
        public static string GetAxisParameter(int axis, int index) => $":GXA{axis.ToString()},{index.ToString()}#";
        public static string SetAxisParameter(int axis, int index, string value) => $":SXA{axis.ToString()},{index.ToString()},{value}#";

        // :SXAC,0# = use runtime NV axis settings, :SXAC,1# = use compile-time Config.h axis settings
        public static string SetRuntimeAxisConfig(bool enabled) => enabled ? ":SXAC,0#" : ":SXAC,1#";

        public static string SetHomePosition() => ":hF#";
        public static string SetParkPosition() => ":hQ#";
        public static string SetEncoderOrigin() => ":SEO#";
        public static string GuideRatePreset(int rateIndex) =>
            $":R{Math.Clamp(rateIndex, 0, 9).ToString(CultureInfo.InvariantCulture)}#";
        public static string SlewSpeedPreset(Model.SlewSpeed speed) => speed switch {
            SlewSpeed.VFast => ":SX93,1#",
            SlewSpeed.Fast => ":SX93,2#",
            SlewSpeed.Normal => ":SX93,3#",
            SlewSpeed.Slow => ":SX93,4#",
            _ => ":SX93,5#"
        };
        public static string GotoBuzzer(bool enabled) => $":SX97,{BoolInt(enabled)}#";
        public static string ContinueGotoAfterPause() => ":SX99,1#";
        public static string AutoMeridianFlip(bool enabled) => $":SX95,{BoolInt(enabled)}#";
        public static string PauseAtHome(bool enabled) => $":SX98,{BoolInt(enabled)}#";
        public static string PreferredPierSide(Model.PreferredPierSide side) =>
            $":SX96,{PreferredPierSideChar(side)}#";
        public static string GetPreferredPierSide() => ":GX96#";
        public static string MeridianFlipNow() => ":MN#";
        public static string BacklashRa(int arcsec) => $":$BR{arcsec.ToString(CultureInfo.InvariantCulture)}#";
        public static string GetBacklashRa() => ":$BR#";
        public static string BacklashDec(int arcsec) => $":$BD{arcsec.ToString(CultureInfo.InvariantCulture)}#";
        public static string GetBacklashDec() => ":$BD#";
        public static string HorizonLimit(double deg) => $":Sh{RoundToInt(deg):+00;-00}#";
        public static string GetHorizonLimit() => ":Gh#";
        public static string OverheadLimit(double deg) => $":So{Math.Clamp(RoundToInt(deg), 60, 90):00}#";
        public static string GetOverheadLimit() => ":Go#";
        public static string MeridianLimitEast(double deg) => $":SXE9,{DegreesToMeridianMinutes(deg)}#";
        public static string GetMeridianLimitEast() => ":GXE9#";
        public static string MeridianLimitWest(double deg) => $":SXEA,{DegreesToMeridianMinutes(deg)}#";
        public static string GetMeridianLimitWest() => ":GXEA#";
        public static string SetMountType(MountType type) {
            if (type is not (MountType.Default or MountType.GEM or MountType.Fork or MountType.AltAz)) {
                throw new ArgumentOutOfRangeException(
                    nameof(type),
                    type,
                    "OnStepX :SXEM# only accepts Default, GEM, Fork, or AltAz runtime settings.");
            }
            return $":SXEM,{((int)type).ToString(CultureInfo.InvariantCulture)}#";
        }
        public static string Reboot() => ":ERESET#";
        public static string ClearEeprom() => ":ENVRESET#";

        public static string GetWeatherTemperature() => ":GX9A#";
        public static string GetWeatherPressure() => ":GX9B#";
        public static string GetWeatherHumidity() => ":GX9C#";
        public static string GetWeatherDewpoint() => ":GX9E#";
        public static string GetControllerTemperature() => ":GX9F#";

        // Servo calibration commands. these are only present in firmwares that were built from may to September 2025
        // these commands dispeared in the axis refactor of 10.26a
        public static string ServoTrackNormal() => ":SX4E,T#";
        public static string ServoTrackFixed() => ":SX4E,F#";
        public static string ServoRecord() => ":SX4E,R#";
        public static string ServoStop() => ":SX4E,W#";
        public static string ServoClear() => ":SX4E,!#";
        public static string ServoLoadCal() => ":SX4E,L#";
        public static string ServoSaveCal() => ":SX4E,S#";
        public static string ServoLoadBackup() => ":SX4E,V#";
        public static string ServoSaveBackup() => ":SX4E,B#";
        public static string ServoHpf() => ":SX4E,H#";
        public static string ServoLpf() => ":SX4E,A#";

        public static string Latitude(double northPositiveDeg) =>
            $":St{FormatSignedDms(northPositiveDeg, 2)}#";
        public static string GetLatitude() => ":GtH#";

        public static string LongitudeFromEastPositive(double eastPositiveDeg) =>
            $":Sg{FormatSignedDms(-eastPositiveDeg, 3)}#";
        public static string GetLongitude() => ":GgH#";
        public static string Elevation(double meters) =>
            $":Sv{meters.ToString("0.0", CultureInfo.InvariantCulture)}#";   // OnstepX uses strtod so the sign is optional for positives (not clear in doc)
        public static string GetElevation() => ":Gv#";

        public static bool IsForkOrAltAz(MountType mountType) =>
            mountType is MountType.Fork or MountType.Fork_TA or MountType.Fork_TAC or
                         MountType.AltAz or MountType.AltAz_Unlimited;

        public static int RoundToInt(double value) =>
            (int)Math.Round(value, MidpointRounding.AwayFromZero);

        public static int DegreesToMeridianMinutes(double degrees) =>
            RoundToInt(degrees * 4.0); // *60/15 RA to deg

        public static char PreferredPierSideChar(PreferredPierSide side) => side switch {
            Model.PreferredPierSide.West => 'W',
            Model.PreferredPierSide.East => 'E',
            Model.PreferredPierSide.Auto => 'A',
            _ => 'B'
        };

        public static string FormatHa(double haHours) {
            haHours = ((haHours % 24.0) + 24.0) % 24.0;
            var totalSeconds = RoundToInt(haHours * 3600.0);
            totalSeconds = ((totalSeconds % 86400) + 86400) % 86400;
            var h = totalSeconds / 3600;
            var m = totalSeconds % 3600 / 60;
            var s = totalSeconds % 60;
            return $"{h:D2}:{m:D2}:{s:D2}";
        }

        public static string FormatSignedDms(double degrees, int degreeDigits) {
            var sign = degrees < 0 ? "-" : "+";
            var totalSeconds = RoundToInt(Math.Abs(degrees) * 3600.0);
            var d = totalSeconds / 3600;
            var m = totalSeconds % 3600 / 60;
            var s = totalSeconds % 60;
            return $"{sign}{d.ToString(new string('0', degreeDigits), CultureInfo.InvariantCulture)}*{m:D2}:{s:D2}";
        }

        private static string BoolInt(bool enabled) => enabled ? "1" : "0";

        private static char HexDigit(int value) {
            if (value < 0 || value > 0x0f) throw new ArgumentOutOfRangeException(nameof(value));
            return "0123456789abcdef"[value];
        }
    }
}
