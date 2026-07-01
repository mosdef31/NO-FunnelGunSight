using BepInEx.Configuration;
using UnityEngine;

namespace FunnelGunSight
{
    public enum WingspanMode { Fixed, Adaptive }

    // All BepInEx config entries for FunnelGunSight.
    // Sections: General | Display | Tracking | Smoothing
    public sealed class FunnelConfig
    {
        // ── General ────────────────────────────────────────────────────────────

        public ConfigEntry<bool>             Enabled             { get; }
        public ConfigEntry<bool>             DebugLogging        { get; }
        public ConfigEntry<KeyboardShortcut> ResetOverlayKey     { get; }
        public ConfigEntry<bool>             InvertTurnDirection { get; }

        // ── Display ────────────────────────────────────────────────────────────

        public ConfigEntry<float> FunnelOpacity         { get; }
        public ConfigEntry<Color> FunnelColor           { get; }
        public ConfigEntry<bool>  HideNativeBoresight   { get; }
        public ConfigEntry<bool>  ShowPipper            { get; }
        public ConfigEntry<float> PipperSize            { get; }
        public ConfigEntry<float> FunnelLineThickness   { get; }
        public ConfigEntry<bool>  ShowRangeDot          { get; }
        public ConfigEntry<float> RangeDotSize          { get; }
        public ConfigEntry<bool>  RangeDotFilled        { get; }
        public ConfigEntry<float> RangeDotLineThickness { get; }
        public ConfigEntry<bool>  FlashOnFiringSolution { get; }
        public ConfigEntry<Color> FiringSolutionColor   { get; }
        public ConfigEntry<bool>  HideWithGearDown      { get; }

        // ── Tracking ───────────────────────────────────────────────────────────

        public ConfigEntry<WingspanMode> WingspanModeSetting        { get; }
        public ConfigEntry<float>        DefaultWingspan            { get; }
        public ConfigEntry<int>          FunnelResolution           { get; }
        public ConfigEntry<float>        MinRangeMeters             { get; }
        public ConfigEntry<float>        MaxRangeMeters             { get; }
        public ConfigEntry<float>        MinTurnRate                { get; }
        public ConfigEntry<bool>         EnablePredictiveTracking   { get; }
        public ConfigEntry<float>        PredictiveTrackingStrength { get; }
        public ConfigEntry<float>        PredictiveTrackingMinRange { get; }
        public ConfigEntry<float>        PredictiveTrackingMaxRange { get; }
        public ConfigEntry<bool>         AutoTargetNearestEnemy     { get; }
        public ConfigEntry<int>          BallisticSimulationSteps   { get; }

        // ── Smoothing ──────────────────────────────────────────────────────────

        public ConfigEntry<float> TurnRateSmoothing { get; }

        // ── Constructor ────────────────────────────────────────────────────────

