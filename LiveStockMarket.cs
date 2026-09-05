using System;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using LiveStockMarket.Patches;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  Live-Stock Market  v0.3.1
//  A wool production chain for Farthest Frontier, built in steps. By SageDragoon.
//
//  Step 1 (done, verified in-game): a Goats / Sheep mode toggle on the vanilla
//  goat barn (GoatBarn and GoatBarn_Tier2 — the same GoatBarn class at tier 1 /
//  tier 2). Position-keyed mode store, buttons overlaid on the barn portrait,
//  mode appended to the barn's ES2 stream, and a Sheep barn is named
//  "Sheep Barn" / "Large Sheep Barn" everywhere. No behavior change.
//
//  Step 2 (this build): ItemWool — a genuinely new item.
//    • Systems/WoolItem.cs: name-keyed registration (ItemID MAX+1), ItemEntry
//      cloned from ItemHide (price, weight, category), Flax's carried mesh,
//      raises ItemStorage's per-item array limit, English strings.
//    • Patches/WoolItemPatches.cs: appended to WorkBucketManager's item lists
//      and given per-item work buckets; storable wherever hides are; icon.
//    • Patches/WoolStoragePatches.cs: marker so saves that predate wool get
//      it allowed by default in every storehouse filter.
//    • Systems/WoolTrade.cs: wool in every merchant's goods list, plus a pref
//      that forces traders to always stock it (the only source until step 3).
//    • Systems/WoolTestHotkey.cs: Ctrl+Shift+<key> drops a stack into a store.
//  Uninstall caveat: a save holding wool loads without the mod with errors
//  (items have no missing-mod path) — see README.
//
//  Plan of record: HANDOFF.md (repo root).
//  Pattern references: FF-Modding-Knowledge/mod-patterns/building-work-modes.md,
//  game-systems/items-system.md (with the September 4, 2026 corrections).
// ─────────────────────────────────────────────────────────────────────────────

[assembly: MelonInfo(typeof(LiveStockMarket.LiveStockMarketMod), "Live-Stock Market", "0.3.1", "SageDragoon")]
[assembly: MelonGame("Crate Entertainment", "Farthest Frontier")]

namespace LiveStockMarket
{
    public class LiveStockMarketMod : MelonMod
    {
        internal const string DisplayName = "Live-Stock Market";
        internal const string Version     = "0.3.1";
        internal const string HarmonyId   = "com.sagedragoon.livestockmarket";
        internal const string LogTag      = "[LSM]";

        internal static readonly BindingFlags AllInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static LiveStockMarketMod Instance { get; private set; }
        internal static MelonLogger.Instance Log => Instance?.LoggerInstance;
        internal static HarmonyLib.Harmony HarmonyInst { get; private set; }

        /// <summary>True while the Map scene is the active scene (hotkeys only run there).</summary>
        internal static bool InMap { get; private set; }

        // ── Config ──────────────────────────────────────────────────────────
        // Category identifier == Keep Clarity modId ("LiveStockMarket") so KC's
        // dedup lines up (keep-clarity-mod-manager-api.md, "Mod ID conventions").
        internal static MelonPreferences_Category    cfgCategory;
        internal static MelonPreferences_Entry<bool> cfgModEnabled;

        // Step 1 — button placement on the barn portrait. Live: read on every
        // injection, and a change rebuilds any open row. Strip before a release.
        internal static MelonPreferences_Entry<float> cfgButtonPosX;    // 0..1 across the portrait (0 = left)
        internal static MelonPreferences_Entry<float> cfgButtonPosY;    // 0..1 up the portrait (0 = bottom)
        internal static MelonPreferences_Entry<int>   cfgButtonWidth;   // UI units, per button
        internal static MelonPreferences_Entry<int>   cfgButtonHeight;  // UI units

        // Step 2 — wool at traders + a test hotkey.
        internal static MelonPreferences_Entry<bool>    cfgTradersAlwaysStockWool;
        internal static MelonPreferences_Entry<KeyCode> cfgTestWoolKey;     // with Ctrl+Shift held
        internal static MelonPreferences_Entry<int>     cfgTestWoolAmount;

        // Step 3 — shearing numbers (live; applied to every Sheep barn's setup clone).
        internal static MelonPreferences_Entry<int>   cfgShearSeasonStartDay;
        internal static MelonPreferences_Entry<int>   cfgShearSeasonEndDay;
        internal static MelonPreferences_Entry<int>   cfgShearCooldownDays;
        internal static MelonPreferences_Entry<int>   cfgWoolPerSheep;
        internal static MelonPreferences_Entry<float> cfgWoolSecondsPerUnit;
        internal static MelonPreferences_Entry<int>   cfgWoolGrowthDays;
        internal static MelonPreferences_Entry<int>   cfgSheepBarnWoolCapacity;

