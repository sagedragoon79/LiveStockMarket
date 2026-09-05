using System;
using System.Reflection;
using MelonLoader;
using LiveStockMarket.Patches;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  Live-Stock Market  v0.1.3
//  A wool production chain for Farthest Frontier, built in steps. By SageDragoon.
//
//  Step 1 (this build): a Goats / Sheep mode toggle on the vanilla goat barn
//  (GoatBarn and GoatBarn_Tier2 — the same GoatBarn class at tier 1 / tier 2).
//    • Mode store: static, position-keyed (Systems/GoatBarnModeStore.cs)
//    • UI: Goats / Sheep buttons overlaid on the barn portrait in its info window
//      (Patches/GoatBarnModeButtonPatches.cs); position + size are live prefs
//    • Name: a Sheep barn is called "Sheep Barn" / "Large Sheep Barn" everywhere
//      (Systems/GoatBarnNaming.cs + Patches/GoatBarnNamePatches.cs); the blurb
//      stays vanilla until step 2
//    • Persistence: Save/Load postfixes append the mode to the barn's own ES2
//      stream inside the normal save file (Patches/GoatBarnSaveLoadPatches.cs)
//    • Load-phase hook: OnGameFinishedLoadingFinalize postfix — step 1 only
//      logs; later steps apply mode-driven data here (Patches/GoatBarnLoadPatches.cs)
//  No behavior change yet: a Sheep barn still runs vanilla goats.
//
//  Verified in-game (September 4, 2026): click flips the highlight, mode
//  survives save → reload, and survives the tier 1 → tier 2 upgrade.
//
//  Plan of record: HANDOFF.md (repo root).
//  Pattern reference: FF-Modding-Knowledge/mod-patterns/building-work-modes.md
// ─────────────────────────────────────────────────────────────────────────────

[assembly: MelonInfo(typeof(LiveStockMarket.LiveStockMarketMod), "Live-Stock Market", "0.1.3", "SageDragoon")]
[assembly: MelonGame("Crate Entertainment", "Farthest Frontier")]

namespace LiveStockMarket
{
    public class LiveStockMarketMod : MelonMod
    {
        internal const string DisplayName = "Live-Stock Market";
        internal const string Version     = "0.1.3";
        internal const string HarmonyId   = "com.sagedragoon.livestockmarket";
        internal const string LogTag      = "[LSM]";

        internal static readonly BindingFlags AllInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        internal static LiveStockMarketMod Instance { get; private set; }
        internal static MelonLogger.Instance Log => Instance?.LoggerInstance;
        internal static HarmonyLib.Harmony HarmonyInst { get; private set; }

        // ── Config ──────────────────────────────────────────────────────────
        // Category identifier == Keep Clarity modId ("LiveStockMarket") so KC's
        // dedup lines up (keep-clarity-mod-manager-api.md, "Mod ID conventions").
        internal static MelonPreferences_Category    cfgCategory;
        internal static MelonPreferences_Entry<bool> cfgModEnabled;

        // Button placement on the barn portrait. Live: read on every injection,
        // and a change rebuilds any open row, so the pair can be nudged from the
        // F10 panel while the barn window is open. Remove once the layout settles.
        internal static MelonPreferences_Entry<float> cfgButtonPosX;    // 0..1 across the portrait (0 = left)
        internal static MelonPreferences_Entry<float> cfgButtonPosY;    // 0..1 up the portrait (0 = bottom)
        internal static MelonPreferences_Entry<int>   cfgButtonWidth;   // UI units, per button
        internal static MelonPreferences_Entry<int>   cfgButtonHeight;  // UI units

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

            // ── Step 1: Goats / Sheep mode toggle on the goat barn ───────────
            GoatBarnNaming.Register();                       // "Sheep Barn" name follows the mode (subscribes first)
            GoatBarnSaveLoadPatches.Register(HarmonyInst);   // persistence — never feature-gated (see file header)
            GoatBarnLoadPatches.Register(HarmonyInst);       // load-phase hook (step 1: name re-apply + log)
            GoatBarnNamePatches.Register(HarmonyInst);       // name re-apply when vanilla regenerates it (upgrade)
            GoatBarnModeButtonPatches.Register(HarmonyInst); // info-window buttons + live title

            Log.Msg($"{LogTag} {DisplayName} v{Version} — loaded. " +
                    "Step 1: Goats / Sheep toggle on GoatBarn / GoatBarn_Tier2 (mode only, no behavior change).");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (sceneName != "Map") return;

            // Per-map reset. The Load postfix repopulates the store from the save
            // (load runs after the Map scene is up), so nothing stale survives
            // between games.
            GoatBarnModeStore.OnMapLoaded();
        }
    }
}
