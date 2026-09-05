using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// Mod items at the trading post.
    ///
    /// The trading post window builds its rows from TradeManager.traderGoods, a
    /// master list of tradeable item names; an item missing from it gets no row
    /// at all. TradeManager stocks merchants from each MerchantDefinition's
    /// goodsList, and its forcedItemNames hook only works for items some goods
    /// list already carries. So every mod item is listed in traderGoods and gets
    /// a MerchantGoods line in every merchant definition, cloned from that
    /// merchant's line for the item's vanilla template (or sane defaults). Wool
    /// is additionally forced into every merchant's cargo while the "traders
    /// always stock wool" pref is on — the only wool source until shearing.
    ///
    /// Runs once per map after the managers exist (coroutine from OnSceneWasLoaded),
    /// and again on demand; every step is idempotent.
    /// </summary>
    internal static class WoolTrade
    {
        private const float PollSeconds = 2f;
        private const float GiveUpSeconds = 120f;

        private static bool _injectedThisMap;
        private static object _coroutine;

        private static string Tag => LiveStockMarketMod.LogTag;

        public static void OnMapLoaded()
        {
            _injectedThisMap = false;
            if (_coroutine != null)
            {
                try { MelonCoroutines.Stop(_coroutine); } catch { }
                _coroutine = null;
            }
            _coroutine = MelonCoroutines.Start(InjectWhenReady());
        }

        private static IEnumerator InjectWhenReady()
        {
            float waited = 0f;
            while (waited < GiveUpSeconds)
            {
                yield return new WaitForSeconds(PollSeconds);
                waited += PollSeconds;

                if (!ModItems.IsRegistered) continue;
                var gm = UnitySingleton<GameManager>.Instance;
                if (gm == null || gm.tradeManager == null) continue;

                EnsureListedAtTradingPost(gm.tradeManager);
                int defs = InjectMerchantGoods();
                ApplyForcedStock(LiveStockMarketMod.cfgTradersAlwaysStockWool.Value);
                if (defs > 0)
                {
                    _injectedThisMap = true;
                    _coroutine = null;
                    yield break;
                }
            }
            LiveStockMarketMod.Log.Msg($"{Tag} WoolTrade: no merchant definitions found after {GiveUpSeconds:F0}s — mod items won't appear at traders until a trading post exists (retry on next map load).");
            _coroutine = null;
        }

        /// <summary>Lists every mod item in the trading post's goods (per map; idempotent).</summary>
        public static void EnsureListedAtTradingPost(TradeManager tm)
        {
            try
            {
                if (tm == null || tm.traderGoods == null) return;
                int added = 0;
                foreach (var def in ModItems.All)
                {
                    if (tm.traderGoods.Contains(def.Name)) continue;
                    tm.traderGoods.Add(def.Name);
                    added++;
                }
                if (added > 0)
                    LiveStockMarketMod.Log.Msg($"{Tag} WoolTrade: {added} mod items listed in the trading post's goods ({tm.traderGoods.Count} goods).");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolTrade.EnsureListedAtTradingPost: {ex.Message}");
            }
        }

        /// <summary>Adds a line per mod item to every merchant definition that lacks one. Returns definitions seen.</summary>
        public static int InjectMerchantGoods()
        {
            var defs = CollectDefinitions();
            int added = 0;
            foreach (var merchant in defs)
            {
                try
                {
                    if (merchant == null || merchant.goodsList == null) continue;
                    foreach (var def in ModItems.All)
                    {
                        if (merchant.goodsList.Exists(g => g != null && g.itemName == def.Name)) continue;
                        var template = merchant.goodsList.Find(g => g != null && g.itemName == def.TemplateName);
                        merchant.goodsList.Add(new MerchantGoods
                        {
                            itemName = def.Name,
                            sellProbability = template != null ? template.sellProbability : 1,
                            buyProbability  = template != null ? template.buyProbability  : 1,
                            minItems = template != null ? template.minItems : 10,
                            maxItems = template != null ? template.maxItems : 40,
                            sellPriceMultiplierMin = template != null ? template.sellPriceMultiplierMin : 1.0f,
                            sellPriceMultiplierMax = template != null ? template.sellPriceMultiplierMax : 1.5f,
                        });
                        added++;
                    }
                }
                catch (Exception ex)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolTrade: merchant '{(merchant != null ? merchant.name : "?")}': {ex.Message}");
                }
            }
            if (defs.Count > 0 && (added > 0 || !_injectedThisMap))
                LiveStockMarketMod.Log.Msg($"{Tag} WoolTrade: {added} merchant lines added across {defs.Count} merchant definitions.");
            return defs.Count;
        }

        private static List<MerchantDefinition> CollectDefinitions()
        {
            var result = new List<MerchantDefinition>();
            try
            {
                foreach (var d in Resources.FindObjectsOfTypeAll<MerchantDefinition>())
                    if (d != null && !result.Contains(d)) result.Add(d);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolTrade: FindObjectsOfTypeAll failed: {ex.Message}");
            }
            try
            {
                var rm = UnitySingleton<GameManager>.Instance?.resourceManager;
                if (rm != null && rm.tradingPostsRO != null && rm.tradingPostsRO.Count > 0)
                {
                    var wagons = rm.tradingPostsRO[0].tradeWagonPrefabs;
                    if (wagons != null)
                        foreach (var w in wagons)
                            if (w != null && w.merchantDefinition != null && !result.Contains(w.merchantDefinition))
                                result.Add(w.merchantDefinition);
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolTrade: trading-post lookup failed: {ex.Message}");
            }
            return result;
        }

        /// <summary>Adds/removes wool from TradeManager.forcedItemNames. Live-safe.</summary>
        public static void ApplyForcedStock(bool force)
        {
            try
            {
                var tm = UnitySingleton<GameManager>.Instance?.tradeManager;
                var forced = tm != null ? tm.forcedItemNames : null;
                if (forced == null) return;
                bool has = forced.Contains(WoolItem.ItemName);
                if (force && !has) { forced.Add(WoolItem.ItemName); LiveStockMarketMod.Log.Msg($"{Tag} WoolTrade: traders will always stock wool."); }
                else if (!force && has) { forced.Remove(WoolItem.ItemName); LiveStockMarketMod.Log.Msg($"{Tag} WoolTrade: wool back to random merchant stock."); }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolTrade.ApplyForcedStock: {ex.Message}");
            }
        }
    }
}
