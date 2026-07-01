using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Model;
using NINA.Image.Interfaces;
using NINA.PlateSolving;
using NINA.PlateSolving.Interfaces;
using NINA.Profile.Interfaces;
using System.ComponentModel.Composition;
using NINA.Plugin.OnStepXTools.Interfaces;
using NINA.Plugin.OnStepXTools.Model;

namespace NINA.Plugin.OnStepXTools.ModelManagement {

    [Export(typeof(IModelBuilder))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public class ModelBuilder : IModelBuilder {
        private readonly IOnStepXMount          _mount;
        private readonly ITelescopeMediator     _telescope;
        private readonly IImagingMediator       _imaging;
        private readonly IPlateSolverFactory    _solverFactory;
        private readonly IProfileService        _profile;
        private readonly IModelBuilderMediator  _mediator;
        private readonly IWeatherDataMediator   _weather;
        private readonly IImageControlVM        _imageControl;
        private readonly ModelBuildSessionStore _store = new();

        [ImportingConstructor]
        public ModelBuilder(
            IOnStepXMount mount,
            ITelescopeMediator telescope,
            IImagingMediator imaging,
            IPlateSolverFactory solverFactory,
            IProfileService profile,
            IModelBuilderMediator mediator,
            IWeatherDataMediator weather,
            IImageControlVM imageControl) {
            _mount         = mount;
            _telescope     = telescope;
            _imaging       = imaging;
            _solverFactory = solverFactory;
            _profile       = profile;
            _mediator      = mediator;
            _weather       = weather;
            _imageControl  = imageControl;
        }

        public async Task<AlignmentModelCoefficients?> BuildModelAsync(
            IReadOnlyList<AlignmentPoint> points,
            ModelBuilderOptions opts,
            IProgress<ApplicationStatus>? progress,
            CancellationToken ct) {

            var session = new ModelBuildSession { Mode = opts.Mode };

            // Resume: restore already-completed points
            if (opts.ResumeSessionId != null) {
                var saved = await _store.LoadAsync(opts.ResumeSessionId);
                if (saved != null) {
                    session = saved;
                    foreach (var sp in saved.Points) {
                        if (sp.Index < points.Count)
                            sp.RestoreTo(points[sp.Index]);
                    }
                }
            }

            var completedPoints = session.Points.ToList();

            // Announce that a build session is starting so the UI can display all points
            _mediator.NotifyStarted(new BuildStartedEventArgs {
                AllPoints = points,
                Mode      = opts.Mode
            });

            for (int i = 0; i < points.Count; i++) {
                ct.ThrowIfCancellationRequested();

                var point = points[i];
                if (point.State == AlignmentPointState.Added) continue; // already done (resumed)

                try {
                    await ProcessPointAsync(point, opts, progress, ct);

                    if (point.State == AlignmentPointState.Added) {
                        var sp = ToSavedPoint(point);
                        completedPoints.Add(sp);
                        session.Points = completedPoints;
                        await _store.SaveAsync(session);
                    }
                } catch (OperationCanceledException) {
                    throw;
                } catch (Exception ex) {
                    Logger.Error($"Point {i} failed: {ex.Message}");
                    point.State = AlignmentPointState.Failed;
                }

                _mediator.NotifyProgress(new BuildProgressEventArgs {
                    CompletedPoints = completedPoints.Count,
                    TotalPoints     = points.Count,
                    CurrentPoint    = point,
                    StatusMessage   = $"Point {i + 1}/{points.Count} - {point.State}",
                    AllPoints       = points
                });
            }

            // Reject plate solves that are catastrophically wrong (solver matched wrong field).
            // Threshold = 3 × RmsErrorThresholdArcsec (default 3 × 3600" = 3°).
            // This accepts all realistic pointing errors, even for poorly aligned mounts.
            var goodPoints = session.Points
                .Where(p => Math.Abs(p.PointingErrorRAArcsec) < opts.RmsErrorThresholdArcsec * 3 &&
                             Math.Abs(p.PointingErrorDecArcsec) < opts.RmsErrorThresholdArcsec * 3)
                .ToList();

            AlignmentModelCoefficients? coefficients = null;

            if (opts.Mode == BuildMode.FullSkyPointingModel) {
                double siteLat = 45.0;
                try { siteLat = _profile.ActiveProfile.AstrometrySettings.Latitude; } catch { }
                coefficients = PointingModelSolver.Solve(goodPoints, siteLat);
                Logger.Info($"ModelBuilder: {goodPoints.Count} good points → solver returned {(coefficients != null ? "coefficients" : "null (need ≥ 6 points)")}.");

                if (coefficients != null && opts.WriteModelToMountOnCompletion) {
                    try { await _mount.WriteCoefficientsAsync(coefficients, ct); }
                    catch (Exception ex) { Logger.Error($"WriteCoefficientsAsync failed: {ex.Message}"); }
                }
                if (opts.SaveToEepromOnCompletion) {
                    try { await _mount.SaveAlignmentToEepromAsync(ct); }
                    catch (Exception ex) { Logger.Error($"SaveAlignmentToEepromAsync failed: {ex.Message}"); }
                }
            } else {
                if (opts.SaveToEepromOnCompletion) {
                    try { await _mount.SaveAlignmentToEepromAsync(ct); }
                    catch (Exception ex) { Logger.Error($"SaveAlignmentToEepromAsync failed: {ex.Message}"); }
                }
            }

            session.Coefficients = coefficients;
            try { await _store.SaveAsync(session); } catch { }

            var residuals = goodPoints.Select(ResidualPoint.FromSavedPoint).ToList();
            // NotifyCompleted is always called so the UI panel updates even on partial failure
            _mediator.NotifyCompleted(new BuildCompletedEventArgs {
                Success      = coefficients != null,
                Coefficients = coefficients,
                Residuals    = residuals
            });

            return coefficients;
        }

        private async Task ProcessPointAsync(
            AlignmentPoint point,
            ModelBuilderOptions opts,
            IProgress<ApplicationStatus>? progress,
            CancellationToken ct) {

            // 1. Convert Alt/Az to RA/Dec using current LST, site latitude and atmosphere.
            //    Refraction corrects geometric → apparent altitude so the mount receives
            //    the apparent (observed) RA/Dec that its ASCOM driver expects.
            var info = _telescope.GetInfo();
            var siteLatitude = IsFinite(info.SiteLatitude)
                ? info.SiteLatitude
                : _profile.ActiveProfile.AstrometrySettings.Latitude;
            var siderealTime = info.SiderealTime;
            if (!IsFinite(siteLatitude) || siteLatitude < -90.0 || siteLatitude > 90.0) {
                throw new InvalidOperationException($"Mount/profile reported invalid site latitude: {siteLatitude}");
            }
            if (!IsFinite(siderealTime)) {
                throw new InvalidOperationException($"Mount reported invalid sidereal time: {siderealTime}");
            }

            GetWeatherForRefraction(out double pressureMbar, out double temperatureCelsius,
                                    info.SiteElevation);
            var coords = AltAzToEquatorial(
                point.AltitudeDeg, point.AzimuthDeg,
                siteLatitude, siderealTime,
                pressureMbar, temperatureCelsius);

            point.TargetRAHours  = coords.RADegrees / 15.0;
            point.TargetDecDeg   = coords.Dec;
            if (!IsFinite(point.TargetRAHours) || !IsFinite(point.TargetDecDeg)) {
                throw new InvalidOperationException(
                    $"Computed invalid target coordinates for point {point.Index}: " +
                    $"Alt={point.AltitudeDeg:F1}, Az={point.AzimuthDeg:F1}, " +
                    $"Lat={siteLatitude:F4}, LST={siderealTime:F4}, " +
                    $"Pressure={pressureMbar:F1}, Temp={temperatureCelsius:F1}");
            }

            // 2. Slew
            point.State = AlignmentPointState.Slewing;
            await _telescope.SlewToCoordinatesAsync(coords, ct);

            // 3. Settle
            point.State = AlignmentPointState.Settling;
            await Task.Delay(TimeSpan.FromSeconds(opts.SlewSettleSeconds), ct);

            // 4. Read mount position after settle
            var mountInfo  = _telescope.GetInfo();
            point.MountRAHours = mountInfo.Coordinates?.RADegrees / 15.0 ?? coords.RADegrees / 15.0;
            point.MountDecDeg  = mountInfo.Coordinates?.Dec ?? coords.Dec;
            var lst = mountInfo.SiderealTime;
            point.MountHAHours = lst - point.MountRAHours;
            if (point.MountHAHours > 12)  point.MountHAHours -= 24;
            if (point.MountHAHours < -12) point.MountHAHours += 24;
            point.PierSide = mountInfo.SideOfPier == NINA.Core.Enum.PierSide.pierEast ? 1 : -1;

            // 5. Capture
            point.State = AlignmentPointState.Exposing;
            var captureSeq = new CaptureSequence {
                ExposureTime       = opts.ExposureTimeSeconds,
                ImageType          = CaptureSequence.ImageTypes.SNAPSHOT,
                TotalExposureCount = 1
            };
            var exposure = await _imaging.CaptureImage(captureSeq, ct, progress);
            var imageData = await exposure.ToImageData(progress, ct);
            await ShowCapturedImageAsync(imageData, ct);

            // 6. Plate solve - use NINA's configured solver with full profile parameters
            point.State = AlignmentPointState.PlateSolving;
            var ps     = _profile.ActiveProfile.PlateSolveSettings;
            var solver = _solverFactory.GetPlateSolver(ps);
            var param  = new PlateSolveParameter {
                FocalLength      = _profile.ActiveProfile.TelescopeSettings.FocalLength,
                Binning          = 1,
                Coordinates      = new Coordinates(
                    point.MountRAHours, point.MountDecDeg,
                    Epoch.JNOW, Coordinates.RAType.Hours),
                SearchRadius     = ps.SearchRadius,
                DownSampleFactor = ps.DownSampleFactor,
                MaxObjects       = ps.MaxObjects,
                BlindFailoverEnabled = ps.BlindFailoverEnabled
            };

            // Try to populate PixelSize from camera settings (improves FOV estimation)
            try { param.PixelSize = _profile.ActiveProfile.CameraSettings.PixelSize; } catch { }

            var result = await solver.SolveAsync(imageData, param, progress, ct);

            if (!result.Success) {
                Logger.Warning($"Plate solve failed for point {point.Index} at Alt={point.AltitudeDeg:F1}° Az={point.AzimuthDeg:F1}°");
                point.State = AlignmentPointState.Failed;
                return;
            }

            // ── Epoch normalisation ──────────────────────────────────────────────
            // Plate solver catalogues are J2000; the mount reports JNOW.
            // Without converting, the epoch difference (~26 arcmin in 2026) would
            // cause all points to be rejected by the RMS outlier filter.
            var solvedCoords = result.Coordinates.Epoch == Epoch.JNOW
                ? result.Coordinates
                : result.Coordinates.Transform(Epoch.JNOW);

            point.SolvedRAHours = solvedCoords.RADegrees / 15.0;
            point.SolvedDecDeg  = solvedCoords.Dec;

            Logger.Debug($"  Point {point.Index}:  Solved RA={result.Coordinates.RAString}  Dec={result.Coordinates.DecString}");
            Logger.Debug($"  Point {point.Index}:  Solved RA={point.SolvedRAHours}  Dec={point.SolvedDecDeg}");

            // 7. Compute pointing errors (JNOW solved vs JNOW mount)
            var cosD = Math.Cos(point.MountDecDeg * Math.PI / 180.0);
            point.PointingErrorRAArcsec  = (point.SolvedRAHours - point.MountRAHours) * 15.0 * 3600.0 * cosD;
            point.PointingErrorDecArcsec = (point.SolvedDecDeg  - point.MountDecDeg) * 3600.0;

            Logger.Debug($"  Point {point.Index}: ΔRA={point.PointingErrorRAArcsec:F1}\"  ΔDec={point.PointingErrorDecArcsec:F1}\"");

            if (opts.Mode == BuildMode.StarAlignment) {
                point.State = AlignmentPointState.Uploading;
                // Use JNOW solved RA for HA computation (lst is also JNOW)
                double actualHAHours  = lst - point.SolvedRAHours;
                double actualDecDeg   = point.SolvedDecDeg;
                double mountHAHours   = point.MountHAHours;
                double mountDecDeg    = point.MountDecDeg;
                await _mount.UploadAlignmentStarAsync(
                    actualHAHours, actualDecDeg,
                    mountHAHours,  mountDecDeg,
                    point.PierSide, ct);
                await _mount.ComputeAlignmentOnControllerAsync(ct);
            }

            point.State = AlignmentPointState.Added;
        }

        private async Task ShowCapturedImageAsync(IImageData imageData, CancellationToken ct) {
            try {
                var render = await _imageControl.PrepareImage(
                    imageData,
                    new PrepareImageParameters(autoStretch: true, detectStars: false),
                    ct);
                _imageControl.RenderedImage = render;
                _imageControl.Image = render.Image;
                _imageControl.Status = new ApplicationStatus {
                    Status = "OnStepX model plate-solve image"
                };
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Warning($"Failed to show OnStepX model image in Image window: {ex.Message}");
            }
        }

        private static SavedModelPoint ToSavedPoint(AlignmentPoint p) => new() {
            Index                    = p.Index,
            AltitudeDeg              = p.AltitudeDeg,
            AzimuthDeg               = p.AzimuthDeg,
            MountHAHours             = p.MountHAHours,
            MountDecDeg              = p.MountDecDeg,
            SolvedRAHours            = p.SolvedRAHours,
            SolvedDecDeg             = p.SolvedDecDeg,
            PierSide                 = p.PierSide,
            PointingErrorRAArcsec    = p.PointingErrorRAArcsec,
            PointingErrorDecArcsec   = p.PointingErrorDecArcsec
        };

        // ── Weather / refraction helpers ─────────────────────────────────────────

        // Fetch ambient temperature and barometric pressure from the mount's OnStepX
        // sensors (:GX9A# / :GX9B#).  Falls back to a standard atmosphere estimated
        // from site elevation when the sensors are unavailable.
        private void GetWeatherForRefraction(
            out double pressureMbar, out double temperatureCelsius,
            double siteElevationM) {

            // Source 3 — standard atmosphere fallback (ISA sea-level → elevation)
            if (!IsFinite(siteElevationM)) siteElevationM = 0.0;
            pressureMbar       = 1013.25 * Math.Pow(1.0 - 2.25577e-5 * siteElevationM, 5.25588);
            temperatureCelsius = 10.0;

            bool haveTemp = false, havePress = false;

            // Source 1 — mount weather sensors (highest priority; read directly via LX200)
            try {
                var tStr = _telescope.SendCommandString(":GX9A#", raw: true);
                var pStr = _telescope.SendCommandString(":GX9B#", raw: true);

                if (!string.IsNullOrWhiteSpace(tStr) &&
                    double.TryParse(tStr.TrimEnd('#').Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var t) &&
                    IsFinite(t) && t > -100.0 && t < 80.0) {
                    temperatureCelsius = t;
                    haveTemp = true;
                }
                if (!string.IsNullOrWhiteSpace(pStr) &&
                    double.TryParse(pStr.TrimEnd('#').Trim(),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var p) &&
                    IsFinite(p) && p > 0) {
                    pressureMbar = p;
                    havePress = true;
                }
            } catch { /* fall through to next source */ }

            if (haveTemp && havePress) return;

            // Source 2 — NINA weather device (IWeatherDataMediator)
            try {
                var wx = _weather.GetInfo();
                if (wx.Connected) {
                    if (!haveTemp && IsFinite(wx.Temperature) && wx.Temperature > -100.0 && wx.Temperature < 80.0) {
                        temperatureCelsius = wx.Temperature;
                        haveTemp = true;
                    }
                    if (!havePress && IsFinite(wx.Pressure) && wx.Pressure > 0) {
                        pressureMbar = wx.Pressure;
                        havePress = true;
                    }
                }
            } catch { /* fall through to standard atmosphere */ }

            if (!IsFinite(pressureMbar) || pressureMbar <= 0) pressureMbar = 1013.25;
            if (!IsFinite(temperatureCelsius) || temperatureCelsius <= -100.0 || temperatureCelsius >= 80.0)
                temperatureCelsius = 10.0;

            if (!haveTemp || !havePress)
                Logger.Debug($"OnStepX refraction: using standard atmosphere " +
                             $"(mount sensor={haveTemp}/{havePress}, weather device connected={TryWeatherConnected()})");
        }

        private bool TryWeatherConnected() {
            try { return _weather.GetInfo().Connected; } catch { return false; }
        }

        // Alt/Az → JNOW apparent equatorial coordinates (topocentric).
        // altDeg is the GEOMETRIC altitude of the model point; refraction is applied
        // internally to convert to apparent altitude before the coordinate transform,
        // so the mount receives the apparent RA/Dec its ASCOM driver expects.
        private static Coordinates AltAzToEquatorial(
            double altDeg, double azDeg, double latDeg, double lstHours,
            double pressureMbar = 1013.25, double temperatureCelsius = 10.0) {

            if (!IsFinite(altDeg) || !IsFinite(azDeg) || !IsFinite(latDeg) || !IsFinite(lstHours)) {
                throw new InvalidOperationException(
                    $"Invalid Alt/Az conversion input: Alt={altDeg}, Az={azDeg}, Lat={latDeg}, LST={lstHours}");
            }

            // Geometric → apparent altitude (atmospheric refraction)
            double apparentAltDeg = ApplyRefraction(altDeg, pressureMbar, temperatureCelsius);

            double altRad = apparentAltDeg * Math.PI / 180.0;
            double azRad  = azDeg  * Math.PI / 180.0;
            double latRad = latDeg * Math.PI / 180.0;

            double sinDec = Math.Sin(altRad) * Math.Sin(latRad)
                          + Math.Cos(altRad) * Math.Cos(latRad) * Math.Cos(azRad);
            double decRad = Math.Asin(Math.Clamp(sinDec, -1.0, 1.0));

            double haY     = -Math.Sin(azRad) * Math.Cos(altRad);
            double haX     =  Math.Cos(latRad) * Math.Sin(altRad)
                           -  Math.Sin(latRad) * Math.Cos(altRad) * Math.Cos(azRad);
            double haHours = Math.Atan2(haY, haX) * 12.0 / Math.PI;

            double raHours = (lstHours - haHours) % 24.0;
            if (raHours < 0.0) raHours += 24.0;

            if (!IsFinite(raHours) || !IsFinite(decRad)) {
                throw new InvalidOperationException(
                    $"Invalid Alt/Az conversion result: RA={raHours}, DecRad={decRad}, " +
                    $"Alt={altDeg}, Az={azDeg}, Lat={latDeg}, LST={lstHours}, " +
                    $"Pressure={pressureMbar}, Temp={temperatureCelsius}");
            }

            return new Coordinates(raHours, decRad * 180.0 / Math.PI, Epoch.JNOW, Coordinates.RAType.Hours);
        }

        // Geometric → apparent altitude using Bennett's formula with P/T correction.
        // Accurate to ~0.07' for altitudes above 5°; returns geometric alt unchanged
        // below −1° (below the horizon where refraction is undefined).
        //   pressureMbar      – local barometric pressure in mbar
        //   temperatureCelsius – local ambient temperature in °C
        private static double ApplyRefraction(
            double geometricAltDeg,
            double pressureMbar      = 1013.25,
            double temperatureCelsius = 10.0) {

            if (!IsFinite(geometricAltDeg)) return geometricAltDeg;
            if (!IsFinite(pressureMbar) || pressureMbar <= 0) pressureMbar = 1013.25;
            if (!IsFinite(temperatureCelsius) || temperatureCelsius <= -100.0 || temperatureCelsius >= 80.0)
                temperatureCelsius = 10.0;

            if (geometricAltDeg < -1.0) return geometricAltDeg;

            // Bennett's formula: refraction R in arcminutes as a function of
            // apparent altitude.  We use geometric alt as a first-order input
            // (the difference is < 2" at 10°, negligible for our purposes).
            double a  = geometricAltDeg;
            double R  = 1.02 / Math.Tan((a + 10.3 / (a + 5.11)) * (Math.PI / 180.0));

            // Pressure and temperature correction (Stone 1996)
            R *= (pressureMbar / 1013.25) * (283.15 / (273.15 + temperatureCelsius));

            // R is in arcminutes; convert to degrees and add to geometric altitude
            return geometricAltDeg + R / 60.0;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
