using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  WoolItemPatches  (now: every mod item — wool and the garments)
//
//  Wires the mod's items into the runtime structures vanilla builds from its
//  hardcoded item list:
//    • ItemStorage.InitItemBundleArrays (prefix): bundle arrays are indexed by
//      (int)itemID and sized from a static max — keep the max above our IDs.
//    • WorkBucketManager.Awake (prefix + postfix): register the entries first
//      (the item table is reloaded per game), then append each item to allItems
//      and the three ID dictionaries, and give it the per-item work buckets.
//      GetItemBasedWorkBucketByItem indexes unneededItemByID directly and
//      storage buildings dereference the canStore buckets, so these must exist
//      before any storage building starts. Garment recipes are injected here
//      too, as a safety net behind the Frontier-scene hook.
//    • ResourceManager.Awake (prefix): ItemInfo per item — settlement counts,
//      the Settlement Items window and the trading post UI all key on it.
//    • StorageBuilding.AddAllowableItems (postfix): each item is storable
//      wherever its template item is (prefab flag driven), ticked by default.
//    • UIAssetMap.Init (postfix): icons. Init also runs from OnEnable and
//      rebuilds the dictionaries, hence a postfix, not a one-shot.
//    • ItemDefinition.item (prefix): a recipe line resolves its item by CLASS
//      name and caches it, and a mod item has no class. Every building copies
//      each recipe line into its stocking-request tables at Awake (the copy
//      drops the cache), so without this the wool line resolved to null and
//      Building.Awake threw — no input stocking, no haul-out of the product,
//      idle workers. Found in v0.4.0, fixed in v0.4.1.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class WoolItemPatches
    {
        // Synthetic WorkBucketIdentifier block. The enum has ~1300 members; 200000+
        // can't collide and the dictionary comparer is int-based. One stride per item.
        private const int BucketIdBase   = 200000;
        private const int BucketIdStride = 16;

        // The per-item bucket maps vanilla fills for every item, in Awake order.
        private static readonly string[] BucketDictFields =
        {
            "hasItemWorkBucketByItem",
            "toCollectWorkBucketByItem",
            "hasItemIgnoreWCWorkBucketByItem",
            "canStoreWorkBucketByItem",
            "canStoreOverCapacityWorkBucketByItem",
            "readyForPickupWorkBucketByItem",
            "buildSiteNeedsSuppliesWorkBucketByItem",
            "buildSiteIgnoreWCNeedsSuppliesWorkBucketByItem",
            "cropfieldBuildSiteNeedsSuppliesWorkBucketByItem",
            "cropfieldBuildSiteIgnoreWCNeedsSuppliesWorkBucketByItem",
            "upkeepNeedsSuppliesWorkBucketByItem",
            "residentStorageHasItemWorkBucketByItem",
        };

        private static string Tag => LiveStockMarketMod.LogTag;

        public static void Register(HarmonyLib.Harmony harmony)
        {
            PatchOne(harmony, typeof(ItemStorage), "InitItemBundleArrays", Type.EmptyTypes,
                prefix: nameof(InitItemBundleArraysPrefix), postfix: null, what: "storage array size");
            PatchOne(harmony, typeof(WorkBucketManager), "Awake", Type.EmptyTypes,
                prefix: nameof(WorkBucketManagerAwakePrefix), postfix: nameof(WorkBucketManagerAwakePostfix), what: "item lists + work buckets");
            PatchOne(harmony, typeof(ResourceManager), "Awake", Type.EmptyTypes,
                prefix: nameof(ResourceManagerAwakePrefix), postfix: null, what: "settlement item info");
            PatchOne(harmony, typeof(StorageBuilding), "AddAllowableItems", Type.EmptyTypes,
                prefix: null, postfix: nameof(AddAllowableItemsPostfix), what: "storable wherever the template is");
            PatchOne(harmony, typeof(UIAssetMap), "Init", Type.EmptyTypes,
                prefix: null, postfix: nameof(UIAssetMapInitPostfix), what: "icons");
            PatchItemGetter(harmony);
        }

        private static void PatchOne(HarmonyLib.Harmony harmony, Type type, string method, Type[] args,
            string prefix, string postfix, string what)
        {
            try
            {
                var target = AccessTools.Method(type, method, args);
                if (target == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: {type.Name}.{method} not found — {what} disabled.");
                    return;
                }
                harmony.Patch(target,
                    prefix:  prefix  != null ? new HarmonyMethod(typeof(WoolItemPatches), prefix)  : null,
                    postfix: postfix != null ? new HarmonyMethod(typeof(WoolItemPatches), postfix) : null);
                LiveStockMarketMod.Log.Msg($"{Tag} WoolItemPatches: patched {type.Name}.{method} ({what})");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: patching {type.Name}.{method} failed: {ex.Message}");
            }
        }

        // ── ItemDefinition.item ───────────────────────────────────────────────
        private static void PatchItemGetter(HarmonyLib.Harmony harmony)
        {
            try
            {
                var getter = AccessTools.PropertyGetter(typeof(ItemDefinition), "item");
                if (getter == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: ItemDefinition.item getter not found — recipe lines naming mod items will not resolve.");
                    return;
                }
                harmony.Patch(getter, prefix: new HarmonyMethod(typeof(WoolItemPatches), nameof(ItemDefinitionItemPrefix)));
                LiveStockMarketMod.Log.Msg($"{Tag} WoolItemPatches: patched ItemDefinition.item (recipe lines resolve mod items by name)");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: patching ItemDefinition.item failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Vanilla: _item ??= MiscUtilities.CreateObjectByName&lt;Item&gt;(itemName), i.e.
        /// Type.GetType(itemName) — null for a mod item, and the getter logs
        /// "Unable to create item" to Unity's log (which is off in this install).
        /// Resolve ours first so the original finds the cache filled.
        /// </summary>
        private static void ItemDefinitionItemPrefix(ItemDefinition __instance, ref Item ____item)
        {
            if (____item != null || __instance == null) return;
            var def = ModItems.Get(__instance.itemName);
            if (def != null) ____item = def.NewItem();
        }

        // ── ItemStorage ───────────────────────────────────────────────────────
        private static void InitItemBundleArraysPrefix()
        {
            ModItems.RaiseItemStorageLimit();
        }

        // ── WorkBucketManager ─────────────────────────────────────────────────
        private static void WorkBucketManagerAwakePrefix()
        {
            ModItems.EnsureRegistered();
            GarmentRecipes.EnsureInjected(); AltRecipes.EnsureInjected();
        }

        private static void WorkBucketManagerAwakePostfix(WorkBucketManager __instance)
        {
            try
            {
                if (!ModItems.EnsureRegistered())
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: mod items not registered — skipping WorkBucketManager append.");
                    return;
                }
                for (int i = 0; i < ModItems.All.Count; i++)
                    AppendToWorkBucketManager(__instance, ModItems.All[i], i);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches.AwakePostfix: {ex}");
            }
        }

        private static void AppendToWorkBucketManager(WorkBucketManager wbm, ModItemDef def, int index)
        {
            var all = wbm.allItems;
            if (all == null)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: WorkBucketManager.allItems is null.");
                return;
            }
            if (all.Exists(i => i != null && i.itemID == def.Id)) return; // already appended

            var byId     = GetDict<ItemID, Item>(wbm, "itemByItemID");
            var neededBy = GetDict<ItemID, Item>(wbm, "neededItemByID");
            var unneeded = GetDict<ItemID, Item>(wbm, "unneededItemByID");
            if (byId == null || neededBy == null || unneeded == null)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: item ID dictionaries not found — {def.Name} NOT appended.");
                return;
            }

            var item = def.NewItem();            // needed = false, like vanilla's base instances
            all.Add(item);
            byId[item.itemID]     = item;
            neededBy[item.itemID] = new Item(item, true);
            unneeded[item.itemID] = new Item(item, false);

            // Per-item work buckets under synthetic identifiers. Keyed by the
            // unneeded instance; Item equality is (itemID, needed).
            var key = unneeded[item.itemID];
            int registered = 0;
            for (int k = 0; k < BucketDictFields.Length; k++)
            {
                var id = (WorkBucketIdentifier)(BucketIdBase + index * BucketIdStride + k);
                var dict = GetDict<Item, WorkBucket>(wbm, BucketDictFields[k]);
                if (dict == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: bucket map '{BucketDictFields[k]}' not found.");
                    continue;
                }
                wbm.RegisterAsOwnerOfWorkBucket(wbm, id);
                var bucket = wbm.GetWorkBucket(wbm, id);
                if (bucket == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: could not create work bucket {(int)id} for '{BucketDictFields[k]}'.");
                    continue;
                }
                if (!dict.ContainsKey(key)) dict.Add(key, bucket);
                registered++;
            }

            LiveStockMarketMod.Log.Msg(
                $"{Tag} WoolItemPatches: {def.Name} appended to WorkBucketManager (allItems={all.Count}, buckets {registered}/{BucketDictFields.Length}).");
        }

        private static Dictionary<TKey, TValue> GetDict<TKey, TValue>(object owner, string field)
        {
            var f = AccessTools.Field(typeof(WorkBucketManager), field);
            return f?.GetValue(owner) as Dictionary<TKey, TValue>;
        }

        // ── ResourceManager ───────────────────────────────────────────────────
        /// <summary>
        /// ResourceManager.Awake builds itemNameToItemInfoDict from a hardcoded list
        /// of vanilla items, then derives its degrade / no-degrade ItemInfo arrays
        /// (the ones UpdateItemCounts walks) from that dictionary. The dictionary is
        /// a field initializer, so adding our items BEFORE Awake runs puts them in
        /// the derived arrays too.
        /// </summary>
        private static void ResourceManagerAwakePrefix(ResourceManager __instance)
        {
            try
            {
                if (!ModItems.EnsureRegistered()) return;
                var field = AccessTools.Field(typeof(ResourceManager), "itemNameToItemInfoDict");
                var dict = field?.GetValue(__instance) as Dictionary<string, ResourceManager.ItemInfo>;
                if (dict == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: ResourceManager.itemNameToItemInfoDict not found — mod items won't be counted.");
                    return;
                }
                int added = 0;
                foreach (var def in ModItems.All)
                {
                    if (dict.ContainsKey(def.Name)) continue;
                    dict.Add(def.Name, new ResourceManager.ItemInfo(def.NewItem()));
                    added++;
                }
                if (added > 0)
                    LiveStockMarketMod.Log.Msg($"{Tag} WoolItemPatches: ItemInfo registered for {added} mod items (settlement counts, Settlement Items window, trading post).");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches.ResourceManagerAwake: {ex.Message}");
            }
        }

        // ── StorageBuilding ───────────────────────────────────────────────────
        private static void AddAllowableItemsPostfix(StorageBuilding __instance)
        {
            try
            {
                if (!ModItems.IsRegistered) return;
                var list = AllowableList(__instance);
                if (list == null) return;
                var user = __instance.userDefinedAllowableItems;

                foreach (var def in ModItems.All)
                {
                    if (!list.Exists(i => i != null && i.itemID == def.TemplateId)) continue;   // follow the template
                    if (list.Exists(i => i != null && i.itemID == def.Id)) continue;
                    var item = def.NewItem();
                    list.Add(item);

                    // Vanilla seeds the player's filter as a copy of allowableItems at the END
                    // of AddAllowableItems — before this postfix — so a fresh building would
                    // show the item unticked. Tick it here. A loaded building replaces this
                    // list with the saved one in Load, where WoolStoragePatches decides.
                    if (user != null && !user.Exists(i => i != null && i.itemID == def.Id))
                        user.Add(item);
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches.AddAllowableItems: {ex.Message}");
            }
        }

        /// <summary>The live allowable list behind allowableItemsRO (protected property).</summary>
        internal static List<Item> AllowableList(StorageBuilding building)
        {
            var prop = AccessTools.Property(typeof(StorageBuilding), "allowableItems");
            return prop?.GetValue(building, null) as List<Item>;
        }

        internal static bool AllowsItem(StorageBuilding building, ItemID id)
        {
            var ro = building != null ? building.allowableItemsRO : null;
            if (ro == null) return false;
            for (int i = 0; i < ro.Count; i++)
                if (ro[i] != null && ro[i].itemID == id) return true;
            return false;
        }

        internal static bool AllowsWool(StorageBuilding building) => AllowsItem(building, WoolItem.ItemId);

        // ── UIAssetMap ────────────────────────────────────────────────────────
        private static void UIAssetMapInitPostfix(UIAssetMap __instance)
        {
            try
            {
                var sprites = __instance.itemNameToSpriteDict;
                var graphics = __instance.itemNameToGraphicDict;
                foreach (var def in ModItems.All)
                {
                    var sprite = def.Icon;
                    if (sprite == null) continue;
                    if (sprites != null && !sprites.ContainsKey(def.Name)) sprites.Add(def.Name, sprite);
                    if (graphics != null && !graphics.ContainsKey(def.Name)) graphics.Add(def.Name, sprite);
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches.UIAssetMapInit: {ex.Message}");
            }
        }
    }
}
