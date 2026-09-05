using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  GarmentPatches — the garments as wearables.
//
//  Villagers seek exactly three clothing items (hide coat, shoes, linen
//  clothes), and every check that matters — the warmth math in VillagerHealth
//  (ExposureClothingBonus / CalculateShoeBonus), the "should seek" predicates,
//  TooColdSeekShelter, the disease factors, the UI alerts — is an inventory
//  query for those items on the villager's permanent inventory. So the
//  substitution happens there, at one choke point, restricted to villager
//  inventories:
//    • GetItemCount(Item / ItemID): a query for a vanilla clothing item that finds
//      none reports the villager's garment instead.
//    • GetTotalPercentIntact(Item): the garment's intact fraction, scaled by the
//      warmth multiplier — the garment is the vanilla item at 125% effectiveness.
//  Villager inventories are registered from the VillagerItemRequester
//  constructor and dropped in Cleanup.
//
//  Seeking: each villager gets three garment requests mirroring the vanilla
//  ones (same Deliver-to-inventory shape, same "should seek" predicate, High
//  priority). While a garment is in stock, the matching vanilla request is
//  demoted to Low, so villagers take the garment first and fall back to the
//  vanilla item when there is none. A villager wearing the vanilla item keeps
//  it until it wears out; no hoarding both.
//
//  Cosmetic gap: the villager window's equipment icons show the vanilla item's
//  icon for a worn garment. Left for the visuals step.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class GarmentPatches
    {
        private sealed class WinterSeek
        {
            public Villager Villager;
            public LogisticsRequester Requester;
            public SingleItemRequest Boots, Cloak, Clothes;
        }

        private static readonly HashSet<ItemStorage> _villagerInventories = new HashSet<ItemStorage>();
        private static readonly ConditionalWeakTable<VillagerItemRequester, WinterSeek> _seeks = new ConditionalWeakTable<VillagerItemRequester, WinterSeek>();
        private static readonly Dictionary<ItemID, Item> _garmentItems = new Dictionary<ItemID, Item>();

        private static readonly PropertyInfo _villagerProp      = AccessTools.Property(typeof(VillagerItemRequester), "villager");
        private static readonly PropertyInfo _requesterProp     = AccessTools.Property(typeof(VillagerItemRequester), "logisticsRequester");
        private static readonly PropertyInfo _coatEligibleProp  = AccessTools.Property(typeof(VillagerItemRequester), "seekCoatRequestEligible");
        private static readonly PropertyInfo _seekShoesProp     = AccessTools.Property(typeof(VillagerItemRequester), "seekShoesRequest");
        private static readonly PropertyInfo _seekCoatProp      = AccessTools.Property(typeof(VillagerItemRequester), "seekCoatRequest");
        private static readonly PropertyInfo _seekLinenProp     = AccessTools.Property(typeof(VillagerItemRequester), "seekLinenClothesRequest");

        private static string Tag => LiveStockMarketMod.LogTag;

        public static void Register(HarmonyLib.Harmony harmony)
        {
            // Inventory substitution.
            PatchMethod(harmony, typeof(ItemStorage), "GetItemCount", new[] { typeof(Item) }, nameof(GetItemCountPostfix), "garment counts as its vanilla item");
            PatchMethod(harmony, typeof(ItemStorage), "GetItemCount", new[] { typeof(ItemID) }, nameof(GetItemCountByIdPostfix), "garment counts as its vanilla item (by id)");
            PatchMethod(harmony, typeof(ItemStorage), "GetTotalPercentIntact", new[] { typeof(Item) }, nameof(GetTotalPercentIntactPostfix), "garment warmth");

            // Seeking.
            try
            {
                var ctor = AccessTools.Constructor(typeof(VillagerItemRequester), new[] { typeof(Villager), typeof(LogisticsRequester) });
                if (ctor != null)
                {
                    harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(GarmentPatches), nameof(RequesterCtorPostfix)));
                    LiveStockMarketMod.Log.Msg($"{Tag} GarmentPatches: patched VillagerItemRequester ctor (garment seek requests)");
                }
                else LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches: VillagerItemRequester ctor not found — villagers won't seek garments.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches: ctor patch failed: {ex.Message}");
            }
            PatchMethod(harmony, typeof(VillagerItemRequester), "Cleanup", Type.EmptyTypes, nameof(CleanupPostfix), "garment seek teardown");
            PatchMethod(harmony, typeof(VillagerItemRequester), "OnPermanentInventoryChanged", Type.EmptyTypes, nameof(RefreshPostfix), "garment seek refresh");
            PatchMethod(harmony, typeof(VillagerItemRequester), "OnExposureChanged", new[] { typeof(float) }, nameof(RefreshPostfix), "garment seek refresh");
            PatchMethod(harmony, typeof(VillagerItemRequester), "OnVillagerClothingSeekingTempChanged", new[] { typeof(VillagerClothingSeekingTempChangedEvent) }, nameof(RefreshPostfix), "garment seek refresh");
            PatchMethod(harmony, typeof(VillagerItemRequester), "CheckIfSeekCoatRequestEligible", Type.EmptyTypes, nameof(RefreshPostfix), "garment seek refresh");
            PatchMethod(harmony, typeof(VillagerItemRequester), "OnOccupationChanged", new[] { typeof(VillagerOccupation.Occupation) }, nameof(RefreshPostfix), "garment seek refresh");
            PatchMethod(harmony, typeof(VillagerItemRequester), "UpdateShoesRequest", Type.EmptyTypes, nameof(DemoteShoesPostfix), "prefer boots");
            PatchMethod(harmony, typeof(VillagerItemRequester), "UpdateCoatRequest", Type.EmptyTypes, nameof(DemoteCoatPostfix), "prefer cloak");
            PatchMethod(harmony, typeof(VillagerItemRequester), "UpdateLinenClothesRequest", Type.EmptyTypes, nameof(DemoteLinenPostfix), "prefer woolen clothes");
        }

        private static void PatchMethod(HarmonyLib.Harmony harmony, Type type, string method, Type[] args, string postfix, string what)
        {
            try
            {
                var target = AccessTools.Method(type, method, args);
                if (target == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches: {type.Name}.{method} not found — {what} disabled.");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(GarmentPatches), postfix));
                LiveStockMarketMod.Log.Msg($"{Tag} GarmentPatches: patched {type.Name}.{method} ({what})");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches: patching {type.Name}.{method} failed: {ex.Message}");
            }
        }

        public static void OnMapLoaded()
        {
            _villagerInventories.Clear();
        }

        // ── Inventory substitution (villager inventories only) ────────────────
        private static void GetItemCountPostfix(ItemStorage __instance, Item item, ref uint __result)
        {
            if (__result != 0 || item == null) return;
            var garment = GarmentItems.ForVanilla(item.itemID);
            if (garment == null || !_villagerInventories.Contains(__instance)) return;
            __result = __instance.GetItemCount(garment.Id);   // the ItemID overload's postfix ignores garment ids
        }

        private static void GetItemCountByIdPostfix(ItemStorage __instance, ItemID itemID, ref uint __result)
        {
            if (__result != 0) return;
            var garment = GarmentItems.ForVanilla(itemID);
            if (garment == null || !_villagerInventories.Contains(__instance)) return;
            __result = __instance.GetItemCount(garment.Id);
        }

        private static void GetTotalPercentIntactPostfix(ItemStorage __instance, Item item, ref float __result)
        {
            if (item == null) return;
            var garment = GarmentItems.ForVanilla(item.itemID);
            if (garment == null || !_villagerInventories.Contains(__instance)) return;
            var garmentItem = GarmentItem(garment);
            if (garmentItem == null) return;
            float intact = __instance.GetTotalPercentIntact(garmentItem);   // re-entrant call: garment id → early out
            if (intact <= 0f) return;
            __result = Mathf.Max(__result, intact * GarmentItems.WarmthMultiplier);
        }

        private static Item GarmentItem(ModItemDef garment)
        {
            if (!_garmentItems.TryGetValue(garment.Id, out var item) || item == null)
            {
                if (!ModItems.IsRegistered) return null;
                item = garment.NewItem();
                _garmentItems[garment.Id] = item;
            }
            return item;
        }

        // ── Seeking ───────────────────────────────────────────────────────────
        private static void RequesterCtorPostfix(VillagerItemRequester __instance)
        {
            try
            {
                var villager = _villagerProp?.GetValue(__instance, null) as Villager;
                var requester = _requesterProp?.GetValue(__instance, null) as LogisticsRequester;
                if (villager == null || villager.permanentInventory == null || requester == null) return;
                _villagerInventories.Add(villager.permanentInventory);

                if (!ModItems.IsRegistered) return;
                var gm = UnitySingleton<GameManager>.Instance;
                var wbm = gm != null ? gm.workBucketManager : null;
                if (wbm == null) return;

                var seek = new WinterSeek { Villager = villager, Requester = requester };
                seek.Boots   = MakeRequest(wbm, villager, GarmentItems.WinterBoots);
                seek.Cloak   = MakeRequest(wbm, villager, GarmentItems.WinterCloak);
                seek.Clothes = MakeRequest(wbm, villager, GarmentItems.WoolenClothes);
                foreach (var r in new[] { seek.Boots, seek.Cloak, seek.Clothes })
                    if (r != null) requester.AddItemRequest(r);

                _seeks.Remove(__instance);
                _seeks.Add(__instance, seek);
                UpdateWinterRequests(__instance);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches.RequesterCtor: {ex.Message}");
            }
        }

        private static SingleItemRequest MakeRequest(WorkBucketManager wbm, Villager villager, ModItemDef garment)
        {
            if (!wbm.itemByItemIDRO.TryGetValue(garment.Id, out var item)) return null;
            var bucket = wbm.GetHasItemWorkBucketByItem(wbm, item);
            var request = new SingleItemRequest(garment.Id, villager.permanentInventory, ItemAction.Deliver,
                new LogisticsRequestID(RequestTypeIdentifier.SingleItem, item.name), bucket);
            request.SetVillagerState(VillagerState.State.SeekingClothes);
            return request;
        }

        private static void CleanupPostfix(VillagerItemRequester __instance)
        {
            try
            {
                var villager = _villagerProp?.GetValue(__instance, null) as Villager;
                if (villager != null && villager.permanentInventory != null)
                    _villagerInventories.Remove(villager.permanentInventory);
                if (_seeks.TryGetValue(__instance, out var seek))
                {
                    foreach (var r in new[] { seek.Boots, seek.Cloak, seek.Clothes })
                    {
                        if (r == null) continue;
                        try { r.ClearAllCounts(); seek.Requester?.RemoveItemRequest(r); } catch { }
                    }
                    _seeks.Remove(__instance);
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches.Cleanup: {ex.Message}");
            }
        }

        private static void RefreshPostfix(VillagerItemRequester __instance)
        {
            UpdateWinterRequests(__instance);
        }

        private static void UpdateWinterRequests(VillagerItemRequester requester)
        {
            try
            {
                if (!_seeks.TryGetValue(requester, out var seek) || seek.Villager == null) return;
                bool coatEligible = _coatEligibleProp != null && (bool)_coatEligibleProp.GetValue(requester, null);
                Set(seek.Boots,   seek.Villager.ShouldSeekShoes());
                Set(seek.Clothes, seek.Villager.ShouldSeekLinenClothes());
                Set(seek.Cloak,   coatEligible && seek.Villager.ShouldSeekCoat());
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches.UpdateWinterRequests: {ex.Message}");
            }
        }

        private static void Set(SingleItemRequest request, bool should)
        {
            if (request == null) return;
            if (should) request.SetSinglePriorityCount(1u, RequestPriority.High);
            else request.ClearAllCounts();
        }

        // ── Prefer the garment while it is in stock ───────────────────────────
        private static void DemoteShoesPostfix(VillagerItemRequester __instance)
            => Demote(__instance, _seekShoesProp, GarmentItems.WinterBoots, v => v.ShouldSeekShoes(), wbm => wbm.itemShoes);

        private static void DemoteLinenPostfix(VillagerItemRequester __instance)
            => Demote(__instance, _seekLinenProp, GarmentItems.WoolenClothes, v => v.ShouldSeekLinenClothes(), wbm => wbm.itemLinenClothes);

        private static void DemoteCoatPostfix(VillagerItemRequester __instance)
            => Demote(__instance, _seekCoatProp, GarmentItems.WinterCloak,
                v => v.ShouldSeekCoat(), wbm => wbm.itemHideCoat);

        private static void Demote(VillagerItemRequester requester, PropertyInfo requestProp, ModItemDef garment,
            Func<Villager, bool> shouldSeek, Func<WorkBucketManager, Item> vanillaItem)
        {
            try
            {
                if (!ModItems.IsRegistered) return;
                var villager = _villagerProp?.GetValue(requester, null) as Villager;
                var vanilla = requestProp?.GetValue(requester, null) as SingleItemRequest;
                if (villager == null || vanilla == null || !shouldSeek(villager)) return;

                var gm = UnitySingleton<GameManager>.Instance;
                var rm = gm != null ? gm.resourceManager : null;
                var wbm = gm != null ? gm.workBucketManager : null;
                if (rm == null || wbm == null) return;
                var info = rm.GetItemInfo(garment.Name);
                if (info == null || info.unusedCount == 0) return;   // no garment in the settlement: vanilla stays High

                var key = vanillaItem(wbm);
                uint qty = (key != null && Villager.clothingQty.TryGetValue(key, out var q)) ? q : 1u;
                vanilla.SetSinglePriorityCount(qty, RequestPriority.Low);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches.Demote: {ex.Message}");
            }
        }
    }
}
