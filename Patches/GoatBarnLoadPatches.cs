using System;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  GoatBarnLoadPatches
//
//  Load-phase application hook — Manifest Delivery's OnGameFinishedLoadingFinalize
//  pattern (see WotW's FishingShackLoadPatches). LivestockBuilding.Load registers
//  a one-shot listener for GameFinishedLoadingFinalizeEvent; the handler is where
//  vanilla (re)builds the herd and starts work state for a LOADED barn. Applying
//  mode-driven data changes here means vanilla initializes at the right values
//  from the first tick instead of re-scanning after a delayed attach.
//
//  Step 1: the hook only logs which barns came back in Sheep mode — a visible
//  confirmation in the MelonLoader log during the toggle → save → reload test.
//  Later steps put radius / capacity / worker changes here. If a change must
//  precede vanilla's herd setup, switch this to a PREFIX (the handler runs
//  vanilla's setup inline).
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class GoatBarnLoadPatches
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                var method = AccessTools.Method(typeof(LivestockBuilding), "OnGameFinishedLoadingFinalize");
                if (method == null)
                {
                    LiveStockMarketMod.Log.Warning(
                        $"{Tag} GoatBarnLoadPatches: LivestockBuilding.OnGameFinishedLoadingFinalize not found — load-phase hook disabled.");
                    return;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(
                    typeof(GoatBarnLoadPatches), nameof(LoadFinalizePostfix)));
                LiveStockMarketMod.Log.Msg(
                    $"{Tag} GoatBarnLoadPatches: patched {method.DeclaringType.Name}.{method.Name} (load-phase mode hook)");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GoatBarnLoadPatches.Register: {ex}");
            }
        }

        private static void LoadFinalizePostfix(object __instance)
        {
            try
            {
                if (!(__instance is GoatBarn barn)) return;

                var mode = GoatBarnModeStore.GetMode(barn);

                // Second chance for the mode-driven name, in case the localization
                // manager wasn't reachable during Load. Idempotent.
                GoatBarnNaming.Apply(barn);

                // Step 1: no behavior to apply. Later steps branch on `mode` here.
                if (mode != GoatBarnModeStore.DefaultMode)
                    LiveStockMarketMod.Log.Msg(
                        $"{Tag} Load-finalize: '{barn.gameObject.name}' is in {mode} mode as '{barn.displayName}' (step 1: no behavior change).");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GoatBarnLoadPatches.Postfix: {ex.Message}");
            }
        }
    }
}
