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

                // Live layout knobs for the Goats / Sheep pair on the barn portrait.
                // A change rebuilds the open row; otherwise reselect the barn.
                Reg("Buttons", LiveStockMarketMod.cfgButtonPosX,
                    Meta("Buttons X (portrait)", "Horizontal center of the pair across the barn portrait. 0 = left edge, 1 = right edge. Live.",
                        min: 0f, max: 1f, step: 0.01f, order: 0));
                Reg("Buttons", LiveStockMarketMod.cfgButtonPosY,
                    Meta("Buttons Y (portrait)", "Vertical center of the pair up the barn portrait. 0 = bottom edge, 1 = top edge. Live.",
                        min: 0f, max: 1f, step: 0.01f, order: 1));
                Reg("Buttons", LiveStockMarketMod.cfgButtonWidth,
                    Meta("Button Width", "Width of each button in UI units. Live.", min: 40, max: 300, step: 1, order: 2));
                Reg("Buttons", LiveStockMarketMod.cfgButtonHeight,
                    Meta("Button Height", "Height of each button in UI units. Live.", min: 20, max: 80, step: 1, order: 3));

                // Step 2: wool supply until shearing exists, and a test hotkey.
                Reg("Wool", LiveStockMarketMod.cfgTradersAlwaysStockWool,
                    Meta("Traders Always Stock Wool", "Every merchant brings wool. Turn off once shearing exists for normal random stock. Live.", order: 0));
                Reg("Testing", LiveStockMarketMod.cfgTestWoolKey,
                    Meta("Add Wool Hotkey", "Hold Ctrl+Shift and press this key to drop a stack of wool into a storehouse. None disables it.", order: 0));
                Reg("Testing", LiveStockMarketMod.cfgTestWoolAmount,
                    Meta("Add Wool Amount", "How much wool the hotkey adds per press.", min: 1, max: 200, step: 1, order: 1));

                // Step 5: garments.
                Reg("Garments", LiveStockMarketMod.cfgGarmentWarmthMultiplier,
                    Meta("Warmth Multiplier", "A garment counts as the vanilla item it replaces at this effectiveness. 1.25 = 25% better. Live.", min: 1f, max: 3f, step: 0.05f, order: 0));
                Reg("Garments", LiveStockMarketMod.cfgGarmentWoolCost,
                    Meta("Wool Per Garment", "Wool in each garment recipe. Live.", min: 1, max: 50, step: 1, order: 1));
                Reg("Garments", LiveStockMarketMod.cfgGarmentInputMultiplier,
                    Meta("Input Multiplier", "Multiplier on the vanilla recipe's leather or flax. 2 = double. Live.", min: 1f, max: 5f, step: 0.25f, order: 2));
                Reg("Garments", LiveStockMarketMod.cfgGarmentPriceMultiplier,
                    Meta("Price Multiplier", "Garment price = the replaced item's price times this.", min: 0.5f, max: 5f, step: 0.05f, restartRequired: true, order: 3));

                // Step 3: shearing numbers, all live (they rewrite every Sheep barn's setup clone).
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
                "A wool production chain, built in steps. Step 1: Goats / Sheep mode on the goat barn. Step 2: wool as a new item.",
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
