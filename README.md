# FunnelGunSight

EEGS-style gun funnel HUD overlay for **Nuclear Option**. Built with BepInEx + Harmony.

Instead of a fixed pipper, the funnel renders two curved walls that taper from a wide
opening at the boresight down to a narrow tip in the lead direction. Get the target to
fit between the walls and you have a correct firing solution — range and lead, at the
same time, no math required.

## Features

- Real-time EEGS funnel driven by your aircraft's own angular rate
- Rigidly locked to the airframe boresight — survives free-look/TrackIR with zero drag
- **Level V** (optional) — blends in line-of-sight rate to a locked target, accounting
  for the target's own maneuvering, not just yours
- Range dot on the spine for designated/HUD-selected targets
- Adaptive wingspan lookup, self-populating from real game data as you fly
- Fully configurable live via BepInEx ConfigurationManager

## Install

1. Install BepInEx 5 in your Nuclear Option folder.
2. Drop `FunnelGunSight.dll` + `wingspans.json` into `BepInEx/plugins/`.
3. Launch — the funnel appears automatically when you select a gun weapon station.

## License

MIT
