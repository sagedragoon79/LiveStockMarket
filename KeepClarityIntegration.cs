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
                "A wool production chain, built in steps. Step 1: Goats / Sheep mode on the goat barn.",
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
