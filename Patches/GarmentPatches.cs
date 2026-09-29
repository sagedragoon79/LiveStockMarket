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
//  priority). Both stay High and the villager takes whichever it reaches
//  first. Whichever arrives satisfies the "should seek" check for both (the
//  count substitution above), so the other request clears; no hoarding both.
//
//  1.2.0 fix — never lower the vanilla request. Up to 1.1.0 the vanilla
//  request was set to Low while any garment was in stock, meant as a fallback.
//  It was a block: a villager serves its own requests through
//  LogisticsRequester.AssignWorker(this), i.e. AssignmentPriority.Default with a
//  High threshold, and ItemRequest.IsActiveForRequestPriority(High) is false
//  for a Low request. "In stock" was ItemInfo.unusedCount, which is the plain
//  stored count, so one garment anywhere, claimed or out of reach, left every
//  villager and soldier without shoes, linen clothes or a hide coat.
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
        private static readonly PropertyInfo _seekLinenProp     = AccessTools.Property(typeof(VillagerItemRequester), "seekLinenClothesRequest");
        private static readonly PropertyInfo _seekCoatProp      = AccessTools.Property(typeof(VillagerItemRequester), "seekCoatRequest");
        private static readonly MethodInfo   _setRequestTag     = AccessTools.PropertySetter(typeof(LogisticsRequest), "requestTag");

        /// <summary>
        /// Clothing errands are urgent (1.2.0). Every clothing request — the vanilla shoes,
        /// linen clothes and hide coat requests and the three garment requests — carries this
        /// tag, and every villager gets a priority override for it, the same mechanism and
        /// value vanilla uses for an archer's missing bow or arrows
        /// (Villager.PeformArcherItemCheck, RequestPriorities(120)). The override replaces the
        /// request's normal priority 0 in the logistics search, so a clothing trip outranks
        /// ordinary hauling and stocking and passes a posted soldier's 120 threshold;
        /// checkPriorityOverridesWhenQueued keeps the boost while the errand waits to start.
        /// Vanilla's RequestTag enum ends at 9; this value is ours.
        /// </summary>
        internal const LogisticsRequest.RequestTag ClothingTag = (LogisticsRequest.RequestTag)19539;
        internal const int UrgentPriority = 120;

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
            // No patches on UpdateShoesRequest / UpdateCoatRequest / UpdateLinenClothesRequest:
            // the vanilla requests keep their High priority (see the 1.2.0 fix in the header).
            if (_setRequestTag == null)
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches: LogisticsRequest.requestTag setter not found — clothing errands keep normal priority.");

            // Guarantees for the two garment effects that matter most, computed in the callers
            // rather than trusting the ItemStorage.GetTotalPercentIntact postfix to be reached:
            // the shoe speed bonus (VillagerHealth.CalculateHealthAndShoeBonus runs from
            // InvokeRepeating by name, so it is never inlined) and clothing warmth.
            PatchMethod(harmony, typeof(VillagerHealth), "CalculateHealthAndShoeBonus", Type.EmptyTypes, nameof(ShoeBonusPostfix), "Winter Boots speed bonus");
            PatchMethod(harmony, typeof(VillagerHealth), "ExposureClothingBonus", Type.EmptyTypes, nameof(ExposureClothingBonusPostfix), "garment warmth, recomputed");

            // Diagnostic: clicking a villager logs its clothing state. Hooked on Villager.OnSelected,
            // which every selection path reaches (world click, list, portrait). The first two
            // attempts hooked EffectsAssetMap.OnEvent(ObjectSelected) — villagers do not go
            // through it — and then the VillagerSelectedMale/FemaleEvent constructors, which are
            // tiny enough for Mono to inline into Input_SelectVillager.OnEnter, so the patch never ran.
            try
            {
                var onSelected = AccessTools.Method(typeof(Villager), "OnSelected", Type.EmptyTypes);
                if (onSelected != null)
                {
                    harmony.Patch(onSelected, postfix: new HarmonyMethod(typeof(GarmentPatches), nameof(VillagerSelectedPostfix)));
                    LiveStockMarketMod.Log.Msg($"{Tag} GarmentPatches: clothing check on villager click ready (Villager.OnSelected).");
                }
                else LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches: Villager.OnSelected not found — no clothing check on click.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches: clothing check not patched: {ex.Message}");
            }
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

        // ── Speed and warmth, recomputed in the callers ───────────────────────
        private static readonly FieldInfo _coatBonus  = AccessTools.Field(typeof(VillagerHealth), "hideCoatExposureBonus");
        private static readonly FieldInfo _linenBonus = AccessTools.Field(typeof(VillagerHealth), "linenClothesExposureBonus");
        private static readonly MethodInfo _exposureClothingBonus = AccessTools.Method(typeof(VillagerHealth), "ExposureClothingBonus", Type.EmptyTypes);

        /// <summary>The better of the vanilla item's intact fraction and the garment's, the garment scaled by the warmth multiplier.</summary>
        private static float Effective(ItemStorage inventory, Item vanilla, ModItemDef garment)
        {
            float best = vanilla != null ? inventory.GetTotalPercentIntact(vanilla) : 0f;   // may already include the garment
            var g = GarmentItem(garment);
            if (g != null) best = Mathf.Max(best, inventory.GetTotalPercentIntact(g) * GarmentItems.WarmthMultiplier);
            return best;
        }

        private static void ShoeBonusPostfix(VillagerHealth __instance)
        {
            try
            {
                var villager = __instance != null ? __instance.villager : null;
                if (villager == null || villager.isDead || villager.permanentInventory == null) return;
                var gm = UnitySingleton<GameManager>.Instance;
                var wbm = gm != null ? gm.workBucketManager : null;
                if (wbm == null) return;
                villager.curShoeBonus = villager.shoeBonusBase * Effective(villager.permanentInventory, wbm.itemShoes, GarmentItems.WinterBoots);
            }
            catch { /* vanilla's value stands */ }
        }

        private static void ExposureClothingBonusPostfix(VillagerHealth __instance, ref float __result)
        {
            try
            {
                var villager = __instance != null ? __instance.villager : null;
                if (villager == null || villager.permanentInventory == null || _coatBonus == null || _linenBonus == null) return;
                var gm = UnitySingleton<GameManager>.Instance;
                var wbm = gm != null ? gm.workBucketManager : null;
                if (wbm == null) return;
                var inv = villager.permanentInventory;
                __result = (float)_coatBonus.GetValue(__instance) * Effective(inv, wbm.itemHideCoat, GarmentItems.WinterCloak)
                         + (float)_linenBonus.GetValue(__instance) * Effective(inv, wbm.itemLinenClothes, GarmentItems.WoolenClothes);
            }
            catch { /* vanilla's value stands */ }
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

                // Clothing errands are urgent (see ClothingTag). Independent of the mod items.
                MarkUrgent(_seekShoesProp?.GetValue(__instance, null) as LogisticsRequest);
                MarkUrgent(_seekLinenProp?.GetValue(__instance, null) as LogisticsRequest);
                MarkUrgent(_seekCoatProp?.GetValue(__instance, null) as LogisticsRequest);
                if (_setRequestTag != null && villager.defaultCategoryRequestPrioritiesOverridesByTag != null)
                    villager.defaultCategoryRequestPrioritiesOverridesByTag[ClothingTag] = new RequestPriorities(UrgentPriority);

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
            MarkUrgent(request);
            return request;
        }

        private static bool _loggedUrgentFailure;

        private static void MarkUrgent(LogisticsRequest request)
        {
            if (request == null || _setRequestTag == null) return;
            try
            {
                _setRequestTag.Invoke(request, new object[] { ClothingTag });
                request.checkPriorityOverridesWhenQueued = true;
            }
            catch (Exception ex)
            {
                if (_loggedUrgentFailure) return;
                _loggedUrgentFailure = true;
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches: could not mark a clothing request urgent: {ex.Message}");
            }
        }

        // ── Diagnostic: click a villager ─────────────────────────────────────
        private static Villager _lastChecked;
        private static float _lastCheckedAt;

        private static void VillagerSelectedPostfix(Villager __instance)
        {
            try
            {
                var villager = __instance;
                if (villager == null || villager.villagerHealth == null || villager.itemRequester == null) return;
                if (villager == _lastChecked && Time.unscaledTime - _lastCheckedAt < 1f) return;   // one line per click
                _lastChecked = villager; _lastCheckedAt = Time.unscaledTime;
                bool noShoes = villager.ShouldSeekShoes(), noClothes = villager.ShouldSeekLinenClothes();
                if (!noShoes && !noClothes) return;   // dressed: nothing to report (the test builds logged every click)
                string inventory = DescribeInventory(villager.permanentInventory);
                string effects = DescribeEffects(villager);

                var gm = UnitySingleton<GameManager>.Instance;
                var rm = gm != null ? gm.resourceManager : null;
                string Stock(string itemName) { var info = rm != null ? rm.GetItemInfo(itemName) : null; return info != null ? info.count.ToString() : "?"; }
                string Describe(object request)
                {
                    if (!(request is ItemRequest r)) return "none";
                    uint n = r.GetTotalRequestedCount();
                    return n == 0 ? "not requested"
                        : $"{n} at {r.lastSingleRequestPriority}{(r.isActive ? "" : " (inactive)")}, reserved {r.GetTotalReservedCount()}, {(r.requestTag == ClothingTag ? "urgent" : "normal")}";
                }

                var soldier = villager.occupation as VillagerOccupationSoldier;
                bool boosted = villager.defaultCategoryRequestPrioritiesOverridesByTag != null && villager.defaultCategoryRequestPrioritiesOverridesByTag.ContainsKey(ClothingTag);
                string state = villager.state != null ? villager.state.GetState().ToString() : "?";
                LiveStockMarketMod.Log.Msg($"{Tag} Clothing check: '{villager.villagerName}' ({villager.GetOccupation()}, {state}" +
                    (soldier != null ? $", posted at a combat flag: {(soldier.focusedOnCombatFlag ? "yes" : "no")}" : "") + ") — " +
                    (noShoes ? $"no shoes: request {Describe(_seekShoesProp?.GetValue(villager.itemRequester, null))}, settlement stock {Stock("ItemShoes")}; " : "") +
                    (noClothes ? $"no clothes: request {Describe(_seekLinenProp?.GetValue(villager.itemRequester, null))}, settlement stock {Stock("ItemLinenClothes")}; " : "") +
                    $"urgent boost {(boosted ? "on" : "OFF")}; {effects}. Inventory: {inventory}.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GarmentPatches.VillagerSelected: {ex.Message}");
            }
        }

        /// <summary>The shoe speed bonus the villager has now and the clothing warmth the game computes.</summary>
        private static string DescribeEffects(Villager villager)
        {
            try
            {
                string warmth = "?";
                if (_exposureClothingBonus != null && villager.villagerHealth != null)
                    warmth = ((float)_exposureClothingBonus.Invoke(villager.villagerHealth, null)).ToString("F3");
                return $"speed bonus {villager.curShoeBonus:F3} (plain shoes give {villager.shoeBonusBase:F3}), clothing warmth {warmth}";
            }
            catch (Exception ex)
            {
                return "effects unreadable: " + ex.Message;
            }
        }

        /// <summary>Every bundle in the inventory as "name ×count (intact N)", read directly — no count substitution involved.</summary>
        private static string DescribeInventory(ItemStorage inventory)
        {
            try
            {
                var all = inventory != null ? inventory.GetCopyOfAllItems() : null;
                if (all == null || all.Count == 0) return "empty";
                var parts = new List<string>();
                foreach (var b in all)
                    if (b != null && b.numberOfItems > 0)
                        parts.Add($"{b.name} ×{b.numberOfItems} (intact {b.totalPercentIntact:F1})");
                return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : "empty";
            }
            catch (Exception ex)
            {
                return "unreadable: " + ex.Message;
            }
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
    }
}
