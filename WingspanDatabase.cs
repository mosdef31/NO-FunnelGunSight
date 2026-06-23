using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx.Logging;

namespace FunnelGunSight
{
    /// <summary>
    /// Loads wingspans.json from the mod folder and provides wingspan lookups by
    /// unit definition key ("plane ID"). The database is self-populating: when
    /// Adaptive mode encounters an aircraft that isn't in the file yet, its
    /// wingspan is derived from the live game data (<see cref="UnitDefinition.width"/>)
    /// and written back to wingspans.json under that aircraft's jsonKey, so the
    /// dataset grows automatically just by playing — no manual data entry needed.
    /// </summary>
    public sealed class WingspanDatabase
    {
        private readonly Dictionary<string, float> _wingspans =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        private readonly ManualLogSource _logger;
        private readonly string          _jsonPath;

        public WingspanDatabase(string jsonPath, ManualLogSource logger)
        {
            _jsonPath = jsonPath;
            _logger   = logger;
            LoadFromFile(jsonPath);
        }

        // ── Loading ───────────────────────────────────────────────────────────

        private void LoadFromFile(string jsonPath)
        {
            if (!File.Exists(jsonPath))
            {
                _logger.LogInfo(
                    $"[FunnelGunSight] wingspans.json not found at '{jsonPath}'. " +
                    "It will be created automatically as aircraft are encountered in Adaptive mode.");
                return;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);
                ParseWingspans(json);
                _logger.LogInfo(
                    $"[FunnelGunSight] Loaded wingspan database: {_wingspans.Count} entries.");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"[FunnelGunSight] Failed to load wingspans.json: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Minimal, dependency-free parser for the format:
        /// <c>{ "wingspans": { "key": float, ... } }</c>
        /// </summary>
        private void ParseWingspans(string json)
        {
            Match block = Regex.Match(json, @"""wingspans""\s*:\s*\{([^}]*)\}", RegexOptions.Singleline);
            if (!block.Success)
            {
                _logger.LogWarning("[FunnelGunSight] 'wingspans' section not found in JSON file.");
                return;
            }

            string section = block.Groups[1].Value;
            MatchCollection entries = Regex.Matches(section, @"""([^""]+)""\s*:\s*([\d.]+)");
            foreach (Match entry in entries)
            {
                string key = entry.Groups[1].Value.ToLowerInvariant();
                string raw = entry.Groups[2].Value;

                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float wingspan))
                    _wingspans[key] = wingspan;
                else
                    _logger.LogWarning($"[FunnelGunSight] Could not parse wingspan value '{raw}' for key '{key}'.");
            }
        }

        // ── Persisting ─────────────────────────────────────────────────────────

        /// <summary>
        /// Rewrites wingspans.json from the current in-memory dataset, sorted by
        /// key for readability. Called only when a new entry is auto-added, so
        /// this runs at most once per newly-encountered aircraft type per session
        /// — never every frame.
        /// </summary>
        private void SaveToFile()
        {
            try
            {
                var keys = new List<string>(_wingspans.Keys);
                keys.Sort(StringComparer.OrdinalIgnoreCase);

                var sb = new StringBuilder();
                sb.Append("{\n  \"wingspans\": {\n");
                for (int i = 0; i < keys.Count; i++)
                {
                    string key   = keys[i];
                    string value = _wingspans[key].ToString("F1", CultureInfo.InvariantCulture);
                    string comma = (i < keys.Count - 1) ? "," : "";
                    sb.Append($"    \"{key}\": {value}{comma}\n");
                }
                sb.Append("  }\n}\n");

                File.WriteAllText(_jsonPath, sb.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    $"[FunnelGunSight] Failed to save wingspans.json: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ── Lookup ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the wingspan in metres for the given unit.
        /// Lookup order: jsonKey → code → definition.width (auto-added) → defaultWingspan.
        /// </summary>
        public float GetWingspan(Unit target, float defaultWingspan)
        {
            if (target?.definition == null)
                return defaultWingspan;

            string jsonKey = target.definition.jsonKey?.ToLowerInvariant() ?? string.Empty;
            if (!string.IsNullOrEmpty(jsonKey) && _wingspans.TryGetValue(jsonKey, out float byKey))
                return byKey;

            string code = target.definition.code?.ToLowerInvariant() ?? string.Empty;
            if (!string.IsNullOrEmpty(code) && _wingspans.TryGetValue(code, out float byCode))
                return byCode;

            // Not in the database yet — derive from the live unit definition and
            // persist it under the canonical plane ID (jsonKey) so it's available
            // immediately next time, with zero manual data entry.
            float definitionWidth = target.definition.width;
            if (definitionWidth > 0f)
            {
                string autoKey = !string.IsNullOrEmpty(jsonKey) ? jsonKey : code;
                if (!string.IsNullOrEmpty(autoKey) && !_wingspans.ContainsKey(autoKey))
                {
                    _wingspans[autoKey] = definitionWidth;
                    _logger.LogInfo(
                        $"[FunnelGunSight] Auto-added '{autoKey}' = {definitionWidth:F1} m to wingspans.json.");
                    SaveToFile();
                }
                return definitionWidth;
            }

            return defaultWingspan;
        }
    }
}
