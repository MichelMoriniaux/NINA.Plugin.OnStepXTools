# OnStepX Command Reference

This reference is derived from the current command handlers in the source tree of OnStepX as of e20a6b7, primarily the `*.command.cpp` files plus the shared command processor in `src/libApp/commands/ProcessCmds.cpp`.

Scope notes:

- This table summarizes the commands that are supported by the Ascom driver and should stay there vs the commands implemented in the different panes of the N.I.N.A. plugin:
- Ascom: this feature is provided by the Ascom *mount* Driver
- Implemented: this feature is implemented in the plugin
- TODO: This feature will eventually make it's way into the plugin
- Out of Scope: no plan to support this feature in the future

## Global Processor Commands

These commands live in the Ascom Driver

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:SBB#` | `1` then switches to 460800 baud | Set serial baud rate | Ascom |
| `:SBA#` | `1` then switches to 230400 baud | Set serial baud rate | Ascom |
| `:SB0#` | `1` then switches to 115200 baud | Set serial baud rate | Ascom |
| `:SB1#` | `1` then switches to 56700 baud | Set serial baud rate | Ascom |
| `:SB2#` | `1` then switches to 38400 baud | Set serial baud rate | Ascom |
| `:SB3#` | `1` then switches to 28800 baud | Set serial baud rate | Ascom |
| `:SB4#` | `1` then switches to 19200 baud | Set serial baud rate | Ascom |
| `:SB5#` | `1` then switches to 14400 baud | Set serial baud rate | Ascom |
| `:SB6#` | `1` then switches to 9600 baud | Set serial baud rate | Ascom |
| `:SB7#` | `1` then switches to 4800 baud | Set serial baud rate | Ascom |
| `:SB8#` | `1` then switches to 2400 baud | Set serial baud rate | Ascom |
| `:SB9#` | `1` then switches to 1200 baud | Set serial baud rate | Ascom |
| `:GE#` | `CC#` | Get last command error code | Ascom |
| `<0x06>` | `A`, `P`, or `CK_FAIL` style response | Special LX200 status query/checksum-fail path handled by the buffer/processor | Ascom |

## Core Telescope / Firmware / Environment

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:B+#` | none | Increase reticle brightness | out of scope |
| `:B-#` | none | Decrease reticle brightness | out of scope |
| `:ECtext#` | none | Echo text to debug output. `_` becomes space, trailing `&` becomes newline. | out of scope |
| `:ERESET#` | none | Reset the MCU | Implemeted |
| `:ENVRESET#` | text | Mark NV storage to be cleared on next boot | Implemented |
| `:ESPFLASH#` | `1`/none depending on build path | Put addon/ESP device into firmware flashing mode when supported |  out of scope: flashing firmware is dangerous and requires direct connection through USB to the mount |
| `:GVD#` | `MTH DD YYYY#` | Firmware build date | TODO |
| `:GVM#` | `name version#` | Firmware name plus version | Ascom |
| `:GVN#` | `M.mm...#` | Firmware version number | out of scope |
| `:GVP#` | `name#` | Product name | out of scope |
| `:GVT#` | `HH:MM:SS#` | Firmware build time | out of scope |
| `:GVC#` | `string#` | Firmware config/product description | out of scope |
| `:GVH#` | `string#` | Firmware hardware/pinmap string | Maybe |
| `:GX9A#` | `sn.n#` | Ambient temperature in C | Implemeted |
| `:GX9B#` | `n.n#` | Pressure in mbar | Implemeted |
| `:GX9C#` | `n.n#` | Relative humidity in percent | Implemeted |
| `:GX9E#` | `sn.n#` | Dew point in C | Implemeted |
| `:GX9F#` | `n#` or `0` | MCU temperature in C | TODO |
| `:SX9A,sn.n#` | `0/1` | Set ambient temperature | TODO: plugin uses the values provided by the mount and fallback on N.I.N.A weather, this would be useful to set the refraction model if there is no sensor |
| `:SX9B,n.n#` | `0/1` | Set pressure | TODO: plugin uses the values provided by the mount and fallback on N.I.N.A weather, this would be useful to set the refraction model if there is no sensor |
| `:SX9C,n.n#` | `0/1` | Set humidity | TODO: plugin uses the values provided by the mount and fallback on N.I.N.A weather, this would be useful to set the refraction model if there is no sensor |

