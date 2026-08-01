using NuclearOption.UIStyleSystem;
using UnityEngine;

namespace FunnelGunSight
{
    // Resolves the colours the funnel draws with.
    //
    // 0.34 moved the HUD off hardcoded Color.green / Color.yellow and onto
    // ThemeManager.Active.ColorTheme, which the player picks in the settings menu
    // and can also supply as JSON from persistentDataPath/themes/. Reading the
    // same source keeps the funnel matching the rest of the HUD, custom themes
    // included, instead of sitting on top of it in a clashing green.
    //
    // Theme colours carry their own alpha, which is taken as-is and then scaled by
    // FunnelOpacity — at the default opacity of 1.0 that leaves the theme's alpha
    // untouched, while still letting a player dim the funnel without breaking the
    // colour match.
    internal static class HudTheme
    {
        // Reading ThemeManager.Active per frame is a static property fetch plus a
        // ScriptableObject field read, which is cheap enough not to cache. Not
        // caching also means a mid-session theme switch is picked up immediately,
        // with no need to subscribe to ThemeManager.ThemeGroupChanged.
        private static ColorTheme? ActiveTheme
        {
            get
            {
                try   { return ThemeManager.Active?.ColorTheme; }
                catch { return null; } // theme system not initialised yet
            }
        }

        // Normal funnel colour. AllClear is what the native boresight uses when it
        // is on target, so it is the closest match to what the funnel replaces.
        public static Color Funnel(FunnelConfig? cfg)
        {
            Color fallback = cfg?.FunnelColor.Value ?? Color.green;
            if (!(cfg?.FollowHudTheme.Value ?? false)) return fallback;
            return ActiveTheme?.AllClear ?? fallback;
        }

        // Firing-solution flash. HudUnitFlash is the theme's dedicated
        // attention colour, so it stays visually distinct from AllClear in any
        // theme a player might pick.
        public static Color FiringSolution(FunnelConfig? cfg)
        {
            Color fallback = cfg?.FiringSolutionColor.Value ?? Color.white;
            if (!(cfg?.FollowHudTheme.Value ?? false)) return fallback;
            return ActiveTheme?.HudUnitFlash ?? fallback;
        }
    }
}
