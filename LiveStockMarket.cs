using System;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using LiveStockMarket.Patches;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  Live-Stock Market  v1.0.0 — a wool economy for Farthest Frontier. By SageDragoon.
//
//  • Goat barns get a Goats / Sheep switch (buttons on the barn portrait). A
//    Sheep barn is named "Sheep Barn", its animals wear a sheep model, and its
//    herders shear wool in season instead of milking. The mode is saved.
//  • Wool is a new item: stored with hides, counted, traded by the agricultural and
//    hunter-and-herder merchants, with an icon and English strings.
//  • Three wool garments — Winter Boots (Cobbler Shop), Winter Cloak (Tannery),
//    Woolen Clothes (Weaver) — are trade goods and warmer replacements for
//    shoes, hide coats and linen clothes; villagers prefer them while in stock.
//
//  Layout: Systems/ (mode store, naming, shearing, items, recipes, visuals),
//  Patches/ (Harmony). Plan and history: HANDOFF.md and docs/DEVELOPMENT.md.
//  Knowledge base: FF-Modding-Knowledge (items-system, livestock-system,
//  building-work-modes, skinned-mesh-swap-on-vanilla-rig).
// ─────────────────────────────────────────────────────────────────────────────

[assembly: MelonInfo(typeof(LiveStockMarket.LiveStockMarketMod), "Live-Stock Market", "1.0.0", "SageDragoon")]
[assembly: MelonGame("Crate Entertainment", "Farthest Frontier")]

namespace LiveStockMarket
{
    public class LiveStockMarketMod : MelonMod
    {
        internal const string DisplayName = "Live-Stock Market";
        internal const string Version     = "1.0.0";
        internal const string HarmonyId   = "com.sagedragoon.livestockmarket";
        internal const string LogTag      = "[LSM]";

        internal static readonly BindingFlags AllInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static LiveStockMarketMod Instance { get; private set; }
        internal static MelonLogger.Instance Log => Instance?.LoggerInstance;
        internal static HarmonyLib.Harmony HarmonyInst { get; private set; }

        /// <summary>True while the Map scene is the active scene.</summary>
        internal static bool InMap { get; private set; }

        // ── Config ──────────────────────────────────────────────────────────
        // Category identifier == Keep Clarity modId ("LiveStockMarket") so KC's
        // dedup lines up (keep-clarity-mod-manager-api.md, "Mod ID conventions").
        internal static MelonPreferences_Category    cfgCategory;
        internal static MelonPreferences_Entry<bool> cfgModEnabled;

        // Garments.
        internal static MelonPreferences_Entry<float> cfgGarmentWarmthMultiplier;   // live
        internal static MelonPreferences_Entry<int>   cfgGarmentWoolCost;           // live (recipes)
        internal static MelonPreferences_Entry<float> cfgGarmentInputMultiplier;    // live (recipes)
        internal static MelonPreferences_Entry<float> cfgGarmentPriceMultiplier;    // restart

        // Shearing (live; applied to every Sheep barn's setup clone).
        internal static MelonPreferences_Entry<int>   cfgShearSeasonStartDay;
        internal static MelonPreferences_Entry<int>   cfgShearSeasonEndDay;
        internal static MelonPreferences_Entry<int>   cfgShearCooldownDays;
        internal static MelonPreferences_Entry<int>   cfgWoolPerSheep;
        internal static MelonPreferences_Entry<float> cfgWoolSecondsPerUnit;
        internal static MelonPreferences_Entry<int>   cfgWoolGrowthDays;
        internal static MelonPreferences_Entry<int>   cfgSheepBarnWoolCapacity;

        // Visuals (live).
        internal static MelonPreferences_Entry<bool> cfgSheepVisuals;        // sheep model, name, icon in Sheep barns
        internal static MelonPreferences_Entry<bool> cfgSheepUseGameShader;  // goat material + sheep textures vs. bundle material

