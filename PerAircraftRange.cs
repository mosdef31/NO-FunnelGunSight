using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FunnelGunSight
{
    /// <summary>
    /// Per-aircraft overrides for the CCIP max range, parsed out of one config
    /// string.
    ///
    /// WHY A STRING AND NOT A SETTING PER AIRCRAFT. The list of airframes is not
    /// ours - stock aircraft arrive with the game and modded ones arrive with
    /// whatever the player has installed - so there is no fixed set of keys to bind
    /// entries for. `rocket-pod` already answers this shape of question with a
    /// clause list (`ExtraHardpoints`), and a player who has edited one can edit
    /// this one.
    ///
    /// WHY A19 IS THE CASE THAT ASKED FOR IT. The global default of 2500 m is sized
    /// for a fighter's strafing pass. The A-19 Brawler is a slow CAS aircraft that
    /// opens fire from further out and holds the run longer, so the pipper goes
    /// blank exactly where it is most wanted. Owner's ask, 2026-09-16: make the
    /// distance configurable per aircraft and give the A-19 a long one.
    ///
    /// MATCHING IS THE SAME FUZZY MATCH `rocket-pod` USES, and deliberately so: an
    /// aircraft answers to several names - the prefab root, the `UnitDefinition`
    /// `unitName`, its `code`, its json key - and nobody agrees on the hyphen.
    /// "A-19", "A19" and "CAS1" all reach the Brawler.
    /// </summary>
    internal static class PerAircraftRange
    {
        /// <summary>
        /// Lower and upper bounds a parsed override is clamped into, in metres.
        /// The ceiling is above the global entry's own 6000 on purpose - the whole
        /// point of an override is to exceed what the slider allows - but a typo of
        /// an extra zero should not walk a trajectory to the horizon.
        /// </summary>
        private const float MinMeters = 300f;
        private const float MaxMeters = 12000f;

        private static string  _parsedFrom = "\u0000";   // never equal to a real config value
        private static Dictionary<string, float> _byName = new Dictionary<string, float>();

        /// <summary>Lowercase, letters and digits only. See the class note.</summary>
        private static string Normalize(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>
        /// Every name this aircraft answers to, most specific first. The
        /// `UnitDefinition` is where the display name and the code live; the prefab
        /// root name is what a modded airframe is usually keyed by.
        /// </summary>
        private static IEnumerable<string> NamesOf(Aircraft aircraft)
        {
            Transform? root = aircraft.transform != null ? aircraft.transform.root : null;
            if (root != null && !string.IsNullOrEmpty(root.name)) yield return root.name;

            if (!string.IsNullOrEmpty(aircraft.name)) yield return aircraft.name;

            UnitDefinition? def = aircraft.definition;
            if (def == null) yield break;
            if (!string.IsNullOrEmpty(def.unitName)) yield return def.unitName;
            if (!string.IsNullOrEmpty(def.code))     yield return def.code;
            if (!string.IsNullOrEmpty(def.name))     yield return def.name;
        }

        /// <summary>
        /// Re-parses the clause list when the config string has changed, so an edit
        /// in the configuration manager takes effect on the next station select
        /// without a restart.
        /// </summary>
        private static void EnsureParsed(string clauses)
        {
            if (clauses == _parsedFrom) return;
            _parsedFrom = clauses;

            var map = new Dictionary<string, float>();

            foreach (string clause in clauses.Split(';'))
            {
                string c = clause.Trim();
                if (c.Length == 0) continue;

                int colon = c.LastIndexOf(':');
                if (colon <= 0 || colon == c.Length - 1)
                {
                    FunnelGunSightPlugin.Instance?.Logger.LogWarning(
                        $"[FunnelGunSight] GroundSightMaxRangePerAircraft: '{c}' is not " +
                        "'<aircraft>:<metres>' and was ignored.");
                    continue;
                }

                string key = Normalize(c.Substring(0, colon));
                string val = c.Substring(colon + 1).Trim();

                if (key.Length == 0 ||
                    !float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float metres))
                {
                    FunnelGunSightPlugin.Instance?.Logger.LogWarning(
                        $"[FunnelGunSight] GroundSightMaxRangePerAircraft: '{c}' has no readable " +
                        "distance and was ignored.");
                    continue;
                }

                map[key] = Mathf.Clamp(metres, MinMeters, MaxMeters);
            }

            _byName = map;
        }

        /// <summary>
        /// The CCIP max range for this aircraft, in metres: its override if it has
        /// one, otherwise the global setting. Logs which it picked, once per
        /// airframe, because a pipper that reaches further on one aircraft than
        /// another is otherwise indistinguishable from a bug.
        /// </summary>
        internal static float Resolve(Aircraft? aircraft, FunnelConfig config)
        {
            float fallback = Mathf.Clamp(
                config.GroundSightMaxRange.Value,
                FunnelConfig.GroundSightMaxRangeMin, FunnelConfig.GroundSightMaxRangeMax);
            if (aircraft == null) return fallback;

            EnsureParsed(config.GroundSightMaxRangePerAircraft.Value);
            if (_byName.Count == 0) return fallback;

            foreach (string name in NamesOf(aircraft))
            {
                if (_byName.TryGetValue(Normalize(name), out float metres))
                {
                    FunnelGunSightPlugin.Instance?.Logger.LogInfo(
                        $"[FunnelGunSight] ground sight max range {metres:F0} m for '{name}' " +
                        $"(global is {fallback:F0} m).");
                    return metres;
                }
            }

            return fallback;
        }
    }
}
