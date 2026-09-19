using System;
using HarmonyLib;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  TradingPostPatches (1.1.0 fix)
//
//  "Keep in stock" at the Trading Center lives in TradingPost.keepInStockDict
//  (item name → bool). Vanilla seeds the keys ONCE, in TradingPost.Awake, from
//  TradeManager.traderGoods, and both accessors ignore a name without a key:
//  SetKeepInStock silently does nothing, GetKeepInStock returns false.
//
//  The mod lists its items in traderGoods from a coroutine that polls after the
//  map loads (WoolTrade.InjectWhenReady), which is after every Trading Center in
//  the save has run Awake. So wool and the garments had no key: the checkbox
//  looked ticked until the window was rebuilt, then read back unticked, and the
//  behavior behind it (hold the target stock after a sale instead of lowering it
//  by the amount sold) never applied. Target stock counts were unaffected — that
//  dictionary adds keys on demand.
//
//  • Awake (postfix): seed a false key for every mod item.
//  • SetKeepInStock (prefix): add the key on demand for a mod item, in case a
//    post ever exists without it.
//  Save writes the names whose value is true and Load adds them back with or
//  without a key, so ticks persist through vanilla's own code.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class TradingPostPatches
    {
        private static string Tag => LiveStockMarketMod.LogTag;
        private static bool _loggedSeed;

        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                int patched = 0;
                var awake = AccessTools.Method(typeof(TradingPost), "Awake");
                if (awake != null)
                {
                    harmony.Patch(awake, postfix: new HarmonyMethod(typeof(TradingPostPatches), nameof(AwakePostfix)));
                    patched++;
                }
                var set = AccessTools.Method(typeof(TradingPost), "SetKeepInStock", new[] { typeof(Item), typeof(bool) });
                if (set != null)
                {
                    harmony.Patch(set, prefix: new HarmonyMethod(typeof(TradingPostPatches), nameof(SetKeepInStockPrefix)));
                    patched++;
                }
                if (patched == 2)
                    LiveStockMarketMod.Log.Msg($"{Tag} TradingPostPatches: patched TradingPost.Awake + SetKeepInStock (keep-in-stock entries for mod items)");
                else
                    LiveStockMarketMod.Log.Warning($"{Tag} TradingPostPatches: only {patched}/2 methods patched — Keep in Stock may not hold for mod items.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} TradingPostPatches.Register: {ex}");
            }
        }

        private static void AwakePostfix(TradingPost __instance)
        {
            try
            {
                int added = Seed(__instance);
                if (added > 0 && !_loggedSeed)
                {
                    _loggedSeed = true;
                    LiveStockMarketMod.Log.Msg($"{Tag} TradingPostPatches: seeded {added} keep-in-stock entr{(added == 1 ? "y" : "ies")} on a Trading Center (logged once).");
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} TradingPostPatches.Awake: {ex.Message}");
            }
        }

        private static void SetKeepInStockPrefix(TradingPost __instance, Item item)
        {
            try
            {
                if (__instance == null || item == null || __instance.keepInStockDict == null) return;
                if (ModItems.Get(item.name) == null) return;
                if (!__instance.keepInStockDict.ContainsKey(item.name)) __instance.keepInStockDict.Add(item.name, false);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} TradingPostPatches.SetKeepInStock: {ex.Message}");
            }
        }

        /// <summary>Adds a false entry for every mod item the post does not know yet. Returns how many.</summary>
        internal static int Seed(TradingPost post)
        {
            if (post == null || post.keepInStockDict == null) return 0;
            int added = 0;
            foreach (var def in ModItems.All)
            {
                if (def == null || string.IsNullOrEmpty(def.Name) || post.keepInStockDict.ContainsKey(def.Name)) continue;
                post.keepInStockDict.Add(def.Name, false);
                added++;
            }
            return added;
        }
    }
}
