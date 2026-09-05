using System;
using System.Collections.Generic;
using HarmonyLib;

// ─────────────────────────────────────────────────────────────────────────────
//  LocalizationPatches
//
//  Serves the mod's own text through the game's LocalizationManager so vanilla
//  UI that calls Localize(itemEntry.descriptionLocKey) etc. gets English for
//  LSM_ tags without touching the I2 sources (see the Keep Clarity I2 handoff:
//  writing into I2's language grid bleeds across languages). Ported from
//  WotW's LocalizationPatches.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class LocalizationPatches
    {
        public const string TagPrefix = "LSM_";

        private static readonly Dictionary<string, string> _tags = new Dictionary<string, string>();

        private static string Tag => LiveStockMarketMod.LogTag;

        /// <summary>Registers (or replaces) tag → text. Tags must start with LSM_.</summary>
        public static void Register(string tag, string text)
        {
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith(TagPrefix, StringComparison.Ordinal)) return;
            _tags[tag] = text ?? string.Empty;
        }

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                var lmType = typeof(LocalizationManager);

                var localize = AccessTools.Method(lmType, "Localize", new[] { typeof(string) });
                if (localize != null)
                {
                    harmony.Patch(localize, prefix: new HarmonyMethod(
                        typeof(LocalizationPatches), nameof(LocalizePrefix)));
                }
                else
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} LocalizationPatches: Localize(string) not found — LSM_ tags will show raw.");
                }

                var isLocalized = AccessTools.Method(lmType, "IsLocalized", new[] { typeof(string) });
                if (isLocalized != null)
                {
                    harmony.Patch(isLocalized, prefix: new HarmonyMethod(
                        typeof(LocalizationPatches), nameof(IsLocalizedPrefix)));
                }

                LiveStockMarketMod.Log.Msg($"{Tag} LocalizationPatches: patched LocalizationManager.Localize / IsLocalized for {TagPrefix} tags");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} LocalizationPatches.Apply: {ex}");
            }
        }

        private static bool LocalizePrefix(string tag, ref string __result)
        {
            if (!string.IsNullOrEmpty(tag) && tag.StartsWith(TagPrefix, StringComparison.Ordinal)
                && _tags.TryGetValue(tag, out var text))
            {
                __result = text;
                return false;
            }
            return true;
        }

        private static bool IsLocalizedPrefix(string tag, ref bool __result)
        {
            if (!string.IsNullOrEmpty(tag) && tag.StartsWith(TagPrefix, StringComparison.Ordinal)
                && _tags.ContainsKey(tag))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }
}
