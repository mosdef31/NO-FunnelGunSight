# FunnelGunSight Technical Documentation

This document covers the architecture, math, and config internals of the mod.
For install instructions and a plain-language feature overview, see README.md.

## File overview

| File | Responsibility |
|---|---|
| `FunnelGunSightPlugin.cs` | BepInEx plugin entry point. Loads config, wingspan database, and Harmony patches. |
| `CombatHUDPatch.cs` | Harmony patches. Injects the funnel on weapon station select, suppresses the native gun pip. |
| `FunnelGunSight.cs` | Per-frame logic. Target detection, turn rate, spine sampling, wall and range dot geometry. |
| `FunnelRenderer.cs` | Draws the funnel with `GL` immediate mode in `OnGUI`. |
| `PlaneOfMotionSampler.cs` | Ballistic simulation and lead calculation for each point along the funnel spine. |
| `FunnelConfig.cs` | All BepInEx config entries. |
| `ConfigurationManagerAttributes.cs` | Duck-typed tag class ConfigurationManager reads for ordering and the advanced/basic split. |
| `HudTheme.cs` | Resolves draw colours from the game's HUD theme, or from config when overridden. |
| `WingspanDatabase.cs` | Loads and auto-populates `wingspans.json`. |

## How the funnel is built each frame

1. `CombatHUDPatch` injects a `FunnelGunSight` component onto the HUD when a gun
   station is selected. It stays alive until the station changes.
2. `FunnelGunSight.UpdateFunnel()` runs in `LateUpdate`:
   - Resolves the current target (see Target tiers below).
   - Reads the aircraft's angular velocity, optionally blends in predictive
     tracking, then applies smoothing.
   - Calls `PlaneOfMotionSampler.Sample()` to get a spine of world-space points,
     each with a lead angle appropriate to its range.
   - Projects the spine to screen space and offsets left/right to build the
     funnel walls, sized to the target's wingspan.
   - Computes the range dot position if a target is locked.
   - Passes everything to `FunnelRenderer.SetDrawData()`.
3. `FunnelRenderer.OnGUI()` draws the walls, pipper cross, and range dot as
   GL triangles.

## Target tiers

- **Tier 1**: a target designated through `CombatHUD.GetTargetList()`. Used for
  wingspan and the range dot.
- **Tier 2**: no Tier 1 target, but a marker is HUD-cursor-selected
  (`HUDUnitMarker.selected`). Also used for wingspan and the range dot.
- **Tier 3**: no Tier 1 or 2 target. If `AutoTargetNearestEnemy` is on, the
  closest hostile aircraft within 30 degrees of the gun boresight sets funnel
  width only. It never produces a range dot, since it isn't a confirmed lock.

## Gun direction

`FunnelGunSight.Initialize()` sums `weapon.transform.forward` across all guns
on the station and converts to aircraft-local space. This is the same source
`HUDBoresightState` uses for its own pip, so any depression angle baked into a
gun's transform (for example, a nose cannon mounted below the fuselage axis)
is picked up automatically without needing per-aircraft config.

There is no screen-space anchor correction. An earlier version tried to read
the native pip's rendered `Image` position each frame to correct for canvas
versus GL coordinate differences. That approach was removed: `CombatHUDPatch`
suppresses `HUDBoresightState.UpdateWeaponDisplay` every frame to hide the
native pip, so the `Image` position was never updated after station selection
and stayed frozen at screen centre. The correction ended up pulling the whole
funnel toward centre regardless of true gun angle. `Camera.WorldToScreenPoint`
combined with the Y flip in `FunnelRenderer.Fy()` is sufficient on its own.

## Ballistic simulation

