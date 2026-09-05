using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Patches;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// ItemWool — the mod's first genuinely new item (step 2).
    ///
    /// Farthest Frontier identifies items by NAME string plus an ItemID enum
    /// value; saves store the name, and every runtime lookup that matters is a
    /// dictionary. The item table is a ScriptableObject list (ItemSetupData).
    /// What this registry does, idempotently:
    ///   1. Item.itemIDByName["ItemWool"] = MAX + 1 (a synthetic, contiguous ID).
    ///   2. Raises ItemStorage.maxItemID: every storage keeps per-item bundle
    ///      arrays indexed by (int)itemID and sized from ItemID.MAX, so the
    ///      synthetic ID would overflow them otherwise.
    ///   3. Adds an ItemEntry cloned from ItemHide (price, weight, category tag,
    ///      report bucket) with our loc keys, no spoilage, and Flax's carried
    ///      mesh — and drops it into the table's lazy lookup dictionaries if
    ///      they already exist.
    ///   4. Registers the English strings with LocalizationPatches.
    /// The WorkBucketManager append (item lists + work buckets) lives in
    /// WoolItemPatches, because it must run inside that manager's Awake.
    ///
    /// Ordering: EnsureRegistered runs at mod init (before any scene) and again
    /// as a prefix on WorkBucketManager.Awake and ItemStorage.InitItemBundleArrays,
    /// so whichever comes first finds wool in place.
    /// </summary>
    internal static class WoolItem
    {
        public const string ItemName          = "ItemWool";
        public const string TemplateItemName  = "ItemHide";   // numbers, category, storage placement
        public const string CarryPropItemName = "ItemFlax";   // carried mesh: a cream bundle
        public const int    IdHeadroomAboveMax = 16;          // room for MAX+1 .. MAX+16 in ItemStorage arrays

        public static readonly ItemID ItemId = (ItemID)((int)ItemID.MAX + 1);

        public const string DescriptionTag = "LSM_ItemWool_Description";
        public const string SingularTag    = "LSM_ItemWool_DescriptionSingular";
        public const string DetailedTag    = "LSM_ItemWool_DetailedDescription";

        public const string DisplayName = "Wool";
        public const string DetailedDescription = "Raw wool shorn from sheep. Spun and woven into cloth.";

        private static bool _registered;
        private static bool _storageLimitRaised;

        private static string Tag => LiveStockMarketMod.LogTag;

        public static bool IsRegistered => _registered;

        public static bool IsWool(Item item) => item != null && item.itemID == ItemId;

        /// <summary>A fresh Item instance for wool (needed = false). Requires registration.</summary>
        public static Item NewItem() => new Item(ItemName);

        /// <summary>Idempotent. Returns false (and logs) if the item table isn't loadable yet.</summary>
        public static bool EnsureRegistered()
        {
            if (_registered) return true;
            try
            {
                RaiseItemStorageLimit();
                RegisterLocalization();

                if (!Item.itemIDByName.ContainsKey(ItemName))
                    Item.itemIDByName[ItemName] = ItemId;

                var setup = GlobalAssets.itemSetupData;
                if (setup == null || setup.itemEntries == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolItem: ItemSetupData not available yet — will retry at WorkBucketManager.Awake.");
                    return false;
                }

                if (!RegisterEntry(setup)) return false;

                _registered = true;
                LiveStockMarketMod.Log.Msg(
                    $"{Tag} WoolItem: registered {ItemName} as ItemID {(int)ItemId} (ItemID.MAX = {(int)ItemID.MAX}; template {TemplateItemName}, carry prop {CarryPropItemName}).");
                return true;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItem.EnsureRegistered: {ex}");
                return false;
            }
        }

        /// <summary>
        /// ItemStorage sizes its bundle arrays from a private static maxItemID that is
        /// computed once from the enum. Set it above our synthetic IDs before any
        /// storage exists (also enforced by a prefix on InitItemBundleArrays).
        /// </summary>
        public static void RaiseItemStorageLimit()
        {
            if (_storageLimitRaised) return;
            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(ItemStorage).TypeHandle);
                var field = AccessTools.Field(typeof(ItemStorage), "maxItemID");
                if (field == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolItem: ItemStorage.maxItemID not found — storing wool may throw.");
                    _storageLimitRaised = true;
                    return;
                }
                int target = (int)ItemID.MAX + IdHeadroomAboveMax;
                int current = (int)field.GetValue(null);
                if (current < target)
                {
                    field.SetValue(null, target);
                    LiveStockMarketMod.Log.Msg($"{Tag} WoolItem: ItemStorage.maxItemID {current} → {target}.");
                }
                _storageLimitRaised = true;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItem.RaiseItemStorageLimit: {ex.Message}");
            }
        }

        private static void RegisterLocalization()
        {
            LocalizationPatches.Register(DescriptionTag, DisplayName);
            LocalizationPatches.Register(SingularTag, DisplayName);
            LocalizationPatches.Register(DetailedTag, DetailedDescription);
        }

        private static ItemSetupData.ItemEntry FindEntry(ItemSetupData setup, string name)
        {
            // Not GetItemEntry: that logs an error on a miss and freezes the lookup dictionary.
            var entries = setup.itemEntries;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].name == name) return entries[i];
            return null;
        }

        private static bool RegisterEntry(ItemSetupData setup)
        {
            var entry = FindEntry(setup, ItemName);
            if (entry == null)
            {
                var template = FindEntry(setup, TemplateItemName);
                if (template == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolItem: template entry '{TemplateItemName}' not found in ItemSetupData.");
                    return false;
                }
                var carry = FindEntry(setup, CarryPropItemName);

                entry = new ItemSetupData.ItemEntry
                {
                    name = ItemName,
                    rowID = -1,
                    descriptionLocKey = DescriptionTag,
                    descriptionSingularLocKey = SingularTag,
                    detailedDescriptionLocKey = DetailedTag,
                    categoryTag = template.categoryTag,
                    goodsCategoryTag = template.goodsCategoryTag,
                    basePrice = template.basePrice,
                    weight = template.weight,
                    work = template.work,
                    quantity = template.quantity,
                    lifetimeInMonths = 0,          // wool doesn't spoil
                    wearMonthsAt100Percent = 0,
                    wearMonthsAt75Percent = 0,
                    wearMonthsAt50Percent = 0,
                    wearMonthsAt25Percent = 0,
                    carryable = true,
                    refundable = true,
                    hideInUI = false,
                    icon = null,   // no code path reads ItemEntry.icon; the UIAssetMap postfix serves the sprite lazily
                    carryableResourcePrefab = (carry != null && carry.carryableResourcePrefab != null)
                        ? carry.carryableResourcePrefab
                        : template.carryableResourcePrefab,
                    decreasingTrendThresholds = template.decreasingTrendThresholds ?? setup.decreasingTrendThresholdDefaults,
                    increasingTrendThresholds = template.increasingTrendThresholds ?? setup.increasingTrendThresholdDefaults,
                };
                setup.itemEntries.Add(entry);
                LiveStockMarketMod.Log.Msg(
                    $"{Tag} WoolItem: entry added (price {entry.basePrice}, weight {entry.weight}, category '{entry.categoryTag}', report '{entry.goodsCategoryTag}', carry prop {(entry.carryableResourcePrefab != null ? entry.carryableResourcePrefab.name : "none")}).");
            }

            // The table builds its lookup dictionaries lazily on first GetItemEntry; if
            // that already happened, the list append alone is invisible.
            AddToPrivateDict(setup, "itemEntryByItemNameDict", ItemName, entry);
            AddToPrivateDict(setup, "itemEntryByItemIDDict", ItemId, entry);
            return true;
        }

        private static void AddToPrivateDict<TKey>(ItemSetupData setup, string fieldName, TKey key, ItemSetupData.ItemEntry value)
        {
            try
            {
                var field = AccessTools.Field(typeof(ItemSetupData), fieldName);
                var dict = field?.GetValue(setup) as Dictionary<TKey, ItemSetupData.ItemEntry>;
                if (dict != null && !dict.ContainsKey(key)) dict[key] = value;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolItem: could not update {fieldName}: {ex.Message}");
            }
        }
    }
}
