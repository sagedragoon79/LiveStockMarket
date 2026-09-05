using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  GarmentDisplayPatches (step 4, cosmetic gap left by step 5)
//
//  The villager window's equipment rows (UIVillagerEquipmentDisplay.UpdateEquipment)
//  list the three vanilla clothing items and match them against the villager's
//  inventory BY NAME. A villager wearing Winter Boots has no "ItemShoes" bundle,
//  so vanilla shows the shoes icon with a missing count — even though every
//  warmth query treats the boots as shoes. After vanilla builds the rows, swap a
//  missing vanilla row for the garment bundle the villager actually carries.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class GarmentDisplayPatches
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        private static readonly FieldInfo _villager = AccessTools.Field(typeof(UIVillagerEquipmentDisplay), "villager");
        private static readonly FieldInfo _entries  = AccessTools.Field(typeof(UIVillagerEquipmentDisplay), "equipmentEntries");
        private static readonly FieldInfo _bundle   = AccessTools.Field(typeof(UIVillagerEquipmentEntry), "itemBundle");
        private static readonly FieldInfo _target   = AccessTools.Field(typeof(UIVillagerEquipmentEntry), "targetCount");

        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                var update = AccessTools.Method(typeof(UIVillagerEquipmentDisplay), "UpdateEquipment", Type.EmptyTypes);
                if (update == null || _villager == null || _entries == null || _bundle == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} GarmentDisplayPatches: UIVillagerEquipmentDisplay layout not as expected — worn garments show the vanilla icon.");
                    return;
                }
                harmony.Patch(update, postfix: new HarmonyMethod(typeof(GarmentDisplayPatches), nameof(UpdateEquipmentPostfix)));
                LiveStockMarketMod.Log.Msg($"{Tag} GarmentDisplayPatches: patched UIVillagerEquipmentDisplay.UpdateEquipment (garment icons)");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentDisplayPatches.Register: {ex}");
            }
        }

        private static void UpdateEquipmentPostfix(UIVillagerEquipmentDisplay __instance)
        {
            try
            {
                var villager = _villager.GetValue(__instance) as Villager;
                if (villager == null || villager.permanentInventory == null) return;
                var entries = _entries.GetValue(__instance) as IList;
                if (entries == null) return;

                List<ItemBundle> all = null;
                foreach (var o in entries)
                {
                    var entry = o as UIVillagerEquipmentEntry;
                    if (entry == null || !entry.gameObject.activeSelf) continue;
                    var bundle = _bundle.GetValue(entry) as ItemBundle;
                    if (bundle == null || bundle.numberOfItems != 0) continue;        // present, or not a placeholder
                    var def = GarmentItems.ForVanilla(bundle.itemID);
                    if (def == null) continue;                                          // not a clothing slot
                    if (all == null)
                    {
                        all = villager.permanentInventory.GetCopyOfAllItems();
                        if (all == null) return;
                    }
                    ItemBundle garment = null;
                    for (int i = 0; i < all.Count; i++)
                        if (all[i] != null && all[i].numberOfItems > 0 && all[i].name == def.Name) { garment = all[i]; break; }
                    if (garment == null) continue;
                    int target = _target != null ? (int)_target.GetValue(entry) : 1;
                    entry.Init(garment, target);
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentDisplayPatches.UpdateEquipment: {ex.Message}");
            }
        }
    }
}
