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
    /// goodsList, so every mod item is listed in traderGoods and gets a
    /// MerchantGoods line cloned from that merchant's line for the item's vanilla
    /// template (or sane defaults). Wool is carried only by the agricultural and
    /// hunter-and-herder merchants (MerchantDefinitionT1_Agricultural / T1_HunterHerder); the garments
    /// appear wherever their vanilla counterparts do. Stock is rolled by vanilla.
    ///
    /// Runs once per map after the managers exist (coroutine from OnSceneWasLoaded);
    /// every step is idempotent.
    /// </summary>
    internal static class WoolTrade
    {
        private const float PollSeconds = 2f;
        private const float GiveUpSeconds = 120f;

        /// <summary>Merchant definition name fragments that carry wool.</summary>
        // MerchantDefinitionT1_Agricultural and MerchantDefinitionT1_HunterHerder (the others:
        // T1_Butcher, T1_MiningBlacksmith, T2_MiningBlacksmith, T2_Luxury).
        private static readonly string[] WoolMerchants = { "Agricultural", "HunterHerder" };

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
                ClearLegacyForcedStock(gm.tradeManager);
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

        private static bool CarriesWool(MerchantDefinition merchant)
        {
            if (merchant == null || string.IsNullOrEmpty(merchant.name)) return false;
            foreach (var key in WoolMerchants)
                if (merchant.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// Adds a line per mod item to each merchant definition that should carry it and
        /// removes wool lines from merchants that should not (a definition can outlive a
        /// map). Returns the number of definitions seen.
        /// </summary>
        public static int InjectMerchantGoods()
        {
            var defs = CollectDefinitions();
            int added = 0, removed = 0;
            var woolSellers = new List<string>();
            foreach (var merchant in defs)
            {
                try
                {
                    if (merchant == null || merchant.goodsList == null) continue;
                    foreach (var def in ModItems.All)
                    {
                        bool wanted = def.Name != WoolItem.ItemName || CarriesWool(merchant);
                        bool present = merchant.goodsList.Exists(g => g != null && g.itemName == def.Name);
                        if (present && !wanted)
                        {
                            removed += merchant.goodsList.RemoveAll(g => g != null && g.itemName == def.Name);
                            continue;
                        }
                        if (def.Name == WoolItem.ItemName && wanted) woolSellers.Add(merchant.name);
                        if (present || !wanted) continue;
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
            if (defs.Count > 0 && (added > 0 || removed > 0 || !_injectedThisMap))
                LiveStockMarketMod.Log.Msg($"{Tag} WoolTrade: {added} merchant lines added, {removed} removed, across {defs.Count} merchant definitions; wool at: {(woolSellers.Count > 0 ? string.Join(", ", woolSellers.ToArray()) : "none")}.");
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

        /// <summary>
        /// Pre-1.0 builds could force wool into every merchant's cargo through
        /// TradeManager.forcedItemNames. Stock is vanilla-random now; drop the entry.
        /// </summary>
        private static void ClearLegacyForcedStock(TradeManager tm)
        {
            try
            {
                var forced = tm != null ? tm.forcedItemNames : null;
                if (forced != null && forced.Remove(WoolItem.ItemName))
                    LiveStockMarketMod.Log.Msg($"{Tag} WoolTrade: removed the pre-1.0 forced wool stock; merchants roll wool like any other good.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolTrade.ClearLegacyForcedStock: {ex.Message}");
            }
        }
    }
}
