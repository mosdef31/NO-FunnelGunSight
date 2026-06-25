using BepInEx.Configuration;
using UnityEngine;

namespace FunnelGunSight
{
    public enum WingspanMode { Fixed, Adaptive }

    /// <summary>
    /// All BepInEx config entries for FunnelGunSight.
    /// Sections: General | Display | Tracking | Smoothing
    /// </summary>
    public sealed class FunnelConfig
    {
        // ── General ────────────────────────────────────────────────────────────

        public ConfigEntry<bool>             Enabled               { get; }
        public ConfigEntry<bool>             DiagLogging           { get; }
        public ConfigEntry<KeyboardShortcut> ForceReinitKey        { get; }
        public ConfigEntry<bool>             InvertAngularVelocity { get; }

        // ── Display ────────────────────────────────────────────────────────────

        public ConfigEntry<float> FunnelOpacity       { get; }
        public ConfigEntry<Color> FunnelColor         { get; }
        public ConfigEntry<bool>  HideNativeBoresight { get; }
        public ConfigEntry<bool>  ShowGunCross        { get; }
        public ConfigEntry<float> GunCrossSize        { get; }
        public ConfigEntry<bool>  ShowRangeDot        { get; }
        public ConfigEntry<float> RangeDotSize        { get; }
        public ConfigEntry<bool>  EnableShootCue      { get; }
        public ConfigEntry<Color> ShootCueColor       { get; }
        public ConfigEntry<bool>  HideWithGearDown    { get; }

        // ── Tracking ───────────────────────────────────────────────────────────

        public ConfigEntry<WingspanMode> WingspanModeSetting      { get; }
        public ConfigEntry<float>        DefaultWingspan           { get; }
        public ConfigEntry<int>          TrajectoryPoints          { get; }
        public ConfigEntry<float>        MinRangeMeters            { get; }
        public ConfigEntry<float>        MaxRangeMeters            { get; }
        public ConfigEntry<float>        MinAngularRate            { get; }
        public ConfigEntry<bool>         EnableLevelV              { get; }
        public ConfigEntry<float>        LevelVBlendWeight         { get; }
        public ConfigEntry<float>        LevelVMinRange            { get; }
        public ConfigEntry<float>        LevelVMaxRange            { get; }
        public ConfigEntry<bool>         EnableBoresightAutoTarget { get; }
        public ConfigEntry<int>          BallisticSteps            { get; }

        // ── Smoothing ──────────────────────────────────────────────────────────

        public ConfigEntry<float> TurnRateDamping { get; }

        // ── Constructor ────────────────────────────────────────────────────────

