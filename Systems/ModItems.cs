using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Patches;

namespace LiveStockMarket.Systems
{
    /// <summary>One mod item: registered by name with a synthetic ItemID, its stats
    /// cloned from a vanilla template entry (price optionally scaled).</summary>
    internal sealed class ModItemDef
    {
        public string Name;               // "ItemWinterBoots"
        public ItemID Id;                 // MAX + n
        public string TemplateName;       // vanilla entry to clone (stats, category, storage placement)
        public ItemID TemplateId;
        public string CarryPropName;      // vanilla entry whose carried mesh to borrow (null = template's)
        public string DisplayName;
        public string Description;
        public string IconResource;       // embedded resource name
        public string IconLooseFile;      // Mods/LiveStockMarket/<file> override
        public Func<float> PriceMultiplier = () => 1f;
        public int LifetimeMonths = -1;   // -1 = template's; 0 = never spoils

        public string DescriptionTag => "LSM_" + Name + "_Description";
        public string SingularTag    => "LSM_" + Name + "_DescriptionSingular";
        public string DetailedTag    => "LSM_" + Name + "_DetailedDescription";

        public Sprite Icon => ModIcons.Get(IconResource, IconLooseFile);
        public Item NewItem() => new Item(Name);
        public bool Matches(Item item) => item != null && item.itemID == Id;
    }

    /// <summary>
    /// Registry of every item this mod adds (wool + the three garments).
    ///
    /// Farthest Frontier identifies items by NAME plus an ItemID enum value; saves
    /// store the name, and every runtime lookup that matters is a dictionary. The
    /// item table is a ScriptableObject list (ItemSetupData). Registration:
    ///   1. Item.itemIDByName[name] = a synthetic, contiguous ID above ItemID.MAX.
    ///   2. Raise ItemStorage.maxItemID (per-storage bundle arrays are indexed by
    ///      (int)itemID and sized from ItemID.MAX).
    ///   3. Add an ItemEntry cloned from the template into ItemSetupData and its
    ///      lazy lookup dictionaries.
    ///   4. English strings via LocalizationPatches.
    /// GlobalAssets.ClearStaticReferencesToAllowUnloading drops the item table when
    /// a game unloads, so registration is keyed on the asset INSTANCE and re-run
    /// whenever a different instance shows up (WorkBucketManager.Awake prefix).
    /// The WorkBucketManager append (item lists + work buckets) lives in
    /// WoolItemPatches, because it must run inside that manager's Awake.
    /// </summary>
    internal static class ModItems
    {
        public const int IdHeadroomAboveMax = 16;

        public static readonly List<ModItemDef> All = new List<ModItemDef>();

        private static ItemSetupData _registeredInto;
        private static bool _storageLimitRaised;

        private static string Tag => LiveStockMarketMod.LogTag;

        /// <summary>True once every item is registered into the CURRENT item table.</summary>
        public static bool IsRegistered => _registeredInto != null && GlobalAssets.itemSetupData == _registeredInto;