## Time / Date / Site

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:Ga#` | `HH:MM:SS#` | Local standard time, 12-hour format | Ascom |
| `:GC#` | `MM/DD/YY#` | Local standard calendar date | Ascom |
| `:Gc#` | `24#` | Local time format indicator |  |
| `:GG#` | `sHH:MM#` | UTC offset to add to local time to get UT1 |
| `:Gg#` | `sDDD*MM#` | Current longitude | Ascom |
| `:GgH#` | `sDDD*MM:SS.SSS#` | Longitude, highest precision | Ascom |
| `:GL#` | `HH:MM:SS#` | Local standard time, 24-hour format | Ascom |
| `:GLH#` | `HH:MM:SS.SSSS#` | Local standard time, highest precision | Ascom |
| `:GM#` | `string#` | Site 1 name | out of scope: N.I.N.A. location is authoritative |
| `:GN#` | `string#` | Site 2 name | out of scope |
| `:GO#` | `string#` | Site 3 name | out of scope |
| `:GP#` | `string#` | Site 4 name | out of scope |
| `:GS#` | `HH:MM:SS#` | Sidereal time | Ascom |
| `:GSH#` | `HH:MM:SS.ss#` | Sidereal time, highest precision | Ascom |
| `:Gt#` | `sDD*MM#` | Current latitude | Ascom |
| `:GtH#` | `sDD*MM:SS.SSS#` | Latitude, highest precision | Implemented |
| `:Gv#` | `sn.n#` | Elevation in meters | Ascom |
| `:GX80#` | `HH:MM:SS.ss#` | UT1 time | Ascom |
| `:GX81#` | `MM/DD/YY#` | UT1 date | Ascom |
| `:GX89#` | `0` or `1` | Date/time ready status. `0` means ready, `1` means not ready. | Ascom |
| `:SCMM/DD/YY#` | `0/1` | Set local date | Ascom |
| `:SCMM/DD/YYYY#` | `0/1` | Set local date | Ascom |
| `:SGsHH#` | `0/1` | Set UTC offset | Ascom |
| `:SGsHH:MM#` | `0/1` | Set UTC offset. Comments indicate `MM` should be `00`, `30`, or `45`. | Ascom |
| `:Sg(s)DDD*MM#` | `0/1` | Set longitude | Ascom |
| `:Sg(s)DDD*MM:SS#` | `0/1` | Set longitude | Ascom |
| `:Sg(s)DDD*MM:SS.SSS#` | `0/1` | Set longitude | Ascom |
| `:SLHH:MM:SS#` | `0/1` | Set local time | Ascom |
| `:SLHH:MM:SS.SSS#` | `0/1` | Set local time | Ascom |
| `:SMname#` | `0/1` | Set site 1 name, max 15 chars | out of scope |
| `:SNname#` | `0/1` | Set site 2 name, max 15 chars | out of scope |
| `:SOname#` | `0/1` | Set site 3 name, max 15 chars | out of scope |
| `:SPname#` | `0/1` | Set site 4 name, max 15 chars | out of scope |
| `:StsDD*MM#` | `0/1` | Set latitude | Ascom |
| `:StsDD*MM:SS#` | `0/1` | Set latitude | Ascom |
| `:StsDD*MM:SS.SSS#` | `0/1` | Set latitude | Ascom |
| `:SUs.s#` | `0/1` | Set DUT1 correction in seconds, nominally `-0.9` to `+0.9` | |
| `:Svsn.n#` | `0/1` | Set elevation in meters | Ascom |
| `:W0#` .. `:W3#` | none | Select active site slot | out of scope |
| `:W?#` | `n#` | Query active site slot | out of scope |

## Mount Position / Tracking / Rates

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:GA#` | `sDD*MM#` or `sDD*MM'SS#` | Current mount altitude | Ascom |
| `:GAH#` | `sDD*MM'SS.SSS#` | Current mount altitude, highest precision | Ascom |
| `:GD#` | `sDD*MM#` or `sDD*MM:SS#` | Current declination | Ascom |
| `:GDH#` | `sDD*MM:SS.SSS#` | Current declination, highest precision | Ascom |
| `:GR#` | `HH:MM.T#` or `HH:MM:SS#` | Current right ascension | Ascom |
| `:GRH#` | `HH:MM:SS.SSSS#` | Current right ascension, highest precision | Ascom |
| `:GT#` | `n.nnnnn#` or `0#` | Tracking rate in Hz, `0` if not tracking | Ascom |
| `:GZ#` | `DDD*MM#` or `DDD*MM'SS#` | Current azimuth | Ascom |
| `:GZH#` | `DDD*MM'SS.SSS#` | Current azimuth, highest precision | Ascom |
| `:GXTD#` | `n.nnnnnnnn#` | Tracking rate offset, Dec, arcsec per sidereal second | out of scope |
| `:GXTR#` | `n.nnnnnnnn#` | Tracking rate offset, RA, arcsec per sidereal second | out of scope |
| `:GX40#` | `DDD*MM:SS#` | Axis1 instrument angle | out of scope |
| `:GX41#` | `DDD*MM:SS#` | Axis2 instrument angle | out of scope |
| `:GX42#` | `n.nnnnnn#` | Axis1 instrument angle in decimal degrees | out of scope |
| `:GX43#` | `n.nnnnnn#` | Axis2 instrument angle in decimal degrees | out of scope |
| `:GX44#` | `n#` | Axis1 encoder count | out of scope |
| `:GX45#` | `n#` | Axis2 encoder count | out of scope |
| `:GXE4#` | `n#` | Axis1 steps per degree | out of scope |
| `:GXE5#` | `n#` | Axis2 steps per degree | out of scope |
| `:GXEE#` | `0#`, `1#`, ... | Mount coordinate mode (`MOUNT_COORDS - 1`) | out of scope |
| `:GXEF#` | `1` or `0` | Axis2 tangent-arm presence flag | out of scope |
| `:GXEG#` | `1` or `0` | Axis1 sector-gear presence flag | out of scope |
| `:GXEM#` | `n#` | Current mount type | Implemented |
| `:GXF3#` | `sn.nnnnnn#` | Axis1 step frequency | out of scope |
| `:GXF4#` | `sn.nnnnnn#` | Axis2 step frequency | out of scope |
| `:GXFA#` | `50%#` | Reported workload placeholder | out of scope |
| `:GXFF#` | `n.nnnnnn#` | Axis1 index position | out of scope |
| `:GXFG#` | `n.nnnnnn#` | Axis2 index position | out of scope |
| `:STn.n#` | `0/1` | Set tracking rate in Hz. `0` stops tracking. | out of scope |
| `:SEO#` | `0/1` | Save absolute encoder origin, or initialize mount coordinate memory at home, when supported | Implemented |
| `:SX40,n#` | `0/1` | Stage encoder axis1 angle in degrees | out of scope |
| `:SX41,n#` | `0/1` | Stage encoder axis2 angle in degrees | out of scope |
| `:SX42,1#` | `0/1` | Sync mount from staged encoder axis values | out of scope |
| `:SX43,0#` | `0/1` | Allow SWS to control sync mode | out of scope |
| `:SX44,deg1,deg2[a]#` | `0/1` | Stage and sync both encoder axes, append `a` when both SWS encoder values are absolute and trusted | out of scope |
| `:GXSGn#` | `sg,trip,badMs,armed,latched#` | Live StallGuard telemetry for axis `n`, when supported | out of scope |
| `:SXEM,n#` | `0/1` | Set mount type for next restart | Implemented |
| `:SXTD,n.n#` | `0/1` | Set Dec tracking rate offset, arcsec per sidereal second | out of scope |
| `:SXTR,n.n#` | `0/1` | Set RA tracking rate offset, arcsec per sidereal second | out of scope |
| `:TS#` | none | Solar tracking rate | Implemented, should be removed as provided by Ascom |
| `:TK#` | none | King tracking rate | Implemented, should be removed as provided by Ascom |
| `:TL#` | none | Lunar tracking rate | Implemented, should be removed as provided by Ascom |
| `:TQ#` | none | Sidereal tracking rate | Implemented, should be removed as provided by Ascom |
| `:T+#` | none | Increase master sidereal clock by 0.02 Hz | Implemented |
| `:T-#` | none | Decrease master sidereal clock by 0.02 Hz | Implemented |
| `:TR#` | none | Reset master sidereal clock | Implemented |
| `:Te#` | `0/1` | Enable tracking | Ascom |
| `:Td#` | `0/1` | Disable tracking | Ascom |
| `:To#` | `0/1` | Enable full compensation model | Implemented |
| `:Tr#` | `0/1` | Enable refraction compensation | Implemented |
| `:Tn#` | `0/1` | Disable compensation | Implemented |
| `:T1#` | `0/1` | Single-axis tracking mode | Implemented |
| `:T2#` | `0/1` | Dual-axis tracking mode | Implemented |
| `:$BDn#` | `0/1` | Set Dec/Alt backlash in arcsec | Implemented |
| `:$BRn#` | `0/1` | Set RA/Azm backlash in arcsec | Implemented |
| `:%BD#` | `n#` | Get Dec/Alt backlash in arcsec | Implemented |
| `:%BR#` | `n#` | Get RA/Azm backlash in arcsec | Implemented |

