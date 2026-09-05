using System;
using System.Reflection;
using MelonLoader;
using UnityEngine;
using LiveStockMarket.Patches;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  Live-Stock Market  v0.2.4
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

[assembly: MelonInfo(typeof(LiveStockMarket.LiveStockMarketMod), "Live-Stock Market", "0.2.4", "SageDragoon")]
[assembly: MelonGame("Crate Entertainment", "Farthest Frontier")]

namespace LiveStockMarket
{
    public class LiveStockMarketMod : MelonMod
    {
        internal const string DisplayName = "Live-Stock Market";
        internal const string Version     = "0.2.4";
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
            GoatBarnSaveLoadPatches.Register(HarmonyInst);   // persistence — never feature-gated (see file header)
            GoatBarnLoadPatches.Register(HarmonyInst);       // load-phase hook (step 1: name re-apply + log)
            GoatBarnNamePatches.Register(HarmonyInst);       // name re-apply when vanilla regenerates it (upgrade)
            GoatBarnModeButtonPatches.Register(HarmonyInst); // info-window buttons + live title

            Log.Msg($"{LogTag} {DisplayName} v{Version} — loaded. Step 1 toggle + step 2 ItemWool (traders, storage, icon).");
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
