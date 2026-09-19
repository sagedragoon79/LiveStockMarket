using System;
using HarmonyLib;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  SheepVisualPatches (step 4)
//
//  • Herd.AddAnimalToHerd (postfix): every animal that joins a herd — spawned,
//    born, bought, transferred — gets the look its home barn's mode calls for.
//    Loaded herds add their animals before the home building is set; those are
//    covered by SheepShearing.ApplyMode, which runs from the SetLoadedHerd hook.
//  • VillagerState.StateToString (prefix): a herder at a Sheep barn reads
//    "Shearing Sheep" instead of "Milking Goats". Vanilla caches the string per
//    villager; returning early here bypasses that cache for this one case.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class SheepVisualPatches
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                var add = AccessTools.Method(typeof(Herd), "AddAnimalToHerd", new[] { typeof(LivestockAnimal), typeof(bool) });
                if (add != null)
                {
                    harmony.Patch(add, postfix: new HarmonyMethod(typeof(SheepVisualPatches), nameof(AddAnimalToHerdPostfix)));
                    LiveStockMarketMod.Log.Msg($"{Tag} SheepVisualPatches: patched Herd.AddAnimalToHerd (sheep look on new animals)");
                }
                else LiveStockMarketMod.Log.Warning($"{Tag} SheepVisualPatches: Herd.AddAnimalToHerd not found — new animals keep the goat look until the barn is toggled.");

                var state = AccessTools.Method(typeof(VillagerState), "StateToString", new[] { typeof(VillagerState.State), typeof(Villager) });
                if (state != null)
                {
                    harmony.Patch(state, prefix: new HarmonyMethod(typeof(SheepVisualPatches), nameof(StateToStringPrefix)));
                    LiveStockMarketMod.Log.Msg($"{Tag} SheepVisualPatches: patched VillagerState.StateToString (\"Shearing Sheep\")");
                }
                else LiveStockMarketMod.Log.Warning($"{Tag} SheepVisualPatches: VillagerState.StateToString not found — herders keep reading \"Milking Goats\".");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepVisualPatches.Register: {ex}");
            }
        }

        private static void AddAnimalToHerdPostfix(Herd __instance, LivestockAnimal animalToAdd)
        {
            try
            {
                var barn = __instance != null ? __instance.homeBuilding as GoatBarn : null;
                if (barn == null || animalToAdd == null) return;
                SheepVisuals.Apply(animalToAdd, SheepVisuals.WantSheep(barn));
                PigVisuals.Apply(animalToAdd, PigVisuals.WantPig(barn));
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepVisualPatches.AddAnimalToHerd: {ex.Message}");
            }
        }

        private static bool StateToStringPrefix(VillagerState.State state, Villager villager, ref string __result)
        {
            try
            {
                if (state != VillagerState.State.MilkingGoats || villager == null) return true;
                var barn = villager.placeOfWork as GoatBarn;
                if (barn == null || !GoatBarnModeStore.IsSheep(barn)) return true;
                var gm = UnitySingleton<GameManager>.Instance;
                var lm = gm != null ? gm.localizationManager : null;
                __result = lm != null ? lm.Localize("LSM_VillagerState_ShearingSheep") : "Shearing Sheep";
                return false;
            }
            catch
            {
                return true;
            }
        }
    }
}
