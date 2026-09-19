using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// Mode-driven display name for goat barns: "Goat Barn" / "Large Goat Barn"
    /// read "Sheep Barn" / "Large Sheep Barn" while a barn is in Sheep mode.
    ///
    /// Resource.displayName is runtime-only: nothing saves it, vanilla regenerates
    /// it from the localization key in Building.SetBuildingDataRecordName (load,
    /// fresh build, tier upgrade, language change), and its setter pushes the
    /// value into the widget blackboard that feeds the map hover label, lists,
    /// and modals. So the rename is an overlay that is re-applied at every point
    /// where the name or the mode can change:
    ///   • the toggle click (OnModeChanged subscription, registered here)
    ///   • the Load postfix, once the saved mode is known
    ///   • a postfix on SetBuildingDataRecordName (covers the tier upgrade)
    /// Apply() is idempotent, so calling it from several places is safe.
    ///
    /// English-only word swap ("Goat" → "Sheep", case preserved). A localized
    /// name without the word gets a " (Sheep)" suffix instead.
    /// </summary>
    internal static class GoatBarnNaming
    {
        private const string GoatWord       = "Goat";
        private const string SheepWord      = "Sheep";
        private const string FallbackSuffix = " (Sheep)";
        private const string PigWord        = "Pig";
        private const string PigSuffix      = " (Pigs)";

        // CEMonoBehaviour.localizationManager is protected — resolved once by reflection.
        private static PropertyInfo _locProp;
        private static bool _locPropTried;

        private static string Tag => LiveStockMarketMod.LogTag;

        public static void Register()
        {
            GoatBarnModeStore.OnModeChanged += OnModeChanged;
        }

        private static void OnModeChanged(Component changed)
        {
            if (changed is GoatBarn barn) Apply(barn);
        }

        /// <summary>Vanilla localized name for the barn's current record
        /// ("Goat Barn", "Large Goat Barn"), or null if it can't be resolved yet.</summary>
        public static string VanillaName(GoatBarn barn)
        {
            try
            {
                string record = barn.buildingDataRecordName;
                if (string.IsNullOrEmpty(record)) return null;

                var setup = GlobalAssets.buildingSetupData;
                var data = setup != null ? setup.GetBuildingData(record) : null;
                if (data == null || string.IsNullOrEmpty(data.identifier)) return null;

                var loc = GetLocalizationManager(barn);
                if (loc == null) return null;
                return loc.Localize("BuildersData_DisplayName_" + data.identifier);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>"Goat Barn" → "Sheep Barn"; a name without the word gets the suffix.</summary>
        public static string SheepName(string vanilla) => Swap(vanilla, SheepWord, FallbackSuffix);

        /// <summary>"Goat Barn" → "Pig Barn"; a name without the word gets the suffix.</summary>
        public static string PigName(string vanilla) => Swap(vanilla, PigWord, PigSuffix);

        private static string Swap(string vanilla, string replacement, string suffix)
        {
            if (string.IsNullOrEmpty(vanilla)) return vanilla;
            int i = vanilla.IndexOf(GoatWord, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return vanilla + suffix;
            string word = char.IsUpper(vanilla[i]) ? replacement : replacement.ToLowerInvariant();
            return vanilla.Substring(0, i) + word + vanilla.Substring(i + GoatWord.Length);
        }

        /// <summary>The name the barn should show for its current mode, or null.</summary>
        public static string ExpectedName(GoatBarn barn)
        {
            string vanilla = VanillaName(barn);
            if (vanilla == null) return null;
            switch (GoatBarnModeStore.GetMode(barn))
            {
                case GoatBarnMode.Sheep: return SheepName(vanilla);
                case GoatBarnMode.Pigs:  return PigName(vanilla);
                default: return vanilla;
            }
        }

        /// <summary>Sets displayName to the mode's name. Returns true when it changed.
        /// The setter also updates the widget blackboard, so the map label follows.</summary>
        public static bool Apply(GoatBarn barn)
        {
            if (barn == null) return false;
            try
            {
                string expected = ExpectedName(barn);
                if (string.IsNullOrEmpty(expected) || barn.displayName == expected) return false;
                barn.displayName = expected;
                return true;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log?.Warning($"{Tag} GoatBarnNaming.Apply: {ex.Message}");
                return false;
            }
        }

        private static LocalizationManager GetLocalizationManager(Component c)
        {
            if (!_locPropTried)
            {
                _locPropTried = true;
                _locProp = AccessTools.Property(c.GetType(), "localizationManager");
                if (_locProp == null)
                    LiveStockMarketMod.Log?.Warning(
                        $"{Tag} GoatBarnNaming: localizationManager property not found — barns keep their vanilla names.");
            }
            return _locProp?.GetValue(c, null) as LocalizationManager;
        }
    }
}
