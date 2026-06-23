using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace FunnelGunSight
{
    [BepInPlugin("com.funnelgunsight.mod", "FunnelGunSight", "1.0.0")]
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
                if (FunnelConfig.ForceReinitKey.Value.IsDown())
                    CombatHUDPatch.RequestReinit();
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    $"[FunnelGunSight] Update error: {ex.Message}");
            }
        }
    }
}
