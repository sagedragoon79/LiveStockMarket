using System;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  PigSoundPatches (1.1.0)
//
//  The sound you hear when you click an animal is not on the animal. It comes
//  from EffectsAssetMap.OnEvent(EventEnum.ObjectSelected, gameObject): the map
//  resolves an EffectEventContainer by the object's CETagComponent.sfxTag, then
//  by its Unity tag ("Goat"), then by "default", and plays one entry of
//  preAudioControllerPrefabs (the bell) followed by one of
//  audioControllerPrefabs (the bleat) through pooled AudioControllers.
//
//  A pig is still a Goat-tagged object, so it rang the bell and bleated. The
//  prefix below answers for a pig with a grunt and skips vanilla for that one
//  object. Everything else falls through untouched.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class PigSoundPatches
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                var onEvent = AccessTools.Method(typeof(EffectsAssetMap), "OnEvent", new[] { typeof(EffectsAssetMap.EventEnum), typeof(GameObject) });
                if (onEvent != null)
                {
                    harmony.Patch(onEvent, prefix: new HarmonyMethod(typeof(PigSoundPatches), nameof(OnEventPrefix)));
                    LiveStockMarketMod.Log.Msg($"{Tag} PigSoundPatches: patched EffectsAssetMap.OnEvent (a clicked pig grunts instead of the goat's bell and bleat)");
                }
                else LiveStockMarketMod.Log.Warning($"{Tag} PigSoundPatches: EffectsAssetMap.OnEvent not found — a clicked pig keeps the goat's bell and bleat.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} PigSoundPatches.Register: {ex}");
            }
        }

        /// <summary>False = vanilla skipped (a pig answered the click itself).</summary>
        private static bool OnEventPrefix(EffectsAssetMap.EventEnum eventEnum, GameObject gameObject)
        {
            try
            {
                if (eventEnum != EffectsAssetMap.EventEnum.ObjectSelected || gameObject == null) return true;
                return !PigSounds.TryPlayClick(gameObject);
            }
            catch
            {
                return true;
            }
        }
    }
}
