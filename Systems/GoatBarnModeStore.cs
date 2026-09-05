using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// Per-barn animal mode: Goats (vanilla) or Sheep (Live-Stock Market). This is
    /// the state behind the [Goats]/[Sheep] toggle injected on the goat barn info
    /// panel.
    ///
    /// The vanilla GoatBarn has no such field, so — exactly like WotW's
    /// GraveyardModeStore / FishingShackEnhancement.SavedModes — we hold it in a
    /// static, position-keyed dictionary and persist it via a Save/Load postfix.
    /// Position keying survives save/load because the barn's transform is restored
    /// (Building.Load) before our LivestockBuilding.Load postfix runs, and it's the
    /// same identity the buttons and the later behavior patches look it up by.
    ///
    /// Step 1 scope: this store + the toggle UI + persistence only. A Sheep barn
    /// still behaves exactly like a goat barn — later steps branch on GetMode().
    /// </summary>
    public enum GoatBarnMode
    {
        Goats = 0, // vanilla — the barn keeps its goats
        Sheep = 1, // Live-Stock Market — wool flock (step 1: label only)
    }

    public static class GoatBarnModeStore
    {
        public const GoatBarnMode DefaultMode = GoatBarnMode.Goats;

        // Position-keyed so it survives save/load (mirrors GraveyardModeStore).
        private static readonly Dictionary<string, GoatBarnMode> _modes =
            new Dictionary<string, GoatBarnMode>();

        /// <summary>Fires when a barn's mode changes, so injected UI can refresh
        /// without polling. Arg is the GoatBarn component.</summary>
        public static event Action<Component> OnModeChanged;

        private static string BuildKey(Vector3 pos)
        {
            int x = Mathf.RoundToInt(pos.x * 1000f);
            int z = Mathf.RoundToInt(pos.z * 1000f);
            return x + ":" + z;
        }

        /// <summary>Cleared per map load (called from OnSceneWasLoaded); the Load
        /// postfix repopulates from the save.</summary>
        public static void OnMapLoaded()
        {
            _modes.Clear();
        }

        /// <summary>True for a raw int that is a known mode — used to reject
        /// garbage read from a legacy (pre-mod) save.</summary>
        public static bool IsValid(int raw)
            => raw == (int)GoatBarnMode.Goats || raw == (int)GoatBarnMode.Sheep;

        public static GoatBarnMode GetMode(Component barn)
        {
            if (barn == null) return DefaultMode;
            return _modes.TryGetValue(BuildKey(barn.transform.position), out var m) ? m : DefaultMode;
        }

        public static bool IsSheep(Component barn) => GetMode(barn) == GoatBarnMode.Sheep;

        /// <summary>Player-initiated change (from the toggle button). Stores, logs,
        /// and notifies subscribers. A no-op when the mode is already set.</summary>
        public static void SetMode(Component barn, GoatBarnMode mode)
        {
            if (barn == null) return;
            if (GetMode(barn) == mode) return;

            _modes[BuildKey(barn.transform.position)] = mode;
            LiveStockMarketMod.Log?.Msg(
                $"{LiveStockMarketMod.LogTag} Goat barn '{barn.gameObject.name}' mode → {mode}");

            try { OnModeChanged?.Invoke(barn); } catch { }
        }

        /// <summary>Restore path — called from the Load postfix with the deserialized
        /// mode, keyed by the barn's (now-restored) position. Silent: no event, no
        /// log-spam on bulk load.</summary>
        public static void SetSavedModeForPosition(Vector3 pos, GoatBarnMode mode)
        {
            _modes[BuildKey(pos)] = mode;
        }

        /// <summary>Number of barns with a recorded mode (diagnostics).</summary>
        public static int Count => _modes.Count;
    }
}
