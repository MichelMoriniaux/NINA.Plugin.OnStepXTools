# NINA.Plugin.OnStepXTools

A [N.I.N.A.](https://nighttime-imaging.eu/) 3.x plugin for mount configuration, real-time status monitoring, and automated all-sky pointing-model generation for telescope mounts running the [OnStepX](https://github.com/hjd1964/OnStepX) firmware, accessed through the OnStep ASCOM telescope driver.

---

## Features

| Feature | Description |
|---------|-------------|
| **Mount Status** | Live RA/Dec, Alt/Az, LST, pier side, tracking state, guide rates, site coordinates, weather from OnStepX sensors |
| **Mount Settings** | All runtime mount parameters (tracking, guide rate, backlash, limits, meridian flip, park, buzzer) plus dynamic axis motor config |
| **Model Builder** | Point generation, live build, real-time sky chart, residual scatter plot, and coefficient management — all in one panel |
| **Full-Sky Pointing Model** | Sequencer item — plate-solve based all-sky model; 12-parameter least-squares solver; resumable sessions |
| **Star Alignment** | Sequencer item — plate-solve based alignment; uploads corrected star pairs to the controller (1–9 stars) |
| **Clear Alignment** | Sequencer item — clears the model from controller RAM |
| **Save to EEPROM** | Sequencer item — persists the model to non-volatile memory |
| **Apply Model from File** | Sequencer item — reads coefficients from a JSON file and writes them directly to the mount |

---

## Requirements

- **N.I.N.A.** 3.2.0.9001 or later
- **OnStepX firmware** on the mount controller (tested with 10.25–10.26)
- **OnStep ASCOM telescope driver** connected and working in NINA
- **.NET 8.0 for Windows** (bundled with NINA)
- A camera and plate solver configured in NINA (for model-building)

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

> **Do not** copy OxyPlot, Newtonsoft.Json, or Autofac — they are already bundled with NINA 3.x.

---

## Quick Start

1. Connect your OnStep mount in NINA via the ASCOM telescope driver.
2. The **OnStepX Mount Status** and **Mount Settings** panels populate automatically when the telescope connects.
3. Open **OnStepX Model Builder**, pick a mode (Full-Sky or Star Alignment), choose an algorithm, and click **▶ Generate Points**.
4. Review the sky chart, adjust settings, then click **▶ Start Build** to run the build directly from the panel — or load the points into a sequencer item for unattended operation.
5. After the build, review residuals in the lower-left scatter plot and coefficients in the left column. Click **Write to Mount** when satisfied.

---

## Panels

### OnStepX Mount Status

Displays live telemetry from NINA's telescope mediator (`ITelescopeConsumer`) refreshed on every mount update:

- Equatorial position (RA/Dec) and horizontal position (Alt/Az)
- Sidereal time (LST), UTC, hours to meridian, time-to-flip
- Mount state: tracking on/off, tracking rate (Sidereal/Lunar/Solar/King), slewing, at-park, at-home, pier side, epoch
- Guide rates (RA and Dec in arcsec/s), alignment mode, driver info
- Site latitude, longitude, elevation

**Weather** (polled from OnStepX sensors every 30 s via LX200 commands):

| Sensor | Command |
|--------|---------|
| Ambient temperature | `:GX9A#` |
| Barometric pressure | `:GX9B#` |
| Relative humidity | `:GX9C#` |
| Dew point | `:GX9E#` |
| Controller temperature | `:GX9F#` |

---

### OnStepX Mount Settings

Reads all current mount runtime parameters on telescope connect and allows editing them. Organised into sections:

- **Site Location** — current lat/lon shown; **Sync from N.I.N.A.** button uploads NINA's profile coordinates to the mount
- **Tracking** — on/off, rate (Sidereal/Lunar/Solar/King), rate compensation, frequency adjust (±0.02 Hz, reset)
- **Guide Rate & Slew Speed** — 10-level guide rate (0.25× to VVF), slew speed (VSlow–VFast)
- **Meridian Flip & Park** — auto flip, pause at home, preferred pier side, trigger flip, set park position, buzzer
- **Backlash** — axis 1 and axis 2 in arcseconds (`:%BR#` / `:%BD#`)
- **Limits** — minimum/maximum altitude, degrees past meridian east/west

**Axis Configuration** (bottom of the same panel):

Reads axis motor configuration using the dynamic `:GXA{n},{i}#` parameter system.

**Firmware version detection** — the panel reads the firmware version from `TelescopeInfo.Description` and selects the appropriate command format:

| Version | GET command | SET command |
|---------|-------------|-------------|
| < 10.26 | `:GXA{n}#` — one composite string per axis | `:SXA{n},{composite}#` |
| ≥ 10.26 | `:GXA{n},0#` for count, `:GXA{n},{i}#` per parameter | `:SXA{n},{i},{v}#` |

For firmware ≥ 10.26, each parameter shows its name, current value, and valid range as reported by the firmware. Mount type (GEM / EQ Fork / Alt-Az) and controller reboot buttons are always available.

Commands use verified OnStepX LX200 syntax (SmartWebServer source cross-referenced).

---

### OnStepX Model Builder

The combined point-generation and model-management panel. The left column contains all controls; the right column shows the live sky chart.

#### Point Generation & Algorithm

**Modes:**
- *Full-Sky Pointing Model* — up to 300 points, any algorithm, 12-parameter least-squares solver
- *Star Alignment* — up to 9 stars, on-device controller solve

**Algorithms:**

| Algorithm | Description |
|-----------|-------------|
| **Golden Spiral** | Fibonacci lattice — uniform area density across the visible sky sector |
| **Auto Grid** | Equal-area latitude-weighted grid — automatically sizes altitude bands and azimuth steps |
| **Random** | Uniform area random — samples sin(alt) uniformly to avoid clustering near the zenith |
| **Sidereal Path** | HA/Dec grid — three declination bands (target ± Dec step) swept across a configurable hour angle range |

**Options:**
- **Meridian Exclusion Zone** — configurable half-width in degrees (pre-filled from NINA's meridian flip setting)
- **Altitude Range** — min/max altitude for point placement
- **Point Count** — slider with mode-appropriate tick frequency (steps of 1 for Star Alignment, 20 for Full-Sky)

All algorithms apply the custom NINA horizon filter and meridian exclusion, then optimise the slew order (East side first, then West, nearest-neighbour within each hemisphere).

Points can be saved to / loaded from `.json` files for reuse across sessions.

#### Direct Build

A **Build Settings** section below the generator allows running a build directly from the panel without a sequencer:

- **Exposure** — capture duration per pointing star (seconds)
- **Settle** — wait time after each slew (seconds)
- **▶ Start Build** — initiates the build using the current point list; requires telescope connected
- **■ Cancel** — aborts after the current point completes

The build uses the same `ModelBuilder` pipeline as the sequencer items, so session persistence, progress events, and mediator notifications all work identically.

#### Live Sky Chart (right column)

Azimuthal equidistant projection (`PlotType.Cartesian`, North up). During a build the chart updates after every point:

| Colour | Meaning |
|--------|---------|
| Cyan | Planned / pending |
| **Green** | Solved and added |
| Yellow | In progress (slewing, settling, exposing, plate-solving) |
| Red | Failed |
| Yellow arrow | Residual error vector (length scaled to longest error × arrow scale) |
| White crosshair | Live mount position (bullseye + arms) |
| Blue wedge | Meridian exclusion zone |
| Grey fill | Custom horizon (below = excluded) |

#### Model Coefficients & Actions (left column, after build)

When a model is available (from a build or loaded from file), the left column shows:

**12-coefficient table (2 × 6):**

| | Col 1 | Col 2 |
|-|-------|-------|
| Row 1 | ax1 — HA index error (″) | ax2 — Dec index error (″) |
| Row 2 | alt — polar altitude (″) | azm — polar azimuth (″) |
| Row 3 | do — Dec/HA orthogonality (″) | pd — polar Dec misalignment (″) |
| Row 4 | df — Dec flexure (″) | tf — tube flexure (″) |
| Row 5 | hcp — HA harmonic phase (°) | hca — HA harmonic amplitude (″) |
| Row 6 | dcp — Dec harmonic phase (°) | dca — Dec harmonic amplitude (″) |

**Arrow scale slider** — multiplies the auto-scaled residual error arrow lengths on the sky chart.

**Action buttons:**
- **Write to Mount** — uploads all 12 coefficients via `:SX0n,v#`
- **Save to EEPROM** — writes to mount then calls `:AW#`
- **Save Model…** / **Load Model…** — JSON file I/O
- **Force Activate** — sends `:SX09,2#` to force the controller to apply the model

#### Residuals Chart (bottom-left, square)

ΔRA vs ΔDec scatter plot with a Jet colour axis (total error magnitude) and a dashed RMS circle. Updates progressively after each solved point during a build.

---

## Sequencer Items

### OnStepX Full-Sky Pointing Model

Automated plate-solve based all-sky pointing model. Add to an Advanced Sequence after generating points.

**Options:**
- **Write model to mount on completion** — uploads solved coefficients via `:SX0n,v#`
- **Resume last session** — reloads the most recent interrupted session from disk; already-completed points are skipped
- **Save coefficients to file** — writes a `.json` file on completion (loadable via Load Model in the panel)
- **Load from file / Use generated points** — sets the point list

The build loop per point:
1. Convert Alt/Az → apparent RA/Dec (with atmospheric refraction using mount weather sensors)
2. Slew to apparent coordinates
3. Settle (configurable)
4. Read mount RA/Dec and LST
5. Capture image (NINA imaging mediator)
6. Plate solve (NINA configured solver, JNOW coordinates — J2000 result transformed to JNOW)
7. Compute pointing errors (ΔRA, ΔDec in arcseconds)
8. Save point to disk atomically
9. Notify Model Builder panel (sky chart and residuals update live)

On completion, the 12-parameter least-squares solver runs. The design matrix matches OnStepX `Align.hs.cpp` exactly so coefficients can be uploaded directly without conversion.

Sessions are stored in:
```
%APPDATA%\NINA\Plugins\OnStepX\ModelBuilds\{sessionId}.json
```

### OnStepX Star Alignment

Same workflow as Full-Sky Pointing Model but uses the on-device controller solver (1–9 stars). Each successfully solved point is uploaded to the controller via:

```
:SX0A,<actual HA arcsec>#
:SX0B,<actual Dec arcsec>#
:SX0C,<mount HA arcsec>#
:SX0D,<mount Dec arcsec>#
:SX0E,<pier side>#
:SX09,1#   (trigger solve)
```

**Options:** Save to EEPROM on completion.

### Clear Alignment Model

Erases the model from controller RAM:
```
:SX09,0#
```

### Save to EEPROM

Persists the current model to non-volatile memory:
```
:AW#
```

### Apply Model from File

Reads a `.json` coefficient file (saved by any build) and writes all 12 parameters directly to the mount via `:SX0n,v#`. Optionally also calls `:AW#` to persist to EEPROM.

---

## Plate Solving Notes

- Exposure time is read from `profileService.ActiveProfile.PlateSolveSettings.ExposureTime`
- The plugin uses NINA's `IPlateSolverFactory.GetPlateSolver(settings)` — whatever solver you have configured in NINA (ASTAP, Astrometry.net, ANSVR, etc.)
- Plate solve results are in J2000 epoch. The plugin converts to JNOW using `result.Coordinates.Transform(Epoch.JNOW)` before comparing with the mount's JNOW coordinates
- **No sync is performed** after solving — the plugin only reads the solved coordinates; the mount's pointing model is never modified by NINA's sync mechanism

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
- `dH = −errRA / cos(Dec)` — HA arcseconds, not RA on-sky arcseconds
- `ax1Cor` design-matrix coefficient is **−1** (errH = −ax1Cor + …)
- `ax2Cor`, `doCor`, and harmonics carry the **pier sign**
- `dfCor` uses the latitude-aware GEM formula (not the simplified fork formula)
- `tfCor` includes site latitude in both ΔH and ΔD

**Minimum points:** 6 (for 12 unknowns). Reliable polar alignment extraction requires 30+ points well distributed across both pier sides and all declination zones.

---

## Commands Reference (OnStepX LX200)

| Operation | GET | SET |
|-----------|-----|-----|
| Alignment star count | `:GX09#` | — |
| Clear model | — | `:SX09,0#` |
| Compute model (Star Align) | — | `:SX09,1#` |
| Force model activation | — | `:SX09,2#` |
| Persist to EEPROM | — | `:AW#` |
| Coefficient n (hex) | `:GX0n#` | `:SX0n,v#` |
| Backlash axis 1 | `:%BR#` | `:$BR{v}#` |
| Backlash axis 2 | `:%BD#` | `:$BD{v}#` |
| Min altitude | `:Gh#` | `:Sh{v}#` |
| Max altitude | `:Go#` | `:So{v}#` |
| Meridian E limit | `:GXE9#` | `:SXE9,{v}#` |
| Meridian W limit | `:GXEA#` | `:SXEA,{v}#` |
| Preferred pier side | `:GXE8#` | `:SXE8,{v}#` |
| Auto meridian flip | `:GXE6#` | `:SXE6,{v}#` |
| Pause at home | `:GXE7#` | `:SXE7,{v}#` |
| Axis parameters (≥10.26) | `:GXA{n},{i}#` | `:SXA{n},{i},{v}#` |
| Weather temp | `:GX9A#` | — |
| Weather pressure | `:GX9B#` | — |
| Weather humidity | `:GX9C#` | — |
| Weather dew point | `:GX9E#` | — |
| Controller temp | `:GX9F#` | — |
| Last error | `:GXE0#` | — |

---

## Session Persistence

Build sessions are written atomically (write to `.tmp`, rename) after every successfully solved point:

```
%APPDATA%\NINA\Plugins\OnStepX\ModelBuilds\{sessionId}.json
```

---

## Comparison with TPoint

### Pointing Model Capability

**TPoint** uses an open-ended term library — users can add or remove correction terms (typically 20–40 for a serious model). The model is built incrementally and can be re-fit without re-observing.

**This plugin** uses exactly the 12 parameters that OnStepX's `Align.hs.cpp` supports:

```
ax1Cor, ax2Cor, altCor, azmCor, doCor, pdCor,
dfCor,  tfCor,  hcp,    hca,    dcp,   dca
```

This ceiling is set by the mount firmware, not by this plugin.

**Gap:** TPoint is substantially more expressive. For a high-end mount it can model tube sag, fork flex, and periodic error harmonics at arbitrary frequency, simultaneously. OnStepX has two harmonics (one RA, one Dec) with pier-sign.

### Solver Robustness

**TPoint** has ~30 years of field refinement: iterative outlier rejection, conditioning warnings, per-point weighting.

**This plugin** uses a single-pass normal-equations solver with Tikhonov regularisation (λ = 1×10⁻⁶). During development several critical correctness bugs were found and fixed:
- Wrong epoch (J2000 vs JNOW) causing ~1300″ systematic Dec error
- Wrong sign on `ax1Cor` in the design matrix
- Missing pier-sign on `ax2Cor`, `doCor`, and harmonics
- Wrong `tfCor`/`dfCor` formulas (not matching `Align.hs.cpp`)
- `goodPoints` filter threshold 60× too tight for unaligned mounts

**Gap:** Significant. TPoint's solver is battle-tested. This solver has been exercised on a limited number of real datasets.

### Minimum Points & Conditioning

| | This plugin | TPoint |
|-|-------------|--------|
| Parameters | 12 | 6 – 40+ (user-selected) |
| Minimum points | 6 (barely overdetermined) | Typically 20–30 for stable results |
| Recommended | 30+ for stable 12-param fit | 50–100 for full model |

With fewer than ~30 points distributed across both pier sides and all declination zones, the individual parameters (especially polar alignment `altCor`/`azmCor`) are unreliable even if the overall RMS looks acceptable.

### Workflow Integration

**TPoint** is a standalone desktop application separate from imaging software.

**This plugin** is native inside NINA:
- Point generation, direct build trigger, live sky chart, and residual display are all in one panel
- The model is uploaded directly to the mount via `:SX0n,v#`
- Refraction uses actual mount weather sensors
- Resumable sessions via JSON files

**Advantage here:** Tighter integration and more convenient for OnStepX users already using NINA.

### Real-Time Correction

**This plugin** writes coefficients directly into OnStepX firmware. The mount's own `Align.hs.cpp` applies them on every goto/track update in real time — no external software required after upload.

### What This Plugin Does Well

- Deep OnStepX integration — coefficients go directly into the firmware in the correct format
- Native NINA sequencer workflow — no extra tools
- Correct atmospheric refraction using mount sensors
- Pier-side-aware design matrix (matching the firmware equations exactly)
- Live visual feedback during build (coloured points, growing residual plot, mount crosshair)
- Direct build launch from the panel (no sequence required)

### What TPoint Does Better

- Far more correction terms for complex mechanical behaviour
- Decades of solver validation and field testing
- Explicit conditioning warnings and sigma-clipping
- Works with many mount platforms, not just OnStepX
- Professional reporting (print-quality charts, statistics)

### Bottom Line

For an OnStepX mount used with NINA, this plugin is a practical and well-integrated solution. The 12-parameter model matches what the firmware can actually apply, so adding more terms would not improve pointing anyway.

However, **the solver is relatively new code** and real-world validation across diverse mount types and declination distributions is ongoing. For operators where pointing accuracy is mission-critical, TPoint remains the more extensively validated choice.
