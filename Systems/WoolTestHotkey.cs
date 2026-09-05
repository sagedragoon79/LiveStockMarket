using System;
using System.Collections.ObjectModel;
using UnityEngine;
using LiveStockMarket.Patches;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// Development aid: Ctrl+Shift + the configured key drops a stack of wool into
    /// the first storage building that accepts it (storehouses first, then storage
    /// depots, then stockyards). Chorded on purpose — bare F-keys collide across the
    /// mod fleet (see the BoatKeys F11 handoff). Set the key to None to disable.
    /// </summary>
    internal static class WoolTestHotkey
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        public static void Tick()
        {
            try
            {
                var entry = LiveStockMarketMod.cfgTestWoolKey;
                if (entry == null) return;
                var key = entry.Value;
                if (key == KeyCode.None) return;

                if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) return;
                if (!(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return;
                if (!Input.GetKeyDown(key)) return;

                // Gate on the live game rather than a scene name: the game loads
                // "Frontier" and then "Map" additively, so scene flags are unreliable.
                if (UnitySingleton<GameManager>.Instance == null)
                {
                    LiveStockMarketMod.Log.Msg($"{Tag} Test hotkey pressed outside a game — ignored.");
                    return;
                }

                int amount = Mathf.Clamp(LiveStockMarketMod.cfgTestWoolAmount?.Value ?? 20, 1, 1000);
                LiveStockMarketMod.Log.Msg($"{Tag} Test hotkey pressed (Ctrl+Shift+{key}) — adding {amount} wool.");
                AddWool(amount);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolTestHotkey: {ex.Message}");
            }
        }

        private static void AddWool(int amount)
        {
            if (!WoolItem.IsRegistered)
            {
                LiveStockMarketMod.Log.Msg($"{Tag} Test hotkey: wool isn't registered — nothing added.");
                return;
            }

            var gm = UnitySingleton<GameManager>.Instance;
            var rm = gm != null ? gm.resourceManager : null;
            if (rm == null)
            {
                LiveStockMarketMod.Log.Msg($"{Tag} Test hotkey: no resource manager (not in a game?).");
                return;
            }

            StorageBuilding target = FirstAccepting(rm.storehousesRO)
                                  ?? FirstAccepting(rm.storageDepotsRO)
                                  ?? FirstAccepting(rm.stockyardsRO);
            if (target == null)
            {
                LiveStockMarketMod.Log.Msg($"{Tag} Test hotkey: no storage building accepts wool — build a Storehouse first.");
                return;
            }

            var bundle = new ItemBundle(WoolItem.NewItem(), (uint)amount, 100u);
            uint added = target.storage.AddItems(bundle);
            LiveStockMarketMod.Log.Msg(
                $"{Tag} Test hotkey: added {added}/{amount} wool to '{target.displayName}'" +
                (added < (uint)amount ? " (storage full)" : "") + ".");
        }

        private static StorageBuilding FirstAccepting<T>(ReadOnlyCollection<T> buildings) where T : StorageBuilding
        {
            if (buildings == null) return null;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b != null && b.storage != null && WoolItemPatches.AllowsWool(b)) return b;
            }
            return null;
        }
    }
}
