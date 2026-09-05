using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// Per-barn runtime state for shearing. Lives on the GoatBarn's GameObject so it
    /// dies with the building. Created in the SetupLogisticsRequests postfix (Awake),
    /// so it exists before Load restores SheepDays from the barn payload.
    /// </summary>
    internal sealed class SheepBarnState : MonoBehaviour
    {
        internal static readonly List<SheepBarnState> Live = new List<SheepBarnState>();

        public GoatBarn Barn;
        /// <summary>The shared vanilla goat asset, captured before any clone is assigned.</summary>
        public LivestockHerdSetupData GoatAsset;
        /// <summary>This barn's clone of the goat asset carrying the shearing numbers.</summary>
        public LivestockHerdSetupData SheepSetup;
        /// <summary>Permanent take-out request for wool (every goat barn has one).</summary>
        public SingleItemRequest WoolRequest;
        /// <summary>Days spent in Sheep mode since growth last restarted (capped at growth days). Saved.</summary>
        public int SheepDays;

        public static SheepBarnState Get(GoatBarn barn) => barn != null ? barn.GetComponent<SheepBarnState>() : null;

        public static SheepBarnState GetOrAdd(GoatBarn barn)
        {
            if (barn == null) return null;
            var state = barn.GetComponent<SheepBarnState>();
            if (state == null)
            {
                state = barn.gameObject.AddComponent<SheepBarnState>();
                state.Barn = barn;
            }
            return state;
        }

        private void Awake()
        {
            if (!Live.Contains(this)) Live.Add(this);
        }

        private void OnDestroy()
        {
            // The clone is deliberately NOT destroyed: during a tier upgrade or
            // relocation the abandoned herd still references it until the new
            // instance adopts the herd. A few hundred bytes per destroyed barn.
            Live.Remove(this);
        }
    }

    /// <summary>
    /// Shearing = the vanilla harvest loop yielding wool for Sheep barns.
    ///
    /// The loop (LivestockBuilding.UpdateMilking) reads its product through the
    /// virtual milkingItemID property and every timing number through the herd's
    /// setup asset. All goat barns share one asset, so a Sheep barn gets its own
    /// clone of it (same guid — saves still resolve to the vanilla asset) with the
    /// shearing numbers from the prefs, assigned to both the building field (which
    /// gates UpdateMilking) and the Herd (which the loop reads).
    ///
    /// Wool is grown, not toggled. Each barn counts days spent in Sheep mode; the
    /// clone's per-animal yield is `wool per sheep × sheep-days ÷ growth days`,
    /// recomputed daily, and growth restarts the day after the shearing season
    /// ends. So a goat barn flipped to Sheep at the season's start has no
    /// sheep-days and shears nothing, and every day spent as sheep is a day with no
    /// milk — nothing is double-counted. The per-sheep cooldown (default 300 days)
    /// keeps it to one shearing per animal per year.
    ///
    /// On a switch, animals mid-session lose the rest of that session and take the
    /// new mode's cooldown; everyone else's cooldown is clamped to the new mode's
    /// value, so a shorn sheep turned goat isn't locked out of milking for a year.
    /// </summary>
    internal static class SheepShearing
    {
        private const int DefaultCapacity = 300;

        private static string Tag => LiveStockMarketMod.LogTag;

        // ── Prefs (clamped) ───────────────────────────────────────────────────
        internal static int   SeasonStart    => Mathf.Clamp(LiveStockMarketMod.cfgShearSeasonStartDay?.Value ?? 78, 1, 365);
        internal static int   SeasonEnd      => Mathf.Clamp(LiveStockMarketMod.cfgShearSeasonEndDay?.Value ?? 200, SeasonStart, 365);
        internal static float CooldownDays   => Mathf.Clamp(LiveStockMarketMod.cfgShearCooldownDays?.Value ?? 300, 1, 400);
        internal static int   WoolPerSheep   => Mathf.Clamp(LiveStockMarketMod.cfgWoolPerSheep?.Value ?? 4, 0, 50);
        internal static float SecondsPerUnit => Mathf.Clamp(LiveStockMarketMod.cfgWoolSecondsPerUnit?.Value ?? 10f, 1f, 600f);
        internal static int   GrowthDays     => Mathf.Clamp(LiveStockMarketMod.cfgWoolGrowthDays?.Value ?? 240, 1, 730);
        internal static int   BarnCapacity   => Mathf.Clamp(LiveStockMarketMod.cfgSheepBarnWoolCapacity?.Value ?? DefaultCapacity, 10, 5000);

        /// <summary>The day of year on which growth restarts: the day after the season ends.</summary>
        internal static int GrowthResetDay => (SeasonEnd % 365) + 1;

        public static void Register()
        {
            GoatBarnModeStore.OnModeChanged += OnModeChanged;
        }

        private static void OnModeChanged(Component changed)
        {
            if (changed is GoatBarn barn) ApplyMode(barn, onSwitch: true);
        }

        // ── Mode application ─────────────────────────────────────────────────
        /// <summary>
        /// Points the barn (field + herd) at the right setup asset for its mode and
        /// rebuilds the product capacity bundle. onSwitch adds the switch rules:
        /// animal cooldown clamp and producer registration.
        /// </summary>
        public static void ApplyMode(GoatBarn barn, bool onSwitch)
        {
            try
            {
                if (barn == null || !WoolItem.IsRegistered) return;
                var state = SheepBarnState.GetOrAdd(barn);
                if (state == null) return;

                if (state.GoatAsset == null && barn.herdSetupData != null && barn.herdSetupData != state.SheepSetup)
                    state.GoatAsset = barn.herdSetupData;
                if (state.GoatAsset == null) return;

                bool sheep = GoatBarnModeStore.IsSheep(barn);
                var target = sheep ? EnsureSheepSetup(state) : state.GoatAsset;
                if (target == null) return;

                barn.herdSetupData = target;
                if (barn.herd != null)
                {
                    barn.herd.herdSetupData = target;
                    barn.herd = barn.herd;   // re-runs the setter: capacity bundle for the current product
                }
                UpdateSetupYield(state);
                SheepVisuals.ApplyToBarn(barn);   // step 4: the look follows the mode on every path (toggle, load, upgrade, adoption)

                if (onSwitch)
                {
                    ClampAnimals(barn, target.milkingSessionCooldownInDays);
                    RefreshProducerRegistration(barn, sheep);
                    LiveStockMarketMod.Log.Msg(
                        $"{Tag} Shearing: '{barn.displayName}' → {(sheep ? "Sheep" : "Goats")} " +
                        $"(sheep-days banked {state.SheepDays}/{GrowthDays}, fleece now {CurrentYield(state)} per sheep).");
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepShearing.ApplyMode: {ex}");
            }
        }

        private static LivestockHerdSetupData EnsureSheepSetup(SheepBarnState state)
        {
            if (state.SheepSetup != null) return state.SheepSetup;
            if (state.GoatAsset == null) return null;
            var clone = UnityEngine.Object.Instantiate(state.GoatAsset);   // same guid string: saves resolve to the goat asset
            clone.name = state.GoatAsset.name + " (LSM Sheep)";
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            state.SheepSetup = clone;
            ApplyPrefsTo(state);
            return clone;
        }

        private static void ApplyPrefsTo(SheepBarnState state)
        {
            var s = state.SheepSetup;
            if (s == null) return;
            s.enableMilking = true;
            s.milkingSeasonStartDay = SeasonStart;
            s.milkingSeasonEndDay = SeasonEnd;
            s.milkingSessionCooldownInDays = CooldownDays;
            s.timePerUnitOfMilk = SecondsPerUnit;
            s.milkStorageCapacity = BarnCapacity;
            UpdateSetupYield(state);
        }

        /// <summary>Wool per sheep for the barn's current growth: proportional, capped at a full fleece.</summary>
        internal static int CurrentYield(SheepBarnState state)
            => Mathf.RoundToInt(WoolPerSheep * Mathf.Clamp01(state.SheepDays / (float)GrowthDays));

        private static void UpdateSetupYield(SheepBarnState state)
        {
            if (state.SheepSetup != null)
                state.SheepSetup.numMilkPerAnimalPerSession = Mathf.Max(0, CurrentYield(state));
        }

        /// <summary>Live pref change: re-apply to every Sheep barn's clone and rebuild capacities.</summary>
        public static void OnPrefsChanged()
        {
            try
            {
                var live = SheepBarnState.Live.ToArray();
                foreach (var state in live)
                {
                    if (state == null || state.SheepSetup == null || state.Barn == null) continue;
                    ApplyPrefsTo(state);
                    if (GoatBarnModeStore.IsSheep(state.Barn) && state.Barn.herd != null)
                        state.Barn.herd = state.Barn.herd;   // capacity bundle follows the new cap
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepShearing.OnPrefsChanged: {ex.Message}");
            }
        }

        // ── Daily growth (PREFIX on OnDayPassed, so today's fleece value is in
        //    place before vanilla queues animals) ───────────────────────────
        public static void OnDayPassed(GoatBarn barn)
        {
            try
            {
                var state = SheepBarnState.Get(barn);
                if (state == null) return;

                int day = DayOfYear();
                if (day == GrowthResetDay && state.SheepDays > 0)
                {
                    LiveStockMarketMod.Log.Msg($"{Tag} Shearing: '{barn.displayName}' season over — fleece growth restarts ({state.SheepDays} sheep-days spent).");
                    state.SheepDays = 0;
                }
                if (GoatBarnModeStore.IsSheep(barn))
                    state.SheepDays = Mathf.Min(state.SheepDays + 1, GrowthDays);

                UpdateSetupYield(state);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepShearing.OnDayPassed: {ex.Message}");
            }
        }

        // ── Save / load ───────────────────────────────────────────────────────
        public static int GetSheepDays(GoatBarn barn)
        {
            var state = SheepBarnState.Get(barn);
            return state != null ? state.SheepDays : 0;
        }

        public static void RestoreSheepDays(GoatBarn barn, int sheepDays)
        {
            var state = SheepBarnState.GetOrAdd(barn);
            if (state == null) return;
            state.SheepDays = Mathf.Max(0, sheepDays);
            UpdateSetupYield(state);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static int DayOfYear()
        {
            try
            {
                var gm = UnitySingleton<GameManager>.Instance;
                var tm = gm != null ? gm.timeManager : null;
                return tm != null ? tm.currentDate.dayOfYear : 0;
            }
            catch { return 0; }
        }

        private static readonly FieldInfo _queueField = AccessTools.Field(typeof(LivestockBuilding), "milkableLivestockQueue");

        private static void ClampAnimals(GoatBarn barn, float newCooldown)
        {
            var herd = barn.herd;
            if (herd == null || herd.animalsInHerdRO == null) return;
            int ended = 0, clamped = 0;
            foreach (var animal in herd.animalsInHerdRO)
            {
                if (animal == null) continue;
                if (animal.milkUnitsRemaining > 0)
                {
                    // Mid-session: the rest of the session is lost and the new mode's
                    // cooldown starts now (never a free re-queue).
                    animal.milkUnitsRemaining = 0;
                    animal.timeTowardsMilkUnit = 0f;
                    animal.milkingCooldownDays = newCooldown;
                    ended++;
                }
                else if (animal.milkingCooldownDays > newCooldown)
                {
                    animal.milkingCooldownDays = newCooldown;
                    clamped++;
                }
            }
            try { (_queueField?.GetValue(barn) as List<LivestockAnimal>)?.Clear(); } catch { }
            if (ended > 0 || clamped > 0)
                LiveStockMarketMod.Log.Msg($"{Tag} Shearing: '{barn.displayName}' switch — {ended} session(s) ended, {clamped} cooldown(s) clamped to {newCooldown:F0} days.");
        }

        private static void RefreshProducerRegistration(GoatBarn barn, bool sheep)
        {
            try
            {
                var gm = UnitySingleton<GameManager>.Instance;
                var rm = gm != null ? gm.resourceManager : null;
                var wbm = gm != null ? gm.workBucketManager : null;
                if (rm == null || wbm == null) return;
                string milkName = wbm.itemByItemIDRO.TryGetValue(ItemID.Milk, out var milk) ? milk.name : "ItemMilk";
                rm.AddOrRemoveBuildingFromManufacturerDict(milkName, barn, remove: sheep);
                rm.AddOrRemoveBuildingFromManufacturerDict(WoolItem.ItemName, barn, remove: !sheep);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepShearing.RefreshProducerRegistration: {ex.Message}");
            }
        }
    }
}
