using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  WoolItemPatches
//
//  Wires ItemWool into the runtime structures vanilla builds from its hardcoded
//  item list:
//    • ItemStorage.InitItemBundleArrays (prefix): bundle arrays are indexed by
//      (int)itemID and sized from a static max — keep the max above our ID.
//    • WorkBucketManager.Awake (prefix + postfix): register the entry first
//      (in case mod init ran before the item table was loadable), then append
//      wool to allItems and the three ID dictionaries, and give it the per-item
//      work buckets. GetItemBasedWorkBucketByItem indexes unneededItemByID
//      directly and storage buildings dereference the canStore buckets, so
//      these must exist before any storage building starts.
//    • StorageBuilding.AddAllowableItems (postfix): wool is storable wherever
//      hides are (prefab flag driven, no building types hardcoded).
//    • UIAssetMap.Init (postfix): icon + hi-res image. Init also runs from
//      OnEnable and rebuilds the dictionaries, hence a postfix, not a one-shot.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class WoolItemPatches
    {
        // Synthetic WorkBucketIdentifier block for wool. The enum has ~1300 members;
        // 200000+ can't collide and the dictionary comparer is int-based.
        private const int BucketIdBase = 200000;

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
                prefix: null, postfix: nameof(AddAllowableItemsPostfix), what: "storable wherever hides are");
            PatchOne(harmony, typeof(UIAssetMap), "Init", Type.EmptyTypes,
                prefix: null, postfix: nameof(UIAssetMapInitPostfix), what: "icon");
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

        // ── ItemStorage ───────────────────────────────────────────────────────
        private static void InitItemBundleArraysPrefix()
        {
            WoolItem.RaiseItemStorageLimit();
        }

        // ── WorkBucketManager ─────────────────────────────────────────────────
        private static void WorkBucketManagerAwakePrefix()
        {
            WoolItem.EnsureRegistered();
        }

        private static void WorkBucketManagerAwakePostfix(WorkBucketManager __instance)
        {
            try
            {
                if (!WoolItem.EnsureRegistered())
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: wool not registered — skipping WorkBucketManager append.");
                    return;
                }
                AppendToWorkBucketManager(__instance);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches.AwakePostfix: {ex}");
            }
        }

        private static void AppendToWorkBucketManager(WorkBucketManager wbm)
        {
            var all = wbm.allItems;
            if (all == null)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: WorkBucketManager.allItems is null.");
                return;
            }
            if (all.Exists(i => i != null && i.itemID == WoolItem.ItemId)) return; // already appended

            var byId     = GetDict<ItemID, Item>(wbm, "itemByItemID");
            var neededBy = GetDict<ItemID, Item>(wbm, "neededItemByID");
            var unneeded = GetDict<ItemID, Item>(wbm, "unneededItemByID");
            if (byId == null || neededBy == null || unneeded == null)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: item ID dictionaries not found — wool NOT appended.");
                return;
            }

            var wool = WoolItem.NewItem();           // needed = false, like vanilla's base instances
            all.Add(wool);
            byId[wool.itemID]     = wool;
            neededBy[wool.itemID] = new Item(wool, true);
            unneeded[wool.itemID] = new Item(wool, false);

            // Per-item work buckets under synthetic identifiers. Keyed by the
            // unneeded instance; Item equality is (itemID, needed).
            var key = unneeded[wool.itemID];
            int registered = 0;
            for (int k = 0; k < BucketDictFields.Length; k++)
            {
                var id = (WorkBucketIdentifier)(BucketIdBase + k);
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
                $"{Tag} WoolItemPatches: {WoolItem.ItemName} appended to WorkBucketManager (allItems={all.Count}, buckets {registered}/{BucketDictFields.Length}).");
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
        /// a field initializer, so adding wool BEFORE Awake runs puts it in the
        /// derived arrays too. Without this: no settlement count, no row in the
        /// Settlement Items window, and null ItemInfo lookups in the trading post UI.
        /// </summary>
        private static void ResourceManagerAwakePrefix(ResourceManager __instance)
        {
            try
            {
                if (!WoolItem.EnsureRegistered()) return;
                var field = AccessTools.Field(typeof(ResourceManager), "itemNameToItemInfoDict");
                var dict = field?.GetValue(__instance) as Dictionary<string, ResourceManager.ItemInfo>;
                if (dict == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches: ResourceManager.itemNameToItemInfoDict not found — wool won't be counted.");
                    return;
                }
                if (dict.ContainsKey(WoolItem.ItemName)) return;
                dict.Add(WoolItem.ItemName, new ResourceManager.ItemInfo(WoolItem.NewItem()));
                LiveStockMarketMod.Log.Msg($"{Tag} WoolItemPatches: ItemInfo registered for {WoolItem.ItemName} (settlement counts, Settlement Items window, trading post).");
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
                if (!WoolItem.IsRegistered) return;
                var list = AllowableList(__instance);
                if (list == null) return;
                if (!list.Exists(i => i != null && i.itemID == ItemID.Hide)) return;   // wool follows hides
                if (list.Exists(i => i != null && i.itemID == WoolItem.ItemId)) return;
                var wool = WoolItem.NewItem();
                list.Add(wool);

                // Vanilla seeds the player's filter as a copy of allowableItems at the END
                // of AddAllowableItems — before this postfix — so a fresh building would
                // show wool unticked. Tick it here. A loaded building replaces this list
                // with the saved one in Load, where WoolStoragePatches decides.
                var user = __instance.userDefinedAllowableItems;
                if (user != null && !user.Exists(i => i != null && i.itemID == WoolItem.ItemId))
                    user.Add(wool);
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

        internal static bool AllowsWool(StorageBuilding building)
        {
            var ro = building != null ? building.allowableItemsRO : null;
            if (ro == null) return false;
            for (int i = 0; i < ro.Count; i++)
                if (ro[i] != null && ro[i].itemID == WoolItem.ItemId) return true;
            return false;
        }

        // ── UIAssetMap ────────────────────────────────────────────────────────
        private static void UIAssetMapInitPostfix(UIAssetMap __instance)
        {
            try
            {
                var sprite = WoolIcon.Sprite;
                if (sprite == null) return;
                var sprites = __instance.itemNameToSpriteDict;
                if (sprites != null && !sprites.ContainsKey(WoolItem.ItemName)) sprites.Add(WoolItem.ItemName, sprite);
                var graphics = __instance.itemNameToGraphicDict;
                if (graphics != null && !graphics.ContainsKey(WoolItem.ItemName)) graphics.Add(WoolItem.ItemName, sprite);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItemPatches.UIAssetMapInit: {ex.Message}");
            }
        }
    }
}
