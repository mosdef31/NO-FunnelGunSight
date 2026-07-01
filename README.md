# FunnelGunSight

A BepInEx mod for **Nuclear Option** that adds an EEGS-style gun funnel to the HUD.

---

## What is a gun funnel?

The Enhanced Envelope Gun Sight (EEGS) funnel is a gun-aiming aid originally developed for the F-16.
Instead of a fixed pipper, it renders two curved walls that taper from a wide opening near the boresight
to a narrow tip in the lead direction.

The width of the walls at any point represents the angular size of a standard target wingspan at that range.
To get a firing solution, you manoeuvre until the target fits between the walls, and at that moment the
funnel is telling you the correct range *and* the correct lead angle simultaneously.

**FunnelGunSight** replicates this directly on the Nuclear Option HUD using the aircraft's own angular
rate and the game's weapon station data.

---

## Features

- EEGS-style funnel that deflects in the current lead direction based on measured angular rate
- Rigidly fixed to the airframe boresight. Free-look, TrackIR, and head movement never
  shift the funnel; it behaves like a combiner-glass HUD bolted to the nose, not a
  screen-space overlay glued to your view
- Funnel walls scale with range so target wingspan fills the opening at the correct distance
- **Range dot**: when a target is designated or HUD-selected, a circle appears on the funnel spine
  at the target's actual slant range; the circle diameter matches the wall separation at that range
  (target fills the circle, correct range, shoot)
- Configurable line thickness for the funnel walls, pipper, and range dot
- Adaptive wingspan mode: looks up the locked target in a bundled aircraft database
- Live-editable config via BepInEx ConfigurationManager
- Reset keybind to recover from a stuck overlay (default **F9**)

---

## Requirements

| Dependency | Version |
|---|---|
| BepInEx | 5.4.x |
| Harmony | included with BepInEx |
| Nuclear Option | current Steam release |

Optional: **BepInEx.ConfigurationManager** for in-game config UI.

---

## Installation

1. Install BepInEx 5 into your Nuclear Option folder if you have not already.
2. Drop `FunnelGunSight.dll` into `BepInEx/plugins/`.
3. Drop `wingspans.json` into the same folder as the DLL. It starts empty and fills
   itself in automatically the first time you encounter each aircraft type in
   Adaptive mode.
4. Launch the game. The overlay appears automatically when you select a gun weapon station.

---

## How to use

1. Select a **gun weapon station** in the cockpit.
2. The funnel appears centred on the HUD boresight.
3. Manoeuvre your aircraft. The funnel walls deflect in the direction of lead based on your current
   angular rate.
4. **Without a target lock:** fly so the target drifts into the funnel and fits between the walls.
   Read the range at the point where it fits.
5. **With a designated or HUD-selected target:** the range dot (circle) appears on the spine at the
   target's current slant range. When the target visually fills the circle, range and lead are both
   correct, fire.

---

## Configuration

All settings are in `BepInEx/config/FunnelGunSight.cfg` and can be changed live in the
ConfigurationManager (`F1` by default).

### General

| Key | Default | Description |
|---|---|---|
| Enabled | true | Master toggle |
| DebugLogging | false | Log turn rate and target state every ~2 s |
| ResetOverlayKey | F9 | Rebuild the overlay (use if it gets stuck) |
| InvertTurnDirection | true | Correct for FBW mods that flip turn direction |

### Display

| Key | Default | Description |
|---|---|---|
| FunnelOpacity | 1.0 | Overall opacity of all HUD elements (0.1 to 1.0) |
| ShowPipper | true | Show the pipper cross at HUD centre |
| PipperSize | 8 px | Half-length of each pipper arm |
| FunnelLineThickness | 2 px | Thickness of the funnel walls and pipper cross |
| ShowRangeDot | true | Show the range dot when a target is locked |
| RangeDotSize | 0.4 | Dot diameter as a fraction of wall separation (0.1 to 1.0) |
| RangeDotFilled | false | Draw the range dot as a solid disc instead of an outline ring |
| RangeDotLineThickness | 2 px | Thickness of the range dot's outline (or edge, if filled) |
| FlashOnFiringSolution | true | Flash the funnel when target is in a firing solution |
| HideWithGearDown | true | Hide the funnel when the landing gear is deployed |

### Tracking

| Key | Default | Description |
|---|---|---|
| WingspanMode | Fixed | Fixed = DefaultWingspan always; Adaptive = per-target lookup |
| DefaultWingspan | 11 m | Wingspan used in Fixed mode or when target is not in database |
| FunnelResolution | 50 | Spine sample count; higher is smoother, minor CPU cost |
| MinRangeMeters | 100 m | Near (wide) end of the funnel |
| MaxRangeMeters | 1200 m | Far (narrow) end of the funnel |
| MinTurnRate | 0.01 rad/s | Rate floor; below this, a fallback axis is used |
| EnablePredictiveTracking | false | Factor in a locked target's own movement, not just yours |
| AutoTargetNearestEnemy | true | Use the nearest enemy in the boresight cone for funnel width when nothing is locked |

### Smoothing

| Key | Default | Description |
|---|---|---|
| TurnRateSmoothing | 0.35 s | Smooths the measured turn rate before it drives the funnel's shape. 0 = instant, no smoothing |

---

## Known limitations

- `InvertTurnDirection` assumes the Firefly Companion FBW sign convention by
  default. If you're on a stock aircraft or a different FBW mod and the
  funnel curves the wrong way, turn this setting off.
- `wingspans.json` starts empty and self-populates as you fly. The first time
  Adaptive mode encounters a given aircraft type, its wingspan is derived
  from the game's own unit data and written to the file automatically under
  that aircraft's `jsonKey` (the game's internal plane ID). No manual data
  entry is needed, and the file only grows; it's never overwritten with
  stale data for keys you've already collected.

---

## Building from source

```
dotnet build FunnelGunSightMod.csproj -c Release
```

Set `GamePath` in `GamePath.props` to your Nuclear Option install directory before building.

For architecture details and the math behind the funnel, see [`TECHNICAL.md`](./TECHNICAL.md).

---

## License

MIT