        public FunnelConfig(ConfigFile config)
        {
            // General
            Enabled = config.Bind(
                "General", "Enabled", true,
                "Turn the gun funnel overlay ON or OFF without uninstalling the mod.");

            DebugLogging = config.Bind(
                "General", "DebugLogging", false,
                "ON: writes turn rate, target info, and screen position to the BepInEx " +
                "log roughly every 2 seconds. Leave OFF during normal play.");

            ResetOverlayKey = config.Bind(
                "General", "ResetOverlayKey", new KeyboardShortcut(KeyCode.F9),
                "Keyboard shortcut that rebuilds the funnel overlay. Press this if it " +
                "gets stuck or disappears after switching aircraft.");

            InvertTurnDirection = config.Bind(
                "General", "InvertTurnDirection", true,
                "ON (default): correct for the Firefly Companion FBW mod, which flips " +
                "turn direction. OFF: use for stock aircraft or other FBW mods if the " +
                "funnel curves the wrong way during turns.");

            // Display
            FunnelOpacity = config.Bind(
                "Display", "FunnelOpacity", 1.0f,
                new ConfigDescription(
                    "How see-through the funnel appears. 1.0 = solid, 0.1 = nearly invisible.",
                    new AcceptableValueRange<float>(0.1f, 1.0f)));

            FunnelColor = config.Bind(
                "Display", "FunnelColor", Color.green,
                "Color of the funnel and pipper. Use FunnelOpacity to control transparency.");

            HideNativeBoresight = config.Bind(
                "Display", "HideNativeBoresight", true,
                "ON: hides the game's own gray gun crosshair while the funnel is active. " +
                "OFF: shows both at once (cluttered, not recommended).");

            ShowPipper = config.Bind(
                "Display", "ShowPipper", true,
                "ON: draws a small cross at the gun boresight point (HUD centre). " +
                "OFF: hides it, leaving only the funnel walls.");

            PipperSize = config.Bind(
                "Display", "PipperSize", 8.0f,
                new ConfigDescription(
                    "Size of the pipper cross in pixels (half-length of each arm).",
                    new AcceptableValueRange<float>(2f, 24f)));

            FunnelLineThickness = config.Bind(
                "Display", "FunnelLineThickness", 2.0f,
                new ConfigDescription(
                    "Thickness in pixels of the funnel walls and pipper cross. " +
                    "Increase if you lose sight of the pipper during hard turns.",
                    new AcceptableValueRange<float>(1f, 8f)));

            ShowRangeDot = config.Bind(
                "Display", "ShowRangeDot", true,
                "ON: when a target is locked, draws a circle on the funnel spine at the " +
                "target's current distance. When the target fills the circle you have " +
                "the correct range to fire. OFF: hides the range circle.");

            RangeDotSize = config.Bind(
                "Display", "RangeDotSize", 0.4f,
                new ConfigDescription(
                    "How large the range circle appears, as a fraction of the funnel " +
                    "width at that distance. 1.0 = fills the full gap; 0.4 (default) = 40%.",
                    new AcceptableValueRange<float>(0.1f, 1.0f)));

            RangeDotFilled = config.Bind(
                "Display", "RangeDotFilled", false,
                "OFF (default): range circle is an outline ring. ON: range circle is a " +
                "solid filled dot. RangeDotLineThickness still controls the edge in both modes.");

            RangeDotLineThickness = config.Bind(
                "Display", "RangeDotLineThickness", 2.0f,
                new ConfigDescription(
                    "Thickness in pixels of the range circle's outline (or edge, if " +
                    "RangeDotFilled is on). Separate from FunnelLineThickness so you can " +
                    "size it independently.",
                    new AcceptableValueRange<float>(1f, 8f)));

            FlashOnFiringSolution = config.Bind(
                "Display", "FlashOnFiringSolution", true,
                "Flash the funnel to FiringSolutionColor when the target is inside the " +
                "funnel walls at the correct range. Requires a locked target.");

            FiringSolutionColor = config.Bind(
                "Display", "FiringSolutionColor", Color.white,
                "Color the funnel flashes to when you have a firing solution.");

            HideWithGearDown = config.Bind(
                "Display", "HideWithGearDown", true,
                "ON: hides the funnel when the landing gear is deployed, matching the " +
                "native gun sight behaviour. OFF: keeps the funnel visible with gear down.");

            // Tracking
            WingspanModeSetting = config.Bind(
                "Tracking", "WingspanMode", WingspanMode.Fixed,
                "Fixed: funnel width always uses DefaultWingspan. Adaptive: funnel width " +
                "automatically matches the locked target's real wingspan.");

            DefaultWingspan = config.Bind(
                "Tracking", "DefaultWingspan", 11.0f,
                new ConfigDescription(
                    "Target wingspan in metres used to set the funnel width. Used in " +
                    "Fixed mode, or as a fallback in Adaptive mode for unknown aircraft.",
                    new AcceptableValueRange<float>(1f, 100f)));

            FunnelResolution = config.Bind(
                "Tracking", "FunnelResolution", 50,
                new ConfigDescription(
                    "How many points make up the funnel curve. Higher looks smoother " +
                    "at a small CPU cost.",
                    new AcceptableValueRange<int>(10, 100)));

            MinRangeMeters = config.Bind(
                "Tracking", "MinRangeMeters", 100.0f,
                new ConfigDescription(
                    "Distance in metres to the near (wide) end of the funnel.",
                    new AcceptableValueRange<float>(10f, 500f)));

            MaxRangeMeters = config.Bind(
                "Tracking", "MaxRangeMeters", 1200.0f,
                new ConfigDescription(
                    "Distance in metres to the far (narrow) end of the funnel.",
                    new AcceptableValueRange<float>(200f, 3000f)));

            MinTurnRate = config.Bind(
                "Tracking", "MinTurnRate", 0.01f,
                new ConfigDescription(
                    "Minimum turn rate in radians/second before the funnel switches to " +
                    "a fallback axis, so the funnel doesn't collapse to a straight line " +
                    "when flying level.",
                    new AcceptableValueRange<float>(0.001f, 0.5f)));

            EnablePredictiveTracking = config.Bind(
                "Tracking", "EnablePredictiveTracking", false,
                "Advanced: when a target is locked, factor in the target's own movement " +
                "to compute lead, not just your own turn rate. More accurate against " +
                "maneuvering targets. Requires a locked target.");

            PredictiveTrackingStrength = config.Bind(
                "Tracking", "PredictiveTrackingStrength", 1.0f,
                new ConfigDescription(
                    "How strongly predictive tracking influences the funnel. 0 = your " +
                    "own turn rate only; 1 = the target's movement fully.",
                    new AcceptableValueRange<float>(0f, 1f)));

            PredictiveTrackingMinRange = config.Bind(
                "Tracking", "PredictiveTrackingMinRange", 300f,
                new ConfigDescription(
                    "Minimum target distance in metres before predictive tracking starts " +
                    "blending in. Prevents noisy readings at close range.",
                    new AcceptableValueRange<float>(50f, 1000f)));

            PredictiveTrackingMaxRange = config.Bind(
                "Tracking", "PredictiveTrackingMaxRange", 800f,
                new ConfigDescription(
                    "Target distance in metres at which predictive tracking reaches full " +
                    "strength.",
                    new AcceptableValueRange<float>(100f, 3000f)));

            AutoTargetNearestEnemy = config.Bind(
                "Tracking", "AutoTargetNearestEnemy", true,
                "ON: when no target is locked, uses the closest enemy aircraft within " +
                "30° of your gun boresight to set funnel width (width only, no range dot). " +
                "OFF: always uses DefaultWingspan with no target locked.");

            BallisticSimulationSteps = config.Bind(
                "Tracking", "BallisticSimulationSteps", 20,
                new ConfigDescription(
                    "How many simulation steps model bullet drag and gravity for each " +
                    "funnel point. Higher is more accurate at long range.",
                    new AcceptableValueRange<int>(5, 100)));

            // Smoothing
            TurnRateSmoothing = config.Bind(
                "Smoothing", "TurnRateSmoothing", 0.35f,
                new ConfigDescription(
                    "How quickly the funnel reacts to changes in turn rate, in seconds. " +
                    "Higher is smoother but slower to respond. 0 = instant, no smoothing.",
                    new AcceptableValueRange<float>(0f, 0.5f)));
        }
    }
}
