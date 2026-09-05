using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  SheepBarnUiPatches (step 4)
//
//  The barn window's livestock widgets pick their sprites from the building's
//  TYPE, so a Sheep barn shows goats: the Misc status block (herd health icon,
//  population icon = the goat "overpopulation" herd sprite) and the controls row
//  (herd icon on the divide / slaughter buttons). After vanilla's Init, swap them
//  for the sheep icons — single sheep for the animal, double sheep for the herd —
//  and re-apply or restore on a mode change while the window is open. The
//  four-state button art (grazing, herd size, divide) stays vanilla.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class SheepBarnUiPatches
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        private static readonly FieldInfo StatusBuilding    = AccessTools.Field(typeof(UISubWidgetLivestockStatus), "livestockBuilding");
        private static readonly FieldInfo StatusAnimalImg   = AccessTools.Field(typeof(UISubWidgetLivestockStatus), "livestockImg");
        private static readonly FieldInfo StatusHerdImg     = AccessTools.Field(typeof(UISubWidgetLivestockStatus), "herdSizeImg");
        private static readonly FieldInfo ControlsBuilding  = AccessTools.Field(typeof(UISubWidgetLivestockControls), "livestockBuilding");
        private static readonly FieldInfo[] ControlsAnimalImgs =
        {
            AccessTools.Field(typeof(UISubWidgetLivestockControls), "livestockImg"),
            AccessTools.Field(typeof(UISubWidgetLivestockControls), "livestockDivideHerdLeftImg"),
            AccessTools.Field(typeof(UISubWidgetLivestockControls), "livestockDivideHerdRightImg"),
            AccessTools.Field(typeof(UISubWidgetLivestockControls), "livestockSlaughterImg"),
        };

        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                var args = new[] { typeof(GameObject), typeof(WidgetSetupData) };
                var status = AccessTools.Method(typeof(UISubWidgetLivestockStatus), "Init", args);
                var controls = AccessTools.Method(typeof(UISubWidgetLivestockControls), "Init", args);
                int patched = 0;
                if (status != null && StatusBuilding != null && StatusAnimalImg != null)
                {
                    harmony.Patch(status, postfix: new HarmonyMethod(typeof(SheepBarnUiPatches), nameof(StatusInitPostfix)));
                    patched++;
                }
                if (controls != null && ControlsBuilding != null)
                {
                    harmony.Patch(controls, postfix: new HarmonyMethod(typeof(SheepBarnUiPatches), nameof(ControlsInitPostfix)));
                    patched++;
                }
                GoatBarnModeStore.OnModeChanged += changed => { if (changed is GoatBarn barn) RefreshOpenWindows(barn); };
                if (patched == 2)
                    LiveStockMarketMod.Log.Msg($"{Tag} SheepBarnUiPatches: patched the barn window's livestock status + controls widgets (sheep icons)");
                else
                    LiveStockMarketMod.Log.Warning($"{Tag} SheepBarnUiPatches: only {patched}/2 livestock widgets patched — some barn icons stay goats.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepBarnUiPatches.Register: {ex}");
            }
        }

        private static void StatusInitPostfix(UISubWidgetLivestockStatus __instance) => ApplyStatus(__instance, null);
        private static void ControlsInitPostfix(UISubWidgetLivestockControls __instance) => ApplyControls(__instance, null);

        private static void ApplyStatus(UISubWidgetLivestockStatus widget, GoatBarn only)
        {
            try
            {
                var barn = StatusBuilding.GetValue(widget) as GoatBarn;
                if (barn == null || (only != null && barn != only)) return;
                bool sheep = SheepVisuals.WantSheep(barn);
                var map = GlobalAssets.uiAssetMap;
                Set(StatusAnimalImg, widget, sheep ? SheepVisuals.Icon  : map?.goatIcon);
                Set(StatusHerdImg,   widget, sheep ? SheepVisuals.Image : map?.goatOverpopulationIcon);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepBarnUiPatches.ApplyStatus: {ex.Message}");
            }
        }

        private static void ApplyControls(UISubWidgetLivestockControls widget, GoatBarn only)
        {
            try
            {
                var barn = ControlsBuilding.GetValue(widget) as GoatBarn;
                if (barn == null || (only != null && barn != only)) return;
                bool sheep = SheepVisuals.WantSheep(barn);
                var sprite = sheep ? SheepVisuals.Icon : GlobalAssets.uiAssetMap?.goatIcon;
                foreach (var f in ControlsAnimalImgs) Set(f, widget, sprite);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepBarnUiPatches.ApplyControls: {ex.Message}");
            }
        }

        private static void Set(FieldInfo field, object widget, Sprite sprite)
        {
            var img = field?.GetValue(widget) as Image;
            if (img != null && sprite != null) img.sprite = sprite;
        }

        /// <summary>A mode change while the barn window is open: redraw its widgets.</summary>
        private static void RefreshOpenWindows(GoatBarn barn)
        {
            try
            {
                foreach (var w in UnityEngine.Object.FindObjectsOfType<UISubWidgetLivestockStatus>()) ApplyStatus(w, barn);
                foreach (var w in UnityEngine.Object.FindObjectsOfType<UISubWidgetLivestockControls>()) ApplyControls(w, barn);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepBarnUiPatches.Refresh: {ex.Message}");
            }
        }
    }
}