        public override void OnInitializeMelon()
        {
            Instance = this;

            cfgCategory = MelonPreferences.CreateCategory("LiveStockMarket");
            cfgModEnabled = cfgCategory.CreateEntry("ModEnabled", true,
                display_name: "Mod Enabled",
                description: "Master switch. Disable to fall back to vanilla behavior. Requires game restart.");

            // Defaults = the values dialed in on the user's screen on September 4, 2026.
            cfgButtonPosX = cfgCategory.CreateEntry("ButtonPosX", 0.56f,
                display_name: "Buttons X (portrait, 0-1)",
                description: "Horizontal center of the Goats / Sheep pair across the barn portrait. 0 = left edge, 1 = right edge. Applies live.");
            cfgButtonPosY = cfgCategory.CreateEntry("ButtonPosY", 0.40f,
                display_name: "Buttons Y (portrait, 0-1)",
                description: "Vertical center of the pair up the barn portrait. 0 = bottom edge, 1 = top edge. Applies live.");
            cfgButtonWidth = cfgCategory.CreateEntry("ButtonWidth", 95,
                display_name: "Button Width",
                description: "Width of each button in UI units. Applies live.");
            cfgButtonHeight = cfgCategory.CreateEntry("ButtonHeight", 47,
                display_name: "Button Height",
                description: "Height of each button in UI units. Applies live.");

            cfgTradersAlwaysStockWool = cfgCategory.CreateEntry("TradersAlwaysStockWool", true,
                display_name: "Traders Always Stock Wool",
                description: "Every visiting merchant brings wool. The only wool source until shearing (step 3). Off = wool is a normal random merchant good. Applies live.");
            // Alpha0: bare 1-9 and Ctrl+1-9 are control groups, W moves the camera.
            cfgTestWoolKey = cfgCategory.CreateEntry("TestWoolKey", KeyCode.Alpha0,
                display_name: "Test: Add Wool Hotkey (Ctrl+Shift+key)",
                description: "Hold Ctrl+Shift and press this key to drop a stack of wool into the first storehouse that accepts it. None disables it.");
            cfgTestWoolAmount = cfgCategory.CreateEntry("TestWoolAmount", 20,
                display_name: "Test: Add Wool Amount",
                description: "How much wool the test hotkey adds per press.");

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

            // ── Step 2: ItemWool ─────────────────────────────────────────────
            LocalizationPatches.Apply(HarmonyInst);   // serves LSM_ strings to vanilla UI
            WoolItem.EnsureRegistered();              // retried inside WorkBucketManager.Awake if the table isn't loadable yet
            WoolItemPatches.Register(HarmonyInst);    // item lists, work buckets, storage placement, icon
            WoolStoragePatches.Register(HarmonyInst); // old-save filter marker — never feature-gated
            cfgTradersAlwaysStockWool.OnEntryValueChanged.Subscribe((oldValue, newValue) => WoolTrade.ApplyForcedStock(newValue));

            // ── Step 1: Goats / Sheep mode toggle on the goat barn ───────────
            GoatBarnNaming.Register();                       // "Sheep Barn" name follows the mode (subscribes first)
            SheepShearing.Register();                        // step 3: setup asset + shearing rules follow the mode
            GoatBarnSaveLoadPatches.Register(HarmonyInst);   // persistence — never feature-gated (see file header)
            GoatBarnLoadPatches.Register(HarmonyInst);       // load-phase hook: name + shearing setup re-apply
            GoatBarnNamePatches.Register(HarmonyInst);       // name re-apply when vanilla regenerates it (upgrade)
            GoatBarnModeButtonPatches.Register(HarmonyInst); // info-window buttons + live title

            // ── Step 3: shearing ─────────────────────────────────────────────
            ShearingPatches.Register(HarmonyInst);           // harvest loop yields wool for Sheep barns
            foreach (var e in new[] { cfgShearSeasonStartDay, cfgShearSeasonEndDay, cfgShearCooldownDays, cfgWoolPerSheep, cfgWoolGrowthDays, cfgSheepBarnWoolCapacity })
                e.OnEntryValueChanged.Subscribe((oldValue, newValue) => SheepShearing.OnPrefsChanged());
            cfgWoolSecondsPerUnit.OnEntryValueChanged.Subscribe((oldValue, newValue) => SheepShearing.OnPrefsChanged());

            Log.Msg($"{LogTag} {DisplayName} v{Version} — loaded. Step 1 toggle, step 2 ItemWool, step 3 shearing.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            InMap = sceneName == "Map";
            if (!InMap) return;

            // Per-map reset. The Load postfix repopulates the store from the save
            // (load runs after the Map scene is up), so nothing stale survives
            // between games.
            GoatBarnModeStore.OnMapLoaded();

            if (cfgModEnabled.Value)
                WoolTrade.OnMapLoaded();   // merchant goods + forced stock, once the managers exist
        }

        public override void OnUpdate()
        {
            if (HarmonyInst == null) return;   // mod disabled
            WoolTestHotkey.Tick();             // gates itself on the live GameManager
        }
    }
}
