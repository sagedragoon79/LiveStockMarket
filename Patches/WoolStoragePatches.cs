using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  WoolStoragePatches
//
//  "Allow wool by default on saves that predate it."
//
//  A storage building's per-item filter (userDefinedAllowableItems) is saved,
//  and on load vanilla auto-allows items newer than the save through its
//  save-version item table. Wool isn't in that table, so an old save would
//  come back with wool unticked in every storehouse — indistinguishable from a
//  player who unticked it on purpose. Same fix as vanilla, mod-side: append a
//  marker to the building's ES2 stream. Marker present → the saved filter is
//  authoritative. Marker absent → the save predates wool → allow it.
//
//  Payload, appended right after vanilla's StorageBuilding fields:
//      int  PayloadMagic   'LSMS'
//      int  PayloadVersion 1
//
//  WRITE only for "end-of-object" types: runtime types with no Save/Load
//  override between them and StorageBuilding (Storehouse, Stockyard,
//  StorageDepot, and vanilla-class mod clones). For those the marker sits at
//  the true end of the object's tagged data, so a save loaded WITHOUT the mod
//  just never reads it. A type that overrides Save/Load (TradingPost: base
//  first, then its own fields) would get the marker mid-stream, where vanilla
//  can't skip it after an uninstall — so those types never get one (v0.2.4+).
//
//  READ with a peek, for every storage building: remember the stream position,
//  read one int, and put the position back unless it is the magic. That keeps
//  a legacy over-read from shifting anything, and still consumes the mid-stream
//  markers that v0.2.3 wrote for the Trading Center (they disappear on the next
//  save). A pre-mod save therefore needs wool ticked once by hand ONLY on
//  non-writer types (the Trading Center); writer types auto-allow.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class WoolStoragePatches
    {
        internal const int PayloadMagic   = 0x4C534D53; // 'LSMS'
        internal const int PayloadVersion = 1;

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
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolStoragePatches: StorageBuilding.Save/Load not found as expected — old saves will need wool ticked manually.");
                    return;
                }
                harmony.Patch(save, postfix: new HarmonyMethod(typeof(WoolStoragePatches), nameof(SavePostfix)));
                harmony.Patch(load, postfix: new HarmonyMethod(typeof(WoolStoragePatches), nameof(LoadPostfix)));
                LiveStockMarketMod.Log.Msg($"{Tag} WoolStoragePatches: patched StorageBuilding.Save/Load (wool filter marker, end-of-object types only)");
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
            LiveStockMarketMod.Log.Msg($"{Tag} WoolStoragePatches: {runtimeType.Name} {(result ? "gets" : "does NOT get")} the wool filter marker.");
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
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolStorageSavePostfix: {ex.Message}");
            }
        }

        private static void LoadPostfix(StorageBuilding __instance, ES2Reader reader)
        {
            bool markerFound = false;
            try
            {
                markerFound = PeekMarker(reader);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolStorageLoadPostfix: peek failed on '{__instance.displayName}': {ex.Message}");
                return;
            }

            if (markerFound) return; // written by this mod: the saved filter is authoritative

            // No marker. For end-of-object types that means the save predates wool.
            // For other types (Trading Center) it's expected on every v0.2.4+ save, so
            // leave their saved filter alone — a pre-mod save needs one manual tick there.
            if (IsEndOfObjectType(__instance.GetType()))
                AllowWoolOnLegacySave(__instance);
        }

        /// <summary>
        /// Reads the marker if it is next in the stream; otherwise restores the
        /// position so vanilla's following reads (or the next object) are untouched.
        /// </summary>
        private static bool PeekMarker(ES2Reader reader)
        {
            var es2Stream = reader.stream;
            if (es2Stream == null) return false;
            long start = es2Stream.Position;
            try
            {
                int magic = reader.Read<int>();
                if (magic == PayloadMagic)
                {
                    reader.Read<int>(); // version 1: nothing more to read
                    return true;
                }
            }
            catch (Exception)
            {
                // ran off the end of the data — legacy save
            }
            es2Stream.Position = start;
            return false;
        }

        private static void AllowWoolOnLegacySave(StorageBuilding building)
        {
            try
            {
                if (!WoolItem.IsRegistered) return;
                var user = building.userDefinedAllowableItems;   // live list behind userDefinedAllowableItemsRO
                if (user == null) return;
                if (user.Exists(i => i != null && i.itemID == WoolItem.ItemId)) return;
                var ro = building.allowableItemsRO;
                Item wool = null;
                for (int i = 0; i < ro.Count; i++)
                    if (ro[i] != null && ro[i].itemID == WoolItem.ItemId) { wool = ro[i]; break; }
                if (wool == null) return;
                user.Add(wool);   // same as vanilla's new-item path: append to the live list, RO wrapper follows
                LiveStockMarketMod.Log.Msg($"{Tag} WoolStoragePatches: '{building.displayName}' predates wool — allowed by default.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolStoragePatches.AllowWoolOnLegacySave: {ex.Message}");
            }
        }
    }
}
