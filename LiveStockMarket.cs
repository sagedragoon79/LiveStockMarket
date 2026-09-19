using System;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using LiveStockMarket.Patches;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  Live-Stock Market  v1.1.0 — a wool economy for Farthest Frontier. By SageDragoon.
//
//  • Goat barns get a Goats / Sheep switch (buttons on the barn portrait). A
//    Sheep barn is named "Sheep Barn", its animals wear a sheep model, and its
//    herders shear wool in season instead of milking. The mode is saved.
//  • Wool is a new item: stored with hides, counted, traded by the agricultural and
//    hunter-and-herder merchants, with an icon and English strings.
//  • Pigs: a third barn mode. Own animated body, mushrooms from woodland grazing
//    (truffle pigs), more meat and tallow when butchered, fast breeding. Plus a
//    tallow candle recipe at the Candle Shop.
//  • Three wool garments — Winter Boots (Cobbler Shop), Winter Cloak (Tannery),
//    Woolen Clothes (Weaver) — are trade goods and warmer replacements for
//    shoes, hide coats and linen clothes; villagers prefer them while in stock.
//
//  Layout: Systems/ (mode store, naming, shearing, items, recipes, visuals),
//  Patches/ (Harmony). Plan and history: HANDOFF.md and docs/DEVELOPMENT.md.
//  Knowledge base: FF-Modding-Knowledge (items-system, livestock-system,
//  building-work-modes, skinned-mesh-swap-on-vanilla-rig).
// ─────────────────────────────────────────────────────────────────────────────

[assembly: MelonInfo(typeof(LiveStockMarket.LiveStockMarketMod), "Live-Stock Market", "1.1.0", "SageDragoon")]
[assembly: MelonGame("Crate Entertainment", "Farthest Frontier")]

namespace LiveStockMarket
{
    public class LiveStockMarketMod : MelonMod
    {
        internal const string DisplayName = "Live-Stock Market";
        internal const string Version     = "1.1.0";
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

        // Pigs (live unless noted).
        internal static MelonPreferences_Entry<float> cfgPigMeatMultiplier;
        internal static MelonPreferences_Entry<float> cfgPigTallowMultiplier;
        internal static MelonPreferences_Entry<float> cfgPigHideMultiplier;
        internal static MelonPreferences_Entry<float> cfgPigBreedingMultiplier;
        internal static MelonPreferences_Entry<float> cfgPigWasteMultiplier;
        internal static MelonPreferences_Entry<float> cfgPigMushroomsPerPigPerDay;
        internal static MelonPreferences_Entry<int>   cfgPigMushroomTreesForFullYield;
        internal static MelonPreferences_Entry<int>   cfgPigMushroomCapacity;
        internal static MelonPreferences_Entry<bool>  cfgPigVisuals;
        internal static MelonPreferences_Entry<float> cfgPigScale;
        // Pig sounds (live).
        internal static MelonPreferences_Entry<bool>  cfgPigSounds;
        internal static MelonPreferences_Entry<float> cfgPigGruntVolume;
        internal static MelonPreferences_Entry<float> cfgPigBreathingVolume;
        internal static MelonPreferences_Entry<float> cfgPigGruntIntervalMin;
        internal static MelonPreferences_Entry<float> cfgPigGruntIntervalMax;
        internal static MelonPreferences_Entry<float> cfgPigSoundRange;
        internal static MelonPreferences_Entry<bool>  cfgPigClickSound;
        internal static MelonPreferences_Entry<int>   cfgPigClickGrunt;

        // Recipes.
        internal static MelonPreferences_Entry<bool>  cfgTallowCandleEnabled;           // restart
        internal static MelonPreferences_Entry<float> cfgTallowCandleTallowMultiplier;  // live

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

            cfgPigMeatMultiplier = cfgCategory.CreateEntry("PigMeatMultiplier", 2f,
                display_name: "Pig Meat Multiplier",
                description: "Meat from a butchered pig relative to a goat. The extra is added straight to the barn's output. Applies live.");
            cfgPigTallowMultiplier = cfgCategory.CreateEntry("PigTallowMultiplier", 2f,
                display_name: "Pig Tallow Multiplier",
                description: "Tallow from a butchered pig relative to a goat. Applies live.");
            cfgPigHideMultiplier = cfgCategory.CreateEntry("PigHideMultiplier", 1.5f,
                display_name: "Pig Hide Multiplier",
                description: "Hide from a butchered pig relative to a goat. Applies live.");
            cfgPigBreedingMultiplier = cfgCategory.CreateEntry("PigBreedingMultiplier", 2f,
                display_name: "Pig Breeding Multiplier",
                description: "Breeding chance and minimum births of a Pig barn relative to goats. Applies live.");
            cfgPigWasteMultiplier = cfgCategory.CreateEntry("PigWasteMultiplier", 1.5f,
                display_name: "Pig Waste Multiplier",
                description: "Waste (manure) a Pig barn produces relative to goats: 1.5 shortens the goat's 30-day interval to 20 days. Applies live.");
            cfgPigMushroomsPerPigPerDay = cfgCategory.CreateEntry("PigMushroomsPerPigPerDay", 0.05f,
                display_name: "Mushrooms Per Pig Per Day",
                description: "Truffle pigs: mushrooms each grown pig finds per day when its grazing area is fully wooded, spring through autumn. Applies live.");
            cfgPigMushroomTreesForFullYield = cfgCategory.CreateEntry("PigMushroomTreesForFullYield", 12,
                display_name: "Trees For Full Mushroom Yield",
                description: "Trees inside the grazing area for the full rate; fewer trees scale it down, none means no mushrooms. Applies live.");
            cfgPigMushroomCapacity = cfgCategory.CreateEntry("PigMushroomCapacity", 100,
                display_name: "Pig Barn Mushroom Capacity",
                description: "Mushrooms a Pig barn holds before haulers must take them out. Applies live.");
            cfgPigVisuals = cfgCategory.CreateEntry("PigVisuals", true,
                display_name: "Pig Model",
                description: "Animals in a Pig barn use the animated pig body, name and icon. Off = goats keep their look. Applies live.");
            cfgPigScale = cfgCategory.CreateEntry("PigScale", 1f,
                display_name: "Pig Size",
                description: "Scale of the pig body. 1 = the model's real size (about 1 m long). Applies live.");

