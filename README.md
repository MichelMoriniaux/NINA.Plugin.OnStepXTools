# NINA.Plugin.OnStepXTools

A [N.I.N.A.](https://nighttime-imaging.eu/) 3.x plugin for mount configuration, real-time status monitoring, and automated all-sky pointing-model generation for telescope mounts running the [OnStepX](https://github.com/hjd1964/OnStepX) firmware, accessed through the OnStep ASCOM telescope driver.

---

## Features

| Feature | Description |
|---------|-------------|
| **Mount Status** | Live RA/Dec, Alt/Az, LST, sidereal time, pier side, tracking state, guide rates, site coordinates, weather from OnStepX sensors |
| **Mount Settings** | Read and write all runtime mount parameters (tracking rate, guide rate, backlash, limits, meridian flip, park, buzzer) |
| **Axis Config** | Dynamic discovery and editing of motor parameters via `:GXA{n},{i}#` (OnStepX ≥ 10.26) or composite `:GXA{n}#` (< 10.26) |
| **Point Generation** | Four algorithms (Golden Spiral, Sidereal Path, Auto Grid, Random) with horizon and meridian exclusion; meridian-crossing-minimised slew order |
| **Model Info** | Live polar sky chart with visit-path, per-point colour coding, real-time mount crosshair, residual scatter plot; upload coefficients to mount |
| **Full-Sky Pointing Model** | NINA-sequencer item - plate-solve based all-sky model; 12-parameter least-squares solver matching OnStepX `Align.hs.cpp`; resumable sessions |
| **Star Alignment** | NINA-sequencer item - plate-solve based alignment; uploads corrected star pairs directly to the OnStepX controller (1–9 stars) |
| **Clear Alignment** | Sequencer item - clears the model from controller RAM (`:SX09,0#`) |
| **Save to EEPROM** | Sequencer item - persists the model to non-volatile memory (`:AW#`) |

---

## Requirements

- **N.I.N.A.** 3.2.0.9001 or later
- **OnStepX firmware** on the mount controller (tested with 10.25–10.26)
- **OnStep ASCOM telescope driver** connected and working in NINA
- **.NET 8.0 for Windows** (bundled with NINA)
- A camera and plate solver configured in NINA (for model-building sequencer items)

---

## Installation

### From build

```powershell
# From the NINA.Plugin.OnStepXTools\ directory:
dotnet build
```

The post-build step automatically copies the DLL to:

```
%LOCALAPPDATA%\NINA\Plugins\3.0.0\NINA.Plugin.OnStepXTools\NINA.Plugin.OnStepXTools.dll
```

Restart NINA; the plugin will appear under **Options → Plugins**.

### Manual

Copy `NINA.Plugin.OnStepXTools.dll` to the path above.

> **Do not** copy OxyPlot, Newtonsoft.Json, or Autofac - they are already bundled with NINA 3.x.

---

## Quick Start

1. Connect your OnStep mount in NINA via the ASCOM telescope driver.
2. The **OnStepX Mount Status**, **Mount Settings**, and **Axis Config** panels will populate automatically in the Imaging tab as soon as the telescope connects.
3. Use **OnStepX Point Generation** to generate a sky coverage plan and save or send it to a sequencer item.
4. Add the **OnStepX Full-Sky Pointing Model** sequencer item to an Advanced Sequence, load the points, and run the sequence.
5. Review residuals in **OnStepX Model Info** and click **Write to Mount** when satisfied.

---

## Panels

### OnStepX Mount Status

Displays live telemetry from NINA's telescope mediator (`ITelescopeConsumer`) refreshed on every mount update:

- Equatorial position (RA/Dec) and horizontal position (Alt/Az)
- Sidereal time (LST in HH:MM:SS), UTC, hours to meridian, time-to-flip
- Mount state: tracking on/off, tracking rate, slewing, at-park, at-home, pier side, epoch
- Guide rates (RA and Dec in arcsec/s)
- Site latitude, longitude, elevation, alignment mode, driver info

**Weather** (polled from OnStepX sensors every 30 s via LX200 commands):

| Sensor | Command |
|--------|---------|
| Ambient temperature | `:GX9A#` |
| Barometric pressure | `:GX9B#` |
| Relative humidity | `:GX9C#` |
| Dew point | `:GX9E#` |
| Controller temperature | `:GX9F#` |

### OnStepX Mount Settings

Reads all current mount runtime parameters on telescope connect and allows editing them. Organised into sections:

- **Site Location** - latitude/longitude in DMS (read) and decimal degrees (edit), Set button
- **Tracking** - on/off, rate (Sidereal/Lunar/Solar/King), rate compensation, frequency adjust (±0.02 Hz, reset)
- **Guide Rate & Slew Speed** - 10-level guide rate (0.25× to VVF), slew speed (VSlow–VFast)
- **Meridian Flip & Park** - auto flip, pause at home, preferred pier side, trigger flip, set park position, goto buzzer
- **Backlash** - axis 1 and axis 2 in arcseconds (`:%BR#` / `:%BD#`)
- **Limits** - minimum/maximum altitude, degrees past meridian east/west

Commands use verified OnStepX LX200 syntax (SmartWebServer source cross-referenced).

### OnStepX Axis Config

Reads axis motor configuration using the dynamic `:GXA{n},{i}#` parameter system.

**Firmware version detection** - the panel reads the firmware version from `TelescopeInfo.Description` (e.g. `"On-Step 10.25p"`) and selects the appropriate command format:

| Version | GET command | SET command |
|---------|-------------|-------------|
| < 10.26 | `:GXA{n}#` - one composite string per axis | `:SXA{n},{composite}#` |
| ≥ 10.26 | `:GXA{n},0#` for count, `:GXA{n},{i}#` per parameter | `:SXA{n},{i},{v}#` |

For firmware ≥ 10.26, each parameter shows its name, current value, and valid range as reported by the firmware. Type codes (boolean, integer, float, power-of-2, decay mode) are decoded and shown appropriately.

Mount type (GEM / EQ Fork / Alt-Az) and controller reboot buttons are always available.

> **Servo calibration** buttons (Track Normally, Record Calibration, etc.) are included but the exact command strings need hardware verification against your firmware version before use.

### OnStepX Point Generation

Generates a list of sky positions for a model build.

**Modes**
- *Full-Sky Pointing Model* - up to 300 points, any algorithm
- *Star Alignment* - up to 9 stars, same algorithms

**Algorithms**

| Algorithm | Description |
|-----------|-------------|
| **Golden Spiral** | Fibonacci lattice - uniform area density across the visible sky sector |
| **Auto Grid** | Equal-area latitude-weighted grid - automatically sizes altitude bands and azimuth steps from the point count |
| **Random** | Uniform area random - samples sin(alt) uniformly to avoid clustering near the zenith |
| **Sidereal Path** | HA/Dec grid - three declination bands (target ± Dec step) swept across a configurable hour angle range; useful for one-night model building |

All algorithms apply:
- Altitude range filter (min/max altitude)
- Custom NINA horizon filter
- Meridian exclusion zone (from NINA meridian flip settings)
- Meridian-crossing-minimised slew order (East side first, then West, nearest-neighbour within each hemisphere)

The sky chart (azimuthal equidistant projection, `PlotType.Cartesian` for true circles) shows:
- Altitude rings (0°, 20°, 40°, 60°, 80°)
- Custom horizon polygon (grey fill)
- Meridian exclusion zone (light blue wedge)
- Meridian line N–Zenith–S (dashed)
- Generated points (cyan) connected by planned visit-order path

Points can be saved to / loaded from `.json` files for reuse across sessions.

### OnStepX Model Info

Shows the current state of the pointing model build in real time.

**Sky chart** (left): azimuthal equidistant polar display with:
- Planned visit path (loaded from sequencer item even before the build starts)
- Points colour-coded by state: green (pending) → yellow (in-progress) → red (solved) → dark-red (failed)
- Error arrows (yellow) for completed points showing direction and magnitude
- Live mount crosshair (bullseye + arms) updated from `ITelescopeConsumer`

**Residual plot** (right): ΔRA vs ΔDec scatter with RMS circle.

**Action buttons**:
- **Write to Mount** - uploads all 12 coefficients via `:SX0n,v#`
- **Save Model…** / **Load Model…** - JSON file I/O
- **Force Model Activation** - sends `:SX09,2#` to force the controller to apply the model

The panel updates progressively - residuals appear after each completed point, not only at the end of the run.

---

## Sequencer Items

### OnStepX Full-Sky Pointing Model

Automated plate-solve based all-sky pointing model. Add to an Advanced Sequence after pointing model points have been generated.

**Options**:
- **Write model to mount on completion** - uploads solved coefficients via `:SX0n,v#`
- **Resume last session** - reloads the most recent interrupted session from disk; already-completed points are skipped
- **Save coefficients to file** - writes a `.json` file on completion (loadable in Model Info View via Load Model…)
- **Load from file / Use generated points** - sets the point list

**Exposure time** comes from NINA's plate-solve settings (`Profile → Plate Solving → Exposure Time`), not from the sequencer item.

The build loop per point:
1. Convert Alt/Az → apparent RA/Dec (with atmospheric refraction using mount weather sensors)
2. Slew to apparent coordinates
3. Settle (3 s default)
4. Read mount RA/Dec and LST
5. Capture image (NINA imaging mediator)
6. Plate solve (NINA configured solver, JNOW coordinates)
7. Convert solve result J2000 → JNOW
8. Compute pointing errors (ΔRA, ΔDec in arcseconds)
9. Save point to disk atomically
10. Notify Model Info panel

On completion, the 12-parameter least-squares solver runs. The design matrix matches OnStepX `Align.hs.cpp` exactly so coefficients can be uploaded directly without conversion.

Sessions are stored in:
```
%APPDATA%\NINA\Plugins\OnStepX\ModelBuilds\{sessionId}.json
```

### OnStepX Star Alignment

Same workflow as Full-Sky Pointing Model but uses the on-device solver (1–9 stars). Each successfully solved point is uploaded to the controller via:

```
:SX0A,<actual HA arcsec>#
:SX0B,<actual Dec arcsec>#
:SX0C,<mount HA arcsec>#
:SX0D,<mount Dec arcsec>#
:SX0E,<pier side>#
:SX09,1#   (trigger solve)
```

**Options**: Save to EEPROM on completion.

### Clear Alignment Model / Save to EEPROM

Simple single-command sequencer items for use in sequences:

```
:SX09,0#   - erase model from RAM (does not affect EEPROM)
:AW#        - persist model to non-volatile memory
```

---

## Plate Solving Notes

- Exposure time is read from `profileService.ActiveProfile.PlateSolveSettings.ExposureTime`
- The plugin uses NINA's `IPlateSolverFactory.GetPlateSolver(settings)` - whatever solver you have configured in NINA's options (ASTAP, Astrometry.net, ANSVR, etc.)
- Plate solve results are in J2000 epoch. The plugin converts to JNOW using `result.Coordinates.Transform(Epoch.JNOW)` before comparing with the mount's JNOW coordinates
- **No sync is performed** after solving - the plugin only reads the solved coordinates; the mount's pointing model is never modified by NINA's sync mechanism

---

## Atmospheric Refraction

When computing the slew target (Alt/Az → apparent RA/Dec), the plugin applies atmospheric refraction using:

- **Bennett's formula** (accurate to ~0.07' for altitudes above 5°)
- **Pressure/temperature correction** (Stone 1996)
- Barometric pressure estimated from site elevation via ISA formula, overridden by actual mount sensor value if available (`:GX9B#`, `:GX9A#`)