## Goto / Sync / Alignment

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:AW#` | `1` | Write alignment model to NV | Implemented |
| `:A?#` | `mno#` | Alignment status: max stars, current star, last required star | out of scope |
| `:A1#` .. `:A9#` | `0/1` | Start manual alignment with that many stars | out of scope |
| `:A+#` | `0/1` | Accept current align point | out of scope |
| `:CS#` | none | Sync to current target coordinates | Ascom |
| `:CM#` | `N/A#` or `E1#`..`E9#` | Sync to current catalog/database object | out of scope |
| `:D#` | `0x7f#` while moving, otherwise raw `#` | LX200 distance-bar style motion indicator | out of scope |
| `:Gr#` | `HH:MM.T#` or `HH:MM:SS#` | Get target RA | Ascom |
| `:GrH#` | `HH:MM:SS.SSSS#` | Get target RA, highest precision | Ascom |
| `:Gd#` | `sDD*MM#` or `sDD*MM:SS#` | Get target Dec | Ascom |
| `:GdH#` | `sDD*MM:SS.SSS#` | Get target Dec, highest precision | Ascom |
| `:Gal#` | `sDD*MM#` or `sDD*MM'SS#` | Get target altitude | Ascom |
| `:GaH#` | `sDD*MM'SS.SSS#` | Get target altitude, highest precision | Ascom |
| `:Gz#` | `DDD*MM#` or `DDD*MM'SS#` | Get target azimuth | Ascom |
| `:GzH#` | `DDD*MM'SS.SSS#` | Get target azimuth, highest precision | Ascom |
| `:MA#` | `0`..`9` | Goto target Alt/Az | Ascom |
| `:MD#` | `0`, `1`, or `2` | Destination pier side for current target | out of scope |
| `:MN#` | `0`..`9` | Goto current position on opposite pier side | out of scope |
| `:MNe#` | `0`..`9` | Force same-position goto to east pier side | out of scope |
| `:MNw#` | `0`..`9` | Force same-position goto to west pier side | out of scope |
| `:MP#` | `0`..`9` | Polar-align goto | out of scope |
| `:MS#` | `0`..`9` | Goto current target | Ascom |
| `:SrHH:MM.T#` | `0/1` | Set target RA | Ascom |
| `:SrHH:MM:SS#` | `0/1` | Set target RA | Ascom |
| `:SrHH:MM:SS.SSSS#` | `0/1` | Set target RA | Ascom |
| `:SdsDD*MM#` | `0/1` | Set target Dec | Ascom |
| `:SdsDD*MM:SS#` | `0/1` | Set target Dec | Ascom |
| `:SdsDD*MM:SS.SSS#` | `0/1` | Set target Dec | Ascom |
| `:SasDD*MM#` | `0/1` | Set target altitude | Ascom |
| `:SasDD*MM'SS#` | `0/1` | Set target altitude | Ascom |
| `:SasDD*MM'SS.SSS#` | `0/1` | Set target altitude | Ascom |
| `:SzDDD*MM#` | `0/1` | Set target azimuth | Ascom |
| `:SzDDD*MM'SS#` | `0/1` | Set target azimuth | Ascom |
| `:SzDDD*MM'SS.SSS#` | `0/1` | Set target azimuth | Ascom |

### Alignment Model Extended Commands

