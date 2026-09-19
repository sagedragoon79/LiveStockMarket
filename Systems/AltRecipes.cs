using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Patches;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// Alternate recipes for vanilla products: a producer's vanilla recipe cloned with
    /// one input swapped for another. First use: tallow candles at the Candle Shop —
    /// the candle recipe with wax replaced by tallow at a multiplied count, same candles
    /// out. Same injection and persistence shape as GarmentRecipes (fixed guid, cloned
    /// per game from the building table, refreshed on pref change).
    /// </summary>
    internal static class AltRecipes
    {
        private sealed class Swap
        {
            public string From;                  // vanilla source item class name
            public string To;                    // replacement item class name (vanilla or mod)
            public Func<float> CountMultiplier;  // replacement count = vanilla count × this
        }

        private sealed class Spec
        {
            public string BuildingIdentifier;
            public string TemplateProduct;       // the vanilla recipe to clone, found by its produced item
            public string DefinitionName;
            public string Guid;
            public string Tooltip;
            public Swap[] Swaps;
            public Func<bool> Enabled;
        }

        private sealed class Live
        {
            public Spec Spec;
            public ManufactureDefinition Definition;
            public ManufactureDefinition Template;
        }

        internal static float TallowPerWax => Mathf.Clamp(LiveStockMarketMod.cfgTallowCandleTallowMultiplier?.Value ?? 2f, 0.25f, 10f);

        private static readonly Spec[] Specs =
        {
            new Spec
            {
                BuildingIdentifier = "CandleShop", TemplateProduct = "ItemCandle",
                DefinitionName = "LSM_ManufactureTallowCandles", Guid = "5a1c0f0e-4c6d-4e00-9e01-000000000201",
                Tooltip = "Tallow candles: candles dipped in rendered tallow instead of beeswax. Same candles, more of a cheaper fat.",
                Swaps = new[] { new Swap { From = "ItemWax", To = "ItemTallow", CountMultiplier = () => TallowPerWax } },
                Enabled = () => LiveStockMarketMod.cfgTallowCandleEnabled?.Value ?? true,
            },
        };

        private static readonly List<Live> _live = new List<Live>();
        private static BuildingSetupData _injectedInto;

        private static readonly FieldInfo _numProducedField = AccessTools.Field(typeof(ProducedItemDefinition), "_numItemsProduced");
        private static readonly FieldInfo _workUnitsField   = AccessTools.Field(typeof(ManufactureDefinition), "_workUnitsForProduction");
        private static readonly FieldInfo _guidField        = AccessTools.Field(typeof(CEGuidScriptableObject), "guidAsString");

        private static string Tag => LiveStockMarketMod.LogTag;

        /// <summary>Idempotent per building-table instance; safe to call often.</summary>
        public static void EnsureInjected()
        {
            try
            {
                var setup = GlobalAssets.buildingSetupData;
                if (setup == null || setup.buildingData == null) return;
                if (_injectedInto == setup) return;

                _live.Clear();
                int added = 0;
                foreach (var spec in Specs)
                {
                    if (spec.Enabled != null && !spec.Enabled()) continue;
                    LocalizationPatches.Register(TooltipTag(spec), spec.Tooltip);
                    BuildingData data = null;
                    foreach (var d in setup.buildingData) if (d != null && d.identifier == spec.BuildingIdentifier) { data = d; break; }
                    if (data == null)
                    {
                        LiveStockMarketMod.Log.Warning($"{Tag} AltRecipes: building '{spec.BuildingIdentifier}' not found — no {spec.DefinitionName}.");
                        continue;
                    }
                    if (data.manufactureDefinitions == null) data.manufactureDefinitions = new List<ManufactureDefinition>();
                    var template = data.manufactureDefinitions.Find(d => d != null && d.name != spec.DefinitionName && d.producedItems != null
                        && d.producedItems.Exists(p => p != null && p.itemName == spec.TemplateProduct));
                    if (template == null)
                    {
                        LiveStockMarketMod.Log.Warning($"{Tag} AltRecipes: '{spec.BuildingIdentifier}' has no recipe producing {spec.TemplateProduct} — no {spec.DefinitionName}.");
                        continue;
                    }
                    var existing = data.manufactureDefinitions.Find(d => d != null && d.name == spec.DefinitionName);
                    if (existing == null)
                    {
                        existing = Build(spec, template);
                        if (existing == null) continue;
                        data.manufactureDefinitions.Add(existing);
                        added++;
                    }
                    _live.Add(new Live { Spec = spec, Definition = existing, Template = template });
                }
                _injectedInto = setup;
                ApplyPrefs(notifyBuildings: false);
                LiveStockMarketMod.Log.Msg($"{Tag} AltRecipes: {added} recipe(s) added, {_live.Count} live.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} AltRecipes.EnsureInjected: {ex}");
            }
        }

        private static string TooltipTag(Spec spec) => "LSM_" + spec.DefinitionName + "_Tooltip";

        private static ManufactureDefinition Build(Spec spec, ManufactureDefinition template)
        {
            try
            {
                var def = ScriptableObject.CreateInstance<ManufactureDefinition>();
                def.name = spec.DefinitionName;
                def.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _guidField?.SetValue(def, spec.Guid);

                def.sourceItems = new List<SourceItemDefinition>();
                if (template.sourceItems != null)
                    foreach (var src in template.sourceItems)
                    {
                        if (src == null) continue;
                        Swap swap = null;
                        foreach (var s in spec.Swaps) if (s.From == src.itemName) { swap = s; break; }
                        if (swap == null) { def.sourceItems.Add(new SourceItemDefinition(src)); continue; }
                        int count = Mathf.Max(1, Mathf.RoundToInt(src.numSourceItemsNeeded * swap.CountMultiplier()));
                        var replaced = new SourceItemDefinition(swap.To, Mathf.Max(count, src.capacity), count, src.criticalSupplySetting)
                        {
                            allowLowPriorityRequestToCapacity = src.allowLowPriorityRequestToCapacity,
                            stockNeededMultiplier = src.stockNeededMultiplier,
                            minAvailableCountAllowedPerStorage = src.minAvailableCountAllowedPerStorage,
                        };
                        var modItem = ModItems.Get(swap.To);
                        if (modItem != null) AccessTools.Field(typeof(ItemDefinition), "_item")?.SetValue(replaced, modItem.NewItem());
                        def.sourceItems.Add(replaced);
                    }

                def.producedItems = new List<ProducedItemDefinition>();
                if (template.producedItems != null)
                    foreach (var p in template.producedItems)
                    {
                        if (p == null) continue;
                        var copy = new ProducedItemDefinition(p.itemName, p.capacity);
                        _numProducedField?.SetValue(copy, p.numItemsProducedBase);
                        copy.ignoreCapacityAlerts = p.ignoreCapacityAlerts;
                        copy.blockTransferringToStorage = p.blockTransferringToStorage;
                        def.producedItems.Add(copy);
                    }

                int baseWork = _workUnitsField != null ? (int)_workUnitsField.GetValue(template) : 100;
                def.workUnitsForProduction = baseWork;
                def.gatheringState = template.gatheringState;
                def.manufacturingState = template.manufacturingState;
                def.productShortageWorkBonuses = new List<ProductShortageWorkBonus>();
                def.manufacturingIcon = template.manufacturingIcon;
                def.manufacturingTooltipLocalizeKey = TooltipTag(spec);
                return def;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} AltRecipes.Build({spec.DefinitionName}): {ex}");
                return null;
            }
        }

        /// <summary>Rewrites the swapped input amounts from the prefs (live) and, with
        /// notifyBuildings, asks producers to re-copy the lines (see GarmentRecipes).</summary>
        public static void ApplyPrefs(bool notifyBuildings = true)
        {
            try
            {
                var parts = new List<string>();
                foreach (var live in _live)
                {
                    var def = live.Definition; var template = live.Template;
                    if (def == null || def.sourceItems == null) continue;
                    foreach (var src in def.sourceItems)
                    {
                        if (src == null) continue;
                        Swap swap = null;
                        foreach (var s in live.Spec.Swaps) if (s.To == src.itemName) { swap = s; break; }
                        if (swap == null) continue;
                        var baseSrc = template.sourceItems?.Find(t => t != null && t.itemName == swap.From);
                        if (baseSrc == null) continue;
                        src.numSourceItemsNeeded = Mathf.Max(1, Mathf.RoundToInt(baseSrc.numSourceItemsNeeded * swap.CountMultiplier()));
                        src.capacity = Mathf.Max(src.numSourceItemsNeeded, baseSrc.capacity);
                    }
                    var inputs = new List<string>();
                    foreach (var s in def.sourceItems) if (s != null) inputs.Add($"{s.numSourceItemsNeeded} {s.itemName}");
                    parts.Add($"{live.Spec.DefinitionName} = {string.Join(" + ", inputs.ToArray())}");
                }
                if (parts.Count > 0) LiveStockMarketMod.Log.Msg($"{Tag} AltRecipes: {string.Join("; ", parts.ToArray())}.");
                if (notifyBuildings)
                {
                    var events = UnitySingletonPersistent<EventManager>.Instance;
                    if (events == null) return;
                    foreach (var live in _live)
                        if (live.Definition != null && live.Definition.sourceItems != null)
                            foreach (var src in live.Definition.sourceItems)
                                if (src != null) events.Raise(new ManufacturingSourceMaterialsChangedEvent(live.Definition, src.itemName));
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} AltRecipes.ApplyPrefs: {ex.Message}");
            }
        }
    }
}