This ensures the mount receives apparent (observed) coordinates as its ASCOM driver expects.

---

## Pointing Model Mathematics

The 12-parameter model matches OnStepX `Align.hs.cpp` exactly:

**ΔH (HA) row:**
```
errH = −ax1Cor + altCor·sinH·tanD − azmCor·cosH·tanD
       + doCor·p·secD − pdCor·p·tanD
       + tfCor·cosLat·sinH·secD + hca·cos(H+hcp)·p
```

**ΔD (Dec) row:**
```
errD = ax2Cor·p + altCor·cosH + azmCor·sinH
       − dfCor·(cosLat·cosH + sinLat·tanD)
       + tfCor·(cosLat·cosH·sinD − sinLat·cosD)
       + dca·cos(D+dcp)·p
```

Where `p` = pier side (+1 pierEast, −1 pierWest) matching OnStep's convention.

Key implementation notes:
- `dH = −errRA / cos(Dec)` - HA arcseconds, not RA on-sky arcseconds
- `ax1Cor` design-matrix coefficient is **−1** (errH = −ax1Cor + …)
- `ax2Cor`, `doCor`, and harmonics carry the **pier sign**
- `dfCor` uses the latitude-aware GEM formula (not the simplified fork formula)
- `tfCor` includes site latitude in both ΔH and ΔD

