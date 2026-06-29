using System;
using NINA.Profile.Interfaces;

namespace NINA.Plugin.OnStepXTools.ModelManagement {

    public class HorizonAndMeridianFilter {
        private readonly IProfileService _profile;

        public HorizonAndMeridianFilter(IProfileService profile) {
            _profile = profile;
        }

        // Altitude/azimuth visibility check used by GoldenSpiral, AutoGrid, Random.
        // Meridian is approximated as the N–S azimuth axis (Az=0°/180°).
        public bool IsVisible(double altitudeDeg, double azimuthDeg) {
            var horizon = _profile.ActiveProfile.AstrometrySettings.Horizon;
            if (horizon != null && altitudeDeg < horizon.GetAltitude(azimuthDeg))
                return false;

            double mExcl = MeridianExclusionHalfWidthDeg();
            if (mExcl > 0) {
                // Normalize to 0–360
                double az = ((azimuthDeg % 360) + 360) % 360;
                // Angular distance to North (az=0) - smaller of az and 360-az
                double dNorth = Math.Min(az, 360 - az);
                // Angular distance to South (az=180)
                double dSouth = Math.Abs(az - 180);
                if (dNorth < mExcl || dSouth < mExcl)
                    return false;
            }
            return true;
        }

        // HA-based check for SiderealPath, which knows HA explicitly.
        // mExcl is in degrees of HA; convert to hours (15°/h).
        public bool IsHAVisible(double haHours) {
            double mExclHours = MeridianExclusionHalfWidthDeg() / 15.0;
            return Math.Abs(haHours) >= mExclHours;
        }

        public double HorizonAltitudeAt(double azimuthDeg) {
            var horizon = _profile.ActiveProfile.AstrometrySettings.Horizon;
            return horizon?.GetAltitude(azimuthDeg) ?? 0.0;
        }

        // Returns the meridian exclusion half-width in degrees of HA.
        // MinutesAfterMeridian × 0.25 deg/min (15°/h ÷ 60 min/h)
        public double MeridianExclusionHalfWidthDeg() {
            var minutes = _profile.ActiveProfile.MeridianFlipSettings.MinutesAfterMeridian;
            return minutes * 0.25;
        }
    }
}
