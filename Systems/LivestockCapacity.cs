using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// Herd capacity per livestock building type (1.2.0).
    ///
    /// A building's maximum herd size is herdSetupData.numLivestockToBeOverpopulated − 1,
    /// read from shared LivestockHerdSetupData assets (one per animal type and tier:
    /// CowHerdSetupData_T1/_T2, GoatHerdSetupData_T1/_T2, HorseHerdSetupData_T1/_T2,
    /// ChickenHerdSetupData_T1, DogHerdSetupData_T1, CatHerdSetupData_T1). The same number
    /// stops the building window's herd-size slider, marks where the crowding penalty starts,
    /// scales breeding (the crowd factor and the minimum-births space), and limits how many
    /// animals traders deliver. So the multiplier goes on the asset:
    /// maximum = round(vanilla maximum × multiplier), N = maximum + 1.
    ///
    ///   • Which slider applies is decided by the BUILDING type when a building asks, else by
    ///     the asset's name, and only last by its animalType field — the shipped cat asset says
    ///     Horse (found in the 1.2.0 test log, September 25, 2026), so animalType alone put cats
    ///     on the Stable slider.
    ///   • The vanilla N is recorded per asset instance the first time it is seen, and every
    ///     later value is computed from it: moving a slider back restores vanilla exactly and
    ///     nothing compounds. A reloaded asset is a new instance with the vanilla value.
    ///   • LivestockBuilding.Load trims the saved herd size to N − 1, so the assets are scaled
    ///     before any barn loads: a sweep when the Map scene loads, plus prefixes on
    ///     LivestockBuilding.Load and Start (Patches/CapacityPatches).
    ///   • The Sheep and Pig barn setup clones copy the goat asset's N (RegisterClone), so a
    ///     goat barn keeps one maximum in every mode and a switch never pushes a herd over it.
    ///   • Live change: a barn whose herd-size setting sat at the old maximum follows the new
    ///     one; a barn above a lowered maximum comes down to it, and vanilla's setter queues
    ///     the extra animals for the butcher. Settings below the maximum are the player's.
    /// </summary>
    internal static class LivestockCapacity
    {
        internal const float MinMultiplier = 1f;
        internal const float MaxMultiplier = 2f;

        internal enum Kind { Other, CowBarn, GoatBarn, ChickenCoop, Stable, DogKennel, CatKennel }

        private static readonly Dictionary<LivestockHerdSetupData, int> _vanillaN = new Dictionary<LivestockHerdSetupData, int>();
        private static readonly Dictionary<LivestockHerdSetupData, Kind> _kind = new Dictionary<LivestockHerdSetupData, Kind>();
        private static readonly Dictionary<LivestockHerdSetupData, LivestockHerdSetupData> _clones = new Dictionary<LivestockHerdSetupData, LivestockHerdSetupData>();
        private static readonly MethodInfo HerdCountChanged = AccessTools.Method(typeof(Herd), "OnAnimalsInHerdCountChanged", new[] { typeof(int) });

        private static string Tag => LiveStockMarketMod.LogTag;

        // ── settings ─────────────────────────────────────────────────────────
        internal static float Multiplier(Kind kind)
        {
            var e = Entry(kind);
            return e == null ? 1f : Mathf.Clamp(e.Value, MinMultiplier, MaxMultiplier);
        }

        private static MelonPreferences_Entry<float> Entry(Kind kind)
        {
            switch (kind)
            {
                case Kind.CowBarn:     return LiveStockMarketMod.cfgCapacityCowBarn;
                case Kind.GoatBarn:    return LiveStockMarketMod.cfgCapacityGoatBarn;
                case Kind.ChickenCoop: return LiveStockMarketMod.cfgCapacityChickenCoop;
                case Kind.Stable:      return LiveStockMarketMod.cfgCapacityStable;
                case Kind.DogKennel:   return LiveStockMarketMod.cfgCapacityDogKennel;
                case Kind.CatKennel:   return LiveStockMarketMod.cfgCapacityCatKennel;
                default:               return null;
            }
        }

        private static string Name(Kind kind)
        {
            switch (kind)
            {
                case Kind.CowBarn:     return "Cow Barn";
                case Kind.GoatBarn:    return "Goat Barn";
                case Kind.ChickenCoop: return "Chicken Coop";
                case Kind.Stable:      return "Stable";
                case Kind.DogKennel:   return "Dog Kennel";
                case Kind.CatKennel:   return "Cat Kennel";
                default:               return "Other";
            }
        }

        // ── which slider an asset belongs to ────────────────────────────────
        private static Kind KindOfBuilding(LivestockBuilding b)
        {
            if (b == null) return Kind.Other;
            if (b is CatKennel)   return Kind.CatKennel;
            if (b is DogKennel)   return Kind.DogKennel;
            if (b is GoatBarn)    return Kind.GoatBarn;
            if (b is ChickenCoop) return Kind.ChickenCoop;
            if (b is Stable)      return Kind.Stable;
            if (b is Barn)        return Kind.CowBarn;
            return Kind.Other;
        }

        private static Kind KindOfName(string name)
        {
            if (string.IsNullOrEmpty(name)) return Kind.Other;
            if (name.StartsWith("Cat", StringComparison.OrdinalIgnoreCase))     return Kind.CatKennel;
            if (name.StartsWith("Dog", StringComparison.OrdinalIgnoreCase))     return Kind.DogKennel;
            if (name.StartsWith("Goat", StringComparison.OrdinalIgnoreCase))    return Kind.GoatBarn;
            if (name.StartsWith("Chicken", StringComparison.OrdinalIgnoreCase)) return Kind.ChickenCoop;
            if (name.StartsWith("Horse", StringComparison.OrdinalIgnoreCase))   return Kind.Stable;
            if (name.StartsWith("Cow", StringComparison.OrdinalIgnoreCase))     return Kind.CowBarn;
            return Kind.Other;
        }

        private static Kind KindOfAnimal(ItemID animal)
        {
            switch (animal)
            {
                case ItemID.Cow:     return Kind.CowBarn;
                case ItemID.Goat:    return Kind.GoatBarn;
                case ItemID.Chicken: return Kind.ChickenCoop;
                case ItemID.Horse:   return Kind.Stable;
                case ItemID.Dog:     return Kind.DogKennel;
                case ItemID.Cat:     return Kind.CatKennel;
                default:             return Kind.Other;
            }
        }

        /// <summary>Building type first, then the asset's name, then its animalType field.</summary>
        private static Kind KindOf(LivestockHerdSetupData so, LivestockBuilding building)
        {
            var fromBuilding = KindOfBuilding(building);
            if (fromBuilding != Kind.Other)
            {
                if (_kind.TryGetValue(so, out var had) && had != fromBuilding)
                    LiveStockMarketMod.Log.Msg($"{Tag} Capacity: '{so.name}' belongs to a {Name(fromBuilding)}, not the {Name(had)}; using the {Name(fromBuilding)} setting.");
                _kind[so] = fromBuilding;
                return fromBuilding;
            }
            if (_kind.TryGetValue(so, out var known)) return known;
            var kind = KindOfName(so.name);
            if (kind == Kind.Other) kind = KindOfAnimal(so.animalType);
            _kind[so] = kind;
            return kind;
        }

        internal static int ScaledN(int vanillaN, float multiplier)
        {
            int max = Mathf.Max(1, vanillaN - 1);
            int scaled = Mathf.Max(1, Mathf.RoundToInt(max * multiplier));
            return scaled + 1;
        }

        // ── assets ───────────────────────────────────────────────────────────
        /// <summary>An LSM per-barn clone (Sheep or Pig setup) — never scaled on its own.</summary>
        internal static bool IsClone(LivestockHerdSetupData so)
            => so != null && (_clones.ContainsKey(so) || (so.name != null && so.name.Contains("(LSM ")));

        /// <summary>A Sheep or Pig barn's setup clone keeps the goat asset's maximum.</summary>
        public static void RegisterClone(LivestockHerdSetupData clone, LivestockHerdSetupData source)
        {
            if (clone == null || source == null || clone == source) return;
            _clones[clone] = source;
            Ensure(clone);
        }

        /// <summary>
        /// Scales one asset to its building's multiplier. Idempotent; cheap after the first call.
        /// Pass the building when there is one: its type decides the slider.
        /// </summary>
        public static void Ensure(LivestockHerdSetupData so, LivestockBuilding building = null)
        {
            try
            {
                if (so == null) return;
                if (_clones.TryGetValue(so, out var source))
                {
                    if (source == null) return;
                    Ensure(source, building);
                    so.numLivestockToBeOverpopulated = source.numLivestockToBeOverpopulated;
                    return;
                }
                if (IsClone(so)) return;   // an LSM clone left over from an earlier game: not in use

                var kind = KindOf(so, building);
                float mult = Multiplier(kind);
                if (!_vanillaN.TryGetValue(so, out int vanilla))
                {
                    vanilla = so.numLivestockToBeOverpopulated;
                    _vanillaN[so] = vanilla;
                    int max0 = Mathf.Max(1, vanilla - 1);
                    int max1 = ScaledN(vanilla, mult) - 1;
                    string quirk = KindOfAnimal(so.animalType) != kind ? $" (the asset's animal type says {so.animalType})" : "";
                    LiveStockMarketMod.Log.Msg($"{Tag} Capacity: {Name(kind)} herds ('{so.name}') hold {max0} by default{quirk}" +
                        (max1 != max0 ? $"; x{mult:F1} → {max1}." : $" (x{mult:F1})."));
                }
                int target = ScaledN(vanilla, mult);
                if (so.numLivestockToBeOverpopulated != target) so.numLivestockToBeOverpopulated = target;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} LivestockCapacity.Ensure: {ex.Message}");
            }
        }

        /// <summary>Scales every setup asset Unity has loaded. Runs when the Map scene loads, before the save's barns.</summary>
        public static void Sweep()
        {
            try
            {
                foreach (var so in Resources.FindObjectsOfTypeAll<LivestockHerdSetupData>()) Ensure(so);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} LivestockCapacity.Sweep: {ex.Message}");
            }
        }

        public static void OnMapLoaded() => Sweep();

        // ── live change ──────────────────────────────────────────────────────
        public static void ApplyPrefs()
        {
            try
            {
                var buildings = UnityEngine.Object.FindObjectsOfType<LivestockBuilding>();
                var before = new Dictionary<LivestockBuilding, int>();
                foreach (var b in buildings)
                    if (b != null && b.herdSetupData != null) before[b] = b.herdSetupData.numLivestockToBeOverpopulated - 1;
                var assetsBefore = _vanillaN.Keys.Where(k => k != null).ToDictionary(k => k, k => k.numLivestockToBeOverpopulated);

                foreach (var so in _vanillaN.Keys.ToArray()) if (so != null) Ensure(so);
                foreach (var clone in _clones.Keys.ToArray()) if (clone != null) Ensure(clone);
                Sweep();
                foreach (var b in buildings) if (b != null) Ensure(b.herdSetupData, b);

                int followed = 0, trimmed = 0;
                foreach (var kv in before)
                {
                    var b = kv.Key;
                    if (b == null || b.herdSetupData == null) continue;
                    int oldMax = kv.Value;
                    int newMax = b.herdSetupData.numLivestockToBeOverpopulated - 1;
                    if (newMax == oldMax) continue;
                    int setting = b.userDefinedMaxLivestock;
                    bool trim = setting > newMax;
                    bool follow = !trim && setting == oldMax && newMax > oldMax;
                    if (trim || follow)
                    {
                        b.userDefinedMaxLivestock = newMax;   // vanilla setter: queues or releases the auto-slaughter
                        if (trim) trimmed++; else followed++;
                    }
                    RefreshHerd(b.herd);
                }

                bool assetChanged = assetsBefore.Any(kv => kv.Key != null && kv.Key.numLivestockToBeOverpopulated != kv.Value);
                if (!assetChanged && followed == 0 && trimmed == 0) return;   // a slider tick that rounds to the same herd sizes

                // "Goat Barn 20/40": one maximum per asset tier, in tier order.
                var maxima = _vanillaN.Keys.Where(k => k != null && _kind.ContainsKey(k) && _kind[k] != Kind.Other)
                    .GroupBy(k => _kind[k])
                    .OrderBy(g => g.Key)
                    .Select(g => $"{Name(g.Key)} {string.Join("/", g.Select(k => k.numLivestockToBeOverpopulated - 1).Distinct().OrderBy(n => n).Select(n => n.ToString()).ToArray())}")
                    .ToArray();
                LiveStockMarketMod.Log.Msg($"{Tag} Capacity: maximum herd sizes now {string.Join(", ", maxima)}; " +
                    $"{followed} building(s) followed a raised maximum, {trimmed} came down to a lowered one (the butcher takes the extras).");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} LivestockCapacity.ApplyPrefs: {ex}");
            }
        }

        /// <summary>Recomputes the herd's crowding flag now instead of at the next birth, death or month.</summary>
        private static void RefreshHerd(Herd herd)
        {
            try
            {
                if (herd == null || HerdCountChanged == null || herd.animalsInHerdRO == null) return;
                HerdCountChanged.Invoke(herd, new object[] { herd.animalsInHerdRO.Count });
            }
            catch { /* the flag also refreshes on the next count change or month */ }
        }
    }
}
