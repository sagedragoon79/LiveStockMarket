using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  GarmentDisplayPatches — the villager window's equipment rows
//
//  UIVillagerEquipmentDisplay.UpdateEquipment (run once from Init, when the
//  window opens) lists the rows for the villager's occupation and matches each
//  against the inventory BY NAME. Two gaps, both filled after vanilla builds
//  the rows:
//    • A villager wearing a garment has no "ItemShoes" / "ItemHideCoat" /
//      "ItemLinenClothes" bundle, so vanilla shows that slot as missing even
//      though every game check counts the garment. Swap the placeholder for
//      the garment bundle the villager carries.
//    • Guards and soldiers get no clothing rows at all in vanilla (only armor,
//      shield, weapon, bow and arrows), so their shoes and clothes, vanilla or
//      wool, are invisible (1.2.0, user request). Append rows: shoes and
//      clothes for soldiers (they never seek a coat; armor stands in), shoes,
//      coat and clothes for guards.
//  Rows are UIVillagerEquipmentEntry instances from the display's own prefab,
//  added to its list so vanilla hides or reuses them on the next rebuild.
//  The first rebuild per villager per session logs one line saying what was swapped or added.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class GarmentDisplayPatches
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        private static readonly FieldInfo _villager = AccessTools.Field(typeof(UIVillagerEquipmentDisplay), "villager");
        private static readonly FieldInfo _entries  = AccessTools.Field(typeof(UIVillagerEquipmentDisplay), "equipmentEntries");
        private static readonly FieldInfo _prefab   = AccessTools.Field(typeof(UIVillagerEquipmentDisplay), "equipmentEntryPrefab");
        private static readonly FieldInfo _parent   = AccessTools.Field(typeof(UIVillagerEquipmentDisplay), "equipmentEntryParent");
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
                LiveStockMarketMod.Log.Msg($"{Tag} GarmentDisplayPatches: patched UIVillagerEquipmentDisplay.UpdateEquipment (garment icons, soldier clothing rows)" +
                    (_prefab == null || _parent == null ? " — entry prefab not found, soldier rows disabled" : ""));
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentDisplayPatches.Register: {ex}");
            }

            // The clicked villager's window: the Villager Storage panel's Clothing section
            // (UIVillagerStorageWindow) lists Villager.GetClothingInventory(), which keeps only
            // bundles matching the hardcoded Villager.clothingItems {hide coat, linen clothes,
            // shoes}. Garments are worn but never listed. Append them. GetClothingInventory has
            // no other caller; Villager.clothingItems itself stays vanilla because
            // GetMissingItems(clothingItems) would then report every villager missing three items.
            try
            {
                var clothing = AccessTools.Method(typeof(Villager), "GetClothingInventory", Type.EmptyTypes);
                if (clothing != null)
                {
                    harmony.Patch(clothing, postfix: new HarmonyMethod(typeof(GarmentDisplayPatches), nameof(ClothingInventoryPostfix)));
                    LiveStockMarketMod.Log.Msg($"{Tag} GarmentDisplayPatches: patched Villager.GetClothingInventory (garments in the Villager Storage panel)");
                }
                else LiveStockMarketMod.Log.Warning($"{Tag} GarmentDisplayPatches: Villager.GetClothingInventory not found — garments stay out of the Villager Storage panel.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentDisplayPatches: GetClothingInventory patch failed: {ex.Message}");
            }
        }

        private static void ClothingInventoryPostfix(Villager __instance, ref List<ItemBundle> __result)
        {
            try
            {
                if (__instance == null || __instance.permanentInventory == null) return;
                var all = __instance.permanentInventory.GetCopyOfAllItems();
                if (all == null) return;
                foreach (var b in all)
                {
                    if (b == null || b.numberOfItems == 0 || !IsGarment(b.name)) continue;
                    if (__result == null) __result = new List<ItemBundle>();
                    if (!__result.Contains(b)) __result.Add(b);
                }
            }
            catch { /* vanilla list stands */ }
        }

        private static bool IsGarment(string name)
        {
            foreach (var def in GarmentItems.All)
                if (def != null && def.Name == name) return true;
            return false;
        }

        private static void UpdateEquipmentPostfix(UIVillagerEquipmentDisplay __instance)
        {
            var notes = new List<string>();
            Villager villager = null;
            try
            {
                villager = _villager.GetValue(__instance) as Villager;
                if (villager == null || villager.permanentInventory == null) return;
                var entries = _entries.GetValue(__instance) as IList;
                if (entries == null) { notes.Add("no entry list"); return; }
                var all = villager.permanentInventory.GetCopyOfAllItems() ?? new List<ItemBundle>();

                // 1) A vanilla clothing row showing "missing": swap in the garment the villager wears.
                int clothingRows = 0;
                foreach (var o in entries)
                {
                    var entry = o as UIVillagerEquipmentEntry;
                    if (entry == null) continue;
                    var bundle = _bundle.GetValue(entry) as ItemBundle;
                    if (bundle == null) continue;                                       // unused entry (vanilla nulls it on disable)
                    var def = GarmentItems.ForVanilla(bundle.itemID);
                    if (def == null) continue;                                          // not a clothing slot
                    clothingRows++;
                    if (bundle.numberOfItems != 0) { notes.Add($"{bundle.name} worn"); continue; }
                    var garment = Find(all, def.Name);
                    if (garment == null) { notes.Add($"{bundle.name} missing, no {def.Name} either"); continue; }
                    entry.Init(garment, TargetOf(entry));
                    notes.Add($"{bundle.name} row → {def.Name}");
                }

                // 2) Guards and soldiers: vanilla lists no clothing, so add the rows.
                var occ = villager.GetOccupation();
                bool soldier = occ == VillagerOccupation.Occupation.Soldier || occ == VillagerOccupation.Occupation.TransitionToSoldier;
                bool guard = occ == VillagerOccupation.Occupation.Guard;
                if ((soldier || guard) && clothingRows == 0)
                {
                    var gm = UnitySingleton<GameManager>.Instance;
                    var wbm = gm != null ? gm.workBucketManager : null;
                    if (wbm != null)
                    {
                        var slots = new List<Item> { wbm.itemShoes };
                        if (guard) slots.Add(wbm.itemHideCoat);
                        slots.Add(wbm.itemLinenClothes);
                        foreach (var item in slots)
                        {
                            var def = GarmentItems.ForVanilla(item.itemID);
                            var shown = Find(all, item.name) ?? (def != null ? Find(all, def.Name) : null) ?? new ItemBundle(item, 0u, 100u);
                            var entry = FreeEntry(__instance, entries);
                            if (entry == null) { notes.Add("no entry to add a row"); break; }
                            entry.Init(shown, 1);
                            notes.Add($"added {shown.name}{(shown.numberOfItems == 0 ? " (missing)" : "")}");
                        }
                        __instance.gameObject.SetActive(true);
                    }
                }
            }
            catch (Exception ex)
            {
                notes.Add("error: " + ex.Message);
            }
            finally
            {
                // Once per villager per session: enough to confirm the rows, quiet afterwards.
                if (villager != null && notes.Count > 0 && _loggedRows.Add(villager.GetInstanceID()))
                    LiveStockMarketMod.Log.Msg($"{Tag} Equipment rows for '{villager.villagerName}' ({villager.GetOccupation()}): {string.Join("; ", notes.ToArray())}.");
            }
        }

        private static readonly HashSet<int> _loggedRows = new HashSet<int>();

        private static ItemBundle Find(List<ItemBundle> all, string name)
        {
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].numberOfItems > 0 && all[i].name == name) return all[i];
            return null;
        }

        private static int TargetOf(UIVillagerEquipmentEntry entry)
        {
            try { return _target != null ? Math.Max(1, (int)_target.GetValue(entry)) : 1; } catch { return 1; }
        }

        /// <summary>An entry vanilla left unused (its bundle is null), or a new one from the display's prefab.</summary>
        private static UIVillagerEquipmentEntry FreeEntry(UIVillagerEquipmentDisplay display, IList entries)
        {
            foreach (var o in entries)
            {
                var entry = o as UIVillagerEquipmentEntry;
                if (entry != null && _bundle.GetValue(entry) == null) return entry;
            }
            var prefab = _prefab != null ? _prefab.GetValue(display) as UIVillagerEquipmentEntry : null;
            var parent = _parent != null ? _parent.GetValue(display) as Transform : null;
            if (prefab == null || parent == null) return null;
            var created = UnityEngine.Object.Instantiate(prefab, parent);
            entries.Add(created);
            return created;
        }
    }
}
