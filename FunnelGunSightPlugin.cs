using System;
using UnityEngine;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace FunnelGunSight
{
    // 1.2.0, the public build, cut 2026-09-18 after the testers flew 1.2.0b and
    // reported nothing. That test build was 1.2.0.1 internally, because the
    // BepInPlugin version string is parsed as a System.Version and throws on a
    // non-numeric segment like a trailing "b"; the archived copy is at
    // releases/funnel-gunsight/1.2.0b. As that note said it would, the public
    // release is plain "1.2.0" and does not reuse the .1.
    [BepInPlugin("com.funnelgunsight.mod", "FunnelGunSight", "1.2.0")]
    [BepInProcess("NuclearOption.exe")]
    public class FunnelGunSightPlugin : BaseUnityPlugin
    {
        // ── Singleton ─────────────────────────────────────────────────────────

        public static FunnelGunSightPlugin? Instance { get; private set; }

        // ── Public accessors ──────────────────────────────────────────────────

        public FunnelConfig?     FunnelConfig { get; private set; }
        public WingspanDatabase? WingspanDb   { get; private set; }

        // BepInEx exposes Logger on BaseUnityPlugin; re-expose for callers.
        public new ManualLogSource Logger => base.Logger;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            try
            {
                Instance = this;

                FunnelConfig = new FunnelConfig(Config);

                string pluginDir = Path.GetDirectoryName(Info.Location)
                                   ?? AppDomain.CurrentDomain.BaseDirectory;
                string jsonPath  = Path.Combine(pluginDir, "wingspans.json");
                WingspanDb = new WingspanDatabase(jsonPath, Logger);

                var harmony = new Harmony("com.funnelgunsight.mod");
                harmony.PatchAll(Assembly.GetExecutingAssembly());

                Logger.LogInfo("[FunnelGunSight] Plugin loaded successfully.");


            }
            catch (Exception ex)
            {
                Logger.LogError(
                    $"[FunnelGunSight] Plugin failed to load: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void Update()
        {
            try
            {
                if (FunnelConfig == null) return;
                if (FunnelConfig.ResetOverlayKey.Value.IsDown())
                    CombatHUDPatch.RequestReinit();

                if (FunnelConfig.RunProofsKey.Value.IsDown())
                {
                    string? report = FunnelProbes.RunAll(null, FunnelConfig);
                    Logger.LogInfo(report != null
                        ? "[FunnelGunSight] Measurement proofs written to " + report
                        : "[FunnelGunSight] Measurement proofs could not be written.");
                }

                // THE WATCHDOG IS WHY THE RESET KEY EXISTS, AND IT SHOULD NOT HAVE
                // TO. The overlay is parented to the HUD centre, and the funnel only
                // ever gets rebuilt when CombatHUD.ShowWeaponStation fires. If the
                // HUD centre is inactive at the moment we inject, the overlay is born
                // dead, nothing on it runs, and nothing fires ShowWeaponStation again
                // - so the sight and the tracer are simply absent for the rest of the
                // sortie unless the owner presses the reset key. Driven from here
                // because the plugin object is always active and the dead overlay,
                // by definition, is not.
                CombatHUDPatch.Tick(Time.unscaledDeltaTime);
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    $"[FunnelGunSight] Update error: {ex.Message}");
            }
        }
    }
}
