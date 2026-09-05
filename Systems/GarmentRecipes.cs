using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Patches;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// The three garment recipes, one per producer, cloned from that producer's
    /// vanilla recipe for the item the garment replaces:
    ///   Cobbler Shop: Winter Boots   = shoes' leather × input multiplier + wool
    ///   Tannery:      Winter Cloak   = hide coat's leather × multiplier + wool
    ///   Weaver:       Woolen Clothes = linen clothes' flax × multiplier + wool
    ///
    /// Recipes live per building identifier in BuildingSetupData (BuildingData.
    /// manufactureDefinitions); the manufacturing UI iterates that list and a
    /// building matches saved work orders against it by the recipe's guid, so a
    /// fixed guid per recipe is all persistence needs (Farther Fabricating does
    /// the same). GlobalAssets drops the building table when a game unloads, so
    /// injection is keyed on the table instance and re-run per game.
    ///
    /// ItemDefinition.item resolves its item by CLASS name (Type.GetType), which
    /// mod items don't have. The private _item cache is pre-set here as a fast
    /// path, but that is not enough on its own: a building copies every recipe
    /// line into its stocking-request tables at Awake (Building.
    /// SetupManufacturingDicts, copy ctor drops the cache), so WoolItemPatches
    /// also prefixes the getter to resolve mod items by name for any copy.
    /// </summary>
    internal static class GarmentRecipes
    {
        private sealed class Spec
        {
            public string BuildingIdentifier;
            public ModItemDef Product;
            public string DefinitionName;
            public string Guid;
            public string Tooltip;
        }

        private sealed class Live
        {
            public Spec Spec;
            public ManufactureDefinition Definition;
            public ManufactureDefinition Template;
        }

        private static readonly Spec[] Specs =
        {
            new Spec { BuildingIdentifier = "CobblerShop",    Product = GarmentItems.WinterBoots,   DefinitionName = "LSM_ManufactureWinterBoots",   Guid = "5a1c0f0e-4c6d-4e00-9e01-000000000101", Tooltip = "Winter Boots: wool-lined leather boots. Warmer than shoes." },
            new Spec { BuildingIdentifier = "Tannery",        Product = GarmentItems.WinterCloak,   DefinitionName = "LSM_ManufactureWinterCloak",   Guid = "5a1c0f0e-4c6d-4e00-9e01-000000000102", Tooltip = "Winter Cloak: a wool-lined leather cloak. Warmer than a hide coat." },
            new Spec { BuildingIdentifier = "WeaverBuilding", Product = GarmentItems.WoolenClothes, DefinitionName = "LSM_ManufactureWoolenClothes", Guid = "5a1c0f0e-4c6d-4e00-9e01-000000000103", Tooltip = "Woolen Clothes: clothes woven from wool. Warmer than linen." },
        };

        private static readonly List<Live> _live = new List<Live>();
        private static BuildingSetupData _injectedInto;

        private static readonly FieldInfo _itemCacheField     = AccessTools.Field(typeof(ItemDefinition), "_item");
        private static readonly FieldInfo _numProducedField   = AccessTools.Field(typeof(ProducedItemDefinition), "_numItemsProduced");
        private static readonly FieldInfo _workUnitsField     = AccessTools.Field(typeof(ManufactureDefinition), "_workUnitsForProduction");
        private static readonly FieldInfo _guidField          = AccessTools.Field(typeof(CEGuidScriptableObject), "guidAsString");

        private static string Tag => LiveStockMarketMod.LogTag;

        internal static int   WoolCost        => Mathf.Clamp(LiveStockMarketMod.cfgGarmentWoolCost?.Value ?? 5, 1, 50);
        internal static float InputMultiplier => Mathf.Clamp(LiveStockMarketMod.cfgGarmentInputMultiplier?.Value ?? 2f, 1f, 5f);

        /// <summary>Idempotent per building-table instance; safe to call often.</summary>
        public static void EnsureInjected()
        {
            try
            {
                if (!ModItems.EnsureRegistered()) return;
                var setup = GlobalAssets.buildingSetupData;
                if (setup == null || setup.buildingData == null) return;
                if (_injectedInto == setup) return;

                _live.Clear();
                int added = 0;
                foreach (var spec in Specs)
                {
                    LocalizationPatches.Register(TooltipTag(spec), spec.Tooltip);

                    var data = FindBuildingData(setup, spec.BuildingIdentifier);
                    if (data == null)
                    {
                        LiveStockMarketMod.Log.Warning($"{Tag} GarmentRecipes: building data '{spec.BuildingIdentifier}' not found — no {spec.Product.DisplayName} recipe.");
                        continue;
                    }
                    if (data.manufactureDefinitions == null) data.manufactureDefinitions = new List<ManufactureDefinition>();

                    var template = data.manufactureDefinitions.Find(d => d != null && d.producedItems != null
                        && d.producedItems.Exists(p => p != null && p.itemName == spec.Product.TemplateName));
                    if (template == null)
                    {
                        LiveStockMarketMod.Log.Warning($"{Tag} GarmentRecipes: '{spec.BuildingIdentifier}' has no recipe producing {spec.Product.TemplateName} — no {spec.Product.DisplayName} recipe.");
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
                ApplyPrefs(notifyBuildings: false);   // no buildings exist yet at scene init
                LiveStockMarketMod.Log.Msg($"{Tag} GarmentRecipes: {added} recipes added, {_live.Count} live (Cobbler Shop, Tannery, Weaver).");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentRecipes.EnsureInjected: {ex}");
            }
        }

        private static string TooltipTag(Spec spec) => "LSM_" + spec.DefinitionName + "_Tooltip";

        private static BuildingData FindBuildingData(BuildingSetupData setup, string identifier)
        {
            foreach (var data in setup.buildingData)
                if (data != null && data.identifier == identifier) return data;
            return null;
        }

        private static ManufactureDefinition Build(Spec spec, ManufactureDefinition template)
        {
            try
            {
                var def = ScriptableObject.CreateInstance<ManufactureDefinition>();
                def.name = spec.DefinitionName;
                def.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _guidField?.SetValue(def, spec.Guid);

                // Sources: the vanilla recipe's inputs, scaled, plus wool.
                def.sourceItems = new List<SourceItemDefinition>();
                if (template.sourceItems != null)
                {
                    foreach (var src in template.sourceItems)
                    {
                        if (src == null) continue;
                        var copy = new SourceItemDefinition(src);
                        def.sourceItems.Add(copy);
                    }
                }
                var wool = new SourceItemDefinition(WoolItem.ItemName, WoolCost * 4, WoolCost, new SourceItemCriticalSupplySettings());
                _itemCacheField?.SetValue(wool, WoolItem.NewItem());
                def.sourceItems.Add(wool);

                // Product: the garment, same batch size and capacity as the template's product.
                var templateProduct = template.producedItems.Find(p => p != null && p.itemName == spec.Product.TemplateName);
                var product = new ProducedItemDefinition(spec.Product.Name, templateProduct != null ? templateProduct.capacity : 20);
                _numProducedField?.SetValue(product, templateProduct != null ? templateProduct.numItemsProducedBase : 1);
                if (templateProduct != null)
                {
                    product.ignoreCapacityAlerts = templateProduct.ignoreCapacityAlerts;
                    product.blockTransferringToStorage = templateProduct.blockTransferringToStorage;
                }
                _itemCacheField?.SetValue(product, spec.Product.NewItem());
                def.producedItems = new List<ProducedItemDefinition> { product };

                // Work, states, UI.
                int baseWork = _workUnitsField != null ? (int)_workUnitsField.GetValue(template) : 100;
                def.workUnitsForProduction = baseWork;
                def.gatheringState = template.gatheringState;
                def.manufacturingState = template.manufacturingState;
                def.productShortageWorkBonuses = new List<ProductShortageWorkBonus>();
                def.manufacturingIcon = spec.Product.Icon;
                def.manufacturingTooltipLocalizeKey = TooltipTag(spec);
                return def;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentRecipes.Build({spec.DefinitionName}): {ex}");
                return null;
            }
        }

        /// <summary>
        /// Rewrites every live recipe's input amounts from the prefs (live). With
        /// notifyBuildings, every producer re-copies the changed lines: a building
        /// copies each recipe line into its stocking-request table at Awake and
        /// only refreshes that copy on ManufacturingSourceMaterialsChangedEvent,
        /// so without the event a slider change would reach the recipe (what a
        /// batch consumes) but not the stocking target (what the worker fetches).
        /// </summary>
        public static void ApplyPrefs(bool notifyBuildings = true)
        {
            try
            {
                foreach (var live in _live)
                {
                    var def = live.Definition;
                    var template = live.Template;
                    if (def == null || def.sourceItems == null) continue;
                    foreach (var src in def.sourceItems)
                    {
                        if (src == null) continue;
                        if (src.itemName == WoolItem.ItemName)
                        {
                            src.numSourceItemsNeeded = WoolCost;
                            src.capacity = WoolCost * 4;
                            continue;
                        }
                        var baseSrc = template.sourceItems?.Find(s => s != null && s.itemName == src.itemName);
                        if (baseSrc == null) continue;
                        src.numSourceItemsNeeded = Mathf.Max(1, Mathf.RoundToInt(baseSrc.numSourceItemsNeeded * InputMultiplier));
                        src.capacity = Mathf.Max(src.numSourceItemsNeeded, Mathf.RoundToInt(baseSrc.capacity * InputMultiplier));
                    }
                }
                if (_live.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (var live in _live)
                    {
                        var inputs = new List<string>();
                        foreach (var s in live.Definition.sourceItems) if (s != null) inputs.Add($"{s.numSourceItemsNeeded} {s.itemName}");
                        parts.Add($"{live.Spec.Product.DisplayName} = {string.Join(" + ", inputs.ToArray())}");
                    }
                    LiveStockMarketMod.Log.Msg($"{Tag} GarmentRecipes: {string.Join("; ", parts.ToArray())}.");
                }
                if (notifyBuildings) NotifyBuildings();
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentRecipes.ApplyPrefs: {ex.Message}");
            }
        }

        /// <summary>
        /// Asks every producer that carries a garment recipe to re-copy its input
        /// lines (Building.OnManufacturingSourceMaterialsChanged re-keys the line
        /// and its stocking request). No-op outside a game: the persistent event
        /// manager has no building listeners then.
        /// </summary>
        private static void NotifyBuildings()
        {
            var events = UnitySingletonPersistent<EventManager>.Instance;
            if (events == null) return;
            int raised = 0;
            foreach (var live in _live)
            {
                var def = live.Definition;
                if (def == null || def.sourceItems == null) continue;
                foreach (var src in def.sourceItems)
                {
                    if (src == null) continue;
                    events.Raise(new ManufacturingSourceMaterialsChangedEvent(def, src.itemName));
                    raised++;
                }
            }
            LiveStockMarketMod.Log.Msg($"{Tag} GarmentRecipes: asked producers to refresh {raised} recipe input lines.");
        }
    }
}
