using System;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  GoatBarnSaveLoadPatches
//
//  Persists a goat barn's [Goats]/[Sheep] mode and its shearing state across
//  save/reload by appending them to the barn's ES2 stream. Mirrors WotW's
//  GraveyardSaveLoadPatches / FishingShackLoadPatches Save/Load postfix pattern.
//
//  Where the hook lands: GoatBarn does not override Save/Load, so
//  AccessTools.Method(typeof(GoatBarn), "Save") resolves to LivestockBuilding.Save,
//  which Barn / ChickenCoop / Stable / Kennel share. Both postfixes therefore
//  filter on `__instance is GoatBarn` — and ONLY on that — so only goat barns get
//  the extra payload and every other livestock stream is untouched. GoatBarn has
//  no subclass writing after LivestockBuilding, so the payload sits at the true
//  end of the object's tagged data: harmless orphan bytes after an uninstall.
//
//  Payload (after vanilla's LivestockBuilding fields):
//      v1 'LSM1':  int magic, int mode
//      v2 'LSM2':  int magic, int mode, int sheepDays   (step 3: fleece growth)
//  Reads use a position-restoring peek: a legacy save (no payload) leaves the
//  stream exactly where vanilla left it. v1 payloads still load (shearing state
//  starts at zero).
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class GoatBarnSaveLoadPatches
    {
        internal const int PayloadMagicV1 = 0x4C534D31; // 'LSM1'
        internal const int PayloadMagicV2 = 0x4C534D32; // 'LSM2'

        private static string Tag => LiveStockMarketMod.LogTag;

        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type barnType = typeof(GoatBarn);

                var saveMethod = AccessTools.Method(barnType, "Save", new[] { typeof(ES2Writer) });
                if (saveMethod != null)
                {
                    harmony.Patch(saveMethod, postfix: new HarmonyMethod(
                        typeof(GoatBarnSaveLoadPatches), nameof(SavePostfix)));
                    LiveStockMarketMod.Log.Msg(
                        $"{Tag} GoatBarnSaveLoadPatches: patched {saveMethod.DeclaringType.Name}.Save (mode + shearing persistence, GoatBarn only)");
                }
                else
                {
                    LiveStockMarketMod.Log.Warning(
                        $"{Tag} GoatBarnSaveLoadPatches: GoatBarn.Save(ES2Writer) not found — mode will NOT persist.");
                }

                var loadMethod = AccessTools.Method(barnType, "Load", new[] { typeof(ES2Reader) });
                if (loadMethod != null)
                {
                    harmony.Patch(loadMethod, postfix: new HarmonyMethod(
                        typeof(GoatBarnSaveLoadPatches), nameof(LoadPostfix)));
                    LiveStockMarketMod.Log.Msg(
                        $"{Tag} GoatBarnSaveLoadPatches: patched {loadMethod.DeclaringType.Name}.Load (mode + shearing persistence, GoatBarn only)");
                }
                else
                {
                    LiveStockMarketMod.Log.Warning(
                        $"{Tag} GoatBarnSaveLoadPatches: GoatBarn.Load(ES2Reader) not found — mode will NOT restore.");
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GoatBarnSaveLoadPatches.Register: {ex}");
            }
        }

        // ⚠️ STREAM-LOCKSTEP INVARIANT: the write decision depends only on the
        // runtime type (`is GoatBarn`), never on a preference or feature flag, and the
        // read is a position-restoring peek. Feature gates live in the UI/behavior
        // patches, never here.
        private static void SavePostfix(object __instance, ES2Writer writer)
        {
            if (!(__instance is GoatBarn barn)) return;
            try
            {
                writer.Write(PayloadMagicV2);
                writer.Write((int)GoatBarnModeStore.GetMode(barn));
                writer.Write(SheepShearing.GetSheepDays(barn));
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GoatBarnSavePostfix: {ex.Message}");
            }
        }

        private static void LoadPostfix(object __instance, ES2Reader reader)
        {
            if (!(__instance is GoatBarn barn)) return;
            try
            {
                var es2Stream = reader.stream;
                long start = es2Stream != null ? es2Stream.Position : -1;

                int magic;
                try { magic = reader.Read<int>(); }
                catch (Exception)
                {
                    if (es2Stream != null && start >= 0) es2Stream.Position = start;
                    return;   // ran off the end: legacy save, default Goats
                }

                if (magic != PayloadMagicV1 && magic != PayloadMagicV2)
                {
                    // Legacy save (written before this mod): nothing of ours behind
                    // vanilla's data. Rewind so nothing downstream shifts. Default Goats.
                    if (es2Stream != null && start >= 0) es2Stream.Position = start;
                    return;
                }

                int modeInt = reader.Read<int>();
                int sheepDays = 0;
                if (magic == PayloadMagicV2)
                    sheepDays = reader.Read<int>();

                if (!GoatBarnModeStore.IsValid(modeInt))
                {
                    LiveStockMarketMod.Log.Warning(
                        $"{Tag} GoatBarnLoadPostfix: '{barn.gameObject.name}' unknown mode={modeInt}, defaulting to Goats.");
                    return;
                }

                var mode = (GoatBarnMode)modeInt;
                GoatBarnModeStore.SetSavedModeForPosition(barn.transform.position, mode);
                // Vanilla already regenerated "Goat Barn" during Building.Load; now that
                // the mode is known, overlay the Sheep name and setup asset (no-ops for Goats).
                GoatBarnNaming.Apply(barn);
                SheepShearing.RestoreSheepDays(barn, sheepDays);
                SheepShearing.ApplyMode(barn, onSwitch: false);   // herd isn't created yet; the field is what Start/finalize read
                if (mode != GoatBarnModeStore.DefaultMode)
                    LiveStockMarketMod.Log.Msg(
                        $"{Tag} GoatBarnLoadPostfix: '{barn.gameObject.name}' restored mode={mode} (name: {barn.displayName}, sheep-days {sheepDays})");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GoatBarnLoadPostfix: '{barn.gameObject.name}': {ex.Message}");
            }
        }
    }
}
