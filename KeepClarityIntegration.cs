using System;
using System.Reflection;
using MelonLoader;

namespace LiveStockMarket
{
    /// <summary>
    /// Optional reflective integration with the Keep Clarity mod-settings panel
    /// (assembly "KeepClarity", namespace FFUIOverhaul.Settings). The mod works
    /// without KC installed; when KC is present, the prefs render in the F10 panel
    /// with labels, tooltips, and the restart-required marker.
    ///
    /// Our assembly name ("LiveStockMarket") sorts after "KeepClarity", so KC has
    /// loaded by the time OnInitializeMelon runs — no deferred OnSceneWasLoaded
    /// needed. modId matches the MelonPreferences category so KC's dedup lines up.
    /// See FF-Modding-Knowledge/project-ui-overhaul/keep-clarity-mod-manager-api.md.
    /// </summary>
    internal static class KeepClarityIntegration
    {
        private const string ModId          = "LiveStockMarket";
        private const string ModDisplayName = "Live-Stock Market";

        private static bool _resolved, _present;
        private static MethodInfo _registerMod, _registerEntry;
        private static Type _metaType;

        private static MelonLogger.Instance Log => LiveStockMarketMod.Log;

        public static void TryRegisterAll()
        {
            if (!ResolveApi())
            {
                Log?.Msg($"{LiveStockMarketMod.LogTag} Keep Clarity not present — using MelonPreferences.cfg only.");
                return;
            }
            try
            {
                RegisterMod();

                Reg("Master", LiveStockMarketMod.cfgModEnabled,
                    Meta("Mod Enabled", "Disable to fall back to vanilla behavior.", restartRequired: true, order: 0));

                Reg("Garments", LiveStockMarketMod.cfgGarmentWarmthMultiplier,
                    Meta("Warmth Multiplier", "A garment counts as the vanilla item it replaces at this effectiveness. 1.25 = 25% better. Live.", min: 1f, max: 3f, step: 0.05f, order: 0));
                Reg("Garments", LiveStockMarketMod.cfgGarmentWoolCost,
                    Meta("Wool Per Garment", "Wool in each garment recipe. Live.", min: 1, max: 50, step: 1, order: 1));
                Reg("Garments", LiveStockMarketMod.cfgGarmentInputMultiplier,
                    Meta("Input Multiplier", "Multiplier on the vanilla recipe's leather or flax. 2 = double. Live.", min: 1f, max: 5f, step: 0.25f, order: 2));
                Reg("Garments", LiveStockMarketMod.cfgGarmentPriceMultiplier,
                    Meta("Price Multiplier", "Garment price = the replaced item's price times this.", min: 0.5f, max: 5f, step: 0.05f, restartRequired: true, order: 3));

                // Shearing numbers, all live (they rewrite every Sheep barn's setup clone).
                Reg("Shearing", LiveStockMarketMod.cfgShearSeasonStartDay,
                    Meta("Season Start (day of year)", "First day Sheep barns shear. Goat milking starts on 78.", min: 1, max: 365, step: 1, order: 0));
                Reg("Shearing", LiveStockMarketMod.cfgShearSeasonEndDay,
                    Meta("Season End (day of year)", "Last day Sheep barns shear.", min: 1, max: 365, step: 1, order: 1));
                Reg("Shearing", LiveStockMarketMod.cfgShearCooldownDays,
                    Meta("Cooldown (days)", "Days before a shorn sheep can be shorn again. 300 = once a year.", min: 1, max: 400, step: 1, order: 2));
                Reg("Shearing", LiveStockMarketMod.cfgWoolPerSheep,
                    Meta("Wool Per Sheep", "Yield of a fully grown fleece per shearing.", min: 0, max: 50, step: 1, order: 3));
                Reg("Shearing", LiveStockMarketMod.cfgWoolGrowthDays,
                    Meta("Fleece Growth (days)", "Days in Sheep mode for a full fleece; proportional below that. Growth restarts the day after the season ends and banks across a switch to Goats.", min: 1, max: 730, step: 1, order: 4));
                Reg("Shearing", LiveStockMarketMod.cfgWoolSecondsPerUnit,
                    Meta("Worker Seconds Per Wool", "Herder time per unit of wool. Milk uses 10.", min: 1f, max: 600f, step: 1f, order: 5));
                Reg("Shearing", LiveStockMarketMod.cfgSheepBarnWoolCapacity,
                    Meta("Sheep Barn Wool Capacity", "Wool a Sheep barn holds before haulers take it out. Milk uses 300.", min: 10, max: 5000, step: 10, order: 6));

                Reg("Visuals", LiveStockMarketMod.cfgSheepVisuals,
                    Meta("Sheep Model", "Animals in a Sheep barn use the sheep model, name and icon. Off = goats keep their look. Live.", order: 0));
                Reg("Visuals", LiveStockMarketMod.cfgSheepUseGameShader,
                    Meta("Game Shader On Sheep", "On: the goat's own material with the sheep textures. Off: the bundle's plain material, if the game shader renders the sheep wrong. Live.", order: 1));
                Reg("Visuals", LiveStockMarketMod.cfgPigVisuals,
                    Meta("Pig Model", "Animals in a Pig barn use the animated pig body, name and icon. Live.", order: 2));
                Reg("Visuals", LiveStockMarketMod.cfgPigScale,
                    Meta("Pig Size", "Scale of the pig body; 1 = about 1 m long. Live.", min: 0.5f, max: 2.5f, step: 0.05f, order: 3));

                Reg("Pigs", LiveStockMarketMod.cfgPigMeatMultiplier,
                    Meta("Meat Multiplier", "Meat from a butchered pig relative to a goat. Live.", min: 0.5f, max: 5f, step: 0.25f, order: 0));
                Reg("Pigs", LiveStockMarketMod.cfgPigTallowMultiplier,
                    Meta("Tallow Multiplier", "Tallow from a butchered pig relative to a goat. Live.", min: 0.5f, max: 5f, step: 0.25f, order: 1));
                Reg("Pigs", LiveStockMarketMod.cfgPigHideMultiplier,
                    Meta("Hide Multiplier", "Hide from a butchered pig relative to a goat. Live.", min: 0.5f, max: 5f, step: 0.25f, order: 2));
                Reg("Pigs", LiveStockMarketMod.cfgPigBreedingMultiplier,
                    Meta("Breeding Multiplier", "Breeding chance and minimum births relative to goats. Live.", min: 0.5f, max: 5f, step: 0.25f, order: 3));
                Reg("Pigs", LiveStockMarketMod.cfgPigWasteMultiplier,
                    Meta("Waste Multiplier", "Waste (manure) relative to goats: 1.5 = a 20-day interval instead of 30. Live.", min: 0.5f, max: 5f, step: 0.25f, order: 4));
                Reg("Pigs", LiveStockMarketMod.cfgPigMushroomsPerPigPerDay,
                    Meta("Mushrooms Per Pig Per Day", "Truffle pigs: per grown pig per day in a fully wooded grazing area, spring through autumn. Live.", min: 0f, max: 1f, step: 0.01f, order: 5));
                Reg("Pigs", LiveStockMarketMod.cfgPigMushroomTreesForFullYield,
                    Meta("Trees For Full Yield", "Trees in the grazing area for the full mushroom rate. Live.", min: 1, max: 60, step: 1, order: 6));
                Reg("Pigs", LiveStockMarketMod.cfgPigMushroomCapacity,
                    Meta("Mushroom Capacity", "Mushrooms a Pig barn holds before haulers take them out. Live.", min: 10, max: 1000, step: 10, order: 7));

                Reg("Sounds", LiveStockMarketMod.cfgPigSounds,
                    Meta("Pig Sounds", "Grunts, a quiet breathing loop and the slaughter squeal from pigs. Live.", order: 0));
                Reg("Sounds", LiveStockMarketMod.cfgPigGruntVolume,
                    Meta("Grunt Volume", "Grunts and squeals, on top of the game's sound sliders. Live.", min: 0f, max: 1f, step: 0.05f, order: 1));
                Reg("Sounds", LiveStockMarketMod.cfgPigBreathingVolume,
                    Meta("Breathing Volume", "The breathing loop on every pig; 0 turns it off. Live.", min: 0f, max: 1f, step: 0.05f, order: 2));
                Reg("Sounds", LiveStockMarketMod.cfgPigGruntIntervalMin,
                    Meta("Grunt Interval Min (s)", "Shortest wait between grunts across all pigs. Live.", min: 1f, max: 120f, step: 1f, order: 3));
                Reg("Sounds", LiveStockMarketMod.cfgPigGruntIntervalMax,
                    Meta("Grunt Interval Max (s)", "Longest wait between grunts across all pigs. Live.", min: 2f, max: 300f, step: 1f, order: 4));
                Reg("Sounds", LiveStockMarketMod.cfgPigSoundRange,
                    Meta("Sound Range (m)", "Where a grunt fades out; breathing carries 40% of it, a squeal 150%. Live.", min: 5f, max: 200f, step: 5f, order: 5));
                Reg("Sounds", LiveStockMarketMod.cfgPigClickSound,
                    Meta("Pig Click Sound", "A clicked pig, and a clicked Pig Barn, grunt instead of playing the goat's sounds. Live.", order: 6));
                Reg("Sounds", LiveStockMarketMod.cfgPigClickGrunt,
                    Meta("Click Grunt", "0 = the click grunt made for it, 1 to 17 = one of the ambient grunts, -1 = a random one each time. Moving the slider plays it. Live.", min: -1, max: 17, step: 1, order: 7));

                Reg("Recipes", LiveStockMarketMod.cfgTallowCandleEnabled,
                    Meta("Tallow Candles", "A second Candle Shop recipe: tallow instead of wax.", restartRequired: true, order: 0));
                Reg("Recipes", LiveStockMarketMod.cfgTallowCandleTallowMultiplier,
                    Meta("Tallow Per Wax", "Tallow in that recipe relative to the vanilla wax count. Live.", min: 0.5f, max: 5f, step: 0.25f, order: 1));

                Log?.Msg($"{LiveStockMarketMod.LogTag} Registered with the Keep Clarity settings panel.");
            }
            catch (Exception e)
            {
                Log?.Warning($"{LiveStockMarketMod.LogTag} Keep Clarity registration failed: {e.Message}");
            }
        }

