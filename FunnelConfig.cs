using BepInEx.Configuration;
using UnityEngine;

namespace FunnelGunSight
{
    public enum WingspanMode { Fixed, Adaptive }

    // All BepInEx config entries for FunnelGunSight.
    // Sections: General | Display | Tracking
    //
    // Every entry carries a ConfigurationManagerAttributes tag. `Order` puts
    // related settings next to each other instead of letting ConfigurationManager
    // sort them alphabetically (which used to separate, say, ShowRangeDot from
    // RangeDotSize). `IsAdvanced` hides the expert knobs behind the "Advanced
    // settings" tickbox, so a new user sees roughly half as many options and none
    // of the ones that need the TECHNICAL.md math to set sensibly.
    public sealed class FunnelConfig
    {
        // ── General ────────────────────────────────────────────────────────────

        public ConfigEntry<bool>             Enabled             { get; }
        public ConfigEntry<bool>             DebugLogging        { get; }
        public ConfigEntry<KeyboardShortcut> ResetOverlayKey     { get; }
        public ConfigEntry<bool>             InvertTurnDirection { get; }

        // ── Display ────────────────────────────────────────────────────────────

        public ConfigEntry<bool>  FollowHudTheme        { get; }
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
        public ConfigEntry<float>        TurnRateSmoothing          { get; }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static ConfigurationManagerAttributes Ui(
            int order, bool advanced = false, string? name = null) =>
            new ConfigurationManagerAttributes
            {
                Order      = order,
                IsAdvanced = advanced,
                DispName   = name
            };

        // ── Constructor ────────────────────────────────────────────────────────

