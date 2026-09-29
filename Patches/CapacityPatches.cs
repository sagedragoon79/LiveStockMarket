using System;
using HarmonyLib;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  CapacityPatches (1.2.0)
//
//  Scale a livestock building's herd maximum before vanilla reads it:
//  • LivestockBuilding.Load (prefix): Load trims the saved herd-size setting to
//    numLivestockToBeOverpopulated − 1, so the asset must already be scaled or a
//    raised setting is lost on every load.
//  • LivestockBuilding.Start (prefix): a new building sets its herd size to the
//    maximum in Start; subclasses (Barn, GoatBarn, ChickenCoop, Stable, the
//    kennels) reach it through base.Start().
//  The Map-scene sweep in LivestockCapacity covers herds that load before their
//  building; these prefixes are the guarantee.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class CapacityPatches
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                int patched = 0;
                var load = AccessTools.Method(typeof(LivestockBuilding), "Load", new[] { typeof(ES2Reader) });
                if (load != null)
                {
                    harmony.Patch(load, prefix: new HarmonyMethod(typeof(CapacityPatches), nameof(ScalePrefix)));
                    patched++;
                }
                var start = AccessTools.Method(typeof(LivestockBuilding), "Start");
                if (start != null)
                {
                    harmony.Patch(start, prefix: new HarmonyMethod(typeof(CapacityPatches), nameof(ScalePrefix)));
                    patched++;
                }
                if (patched == 2)
                    LiveStockMarketMod.Log.Msg($"{Tag} CapacityPatches: patched LivestockBuilding.Load + Start (herd maximum scaled before vanilla reads it)");
                else
                    LiveStockMarketMod.Log.Warning($"{Tag} CapacityPatches: only {patched}/2 methods patched — a raised herd size may be trimmed on load.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} CapacityPatches.Register: {ex}");
            }
        }

        private static void ScalePrefix(LivestockBuilding __instance)
        {
            try
            {
                if (__instance != null) LivestockCapacity.Ensure(__instance.herdSetupData, __instance);   // the building type picks the slider
            }
            catch { /* vanilla continues with the unscaled maximum */ }
        }
    }
}
