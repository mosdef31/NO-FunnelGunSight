# FunnelGunSight

**An F-16 style gun funnel for Nuclear Option. Stop guessing your lead.**

[![Latest release](https://img.shields.io/github/v/release/mosdef31/NO-FunnelGunSight?style=for-the-badge&label=download&color=2ea043)](https://github.com/mosdef31/NO-FunnelGunSight/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/mosdef31/NO-FunnelGunSight/total?style=for-the-badge&color=blue)](https://github.com/mosdef31/NO-FunnelGunSight/releases)
[![Game version](https://img.shields.io/badge/Nuclear%20Option-0.34%2B-orange?style=for-the-badge)](https://store.steampowered.com/app/2168680/Nuclear_Option/)
[![License](https://img.shields.io/badge/license-MIT-lightgrey?style=for-the-badge)](./LICENSE)

📥 **[Download the latest release](https://github.com/mosdef31/NO-FunnelGunSight/releases/latest)** &nbsp;·&nbsp;
📝 **[What's new](./CHANGELOG.md)** &nbsp;·&nbsp;
🔧 **[How it works](./TECHNICAL.md)** &nbsp;·&nbsp;
🐛 **[Report a bug](https://github.com/mosdef31/NO-FunnelGunSight/issues)**

---

## The problem

Nuclear Option gives you a single dot for the guns. It tells you where the
bullets go, but nothing about **how far away** the target is or **how hard you
need to pull**. So you hose rounds at a moving target and hope.

## The fix

The funnel is two curved walls instead of a dot. Their width at any point is how
wide a fighter *looks* at that distance.

> **Pull until the target fits snugly between the walls, then shoot.**

That's the whole technique. When it fits, the range is right and your lead is
right, at the same time. No mental math, no memorising a gunnery table. This is
the same Enhanced Envelope Gun Sight idea the real F-16 uses, wired up to Nuclear
Option's own flight model and weapon data.

If you've locked a target, you also get a **range circle** on the funnel sitting
at that target's actual distance — fill the circle, take the shot.

---

## Install

**Using a mod manager?** FunnelGunSight is on
[NOMNOM](https://github.com/KopterBuzz/NOMNOM). Search for it and hit install —
you're done.

**By hand:**

1. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases) into your
   Nuclear Option folder if you haven't already.
2. Download `FunnelGunSightMod.dll` from
   [the latest release](https://github.com/mosdef31/NO-FunnelGunSight/releases/latest).
3. Drop it into `BepInEx/plugins/`.
4. Launch. Select a gun station and the funnel appears.

Optional but recommended:
[BepInEx.ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases)
lets you tweak every setting in-game with `F1` instead of editing a text file.

> **Requires Nuclear Option 0.34 or newer.** Still on 0.33.4? Grab
> [v1.0.4](https://github.com/mosdef31/NO-FunnelGunSight/releases/tag/v1.0.4)
> instead.

---

## How to use it

1. Select a **gun station** in the cockpit. The funnel shows up on its own.
2. Turn into your target. The funnel bends toward where you need to be aiming.
3. **No lock:** manoeuvre until the target sits between the walls and fills the
   gap. Shoot.
4. **With a lock:** a circle appears on the funnel at the target's real distance.
   When the target fills that circle, shoot.

Funnel stuck or missing after an aircraft swap? Press **F9** to rebuild it.

---

## Features

- **Accurate lead.** The bullet's flight time is simulated against the game's own
  drag and gravity, not guessed from `distance ÷ muzzle velocity`. (See
  [what's new in 1.1.0](./CHANGELOG.md) — this used to be wrong, and it mattered.)
- **Bolted to the nose.** Free-look, TrackIR and head movement never drag the
  funnel around. It behaves like real combiner glass, not a screen overlay.
- **Matches your HUD.** Takes its colour and transparency from your active HUD
  theme, custom themes included. Override it if you'd rather.
- **Range circle** on the funnel for any designated or HUD-selected target.
- **Learns aircraft sizes.** Adaptive mode sizes the funnel to whatever you've
  actually locked, filling in its database from real game data as you fly.
- **Predictive tracking** (optional) factors in a locked target's own movement,
  not just yours.
- **Tweak everything live** in ConfigurationManager — no restarts.

---

## Configuration

Settings live in `BepInEx/config/com.funnelgunsight.mod.cfg`, or press `F1`
in-game with ConfigurationManager installed.

Everything below works out of the box. You only need to touch these if you want
to. The `code name` under each setting is what it's called in the `.cfg` file, if
you're editing that by hand.

### The two you might actually change

| Setting | Default | What it does |
|---|---|---|
| **Wingspan mode**<br>`WingspanMode` | Fixed | `Fixed` sizes the funnel for one wingspan always. `Adaptive` resizes it to whatever you've locked. Adaptive is more accurate; Fixed is more predictable. |
| **Invert turn direction**<br>`InvertTurnDirection` | on | If the funnel curves the *wrong way* when you turn, flip this. On matches the Firefly Companion FBW mod. |

### Appearance

| Setting | Default | What it does |
|---|---|---|
| Match HUD theme<br>`FollowHudTheme` | on | Take colour + transparency from the game's HUD theme. Turn off to pick your own. |
| Funnel colour<br>`FunnelColor` | green | Your own colour. Only used with *Match HUD theme* off. |
| Opacity<br>`FunnelOpacity` | 1.0 | Dims the funnel. Works in both colour modes. |
| Flash on firing solution<br>`FlashOnFiringSolution` | on | Flash when the target is in the walls at the right range. |
| Firing solution colour<br>`FiringSolutionColor` | white | Flash colour. Only used with *Match HUD theme* off. |
| Show pipper<br>`ShowPipper` | on | The small cross at the funnel's centre. |
| Pipper size<br>`PipperSize` | 8 px | How big that cross is. |
| Line thickness<br>`FunnelLineThickness` | 2 px | Thicker walls if you lose sight of them in hard turns. |
| Show range circle<br>`ShowRangeDot` | on | The circle marking a locked target's distance. |
| Range circle size<br>`RangeDotSize` | 0.4 | Circle size as a fraction of the funnel's width there. |
| Filled range circle<br>`RangeDotFilled` | off | Solid disc instead of an outline ring. |
| Range circle thickness<br>`RangeDotLineThickness` | 2 px | Set separately from the wall thickness. |
| Hide with gear down<br>`HideWithGearDown` | on | Hides the funnel on approach, like the stock sight. |

### Aiming

| Setting | Default | What it does |
|---|---|---|
| Default wingspan<br>`DefaultWingspan` | 11 m | Wingspan the funnel is sized for. Used by Fixed mode, and for aircraft Adaptive mode hasn't met yet. 11 m is about an FS-12. |
| Auto-size on nearest enemy<br>`AutoTargetNearestEnemy` | on | With nothing locked, size the funnel using the closest enemy in front of you. |
| Funnel near range<br>`MinRangeMeters` | 100 m | The wide end of the funnel. |
| Funnel far range<br>`MaxRangeMeters` | 1200 m | The narrow end of the funnel. |
| Turn rate smoothing<br>`TurnRateSmoothing` | 0.15 s | How fast the funnel reacts. Higher is steadier but lags hard manoeuvres. |
| Predictive tracking<br>`EnablePredictiveTracking` | off | Also account for a locked target's own movement. Better against someone jinking. |

### Other

| Setting | Default | What it does |
|---|---|---|
| Enable funnel<br>`Enabled` | on | Master switch. Turns the overlay off without uninstalling. |
| Reset overlay key<br>`ResetOverlayKey` | F9 | Rebuilds the funnel if it gets stuck. |

<details>
<summary><b>Advanced settings</b> (hidden behind the "Advanced settings" tickbox — you shouldn't need these)</summary>

| Setting | Default | What it does |
|---|---|---|
| Debug logging<br>`DebugLogging` | off | Dumps turn rate and target state to the BepInEx log every ~2 s. For bug reports. |
| Hide native crosshair<br>`HideNativeBoresight` | on | Hides the game's grey gun dot. Turn off to see both at once. |
| Predictive strength<br>`PredictiveTrackingStrength` | 1.0 | How much a locked target's motion is blended in. |
| Predictive min range<br>`PredictiveTrackingMinRange` | 300 m | Where predictive tracking starts fading in. Closer than this the reading is too noisy. |
| Predictive max range<br>`PredictiveTrackingMaxRange` | 800 m | Where it reaches full strength. |
| Funnel resolution<br>`FunnelResolution` | 50 | Points making up the curve. Higher is smoother, costs a little CPU. |
| Ballistic sim steps<br>`BallisticSimulationSteps` | 40 | Steps used to simulate drag and gravity. 40 keeps lead error under ~2% at 1200 m. |
| Minimum turn rate<br>`MinTurnRate` | 0.01 rad/s | Below this the funnel uses a fallback axis so it doesn't collapse in level flight. |

</details>

> **Upgrading from 1.0.x?** Your settings carry over, with one exception:
> `TurnRateSmoothing` moved sections and resets to the new 0.15 s default. That's
> intentional — the old 0.35 s made the funnel lag behind hard pulls. You can
> delete the now-empty `[Smoothing]` section from the `.cfg`.

---

## Known quirks

- **The funnel and the game's own gun dot disagree at longer ranges — the funnel
  is the correct one.** The stock dot works out lead using a bullet speed that
  ignores air resistance, so it asks for too little lead; at 1000 m with a heavy
  cannon that's roughly a quarter short. If you're used to shooting the stock
  dot, the funnel will feel like it's leading too much. It isn't. Details in
  [TECHNICAL.md](./TECHNICAL.md).
- **Funnel curving the wrong way?** Turn off *Invert turn direction*. It's set
  for the Firefly Companion FBW mod by default.
- **Aircraft database starts empty.** `wingspans.json` fills itself in the first
  time Adaptive mode meets each aircraft type. Nothing to enter by hand, and it
  only ever grows.

---

## Building from source

```
dotnet build FunnelGunSightMod.csproj -c Release
```

Set `GameDir` in `GamePath.props` to your Nuclear Option folder first — the one
containing `NuclearOption.exe`.

Architecture, ballistics math, and the reasoning behind the design decisions:
[TECHNICAL.md](./TECHNICAL.md).

---

## License

MIT — see [LICENSE](./LICENSE).
