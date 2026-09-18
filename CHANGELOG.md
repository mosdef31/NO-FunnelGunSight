# Changelog

## 1.2.0

### New: Added more useful sights and options

There is a new **Sights** section in the settings with an independent switch for
each. Turning one on never turns another off.

- **Ground attack sight, CCIP** (on by default). A pipper on the spot where the
  rounds meet terrain, water or a ship, solved through the same drag and gravity
  as the funnel. It is drawn only when the gun line actually reaches something, so
  it stays out of the way in level flight.
- **Lead pipper, LCOS** (off by default). A single lead dot instead of the
  funnel's fifty. With a lock it uses the real range; without one it uses an
  assumed range you set.
- **Range readout** (on by default). The range as a number beside the range
  circle and the ground pipper, in hundreds of meters.

### Changed: Adaptive is now the default wingspan mode

The funnel resizes to the locked target's real wingspan instead of assuming one
fixed number for every aircraft. Switch back to **Fixed** in the settings if you
preferred the old behavior.

### Fixed: the funnel overlay came and went on some stations

On a station where the HUD center the funnel anchors to is inactive, the mod
retried twice a second for as long as you sat there, and the overlay appeared
only on the rare frame the retry happened to land in a brief active window. It
now backs off after a few quick tries and reports the state once instead of
fighting it, so the sight is either there or it is not, rather than flickering.

### Fixed: typing a number into the settings

