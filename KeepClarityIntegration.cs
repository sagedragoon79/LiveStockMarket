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
                "Sheep barns, wool, shearing and wool garments. Flip a goat barn to Sheep: its animals become sheep, herders shear wool in season, and the Cobbler, Tannery and Weaver can make Winter Boots, a Winter Cloak and Woolen Clothes.",
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
