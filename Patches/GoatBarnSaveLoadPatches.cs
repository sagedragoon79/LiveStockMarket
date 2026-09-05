using System;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  GoatBarnSaveLoadPatches
//
//  Persists a goat barn's [Goats]/[Sheep] mode across save/reload by appending
//  it to the barn's ES2 stream. Mirrors WotW's GraveyardSaveLoadPatches /
//  FishingShackLoadPatches Save/Load postfix pattern.
//
//  Where the hook lands: GoatBarn does not override Save/Load, so
//  AccessTools.Method(typeof(GoatBarn), "Save") resolves to LivestockBuilding.Save,
//  which Barn / ChickenCoop / Stable / Kennel share. Both postfixes therefore
//  filter on `__instance is GoatBarn` — and ONLY on that — so only goat barns get
//  the extra payload and every other livestock stream is untouched.
//
//  Payload (after vanilla's LivestockBuilding fields):
//      int  PayloadMagic   'LSM1' — marks "an LSM payload follows"
//      int  mode           GoatBarnMode (0 = Goats, 1 = Sheep)
//  SaveManager writes each barn under its own ES2 tag ("goatBarn<i>"), so a
//  legacy save (no payload) makes the magic read return garbage or throw — both
//  paths default to Goats and the next save writes a proper payload. The magic
//  also versions the payload: later steps bump it ('LSM2', …) and read more.
//
//  Backward compatible: pre-mod saves load with every barn in Goats mode.
//  Uninstall: the orphaned 8 bytes per barn are never read by vanilla (tagged
//  reads stop after vanilla's fields) — test before shipping, per the pattern doc.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class GoatBarnSaveLoadPatches
    {
        /// <summary>'LSM1' — payload marker + version. Bump when the payload grows.</summary>
        internal const int PayloadMagic = 0x4C534D31;

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
                        $"{Tag} GoatBarnSaveLoadPatches: patched {saveMethod.DeclaringType.Name}.Save (mode persistence, GoatBarn only)");
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
                        $"{Tag} GoatBarnSaveLoadPatches: patched {loadMethod.DeclaringType.Name}.Load (mode persistence, GoatBarn only)");
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

        // ⚠️ STREAM-LOCKSTEP INVARIANT: SavePostfix and LoadPostfix must BOTH run
        // unconditionally for the same set of instances — never gate either behind
        // a preference or feature flag, and keep the `is GoatBarn` filter identical
        // in both. ES2 data inside one tag is positional: if Save skips the append
        // while Load still reads (or vice versa), the barn's stream desyncs.
        // Feature gates live in the UI/behavior patches, never here.
        private static void SavePostfix(object __instance, ES2Writer writer)
        {
            if (!(__instance is GoatBarn barn)) return;
            try
            {
                writer.Write(PayloadMagic);
                writer.Write((int)GoatBarnModeStore.GetMode(barn));
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
                int magic = reader.Read<int>();
                if (magic != PayloadMagic)
                {
                    // Legacy save (written before this mod): nothing of ours behind
                    // vanilla's data. Default Goats; saving writes a proper payload.
                    return;
                }

                int modeInt = reader.Read<int>();
                if (!GoatBarnModeStore.IsValid(modeInt))
                {
                    LiveStockMarketMod.Log.Warning(
                        $"{Tag} GoatBarnLoadPostfix: '{barn.gameObject.name}' unknown mode={modeInt}, defaulting to Goats.");
                    return;
                }

                var mode = (GoatBarnMode)modeInt;
                GoatBarnModeStore.SetSavedModeForPosition(barn.transform.position, mode);
                // Vanilla already regenerated "Goat Barn" during Building.Load; now that
                // the mode is known, overlay the Sheep name (no-op for Goats).
                GoatBarnNaming.Apply(barn);
                if (mode != GoatBarnModeStore.DefaultMode)
                    LiveStockMarketMod.Log.Msg(
                        $"{Tag} GoatBarnLoadPostfix: '{barn.gameObject.name}' restored mode={mode} (name: {barn.displayName})");
            }
            catch (Exception ex)
            {
                // Reader ran past the barn's data — cleanest legacy signal.
                LiveStockMarketMod.Log.Msg(
                    $"{Tag} GoatBarnLoadPostfix: no LSM payload in save for '{barn.gameObject.name}' (legacy), " +
                    $"defaulting to Goats. ({ex.GetType().Name})");
            }
        }
    }
}
