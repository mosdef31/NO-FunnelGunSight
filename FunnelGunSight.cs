using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace FunnelGunSight
{
    public sealed class FunnelGunSight : MonoBehaviour
    {
        private Aircraft?         _aircraft;
        private WeaponStation?    _weaponStation;
        private Vector3           _gunDirectionLocal; // aircraft-local, computed once
        private FunnelRenderer?   _renderer;
        private FunnelConfig?     _config;
        private WingspanDatabase? _wingspanDb;

        private bool _loggedRbNull;
        private int  _diagFrame;
        private const int DiagIntervalFrames = 120;

        // ── Smoothing state ────────────────────────────────────────────────────
        // _smoothedAngularVel damps the physics input (fixes twisting near zero
        // turn rate and violent snaps during brief low-speed maneuvers).
        private Vector3 _smoothedAngularVel;
        private bool    _smoothingInitialized;

        // Distance used to project the pure-boresight (zero-lead) gun cross.
        // Direction-only, so the exact value doesn't matter — large enough to
        // avoid parallax between the camera and the aircraft origin.
        private const float BoresightProjectionDistance = 3000f;

        // ── Reflected fields ───────────────────────────────────────────────────
        private static FieldInfo? _markersField;
        private static FieldInfo MarkersField =>
            _markersField ??= typeof(CombatHUD).GetField(
                "markers", BindingFlags.NonPublic | BindingFlags.Instance);

        // CombatHUD.weaponState — the live HUDWeaponState instance for whichever
        // weapon station is currently selected. When it's a HUDBoresightState
        // (the native gun pipper), we read its already-rendering boresight Image
        // position directly and anchor our funnel to it — see UpdateFunnel().
        private static FieldInfo? _weaponStateField;
        private static FieldInfo WeaponStateField =>
            _weaponStateField ??= typeof(CombatHUD).GetField(
                "weaponState", BindingFlags.NonPublic | BindingFlags.Instance);

        private static FieldInfo? _boresightImageField;
        private static FieldInfo BoresightImageField =>
            _boresightImageField ??= typeof(HUDBoresightState).GetField(
                "boresight", BindingFlags.NonPublic | BindingFlags.Instance);

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

            var dirSum = Vector3.zero;
            foreach (Weapon w in weaponStation.Weapons)
                dirSum += w.transform.forward;

            _gunDirectionLocal = aircraft.transform.InverseTransformDirection(dirSum);
            _renderer           = gameObject.AddComponent<FunnelRenderer>();
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

        /// <summary>
        /// Converts a smoothing time constant (seconds) into a per-frame Lerp
        /// factor, framerate-independent via exponential decay. A time constant
        /// of 0 disables smoothing entirely (returns 1 — snap straight to target).
        /// </summary>
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

            // Gun direction in world space — depends only on the aircraft, never
            // on the camera, so this is unaffected by free-look either way.
            Vector3 gunWorldDir = _aircraft.transform.TransformDirection(_gunDirectionLocal);

            // ── Target detection ───────────────────────────────────────────────
            // primaryTarget → any tier  → wingspan lookup.
            // rangeTarget   → Tier 1/2  → slant range for the range dot.
            // Tier 3 (boresight cone) provides wingspan only; it must not place a
            // range dot or influence angular velocity.
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
                    // Tier 2 — HUD-cursor-highlighted marker.
                    foreach (HUDUnitMarker m in markers)
                    {
                        if (m.selected && m.unit is Aircraft && m.unit != _aircraft)
                        {
                            primaryTarget = m.unit;
                            rangeTarget   = m.unit;
                            break;
                        }
                    }

                    // Tier 3 — closest hostile in boresight cone (wingspan only).
                // Disabled if EnableBoresightAutoTarget is off.
                if (primaryTarget == null && (_config?.EnableBoresightAutoTarget.Value ?? true))
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
            float wingspan = _config.DefaultWingspan.Value;
            if (primaryTarget != null &&
                _config.WingspanModeSetting.Value == WingspanMode.Adaptive)
                wingspan = _wingspanDb!.GetWingspan(primaryTarget, _config.DefaultWingspan.Value);

            // ── Angular velocity (own aircraft rate) ────────────────────────────
            // Negate the entire world-space angular velocity. This works because:
            //   • The FBW inverts yaw (rb.angularVelocity.y is sign-flipped).
            //     Negating the full vector corrects it without needing to decompose
            //     into local axes.
            //   • Unity's left-hand coordinate system makes the natural pitch
            //     direction negative; negating corrects it too.
            //   • Critically, negating in world space (not local space) is
            //     orientation-independent — it stays correct at any roll or bank
            //     angle. The previous approach of negating local.x and local.y
            //     separately was equivalent for level flight but broke when the
            //     aircraft was significantly rolled, because the local X axis then
            //     had a large world-Y component that was negated along with it,
            //     flipping the screen-space spine direction.
            var angularVel = Vector3.zero;
            if (_aircraft.rb != null)
            {
                // Section 6 — FBW compatibility: InvertAngularVelocity wraps the
                // negation so stock aircraft (or non-Firefly FBW mods) can disable it.
                angularVel = _config.InvertAngularVelocity.Value
                    ? -_aircraft.rb.angularVelocity
                    : _aircraft.rb.angularVelocity;
            }
            else if (!_loggedRbNull)
            {
                _loggedRbNull = true;
                FunnelGunSightPlugin.Instance?.Logger.LogDebug(
                    "[FunnelGunSight] Aircraft Rigidbody is null.");
            }

            // ── Level V — line-of-sight rate (optional) ─────────────────────────
            // Own-aircraft rate (above) answers "where do bullets go if I hold this
            // rotation?" — a correct snapshot, but it only equals the actual lead
            // requirement once you're already holding a perfect steady track. The
            // quantity that captures the target's own maneuvering too is the
            // rotation rate of the line of sight itself — the same measurement
            // proportional-navigation guidance uses:
            //
            //     ω_LOS = -(r × v_rel) / |r|²
            //
            // where r is relative position and v_rel is relative velocity. Negated
            // for the same reason the own-rate negation above is partly a Unity
            // left-hand-coordinate correction, not purely an FBW quirk: this is a
            // plain kinematic cross product, computed independently of
            // rb.angularVelocity, but it lands in the same "natural" axis
            // convention raw rb.angularVelocity used before its own negation — so
            // it needs the identical sign flip to land in the space Sample()
            // expects.
            //
            // Blended in (not swapped outright) via LevelVBlendWeight so users can
            // mix the two characters rather than being forced to pick one.
            // Restricted to rangeTarget (Tier 1/2 only) — Tier 3 boresight-cone
            // targets aren't a confirmed lock, same rule already applied to the
            // range dot above. Guarded by MinRangeMeters so point-blank range
            // can't blow up the division.
            //
            // Section 4 — range-based auto-blend: LOS rate is noisier at close
            // range (ω = v_perp / r amplifies errors at small r). Weight ramps
            // from 0 at LevelVMinRange to full LevelVBlendWeight at LevelVMaxRange.
            if (_config.EnableLevelV.Value && rangeTarget != null &&
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
                        _config.LevelVMinRange.Value,
                        _config.LevelVMaxRange.Value,
                        range);
                    float blendWeight = _config.LevelVBlendWeight.Value * autoWeight;

                    angularVel = Vector3.Lerp(angularVel, losRate, blendWeight);
                }
            }

            // ── Turn-rate damping ────────────────────────────────────────────
            // Raw angularVel is noisy, and near zero magnitude its direction
            // becomes numerically unstable (tiny noise dominates a near-zero
            // vector), which both twists the funnel at wings-level and makes it
            // snap violently during brief low-speed high-rate maneuvers.
            // Exponential smoothing here fixes both at the source — the spine
            // sampling below never sees the raw, noisy signal.
            if (!_smoothingInitialized)
                _smoothedAngularVel = angularVel; // snap on first frame, no slide-in
            else
                _smoothedAngularVel = Vector3.Lerp(
                    _smoothedAngularVel, angularVel,
                    SmoothingAlpha(_config.TurnRateDamping.Value, Time.deltaTime));
            _smoothingInitialized = true;
            angularVel = _smoothedAngularVel;

            // ── Spine sampling (world space, fixed to aircraft) ──────────────────
            // Section 2: pass aircraft velocity so Sample() can account for the
            // bullet's inherited speed component.
            // Section 3: pass WeaponInfo and BallisticSteps for Euler integration
            // of drag and gravity per spine point.
            (Vector3 point, float range)[] spine = PlaneOfMotionSampler.Sample(
                _aircraft.transform.position,
                gunWorldDir,
                _weaponStation.WeaponInfo.muzzleVelocity,
                angularVel,
                _aircraft.rb?.velocity ?? Vector3.zero,
                _weaponStation.WeaponInfo,
                _config.BallisticSteps.Value,
                _config.TrajectoryPoints.Value,
                _config.MinRangeMeters.Value,
                _config.MaxRangeMeters.Value,
                _config.MinAngularRate.Value,
                out Vector3 spineAxis);

            // ── Projection, anchored to the live native gun pipper ───────────────
            // HUDBoresightState (the game's own gun crosshair) computes its screen
            // position with the exact same formula we use here: gunDirectionRelative
            // → a point 3000 m out → raw mainCamera.WorldToScreenPoint, no
            // compensation. So our own raw projection should already numerically
            // match it. To guarantee that — and to automatically absorb any
            // coordinate-space difference between Unity's UI canvas and our own
            // GL/OnGUI drawing (DPI scaling, CanvasScaler, etc.) — we read the
            // live pipper's already-rendered position via reflection each frame,
            // measure the delta against our own computed value, and apply that
            // delta to everything we draw. If the native pipper isn't active for
            // this weapon (a different HUDWeaponState subclass), the delta is
            // zero and we fall back to our own raw projection.
            Vector3 boresightWorld = _aircraft.transform.position
                                      + gunWorldDir * BoresightProjectionDistance;
            Vector3 bs3 = camera.WorldToScreenPoint(boresightWorld);
            var     ourBoresightScreen = new Vector2(bs3.x, bs3.y);

            // Section 5 — gear-down hiding: match native boresight behaviour.
            // Aircraft.gearDeployed is a public bool field.
            bool gearDown     = _config.HideWithGearDown.Value && _aircraft.gearDeployed;
            bool crossVisible = bs3.z > 0f && !gearDown;

            Vector2 correction = Vector2.zero;
            bool    nativeAnchorFound = false;

            if (combatHud != null &&
                WeaponStateField?.GetValue(combatHud) is HUDBoresightState boresightState)
            {
                if (BoresightImageField?.GetValue(boresightState) is Image boresightImg &&
                    boresightImg != null)
                {
                    Vector3 nativePos = boresightImg.transform.position;
                    correction         = new Vector2(nativePos.x, nativePos.y) - ourBoresightScreen;
                    nativeAnchorFound  = true;
                }
            }

            Vector2 boresightScreen = ourBoresightScreen + correction;
            Vector2 totalOffset     = boresightScreen - ourBoresightScreen;

            var  screenArc = new Vector2[spine.Length];
            bool spineOk   = spine.Length >= 2;

            if (spineOk)
            {
                for (int i = 0; i < spine.Length; i++)
                {
                    Vector3 sp = camera.WorldToScreenPoint(spine[i].point);
                    if (sp.z < 0f)
                    {
                        // Behind the camera — shouldn't normally happen given
                        // MaxRangeMeters bounds, but guard against degenerate
                        // geometry anyway.
                        spineOk = false;
                        break;
                    }
                    screenArc[i] = new Vector2(sp.x, sp.y) + totalOffset;
                }
            }

            // ── Diagnostics ───────────────────────────────────────────────────
            bool shouldLogDiag = _config.DiagLogging.Value && ++_diagFrame >= DiagIntervalFrames;
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
                log?.LogInfo($"[FunnelDiag] nativeAnchorFound={nativeAnchorFound} " +
                             $"correction={correction} " +
                             $"boresightScreen={boresightScreen} visible={crossVisible}");
                log?.LogInfo($"[FunnelDiag] wingspan={wingspan:F2}m " +
                             $"spineLen={spine.Length} firstRange={spine[0].range:F1} " +
                             $"lastRange={spine[spine.Length - 1].range:F1}");
            }

            if (!spineOk)
            {
                _renderer?.SetDrawData(boresightScreen, null, null,
                    isVisible: false, dotPos: null, dotRadius: 0f, inSolution: false);
                return;
            }

            // ── Wall geometry (2D screen-space) ──────────────────────────────────
            // The walls are computed entirely in screen space, making the funnel
            // a true 2D HUD overlay regardless of camera angle.
            //
            // Previous approach (3D world-space offset): computed a side direction
            // via Cross(localDir, spineAxis) in 3D, offset each spine point in
            // world space, then projected. Because spineAxis is the aircraft
            // rotation axis, all offsets lay in the same 3D plane. Viewed from any
            // angle that wasn't perpendicular to that plane the funnel appeared as
            // a thin flat ribbon — a literal flat surface in 3D, not a HUD shape.
            //
            // Current approach: for each spine point, the wall half-width in pixels
            // is the angular size of the target wingspan at that range
            // (halfWingspan / range * focalLengthPx). The offset direction is the
            // global screen-space perpendicular of the spine (screenArc[0] →
            // screenArc[n-1]). Using the global tangent rather than local finite
            // differences avoids the noise/flip problem at low turn rate where
            // adjacent projected points are nearly coincident.

            int n = spine.Length;

            float focalLengthPx =
                Screen.height * 0.5f /
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            float halfWingspanMeters = wingspan * 0.5f;

            // Global spine tangent in screen space — stable even at near-zero
            // turn rate. When the spine genuinely has no screen-space extent
            // (zero turn rate, all points project to the same pixel) the walls
            // correctly collapse to zero width; fall back to rightward so the
            // arrays are still valid and no NaN propagates.
            Vector2 spineScreenVec = screenArc[n - 1] - screenArc[0];
            Vector2 perpDir = spineScreenVec.sqrMagnitude > 0.01f
                ? new Vector2(-spineScreenVec.y, spineScreenVec.x).normalized
                : new Vector2(1f, 0f);

            var leftWall  = new Vector2[n];
            var rightWall = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                float halfPx     = halfWingspanMeters / spine[i].range * focalLengthPx;
                leftWall[i]  = screenArc[i] - perpDir * halfPx;
                rightWall[i] = screenArc[i] + perpDir * halfPx;
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

            // ── Range dot ───────────────────────────────────────────────────────
            int      dotIdx    = -1;
            Vector2? dotPos    = null;
            float    dotRadius = 0f;

            if (rangeTarget != null && _config.ShowRangeDot.Value)
            {
                float targetRange = Vector3.Distance(
                    _aircraft.transform.position, rangeTarget.transform.position);

                if (targetRange >= spine[0].range && targetRange <= spine[n - 1].range)
                {
                    for (int i = 0; i < n - 1; i++)
                    {
                        if (spine[i].range <= targetRange &&
                            targetRange     <= spine[i + 1].range)
                        {
                            float t   = Mathf.InverseLerp(
                                spine[i].range, spine[i + 1].range, targetRange);
                            dotPos    = Vector2.Lerp(screenArc[i], screenArc[i + 1], t);
                            float halfPx = (wingspan * 0.5f / Mathf.Max(targetRange, 1f))
                                           * focalLengthPx;
                            dotRadius = halfPx * _config.RangeDotSize.Value;
                            dotIdx    = i;
                            break;
                        }
                    }
                }
                else if (targetRange > spine[n - 1].range)
                {
                    dotPos = screenArc[n - 1];
                    float halfPx = (wingspan * 0.5f / Mathf.Max(spine[n - 1].range, 1f))
                                   * focalLengthPx;
                    dotRadius = halfPx * _config.RangeDotSize.Value;
                    dotIdx    = n - 1;
                }
                // Inside MinRange: no dot — target too close to engage.
            }

            // ── Section 1 — In-solution SHOOT cue ───────────────────────────────
            // When a Tier 1/2 target is locked and the range dot has been placed,
            // check whether the target's current screen position falls within the
            // funnel walls at the dot's spine index. If so, signal inSolution so
            // FunnelRenderer can flash the funnel to ShootCueColor.
            bool inSolution = false;
            if (_config.EnableShootCue.Value && rangeTarget != null &&
                dotPos.HasValue && dotIdx >= 0)
            {
                Vector3 targetSP = camera.WorldToScreenPoint(rangeTarget.transform.position);
                if (targetSP.z > 0f)
                {
                    var   targetScreen  = new Vector2(targetSP.x, targetSP.y) + totalOffset;
                    float wallHalfWidth = Vector2.Distance(leftWall[dotIdx], rightWall[dotIdx]) * 0.5f;
                    float distToSpine   = Vector2.Distance(targetScreen, screenArc[dotIdx]);
                    inSolution = distToSpine < wallHalfWidth;
                }
            }

            _renderer?.SetDrawData(boresightScreen, leftWall, rightWall,
                isVisible: crossVisible, dotPos, dotRadius, inSolution);
        }
    }
}