        public static ModItemDef Get(ItemID id)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Id == id) return All[i];
            return null;
        }

        public static ModItemDef Get(string name)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Name == name) return All[i];
            return null;
        }

        /// <summary>The mod item that substitutes for a vanilla template item, if any.</summary>
        public static ModItemDef GetByTemplate(ItemID templateId)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].TemplateId == templateId) return All[i];
            return null;
        }

        public static bool IsModItem(Item item) => item != null && Get(item.itemID) != null;

        public static void Define(ModItemDef def)
        {
            if (Get(def.Name) != null) return;
            All.Add(def);
        }

        /// <summary>Idempotent per item table instance. Returns false (and logs) if the table isn't loadable yet.</summary>
        public static bool EnsureRegistered()
        {
            try
            {
                RaiseItemStorageLimit();

                var setup = GlobalAssets.itemSetupData;
                if (setup == null || setup.itemEntries == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} ModItems: ItemSetupData not available yet — will retry at WorkBucketManager.Awake.");
                    return false;
                }
                if (_registeredInto == setup) return true;

                int ok = 0;
                foreach (var def in All)
                {
                    RegisterLocalization(def);
                    if (!Item.itemIDByName.ContainsKey(def.Name))
                        Item.itemIDByName[def.Name] = def.Id;
                    if (RegisterEntry(setup, def)) ok++;
                }
                if (ok != All.Count)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} ModItems: {ok}/{All.Count} items registered — template entries missing?");
                    return false;
                }

                _registeredInto = setup;
                LiveStockMarketMod.Log.Msg($"{Tag} ModItems: {ok} items registered into the item table (ItemID.MAX = {(int)ItemID.MAX}).");
                return true;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} ModItems.EnsureRegistered: {ex}");
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
                    LiveStockMarketMod.Log.Warning($"{Tag} ModItems: ItemStorage.maxItemID not found — storing mod items may throw.");
                    _storageLimitRaised = true;
                    return;
                }
                int target = (int)ItemID.MAX + IdHeadroomAboveMax;
                int current = (int)field.GetValue(null);
                if (current < target)
                {
                    field.SetValue(null, target);
                    LiveStockMarketMod.Log.Msg($"{Tag} ModItems: ItemStorage.maxItemID {current} → {target}.");
                }
                _storageLimitRaised = true;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} ModItems.RaiseItemStorageLimit: {ex.Message}");
            }
        }

        private static void RegisterLocalization(ModItemDef def)
        {
            LocalizationPatches.Register(def.DescriptionTag, def.DisplayName);
            LocalizationPatches.Register(def.SingularTag, def.DisplayName);
            LocalizationPatches.Register(def.DetailedTag, def.Description);
        }

        internal static ItemSetupData.ItemEntry FindEntry(ItemSetupData setup, string name)
        {
            // Not GetItemEntry: that logs an error on a miss and freezes the lookup dictionary.
            var entries = setup.itemEntries;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i] != null && entries[i].name == name) return entries[i];
            return null;
        }

        private static bool RegisterEntry(ItemSetupData setup, ModItemDef def)
        {
            var entry = FindEntry(setup, def.Name);
            if (entry == null)
            {
                var template = FindEntry(setup, def.TemplateName);
                if (template == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} ModItems: template entry '{def.TemplateName}' for {def.Name} not found.");
                    return false;
                }
                var carry = def.CarryPropName != null ? FindEntry(setup, def.CarryPropName) : null;
                float priceMult = def.PriceMultiplier != null ? def.PriceMultiplier() : 1f;

                entry = new ItemSetupData.ItemEntry
                {
                    name = def.Name,
                    rowID = -1,
                    descriptionLocKey = def.DescriptionTag,
                    descriptionSingularLocKey = def.SingularTag,
                    detailedDescriptionLocKey = def.DetailedTag,
                    categoryTag = template.categoryTag,
                    goodsCategoryTag = template.goodsCategoryTag,
                    basePrice = Mathf.Max(1, Mathf.RoundToInt(template.basePrice * priceMult)),
                    weight = template.weight,
                    work = template.work,
                    quantity = template.quantity,
                    lifetimeInMonths = def.LifetimeMonths >= 0 ? def.LifetimeMonths : template.lifetimeInMonths,
                    wearMonthsAt100Percent = template.wearMonthsAt100Percent,
                    wearMonthsAt75Percent = template.wearMonthsAt75Percent,
                    wearMonthsAt50Percent = template.wearMonthsAt50Percent,
                    wearMonthsAt25Percent = template.wearMonthsAt25Percent,
                    carryable = template.carryable,
                    refundable = template.refundable,
                    hideInUI = false,
                    icon = null,   // no code path reads ItemEntry.icon; the UIAssetMap postfix serves sprites
                    carryableResourcePrefab = (carry != null && carry.carryableResourcePrefab != null)
                        ? carry.carryableResourcePrefab
                        : template.carryableResourcePrefab,
                    decreasingTrendThresholds = template.decreasingTrendThresholds ?? setup.decreasingTrendThresholdDefaults,
                    increasingTrendThresholds = template.increasingTrendThresholds ?? setup.increasingTrendThresholdDefaults,
                };
                setup.itemEntries.Add(entry);
                LiveStockMarketMod.Log.Msg(
                    $"{Tag} ModItems: {def.Name} entry added as ItemID {(int)def.Id} (template {def.TemplateName}: price {template.basePrice} → {entry.basePrice}, weight {entry.weight}, wear {entry.wearMonthsAt100Percent}/{entry.wearMonthsAt75Percent}/{entry.wearMonthsAt50Percent}/{entry.wearMonthsAt25Percent} months, category '{entry.categoryTag}').");
            }

            // The table builds its lookup dictionaries lazily on first GetItemEntry; if
            // that already happened, the list append alone is invisible.
            AddToPrivateDict(setup, "itemEntryByItemNameDict", def.Name, entry);
            AddToPrivateDict(setup, "itemEntryByItemIDDict", def.Id, entry);
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
                LiveStockMarketMod.Log.Warning($"{Tag} ModItems: could not update {fieldName}: {ex.Message}");
            }
        }
    }
}
