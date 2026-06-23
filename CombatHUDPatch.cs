using System;
using HarmonyLib;
using UnityEngine;

namespace FunnelGunSight
{
    /// <summary>
    /// Harmony postfix patch on <see cref="CombatHUD.ShowWeaponStation"/>.
    /// Injects a <see cref="FunnelGunSight"/> component when a gun station is selected,
    /// and destroys any previous instance when the player switches stations.
    /// </summary>
    [HarmonyPatch(typeof(CombatHUD), nameof(CombatHUD.ShowWeaponStation))]
    public static class CombatHUDPatch
    {
        // ── State tracking ─────────────────────────────────────────────────────
        private static WeaponStation? _currentStation;
        private static GameObject?    _currentFunnelGO;

        // Stored so RequestReinit() can re-run the injection without waiting for
        // the next ShowWeaponStation call from the game.
        private static CombatHUD?     _lastHud;
        private static WeaponStation? _lastStation;

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
                        "[FunnelGunSight] FlightHud singleton not available — skipping injection.");
                    return;
                }

                Transform? hudCenter = flightHud.GetHUDCenter();
                if (hudCenter == null)
                {
                    plugin.Logger.LogWarning(
                        "[FunnelGunSight] FlightHud HUDCenter not found — skipping injection.");
                    return;
                }

                if (__instance == null)
                {
                    plugin.Logger.LogWarning(
                        "[FunnelGunSight] CombatHUD instance is null — skipping injection.");
                    return;
                }

                Aircraft? aircraft = __instance.aircraft;
                if (aircraft == null)
                {
                    plugin.Logger.LogWarning(
                        "[FunnelGunSight] CombatHUD.aircraft is null — skipping injection.");
                    return;
                }

                var go = new GameObject("FunnelGunSightOverlay");
                go.transform.SetParent(hudCenter, worldPositionStays: false);

                var funnel = go.AddComponent<FunnelGunSight>();
                funnel.Initialize(aircraft, weaponStation, plugin.FunnelConfig, plugin.WingspanDb!);

                _currentStation  = weaponStation;
                _currentFunnelGO = go;

                plugin.Logger.LogInfo(
                    $"[FunnelGunSight] Funnel injected for station: {weaponStation.WeaponInfo.weaponName}");
            }
            catch (Exception ex)
            {
                FunnelGunSightPlugin.Instance?.Logger.LogError(
                    $"[FunnelGunSight] Failed to inject funnel sight: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ── Public API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Destroys the current funnel and immediately recreates it using the last
        /// known HUD and weapon station.  Call this to recover from a stuck overlay
        /// or after switching aircraft in a scenario.
        /// </summary>
        public static void RequestReinit()
        {
            if (_lastHud == null || _lastStation == null)
            {
                FunnelGunSightPlugin.Instance?.Logger.LogWarning(
                    "[FunnelGunSight] RequestReinit: no previous HUD/station recorded — nothing to reinit.");
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
    }
}
