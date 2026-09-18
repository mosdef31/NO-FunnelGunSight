using BepInEx.Configuration;
using UnityEngine;

namespace FunnelGunSight
{
    public enum WingspanMode { Fixed, Adaptive }

    // All BepInEx config entries for FunnelGunSight.
    // Sections: General | Sights | Display | Tracking
    //
    // THE `Sights` SECTION IS WHY THIS MOD IS NO LONGER ONLY A FUNNEL. Owner,
    // 2026-09-16: "i want to have different styles of gunsights and player can
    // chose which he wants. existing together not replacing." So each sight is its
    // own independent toggle rather than one mode selector - turning the tracer
    // line on does not turn the funnel off, and a player who wants all four at
    // once can have all four at once. A single-select enum would have been smaller
    // code and would have answered a different question.
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
        public ConfigEntry<KeyboardShortcut> RunProofsKey        { get; }
        public ConfigEntry<bool>             InvertTurnDirection { get; }

        // ── Sights ─────────────────────────────────────────────────────────────

        public ConfigEntry<bool>  ShowFunnel          { get; }
        public ConfigEntry<bool>  ShowGroundSight     { get; }
        public ConfigEntry<float> GroundSightMaxRange { get; }
        public ConfigEntry<string> GroundSightMaxRangePerAircraft { get; }
        public ConfigEntry<GroundSightShape> GroundSightShapeSetting { get; }
        public ConfigEntry<float> GroundSightSize      { get; }
        public ConfigEntry<bool>  FlashGroundSightOnTarget { get; }
        public ConfigEntry<float> GroundSightFlashHz   { get; }
        public ConfigEntry<bool>  MarkGroundTargetKind { get; }
        public ConfigEntry<bool>  ShowLeadDot         { get; }
        public ConfigEntry<float> LeadDotRange        { get; }
        public ConfigEntry<bool>  ShowRangeReadout    { get; }

        // ── Numeric bounds ─────────────────────────────────────────────────────
        //
        // Pulled out of the AcceptableValueRange calls below and applied by hand at
        // each read site instead. ConfigurationManager's numeric text box re-parses
        // and re-clamps on EVERY KEYSTROKE when a range is attached, which is what
        // turns typing "15" into "35" for a 3-40 field: the "1" clamps up to the
        // minimum (3) before the "5" is even typed. Dropping the range gets the
        // plain text box, which only validates on Enter/blur; the bound is not
        // lost, just moved to where it can't fight the player's typing.
        public const float GroundSightMaxRangeMin = 300f, GroundSightMaxRangeMax = 6000f;
        public const float GroundSightSizeMin     = 3f,   GroundSightSizeMax     = 40f;
        public const float GroundSightFlashHzMin  = 1f,   GroundSightFlashHzMax  = 12f;
        public const float LeadDotRangeMin        = 100f, LeadDotRangeMax        = 1500f;

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
        public ConfigEntry<bool>         AspectAwareSizing          { get; }
        public ConfigEntry<float>        MaxTargetSizeMeters        { get; }

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

            // LEFTALT COMBO RATHER THAN A BARE FUNCTION KEY. The F row is contested
            // - the game and several other plugins already claim most of it - and a
            // probe that fights another mod for its key is a probe nobody runs.
            RunProofsKey = config.Bind(
                "General", "RunProofsKey", new KeyboardShortcut(KeyCode.P, KeyCode.LeftAlt),
                new ConfigDescription(
                    "Runs the three measurement proofs - gun data, aspect-dependent " +
                    "sizing and ground-sight impact accuracy - and writes a report " +
                    "into probe-runs/ beside the game's saved data. Run it in a " +
                    "mission, in an aircraft with a gun, pointing at terrain.",
                    null, Ui(79, advanced: true, name: "Run measurement proofs key")));

            DebugLogging = config.Bind(
                "General", "DebugLogging", false,
                new ConfigDescription(
                    "Writes turn rate, target state, and screen positions to the " +
                    "BepInEx log every ~2 seconds. For troubleshooting only; leave OFF " +
                    "during normal play.",
                    null, Ui(10, advanced: true, name: "Debug logging")));

            // ── Sights ─────────────────────────────────────────────────────────
            //
            // Four sights, four independent switches. None of them turns another
            // one off. See the section note at the top of the file.

            ShowFunnel = config.Bind(
                "Sights", "ShowFunnel", true,
                new ConfigDescription(
                    "The envelope sight: two walls one target-wingspan apart, curved " +
                    "along your plane of motion. This is the sight the mod is named " +
                    "for and the right default for air-to-air.",
                    null, Ui(100, name: "Envelope sight (funnel)")));