        public FunnelConfig(ConfigFile config)
        {
            // ── General ────────────────────────────────────────────────────────

            Enabled = config.Bind(
                "General", "Enabled", true,
                new ConfigDescription(
                    "Master switch for the gun funnel. Turn it off to disable the " +
                    "overlay without uninstalling the mod.",
                    null, Ui(100, name: "Enable funnel")));

            InvertTurnDirection = config.Bind(
                "General", "InvertTurnDirection", true,
                new ConfigDescription(
                    "Turn this OFF if the funnel curves the wrong way during turns. " +
                    "ON (default) matches the Firefly Companion FBW mod, which flips " +
                    "turn direction.",
                    null, Ui(90, name: "Invert turn direction")));

            ResetOverlayKey = config.Bind(
                "General", "ResetOverlayKey", new KeyboardShortcut(KeyCode.F9),
                new ConfigDescription(
                    "Rebuilds the overlay. Press this if the funnel gets stuck or " +
                    "disappears after switching aircraft.",
                    null, Ui(80, name: "Reset overlay key")));

            DebugLogging = config.Bind(
                "General", "DebugLogging", false,
                new ConfigDescription(
                    "Writes turn rate, target state, and screen positions to the " +
                    "BepInEx log every ~2 seconds. For troubleshooting only; leave OFF " +
                    "during normal play.",
                    null, Ui(10, advanced: true, name: "Debug logging")));

            // ── Display ────────────────────────────────────────────────────────

            FollowHudTheme = config.Bind(
                "Display", "FollowHudTheme", true,
                new ConfigDescription(
                    "ON (default): the funnel takes its colour and transparency from " +
                    "the game's current HUD theme, so it always matches the rest of the " +
                    "HUD — including custom themes. OFF: use the FunnelColor and " +
                    "FiringSolutionColor settings below instead.",
                    null, Ui(100, name: "Match HUD theme")));

            FunnelColor = config.Bind(
                "Display", "FunnelColor", Color.green,
                new ConfigDescription(
                    "Colour of the funnel walls and pipper. Only used when " +
                    "'Match HUD theme' is OFF.",
                    null, Ui(95, name: "Funnel colour")));

            FunnelOpacity = config.Bind(
                "Display", "FunnelOpacity", 1.0f,
                new ConfigDescription(
                    "How see-through the funnel is. 1.0 = fully solid, 0.1 = nearly " +
                    "invisible. Applies in both colour modes, so you can dim a " +
                    "theme-matched funnel without breaking the colour match.",
                    new AcceptableValueRange<float>(0.1f, 1.0f),
                    Ui(90, name: "Opacity")));

            FlashOnFiringSolution = config.Bind(
                "Display", "FlashOnFiringSolution", true,
                new ConfigDescription(
                    "Flash the funnel when the target is inside the walls at the right " +
                    "range. Requires a locked target.",
                    null, Ui(85, name: "Flash on firing solution")));

            FiringSolutionColor = config.Bind(
                "Display", "FiringSolutionColor", Color.white,
                new ConfigDescription(
                    "Colour the funnel flashes to on a firing solution. Only used when " +
                    "'Match HUD theme' is OFF.",
                    null, Ui(80, name: "Firing solution colour")));

            ShowPipper = config.Bind(
                "Display", "ShowPipper", true,
                new ConfigDescription(
                    "Draw a small cross at the gun boresight, in the middle of the funnel.",
                    null, Ui(75, name: "Show pipper")));

            PipperSize = config.Bind(
                "Display", "PipperSize", 8.0f,
                new ConfigDescription(
                    "Length of each pipper arm, in pixels.",
                    new AcceptableValueRange<float>(2f, 24f),
                    Ui(70, name: "Pipper size")));

            FunnelLineThickness = config.Bind(
                "Display", "FunnelLineThickness", 2.0f,
                new ConfigDescription(
                    "Line thickness of the funnel walls and pipper, in pixels. Increase " +
                    "if you lose sight of them during hard turns.",
                    new AcceptableValueRange<float>(1f, 8f),
                    Ui(65, name: "Line thickness")));

            ShowRangeDot = config.Bind(
                "Display", "ShowRangeDot", true,
                new ConfigDescription(
                    "Draw a circle on the funnel at the locked target's actual distance. " +
                    "When the target fills the circle, range and lead are both correct.",
                    null, Ui(60, name: "Show range circle")));

            RangeDotSize = config.Bind(
                "Display", "RangeDotSize", 0.4f,
                new ConfigDescription(
                    "Size of the range circle as a fraction of the funnel width at that " +
                    "distance. 1.0 fills the gap between the walls; 0.4 is the default.",
                    new AcceptableValueRange<float>(0.1f, 1.0f),
                    Ui(55, name: "Range circle size")));

            RangeDotFilled = config.Bind(
                "Display", "RangeDotFilled", false,
                new ConfigDescription(
                    "ON: draw the range circle as a solid disc. OFF (default): draw it " +
                    "as an outline ring.",
                    null, Ui(50, name: "Filled range circle")));

            RangeDotLineThickness = config.Bind(
                "Display", "RangeDotLineThickness", 2.0f,
                new ConfigDescription(
                    "Line thickness of the range circle, in pixels. Separate from the " +
                    "funnel wall thickness so you can size the two independently.",
                    new AcceptableValueRange<float>(1f, 8f),
                    Ui(45, name: "Range circle thickness")));

            HideWithGearDown = config.Bind(
                "Display", "HideWithGearDown", true,
                new ConfigDescription(
                    "Hide the funnel while the landing gear is down, matching the native " +
                    "gun sight.",
                    null, Ui(40, name: "Hide with gear down")));

            HideNativeBoresight = config.Bind(
                "Display", "HideNativeBoresight", true,
                new ConfigDescription(
                    "ON (default): hide the game's own grey gun crosshair while the " +
                    "funnel is up. OFF: draw both at once, which is cluttered and mainly " +
                    "useful for comparing the two.",
                    null, Ui(10, advanced: true, name: "Hide native crosshair")));

            // ── Tracking ───────────────────────────────────────────────────────

            WingspanModeSetting = config.Bind(
                "Tracking", "WingspanMode", WingspanMode.Fixed,
                new ConfigDescription(
                    "Fixed: the funnel is always sized for DefaultWingspan. Adaptive: it " +
                    "resizes to the locked target's real wingspan, learning each aircraft " +
                    "type the first time you meet it.",
                    null, Ui(100, name: "Wingspan mode")));

            DefaultWingspan = config.Bind(
                "Tracking", "DefaultWingspan", 11.0f,
                new ConfigDescription(
                    "Target wingspan in metres that sets the funnel width. Used in Fixed " +
                    "mode, and as the fallback for unknown aircraft in Adaptive mode. " +
                    "11 m is roughly an FS-12.",
                    new AcceptableValueRange<float>(1f, 100f),
                    Ui(95, name: "Default wingspan (m)")));

            AutoTargetNearestEnemy = config.Bind(
                "Tracking", "AutoTargetNearestEnemy", true,
                new ConfigDescription(
                    "With no target locked, size the funnel using the closest enemy " +
                    "aircraft within 30° of the boresight. Width only — no range circle, " +
                    "since it is not a confirmed lock.",
                    null, Ui(90, name: "Auto-size on nearest enemy")));

            MinRangeMeters = config.Bind(
                "Tracking", "MinRangeMeters", 100.0f,
                new ConfigDescription(
                    "Distance to the near, wide end of the funnel, in metres.",
                    new AcceptableValueRange<float>(10f, 500f),
                    Ui(85, name: "Funnel near range (m)")));

            MaxRangeMeters = config.Bind(
                "Tracking", "MaxRangeMeters", 1200.0f,
                new ConfigDescription(
                    "Distance to the far, narrow end of the funnel, in metres.",
                    new AcceptableValueRange<float>(200f, 3000f),
                    Ui(80, name: "Funnel far range (m)")));

            // Moved out of the old "Smoothing" section in 1.1.0 so this setting is
            // next to the tracking behaviour it affects. The move deliberately
            // resets it: the old 0.35 s default lagged the funnel behind a rising
            // pull, which underleads in exactly the hard turn where you shoot.
            TurnRateSmoothing = config.Bind(
                "Tracking", "TurnRateSmoothing", 0.15f,
                new ConfigDescription(
                    "How long the funnel takes to react to a change in turn rate, in " +
                    "seconds. Higher is steadier but lags behind hard manoeuvres; 0 is " +
                    "instant but jittery.",
                    new AcceptableValueRange<float>(0f, 0.5f),
                    Ui(75, name: "Turn rate smoothing (s)")));

            EnablePredictiveTracking = config.Bind(
                "Tracking", "EnablePredictiveTracking", false,
                new ConfigDescription(
                    "Factor a locked target's own movement into the lead, not just your " +
                    "turn rate. More accurate against a manoeuvring target. Requires a lock.",
                    null, Ui(70, name: "Predictive tracking")));

            PredictiveTrackingStrength = config.Bind(
                "Tracking", "PredictiveTrackingStrength", 1.0f,
                new ConfigDescription(
                    "How much predictive tracking influences the funnel. 0 = your turn " +
                    "rate only, 1 = the target's line-of-sight rate fully.",
                    new AcceptableValueRange<float>(0f, 1f),
                    Ui(65, advanced: true, name: "Predictive strength")));

            PredictiveTrackingMinRange = config.Bind(
                "Tracking", "PredictiveTrackingMinRange", 300f,
                new ConfigDescription(
                    "Distance in metres at which predictive tracking starts blending in. " +
                    "Below this the line-of-sight rate is too noisy to use.",
                    new AcceptableValueRange<float>(50f, 1000f),
                    Ui(60, advanced: true, name: "Predictive min range (m)")));

            PredictiveTrackingMaxRange = config.Bind(
                "Tracking", "PredictiveTrackingMaxRange", 800f,
                new ConfigDescription(
                    "Distance in metres at which predictive tracking reaches full strength.",
                    new AcceptableValueRange<float>(100f, 3000f),
                    Ui(55, advanced: true, name: "Predictive max range (m)")));

            FunnelResolution = config.Bind(
                "Tracking", "FunnelResolution", 50,
                new ConfigDescription(
                    "How many points make up the funnel curve. Higher is smoother at a " +
                    "small CPU cost.",
                    new AcceptableValueRange<int>(10, 100),
                    Ui(20, advanced: true, name: "Funnel resolution")));

            BallisticSimulationSteps = config.Bind(
                "Tracking", "BallisticSimulationSteps", 40,
                new ConfigDescription(
                    "Simulation steps used to model bullet drag and gravity for each " +
                    "funnel point. 40 keeps the time-of-flight error under about 2% at " +
                    "1200 m; lowering it trades accuracy for CPU.",
                    new AcceptableValueRange<int>(5, 100),
                    Ui(15, advanced: true, name: "Ballistic sim steps")));

            MinTurnRate = config.Bind(
                "Tracking", "MinTurnRate", 0.01f,
                new ConfigDescription(
                    "Turn rate in radians/second below which the funnel uses a fallback " +
                    "axis, so it does not collapse to a straight line in level flight.",
                    new AcceptableValueRange<float>(0.001f, 0.5f),
                    Ui(10, advanced: true, name: "Minimum turn rate (rad/s)")));
        }
    }
}
