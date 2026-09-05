using System;
using HarmonyLib;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  GoatBarnNamePatches
//
//  Re-applies the mode-driven display name whenever vanilla regenerates it.
//  Building.SetBuildingDataRecordName(string, bool) is where every goat barn
//  gets "Goat Barn" / "Large Goat Barn" from the localization key: on load
//  (force: true from Building), on a fresh build, and on the tier upgrade
//  (the upgrade path calls it on the NEW instance, which sits at the same
//  position — so the position-keyed mode already says Sheep and the new barn
//  comes up as "Large Sheep Barn").
//
//  During a load this postfix runs before the saved mode is known (the mode is
//  read by the LivestockBuilding.Load postfix), so it sees Goats and leaves the
//  vanilla name; the Load postfix then applies Sheep. Apply() is idempotent.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class GoatBarnNamePatches
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                var method = AccessTools.Method(typeof(Building), "SetBuildingDataRecordName",
                    new[] { typeof(string), typeof(bool) });
                if (method == null)
                {
                    LiveStockMarketMod.Log.Warning(
                        $"{Tag} GoatBarnNamePatches: Building.SetBuildingDataRecordName not found — Sheep name won't survive upgrades.");
                    return;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(
                    typeof(GoatBarnNamePatches), nameof(SetBuildingDataRecordNamePostfix)));
                LiveStockMarketMod.Log.Msg(
                    $"{Tag} GoatBarnNamePatches: patched Building.SetBuildingDataRecordName (mode-driven barn name)");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GoatBarnNamePatches.Register: {ex}");
            }
        }

        private static void SetBuildingDataRecordNamePostfix(object __instance)
        {
            try
            {
                if (__instance is GoatBarn barn) GoatBarnNaming.Apply(barn);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GoatBarnNamePatches.Postfix: {ex.Message}");
            }
        }
    }
}