            ShowGroundSight = config.Bind(
                "Sights", "ShowGroundSight", true,
                new ConfigDescription(
                    "CCIP for strafing: a pipper on the spot where the rounds meet the " +
                    "ground, solved through the same drag and gravity as the funnel. " +
                    "Drawn only when the gun line actually reaches terrain, water or a " +
                    "ship, so it stays out of the way in level flight.",
                    null, Ui(85, name: "Ground attack sight (CCIP)")));

            GroundSightMaxRange = config.Bind(
                "Sights", "GroundSightMaxRange", 2500.0f,
                new ConfigDescription(
                    "Stop looking for an impact point beyond this distance, in metres. " +
                    "Lower it if the pipper is distracting during level flight over " +
                    $"rising ground. ({GroundSightMaxRangeMin:F0}-{GroundSightMaxRangeMax:F0})",
                    null, Ui(80, name: "Ground sight max range (m)")));

            GroundSightMaxRangePerAircraft = config.Bind(
                "Sights", "GroundSightMaxRangePerAircraft", "A-19:5000",
                new ConfigDescription(
                    "Per-aircraft overrides for the setting above, as a semicolon-separated " +
                    "list of '<aircraft>:<metres>' clauses - for example " +
                    "'A-19:5000; OA-27:4000'. An aircraft with no clause uses the global " +
                    "value. Names are matched loosely, so 'A-19', 'A19' and the internal " +
                    "key 'CAS1' all reach the Brawler. The A-19 is here by default because " +
                    "it opens fire from further out than the 2500 m a fighter's strafing " +
                    "pass needs.",
                    null, Ui(78, advanced: true, name: "Ground sight max range, per aircraft")));

            GroundSightShapeSetting = config.Bind(
                "Sights", "GroundSightShape", GroundSightShape.SegmentedRings,
                new ConfigDescription(
                    "Which reticle the ground-attack sight draws. SegmentedRings is the " +
                    "default; the other four are the supplied designs - a plain ring and " +
                    "dot, a helicopter crosshair, an inward-pointing diamond and a " +
                    "stalked funnel pipper. Shape only; the impact point it marks is " +
                    "solved identically in every case.",
                    null, Ui(77, name: "Ground sight shape")));

            GroundSightSize = config.Bind(
                "Sights", "GroundSightSize", 18.0f,
                new ConfigDescription(
                    "Angular radius the ground pipper is drawn at, in milliradians. It is " +
                    "an ANGLE rather than a pixel count so the reticle keeps the same " +
                    "apparent size through a zoom. The four detailed shapes carry ladders " +
                    $"and ticks that need room, which is why the default sits at 18 rather " +
                    $"than the plain ring's own comfortable 9. ({GroundSightSizeMin:F0}-" +
                    $"{GroundSightSizeMax:F0})",
                    null, Ui(76, name: "Ground sight size (mrad)")));

            FlashGroundSightOnTarget = config.Bind(
                "Sights", "FlashGroundSightOnTarget", true,
                new ConfigDescription(
                    "Flash the ground pipper when the rounds would land on something " +
                    "killable rather than on bare ground or open water. Owner, " +
                    "2026-09-17: \"i want the Ground attack sight ccip to flash when " +
                    "ground target is a hit.\"",
                    null, Ui(75, name: "Flash the ground sight on a hit")));

            GroundSightFlashHz = config.Bind(
                "Sights", "GroundSightFlashHz", 4.0f,
                new ConfigDescription(
                    "How fast the ground pipper flashes, in cycles per second, when it is " +
                    $"over a target. ({GroundSightFlashHzMin:F0}-{GroundSightFlashHzMax:F0})",
                    null, Ui(74, advanced: true, name: "Ground sight flash rate (Hz)")));

            MarkGroundTargetKind = config.Bind(
                "Sights", "MarkGroundTargetKind", true,
                new ConfigDescription(
                    "Name what the rounds would hit beside the ground pipper - BUILDING, " +
                    "VEHICLE, SHIP or AIRCRAFT - so a ground target is distinguishable " +
                    "from an aircraft that has drifted into the gun line.",
                    null, Ui(73, name: "Name the ground target type")));

            ShowLeadDot = config.Bind(
                "Sights", "ShowLeadDot", false,
                new ConfigDescription(
                    "A lead-computing pipper (LCOS): one dot at the lead solution for a " +
                    "single range, instead of the funnel's fifty. It answers instantly " +
                    "and it is wrong the moment the range assumption is wrong. With a " +
                    "locked target it uses the real range; without one it uses the " +
                    "assumed range below.",
                    null, Ui(75, name: "Lead pipper (LCOS)")));