        private static bool ResolveApi()
        {
            if (_resolved) return _present;
            _resolved = true;
            var apiType = Type.GetType("FFUIOverhaul.Settings.SettingsAPI, KeepClarity");
            _metaType   = Type.GetType("FFUIOverhaul.Settings.SettingsMeta, KeepClarity");
            if (apiType == null || _metaType == null) { _present = false; return false; }
            _registerMod = apiType.GetMethod("RegisterMod", BindingFlags.Public | BindingFlags.Static);
            foreach (var m in apiType.GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "Register" && m.IsGenericMethodDefinition) { _registerEntry = m; break; }
            _present = _registerMod != null && _registerEntry != null;
            return _present;
        }

        private static void RegisterMod()
        {
            _registerMod.Invoke(null, new object[]
            {
                ModId, ModDisplayName,
                "Sheep and pig barns, wool, shearing, wool garments, truffle pigs and tallow candles. Flip a goat barn to Sheep or Pigs: sheep are shorn for wool that the Cobbler, Tannery and Weaver turn into Winter Boots, a Winter Cloak and Woolen Clothes; pigs forage mushrooms in a wooded grazing area and butcher for extra meat and tallow.",
                LiveStockMarketMod.Version,
                null,                                        // iconResourcePath
                new[] { 0.86f, 0.80f, 0.66f, 1f },           // wool-cream accent stripe
                20                                           // constellation order: KC=0, RR=10, others=20
            });
        }

        private static object Meta(string label = null, string tooltip = null,
            object min = null, object max = null, object step = null,
            bool restartRequired = false, int order = 0)
        {
            var m = Activator.CreateInstance(_metaType);
            void Set(string field, object value) { var f = _metaType.GetField(field); if (f != null) f.SetValue(m, value); }
            Set("Label", label);
            Set("Tooltip", tooltip);
            Set("Min", min);
            Set("Max", max);
            Set("Step", step);
            Set("RestartRequired", restartRequired);
            Set("Order", order);
            return m;
        }

        private static void Reg<T>(string category, MelonPreferences_Entry<T> entry, object meta)
        {
            if (entry == null) return;
            _registerEntry.MakeGenericMethod(typeof(T))
                .Invoke(null, new object[] { ModId, ModDisplayName, category, entry, meta });
        }
    }
}