        public FunnelConfig(ConfigFile config)
        {
            // Instructions sentinel — appears at the top of the [General] section
            // in ConfigurationManager as a non-editable info row.
            config.Bind("General", "_Instructions", "",
                new ConfigDescription(
                    "Hover over any setting name to see what it does. " +
                    "All changes take effect immediately — no restart needed. " +
                    "Default values work well for most players."));

            // General
            Enabled = config.Bind(
                "General", "Enabled", true,
                "Turn the gun funnel overlay ON or OFF without uninstalling the mod.");

            DiagLogging = config.Bind(
                "General", "DiagLogging", false,
                "ON: writes angular velocity, target tier, and screen position to the " +
                "BepInEx log roughly every 2 seconds. Leave OFF during normal play — " +
                "it produces a lot of log output.");

            ForceReinitKey = config.Bind(
                "General", "ForceReinitKey", new KeyboardShortcut(KeyCode.F9),
                "Keyboard shortcut that destroys and recreates the funnel overlay. " +
                "Press this if the overlay gets stuck or disappears after switching aircraft.");

            InvertAngularVelocity = config.Bind(
                "General", "InvertAngularVelocity", true,
                "ON (default): correct for the Firefly Companion FBW mod, which inverts the " +
                "sign of rb.angularVelocity. " +
                "OFF: use for stock aircraft or other FBW mods if the funnel curves the wrong way " +
                "during turns — flip this first before changing anything else.");

            // Display
            FunnelOpacity = config.Bind(
                "Display", "FunnelOpacity", 1.0f,
                new ConfigDescription(
                    "How transparent the funnel appears. 1.0 = solid, 0.1 = nearly invisible. " +
                    "Reduce if it feels too distracting.",
                    new AcceptableValueRange<float>(0.1f, 1.0f)));

            FunnelColor = config.Bind(
                "Display", "FunnelColor", Color.green,
                "Color of all funnel elements. Alpha is ignored here — use FunnelOpacity to " +
                "control transparency. The default green matches the game's native HUD color.");

            HideNativeBoresight = config.Bind(
                "Display", "HideNativeBoresight", true,
                "ON: hides the game's own gray gun crosshair and lead circle while the funnel " +
                "is active, since the funnel replaces them. OFF: shows both at once " +
                "(not recommended — cluttered).");

            ShowGunCross = config.Bind(
                "Display", "ShowGunCross", true,
                "ON: draws a small cross at the gun boresight point (HUD centre). " +
                "OFF: hides the cross, leaving only the funnel walls.");

            GunCrossSize = config.Bind(
                "Display", "GunCrossSize", 8.0f,
                new ConfigDescription(
                    "Size of the boresight cross — specifically the half-length of each arm " +
                    "in pixels. 8 pixels (default) produces a small, unobtrusive cross.",
                    new AcceptableValueRange<float>(2f, 24f)));

            ShowRangeDot = config.Bind(
                "Display", "ShowRangeDot", true,
                "ON: when a target is designated or HUD-selected, draws a circle on the funnel " +
                "spine at the target's current distance. When the target visually fills the " +
                "circle you have the correct range — fire. OFF: hides the range circle.");

            RangeDotSize = config.Bind(
                "Display", "RangeDotSize", 0.4f,
                new ConfigDescription(
                    "How large the range circle appears, as a fraction of the funnel wall " +
                    "separation at that distance. 1.0 = fills the full gap between walls; " +
                    "0.4 (default) = 40% of the gap.",
                    new AcceptableValueRange<float>(0.1f, 1.0f)));

            EnableShootCue = config.Bind(
                "Display", "EnableShootCue", true,
                "Flash the funnel to ShootCueColor when the target is inside the funnel walls " +
                "at the correct range — you have a firing solution. Works only with a " +
                "Tier 1/2 (designated or HUD-cursor) target.");

            ShootCueColor = config.Bind(
                "Display", "ShootCueColor", Color.white,
                "Color the funnel flashes to when a firing solution is detected. " +
                "Default white is easy to distinguish from the normal green.");

            HideWithGearDown = config.Bind(
                "Display", "HideWithGearDown", true,
                "ON: automatically hides the funnel when the landing gear is deployed. " +
                "Matches the native boresight system behaviour — with gear down, the game " +
                "shows a waterline symbol instead of the gun sight, so the funnel is not useful. " +
                "OFF: keeps the funnel visible with gear down (useful for testing).");

            // Tracking
            WingspanModeSetting = config.Bind(
                "Tracking", "WingspanMode", WingspanMode.Fixed,
                "Fixed: funnel width is always based on DefaultWingspan — simple and consistent. " +
                "Adaptive: funnel width automatically adjusts to the locked target's real " +
                "wingspan as aircraft types are encountered in play (stored in wingspans.json).");

            DefaultWingspan = config.Bind(
                "Tracking", "DefaultWingspan", 11.0f,
                new ConfigDescription(
                    "Target wingspan in metres used to set the funnel wall separation. Used " +
                    "always in Fixed mode, or as a fallback in Adaptive mode when the target " +
                    "type is not yet in wingspans.json. 11 m covers most fighters.",
                    new AcceptableValueRange<float>(1f, 100f)));

            TrajectoryPoints = config.Bind(
                "Tracking", "TrajectoryPoints", 50,
                new ConfigDescription(
                    "How many points are computed along the funnel spine. More points produce " +
                    "a smoother curve at a small CPU cost. 50 is smooth for all in-game speeds.",
                    new AcceptableValueRange<int>(10, 100)));

            MinRangeMeters = config.Bind(
                "Tracking", "MinRangeMeters", 100.0f,
                new ConfigDescription(
                    "Distance in metres to the near (wide) end of the funnel. " +
                    "Targets closer than this will not show a range dot.",
                    new AcceptableValueRange<float>(10f, 500f)));

            MaxRangeMeters = config.Bind(
                "Tracking", "MaxRangeMeters", 1200.0f,
                new ConfigDescription(
                    "Distance in metres to the far (narrow) end of the funnel. " +
                    "Targets beyond this clamp the range dot to the tip of the funnel.",
                    new AcceptableValueRange<float>(200f, 3000f)));

            MinAngularRate = config.Bind(
                "Tracking", "MinAngularRate", 0.01f,
                new ConfigDescription(
                    "Minimum turn rate in radians/second. Below this the funnel switches to a " +
                    "gravity-correction fallback axis to avoid the spine collapsing to a straight " +
                    "line. 0.01 rad/s (default) is well below any intentional manoeuvre.",
                    new AcceptableValueRange<float>(0.001f, 0.5f)));

            EnableLevelV = config.Bind(
                "Tracking", "EnableLevelV", false,
                "Advanced: when a target is locked, use the target's actual movement across " +
                "your sight to compute the lead — not just your own turn rate. Makes the funnel " +
                "more accurate against maneuvering targets. Requires a designated (Tier 1) or " +
                "HUD-selected (Tier 2) target.");

            LevelVBlendWeight = config.Bind(
                "Tracking", "LevelVBlendWeight", 1.0f,
                new ConfigDescription(
                    "How strongly the target's line-of-sight rate influences the funnel when " +
                    "Level V is enabled. 0 = use only your own turn rate (Level V has no effect); " +
                    "1 = use the target's crossing rate fully. Values between blend the two.",
                    new AcceptableValueRange<float>(0f, 1f)));

            LevelVMinRange = config.Bind(
                "Tracking", "LevelVMinRange", 300f,
                new ConfigDescription(
                    "Level V only: minimum target distance in metres before the line-of-sight " +
                    "rate starts blending in. Below this distance Level V has no effect — only " +
                    "your own turn rate is used. Prevents noisy readings at close range from " +
                    "distorting the funnel.",
                    new AcceptableValueRange<float>(50f, 1000f)));

            LevelVMaxRange = config.Bind(
                "Tracking", "LevelVMaxRange", 800f,
                new ConfigDescription(
                    "Level V only: target distance in metres at which the line-of-sight rate " +
                    "reaches full LevelVBlendWeight. Between LevelVMinRange and this value the " +
                    "blend ramps up gradually.",
                    new AcceptableValueRange<float>(100f, 3000f)));

            EnableBoresightAutoTarget = config.Bind(
                "Tracking", "EnableBoresightAutoTarget", true,
                "ON: when no target is designated or HUD-selected, automatically uses the " +
                "closest enemy aircraft within 30° of your gun boresight to set funnel width. " +
                "This only affects the funnel width, never places a range dot. " +
                "OFF: always uses DefaultWingspan when no explicit target is locked.");

            BallisticSteps = config.Bind(
                "Tracking", "BallisticSteps", 20,
                new ConfigDescription(
                    "How many simulation steps are used to model bullet drag and gravity for " +
                    "each funnel point. Higher = more accurate at long range and for slow or " +
                    "high-drag weapons. 20 steps (default) is accurate to within 1% for all " +
                    "in-game guns with negligible CPU cost.",
                    new AcceptableValueRange<int>(5, 100)));

            // Smoothing
            TurnRateDamping = config.Bind(
                "Smoothing", "TurnRateDamping", 0.35f,
                new ConfigDescription(
                    "How quickly the funnel reacts to changes in your turn rate (in seconds). " +
                    "Higher = smoother but slower to respond. Lower = more responsive but can " +
                    "twitch. 0.35 s works well for most aircraft. 0 = instant/no smoothing.",
                    new AcceptableValueRange<float>(0f, 0.5f)));
        }
    }
}
