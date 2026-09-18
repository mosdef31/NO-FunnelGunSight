using System;
using HarmonyLib;
using UnityEngine;

namespace FunnelGunSight
{
    // Injects the funnel on CombatHUD.ShowWeaponStation and suppresses the native
    // pip via HUDBoresightState patches while HideNativeBoresight is on. The
    // native pip methods run every frame and reassign Image state, so returning
    // false from the prefix (skip original) is the only reliable way to hide it.
    [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.ShowWeaponStation))]
    public static class CombatHUDPatch
    {
        // ── Funnel state ───────────────────────────────────────────────────────
        private static WeaponStation? _currentStation;
        private static GameObject?    _currentFunnelGO;

        private static CombatHUD?     _lastHud;
        private static WeaponStation? _lastStation;

        // ── Watchdog state ─────────────────────────────────────────────────────
        // Seconds the overlay has been continuously dead. A born-dead overlay is
        // dead from frame one, so this is not a rare race that a single retry would
        // cover - it has to keep looking.
        private static float _deadFor;

        // THE BACKOFF, ADDED 2026-09-17 AFTER A FLIGHT LOG SHOWED WHY THE CENSUS
        // "DOES NOT APPEAR CONSISTENTLY".
        //
        // The 0.5s watchdog interval assumed a dead overlay is a rare, transient
        // race - "short enough that the owner sees a blink". On this flight the
        // HUD centre for one station (P_Trisurface1's 20mm cannon) stayed inactive
        // for minutes, and the watchdog dutifully destroyed and recreated a doomed
        // overlay every half second the whole time: hundreds of GameObjects, one
        // log line each, and the census only ever got a live read on the rare
        // frame the recreate happened to land while the HUD centre was briefly
        // active. That is not a census bug - the census was correct on the one
        // second it got a real overlay to examine - it is the retry storm eating
        // the flight.
        //
        // A few quick retries at the old cadence still cover the real blink case.
        // Past that, the interval grows (capped) instead of hammering the scene
        // every frame budget allows, and the warning is logged once per state
        // change rather than once per attempt - CLAUDE.md's "ration repeats".
        private const int   RapidReviveAttempts = 3;
        private const float ReviveBackoffMax    = 10f;
        private static float _reviveInterval = 0.5f;
        private static int   _reviveAttempts;
        private static bool  _saidBackingOff;

        // Seconds since the last injection, used to run the census ONE SECOND LATE
        // rather than in the same frame as AddComponent. See RunCensus.
        private static float _sinceInject = -1f;
        private static bool  _censusDone = true;

        // ── Public ─────────────────────────────────────────────────────────────

        public static bool FunnelActive => _currentFunnelGO != null;

        // ── Patch ──────────────────────────────────────────────────────────────

        static void Postfix(CombatHUD __instance, WeaponStation weaponStation)
        {
            try
            {
                // Always remember the latest HUD and station so RequestReinit can replay.
                if (__instance != null) _lastHud = __instance;
                if (weaponStation != null) _lastStation = weaponStation;

                // Same station + funnel still alive → nothing to do.
                // ShowWeaponStation fires on every target add/remove, not only on
                // weapon switches.  Skipping here prevents the flickering
                // destroy+recreate cycle.
                if (weaponStation != null
                    && weaponStation == _currentStation
                    && _currentFunnelGO != null
                    && _currentFunnelGO.activeInHierarchy)
                    return;

                DestroyExistingFunnel();
                _currentStation  = null;
                _currentFunnelGO = null;

                if (weaponStation == null)               return;
                if (!weaponStation.WeaponInfo.gun)       return;
                if (!weaponStation.WeaponInfo.boresight)  return;

                FunnelGunSightPlugin? plugin = FunnelGunSightPlugin.Instance;
                if (plugin == null || plugin.FunnelConfig == null ||
                    !plugin.FunnelConfig.Enabled.Value)
                    return;

                FlightHud? flightHud = SceneSingleton<FlightHud>.i;
                if (flightHud == null)
                {
                    plugin.Logger.LogWarning(
                        "[FunnelGunSight] FlightHud singleton not available, skipping injection.");
                    return;
                }

                Transform? hudCenter = flightHud.GetHUDCenter();
                if (hudCenter == null)
                {
                    plugin.Logger.LogWarning(
                        "[FunnelGunSight] FlightHud HUDCenter not found, skipping injection.");
                    return;
                }

                if (__instance == null)
                {
                    plugin.Logger.LogWarning(
                        "[FunnelGunSight] CombatHUD instance is null, skipping injection.");
                    return;
                }

                Aircraft? aircraft = __instance.aircraft;
                if (aircraft == null)
                {
                    plugin.Logger.LogWarning(
                        "[FunnelGunSight] CombatHUD.aircraft is null, skipping injection.");
                    return;
                }

                var go = new GameObject("FunnelGunSightOverlay");
                go.transform.SetParent(hudCenter, worldPositionStays: false);

                var funnel = go.AddComponent<FunnelGunSight>();
                funnel.Initialize(aircraft, weaponStation, plugin.FunnelConfig, plugin.WingspanDb!);

                _currentStation  = weaponStation;
                _currentFunnelGO = go;

                // LIVE-INSTANCE CENSUS, AND IT IS NOT BEHIND DebugLogging.
                //
                // The stuck tracer line survived the renderer's stale-frame guard, so
                // the remaining hypothesis is that an OLD FunnelGunSight outlives its
                // aircraft: it keeps running its own LateUpdate against a stale
                // `_aircraft`, keeps calling SetDrawData - so the guard never trips -
                // and draws a line frozen in world space. That reads as "stuck", and
                // as "only on Aryx aircraft" if it means "only after switching to one".
                //
                // ResetOverlayKey's config description has said "press this if the
                // funnel gets stuck or disappears after switching aircraft" for long
                // enough that the leak is already suspected in writing.
                //
                // ONE LINE PER INJECTION SETTLES IT. Greater than one and the fix is
                // lifecycle here, not anything in the renderer.
                int liveSights = UnityEngine.Object.FindObjectsOfType<FunnelGunSight>().Length;
                plugin.Logger.LogInfo(
                    $"[FunnelGunSight] Funnel injected for station: {weaponStation.WeaponInfo.weaponName} " +
                    $"liveSights={liveSights} aircraft={aircraft.name}");

                // THE OVERLAY CAN BE BORN DEAD, AND THAT IS ITEM 14.
                //
                // Owner, 2026-09-17: "tracer line does not appear at all on some of
                // stock aircraft." It does not, and the log says why: two injections
                // this sortie reported liveSights=0 IMMEDIATELY AFTER AddComponent,
                // which is impossible for a live component, because
                // FindObjectsOfType skips components on INACTIVE GameObjects. The
                // overlay was parented to a HUD centre that was not active, so the
                // funnel never ran a frame. No tracer was logged between that
                // injection and the next one, which is exactly the reported symptom.
                //
                // Logged here as a fact rather than inferred from a zero count, and
                // the watchdog in Tick is what actually recovers it.
                bool born = go.activeInHierarchy;
                plugin.Logger.LogInfo(
                    $"[FunnelGunSight] Overlay born {(born ? "ALIVE" : "DEAD - the HUD centre is inactive, so nothing on it will run")}"
                    + $" hudCenterActive={hudCenter.gameObject.activeInHierarchy}");

                // THE CENSUS RAN A FRAME TOO EARLY AND THEREFORE MEASURED NOTHING.
                // It printed "0 LineRenderer(s) under the HUD" on every aircraft in
                // the last sortie, including ones where the funnel and the tracer
                // were visibly working, because FunnelRenderer builds its
                // LineRenderers after Initialize - not in the same frame as
                // AddComponent. Armed here and run a second later, in Tick.
                _sinceInject = 0f;
                _censusDone  = false;

                if (liveSights > 1)
                    plugin.Logger.LogWarning(
                        $"[FunnelGunSight] {liveSights} FunnelGunSight components are alive at once. " +
                        "Only the newest has a current aircraft; the others are leaked and are still " +
                        "drawing. This is the stuck-tracer cause.");
            }
            catch (Exception ex)
            {
                FunnelGunSightPlugin.Instance?.Logger.LogError(
                    $"[FunnelGunSight] Failed to inject funnel sight: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ── Public API ─────────────────────────────────────────────────────────

        // ═══════════════════════════════════════════════════════════════════════
        //  THE WATCHDOG. Called every frame from the plugin, which is always active.
        //
        //  Two jobs, both of which need a driver that is NOT parented to the HUD:
        //
        //  1. REVIVE A DEAD OVERLAY. If the HUD centre was inactive when we injected,
        //     the overlay is inactive too and nothing on it will ever run. Nothing in
        //     the game re-fires ShowWeaponStation on its own, so without this the
        //     sight is gone for the sortie. A short streak of quick retries covers
        //     an ordinary HUD-mode flicker; past that the interval backs off - see
        //     the field note on _reviveInterval for why.
        //
        //  2. RUN THE CENSUS LATE. See the note at the injection site.
        // ═══════════════════════════════════════════════════════════════════════
        public static void Tick(float dt)
        {
            if (_sinceInject >= 0f)
            {
                _sinceInject += dt;
                if (!_censusDone && _sinceInject >= 1f)
                {
                    _censusDone = true;
                    RunCensus();
                }
            }

            // Nothing to revive until a gun station has been seen at least once.
            if (_lastHud == null || _lastStation == null) return;
            if (_currentFunnelGO != null && _currentFunnelGO.activeInHierarchy)
            {
                if (_reviveAttempts > 0)
                {
                    FunnelGunSightPlugin.Instance?.Logger.LogInfo(
                        $"[FunnelGunSight] Overlay recovered after {_reviveAttempts} revive attempt(s).");
                }
                _deadFor         = 0f;
                _reviveInterval  = 0.5f;
                _reviveAttempts  = 0;
                _saidBackingOff  = false;
                return;
            }

            _deadFor += dt;
            if (_deadFor < _reviveInterval) return;
            _deadFor = 0f;
            _reviveAttempts++;

            if (_reviveAttempts <= RapidReviveAttempts)
            {
                FunnelGunSightPlugin.Instance?.Logger.LogWarning(
                    "[FunnelGunSight] The overlay has been inactive, so it is being rebuilt. This "
                    + "is the born-dead case: it was parented to a HUD centre that was not active, "
                    + "so nothing was drawn. Recovered without the reset key.");
            }
            else
            {
                // BACKING OFF RATHER THAN KILLING THE WATCHDOG. A HUD centre that is
                // still inactive after several quick retries is not blinking, it is
                // chronically inactive for this station - a rebuild every half
                // second for the rest of the sortie is pure log and GameObject churn
                // with nothing to show for it. The interval grows instead, so a HUD
                // centre that DOES come back later (a view or aircraft switch) is
                // still found within ReviveBackoffMax seconds, just without the storm.
                _reviveInterval = Mathf.Min(_reviveInterval * 1.5f, ReviveBackoffMax);

                if (!_saidBackingOff)
                {
                    _saidBackingOff = true;
                    FunnelGunSightPlugin.Instance?.Logger.LogWarning(
                        $"[FunnelGunSight] The HUD centre has stayed inactive through "
                        + $"{_reviveAttempts} revive attempts, so retries are backing off to as "
                        + $"slow as {ReviveBackoffMax:F0}s instead of rebuilding every half second.");
                }
            }

            RequestReinit();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  THE CENSUS WAS LOOKING IN THE WRONG PLACE FOR THE WRONG THING, TWICE.
        //
        //  It reported `0 LineRenderer(s) under the HUD centre` on every injection
        //  across three sorties, including ones where the sight was demonstrably
        //  alive and drawing, so nothing was ever tagged OURS or FOREIGN and the
        //  Aryx stuck-tracer report stayed unexplained. Both halves were wrong:
        //
        //  1. WE DRAW IN GL IMMEDIATE MODE. FunnelRenderer issues GL.Begin /
        //     AddQuad inside OnRenderObject; there is no LineRenderer anywhere in
        //     this mod and there never has been. A census of LineRenderers could
        //     not have found our trail on its best day, so OURS was unreachable by
        //     construction. It now reports the renderer we ACTUALLY have.
        //
        //  2. A FOREIGN TRACER IS NOT UNDER THE HUD CENTRE. The fault is reported
        //     only on Aryx aircraft, which ship their own cockpit and HUD prefabs,
        //     and a gunsight line belonging to one of those hangs off the COCKPIT,
        //     not off the little transform we parent ourselves to. Searching one
        //     node's children was searching the one subtree it could not be in.
        //
        //  Owner, 2026-09-17, choosing this over rewriting the trail: fix the
        //  census, keep the GL drawing. Nothing about what is drawn changes here.
        // ═══════════════════════════════════════════════════════════════════════
        private static void RunCensus()
        {
            FunnelGunSightPlugin? plugin = FunnelGunSightPlugin.Instance;
            if (plugin == null || _currentFunnelGO == null) return;

            Transform? hudCenter = _currentFunnelGO.transform.parent;
            if (hudCenter == null) return;

            var sb = new System.Text.StringBuilder();

            // ── Ours, reported as what it is ──────────────────────────────────
            var renderers = _currentFunnelGO.GetComponentsInChildren<FunnelRenderer>(true);
            sb.Append($"\n  OURS: {renderers.Length} FunnelRenderer(s) - GL immediate mode, "
                      + "no LineRenderer by design.");
            foreach (FunnelRenderer r in renderers)
                sb.Append($"\n    {r.name} enabled={r.enabled} "
                          + $"active={r.gameObject.activeInHierarchy}");

            var sights = _currentFunnelGO.GetComponentsInChildren<FunnelGunSight>(true);
            sb.Append($"\n  OURS: {sights.Length} FunnelGunSight(s)");
            if (sights.Length > 1)
                sb.Append("  <-- MORE THAN ONE IS THE LEAK, not a healthy state");

            // ── Foreign, searched WIDE ────────────────────────────────────────
            //
            // The search root climbs to the top of whatever hierarchy the HUD
            // centre sits in, so an Aryx cockpit's own gunsight line is inside it.
            // Everything found is FOREIGN by definition now, since ours are not
            // LineRenderers at all - but each one is still named and measured,
            // because "which object is drawing the stuck tracer" is the question.
            Transform root = hudCenter.root;
            var lines = root.GetComponentsInChildren<LineRenderer>(true);

            int drawing = 0;
            foreach (LineRenderer lr in lines)
            {
                bool live = lr.enabled && lr.gameObject.activeInHierarchy && lr.positionCount > 1;
                if (live) drawing++;

                sb.Append("\n    FOREIGN ");
                sb.Append(Path(lr.transform, root));
                sb.Append($" positions={lr.positionCount} enabled={lr.enabled}");
                sb.Append($" active={lr.gameObject.activeInHierarchy}");
                sb.Append($" mat={(lr.sharedMaterial == null ? "NONE" : lr.sharedMaterial.name)}");
                if (live) sb.Append("  <-- LIVE");
            }

            plugin.Logger.LogInfo(
                $"[FunnelGunSight] CENSUS one second after injection, under root '{root.name}' "
                + $"(HUD centre '{hudCenter.name}'): {lines.Length} foreign LineRenderer(s), "
                + $"{drawing} of them actually drawing.{sb}");
        }

        /// <summary>
        /// A transform's path relative to a root. A bare `lr.name` is almost always
        /// something like "Line", which names nothing - the path is what identifies
        /// which prefab the object came out of.
        /// </summary>
        private static string Path(Transform t, Transform root)
        {
            var parts = new System.Collections.Generic.List<string>();
            for (Transform? c = t; c != null && c != root; c = c.parent)
                parts.Add(c.name);
            parts.Reverse();
            return parts.Count == 0 ? t.name : string.Join("/", parts);
        }

        // Destroys and recreates the funnel using the last known HUD and station.
        public static void RequestReinit()
        {
            if (_lastHud == null || _lastStation == null)
            {
                FunnelGunSightPlugin.Instance?.Logger.LogWarning(
                    "[FunnelGunSight] RequestReinit: no previous HUD/station recorded, nothing to reinit.");
                return;
            }

            FunnelGunSightPlugin.Instance?.Logger.LogInfo("[FunnelGunSight] Force-reinitializing funnel.");

            // Destroy current overlay and clear tracking so Postfix doesn't skip.
            DestroyExistingFunnel();
            _currentStation  = null;
            _currentFunnelGO = null;

            // Replay the postfix with the stored references.
            Postfix(_lastHud, _lastStation);
        }

        // ── Private ────────────────────────────────────────────────────────────

        private static void DestroyExistingFunnel()
        {
            if (_currentFunnelGO != null)
            {
                UnityEngine.Object.Destroy(_currentFunnelGO);
                _currentFunnelGO = null;
            }
        }

        private static bool ShouldSuppressNativePip()
        {
            FunnelConfig? cfg = FunnelGunSightPlugin.Instance?.FunnelConfig;
            return FunnelActive && (cfg?.HideNativeBoresight.Value ?? true);
        }

        // ── Native pip suppression patches ────────────────────────────────────

        [HarmonyPatch(typeof(HUDBoresightState), nameof(HUDBoresightState.UpdateWeaponDisplay))]
        [HarmonyPrefix]
        private static bool SuppressUpdateWeaponDisplay() => !ShouldSuppressNativePip();

        [HarmonyPatch(typeof(HUDBoresightState), nameof(HUDBoresightState.HUDFixedUpdate))]
        [HarmonyPrefix]
        private static bool SuppressHUDFixedUpdate() => !ShouldSuppressNativePip();
    }
}