            LeadDotRange = config.Bind(
                "Sights", "LeadDotRange", 300.0f,
                new ConfigDescription(
                    "The range the lead pipper assumes when you have no lock, in metres. " +
                    $"Typical guns-merge range is a good setting. ({LeadDotRangeMin:F0}-" +
                    $"{LeadDotRangeMax:F0})",
                    null, Ui(70, name: "Lead pipper assumed range (m)")));

            ShowRangeReadout = config.Bind(
                "Sights", "ShowRangeReadout", true,
                new ConfigDescription(
                    "Print the range as a number beside the range circle and the ground " +
                    "pipper, in hundreds of metres - '7' is 700 m. A circle tells you " +
                    "where; this tells you how far.",
                    null, Ui(65, name: "Range readout")));

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
                    "ON (default): hide the game's own grey gun pip while the funnel is " +
                    "up. OFF: draw the stock pip alongside the funnel. The two disagree " +
                    "at longer ranges and the funnel is the correct one - the stock pip " +
                    "leads target motion on a drag-free time of flight. See README, " +
                    "Known quirks.",
                    null, Ui(35, name: "Hide stock gun pip")));

            // ── Tracking ───────────────────────────────────────────────────────

            WingspanModeSetting = config.Bind(
                "Tracking", "WingspanMode", WingspanMode.Adaptive,
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

            // THE KEY IS RENAMED ON PURPOSE AND THIS IS THE SECOND TIME.
            //
            // 1.1.0 moved it out of the old "Smoothing" section and cut it from
            // 0.35 s to 0.15 s, because the old value lagged the funnel behind a
            // rising pull. Owner, 2026-09-16, on whether 0.15 should stay: "lets
            // change and test."
            //
            // The real EEGS takes 0.5 to 1.5 s to settle on a plane-of-motion
            // change (f-16.net). Ours at 0.15 s was three times faster than the
            // fastest real one, which is why it reads as twitchy. The default is
            // now 0.5 s - the bottom of the documented band, so it is steadier
            // without being sluggish - and the allowed range runs to 1.5 s so the
            // whole real span can be flown rather than just the fast end of it.
            //
            // A default change alone would have done NOTHING: BepInEx keeps the
            // value already written in the config file, so anyone who has flown
            // this mod would have kept 0.15 s and the test would have silently not
            // happened. Renaming the key is what forces the new default to be
            // taken. That is the same trick 1.1.0 used, for the same reason.
            TurnRateSmoothing = config.Bind(
                "Tracking", "TurnRateSmoothingSeconds", 0.50f,
                new ConfigDescription(
                    "How long the funnel takes to react to a change in turn rate, in " +
                    "seconds. Higher is steadier but lags behind hard manoeuvres; 0 is " +
                    "instant but jittery. The real EEGS gun sight this is modelled on " +
                    "settles in 0.5 to 1.5 s, so that is the honest band; below about " +
                    "0.2 s the funnel is faster than any real sight and shows it.",
                    new AcceptableValueRange<float>(0f, 1.5f),
                    Ui(75, name: "Turn rate smoothing (s)")));

            AspectAwareSizing = config.Bind(
                "Tracking", "AspectAwareSizing", true,
                new ConfigDescription(
                    "Size the funnel walls to how wide the target actually LOOKS right " +
                    "now, instead of always to its wingspan. A target seen head-on is " +
                    "showing you its length, not its span, and walls set a wingspan " +
                    "apart then ask you to fit a dimension that is not on screen. " +
                    "Requires a target; falls back to the wingspan without one.",
                    null, Ui(72, name: "Aspect-aware sizing")));

            // ONE CAP FOR BOTH SIZING PATHS. Owner, having flown 1.2.0: "make the
            // adaptive width (wingspan) mode recognize buildings and ground units,
            // or cap its max size in meters." It applies to the Adaptive wingspan
            // lookup AND to aspect-aware sizing, because both can be handed a
            // hangar, a carrier or a bridge and neither has any business drawing a
            // hundred-metre envelope across the whole canopy.
            //
            // 30 m is a shade over the largest aircraft in the game, so nothing
            // that is actually a gun target is clipped by it.
            MaxTargetSizeMeters = config.Bind(
                "Tracking", "MaxTargetSizeMeters", 30.0f,
                new ConfigDescription(
                    "Largest target width the funnel will size itself to, in metres. " +
                    "Stops a hangar, a ship or a bridge from opening the walls across " +
                    "the whole screen. Applies to both Adaptive wingspan mode and " +
                    "aspect-aware sizing.",
                    new AcceptableValueRange<float>(5f, 200f),
                    Ui(71, name: "Max target size (m)")));

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
                    "Safety ceiling on the bullet simulation, not an accuracy knob. The " +
                    "funnel now steps its trajectory at the game's own physics rate so " +
                    "it predicts the game's bullet exactly; this is only the floor on " +
                    "how many steps a very short shot is allowed. Leave it alone.",
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
