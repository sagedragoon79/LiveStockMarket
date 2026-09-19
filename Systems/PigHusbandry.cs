using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// Pigs mode rules on a goat barn (all numbers are prefs):
    ///   • No harvest loop: the pig setup clone has enableMilking off. The barn's
    ///     product is mushrooms (milkingItemID = Mushroom in Pigs mode) so the product
    ///     line, capacity bundle and take-out request all work.
    ///   • Truffle pigs: every day outside winter, each grown pig adds
    ///     MushroomsPerPigPerDay × woodland to a fractional accumulator, where woodland
    ///     is the number of TreeResource objects inside the herd's grazing rect divided
    ///     by TreesForFullYield (capped at 1). Whole mushrooms go into the barn's
    ///     storage up to MushroomCapacity; no herder time.
    ///   • Butchering: a pig yields the goat carcass as usual plus a bonus added straight
    ///     to the barn's output: (multiplier − 1) × the carcass recipe's amount, per
    ///     product (meat, tallow, hide).
    ///   • Breeding: the goat's breeding curves scaled by BreedingMultiplier.
    ///   • Waste: the goat's poop interval shortened by WasteMultiplier (30 days → 20 at
    ///     1.5x); the per-animal batch scales only when the interval can't express the
    ///     value. Vanilla's daily block then does the work: every animal in the herd,
    ///     capped at maxPoops, tallied in the production stats.
    /// </summary>
    internal static class PigHusbandry
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        internal static float MeatMult     => Mathf.Clamp(LiveStockMarketMod.cfgPigMeatMultiplier?.Value ?? 2f, 0.1f, 10f);
        internal static float TallowMult   => Mathf.Clamp(LiveStockMarketMod.cfgPigTallowMultiplier?.Value ?? 2f, 0.1f, 10f);
        internal static float HideMult     => Mathf.Clamp(LiveStockMarketMod.cfgPigHideMultiplier?.Value ?? 1.5f, 0.1f, 10f);
        internal static float BreedingMult => Mathf.Clamp(LiveStockMarketMod.cfgPigBreedingMultiplier?.Value ?? 2f, 0.1f, 10f);
        internal static float WasteMult    => Mathf.Clamp(LiveStockMarketMod.cfgPigWasteMultiplier?.Value ?? 1.5f, 0.1f, 10f);
        internal static float MushroomsPerPigPerDay => Mathf.Clamp(LiveStockMarketMod.cfgPigMushroomsPerPigPerDay?.Value ?? 0.05f, 0f, 5f);
        internal static int   TreesForFullYield     => Mathf.Clamp(LiveStockMarketMod.cfgPigMushroomTreesForFullYield?.Value ?? 12, 1, 200);
        internal static int   MushroomCapacity      => Mathf.Clamp(LiveStockMarketMod.cfgPigMushroomCapacity?.Value ?? 100, 10, 5000);

        private const int SpringStart = 78;    // TimeManager.FIRST_DAY_OF_SPRING
        private const int WinterStart = 355;   // TimeManager.FIRST_DAY_OF_WINTER

        private static readonly FieldInfo  ManuDefByStatus = AccessTools.Field(typeof(LivestockBuilding), "manuDefByStatus");
        private static readonly MethodInfo GrazingRect     = AccessTools.Method(typeof(Herd), "GetGrazingAreaRect");
        private static bool _loggedMissing;

        // ── setup clone ──────────────────────────────────────────────────────
        public static void ApplyPrefsTo(LivestockHerdSetupData pig, LivestockHerdSetupData goat)
        {
            if (pig == null || goat == null) return;
            pig.enableMilking = false;                       // no herder harvest; mushrooms arrive by themselves
            pig.milkStorageCapacity = MushroomCapacity;      // the product capacity bundle reads this
            pig.chanceToBreedByStatus = ScaleCurve(goat.chanceToBreedByStatus, BreedingMult, clamp01: true);
            pig.minimumBirthsByAdultCount = ScaleCurve(goat.minimumBirthsByAdultCount, BreedingMult, clamp01: false);

            // Waste rate = batch / interval. Shorten the interval first (exact for 1.25, 1.5,
            // 2, 3, 5...), then scale the batch when a one-day interval still falls short.
            int goatInterval = Mathf.Max(1, goat.poopIntervalInDays);
            int goatBatch    = Mathf.Max(1, goat.numPoopsPerCowPerInterval);
            int interval     = Mathf.Max(1, Mathf.RoundToInt(goatInterval / WasteMult));
            int batch        = Mathf.Max(1, Mathf.RoundToInt(WasteMult * goatBatch * interval / goatInterval));
            pig.poopIntervalInDays = interval;
            pig.numPoopsPerCowPerInterval = batch;
        }

        private static AnimationCurve ScaleCurve(AnimationCurve src, float mult, bool clamp01)
        {
            if (src == null) return null;
            var keys = src.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                float v = keys[i].value * mult;
                keys[i].value = clamp01 ? Mathf.Clamp01(v) : v;
                keys[i].inTangent *= mult;
                keys[i].outTangent *= mult;
            }
            var curve = new AnimationCurve(keys) { preWrapMode = src.preWrapMode, postWrapMode = src.postWrapMode };
            return curve;
        }

        // ── truffle pigs ─────────────────────────────────────────────────────
        public static bool InSeason(int dayOfYear) => dayOfYear >= SpringStart && dayOfYear < WinterStart;

        public static void OnDayPassed(GoatBarn barn, SheepBarnState state)
        {
            try
            {
                if (barn == null || state == null || !GoatBarnModeStore.IsPigs(barn)) return;
                int day = SheepShearing.DayOfYear();
                if (!InSeason(day))
                {
                    state.MushroomAccumulator = 0f;
                    if (!state.LoggedTruffleWinter)
                    {
                        state.LoggedTruffleWinter = true;
                        LiveStockMarketMod.Log.Msg($"{Tag} Truffle pigs: '{barn.displayName}' — winter (day {day}), no foraging until day {SpringStart}.");
                    }
                    return;
                }
                state.LoggedTruffleWinter = false;
                var herd = barn.herd;
                if (herd == null || herd.animalsInHerdRO == null || barn.storage == null) return;

                int pigs = 0;
                foreach (var a in herd.animalsInHerdRO) if (a != null && a.isFullyGrown) pigs++;
                if (pigs == 0) return;

                int trees = CountTrees(herd);
                float woodland = Mathf.Clamp01(trees / (float)TreesForFullYield);
                float perDay = pigs * MushroomsPerPigPerDay * woodland;
                if (trees != state.LastTreesLogged)
                {
                    // Once per barn, and again whenever the tree count changes (grazing area moved).
                    state.LastTreesLogged = trees;
                    string outlook = perDay > 0f
                        ? $"about one every {Mathf.CeilToInt(1f / perDay)} day(s)"
                        : (trees == 0 ? "none until the grazing area covers trees" : "none");
                    LiveStockMarketMod.Log.Msg($"{Tag} Truffle pigs: '{barn.displayName}' ({barn.name}) has {pigs} grown pig(s) and {trees} tree(s) in its grazing area ({woodland:P0} of the {TreesForFullYield} for full yield) → {perDay:F2} mushroom(s)/day, {outlook}.");
                }
                state.MushroomAccumulator += perDay;
                int n = Mathf.FloorToInt(state.MushroomAccumulator);
                if (n <= 0) return;

                var gm = UnitySingleton<GameManager>.Instance;
                var mushroom = gm != null && gm.workBucketManager != null ? gm.workBucketManager.itemMushroom : null;
                if (mushroom == null) return;
                int room = MushroomCapacity - (int)barn.storage.GetItemCount(mushroom);
                if (room <= 0) return;
                n = Mathf.Min(n, room);
                state.MushroomAccumulator -= n;
                uint added = barn.storage.AddItems(new ItemBundle(mushroom, (uint)n, 100u));
                if (!state.LoggedTruffles)
                {
                    state.LoggedTruffles = true;
                    LiveStockMarketMod.Log.Msg($"{Tag} Truffle pigs: first delivery at '{barn.displayName}' ({barn.name}): +{added} mushroom(s) (the barn holds up to {MushroomCapacity}).");
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} PigHusbandry.OnDayPassed: {ex.Message}");
            }
        }

        private static int CountTrees(Herd herd)
        {
            try
            {
                if (GrazingRect == null) { LogMissing("Herd.GetGrazingAreaRect not found"); return 0; }
                var gm = UnitySingleton<GameManager>.Instance;
                var tm = gm != null ? gm.terrainManager : null;
                if (tm == null) return 0;
                var rect = (Rect)GrazingRect.Invoke(herd, null);
                if (rect.width <= 0f || rect.height <= 0f) return 0;
                var objs = tm.GetObjectsInRect("TreeResource", rect);
                return objs != null ? objs.Count : 0;
            }
            catch (Exception ex)
            {
                LogMissing("tree count failed: " + ex.Message);
                return 0;
            }
        }

        // ── butchering (prefix on SlaughterAnimalInHerd, before vanilla adds the carcass) ──
        public static void OnSlaughter(GoatBarn barn, LivestockAnimal animal)
        {
            try
            {
                if (barn == null || animal == null || !GoatBarnModeStore.IsPigs(barn)) return;
                var herd = barn.herd;
                if (herd == null || !animal.isFullyGrown || herd.animalsInHerdRO == null || !herd.animalsInHerdRO.Contains(animal)) return;
                PigSounds.PlaySqueal(animal.transform.position);   // its last sound; the object is destroyed by vanilla right after
                var byStatus = ManuDefByStatus?.GetValue(barn) as Dictionary<LivestockHealthStatus, ManufactureDefinition>;
                if (byStatus == null) { LogMissing("manuDefByStatus not found"); return; }
                if (!byStatus.TryGetValue(herd.healthStatusIndicator, out var def) || def == null || def.producedItems == null || barn.storage == null) return;

                var parts = new List<string>();
                foreach (var p in def.producedItems)
                {
                    if (p == null || p.item == null) continue;
                    float mult = MultiplierFor(p.item.itemID);
                    int bonus = Mathf.RoundToInt((mult - 1f) * p.numItemsProduced);
                    if (bonus <= 0) continue;
                    uint added = barn.storage.AddItems(new ItemBundle(p.item, (uint)bonus, 100u));
                    parts.Add($"+{added} {p.itemName}");
                }
                if (parts.Count > 0)
                    LiveStockMarketMod.Log.Msg($"{Tag} Pig butchered at '{barn.displayName}' ({herd.healthStatusIndicator}): {string.Join(", ", parts.ToArray())} on top of the carcass.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} PigHusbandry.OnSlaughter: {ex.Message}");
            }
        }

        private static float MultiplierFor(ItemID id)
        {
            if (id == ItemID.Meat)   return MeatMult;
            if (id == ItemID.Tallow) return TallowMult;
            if (id == ItemID.Hide)   return HideMult;
            return 1f;
        }

        private static void LogMissing(string what)
        {
            if (_loggedMissing) return;
            _loggedMissing = true;
            LiveStockMarketMod.Log.Warning($"{Tag} PigHusbandry: {what}.");
        }
    }
}