`ALIGN_MAX_NUM_STARS > 1` builds expose:

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:GX00#` | `n#` | `ax1Cor` in arcsec | Implemented |
| `:GX01#` | `n#` | `ax2Cor` in arcsec | Implemented |
| `:GX02#` | `n#` | `altCor` in arcsec | Implemented |
| `:GX03#` | `n#` | `azmCor` in arcsec | Implemented |
| `:GX04#` | `n#` | `doCor` in arcsec | Implemented |
| `:GX05#` | `n#` | `pdCor` in arcsec | Implemented |
| `:GX06#` | `n#` | `ffCor` for FORK/ALTAZM, else `0` | Implemented |
| `:GX07#` | `n#` | `dfCor` for GEM-style mounts, else `0` | Implemented |
| `:GX08#` | `n#` | `tfCor` in arcsec | Implemented |
| `:GX0a#` | `n#` | `hcp` in degrees | Implemented |
| `:GX0b#` | `n#` | `hca` in arcsec | Implemented |
| `:GX0c#` | `n#` | `dcp` in degrees | Implemented |
| `:GX0d#` | `n#` | `dca` in arcsec | Implemented |
| `:GX09#` | `n#` | Number of uploaded stars, then resets internal star index | Implemented |
| `:GX0A#` | `HH:MM:SS#` | Uploaded star actual HA | Implemented |
| `:GX0B#` | `sDD*MM:SS#` | Uploaded star actual Dec | Implemented |
| `:GX0C#` | `HH:MM:SS#` | Uploaded star mount HA | Implemented |
| `:GX0D#` | `sDD*MM:SS#` | Uploaded star mount Dec | Implemented |
| `:GX0E#` | `n#` | Uploaded star pier side, then advances star index | Out Of Scope |
| `:SX00,n#` .. `:SX0d,n#` | `0/1` | Set alignment model coefficients listed above | Implemented |
| `:SX09,0#` | `0/1` | Reset alignment upload state | Implemented |
| `:SX09,1#` | `0/1` | Build model from uploaded stars | Implemented |
| `:SX09,2#` | `0/1` | Force model active | Implemented |
| `:SX0A,HH:MM:SS#` | `0/1` | Upload actual HA for current star | Implemented |
| `:SX0B,sDD*MM:SS#` | `0/1` | Upload actual Dec for current star | Implemented |
| `:SX0C,HH:MM:SS#` | `0/1` | Upload mount HA for current star | Implemented |
| `:SX0D,sDD*MM:SS#` | `0/1` | Upload mount Dec for current star | Implemented |
| `:SX0E,n#` | `0/1` | Upload pier side and advance to next star | Implemented |

### Goto Extended Settings

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:GX92#` | `n.nnn#` | Current slew period in us/step |
| `:GX93#` | `n.nnn#` | Base/default slew period in us/step |
| `:GX94#` | `n` plus optional ` N` | Current pier side: `0` none, `1` east, `2` west | Ascom |
| `:GX95#` | `0` or `1` | Auto meridian flip enabled | Implemented |
| `:GX96#` | `E`, `W`, `B`, or `A` | Preferred pier side | Implemented |
| `:GX97#` | `n.n#` | Current step rate in deg/s | TODO |
| `:GX99#` | `n.nnn#` | Fastest allowed slew period in us/step | TODO |
| `:SX92,n.nnn#` | `0/1` | Set current slew period in us/step | TODO |
| `:SX93,[1-5]#` | none | Slew preset: `5`=50%, `4`=66.7%, `3`=100%, `2`=150%, `1`=200% | Implemented |
| `:SX95,0#` or `:SX95,1#` | `0/1` | Disable/enable automatic meridian flip | Implemented |
| `:SX96,E#` | `0/1` | Preferred pier side east | Implemented |
| `:SX96,W#` | `0/1` | Preferred pier side west | Implemented |
| `:SX96,B#` | `0/1` | Preferred pier side best | Implemented |
| `:SX96,A#` | `0/1` | Preferred pier side automatic | Implemented |
| `:SX98,0#` or `:SX98,1#` | `0/1` | Disable/enable pause at home during meridian flip | Implemented |
| `:SX99,1#` | `0/1` | Continue after pause at home | Implemented |

## Guide / Manual Motion

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:GX90#` | `n.nn#` | Pulse guide rate |
| `:Mgdn#` | none | Pulse guide for `n` ms in direction `d` where `d` is `w`, `e`, `n`, or `s` |
| `:MGdn#` | `0/1` | Same as `:Mgdn#`, numeric form |
| `:Mw#` | none | Move west at current guide rate |
| `:Me#` | none | Move east at current guide rate |
| `:Mn#` | none | Move north at current guide rate |
| `:Ms#` | none | Move south at current guide rate |
| `:Mp#` | none | Spiral-search motion |
| `:Q#` | none | Stop all slews, abort goto |
| `:Qe#` / `:Qw#` | none | Stop east/west motion |
| `:Qn#` / `:Qs#` | none | Stop north/south motion |
| `:RAn.n#` | none | Set axis1 custom guide rate in deg/s |
| `:REn.n#` | none | Set axis2 custom guide rate in deg/s |
| `:RG#` | none | Guide rate preset 1x |
| `:RC#` | none | Centering rate preset 8x |
| `:RM#` | none | Find rate preset 20x |
| `:RF#` | none | Fast rate preset 48x |
| `:RS#` | none | Slew rate preset, half current goto rate |
| `:R0#` .. `:R9#` | none | Numeric guide-rate preset |

## Park / Home / Limits / Status

### Park

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:hP#` | `0/1` | Park mount | Ascom |
| `:hQ#` | `0/1` | Set current position as park | Implemented |
| `:hR#` | `0/1` | Unpark mount | Ascom |