Some numeric settings (ground sight size, its max range, its flash rate, the
lead pipper's assumed range) would mangle a typed value - typing "15" could land
on "35" - because the settings menu re-validated the field on every keystroke.
Typing now works normally.

### New: the walls are sized for what the target is showing you

A target seen head-on presents its length, not its wingspan, so walls set one
wingspan apart were asking you to fit a dimension that was not on screen. The
funnel now measures the target's apparent width along the direction the walls are
actually drawn in. Turn it off with **Aspect-aware sizing** to go back to the
wingspan at every aspect.

### Changed: the funnel settles more like a real gun sight

Turn rate smoothing was 0.15 s. The real enhanced envelope sight this is modeled
on takes 0.5 to 1.5 s to settle after you change your plane of motion, so ours was
three times faster than the fastest real one and it read as twitchy. The default
is now **0.5 s** and the setting goes up to 1.5 s.

**This setting is reset by the update**, deliberately - otherwise nobody who has
already flown the mod would have got the new value.


### Fixed: modded guns could be aimed with the wrong muzzle velocity

The funnel took the round's launch speed from the weapon's `WeaponInfo`. The game
does not. `Gun.SpawnBullet` launches the round at the gun's own `muzzleVelocity`
field, which is seeded from `WeaponInfo` when the gun wakes up and then drifts
away from it in two ways:

- A gun fed from a `GunAmmo` weapon mount has its whole `WeaponInfo` swapped for
  the mount's afterwards, so it keeps firing at its original speed while showing
  different numbers. No stock aircraft gun uses such a mount, which is why this
  never showed up on stock aircraft and did show up on modded ones.
- A gun that is overheating has its muzzle velocity written down as the barrel
  heats.

The funnel now reads the field the game actually fires with, and keeps
`WeaponInfo.muzzleVelocity` only where the game keeps it: as the divisor in the
drag term. On a gun whose two numbers differ by 60 m/s, time of flight to 1000 m
was out by 5.7%, which is about 12 m of cross-range miss in a 10 deg/s pull.

Turn **Debug logging** on and the log now prints, once per weapon selection, every
ballistic input the funnel is using, and warns when the gun and its `WeaponInfo`
disagree. That is the line to attach to a bug report about a modded aircraft.

### Fixed: the trajectory is now stepped at the game's own physics rate

The simulation was integrated finely, which made it more accurate than the game
and therefore wrong. The game steps every bullet once per `FixedUpdate`, and
explicit Euler at that step bleeds speed faster than the true curve, so real
rounds arrive later than a finely integrated model predicts. Matching the game's
step is worth up to 1% of time of flight on the draggiest cannon at 1200 m, which
is about 3.5 m of cross-range miss, and it grows with drag.

`BallisticSimulationSteps` is no longer an accuracy setting. The step is the
game's and cannot be chosen. The key is kept, as a safety ceiling, so nobody's
config file breaks.

### Fixed: the funnel vanished instead of being clipped

One spine point behind the camera blanked the entire sight. In an external view,
or in a hard pull at a wide field of view, the near end of the funnel crosses
behind the camera routinely, so the sight blinked out exactly when it was being
used hardest. Only the points behind the camera are dropped now.

### The walls and the range circle are drawn better

- Walls are one continuous mitred strip instead of a row of separate quads, so
  there is no longer a wedge of missing pixels on the outside of every bend. At 2
  px this read as a slightly ragged line; at 4 px in a hard turn it read as a
  dashed one.
- The range circle's segment count follows its radius instead of always being
  sixteen, so a close lock is a circle rather than a visible polygon.

### Faster

- The time of flight to all fifty funnel points is now solved by one sweep down
  the gun line instead of fifty separate simulations of the same trajectory.
- The per-frame arrays are pooled instead of reallocated every `LateUpdate`.

### Changed

- **Hide stock gun pip** is no longer an advanced setting. Turning it off draws
  the stock pip alongside the funnel, which is what it always did, but it was
  hidden behind the advanced tickbox where nobody found it.

## 1.1.0

**Requires Nuclear Option 0.34 or later.** If you are still on 0.33.4, stay on
1.0.4 - the mod manager will serve it to you.

### Fixed: the funnel underled, badly

Time of flight was being computed as if bullets flew through a vacuum. The
ballistic simulation modeled drag correctly for the trajectory's shape, but the
integration ran for a fixed number of steps sized from the *drag-free* flight
time, so it always ran out of steps before the bullet actually covered the
distance - and reported the drag-free time anyway. Lead angle is directly
proportional to time of flight, so the funnel asked for less lead than the shot
needed, and the error grew with both range and the weapon's drag coefficient:

| Weapon | 500 m | 800 m | 1000 m | 1200 m |
|---|---|---|---|---|
| 25mm Autocannon | −14% | −22% | −27% | −31% |
| 20mm / 25mm Rotary | −11% | −17% | −21% | −25% |
| 27mm / 30mm Rotary | −7% | −11% | −13% | −16% |
| 35mm Autocannon | −4% | −7% | −8% | −10% |

In practice, a 25mm Rotary shot at 1000 m in a 10 °/s pull was aimed about 42 m
behind where it needed to be.

The simulation now integrates until it actually reaches the target range and
interpolates the final partial step, with the step size derived from a
drag-aware estimate. Worst-case time-of-flight error is now under 2% at 1200 m.

**Note:** the game's own gun pip has the same flaw - `ControlsFilter.CalcAim`
projects target motion over `distance / muzzleVelocity`, ignoring drag. The
funnel and the native pip will now visibly disagree at longer ranges. The funnel
is the one that is right.

### Fixed: bullets inherit your full velocity, not just part of it

The simulation only added the component of your aircraft's velocity along the
gun axis. The game gives a bullet your entire velocity vector, so a shot fired
with any angle of attack or sideslip also drifts sideways. That drift is now
modeled.

### Changed: the funnel follows your HUD theme

0.34 replaced the HUD's hardcoded colors with a theme system. The funnel now
reads the same theme, so it matches the rest of your HUD - including custom
themes - instead of sitting on top of it in a fixed green.

- New **Match HUD theme** setting, on by default.
- Turn it off to go back to the manual `FunnelColor` / `FiringSolutionColor`
  settings, which are otherwise ignored.
- **Opacity** now scales the theme's own alpha rather than replacing it, so you
  can dim the funnel without breaking the color match.

### Changed: the config menu is navigable

- Settings are now ordered by what they do instead of alphabetically, so related
  ones sit together (the range circle's size no longer lands three settings away
  from the toggle that turns it on).
- Expert knobs are hidden behind **Advanced settings**: debug logging, native
  crosshair suppression, predictive tracking's three tuning values, funnel
  resolution, ballistic sim steps, and minimum turn rate. That roughly halves
  what a new player sees.
- Every setting has a readable display name and a plain-language description.

### Changed: defaults

- `TurnRateSmoothing` **0.35 s → 0.15 s**, and it moved from the `Smoothing`
  section to `Tracking`. The old value lagged the funnel behind a rising pull,
  underleading in exactly the hard turn where you shoot. **This one setting will
  reset to the new default** - the section move is deliberate, so the fix
  reaches existing installs instead of only new ones. Everything else you have
  configured is preserved.
- `BallisticSimulationSteps` **20 → 40**, which is what keeps the corrected
  time-of-flight error under 2% at long range. Lower it if you need the CPU back.

### Housekeeping

- Fixed a possible-null dereference warning; the mod now builds with zero
  warnings.

## 1.0.4

Config keys renamed for clarity - see the table in `TECHNICAL.md`. Added
`FunnelLineThickness` and `RangeDotLineThickness`.
