using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  WoolStoragePatches
//
//  "Allow a mod item by default on saves that predate it."
//
//  A storage building's per-item filter (userDefinedAllowableItems) is saved
//  as the list of ALLOWED items, and on load vanilla auto-allows items newer
//  than the save through its save-version item table. A mod item isn't in that
//  table, so a save that predates it comes back with the item unticked in every
//  storehouse — indistinguishable from a player who unticked it on purpose —
//  and nothing can store it: the Settlement Items window shows "Storage Limit
//  Reached" and the item piles up in its producer. Same fix as vanilla,
//  mod-side: append a marker to the building's ES2 stream listing the mod
//  items the save KNEW about. On load, a mod item missing from that list is
//  newer than the save → allow it; one in the list was the player's call.
//
//  Payload, appended right after vanilla's StorageBuilding fields:
//      int  PayloadMagic    'LSMS'
//      int  PayloadVersion  2
//      int  count           mod items known at save time
//      int  itemID × count
//  Version 1 (v0.2.4 – v0.4.1) carried no list and is read as "knows wool
//  only", so a v1 save auto-allows the garments once and the next save writes
//  v2. v0.4.0 shipped the garments against the v1 marker, which is how they
//  came back unticked everywhere (found and fixed September 5, 2026, v0.4.2).
//
//  WRITE only for "end-of-object" types: runtime types with no Save/Load
//  override between them and StorageBuilding (Storehouse, Stockyard,
//  StorageDepot, Granary, RootCellar, Treasury, Market, vanilla-class mod
//  clones). For those the marker sits at the true end of the object's tagged
//  data, so a save loaded WITHOUT the mod just never reads it. A type that
//  overrides Save/Load (TradingPost: base first, then its own fields) would
//  get the marker mid-stream, where vanilla can't skip it after an uninstall —
//  so those types never get one (v0.2.4+).
//
//  READ with a peek, for every storage building: remember the stream position,
//  read one int, and put the position back unless it is the magic. That keeps
//  a legacy over-read from shifting anything, and still consumes the mid-stream
//  markers that v0.2.3 wrote for the Trading Center (they disappear on the next
//  save). A save that predates an item therefore needs it ticked once by hand
//  ONLY on non-writer types (the Trading Center); writer types auto-allow.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class WoolStoragePatches
    {
        internal const int PayloadMagic   = 0x4C534D53; // 'LSMS'
        internal const int PayloadVersion = 2;

        private static readonly Dictionary<Type, bool> _writerTypeCache = new Dictionary<Type, bool>();

        private static string Tag => LiveStockMarketMod.LogTag;

        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                var save = AccessTools.Method(typeof(StorageBuilding), "Save", new[] { typeof(ES2Writer) });
                var load = AccessTools.Method(typeof(StorageBuilding), "Load", new[] { typeof(ES2Reader) });
                if (save == null || load == null || save.DeclaringType != typeof(StorageBuilding) || load.DeclaringType != typeof(StorageBuilding))
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolStoragePatches: StorageBuilding.Save/Load not found as expected — old saves will need mod items ticked manually.");
                    return;
                }
                harmony.Patch(save, postfix: new HarmonyMethod(typeof(WoolStoragePatches), nameof(SavePostfix)));
                harmony.Patch(load, postfix: new HarmonyMethod(typeof(WoolStoragePatches), nameof(LoadPostfix)));
                LiveStockMarketMod.Log.Msg($"{Tag} WoolStoragePatches: patched StorageBuilding.Save/Load (known-items filter marker v{PayloadVersion}, end-of-object types only)");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolStoragePatches.Register: {ex}");
            }
        }

        /// <summary>
        /// True when StorageBuilding.Save is the LAST writer for this runtime type:
        /// no type between it and StorageBuilding declares Save(ES2Writer) or
        /// Load(ES2Reader). Only these get the marker (end of the object's data).
        /// </summary>
        internal static bool IsEndOfObjectType(Type runtimeType)
        {
            if (runtimeType == null) return false;
            if (_writerTypeCache.TryGetValue(runtimeType, out var cached)) return cached;

            bool result = true;
            try
            {
                const BindingFlags declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                for (Type t = runtimeType; t != null && t != typeof(StorageBuilding); t = t.BaseType)
                {
                    if (t.GetMethod("Save", declared, null, new[] { typeof(ES2Writer) }, null) != null ||
                        t.GetMethod("Load", declared, null, new[] { typeof(ES2Reader) }, null) != null)
                    {
                        result = false;
                        break;
                    }
                }
                if (!typeof(StorageBuilding).IsAssignableFrom(runtimeType)) result = false;
            }
            catch
            {
                result = false;
            }
            _writerTypeCache[runtimeType] = result;
            LiveStockMarketMod.Log.Msg($"{Tag} WoolStoragePatches: {runtimeType.Name} {(result ? "gets" : "does NOT get")} the filter marker.");
            return result;
        }

        // ⚠️ STREAM-LOCKSTEP: the write decision depends ONLY on the runtime type
        // (stable across save and load, and across sessions). The read is a
        // position-restoring peek, so it is safe whether or not a marker exists.
        private static void SavePostfix(StorageBuilding __instance, ES2Writer writer)
        {
            try
            {
                if (!IsEndOfObjectType(__instance.GetType())) return;
                writer.Write(PayloadMagic);
                writer.Write(PayloadVersion);
                writer.Write(ModItems.All.Count);
                foreach (var def in ModItems.All) writer.Write((int)def.Id);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolStorageSavePostfix: {ex.Message}");
            }
        }

        private static void LoadPostfix(StorageBuilding __instance, ES2Reader reader)
        {
            HashSet<int> known;
            try
            {
                known = PeekMarker(reader);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolStorageLoadPostfix: peek failed on '{__instance.displayName}': {ex.Message}");
                return;
            }

            if (known != null)
            {
                // Written by this mod: items the save knew keep the player's setting;
                // items added by a later mod version are newer than the save.
                AllowItemsNewerThanSave(__instance, known);
                return;
            }

            // No marker. For end-of-object types that means the save predates every
            // mod item. For other types (Trading Center) it's expected on every save,
            // so leave their saved filter alone — an older save needs manual ticks there.
            if (IsEndOfObjectType(__instance.GetType()))
                AllowItemsNewerThanSave(__instance, null);
        }

        /// <summary>
        /// Reads the marker if it is next in the stream and returns the mod item IDs
        /// the save knew about (a v1 marker means wool only). Returns null, with the
        /// position restored, when there is no marker, so vanilla's following reads
        /// (or the next object) are untouched.
        /// </summary>
        private static HashSet<int> PeekMarker(ES2Reader reader)
        {
            var es2Stream = reader.stream;
            if (es2Stream == null) return null;
            long start = es2Stream.Position;
            try
            {
                int magic = reader.Read<int>();
                if (magic == PayloadMagic)
                {
                    int version = reader.Read<int>();
                    var known = new HashSet<int>();
                    if (version >= 2)
                    {
                        int count = reader.Read<int>();
                        for (int i = 0; i < count; i++) known.Add(reader.Read<int>());
                    }
                    else
                    {
                        known.Add((int)WoolItem.ItemId);   // v1 (v0.2.4 – v0.4.1): the only mod item then
                    }
                    return known;
                }
            }
            catch (Exception)
            {
                // ran off the end of the data — legacy save
            }
            es2Stream.Position = start;
            return null;
        }

        /// <summary>
        /// Ticks every mod item the save did not know about (all of them when
        /// <paramref name="known"/> is null) in this building's filter, if the
        /// building type accepts the item at all. Same as vanilla's new-item path:
        /// append to the live list, the read-only wrapper follows.
        /// </summary>
        private static void AllowItemsNewerThanSave(StorageBuilding building, HashSet<int> known)
        {
            try
            {
                if (!ModItems.IsRegistered) return;
                var user = building.userDefinedAllowableItems;   // live list behind userDefinedAllowableItemsRO
                var ro = building.allowableItemsRO;
                if (user == null || ro == null) return;
                var allowed = new List<string>();
                foreach (var def in ModItems.All)
                {
                    if (known != null && known.Contains((int)def.Id)) continue;   // the save knew it: the filter is the player's
                    if (user.Exists(i => i != null && i.itemID == def.Id)) continue;
                    Item found = null;
                    for (int i = 0; i < ro.Count; i++)
                        if (ro[i] != null && ro[i].itemID == def.Id) { found = ro[i]; break; }
                    if (found == null) continue;
                    user.Add(found);
                    allowed.Add(def.DisplayName);
                }
                if (allowed.Count > 0)
                    LiveStockMarketMod.Log.Msg($"{Tag} WoolStoragePatches: '{building.displayName}' predates {string.Join(", ", allowed.ToArray())} — allowed by default.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolStoragePatches.AllowItemsNewerThanSave: {ex.Message}");
            }
        }
    }
}