### Home

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:h?#` | `hasSense,axis1Offset,axis2Offset#` | Home status. Source comment mentions auto-home, but current code returns 3 fields only. |
| `:hA0#` / `:hA1#` | none | Disable/enable automatic home at boot |
| `:hC#` | none | Move to home | Ascom |
| `:hC1,R#` | none | Toggle axis1 home-sense reversal |
| `:hC1,n#` | none | Set axis1 home offset in arcsec |
| `:hC2,R#` | none | Toggle axis2 home-sense reversal |
| `:hC2,n#` | none | Set axis2 home offset in arcsec |
| `:hF#` | none | Reset mount at home/cold-start position | 

### Limits

For a visual explanation of confusing GEM east/west pier-side limit geometry,
see [GOTO_NOTES.md](GOTO_NOTES.md#workflow-4-reachability-and-target-unwinding).

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:Gh#` | `sDD*#` | Horizon limit |
| `:Go#` | `DD*#` | Overhead limit |
| `:GXE9#` | `n#` | East meridian limit in minutes |
| `:GXEA#` | `n#` | West meridian limit in minutes |
| `:GXEe#` | `n#` | Axis1 minimum limit in degrees |
| `:GXEw#` | `n#` | Axis1 maximum limit in degrees |
| `:GXEB#` | `n#` | Axis1 maximum limit in hours |
| `:GXEC#` | `n#` | Axis2 minimum limit in degrees |
| `:GXED#` | `n#` | Axis2 maximum limit in degrees |
| `:ShsDD#` | `0/1` | Set lower altitude limit |
| `:SoDD#` | `0/1` | Set overhead altitude limit |
| `:SXE9,n#` | `0/1` | Set east meridian limit in minutes |
| `:SXEA,n#` | `0/1` | Set west meridian limit in minutes |

### Status

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:Gm#` | `E#`, `W#`, or `N#` | Meridian pier side |
| `:GU#` | status string | Human-readable packed status |
| `:Gu#` | packed bytes | Bit-packed status |
| `:GW#` | 4-char status | Mount type, tracking, parked/home, align-done |
| `:SX97,0#` | `0/1` | Disable buzzer | Implemented |
| `:SX97,1#` | `0/1` | Enable buzzer | Implemented |
| `:SX97,2#` | `0/1` | Beep | TODO |
| `:SX97,3#` | `0/1` | Alert tone | TODO |
| `:SX97,4#` | `0/1` | Click | TODO |

#### `:GU#` Status Characters

The reply is an ordered string assembled from active conditions. Characters currently used by the code include:

| Char | Meaning | Plugin status |
| --- | --- | --- |
| `n` | Not tracking | Implemented |
| `N` | No goto active |
| `p` | Not parked |
| `I` | Parking in progress |
| `P` | Parked |
| `F` | Park failed |
| `e` | Sync-to-encoders-only mode |
| `H` | At home |
| `h` | Homing |
| `B` | Auto-home at boot |
| `S` | PPS synced |
| `G` | Pulse guide active |
| `g` | Guide active |
| `r` | Refraction compensation enabled | Implemented |
| `s` | Single-axis compensation mode | Implemented |
| `t` | Full compensation model enabled | Implemented |
| `(` | Lunar tracking rate selected | Implemented |
| `O` | Solar tracking rate selected | Implemented |
| `k` | King tracking rate selected | Implemented |
| `w` | Meridian flip paused at home |
| `u` | Pause-at-home enabled | Implemented |
| `z` | Buzzer enabled | Implemented |
| `a` | Automatic meridian flip enabled | Implemented |
| `R` | PEC data recorded |
| `/`, `,`, `~`, `;`, `^` | PEC state |
| `E`, `K`, `A`, `L` | GEM, FORK, ALTAZM, ALTALT |
| `o`, `T`, `W` | Pier side none, east, west |
| final digits | pulse-guide rate, guide rate, general error code |

#### `:Gu#` Packed Status Layout

`Gu` is the binary/pseudo-binary status form. The current implementation fills bytes as:

| Byte | Contents | Plugin status |
| --- | --- | --- |
| `0` | tracking/goto/PPS/pulse-guide plus compensation mode |
| `1` | tracking-rate selection plus sync-to-encoders and guide-active |
| `2` | home/homing/auto-home/home-pause/buzzer/auto-flip |
| `3` | mount type plus pier side |
| `4` | PEC state and PEC-recorded flag |
| `5` | park state |
| `6` | pulse-guide selection |
| `7` | guide-rate selection |
| `8` | general limits/error code |

## Object Library
These commands are out of scope, they are intended for standalone operation. As we are running in N.I.N.A. we already have access to the caatalog and the framing assistant.

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:LB#` | none | Previous object matching current constraints | out of scope |
| `:LCn#` | none | Select record number `n` | out of scope |
| `:LI#` | `name,type#` | Current object name and type | out of scope |
| `:LIG#` | none | Load current object into goto target | out of scope |
| `:LR#` | `name,type,ra,dec#` | Current object info, then advance to next record | out of scope |
| `:LWname,type#` | `0/1` | Write current target RA/Dec to next free record | out of scope |
| `:LN#` | none | Next object matching current constraints | out of scope |
| `:L$#` | `1` | Move to catalog name record | out of scope |
| `:LD#` | none | Clear current record | out of scope |
| `:LL#` | none | Clear current catalog | out of scope |
| `:L!#` | none | Clear all catalogs | out of scope |
| `:L?#` | `n#` | Free records across all catalogs | out of scope |
| `:Lon#` | `0/1` | Select catalog `0..14` | out of scope |

Object type codes written/read by library commands are:

`UNK`, `OC`, `GC`, `PN`, `DN`, `SG`, `EG`, `IG`, `KNT`, `SNR`, `GAL`, `CN`, `STR`, `PLA`, `CMT`, `AST`

## PEC

Available when PEC support is enabled for axis 1.

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:GX91#` | `n#` | PEC analog value | TODO |
| `:GXE6#` | `n.nnnnnn#` | Steps per sidereal second | TODO |
| `:GXE7#` | `n#` | Worm rotation steps from NV | TODO |
| `:GXE8#` | `n#` | PEC buffer size in seconds | TODO |
| `:SXE7,n#` | `0/1` | Set worm rotation steps | TODO |
| `:VH#` | `nnnnn#` | PEC index sense position in sidereal seconds | TODO |
| `:VR#` | `snnn,nnn#` | Current PEC segment correction plus segment index | TODO |
| `:VRn#` | `snnn#` | PEC correction for segment `n` | TODO |
| `:Vrn#` | `x0x1...x9#` | Ten-byte hex frame of PEC data starting at segment `n` | TODO |
| `:VS#` | `n.nnnnnn#` | Steps per sidereal second of worm rotation | TODO |
| `:VW#` | `nnnnnn#` | Worm rotation steps | TODO |
| `:WR+#` | `0/1` | Rotate PEC table forward one second | TODO |
| `:WR-#` | `0/1` | Rotate PEC table backward one second | TODO |
| `:WRn,sn#` | none | Write PEC correction for segment `n` | TODO |
| `:$QZ+#` | none | Enable PEC playback | TODO |
| `:$QZ-#` | none | Disable PEC | TODO |
| `:$QZ/#` | none | Arm PEC recording | TODO |
| `:$QZZ#` | none | Clear PEC buffer | TODO |
| `:$QZ!#` | none | Save PEC data to NV | TODO |
| `:$QZ?#` | `I#`, `p#`, `P#`, `r#`, `R#`, optionally with `.#` | PEC status | TODO |

PEC status characters from `:$QZ?#`:

| Char | Meaning | Plugin status |
| --- | --- | --- |
| `I` | Ignore/off |
| `p` | Ready to play |
| `P` | Playing |
| `r` | Ready to record |
| `R` | Recording |
| `.` | Index detected this second |

### `:GXUa#` Driver Status Flags

Local ASCII replies are comma-separated flag mnemonics:

| Flag | Meaning | Plugin status |
| --- | --- | --- |
| `ST` | Standstill |
| `OA` | Output A open load |
| `OB` | Output B open load |
| `GA` | Output A short to ground |
| `GB` | Output B short to ground |
| `OT` | Over-temperature |
| `PW` | Over-temperature warning |
| `GF` | Driver fault |

## Focuser

Available in focuser-enabled or remote-focuser builds.

Addressing rules:

- `:FA#` returns the currently selected focuser number.
- `:FA1#` .. `:FA6#` selects the active focuser.
- `:F...#` uses the active focuser.
- `:F1...#` .. `:F6...#` directs a command to a specific focuser immediately.

Unit rules for local ASCII focuser commands:

- Uppercase `B`, `D`, `G`, `I`, `M`, `R`, `S` use microns.
- Lowercase `b`, `d`, `g`, `i`, `m`, `r`, `s` use raw steps.
- Other focuser commands are unitless or fixed-unit as noted below.

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:FA#` | `n` | Get active focuser number | out of scope |
| `:FA1#` .. `:FA6#` | `0/1` | Select active focuser | out of scope |
| `:hP#` | `0/1` | Park all focusers in standalone/remote focuser builds | out of scope |
| `:hR#` | `0/1` | Unpark all focusers in standalone/remote focuser builds | out of scope |
| `:Fa#` | `1` | Primary focuser present/selected | out of scope |
| `:FT#` | `M1#`, `S3#`, etc. | Focuser status plus goto-rate digit | out of scope |
| `:Fp#` | `0` or `1` | Mode. Current implementation returns `1` for DC/pseudo-absolute, `0` otherwise. | out of scope |
| `:FI#` / `:Fi#` | `n#` | Full-in/min position | out of scope |
| `:FM#` / `:Fm#` | `n#` | Maximum position | out of scope |
| `:Fe#` | `n.n#` | Temperature delta from TCF baseline | out of scope |
| `:Ft#` | `n.n#` | Focuser temperature in C | out of scope |
| `:Fu#` | `n.nnnnn#` | Microns per step | out of scope |
| `:FB#` / `:Fb#` | `n#` | Backlash | out of scope |
| `:FBn#` / `:Fbn#` | `0/1` | Set backlash | out of scope |
| `:FC#` | `n.nnnnn#` | TCF coefficient in microns per C | out of scope |
| `:FCsn.n#` | `0/1` | Set TCF coefficient | out of scope |
| `:Fc#` | `0` or `1` | TCF enabled status | out of scope |
| `:Fc0#` / `:Fc1#` | `0/1` | Disable/enable TCF | out of scope |
| `:FD#` / `:Fd#` | `n#` | TCF deadband | out of scope |
| `:FDn#` / `:Fdn#` | `0/1` | Set TCF deadband | out of scope |
| `:FP#` | `n#` | DC motor power percent | out of scope |
| `:FPn#` | `0/1` | Set DC motor power percent | out of scope |
| `:FQ#` | none | Stop focuser | out of scope |
| `:F1#` .. `:F9#` | none | Set move/goto rate preset | out of scope |
| `:FW#` | `n#` | Working goto rate in um/s | out of scope |
| `:F+#` | none | Move inward | out of scope |
| `:F-#` | none | Move outward | out of scope |
| `:FG#` / `:Fg#` | `sn#` | Current position | out of scope |
| `:FRsn#` / `:Frsn#` | none | Relative goto | out of scope |
| `:FSn#` / `:Fsn#` | `0/1` | Absolute goto | out of scope |
| `:FZ#` | none | Zero current position | out of scope |
| `:FH#` | none | Set current position as home | out of scope |
| `:Fh#` | none | Move to home | out of scope |
| `:GXU4#` .. `:GXU9#` | flags | Driver status for focuser axes that expose `Axis.command.cpp` | out of scope |

Rate preset meaning:

| Preset | Meaning | Plugin status |
| --- | --- | --- |
| `1` | 1 um/s move rate | out of scope |
| `2` | 10 um/s move rate | out of scope |
| `3` | 100 um/s move rate | out of scope |
| `4` | 0.5x goto rate, move mode | out of scope |
| `5` | 0.5x goto rate | out of scope |
| `6` | 0.66x goto rate | out of scope |
| `7` | 1x goto rate | out of scope |
| `8` | 1.5x goto rate | out of scope |
| `9` | 2x goto rate | out of scope |

## Rotator

Available in rotator-enabled or remote-rotator builds.

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:rA#` | `0/1` | Rotator active | out of scope |
| `:hP#` | `0/1` | Park rotator in standalone/remote rotator builds | out of scope |
| `:hR#` | `0/1` | Unpark rotator in standalone/remote rotator builds | out of scope |
| `:rT#` | `M1#`, `SD3#`, etc. | Status string plus rate digit | out of scope |
| `:rI#` | `n#` | Minimum angle in degrees | out of scope |
| `:rM#` | `n#` | Maximum angle in degrees | out of scope |
| `:rD#` | `n.n#` | Degrees per step | out of scope |
| `:rb#` | `n#` | Backlash in steps | out of scope |
| `:rbn#` | `0/1` | Set backlash | out of scope |
| `:rQ#` | none | Stop motion | out of scope |
| `:r1#` .. `:r9#` | none | Set move/goto rate preset | out of scope |
| `:rW#` | `n.n#` | Working slew rate in deg/s | out of scope |
| `:rc#` | none | Continuous-move no-op, accepted for compatibility | out of scope |
| `:r>#` | none | Move clockwise | out of scope |
| `:r<#` | none | Move counter-clockwise | out of scope |
| `:rG#` | `sDDD*MM#` | Current angle | out of scope |
| `:rrsDDD*MM#` | none | Relative goto | out of scope |
| `:rSsDDD*MM#` | `0/1` | Absolute goto | out of scope |
| `:rZ#` | none | Zero position | out of scope |
| `:rF#` | none | Set current position to half travel | out of scope |
| `:rC#` | none | Move to half-travel / home target | out of scope |
| `:r+#` | none | Enable derotation | out of scope |
| `:r-#` | none | Disable derotation | out of scope |
| `:rP#` | none | Move to parallactic angle | out of scope |
| `:rR#` | none | Toggle derotator reverse direction | out of scope |
| `:GX98#` | `D#`, `R#`, or `N#` | Rotator capability: derotate, rotate-only, or none | out of scope |
| `:GXU3#` | flags | Driver status for rotator axis in standalone/remote builds | out of scope |

Rotator rate presets:

| Preset | Meaning | Plugin status |
| --- | --- | --- |
| `1` | 0.01 deg/s move rate | out of scope |
| `2` | 0.1 deg/s move rate | out of scope |
| `3` | 1.0 deg/s move rate | out of scope |
| `4` | 0.5x goto rate used as move rate | out of scope |
| `5` | 0.5x goto rate | out of scope |
| `6` | 0.66x goto rate | out of scope |
| `7` | 1x goto rate | out of scope |
| `8` | 1.5x goto rate | out of scope |
| `9` | 2x goto rate | out of scope |

## Auxiliary Features

Available when auxiliary features are enabled.

Feature slots are numbered `1..8`.

### Discovery

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:GXY0#` | `xxxxxxxx#` | Eight-character bitmap of active feature slots, `1` = present | out of scope |
| `:GXYn#` | `name,purpose#` | Slot name plus purpose code | out of scope |

### Slot State

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:GXXn#` | purpose-specific payload | Get current state for slot `n` | out of scope |
| `:SXXn,Vv#` | `0/1` | Generic value set | out of scope |
| `:SXXn,Zf#` | `0/1` | Dew-heater zero temperature | out of scope |
| `:SXXn,Sf#` | `0/1` | Dew-heater span temperature | out of scope |
| `:SXXn,Ef#` | `0/1` | Intervalometer exposure seconds | out of scope |
| `:SXXn,Df#` | `0/1` | Intervalometer delay seconds | out of scope |
| `:SXXn,Cf#` | `0/1` | Intervalometer count | out of scope |

### `:GXXn#` Reply Shapes

The payload depends on the slot purpose:

| Purpose | Reply shape | Plugin status |
| --- | --- | --- |
| Switch / momentary switch / cover switch | `value` plus optional power telemetry |
| Analog output | `value` plus optional power telemetry |
| Dew heater | `enabled,zero,span,deltaT` plus optional power telemetry |
| Intervalometer | `currentCount,exposure,delay,count` |

When power monitoring is compiled in, the local ASCII implementation appends:

`;<volts>,<amps>,<flags>`

Where flags are a five-character string using `P`, `C`, `U`, `V`, `T` or `!`.

## Axis / Motor / Driver Service Commands

Axis service commands are implemented by `Axis.command.cpp`. For the main telescope/mount build these are reachable for axis 1 and axis 2, if present. The rotator exposes axis3, if present. The focuser(s) expose any of axis 4 to axis 9, if present.

| Command | Reply | Description | Plugin status |
| --- | --- | --- | --- |
| `:GXAa,p#` | `value,min,max,type,name#` | Get axis parameter `p` for axis `a` (`1..9`) | Implemented |
| `:GXAa,M#` | `name#` | Motor/driver name for axis `a` | TODO |
| `:GXAa,0#` | `n#` | Parameter count for axis `a` | TODO |
| `:GXSa#` | `delta,velocity#` | Servo-only delta and velocity for axis `a` | TODO |
| `:GXUa#` | `flags#` | Stepper driver status for axis `a` | TODO |
| `:SXAC,0#` | `0/1` | Use runtime NV axis settings | TODO |
| `:SXAC,1#` | `0/1` | Use compile-time `Config.h` axis settings | TODO |
| `:SXAa,R#` | `0/1` | Revert axis `a` settings to defaults on next boot | Implemented |
| `:SXAa,p,value#` | `0/1` | Set axis parameter `p` for axis `a` | Implemented |
| `:SX4E,T#` | `0/1` | Servo Calibration Track Normal | was removed in 10.26a |
| `:SX4E,F#` | `0/1` | Servo Calibration Track fixed rate | was removed in 10.26a |
| `:SX4E,R#` | `0/1` | Servo Calibration record | was removed in 10.26a |
| `:SX4E,W#` | `0/1` | Servo Calibration stop recording | was removed in 10.26a |
| `:SX4E,!#` | `0/1` | Servo Calibration Clear Buffer | was removed in 10.26a |
| `:SX4E,L#` | `0/1` | Servo Calibration load calibration | was removed in 10.26a |
| `:SX4E,S#` | `0/1` | Servo Calibration save calibration | was removed in 10.26a |
| `:SX4E,V#` | `0/1` | Servo Calibration load backup | was removed in 10.26a |
| `:SX4E,B#` | `0/1` | Servo Calibration save backup | was removed in 10.26a |
| `:SX4E,H#` | `0/1` | Servo Calibration high-pass filter | was removed in 10.26a |
| `:SX4E,A#` | `0/1` | Servo Calibration low-pass filter | was removed in 10.26a |

### `:GXAa,p#` Reply Format

The reply is:

`value,min,max,type,name#`

Where:

- `value` is the current NV value
- `min` and `max` are the documented range
- `type` is the axis parameter type code returned by firmware
- `name` is the parameter name from the axis parameter table
- some `name` values are locale tokens such as `$1` or `$12` rather than literal English labels

Axis parameter type codes:

| Code | Symbol | Meaning | Plugin status |
| --- | --- | --- | --- |
| `0` | `AXP_INVALID` | Invalid / placeholder entry |
| `1` | `AXP_BOOLEAN` | Boolean value stored in NV and applied on restart/reinit |
| `2` | `AXP_BOOLEAN_IMMEDIATE` | Boolean value applied immediately when set |
| `3` | `AXP_INTEGER` | Integer value stored in NV and applied on restart/reinit |
| `4` | `AXP_INTEGER_IMMEDIATE` | Integer value applied immediately when set |
| `5` | `AXP_FLOAT` | Floating-point value stored in NV and reported as a plain numeric field |
| `6` | `AXP_FLOAT_IMMEDIATE` | Floating-point value applied immediately when set |
| `7` | `AXP_FLOAT_RAD` | Angular float stored internally in radians |
| `8` | `AXP_FLOAT_RAD_INV` | Inverse angular float stored internally with radian/degree conversion |
| `9` | `AXP_POW2` | Power-of-two constrained integer-like value |
| `10` | `AXP_DECAY` | Driver decay-mode selector |

Protocol note:

- `:GXAa,p#` normalizes `AXP_FLOAT_RAD` and `AXP_FLOAT_RAD_INV` to reply type code `5` and converts the value/range to degree-based units for transport.
- Axis parameter values are float-backed in firmware; values are always float even when the logical type is boolean or integer so the UI can show the appropriate controls.
- Boolean parameters: some use logical `0`/`1`, while others use the firmware constants `OFF = -1` and `ON = -2`; so any UI can show 'True'/'False' or 'On'/'Off'.
- For `AXP_DECAY`, the UI shows the matching decay-mode text for the numeric value `1=MIXED`, `2=FAST`, `3=SLOW`, `4=SPREADCYCLE`, `5=STEALTHCHOP`.

Locale-backed axis parameter name tokens currently used by firmware:

| Token | Meaning | Plugin status |
| --- | --- | --- |
| `$1` | Counts/degree | Implemented |
| `$2` | Min limit, degs | Implemented |
| `$3` | Max limit, degs | Implemented |
| `$4` | Steps/um | |
| `$5` | Min limit, um | |
| `$6` | Max limit, um | |
| `$7` | Reverse | Implemented |
| `$8` | Microsteps | Implemented |
| `$9` | Microsteps Goto | Implemented |
| `$10` | Decay mode | |
| `$11` | Decay mode Goto | |
| `$12` | mA Hold | Implemented |
| `$13` | mA Run | Implemented |
| `$14` | mA Goto | Implemented |
| `$15` | 256x Interpolate | |
| `$16` | P tracking | Implemented |
| `$17` | I tracking | Implemented |
| `$18` | D tracking | Implemented |
| `$19` | P slewing | Implemented |
| `$20` | I slewing | Implemented |
| `$21` | D slewing | Implemented |
| `$22` | Rads/count | |
| `$23` | Steps/count ratio | |
| `$24` | Max accel, %/s/s | |
| `$25` | Min power, % | |
| `$26` | Max power, % | |

