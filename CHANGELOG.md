# Changelog

## 1.1.0

**Requires Nuclear Option 0.34 or later.** If you are still on 0.33.4, stay on
1.0.4 — the mod manager will serve it to you.

### Fixed: the funnel underled, badly

Time of flight was being computed as if bullets flew through a vacuum. The
ballistic simulation modelled drag correctly for the trajectory's shape, but the
integration ran for a fixed number of steps sized from the *drag-free* flight
time, so it always ran out of steps before the bullet actually covered the
distance — and reported the drag-free time anyway. Lead angle is directly
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

**Note:** the game's own gun pip has the same flaw — `ControlsFilter.CalcAim`
projects target motion over `distance / muzzleVelocity`, ignoring drag. The
funnel and the native pip will now visibly disagree at longer ranges. The funnel
is the one that is right.

### Fixed: bullets inherit your full velocity, not just part of it

The simulation only added the component of your aircraft's velocity along the
gun axis. The game gives a bullet your entire velocity vector, so a shot fired
with any angle of attack or sideslip also drifts sideways. That drift is now
modelled.

### Changed: the funnel follows your HUD theme

0.34 replaced the HUD's hardcoded colours with a theme system. The funnel now
reads the same theme, so it matches the rest of your HUD — including custom
themes — instead of sitting on top of it in a fixed green.

- New **Match HUD theme** setting, on by default.
- Turn it off to go back to the manual `FunnelColor` / `FiringSolutionColor`
  settings, which are otherwise ignored.
- **Opacity** now scales the theme's own alpha rather than replacing it, so you
  can dim the funnel without breaking the colour match.

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
  reset to the new default** — the section move is deliberate, so the fix
  reaches existing installs instead of only new ones. Everything else you have
  configured is preserved.
- `BallisticSimulationSteps` **20 → 40**, which is what keeps the corrected
  time-of-flight error under 2% at long range. Lower it if you need the CPU back.

### Housekeeping

- Fixed a possible-null dereference warning; the mod now builds with zero
  warnings.

## 1.0.4

Config keys renamed for clarity — see the table in `TECHNICAL.md`. Added
`FunnelLineThickness` and `RangeDotLineThickness`.