**Minimum points**: 6 (for 12 unknowns). Reliable polar alignment extraction requires 30+ points well distributed across both pier sides and all declination zones.

---

## Commands Reference (OnStepX LX200)

| Operation | GET | SET |
|-----------|-----|-----|
| Alignment star count | `:GX09#` | - |
| Clear model | - | `:SX09,0#` |
| Compute model | - | `:SX09,1#` |
| Force model activation | - | `:SX09,2#` |
| Persist to EEPROM | - | `:AW#` |
| Coefficient n (hex) | `:GX0n#` | `:SX0n,v#` |
| Backlash axis 1 | `:%BR#` | `:$BR{v}#` |
| Backlash axis 2 | `:%BD#` | `:$BD{v}#` |
| Min altitude | `:Gh#` | `:Sh{v}#` |
| Max altitude | `:Go#` | `:So{v}#` |
| Meridian E limit | `:GXE9#` | `:SXE9,{v}#` |
| Meridian W limit | `:GXEA#` | `:SXEA,{v}#` |
| Preferred pier side | `:GXE8#` | `:SXE8,{v}#` |
| Axis parameters (≥10.26) | `:GXA{n},{i}#` | `:SXA{n},{i},{v}#` |

> Command strings marked as needing hardware verification in the code comments have been cross-referenced with the [SmartWebServer source](https://github.com/hjd1964/SmartWebServer) but should be validated against your specific firmware version before use in production.

---

## Session Persistence

Build sessions are written atomically (write to `.tmp`, rename) after every successfully solved point:

```
%APPDATA%\NINA\Plugins\OnStepX\ModelBuilds\{sessionId}.json
```

Settings file:
```
%APPDATA%\NINA\Plugins\OnStepX\settings.json
```

---

## Comparison with TPoint

### Pointing Model Capability

**TPoint** uses an open-ended term library - users can add or remove correction terms (typically 20–40 for a serious model: `NPAE`, `MA`, `ME`, `TF`, `FF`, `DAF`, `ACEC`, `ACES`, `NDD`, `NDS`, and many more). The model is built incrementally and can be re-fit without re-observing.

**This plugin** uses exactly the 12 parameters that OnStepX's `Align.hs.cpp` supports:

```
ax1Cor, ax2Cor, altCor, azmCor, doCor, pdCor,
dfCor,  tfCor,  hcp,    hca,    dcp,   dca
```

This ceiling is set by the mount firmware, not by this plugin. The plugin cannot do better than the firmware can correct.

**Gap**: TPoint is substantially more expressive. For a Paramount or similar high-end mount it can model tube sag, fork flex, and periodic error harmonics at arbitrary frequency, all simultaneously. OnStepX has two harmonics (one RA, one Dec) with pier-sign.

### Solver Robustness

**TPoint** has ~30 years of field refinement. It handles ill-conditioned systems gracefully, performs iterative outlier rejection (sigma-clipping), and can weight individual points by time or altitude.

**This plugin** uses a single-pass normal-equations solver with Tikhonov regularisation (λ = 1×10⁻⁶). During development several critical correctness bugs were found and fixed:
- Wrong epoch (J2000 vs JNOW) causing ~1300" systematic Dec error
- Wrong sign on `ax1Cor` in the design matrix
- Missing pier-sign on `ax2Cor`, `doCor`, and harmonics
- Wrong `tfCor`/`dfCor` formulas (not matching `Align.hs.cpp`)
- `goodPoints` filter threshold 60× too tight for unaligned mounts

The solver is now aligned with OnStepX firmware math, but it has not been validated at the same level as TPoint.

**Gap**: Significant. TPoint's solver is battle-tested. This solver has only been exercised on a handful of real datasets.

### Minimum Points & Conditioning

| | This plugin | TPoint |
|-|-------------|--------|
| Parameters | 12 | 6 – 40+ (user-selected) |
| Minimum points | 6 (barely overdetermined) | Typically 20–30 for stable results |
| With 9 points | Polar alignment unreliable | TPoint would flag as under-sampled |
| Recommended | 30+ for stable 12-param fit | 50–100 for full model |

With fewer than ~30 points distributed across both pier sides and all declination zones, the individual parameters (especially polar alignment `altCor`/`azmCor`) are unreliable even though the overall RMS may look acceptable.

### Workflow Integration

**TPoint** is a standalone desktop application. The user runs it separately from their imaging software, exports the model, imports it into mount control software.

**This plugin** is native inside NINA:
- Point generation, sequencer item, live sky chart, and residual display are all in one workflow
- The model is uploaded directly to the mount mid-sequence via `:SX0n,v#`
- Refraction uses actual mount weather sensors
- Resume after interruption works via JSON session files

**Advantage here**: The integration is tighter and more convenient for OnStepX users already using NINA.

### Real-Time Correction

**TPoint** outputs corrections that the mount control application applies. The mount itself may or may not see them depending on integration.

**This plugin** writes coefficients directly into OnStepX firmware. The mount's own `Align.hs.cpp` applies them on every goto/track update in real time - no external software required after upload.

**Advantage here**: Zero latency and works even without a PC connected after the model is uploaded.

### What This Plugin Does Well

- Deep OnStepX integration - coefficients go directly into the firmware in the right format
- Native NINA sequencer workflow - no extra tools
- Correct atmospheric refraction using mount sensors
- Pier-side-aware design matrix (matching the firmware's equations exactly)
- Live visual feedback during build (path, coloured points, growing residual plot)

### What TPoint Does Better

- Far more correction terms for complex mechanical behavior
- Decades of solver validation and field testing
- Explicit conditioning warnings and sigma-clipping
- Works with many mount platforms, not just OnStepX
- Professional reporting (print-quality charts, statistics)
- Can identify specific mechanical problems by term inspection

### Bottom Line

For an OnStepX mount used with NINA, this plugin is a practical and well-integrated solution. The 12-parameter model matches what the firmware can actually apply, so adding more terms (as TPoint could) would not improve pointing anyway.

However, **the solver is new code that has only recently had foundational bugs corrected**, and real-world validation with more datasets and diverse mount types has not yet been done. For mounts and operators where pointing accuracy is mission-critical, TPoint (or a mature alternative like PemPro) remains the more reliable choice until this solver accumulates more field validation.