            cfgPigSounds = cfgCategory.CreateEntry("PigSounds", true,
                display_name: "Pig Sounds",
                description: "Grunts, a quiet breathing loop and the slaughter squeal from pigs. Applies live.");
            cfgPigGruntVolume = cfgCategory.CreateEntry("PigGruntVolume", 0.6f,
                display_name: "Pig Grunt Volume",
                description: "Volume of grunts and squeals (0 to 1), on top of the game's sound sliders. Applies live.");
            cfgPigBreathingVolume = cfgCategory.CreateEntry("PigBreathingVolume", 0.25f,
                display_name: "Pig Breathing Volume",
                description: "Volume of the breathing loop on every pig (0 to 1); 0 turns it off. Applies live.");
            cfgPigGruntIntervalMin = cfgCategory.CreateEntry("PigGruntIntervalMin", 6f,
                display_name: "Pig Grunt Interval Min",
                description: "Shortest wait in seconds between grunts across all pigs. Applies live.");
            cfgPigGruntIntervalMax = cfgCategory.CreateEntry("PigGruntIntervalMax", 20f,
                display_name: "Pig Grunt Interval Max",
                description: "Longest wait in seconds between grunts across all pigs. Applies live.");
            cfgPigSoundRange = cfgCategory.CreateEntry("PigSoundRange", 40f,
                display_name: "Pig Sound Range",
                description: "Distance in meters at which a grunt fades out; breathing carries 40% of it, a squeal 150%. Applies live.");
            cfgPigClickSound = cfgCategory.CreateEntry("PigClickSound", true,
                display_name: "Pig Click Sound",
                description: "A clicked pig, and a clicked Pig Barn, grunt instead of playing the goat's sounds. Applies live.");
            cfgPigClickGrunt = cfgCategory.CreateEntry("PigClickGrunt", 0,
                display_name: "Pig Click Grunt",
                description: "Which grunt answers a click: 0 = the click grunt made for it, 1 to 17 = one of the ambient grunts, -1 = a random ambient grunt each time. Changing it plays the grunt. Applies live.");
            cfgTallowCandleEnabled = cfgCategory.CreateEntry("TallowCandleEnabled", true,
                display_name: "Tallow Candle Recipe",
                description: "Adds a second candle recipe at the Candle Shop that uses tallow instead of wax. Requires game restart.");
            cfgTallowCandleTallowMultiplier = cfgCategory.CreateEntry("TallowCandleTallowMultiplier", 2f,
                display_name: "Tallow Per Wax",
                description: "Tallow in the tallow candle recipe relative to the wax in the vanilla one. 2 = twice as much. Applies live.");

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
            TradingPostPatches.Register(HarmonyInst); // Trading Center keep-in-stock entries for mod items
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
            PigSoundPatches.Register(HarmonyInst);           // a clicked pig grunts instead of the goat's bell and bleat
            cfgSheepVisuals.OnEntryValueChanged.Subscribe((oldValue, newValue) => SheepVisuals.ApplyAll());
            cfgSheepUseGameShader.OnEntryValueChanged.Subscribe((oldValue, newValue) => SheepVisuals.ApplyAll());

            // ── Pigs + recipes ───────────────────────────────────────────────
            PigVisuals.Register();                           // strings; the look is applied from SheepShearing.ApplyMode
            cfgPigBreedingMultiplier.OnEntryValueChanged.Subscribe((oldValue, newValue) => SheepShearing.OnPrefsChanged());
            cfgPigWasteMultiplier.OnEntryValueChanged.Subscribe((oldValue, newValue) => SheepShearing.OnPrefsChanged());
            cfgPigMushroomCapacity.OnEntryValueChanged.Subscribe((oldValue, newValue) => SheepShearing.OnPrefsChanged());
            cfgPigVisuals.OnEntryValueChanged.Subscribe((oldValue, newValue) => PigVisuals.ApplyAll());
            foreach (var e in new[] { cfgPigGruntVolume, cfgPigBreathingVolume, cfgPigGruntIntervalMin, cfgPigGruntIntervalMax, cfgPigSoundRange })
                e.OnEntryValueChanged.Subscribe((oldValue, newValue) => PigSounds.ApplyPrefs());
            cfgPigSounds.OnEntryValueChanged.Subscribe((oldValue, newValue) => PigSounds.ApplyPrefs());
            cfgPigClickGrunt.OnEntryValueChanged.Subscribe((oldValue, newValue) => PigSounds.PreviewClick());
            cfgPigScale.OnEntryValueChanged.Subscribe((oldValue, newValue) => PigVisuals.ApplyAll());
            cfgTallowCandleTallowMultiplier.OnEntryValueChanged.Subscribe((oldValue, newValue) => AltRecipes.ApplyPrefs());

            Log.Msg($"{LogTag} {DisplayName} v{Version} — loaded.");
        }

        public override void OnUpdate()
        {
            if (cfgModEnabled != null && cfgModEnabled.Value) PigSounds.Tick();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            InMap = sceneName == "Map";
            PigSounds.OnSceneLoaded();
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
            {
                GarmentRecipes.EnsureInjected();
                AltRecipes.EnsureInjected();
            }
        }
    }
}
