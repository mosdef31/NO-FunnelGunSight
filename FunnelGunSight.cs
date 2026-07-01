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
        private FunnelRenderer?   _renderer;
        private FunnelConfig?     _config;
        private WingspanDatabase? _wingspanDb;

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

            // weapon.transform.forward matches HUDBoresightState's own gun direction,
            // including any depression angle baked into the gun's transform.
            var dirSum = Vector3.zero;
            foreach (Weapon w in weaponStation.Weapons)
                dirSum += w.transform.forward;

            _gunDirectionLocal = dirSum.sqrMagnitude > 0.001f
                ? aircraft.transform.InverseTransformDirection(dirSum)
                : Vector3.forward;

            _renderer = gameObject.AddComponent<FunnelRenderer>();
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

            Vector3 gunWorldDir = _aircraft.transform.TransformDirection(_gunDirectionLocal);

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
                    if (primaryTarget == null && (_config?.AutoTargetNearestEnemy.Value ?? true))
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
            (Vector3 point, float range)[] spine = PlaneOfMotionSampler.Sample(
                _aircraft.transform.position,
                gunWorldDir,
                _weaponStation.WeaponInfo.muzzleVelocity,
                angularVel,
                _aircraft.rb?.velocity ?? Vector3.zero,
                _weaponStation.WeaponInfo,
                _config.BallisticSimulationSteps.Value,
                _config.FunnelResolution.Value,
                _config.MinRangeMeters.Value,
                _config.MaxRangeMeters.Value,
                _config.MinTurnRate.Value,
                out Vector3 spineAxis);

            // ── Screen projection ─────────────────────────────────────────────
            // WorldToScreenPoint has (0,0) at bottom-left; FunnelRenderer.Fy()
            // flips Y for GL.LoadPixelMatrix's top-left origin.
            Vector3 boresightWorld = _aircraft.transform.position
                                     + gunWorldDir * BoresightProjectionDistance;
            Vector3 bsp            = camera.WorldToScreenPoint(boresightWorld);

            bool gearDown     = _config.HideWithGearDown.Value && _aircraft.gearDeployed;
            bool crossVisible = bsp.z > 0f && !gearDown;

            Vector2 boresightScreen = new Vector2(bsp.x, bsp.y);
            Vector2 totalOffset     = Vector2.zero;

            var  screenArc = new Vector2[spine.Length];
            bool spineOk   = spine.Length >= 2;

            if (spineOk)
            {
                for (int i = 0; i < spine.Length; i++)
                {
                    Vector3 sp = camera.WorldToScreenPoint(spine[i].point);
                    if (sp.z < 0f)
                    {
                        spineOk = false;
                        break;
                    }
                    screenArc[i] = new Vector2(sp.x, sp.y) + totalOffset;
                }
            }

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

            if (!spineOk)
            {
                _renderer?.SetDrawData(boresightScreen, null, null,
                    isVisible: false, dotPos: null, dotRadius: 0f, inSolution: false);
                return;
            }

            // ── Wall geometry ─────────────────────────────────────────────────
            // Wall half-width at each point is the angular size of the target
            // wingspan at that range. Offset direction is the global screen-space
            // perpendicular of the spine, which stays stable even at low turn rate.
            int n = spine.Length;

            float focalLengthPx =
                Screen.height * 0.5f /
                Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            float halfWingspanMeters = wingspan * 0.5f;

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

            // ── Range dot ─────────────────────────────────────────────────────
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