        public override void OnInitializeMelon()
        {
            Instance = this;

            cfgCategory = MelonPreferences.CreateCategory("LiveStockMarket");
            cfgModEnabled = cfgCategory.CreateEntry("ModEnabled", true,
                display_name: "Mod Enabled",
                description: "Master switch. Disable to fall back to vanilla behavior. Requires game restart.");

            cfgGarmentWarmthMultiplier = cfgCategory.CreateEntry("GarmentWarmthMultiplier", 1.25f,
                display_name: "Garment Warmth Multiplier",
                description: "A garment counts as the vanilla item it replaces at this effectiveness (shoe bonus, exposure clothing bonus). 1.25 = 25% better. Applies live.");
            cfgGarmentWoolCost = cfgCategory.CreateEntry("GarmentWoolCost", 5,
                display_name: "Garment Wool Cost",
                description: "Wool per garment in the Cobbler, Tannery and Weaver recipes. Applies live.");
            cfgGarmentInputMultiplier = cfgCategory.CreateEntry("GarmentInputMultiplier", 2f,
                display_name: "Garment Input Multiplier",
                description: "Multiplier on the vanilla recipe's leather or flax cost for the garment version. 2 = double. Applies live.");
            cfgGarmentPriceMultiplier = cfgCategory.CreateEntry("GarmentPriceMultiplier", 1.25f,
                display_name: "Garment Price Multiplier",
                description: "Garment base price = the replaced item's price times this. Requires game restart.");

            cfgShearSeasonStartDay = cfgCategory.CreateEntry("ShearSeasonStartDay", 78,
                display_name: "Shearing Season Start (day of year)",
                description: "First day of the year on which Sheep barns shear. Goat milking uses 78. Applies live.");
            cfgShearSeasonEndDay = cfgCategory.CreateEntry("ShearSeasonEndDay", 200,
                display_name: "Shearing Season End (day of year)",
                description: "Last day of the year on which Sheep barns shear. Applies live.");
            cfgShearCooldownDays = cfgCategory.CreateEntry("ShearCooldownDays", 300,
                display_name: "Shearing Cooldown (days)",
                description: "Days before a shorn sheep can be shorn again. 300 = once a year. Applies live.");
            cfgWoolPerSheep = cfgCategory.CreateEntry("WoolPerSheep", 4,
                display_name: "Wool Per Sheep (full growth)",
                description: "Wool a fully grown fleece yields per shearing. Scaled down when the barn has fewer sheep-days than the growth period. Applies live.");
            cfgWoolSecondsPerUnit = cfgCategory.CreateEntry("WoolSecondsPerUnit", 10f,
                display_name: "Worker Seconds Per Wool",
                description: "Herder time per unit of wool. Milk uses 10. Applies live.");
            cfgWoolGrowthDays = cfgCategory.CreateEntry("WoolGrowthDays", 240,
                display_name: "Fleece Growth (days)",
                description: "Days in Sheep mode for a full fleece; yield is proportional below that. Growth restarts the day after the season ends, so 240 = season end (200) back to season start (78). Applies live.");
            cfgSheepBarnWoolCapacity = cfgCategory.CreateEntry("SheepBarnWoolCapacity", 300,
                display_name: "Sheep Barn Wool Capacity",
                description: "How much wool a Sheep barn holds before haulers must take it out. Milk uses 300. Applies live.");

            cfgSheepVisuals = cfgCategory.CreateEntry("SheepVisuals", true,
                display_name: "Sheep Model",
                description: "Animals in a Sheep barn use the sheep model, name and icon. Off = goats keep their look. Applies live.");
            cfgSheepUseGameShader = cfgCategory.CreateEntry("SheepUseGameShader", true,
                display_name: "Game Shader On Sheep",
                description: "On: the sheep uses the goat's own material with the sheep textures. Off: the bundle's plain material. Applies live.");

            // Optional soft dependency — renders the prefs in Keep Clarity's F10 panel.
            // "LiveStockMarket" sorts after "KeepClarity", so KC is already loaded here.
            KeepClarityIntegration.TryRegisterAll();

            Log.Msg($"{LogTag} {DisplayName} v{Version} — initializing.");

            if (!cfgModEnabled.Value)
            {
                Log.Msg($"{LogTag} ModEnabled = false; skipping all patches (vanilla behavior).");
                return;
            }

            HarmonyInst = new HarmonyLib.Harmony(HarmonyId);

            // ── Items: wool and the three garments ───────────────────────────
            LocalizationPatches.Apply(HarmonyInst);   // serves LSM_ strings to vanilla UI
            ModItems.Define(WoolItem.Def);
            GarmentItems.DefineAll();
            ModItems.EnsureRegistered();              // re-run per item table instance from WorkBucketManager.Awake
            WoolItemPatches.Register(HarmonyInst);    // item lists, work buckets, ItemInfo, storage placement, icons, recipe lines
            WoolStoragePatches.Register(HarmonyInst); // storage filter marker for saves that predate an item — never feature-gated
            GarmentPatches.Register(HarmonyInst);     // garments as wearables: inventory substitution + seek requests
            cfgGarmentWoolCost.OnEntryValueChanged.Subscribe((oldValue, newValue) => GarmentRecipes.ApplyPrefs());
            cfgGarmentInputMultiplier.OnEntryValueChanged.Subscribe((oldValue, newValue) => GarmentRecipes.ApplyPrefs());

            // ── Goats / Sheep mode on the goat barn ──────────────────────────
            GoatBarnNaming.Register();                       // "Sheep Barn" name follows the mode (subscribes first)
            SheepShearing.Register();                        // setup asset, shearing rules and the sheep look follow the mode
            GoatBarnSaveLoadPatches.Register(HarmonyInst);   // persistence — never feature-gated (see file header)
            GoatBarnLoadPatches.Register(HarmonyInst);       // load-phase hook: name + shearing setup re-apply
            GoatBarnNamePatches.Register(HarmonyInst);       // name re-apply when vanilla regenerates it (upgrade)
            GoatBarnModeButtonPatches.Register(HarmonyInst); // info-window buttons + live title

            // ── Shearing ─────────────────────────────────────────────────────
            ShearingPatches.Register(HarmonyInst);           // harvest loop yields wool for Sheep barns
            foreach (var e in new[] { cfgShearSeasonStartDay, cfgShearSeasonEndDay, cfgShearCooldownDays, cfgWoolPerSheep, cfgWoolGrowthDays, cfgSheepBarnWoolCapacity })
                e.OnEntryValueChanged.Subscribe((oldValue, newValue) => SheepShearing.OnPrefsChanged());
            cfgWoolSecondsPerUnit.OnEntryValueChanged.Subscribe((oldValue, newValue) => SheepShearing.OnPrefsChanged());

            // ── Visuals ──────────────────────────────────────────────────────
            SheepVisuals.Register();                         // strings; the look is applied from SheepShearing.ApplyMode
            SheepVisualPatches.Register(HarmonyInst);        // new animals + the "Shearing Sheep" herder label
            GarmentDisplayPatches.Register(HarmonyInst);     // worn garments show their own icon in the villager window
            SheepBarnUiPatches.Register(HarmonyInst);        // barn window: sheep icons on the status block and control buttons
            cfgSheepVisuals.OnEntryValueChanged.Subscribe((oldValue, newValue) => SheepVisuals.ApplyAll());
            cfgSheepUseGameShader.OnEntryValueChanged.Subscribe((oldValue, newValue) => SheepVisuals.ApplyAll());

            Log.Msg($"{LogTag} {DisplayName} v{Version} — loaded.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            InMap = sceneName == "Map";
            if (!InMap) return;

            // Per-map reset. The Load postfix repopulates the store from the save
            // (load runs after the Map scene is up), so nothing stale survives
            // between games.
            GoatBarnModeStore.OnMapLoaded();
            GarmentPatches.OnMapLoaded();

            if (cfgModEnabled.Value)
                WoolTrade.OnMapLoaded();   // merchant goods, once the managers exist
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // The game scene ("Frontier") loads before "Map"; the building table is
            // reloaded per game, so recipes are injected here (Farther Fabricating's
            // hook) and again from WorkBucketManager.Awake as a safety net.
            if (sceneName == "Frontier" && HarmonyInst != null)
                GarmentRecipes.EnsureInjected();
        }
    }
}
