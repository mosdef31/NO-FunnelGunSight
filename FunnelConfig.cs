using BepInEx.Configuration;
using UnityEngine;

namespace FunnelGunSight
{
    public enum WingspanMode { Fixed, Adaptive }

    /// <summary>
    /// All BepInEx config entries for FunnelGunSight.
    /// Sections: General | Display | Tracking
    /// </summary>
    public sealed class FunnelConfig
    {
        // ── General ────────────────────────────────────────────────────────────

        public ConfigEntry<bool>             Enabled        { get; }
        public ConfigEntry<bool>             DiagLogging    { get; }
        public ConfigEntry<KeyboardShortcut> ForceReinitKey { get; }

        // ── Display ────────────────────────────────────────────────────────────

        public ConfigEntry<float> FunnelOpacity  { get; }
        public ConfigEntry<bool>  ShowGunCross   { get; }
        public ConfigEntry<float> GunCrossSize   { get; }
        public ConfigEntry<bool>  ShowRangeDot   { get; }
        public ConfigEntry<float> RangeDotSize   { get; }

        // ── Tracking ───────────────────────────────────────────────────────────

        public ConfigEntry<WingspanMode> WingspanModeSetting { get; }
        public ConfigEntry<float>        DefaultWingspan { get; }
        public ConfigEntry<int>          TrajectoryPoints { get; }
        public ConfigEntry<float>        MinRangeMeters  { get; }
        public ConfigEntry<float>        MaxRangeMeters  { get; }
        public ConfigEntry<float>        MinAngularRate  { get; }
        public ConfigEntry<bool>         EnableLevelV    { get; }
        public ConfigEntry<float>        LevelVBlendWeight { get; }

        // ── Smoothing ──────────────────────────────────────────────────────────

        public ConfigEntry<float> TurnRateDamping        { get; }

        // ── Constructor ────────────────────────────────────────────────────────

        public FunnelConfig(ConfigFile config)
        {
            // General
            Enabled = config.Bind(
                "General", "Enabled", true,
                "Master toggle. Disable to hide the overlay without uninstalling the mod.");

            DiagLogging = config.Bind(
                "General", "DiagLogging", false,
                "Write angular velocity, target tier, and screen position to the BepInEx log " +
                "every ~2 s. Leave off in normal use.");

            ForceReinitKey = config.Bind(
                "General", "ForceReinitKey", new KeyboardShortcut(KeyCode.F9),
                "Press to destroy and recreate the funnel overlay. " +
                "Use if the overlay gets stuck or disappears after switching aircraft.");

            // Display
            FunnelOpacity = config.Bind(
                "Display", "FunnelOpacity", 1.0f,
                new ConfigDescription(
                    "Opacity of all funnel elements (walls, cross, dot). " +
                    "1.0 = fully opaque, 0.1 = nearly invisible.",
                    new AcceptableValueRange<float>(0.1f, 1.0f)));

            ShowGunCross = config.Bind(
                "Display", "ShowGunCross", true,
                "Show a small cross at the boresight / HUD centre. " +
                "Disable if it overlaps the game's own boresight indicator.");

            GunCrossSize = config.Bind(
                "Display", "GunCrossSize", 8.0f,
                new ConfigDescription(
                    "Half-length of each cross arm in pixels.",
                    new AcceptableValueRange<float>(2f, 24f)));

            ShowRangeDot = config.Bind(
                "Display", "ShowRangeDot", true,
                "When a target is designated or HUD-selected, draw a circle on the funnel " +
                "spine at the target's current slant range. " +
                "When the target visually fills the circle, you have a firing solution.");

            RangeDotSize = config.Bind(
                "Display", "RangeDotSize", 0.4f,
                new ConfigDescription(
                    "Diameter of the range dot as a fraction of the funnel wall separation " +
                    "at that range. 1.0 = full wall width, 0.4 = 40 % (default).",
                    new AcceptableValueRange<float>(0.1f, 1.0f)));

            // Tracking
            WingspanModeSetting = config.Bind(
                "Tracking", "WingspanMode", WingspanMode.Fixed,
                "Fixed: use DefaultWingspan for all targets. " +
                "Adaptive: look up the locked target's wingspan from wingspans.json.");

            DefaultWingspan = config.Bind(
                "Tracking", "DefaultWingspan", 11.0f,
                new ConfigDescription(
                    "Wingspan in metres used in Fixed mode, or as a fallback when the target " +
                    "is not in wingspans.json.",
                    new AcceptableValueRange<float>(1f, 100f)));

            TrajectoryPoints = config.Bind(
                "Tracking", "TrajectoryPoints", 50,
                new ConfigDescription(
                    "Number of sample points along the funnel spine. " +
                    "Higher values produce a smoother curve at a small CPU cost.",
                    new AcceptableValueRange<int>(10, 100)));

            MinRangeMeters = config.Bind(
                "Tracking", "MinRangeMeters", 100.0f,
                new ConfigDescription(
                    "Range of the near (wide) end of the funnel in metres.",
                    new AcceptableValueRange<float>(10f, 500f)));

            MaxRangeMeters = config.Bind(
                "Tracking", "MaxRangeMeters", 1200.0f,
                new ConfigDescription(
                    "Range of the far (narrow) end of the funnel in metres.",
                    new AcceptableValueRange<float>(200f, 3000f)));

            MinAngularRate = config.Bind(
                "Tracking", "MinAngularRate", 0.01f,
                new ConfigDescription(
                    "Angular rate floor in rad/s. Below this, the funnel switches to a " +
                    "gravity-correction fallback axis instead of the measured rate.",
                    new AcceptableValueRange<float>(0.001f, 0.5f)));

            EnableLevelV = config.Bind(
                "Tracking", "EnableLevelV", false,
                "Level V: blend in the line-of-sight rotation rate to the current Tier 1/2 " +
                "target (the rate the target is actually crossing your sight, derived from " +
                "relative position/velocity — same quantity proportional-navigation guidance " +
                "tracks). This captures the target's own maneuvering, which the aircraft's " +
                "own turn rate alone cannot. No effect with no Tier 1/2 target locked, or " +
                "within MinRangeMeters of it (avoids divide-by-near-zero blowup at point-blank " +
                "range) — falls back to pure own-rate in both cases.");

            LevelVBlendWeight = config.Bind(
                "Tracking", "LevelVBlendWeight", 1.0f,
                new ConfigDescription(
                    "Only used when EnableLevelV is on. 0 = pure own-aircraft rate (Level V has " +
                    "no effect). 1 = pure line-of-sight rate (own rate ignored whenever a " +
                    "Tier 1/2 target is present). Values in between blend the two continuously.",
                    new AcceptableValueRange<float>(0f, 1f)));

            TurnRateDamping = config.Bind(
                "Smoothing", "TurnRateDamping", 0.35f,
                new ConfigDescription(
                    "Smoothing time constant (seconds) applied to the measured turn rate before " +
                    "it drives the funnel's curvature. Raises this to fix twitching/twisting near " +
                    "zero turn rate and sudden violent snaps during brief low-speed maneuvers. " +
                    "0 = no damping (raw, instant response).",
                    new AcceptableValueRange<float>(0f, 0.5f)));

        }
    }
}
