using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  ShearingPatches
//
//  Hooks the vanilla harvest loop so Sheep barns yield wool:
//    • LivestockBuilding.milkingItemID (getter postfix): wool for a Sheep-mode
//      GoatBarn. The harvest loop, the capacity bundle, the availability check,
//      the produced-item icons and the yearly production tracking all read it.
//    • SetupLogisticsRequests (postfix, Awake): every goat barn gets a permanent
//      second take-out request, for wool. Vanilla creates its milk request once at
//      Awake, before the mode is known, so instead of rebuilding requests on a
//      switch both products always have one; leftover milk still leaves a Sheep barn.
//    • OnWorkerAdded / OnWorkerRemoved (postfix): the wool request follows the
//      barn's logistics workers exactly like the milk request.
//    • CheckWorkAvailabilityForTransferringMilk (prefix): vanilla drives its milk
//      request from the count of milkingItemID — which is wool in Sheep mode. In
//      Sheep mode the prefix drives both requests itself and skips vanilla; in
//      Goats mode it only adds the wool side (leftover wool) and lets vanilla run.
//    • OnDayPassed (prefix): sheep-days growth and the season-start yield lock,
//      before vanilla queues that day's animals; truffle pigs' daily mushrooms.
//    • Pigs mode: milkingItemID is Mushroom (product line + capacity), a third
//      permanent take-out request carries mushrooms, and SlaughterAnimalInHerd
//      (prefix) adds the pig's bonus meat / tallow / hide.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class ShearingPatches
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        private static readonly MethodInfo _checkToRequest =
            AccessTools.Method(typeof(Resource), "CheckToRequestItemCount");
        private static readonly FieldInfo _milkRequestField =
            AccessTools.Field(typeof(LivestockBuilding), "takeOutMilkingItemRequest");
        private static readonly PropertyInfo _expectingMilkWorkProp =
            AccessTools.Property(typeof(LivestockBuilding), "expectingMilkWork");
        private static readonly PropertyInfo _logisticsRequesterProp =
            AccessTools.Property(typeof(Building), "logisticsRequester");

        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                var getter = AccessTools.PropertyGetter(typeof(LivestockBuilding), "milkingItemID");
                if (getter != null)
                {
                    harmony.Patch(getter, postfix: new HarmonyMethod(typeof(ShearingPatches), nameof(MilkingItemIdPostfix)));
                    LiveStockMarketMod.Log.Msg($"{Tag} ShearingPatches: patched LivestockBuilding.milkingItemID (wool for Sheep barns)");
                }
                else LiveStockMarketMod.Log.Warning($"{Tag} ShearingPatches: milkingItemID getter not found — Sheep barns keep making milk.");

                PatchOne(harmony, "SetupLogisticsRequests", Type.EmptyTypes, prefix: null, postfix: nameof(SetupLogisticsRequestsPostfix), what: "wool take-out request");
                PatchOne(harmony, "OnWorkerAdded", new[] { typeof(IWorker) }, prefix: null, postfix: nameof(OnWorkerAddedPostfix), what: "wool request workers");
                PatchOne(harmony, "OnWorkerRemoved", new[] { typeof(IWorker) }, prefix: null, postfix: nameof(OnWorkerRemovedPostfix), what: "wool request workers");
                PatchOne(harmony, "CheckWorkAvailabilityForTransferringMilk", new[] { typeof(bool) }, prefix: nameof(TransferAvailabilityPrefix), postfix: null, what: "milk + wool take-out");
                PatchOne(harmony, "OnDayPassed", new[] { typeof(DayPassedEvent) }, prefix: nameof(OnDayPassedPrefix), postfix: null, what: "wool growth + truffle pigs");
                PatchOne(harmony, "SlaughterAnimalInHerd", new[] { typeof(LivestockAnimal) }, prefix: nameof(SlaughterPrefix), postfix: null, what: "pig butchering yields");
                // SetLoadedHerd does `herdToSet.herdSetupData = herdSetupData` (the building's
                // field) on every adoption path — tier upgrade (PostRelocate), Start for an
                // abandoned herd, load finalize. A new instance's field is the goat asset, so
                // re-apply the mode right after, or a Sheep barn silently runs on goat numbers.
                PatchOne(harmony, "SetLoadedHerd", new[] { typeof(Herd) }, prefix: null, postfix: nameof(SetLoadedHerdPostfix), what: "setup asset follows the mode on herd adoption");

                if (_checkToRequest == null) LiveStockMarketMod.Log.Warning($"{Tag} ShearingPatches: Resource.CheckToRequestItemCount not found — wool won't be hauled out of barns.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} ShearingPatches.Register: {ex}");
            }
        }

        private static void PatchOne(HarmonyLib.Harmony harmony, string method, Type[] args, string prefix, string postfix, string what)
        {
            try
            {
                var target = AccessTools.Method(typeof(LivestockBuilding), method, args);
                if (target == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} ShearingPatches: LivestockBuilding.{method} not found — {what} disabled.");
                    return;
                }
                harmony.Patch(target,
                    prefix:  prefix  != null ? new HarmonyMethod(typeof(ShearingPatches), prefix)  : null,
                    postfix: postfix != null ? new HarmonyMethod(typeof(ShearingPatches), postfix) : null);
                LiveStockMarketMod.Log.Msg($"{Tag} ShearingPatches: patched {target.DeclaringType.Name}.{method} ({what})");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} ShearingPatches: patching {method} failed: {ex.Message}");
            }
        }

        // ── Product ───────────────────────────────────────────────────────────
        private static void MilkingItemIdPostfix(LivestockBuilding __instance, ref ItemID __result)
        {
            if (!WoolItem.IsRegistered || !(__instance is GoatBarn barn)) return;
            switch (GoatBarnModeStore.GetMode(barn))
            {
                case GoatBarnMode.Sheep: __result = WoolItem.ItemId; break;
                case GoatBarnMode.Pigs:  __result = ItemID.Mushroom; break;   // truffle pigs: the product line, capacity bundle and take-out follow
            }
        }

        // ── Wool take-out request ─────────────────────────────────────────────
        private static void SetupLogisticsRequestsPostfix(LivestockBuilding __instance)
        {
            try
            {
                if (!(__instance is GoatBarn barn) || !WoolItem.IsRegistered) return;
                var state = SheepBarnState.GetOrAdd(barn);
                if (state == null || (state.WoolRequest != null && state.MushroomRequest != null)) return;

                var gm = UnitySingleton<GameManager>.Instance;
                var wbm = gm != null ? gm.workBucketManager : null;
                var requester = _logisticsRequesterProp?.GetValue(barn, null) as LogisticsRequester;
                if (wbm == null || requester == null || barn.storage == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} ShearingPatches: managers not ready for '{barn.name}' — no wool / mushroom take-out requests.");
                    return;
                }
                if (state.WoolRequest == null && wbm.itemByItemIDRO.TryGetValue(WoolItem.ItemId, out var wool))
                    state.WoolRequest = CreateTakeOut(barn, wbm, requester, wool);
                if (state.MushroomRequest == null && wbm.itemMushroom != null)
                    state.MushroomRequest = CreateTakeOut(barn, wbm, requester, wbm.itemMushroom);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} ShearingPatches.SetupLogisticsRequests: {ex.Message}");
            }
        }

        /// <summary>A permanent take-out request for one product, sized by TransferAvailabilityPrefix.</summary>
        private static SingleItemRequest CreateTakeOut(GoatBarn barn, WorkBucketManager wbm, LogisticsRequester requester, Item item)
        {
            var bucket = wbm.GetCanStoreWorkBucketByItem(wbm, item);
            var request = new SingleItemRequest(item.itemID, barn.storage, ItemAction.TakeOut,
                new LogisticsRequestID(RequestTypeIdentifier.SingleItem, item.name), bucket,
                LogisticsRequest.RequestTag.None, 1u);
            request.SetVillagerState(VillagerState.State.StockpilingMilk);
            request.storageExclusionFlags |= StorageFlags.TempStorage;
            requester.AddItemRequest(request);
            return request;
        }

        private static void OnWorkerAddedPostfix(LivestockBuilding __instance, IWorker workerAdded)
        {
            try
            {
                if (!(__instance is GoatBarn barn) || !(workerAdded is ILogisticsWorker worker)) return;
                var state = SheepBarnState.Get(barn);
                var priority = new LogisticsAssignment.AssignmentPriority(VillagerOccupationManufacturer.sharedManufacturingPriorityModifier, RequestPriority.High);
                state?.WoolRequest?.AssignWorker(worker, LogisticsAssignment.AssignmentCategory.Default, priority);
                state?.MushroomRequest?.AssignWorker(worker, LogisticsAssignment.AssignmentCategory.Default, priority);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} ShearingPatches.OnWorkerAdded: {ex.Message}");
            }
        }

        private static void OnWorkerRemovedPostfix(LivestockBuilding __instance, IWorker workerRemoved)
        {
            try
            {
                if (!(__instance is GoatBarn barn) || !(workerRemoved is ILogisticsWorker worker)) return;
                var state = SheepBarnState.Get(barn);
                state?.WoolRequest?.UnassignWorker(worker);
                state?.MushroomRequest?.UnassignWorker(worker);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} ShearingPatches.OnWorkerRemoved: {ex.Message}");
            }
        }

        // ── Take-out availability ─────────────────────────────────────────────
        private static bool TransferAvailabilityPrefix(LivestockBuilding __instance, bool validForWorkAvailability)
        {
            try
            {
                if (!(__instance is GoatBarn barn)) return true;
                var state = SheepBarnState.Get(barn);
                if (state == null || _checkToRequest == null) return true;

                if (!validForWorkAvailability)
                {
                    state.WoolRequest?.ClearAllCounts();
                    state.MushroomRequest?.ClearAllCounts();
                    return true;   // vanilla clears the milk request
                }

                var gm = UnitySingleton<GameManager>.Instance;
                var wbm = gm != null ? gm.workBucketManager : null;
                if (wbm == null || barn.storage == null) return true;
                var mode = GoatBarnModeStore.GetMode(barn);
                bool sheep = mode == GoatBarnMode.Sheep, pigs = mode == GoatBarnMode.Pigs;

                // Wool: in Sheep mode follow vanilla's rule (take out when no more work
                // is expected or the barn is full); in any other mode leftovers leave.
                if (state.WoolRequest != null && wbm.itemByItemIDRO.TryGetValue(WoolItem.ItemId, out var wool))
                {
                    uint woolCount = barn.storage.GetItemCount(wool);
                    uint woolFree = barn.storage.GetNumberOfUnreservedItems(wool);
                    bool should = false;
                    if (woolFree != 0)
                    {
                        if (!sheep) should = true;
                        else
                        {
                            var expecting = _expectingMilkWorkProp?.GetValue(barn, null) as bool?;
                            int cap = (barn.herd != null && barn.herd.herdSetupData != null) ? barn.herd.herdSetupData.milkStorageCapacity : 300;
                            should = (expecting.HasValue && !expecting.Value) || woolCount >= cap;
                        }
                    }
                    CheckToRequest(state.WoolRequest, should, woolFree);
                }

                // Mushrooms: in Pigs mode haul when a stack is worth the trip or the barn is
                // full (they trickle in daily); in any other mode leftovers leave.
                if (state.MushroomRequest != null && wbm.itemMushroom != null)
                {
                    uint count = barn.storage.GetItemCount(wbm.itemMushroom);
                    uint free = barn.storage.GetNumberOfUnreservedItems(wbm.itemMushroom);
                    bool should = free != 0 && (!pigs || free >= 10 || count >= (uint)PigHusbandry.MushroomCapacity);
                    CheckToRequest(state.MushroomRequest, should, free);
                }

                if (!sheep && !pigs) return true;   // milkingItemID is Milk: vanilla handles the milk request

                // Sheep / Pigs mode: vanilla would size the MILK request from the wool or
                // mushroom count. Drive the milk request from the milk count instead, then skip vanilla.
                var milkRequest = _milkRequestField?.GetValue(barn) as ItemRequest;
                if (milkRequest != null && wbm.itemByItemIDRO.TryGetValue(ItemID.Milk, out var milk))
                {
                    uint milkFree = barn.storage.GetNumberOfUnreservedItems(milk);
                    CheckToRequest(milkRequest, milkFree != 0, milkFree);
                }
                return false;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} ShearingPatches.TransferAvailability: {ex.Message}");
                return true;
            }
        }

        private static void CheckToRequest(ItemRequest request, bool should, uint amount)
        {
            _checkToRequest.Invoke(null, new object[] { request, should, amount, RequestPriority.High });
        }

        // ── Growth ────────────────────────────────────────────────────────────
        private static void OnDayPassedPrefix(LivestockBuilding __instance)
        {
            if (__instance is GoatBarn barn) SheepShearing.OnDayPassed(barn);
        }

        // ── Butchering (PREFIX: the animal is still in the herd, the carcass not yet added) ──
        private static void SlaughterPrefix(LivestockBuilding __instance, LivestockAnimal animalToSlaughter)
        {
            if (__instance is GoatBarn barn) PigHusbandry.OnSlaughter(barn, animalToSlaughter);
        }

        // ── Herd adoption ─────────────────────────────────────────────────────
        private static void SetLoadedHerdPostfix(LivestockBuilding __instance)
        {
            if (__instance is GoatBarn barn) SheepShearing.ApplyMode(barn, onSwitch: false);
        }
    }
}
