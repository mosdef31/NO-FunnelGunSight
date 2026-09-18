using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace FunnelGunSight
{
    public sealed class FunnelGunSight : MonoBehaviour
    {
        private Aircraft?         _aircraft;
        private WeaponStation?    _weaponStation;
        private Vector3           _gunDirectionLocal; // aircraft-local gun direction
        private Vector3           _gunOriginLocal;    // aircraft-local muzzle origin, offset from aircraft.transform.position
        private FunnelRenderer?   _renderer;
        private FunnelConfig?     _config;
        private WingspanDatabase? _wingspanDb;
        private Gun?              _primaryGun;

        // UpdateFunnel used to allocate five arrays every frame - the spine, its times
        // of flight, the projected arc and both walls. At the default 50 points in
        // LateUpdate that is a few hundred kilobytes a second of pure garbage for a
        // shape whose size only changes when FunnelResolution does. They are grown
        // once and reused.
        private Vector2[]? _screenArcBuffer;
        private Vector2[]? _leftWallBuffer;
        private Vector2[]? _rightWallBuffer;

        private static Vector2[] EnsureBuffer(ref Vector2[]? buffer, int length)
        {
            if (buffer == null || buffer.Length < length)
                buffer = new Vector2[length];
            return buffer;
        }

        // ── Ground sight ───────────────────────────────────────────────────────
        //
        // THE SOLVE IS THROTTLED AND THE PIPPER IS NOT. Walking a 2 km trajectory
        // costs a linecast every 25 m, which is not a per-frame expense for a HUD
        // mark; but re-projecting a world point IS nearly free, so the impact point
        // is solved every few frames and re-projected every frame. The pipper
        // therefore tracks head movement and aircraft motion smoothly while the
        // underlying solution updates at about 20 Hz, which is far faster than a
        // gun run changes.
        private const int GroundSolveIntervalFrames = 3;

        /// <summary>
        /// How far the CCIP solve looks for an impact, in metres. The global config
        /// value unless this airframe has an override - see
        /// <see cref="PerAircraftRange"/> for why the A-19 needed one.
        /// </summary>
        private float _groundSightMaxRange;

        private int                  _groundSolveFrame;

        // ═══════════════════════════════════════════════════════════════════════
        //  THE PIPPER IS SMOOTHED BECAUSE THE SOLVE IS THROTTLED, 2026-09-17.
        //
        //  Owner, having flown it: the ground CCIP "does work but is not moving
        //  smoothly, as i turn its movements feel snappy and not smooth."
        //
        //  IT IS THE THROTTLE, NOT THE SOLVER. GroundSolveIntervalFrames is 3, so
        //  the impact point is recomputed on one frame in three and holds perfectly
        //  still on the other two. The projection runs every frame, so camera
        //  movement is already smooth - what steps is the WORLD POINT, and it steps
        //  by however far the aircraft's nose swept in 50 ms. In a hard turn that is
        //  a visible jump three times a second, which is exactly "snappy".
        //
        //  SMOOTHING THE POINT RATHER THAN SOLVING MORE OFTEN. The throttle exists
        //  because a solve is up to 125 integration steps and a batch of linecasts;
        //  running it every frame to fix an appearance problem would triple that
        //  cost for no extra accuracy. An exponential filter over roughly one solve
        //  interval fills the gaps with the motion that is actually happening, and
        //  it settles on the true point whenever the aircraft stops turning.
        //
        //  IT DOES NOT SMOOTH ACROSS A DISCONTINUITY. When the solution appears, or
        //  when it jumps further than a smooth sweep could account for - a ridge
        //  line, a building edge, a target moving out from under the reticle - the
        //  point is taken as-is. Sliding the pipper across a valley over a fifth of
        //  a second would be a lie about where the rounds land, which is worse than
        //  a step.
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>The drawn impact point, which chases the solved one.</summary>
        private Vector3 _groundPointSmoothed;
        private bool    _groundPointValid;

        /// <summary>
        /// The filter's time constant, in seconds.
        ///
        /// RAISED FROM 0.045 TO 0.07 ON 2026-09-17. At one solve interval the filter
        /// is nearly caught up when the next step arrives, so most of the step is
        /// still in the picture and the 20 Hz ripple survives - smoother than a raw
        /// step and still readable as one. Half as much again spreads each step over
        /// the whole gap and the ripple goes. The cost is about one frame of lag on a
        /// sustained sweep, which is not an accuracy loss: the filter converges on the
        /// solved point whenever the turn stops, and the solved point is untouched.
        /// </summary>
        private const float GroundPipperSmoothingSeconds = 0.07f;

        /// <summary>
        /// Beyond this, a move is a discontinuity rather than a sweep and is taken
        /// whole - so the pipper steps instead of sliding across a valley.
        ///
        /// IT IS AN ANGLE NOW, NOT 40 METRES, AND THAT IS WHY IT WAS STILL JANKY.
        /// Owner, having flown the smoothed build, 2026-09-17: "it is still a bit
        /// janky." A flat 40 m is a threshold in the wrong units. The impact point's
        /// travel between two solves is the aircraft's angular sweep times the RANGE,
        /// so at 1500 m an ordinary turn moves it well past 40 m - which tripped the
        /// discontinuity test and took the point whole, bypassing the filter
        /// entirely. The smoothing was being switched off in exactly the case the
        /// owner was complaining about, and doing nothing in the case he was not.
        ///
        /// 60 milliradians is about three and a half degrees of sweep between solves,
        /// far more than any airframe here turns in 50 ms and far less than the jump
        /// off a ridge line or a building edge, which is what the test is FOR.
        /// </summary>
        private const float GroundPipperSnapMilliradians = 60f;
        private GroundSight.Solution _groundSolution = GroundSight.Solution.None;

        /// <summary>
        /// Angular radius the CCIP pipper is drawn at, in milliradians. Fixed in
        /// ANGLE rather than in pixels so it keeps the same apparent size when the
        /// player zooms, the way every other mark on this HUD behaves.
        /// </summary>
        private const float GroundPipperMilliradians = 9f;

        /// <summary>
        /// Radius of the LCOS lead dot, in pixels. It is a dot, not a ring, and a
        /// dot that scales with anything reads as a bug.
        /// </summary>
        private const float LeadDotRadiusPixels = 3.5f;

        /// <summary>
        /// Absolute ceiling on how far off the spine a target may be and still count
        /// as a firing solution, in pixels at 1080p. See the second half of the
        /// solution test for why the wall width alone was not enough.
        /// </summary>
        private const float FiringSolutionMaxPixels = 60f;

        private bool _loggedRbNull;
        private int  _diagFrame;
        private const int DiagIntervalFrames = 120;

        // Damped turn rate used for spine sampling, updated in UpdateFunnel().
        private Vector3 _smoothedAngularVel;
        private bool    _smoothingInitialized;

        // Distance used to project the zero-lead pipper point.
        private const float BoresightProjectionDistance = 3000f;

        private static FieldInfo? _markersField;
        private static FieldInfo MarkersField =>
            _markersField ??= typeof(CombatHUD).GetField(
                "markers", BindingFlags.NonPublic | BindingFlags.Instance);

        // Gun.muzzles (private Transform[]) is where the game actually spawns each
        // bullet from (Gun.SpawnBullet -> bulletSim.AddBullet(transform.transform, ...)).
        // Stock nose cannons sit close enough to the aircraft pivot that ignoring this
        // was invisible; a modded aircraft with wing-root or wingtip guns puts the real
        // muzzle a meter or more off the pivot, which is enough to miss a snug funnel.
        private static FieldInfo? _muzzlesField;
        private static FieldInfo MuzzlesField =>
            _muzzlesField ??= typeof(Gun).GetField(
                "muzzles", BindingFlags.NonPublic | BindingFlags.Instance);

        // Gun.muzzleVelocity (private float) is the speed the game ACTUALLY launches
        // the round at: Gun.SpawnBullet passes `vector + muzzle.forward * muzzleVelocity`
        // into BulletSim.AddBullet. It is seeded from `info.muzzleVelocity` in Gun.Awake
        // and then diverges from it in two ways the funnel has to follow:
        //
        //   * Gun.LoadAmmunition REPLACES `info` with the ammo mount's own WeaponInfo,
        //     and it runs after Awake. A gun fed from a `GunAmmo` WeaponMount therefore
        //     fires at the PREFAB's nominal speed while carrying the MOUNT's WeaponInfo.
        //     No stock aircraft gun uses a GunAmmo mount - only four turret and ground
        //     mounts in the whole game do - so this is invisible on stock aircraft and
        //     bites exactly the modded ones.
        //   * Gun.Heat.Update writes it down as the barrel heats
        //     (`muzzleVelocity = info.muzzleVelocity - velocityDegradation * overheat`),
        //     so a gun held on the trigger is slower than its data sheet.
        //
        // Reading the field each frame is right for both. The drag denominator stays
        // `info.muzzleVelocity`, because TrajectoryTrace divides by that regardless.
        private static FieldInfo? _muzzleVelocityField;
        private static FieldInfo MuzzleVelocityField =>
            _muzzleVelocityField ??= typeof(Gun).GetField(
                "muzzleVelocity", BindingFlags.NonPublic | BindingFlags.Instance);

        // ── Initialization ─────────────────────────────────────────────────────

        public void Initialize(
            Aircraft         aircraft,
            WeaponStation    weaponStation,
            FunnelConfig     config,
            WingspanDatabase wingspanDb)
        {
            _aircraft      = aircraft;
            _weaponStation = weaponStation;
            _config        = config;
            _wingspanDb    = wingspanDb;

            // Prefer each barrel's own muzzle transform (position and forward) over
            // the owning Gun/Weapon component's transform, since that's what the game
            // itself fires from. Falls back to the weapon transform for any weapon
            // where muzzles can't be read (non-Gun weapon, or the field lookup fails).
            var dirSum = Vector3.zero;
            var posSum = Vector3.zero;
            int muzzleCount = 0;

            foreach (Weapon w in weaponStation.Weapons)
            {
                Transform[]? muzzles = w is Gun gun
                    ? MuzzlesField?.GetValue(gun) as Transform[]
                    : null;

                if (muzzles != null && muzzles.Length > 0)
                {
                    foreach (Transform m in muzzles)
                    {
                        dirSum += m.forward;
                        posSum += aircraft.transform.InverseTransformPoint(m.position);
                        muzzleCount++;
                    }
                }
                else
                {
                    dirSum += w.transform.forward;
                    posSum += aircraft.transform.InverseTransformPoint(w.transform.position);
                    muzzleCount++;
                }
            }

            _gunDirectionLocal = dirSum.sqrMagnitude > 0.001f
                ? aircraft.transform.InverseTransformDirection(dirSum)
                : Vector3.forward;

            _gunOriginLocal = muzzleCount > 0 ? posSum / muzzleCount : Vector3.zero;

            // The gun whose ballistics the funnel is drawn for. WeaponStation.WeaponInfo
            // is assigned `Weapons[0].info`, so the first Gun on the station is the one
            // the station already speaks for - but the station's copy is a snapshot
            // taken at RegisterWeapon/AssessAmmo time, and the weapon's own `info` is
            // live. Hold the weapon.
            foreach (Weapon w in weaponStation.Weapons)
            {
                if (w is Gun g) { _primaryGun = g; break; }
            }

            // Resolved once per injection rather than per solve: a clause list is
            // parsed and an aircraft does not change identity under a funnel.
            _groundSightMaxRange = PerAircraftRange.Resolve(aircraft, config);

            LogStationBallistics(weaponStation);

            _renderer = gameObject.AddComponent<FunnelRenderer>();
        }

        // The WeaponInfo the game's own BulletSim will use for this station's rounds.
        // Prefer the weapon's live `info` over the station's snapshot of it.
        private WeaponInfo? ResolveWeaponInfo()
        {
            if (_primaryGun != null && _primaryGun.info != null) return _primaryGun.info;
            return _weaponStation?.WeaponInfo;
        }

        // The speed the round is actually launched at - see MuzzleVelocityField.
        private float ResolveMuzzleVelocity(WeaponInfo? info)
        {
            float nominal = info != null ? info.muzzleVelocity : 0f;

            if (_primaryGun != null &&
                MuzzleVelocityField?.GetValue(_primaryGun) is float live &&
                live > 1f)
                return live;

            return nominal;
        }

        // Weapons already reported on, so the line is written once per gun type per
        // session rather than on every station select. Static, because a new
        // FunnelGunSight is built each time a station is shown.
        private static readonly HashSet<string> _ballisticsLogged = new HashSet<string>();

        // ONE LINE PER GUN TYPE, AND IT IS NOT BEHIND DebugLogging ANY MORE.
        //
        // It was, and that is exactly why the sortie of 2026-09-16 did not settle
        // anything: the owner flew the Aryx F-16M King Viper with the fix in, the
        // funnel injected for its cannon, and the log has no ballistics line in it
        // because the switch was off. A diagnostic that needs to be armed in advance
        // only fires on the flight where somebody already suspected the answer.
        //
        // The cost of un-gating it is one line per gun type per session. The value
        // is that the NEXT ordinary flight settles whether the modded-gun report was
        // the GunAmmo muzzle-velocity split, with nothing for the owner to turn on.
        private void LogStationBallistics(WeaponStation station)
        {
            if (_config == null) return;

            var log = FunnelGunSightPlugin.Instance?.Logger;
            if (log == null) return;

            string gunKey = ResolveWeaponInfo()?.weaponName ?? $"station{station.Number}";
            if (!_ballisticsLogged.Add(gunKey)) return;

            WeaponInfo? info    = ResolveWeaponInfo();
            float       live    = ResolveMuzzleVelocity(info);
            float       nominal = info != null ? info.muzzleVelocity : 0f;

            log.LogInfo(
                $"[FunnelGunSight] station {station.Number}: weapon={info?.weaponName ?? "<null>"} " +
                $"launchVelocity={live:F1} infoMuzzleVelocity={nominal:F1} " +
                $"dragCoef={(info != null ? info.dragCoef : 0f):F3} " +
                $"gravMult={(info != null ? info.gravMult : 0f):F3} " +
                $"weapons={station.Weapons.Count} fixedDeltaTime={Time.fixedDeltaTime:F4}");

            if (info != null && Mathf.Abs(live - nominal) > 1f)
                log.LogWarning(
                    $"[FunnelGunSight] station {station.Number}: the gun fires at {live:F1} m/s but its " +
                    $"WeaponInfo says {nominal:F1} m/s. The funnel follows the gun. This is what a " +
                    "GunAmmo weapon mount does, and it is the likely cause of a modded gun reading wrong.");

            // A station carrying two different guns has one funnel and two ballistics.
            // No stock aircraft does this; a modded one can.
            foreach (Weapon w in station.Weapons)
            {
                if (w is Gun g && g.info != null && info != null && g.info != info)
                {
                    log.LogWarning(
                        $"[FunnelGunSight] station {station.Number} mixes weapon types " +
                        $"({info.weaponName} and {g.info.weaponName}). The funnel is drawn for " +
                        $"{info.weaponName} only.");
                    break;
                }
            }
        }

        // ── Unity lifecycle ────────────────────────────────────────────────────

        private void LateUpdate()
        {
            try   { UpdateFunnel(); }
            catch (System.Exception ex)
            {
                FunnelGunSightPlugin.Instance?.Logger.LogError(
                    $"[FunnelGunSight] LateUpdate error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void OnDestroy()
        {
            if (_renderer != null) Destroy(_renderer);
        }

        // Converts a time constant in seconds to a per-frame lerp factor
        // (framerate-independent exponential decay). 0 disables smoothing.
        private static float SmoothingAlpha(float timeConstantSeconds, float deltaTime)
        {
            if (timeConstantSeconds <= 0f) return 1f;
            return 1f - Mathf.Exp(-deltaTime / timeConstantSeconds);
        }

        // ── Core update ────────────────────────────────────────────────────────

        private void UpdateFunnel()
        {
            if (SceneSingleton<FlightHud>.i == null) return;

            var camManager = SceneSingleton<CameraStateManager>.i;
            if (camManager == null) return;

            Camera camera = camManager.mainCamera;
            if (camera == null) return;

            if (_aircraft == null || !_aircraft.gameObject.activeInHierarchy) return;
            if (_weaponStation == null || _config == null) return;

            Vector3 gunWorldDir    = _aircraft.transform.TransformDirection(_gunDirectionLocal);
            Vector3 gunOriginWorld = _aircraft.transform.TransformPoint(_gunOriginLocal);

            // primaryTarget: any tier, used for wingspan. rangeTarget: Tier 1/2 only,
            // used for the range dot. Tier 3 (boresight cone) affects width only.
            Unit? primaryTarget = null;
            Unit? rangeTarget   = null;

            var combatHud = SceneSingleton<CombatHUD>.i;
            if (combatHud != null)
            {
                List<Unit> weaponTargets = combatHud.GetTargetList();
                if (weaponTargets != null && weaponTargets.Count > 0)
                {
                    primaryTarget = weaponTargets[0];  // Tier 1
                    rangeTarget   = primaryTarget;
                }
                else if (MarkersField?.GetValue(combatHud) is List<HUDUnitMarker> markers)
                {
                    // Tier 2 - HUD-cursor-highlighted marker.
                    foreach (HUDUnitMarker m in markers)
                    {
                        if (m.selected && m.unit is Aircraft && m.unit != _aircraft)
                        {
                            primaryTarget = m.unit;
                            rangeTarget   = m.unit;
                            break;
                        }
                    }

                    // Tier 3 - closest hostile in boresight cone (wingspan only).
                    // _config is guaranteed non-null by the guard above; using `?.`
                    // here made the compiler treat it as nullable again for the rest
                    // of the method, which is where the CS8602 on the wingspan line
                    // came from.
                    if (primaryTarget == null && _config.AutoTargetNearestEnemy.Value)
                    {
                        float bestDot = Mathf.Cos(30f * Mathf.Deg2Rad);
                        foreach (HUDUnitMarker m in markers)
                        {
                            if (!(m.unit is Aircraft c) || c == _aircraft) continue;
                            if (c.NetworkHQ == _aircraft.NetworkHQ)         continue;

                            float dot = Vector3.Dot(
                                (c.transform.position - _aircraft.transform.position).normalized,
                                gunWorldDir);
                            if (dot > bestDot) { bestDot = dot; primaryTarget = c; }
                        }
                    }
                }
            }

            // ── Wingspan ───────────────────────────────────────────────────────
            float sizeCap  = _config.MaxTargetSizeMeters.Value;
            float wingspan = _config.DefaultWingspan.Value;
            if (primaryTarget != null &&
                _config.WingspanModeSetting.Value == WingspanMode.Adaptive)
            {
                // CAPPED, AND THE CAP IS WHY ADAPTIVE MODE IS SAFE TO LEAVE ON.
                // The wingspan database learns whatever it is pointed at, and the
                // Tier 3 fallback points it at the nearest hostile in the boresight
                // cone - which over a base is a hangar. See MaxTargetSizeMeters.
                wingspan = Mathf.Min(
                    _wingspanDb!.GetWingspan(primaryTarget, _config.DefaultWingspan.Value),
                    sizeCap);
            }

            // ── Turn rate ─────────────────────────────────────────────────────
            // World-space negation corrects both the FBW yaw inversion and Unity's
            // left-hand pitch convention, and stays correct at any roll angle.
            var angularVel = Vector3.zero;
            if (_aircraft.rb != null)
            {
                angularVel = _config.InvertTurnDirection.Value
                    ? -_aircraft.rb.angularVelocity
                    : _aircraft.rb.angularVelocity;
            }
            else if (!_loggedRbNull)
            {
                _loggedRbNull = true;
                FunnelGunSightPlugin.Instance?.Logger.LogDebug(
                    "[FunnelGunSight] Aircraft Rigidbody is null.");
            }

            // ── Predictive tracking (optional) ──────────────────────────────────
            // Line-of-sight rate: -(r x v_rel) / |r|^2, same quantity proportional
            // navigation guidance uses. Same sign convention as angularVel above.
            // Blended in by range so noisy close-range readings don't dominate.
            if (_config.EnablePredictiveTracking.Value && rangeTarget != null &&
                rangeTarget.rb != null && _aircraft.rb != null)
            {
                Vector3 relPos  = rangeTarget.transform.position - _aircraft.transform.position;
                float   minR    = _config.MinRangeMeters.Value;
                float   rangeSq = relPos.sqrMagnitude;

                if (rangeSq > minR * minR)
                {
                    Vector3 relVel  = rangeTarget.rb.velocity - _aircraft.rb.velocity;
                    Vector3 losRate = -Vector3.Cross(relPos, relVel) / rangeSq;

                    float range      = Mathf.Sqrt(rangeSq);
                    float autoWeight = Mathf.InverseLerp(
                        _config.PredictiveTrackingMinRange.Value,
                        _config.PredictiveTrackingMaxRange.Value,
                        range);
                    float blendWeight = _config.PredictiveTrackingStrength.Value * autoWeight;

                    angularVel = Vector3.Lerp(angularVel, losRate, blendWeight);
                }
            }

            // ── Turn-rate smoothing ──────────────────────────────────────────
            if (!_smoothingInitialized)
                _smoothedAngularVel = angularVel; // snap on first frame
            else
                _smoothedAngularVel = Vector3.Lerp(
                    _smoothedAngularVel, angularVel,
                    SmoothingAlpha(_config.TurnRateSmoothing.Value, Time.deltaTime));
            _smoothingInitialized = true;
            angularVel = _smoothedAngularVel;

            // ── Spine sampling ────────────────────────────────────────────────
            WeaponInfo? ballisticInfo = ResolveWeaponInfo();

            // Every sight in this mod integrates the same round the same way. See
            // Ballistics: the launch speed and the drag denominator are two
            // different numbers on purpose.
            var ballistics = new Ballistics.Inputs(
                ResolveMuzzleVelocity(ballisticInfo), ballisticInfo);

            (Vector3 point, float range)[] spine = PlaneOfMotionSampler.Sample(
                gunOriginWorld,
                gunWorldDir,
                in ballistics,
                angularVel,
                _aircraft.rb?.velocity ?? Vector3.zero,
                _config.BallisticSimulationSteps.Value,
                _config.FunnelResolution.Value,
                _config.MinRangeMeters.Value,
                _config.MaxRangeMeters.Value,
                _config.MinTurnRate.Value,
                out Vector3 spineAxis);

            // ── Screen projection ─────────────────────────────────────────────
            // WorldToScreenPoint has (0,0) at bottom-left; FunnelRenderer.Fy()
            // flips Y for GL.LoadPixelMatrix's top-left origin.
            Vector3 boresightWorld = gunOriginWorld
                                     + gunWorldDir * BoresightProjectionDistance;
            Vector3 bsp            = camera.WorldToScreenPoint(boresightWorld);

            bool gearDown     = _config.HideWithGearDown.Value && _aircraft.gearDeployed;
            bool crossVisible = bsp.z > 0f && !gearDown;

            Vector2 boresightScreen = new Vector2(bsp.x, bsp.y);

            // A point behind the camera projects to a mirrored, meaningless pixel, so
            // it cannot be drawn. It used to blank the WHOLE funnel: one bad point out
            // of fifty and the sight vanished. In an external view, or in a hard pull
            // with a wide field of view, the near end of the spine crosses behind the
            // camera plane routinely, so the sight blinked out exactly when it was being
            // used hardest. Only the points behind the camera are dropped now - the
            // funnel keeps the longest run that is in front and draws that.
            Vector2[] screenArc = EnsureBuffer(ref _screenArcBuffer, spine.Length);

            int runStart = 0, runCount = 0;
            int curStart = 0, curCount = 0;

            for (int i = 0; i < spine.Length; i++)
            {
                Vector3 sp = camera.WorldToScreenPoint(spine[i].point);

                if (sp.z > 0f)
                {
                    screenArc[i] = new Vector2(sp.x, sp.y);
                    if (curCount == 0) curStart = i;
                    curCount++;
                    if (curCount > runCount) { runStart = curStart; runCount = curCount; }
                }
                else
                {
                    curCount = 0;
                }
            }

            bool spineOk = runCount >= 2;

            // ── Diagnostics ───────────────────────────────────────────────────
            bool shouldLogDiag = _config.DebugLogging.Value && ++_diagFrame >= DiagIntervalFrames;
            if (shouldLogDiag)
            {
                _diagFrame = 0;
                string tier = rangeTarget != null ? "dot" :
                              primaryTarget != null ? "III" : "II";
                var log = FunnelGunSightPlugin.Instance?.Logger;
                log?.LogInfo($"[FunnelDiag] tier={tier} " +
                             $"target={primaryTarget?.unitName ?? "none"}");
                log?.LogInfo($"[FunnelDiag] angVel={angularVel} " +
                             $"mag={angularVel.magnitude:F4}");
                log?.LogInfo($"[FunnelDiag] boresightScreen={boresightScreen} " +
                             $"visible={crossVisible} gunDir={gunWorldDir}");
                log?.LogInfo($"[FunnelDiag] wingspan={wingspan:F2}m " +
                             $"spineLen={spine.Length} firstRange={spine[0].range:F1} " +
                             $"lastRange={spine[spine.Length - 1].range:F1}");
            }

            // ── The sights that do not need the spine ─────────────────────────
            //
            // Solved before the early return below, because the ground pipper is a
            // straight-down-the-barrel mark: it is still correct on the frames
            // where the funnel's own spine has gone behind the camera, and blanking
            // it with the spine would make it blink out in exactly the external
            // views and hard pulls that already cost the funnel its walls once.
            float focalPx =
                Screen.height * 0.5f /
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            SolveGroundSight(gunOriginWorld, gunWorldDir, in ballistics);
            ProjectGroundSight(
                camera, focalPx,
                out Vector2? groundPipper, out float groundRadius, out string? groundLabel);

            if (!spineOk)
            {
                var partial = new SightDrawData
                {
                    GunCross     = boresightScreen,
                    IsVisible    = crossVisible,
                    ShowWalls    = false,
                    GroundPipper = groundPipper,
                    GroundPipperRadius = groundRadius,
                    GroundKind = _groundSolution.Kind,
                    GroundIsTarget = _groundSolution.IsTarget,
                    GroundLabel  = groundLabel
                };
                _renderer?.SetDrawData(in partial);
                return;
            }

            // ── Wall geometry ─────────────────────────────────────────────────
            // Wall half-width at each point is the angular size of the target
            // wingspan at that range. Offset direction is the global screen-space
            // perpendicular of the spine, which stays stable even at low turn rate.
            int n = runCount;

            float focalLengthPx = focalPx;

            Vector2 spineScreenVec = screenArc[runStart + n - 1] - screenArc[runStart];
            Vector2 perpDir = spineScreenVec.sqrMagnitude > 0.01f
                ? new Vector2(-spineScreenVec.y, spineScreenVec.x).normalized
                : new Vector2(1f, 0f);

            // ── Aspect-aware sizing ───────────────────────────────────────────
            //
            // The walls are stepped sideways along `perpDir` in PIXELS, so the
            // dimension the player is being asked to fit is whatever that pixel
            // direction corresponds to in the world. Measuring the target's extent
            // along exactly that direction is what makes the walls mean something at
            // every aspect instead of only on the beam. See TargetExtent.
            float effectiveSpan = wingspan;
            if (_config.AspectAwareSizing.Value && primaryTarget != null)
            {
                Vector3 perpWorld = TargetExtent.ScreenPerpToWorld(camera, perpDir);
                effectiveSpan = TargetExtent.ApparentWidth(
                    primaryTarget, perpWorld, wingspan, sizeCap);
            }

            float halfWingspanMeters = effectiveSpan * 0.5f;

            Vector2[] leftWall  = EnsureBuffer(ref _leftWallBuffer,  spine.Length);
            Vector2[] rightWall = EnsureBuffer(ref _rightWallBuffer, spine.Length);

            for (int i = 0; i < n; i++)
            {
                int   s      = runStart + i;
                float halfPx = halfWingspanMeters / spine[s].range * focalLengthPx;
                leftWall[i]  = screenArc[s] - perpDir * halfPx;
                rightWall[i] = screenArc[s] + perpDir * halfPx;
            }

            if (shouldLogDiag)
            {
                float pixelGapNear = Vector2.Distance(leftWall[0], rightWall[0]);
                float pixelGapFar  = Vector2.Distance(leftWall[n - 1], rightWall[n - 1]);
                var   log          = FunnelGunSightPlugin.Instance?.Logger;
                log?.LogInfo($"[FunnelDiag] halfWingspanMeters={halfWingspanMeters:F2} " +
                             $"pixelGapNear={pixelGapNear:F1} pixelGapFar={pixelGapFar:F1} " +
                             $"fov={camera.fieldOfView:F1} screenH={Screen.height}");
            }

            // ── Range dot ─────────────────────────────────────────────────────
            int      dotIdx    = -1;
            Vector2? dotPos    = null;
            float    dotRadius = 0f;

            if (rangeTarget != null && _config.ShowRangeDot.Value)
            {
                float targetRange = Vector3.Distance(
                    _aircraft.transform.position, rangeTarget.transform.position);

                int firstS = runStart;
                int lastS  = runStart + n - 1;

                if (targetRange >= spine[firstS].range && targetRange <= spine[lastS].range)
                {
                    for (int i = 0; i < n - 1; i++)
                    {
                        int s = runStart + i;
                        if (spine[s].range <= targetRange &&
                            targetRange    <= spine[s + 1].range)
                        {
                            float t   = Mathf.InverseLerp(
                                spine[s].range, spine[s + 1].range, targetRange);
                            dotPos    = Vector2.Lerp(screenArc[s], screenArc[s + 1], t);
                            float halfPx = (effectiveSpan * 0.5f / Mathf.Max(targetRange, 1f))
                                           * focalLengthPx;
                            dotRadius = halfPx * _config.RangeDotSize.Value;
                            dotIdx    = i;
                            break;
                        }
                    }
                }
                else if (targetRange > spine[lastS].range)
                {
                    dotPos = screenArc[lastS];
                    float halfPx = (effectiveSpan * 0.5f / Mathf.Max(spine[lastS].range, 1f))
                                   * focalLengthPx;
                    dotRadius = halfPx * _config.RangeDotSize.Value;
                    dotIdx    = n - 1;
                }
                // Inside MinRange: no dot, target too close to engage.
            }

            // ── Firing solution cue ──────────────────────────────────────────
            bool inSolution = false;
            if (_config.FlashOnFiringSolution.Value && rangeTarget != null &&
                dotPos.HasValue && dotIdx >= 0)
            {
                Vector3 targetSP = camera.WorldToScreenPoint(rangeTarget.transform.position);
                if (targetSP.z > 0f)
                {
                    var   targetScreen  = new Vector2(targetSP.x, targetSP.y);
                    float wallHalfWidth = Vector2.Distance(leftWall[dotIdx], rightWall[dotIdx]) * 0.5f;
                    float distToSpine   = Vector2.Distance(targetScreen, screenArc[runStart + dotIdx]);

                    // AND THE TARGET HAS TO BE NEAR THE RANGE DOT AS WELL AS INSIDE
                    // THE WALLS. Owner, having flown 1.2.0: the funnel "was flashing
                    // to fire while the target was far to the side."
                    //
                    // The wall half-width was the whole test, so a wide funnel said
                    // yes to a target anywhere it overlapped - and 1.2.0's broken
                    // aspect sizing made the walls screen-wide, which turned "inside
                    // the walls" into "on the HUD". The sizing bug is fixed in
                    // TargetExtent, but the test was too loose on its own and would
                    // have flashed early on any genuinely large target too: a
                    // firing solution means the rounds and the target arrive
                    // together, not that the target is somewhere in a wide cone.
                    //
                    // The second condition is an absolute ceiling in pixels, so it
                    // binds exactly when the walls have opened up and does nothing
                    // at ordinary gun range.
                    float capPx = FiringSolutionMaxPixels;
                    inSolution = distToSpine < Mathf.Min(wallHalfWidth, capPx);
                }
            }

            // ── Lead pipper (LCOS) ────────────────────────────────────────────
            //
            // The funnel's own maths evaluated at ONE range instead of fifty. With
            // a lock that range is measured; without one it is the configured
            // assumption. That is the whole difference between families 2 and 3 of
            // the sight survey, so both are this one block.
            Vector2? leadDot = null;
            bool leadDotMeasured = false;
            if (_config.ShowLeadDot.Value)
            {
                leadDotMeasured = rangeTarget != null;
                float assumedRange = rangeTarget != null
                    ? Vector3.Distance(_aircraft.transform.position, rangeTarget.transform.position)
                    : Mathf.Clamp(
                        _config.LeadDotRange.Value,
                        FunnelConfig.LeadDotRangeMin, FunnelConfig.LeadDotRangeMax);

                assumedRange = Mathf.Clamp(
                    assumedRange, _config.MinRangeMeters.Value, _config.MaxRangeMeters.Value);

                // The spine already IS the lead solution at every sampled range, so
                // the dot is an interpolation along it rather than a second solve.
                int firstS = runStart, lastS = runStart + n - 1;
                if (assumedRange >= spine[firstS].range && assumedRange <= spine[lastS].range)
                {
                    for (int i = 0; i < n - 1; i++)
                    {
                        int s = runStart + i;
                        if (spine[s].range <= assumedRange && assumedRange <= spine[s + 1].range)
                        {
                            float t = Mathf.InverseLerp(
                                spine[s].range, spine[s + 1].range, assumedRange);
                            leadDot = Vector2.Lerp(screenArc[s], screenArc[s + 1], t);
                            break;
                        }
                    }
                }
            }

            // ── Range readout ─────────────────────────────────────────────────
            //
            // IT IS NO LONGER ATTACHED TO THE RANGE DOT. Owner, having flown it:
            // "range readout is appearing inside the range dot, but it in fixed
            // position near the centered cross instead." The dot moves along the
            // spine with the target's range, so the number moved with it - which is
            // the one thing a readout must not do, because reading it meant hunting
            // for it. It now sits at a fixed offset from the boresight cross, where
            // the eye already is.
            string? rangeLabel = null;
            if (_config.ShowRangeReadout.Value && rangeTarget != null)
                rangeLabel = FormatRange(Vector3.Distance(
                    _aircraft.transform.position, rangeTarget.transform.position));

            var draw = new SightDrawData
            {
                GunCross           = boresightScreen,
                LeftWall           = leftWall,
                RightWall          = rightWall,
                WallCount          = n,
                IsVisible          = crossVisible,
                ShowWalls          = _config.ShowFunnel.Value,
                DotPos             = _config.ShowFunnel.Value ? dotPos : null,
                DotRadius          = dotRadius,
                InSolution         = inSolution,
                GroundPipper       = groundPipper,
                GroundPipperRadius = groundRadius,
                GroundKind         = _groundSolution.Kind,
                GroundIsTarget     = _groundSolution.IsTarget,
                LeadDot            = leadDot,
                LeadDotRadius      = LeadDotRadiusPixels,
                LeadDotMeasured    = leadDotMeasured,
                RangeLabel         = rangeLabel,
                GroundLabel        = groundLabel
            };

            _renderer?.SetDrawData(in draw);
        }

        // ── The sight that reads straight down the barrel ───────────────────────

        /// <summary>
        /// Re-solves the CCIP impact point every
        /// <see cref="GroundSolveIntervalFrames"/> frames. See the field note: the
        /// linecasts are the expense, the projection is not.
        /// </summary>
        private void SolveGroundSight(
            Vector3 gunOriginWorld, Vector3 gunWorldDir, in Ballistics.Inputs ballistics)
        {
            if (_config == null || !_config.ShowGroundSight.Value)
            {
                _groundSolution = GroundSight.Solution.None;
                return;
            }

            if (++_groundSolveFrame < GroundSolveIntervalFrames) return;
            _groundSolveFrame = 0;

            _groundSolution = GroundSight.Solve(
                gunOriginWorld,
                gunWorldDir,
                _aircraft?.rb?.velocity ?? Vector3.zero,
                in ballistics,
                _aircraft != null ? _aircraft.maxRadius : 0f,
                _groundSightMaxRange);
        }

        private void ProjectGroundSight(
            Camera camera, float focalPx,
            out Vector2? pipper, out float radius, out string? label)
        {
            pipper = null;
            radius = 0f;
            label  = null;

            if (_config == null || !_groundSolution.Hit)
            {
                _groundPointValid = false;
                return;
            }

            Vector3 target = _groundSolution.Point;

            // The threshold is an angle at the impact point's own range, so it means
            // the same thing on a 300 m strafing pass and a 1500 m stand-off one.
            float snapMetres = GroundPipperSnapMilliradians * 0.001f
                               * Mathf.Max(_groundSolution.Range, 1f);

            if (!_groundPointValid ||
                (target - _groundPointSmoothed).sqrMagnitude > snapMetres * snapMetres)
            {
                _groundPointSmoothed = target;
                _groundPointValid    = true;
            }
            else
            {
                // Frame-rate independent: the same fraction of the remaining gap per
                // SECOND, not per frame, so the pipper behaves the same at 30 fps and
                // at 144.
                float k = 1f - Mathf.Exp(-Time.deltaTime / GroundPipperSmoothingSeconds);
                _groundPointSmoothed = Vector3.Lerp(_groundPointSmoothed, target, k);
            }

            Vector3 sp = camera.WorldToScreenPoint(_groundPointSmoothed);
            if (sp.z <= 0f) return;

            pipper = new Vector2(sp.x, sp.y);

            // A fixed angular size in pixels is `angle * focalLength`, which is what
            // keeps the pipper the same apparent size through a zoom.
            //
            // THE SIZE IS A SETTING NOW RATHER THAN THE CONSTANT, because the four
            // selectable shapes do not read at one size: the plain ring and dot is
            // legible at 9 mrad and the helo crosshair's ladder is sub-pixel there.
            // GroundPipperMilliradians remains the default the setting is bound to.
            radius = Mathf.Clamp(
                _config.GroundSightSize.Value,
                FunnelConfig.GroundSightSizeMin, FunnelConfig.GroundSightSizeMax)
                * 0.001f * focalPx;

            // THE LABEL NAMES WHAT WOULD BE HIT, NOT JUST HOW FAR IT IS. Owner,
            // 2026-09-17: "i want it to differentiate ground targets (buildings,
            // ground units and ships) from air targets." The flash says THAT it is
            // a target; this says WHICH, which is the half of the request the
            // flash on its own does not answer.
            string? range = _config.ShowRangeReadout.Value
                ? FormatRange(_groundSolution.Range)
                : null;

            string? kind = _config.MarkGroundTargetKind.Value
                ? KindLabel(_groundSolution.Kind)
                : null;

            label = (range, kind) switch
            {
                (null, null) => null,
                (null, _)    => kind,
                (_, null)    => range,
                _            => kind + " " + range,
            };
        }

        /// <summary>
        /// The word for a struck target, or null for the two things that are not
        /// targets. Bare terrain and open water get NO label on purpose: a strafing
        /// run spends most of its time over one or the other, and writing "TERRAIN"
        /// under the pipper for all of it is noise.
        /// </summary>
        private static string? KindLabel(GroundSight.TargetKind kind) => kind switch
        {
            GroundSight.TargetKind.Building      => "BLDG",
            GroundSight.TargetKind.GroundVehicle => "VEH",
            GroundSight.TargetKind.Ship          => "SHIP",
            GroundSight.TargetKind.Aircraft      => "AIR",
            _                                    => null,
        };

        /// <summary>
        /// Range in hundreds of metres, which is how a pilot calls it out: 700 m is
        /// "7". Under 100 m it degrades to two decimals of a hundred rather than
        /// printing "0", because inside 100 m the exact number is the only thing
        /// that matters.
        /// </summary>
        private static string FormatRange(float metres)
        {
            float hundreds = metres / 100f;
            return hundreds < 1f
                ? hundreds.ToString("F1")
                : Mathf.RoundToInt(hundreds).ToString();
        }
    }
}