`PlaneOfMotionSampler.SimulateBullet()` Euler-integrates the bullet under drag
and gravity, using the same law as the game's own `BulletSim.Bullet.
TrajectoryTrace`:

```
vel.y -= 9.81 * dt * gravMult
vel   -= vel.sqrMagnitude * dragCoef * dt * vel.normalized / rawMuzzleVelocity
pos   += vel * dt
```

(`vel.sqrMagnitude * vel.normalized` is `vel.magnitude * vel`, which is how the
game writes the same term.) Drag divides by the raw, air-relative muzzle
velocity, since drag opposes motion through air, not motion relative to the
ground.

The bullet's initial velocity is the **full** vector the game gives it:

```
initialVelocity = gunDir * muzzleVelocity + aircraftVelocity
```

`Gun.Fire()` passes `velocityInherit.velocity + muzzle.forward * muzzleVelocity`
straight into `BulletSim.AddBullet`, so a shot fired with any angle of attack or
sideslip also drifts sideways. Modelling only the along-axis component of
aircraft velocity — as versions up to 1.0.4 did — loses that drift.

### Terminating the integration (the 1.1.0 lead fix)

The loop runs **until the bullet actually reaches the requested range**, then
interpolates within the final step so the returned point sits exactly on that
range:

```
f    = (targetRange - distPrev) / (dist - distPrev)
pos  = Lerp(posPrev, pos, f)
tof += dt * f
```

Up to 1.0.4 it instead ran exactly `BallisticSimulationSteps` iterations with

```
dt = (targetRange / muzzleVelocity) / steps
```

Because drag slows the bullet, `steps` iterations of that `dt` never covered
`targetRange` — the early-exit test almost never fired, the loop ended by
exhausting its budget, and `actualTof` therefore always came out to
`targetRange / muzzleVelocity`: the drag-free time. The trajectory's *shape* was
right; its *duration* was not. Since

```
leadDeg = angularSpeed * timeOfFlight * Rad2Deg
```

is linear in time of flight, the funnel underled by exactly the time error —
11% at 500 m and 25% at 1200 m for a 20mm, 14% and 31% for the draggier 25mm.
At 1000 m in a 10 °/s pull that is about 42 m of cross-range miss.

`dt` is now sized from a drag-aware estimate, so `BallisticSimulationSteps`
stays a meaningful resolution knob across weapons whose drag coefficients differ
by 3×. Integrating `dv/dt = -k·v²` with `k = dragCoef / muzzleVelocity` gives

```
t(x) = (e^(k·x) - 1) / (v₀·k)
```

which ignores gravity and inherited velocity, so the loop is allowed
`steps × 3` iterations before giving up. At the default 40 steps the worst
observed time-of-flight error across every gun in the game is under 2% at
1200 m; at 20 steps it is under 4.5%.

The resulting bullet position's direction (relative to the muzzle) already
includes gravity droop and inherited-velocity drift. The angular lead from turn
rate is applied as an additional rotation on top of that direction:

```
leadDir = Quaternion.AngleAxis(leadDeg, turnAxis) * ballisticDirection
```

### The native pip has the same bug

`ControlsFilter.CalcAim` computes its lead time as

```csharp
float num = targetDist / weapon.info.muzzleVelocity;
Vector3 vector2 = num * vector + 0.5f * num * num * targetAccelSmoothed;
```

— drag-free, exactly the error above. It does correct ballistic *droop*
separately through `Kinematics.TrajectorySim`, but discards that simulation's
`timeToTarget` (`out var _`) and leads target motion on the naive time anyway.
So the native pip underleads too, and a corrected funnel will visibly disagree
with it. Expect that to be reported as a funnel bug; it is not.

## Turn rate and predictive tracking

`rb.angularVelocity` is negated in world space when `InvertTurnDirection` is
on. This corrects both the Firefly Companion FBW mod's inverted yaw and
Unity's left-handed pitch convention in one operation, and stays correct at
any roll angle (unlike negating local X/Y separately, which breaks once the
aircraft is significantly rolled).

When `EnablePredictiveTracking` is on and a Tier 1/2 target is locked, the
line-of-sight rotation rate is blended in:

```
losRate = -(relativePosition x relativeVelocity) / |relativePosition|^2
```

This is the same quantity proportional navigation guidance uses. It is
blended by range, ramping from 0 at `PredictiveTrackingMinRange` to
`PredictiveTrackingStrength` at `PredictiveTrackingMaxRange`, since the raw
rate is noisy at close range (small `r` amplifies errors).

The final combined turn rate is smoothed with exponential decay
(`TurnRateSmoothing`, a time constant in seconds) so it doesn't twitch near
zero magnitude or snap during brief high-rate maneuvers.

## Wall geometry

Walls are built entirely in screen space, not world space. For each spine
point, the half-width in pixels is the target wingspan's angular size at that
range:

```
halfWidthPx = (wingspan / 2) / range * focalLengthPx
focalLengthPx = screenHeight / 2 / tan(fov / 2)
```

The offset direction is the screen-space perpendicular of the vector from the
first to the last spine point (the global tangent), not a per-segment local
perpendicular. This avoids direction flips when adjacent spine points are
nearly coincident at low turn rate.

## Line thickness rendering

`GL.LINES` has no width control on most render backends. `FunnelRenderer`
draws thickness by building quads (two triangles each) instead: for a segment
from A to B with thickness t, it offsets both points by a perpendicular
vector of length t/2 in each direction. The range dot's outline is drawn the
same way as a ring, using an inner and outer radius.

`FunnelLineThickness` controls the funnel walls and pipper cross.
`RangeDotLineThickness` controls the range circle's outline, independently.

When `RangeDotFilled` is on, a solid disc (triangle fan from the centre) is
drawn first, then the same outline ring is drawn on top using
`RangeDotLineThickness`, so the thickness setting stays meaningful in both
modes: it's the full ring width when unfilled, and the edge width when
filled.

## Coordinate spaces

`Camera.WorldToScreenPoint` returns pixel coordinates with (0,0) at the
bottom-left (OpenGL convention). `GL.LoadPixelMatrix()` under DX11 on Windows
uses (0,0) at the top-left. `FunnelRenderer.Fy(y) = Screen.height - y` converts
between the two. Everything upstream of `FunnelRenderer` stays in
`WorldToScreenPoint` space; the flip happens once, at the point of drawing.

Colors also need conversion: Unity UI components gamma-correct their color
property automatically before sending it to the GPU, but `GL.Color()` does
not. `FunnelRenderer` converts to `.linear` manually when
`QualitySettings.activeColorSpace == ColorSpace.Linear`, so GL output matches
native HUD elements using the same nominal color.

## HUD theme colors

0.34 moved the HUD off hardcoded `Color.green` / `Color.yellow` and onto
`ThemeManager.Active.ColorTheme`, which the player selects in settings and can
also supply as JSON from `persistentDataPath/themes/`. `HudTheme` reads the same
source so the funnel tracks whatever the player picked:

| Funnel element | Theme color | Why |
|---|---|---|
| walls, pipper | `AllClear` | what the native boresight uses when on target |
| firing-solution flash | `HudUnitFlash` | the theme's dedicated attention color, so it stays distinct from `AllClear` under any theme |

`ThemeManager.Active` is read per frame rather than cached. It is a static
property fetch plus a ScriptableObject field read, which is cheap, and not
caching means a mid-session theme change is picked up with no need to subscribe
to `ThemeManager.ThemeGroupChanged`. The read is wrapped in a try/catch that
falls back to the config colors, since the theme system is not initialised during
early plugin load.

Theme colors carry their own alpha. `FunnelOpacity` multiplies it rather than
replacing it, so the slider dims a theme-matched funnel without breaking the
match. With `FollowHudTheme` off the configured colors have alpha 1, so the
multiply reduces to the pre-1.1.0 behaviour.

**This is the reason 1.1.0 requires 0.34.** `NuclearOption.UIStyleSystem` does
not exist in 0.33.4, and the reference is compile-time, not reflective.

## Config menu presentation

`ConfigurationManagerAttributes` is a duck-typed tag class: ConfigurationManager
locates it by type name through reflection, so the mod gets ordering control
without taking a hard dependency on ConfigurationManager being installed. If it
is absent, the tags are ignored and nothing breaks. **Do not rename the class or
its fields** — the match is by name, and a rename fails silently.

`Order` sorts within a section (higher first), replacing the default alphabetical
sort that used to separate `ShowRangeDot` from `RangeDotSize`. `IsAdvanced` hides
the knobs that need this document to set sensibly.

Section order itself is still alphabetical and cannot be controlled without
renaming sections, which would orphan every key inside them. `General`,
`Display` and `Tracking` are therefore left as-is.

## wingspans.json

Format:

```json
{
  "wingspans": {
    "some_plane_key": 11.2
  }
}
```

Lookup order in `WingspanDatabase.GetWingspan()`: `jsonKey`, then `code`, then
`UnitDefinition.width` (only for aircraft; buildings, ships, and ground
vehicles are excluded since their `width` is a footprint, not a wingspan).
If a wingspan is derived from `width`, it's written back to the file under
the unit's `jsonKey` so it's available immediately on the next lookup, with
no manual data entry required.

## Config changes in 1.1.0

One key **moved section**, which resets it:

| Old | New |
|---|---|
| `Smoothing.TurnRateSmoothing` (default 0.35) | `Tracking.TurnRateSmoothing` (default 0.15) |

The move is deliberate rather than incidental. A changed default does not reach
anyone who already has a config file, and 0.35 s of smoothing lags the funnel
behind a rising pull — underleading in the same hard turn where the time-of-flight
bug was already underleading. Moving the key forces the new value on existing
installs. The now-empty `[Smoothing]` section can be deleted from
`FunnelGunSight.cfg` by hand.

One key was **added**: `Display.FollowHudTheme` (default `true`).

One default changed in place, and so only affects fresh installs:
`BallisticSimulationSteps` 20 → 40. Existing users on 20 keep a worst-case
time-of-flight error of ~4.5% instead of ~2%, both of which are far better than
the 31% before the fix.

## Config keys renamed in 1.0.4

The following config keys were renamed for clarity. Existing config files
will show these as new entries with default values; old entries can be
deleted manually.

| Old key | New key |
|---|---|
| `DiagLogging` | `DebugLogging` |
| `ForceReinitKey` | `ResetOverlayKey` |
| `InvertAngularVelocity` | `InvertTurnDirection` |
| `ShowGunCross` | `ShowPipper` |
| `GunCrossSize` | `PipperSize` |
| `EnableShootCue` | `FlashOnFiringSolution` |
| `ShootCueColor` | `FiringSolutionColor` |
| `MinAngularRate` | `MinTurnRate` |
| `EnableLevelV` | `EnablePredictiveTracking` |
| `LevelVBlendWeight` | `PredictiveTrackingStrength` |
| `LevelVMinRange` | `PredictiveTrackingMinRange` |
| `LevelVMaxRange` | `PredictiveTrackingMaxRange` |
| `EnableBoresightAutoTarget` | `AutoTargetNearestEnemy` |
| `BallisticSteps` | `BallisticSimulationSteps` |
| `TrajectoryPoints` | `FunnelResolution` |
| `TurnRateDamping` | `TurnRateSmoothing` |

Two new keys were added: `FunnelLineThickness` and `RangeDotLineThickness`.
The `_Instructions` sentinel entry at the top of the General section was
removed.
