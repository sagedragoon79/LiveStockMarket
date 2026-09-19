using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LiveStockMarket.Systems;

// ─────────────────────────────────────────────────────────────────────────────
//  GoatBarnModeButtonPatches
//
//  Injects a [Goats] / [Sheep] toggle into the goat barn's info window. Ports
//  WotW's HunterModeButtonPatches / GraveyardModeButtonPatches pattern (which
//  itself grew out of Tended Wilds' forager-shack Plant button) onto
//  UIBuildingInfoWindow_New.SetTargetData — the standard-layout window that
//  LivestockBuilding.GetInfoWindowFlags() selects for goat barns.
//
//  Placement (chosen from in-game screenshots, September 4, 2026): the pair is
//  overlaid on the barn PORTRAIT in the top panel (UIBuildingInfoWindow_New.
//  portraitImage), side by side, centered at a normalized (x, y) inside the
//  portrait rect. Position and button size come from live MelonPreferences so
//  the layout can be nudged from Keep Clarity's panel without a rebuild; a pref
//  change rebuilds any open row. Fallbacks, in order: a layout row directly
//  above the vanilla livestock controls sub-widget, then WotW's fixed
//  top-center overlay.
//
//  The window is pooled and reused for every building, so every SetTargetData
//  sweeps the live rows first (registry sweep, not a scene scan — WotW's
//  click→window-open hitch fix) and re-injects only for a GoatBarn (both tiers).
//
//  Step 1: clicking a button only sets GoatBarnModeStore mode (persisted
//  separately). It does NOT change what the barn produces yet.
// ─────────────────────────────────────────────────────────────────────────────

namespace LiveStockMarket.Patches
{
    internal static class GoatBarnModeButtonPatches
    {
        private static readonly BindingFlags AllInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private const string RowName = "LSM_GoatBarnModeButtons";

        // Row chrome around the two buttons (UI units).
        private const float Spacing = 6f;
        private const int   PadX    = 2;
        private const int   PadY    = 2;

        // WotW constellation palette so the toggle reads like the hunter / fishing /
        // graveyard mode buttons players already know.
        private static readonly Color GoldBright = new Color(0.95f, 0.82f, 0.35f, 1f);
        private static readonly Color GoldMuted  = new Color(0.55f, 0.45f, 0.22f, 1f);
        private static readonly Color BgActive   = new Color(0.18f, 0.26f, 0.12f, 0.95f);
        private static readonly Color BgInactive = new Color(0.10f, 0.09f, 0.07f, 0.90f);

        // Cached FieldInfo for UIBuildingInfoWindow_New.building — resolved once.
        // The postfix runs on EVERY building selection.
        private static FieldInfo _buildingField;
        private static bool _buildingFieldTried;

        private static string Tag => LiveStockMarketMod.LogTag;

        public static void Register(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type windowType = typeof(UIBuildingInfoWindow_New);

                var setTargetData = windowType.GetMethod("SetTargetData", AllInstance);
                if (setTargetData == null)
                {
                    LiveStockMarketMod.Log.Warning(
                        $"{Tag} GoatBarnModeButton: UIBuildingInfoWindow_New.SetTargetData not found — no buttons.");
                    return;
                }

                harmony.Patch(setTargetData, postfix: new HarmonyMethod(
                    typeof(GoatBarnModeButtonPatches), nameof(SetTargetDataPostfix)));
                LiveStockMarketMod.Log.Msg(
                    $"{Tag} GoatBarnModeButton: patched UIBuildingInfoWindow_New.SetTargetData");

            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GoatBarnModeButton register failed: {ex.Message}");
            }
        }

        /// <summary>Re-injects every open row with the current placement prefs.</summary>
        internal static void RebuildLiveRows()
        {
            try
            {
                var snapshot = ModeButtonSubscriber.Live.ToArray();
                for (int i = 0; i < snapshot.Length; i++)
                    if (snapshot[i] != null) snapshot[i].Rebuild();
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GoatBarnModeButton rebuild failed: {ex.Message}");
            }
        }

        private static void SetTargetDataPostfix(object __instance)
        {
            try
            {
                var window = __instance as Component;
                if (window == null) return;

                var barn = ResolveBuilding(__instance) as GoatBarn;
                if (barn == null)
                {
                    RemoveAllButtonRows();
                    return;
                }

                InjectButtons(window, barn);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GoatBarnModeButton postfix error: {ex.Message}");
            }
        }

        /// <summary>The window stores its Building in a private field set by
        /// SetTargetData (building = targetObject.GetComponent&lt;Building&gt;()).</summary>
        private static Building ResolveBuilding(object window)
        {
            if (!_buildingFieldTried)
            {
                _buildingFieldTried = true;
                _buildingField = window.GetType().GetField("building", AllInstance)
                              ?? AccessTools.Field(window.GetType(), "building");
                if (_buildingField == null)
                    LiveStockMarketMod.Log.Warning(
                        $"{Tag} GoatBarnModeButton: UIBuildingInfoWindow_New.building field not found — no buttons.");
            }
            return _buildingField?.GetValue(window) as Building;
        }

        /// <summary>Destroys every injected row, whichever window it hangs off.
        /// Registry sweep instead of FindObjectsOfType (selection-path perf).</summary>
        private static void RemoveAllButtonRows()
        {
            var live = ModeButtonSubscriber.Live;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                var sub = live[i];
                if (sub != null)
                    UnityEngine.Object.Destroy(sub.gameObject);
                else
                    live.RemoveAt(i); // defensive: purge dead entries
            }
        }

        // ── Placement on the barn portrait (dialed in during development) ─────────────────────
        private const float ButtonWidth  = 68f;   // three across the portrait
        private const float ButtonHeight = 47f;
        private const float PosX         = 0.56f;   // across the portrait, 0 = left edge
        private const float PosY         = 0.40f;   // up the portrait, 0 = bottom edge

        private static void InjectButtons(Component window, GoatBarn barn)
        {
            // One row at a time. Object.Destroy is end-of-frame, so a same-frame
            // rebuild (mode click / pref change) briefly overlaps — harmless, and
            // the old row's subscriber unhooks itself in OnDestroy.
            RemoveAllButtonRows();

            TMP_FontAsset gameFont = null;
            float gameFontSize = 14f;
            var existingText = window.GetComponentInChildren<TextMeshProUGUI>(true);
            if (existingText != null)
            {
                gameFont = existingText.font;
                gameFontSize = existingText.fontSize;
            }

            float w = ButtonWidth;
            float h = ButtonHeight;

            var row = new GameObject(RowName);
            var rowRT = row.AddComponent<RectTransform>();
            string anchor = PlaceRow(window, row, rowRT, w, h);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Spacing;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.padding = new RectOffset(PadX, PadX, PadY, PadY);

            var current = GoatBarnModeStore.GetMode(barn);
            CreateButton(row.transform, "Goats", GoatBarnMode.Goats, barn, current, gameFont, gameFontSize, w, h);
            CreateButton(row.transform, "Sheep", GoatBarnMode.Sheep, barn, current, gameFont, gameFontSize, w, h);
            CreateButton(row.transform, "Pigs", GoatBarnMode.Pigs, barn, current, gameFont, gameFontSize, w, h);

            // Event-driven refresh: rebuild when this barn's mode changes so the
            // active highlight updates on click.
            var sub = row.AddComponent<ModeButtonSubscriber>();
            sub.Setup(window, barn);

            // Mode-driven name: make sure displayName matches the mode, then push it
            // into the title field so a click flips the title immediately (vanilla
            // only reads displayName into the field when the window opens).
            GoatBarnNaming.Apply(barn);
            RefreshTitle(window, barn);

            LiveStockMarketMod.Log.Msg(
                $"{Tag} GoatBarnModeButton: injected for '{barn.gameObject.name}' " +
                $"(mode={current}, anchor={anchor}, size={w:F0}x{h:F0}, title='{barn.displayName}')");
        }

        private static void RefreshTitle(Component window, GoatBarn barn)
        {
            try
            {
                var win = window as UIBuildingInfoWindow_New;
                var field = win != null ? win.nameInputField : null;
                if (field == null || field.isFocused) return;
                if (field.text != barn.displayName) field.text = barn.displayName;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} GoatBarnModeButton: title refresh failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Parents and positions the row. Preferred: overlaid on the barn portrait
        /// at the pref-driven normalized point. Fallbacks: a layout row above the
        /// livestock controls sub-widget, then a fixed top-center overlay.
        /// Returns a label for the log.
        /// </summary>
        private static string PlaceRow(Component window, GameObject row, RectTransform rowRT, float w, float h)
        {
            float rowW = 3f * w + 2f * Spacing + 2f * PadX;
            float rowH = h + 2f * PadY;

            var win = window as UIBuildingInfoWindow_New;
            var portrait = win != null ? win.portraitImage : null;
            if (portrait != null)
            {
                float nx = PosX, ny = PosY;
                row.transform.SetParent(portrait.transform, false);
                var le = row.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
                rowRT.anchorMin = new Vector2(nx, ny);
                rowRT.anchorMax = new Vector2(nx, ny);
                rowRT.pivot = new Vector2(0.5f, 0.5f);
                rowRT.anchoredPosition = Vector2.zero;
                rowRT.sizeDelta = new Vector2(rowW, rowH);
                return $"portrait x={nx:F2} y={ny:F2}";
            }

            var controls = window.GetComponentInChildren<UISubWidgetLivestockControls>(true);
            if (controls != null && controls.transform.parent != null)
            {
                // Layout row directly above the herd/slaughter/grazing controls.
                row.transform.SetParent(controls.transform.parent, false);
                rowRT.anchorMin = new Vector2(0f, 1f);
                rowRT.anchorMax = new Vector2(1f, 1f);
                rowRT.pivot = new Vector2(0.5f, 1f);
                rowRT.sizeDelta = new Vector2(0f, rowH);
                var le = row.AddComponent<LayoutElement>();
                le.preferredHeight = rowH;
                le.minHeight = rowH;
                le.flexibleWidth = 1f;
                row.transform.SetSiblingIndex(controls.transform.GetSiblingIndex());
                return "livestock controls (fallback)";
            }

            // WotW-style fixed overlay near the top of the window.
            row.transform.SetParent(window.transform, false);
            var le2 = row.AddComponent<LayoutElement>();
            le2.ignoreLayout = true;
            rowRT.anchorMin = new Vector2(0.5f, 1f);
            rowRT.anchorMax = new Vector2(0.5f, 1f);
            rowRT.pivot = new Vector2(0.5f, 1f);
            rowRT.anchoredPosition = new Vector2(0f, -255f);
            rowRT.sizeDelta = new Vector2(rowW, rowH);
            return "overlay (fallback)";
        }

        private static void CreateButton(Transform parent, string label, GoatBarnMode mode,
            GoatBarn barn, GoatBarnMode currentMode, TMP_FontAsset font, float fontSize,
            float width, float height)
        {
            bool isActive = currentMode == mode;

            var btnObj = new GameObject($"LSM_GoatBarnBtn_{label}");
            btnObj.transform.SetParent(parent, false);

            var borderImg = btnObj.AddComponent<Image>();
            borderImg.color = isActive ? GoldBright : GoldMuted;
            borderImg.raycastTarget = true;

            var le = btnObj.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.preferredWidth = width;
            le.minWidth = width;

            var innerObj = new GameObject("Inner");
            innerObj.transform.SetParent(btnObj.transform, false);
            var innerRT = innerObj.AddComponent<RectTransform>();
            innerRT.anchorMin = Vector2.zero;
            innerRT.anchorMax = Vector2.one;
            innerRT.offsetMin = new Vector2(2f, 2f);
            innerRT.offsetMax = new Vector2(-2f, -2f);
            var innerImg = innerObj.AddComponent<Image>();
            innerImg.color = isActive ? BgActive : BgInactive;
            innerImg.raycastTarget = false;

            var textObj = new GameObject("Label");
            textObj.transform.SetParent(innerObj.transform, false);
            var textRT = textObj.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.fontStyle = FontStyles.Bold;
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = isActive ? GoldBright : GoldMuted;
            tmp.raycastTarget = false;
            tmp.outlineWidth = 0.15f;
            tmp.outlineColor = new Color32(0, 0, 0, 200);
            // Never wrap; auto-size down so the label always fits on one line and
            // scales with the (pref-driven) button height.
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 9f;
            tmp.fontSizeMax = Mathf.Clamp(height * 0.5f, 12f, Mathf.Max(16f, fontSize * 1.10f));

            var trigger = btnObj.AddComponent<EventTrigger>();
            var capturedBarn = barn;
            var capturedMode = mode;
            var capturedBtn = btnObj;

            var clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            clickEntry.callback.AddListener((data) =>
            {
                GoatBarnModeStore.SetMode(capturedBarn, capturedMode);
            });
            trigger.triggers.Add(clickEntry);

            var enterEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enterEntry.callback.AddListener((data) =>
                ShowTooltip(capturedBtn.transform, GetTooltipText(capturedMode), font));
            trigger.triggers.Add(enterEntry);

            var exitEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exitEntry.callback.AddListener((data) => HideTooltip());
            trigger.triggers.Add(exitEntry);
        }

        // ── Tooltip (compact port of the WotW mode-button tooltip) ─────────────
        private static GameObject _tooltipObj;

        private static string GetTooltipText(GoatBarnMode mode)
        {
            switch (mode)
            {
                case GoatBarnMode.Goats:
                    return "<b>Goats</b>\n<i>Vanilla goat barn — milk and meat.</i>";
                case GoatBarnMode.Sheep:
                    return "<b>Sheep</b>\n<i>Wool flock — shorn once a year in season.\nMeat and hides as goats.</i>";
                case GoatBarnMode.Pigs:
                    return "<b>Pigs</b>\n<i>Pig herd — more meat and tallow when butchered,\nbreeds fast, roots up mushrooms when grazing among trees.</i>";
                default:
                    return mode.ToString();
            }
        }

        private static void ShowTooltip(Transform anchor, string text, TMP_FontAsset font)
        {
            HideTooltip();
            if (anchor == null) return;

            _tooltipObj = new GameObject("LSM_GoatBarnButtonTooltip");
            _tooltipObj.transform.SetParent(anchor, false);
            _tooltipObj.transform.SetAsLastSibling();

            var canvas = _tooltipObj.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32000;

            var rt = _tooltipObj.GetComponent<RectTransform>() ?? _tooltipObj.AddComponent<RectTransform>();
            // Below the button (the buttons sit on the portrait near the top of
            // the window, so a tooltip above would run off the panel).
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -6f);
            rt.sizeDelta = new Vector2(340f, 96f);

            var border = _tooltipObj.AddComponent<Image>();
            border.color = GoldMuted;
            border.raycastTarget = false;

            var inner = new GameObject("Inner");
            inner.transform.SetParent(_tooltipObj.transform, false);
            var innerRT = inner.AddComponent<RectTransform>();
            innerRT.anchorMin = Vector2.zero;
            innerRT.anchorMax = Vector2.one;
            innerRT.offsetMin = new Vector2(2f, 2f);
            innerRT.offsetMax = new Vector2(-2f, -2f);
            var innerImg = inner.AddComponent<Image>();
            innerImg.color = new Color(0.06f, 0.05f, 0.04f, 0.96f);
            innerImg.raycastTarget = false;

            var content = new GameObject("Content");
            content.transform.SetParent(inner.transform, false);
            var contentRT = content.AddComponent<RectTransform>();
            contentRT.anchorMin = Vector2.zero;
            contentRT.anchorMax = Vector2.one;
            contentRT.offsetMin = new Vector2(10f, 8f);
            contentRT.offsetMax = new Vector2(-10f, -8f);

            var tmp = content.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.fontSize = 15f;
            tmp.color = new Color(0.95f, 0.92f, 0.78f, 1f);
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.raycastTarget = false;
            tmp.richText = true;
            tmp.enableWordWrapping = true;
            tmp.text = text;
        }

        private static void HideTooltip()
        {
            if (_tooltipObj != null)
            {
                UnityEngine.Object.Destroy(_tooltipObj);
                _tooltipObj = null;
            }
        }

        /// <summary>Rebuilds the buttons when this barn's mode (or the placement
        /// prefs) change so the row stays current. Pool-safe teardown.</summary>
        private class ModeButtonSubscriber : MonoBehaviour
        {
            /// <summary>Live instances — lets RemoveAllButtonRows sweep without a
            /// scene-wide FindObjectsOfType. Registered in Setup (always called
            /// right after AddComponent), removed in OnDestroy.</summary>
            internal static readonly List<ModeButtonSubscriber> Live =
                new List<ModeButtonSubscriber>();

            private Component _window;
            private GoatBarn _barn;
            private Action<Component> _handler;

            public void Setup(Component window, GoatBarn barn)
            {
                if (!Live.Contains(this)) Live.Add(this);
                _window = window;
                _barn = barn;
                _handler = OnModeChanged;
                GoatBarnModeStore.OnModeChanged += _handler;
            }

            private void OnModeChanged(Component changed)
            {
                if (changed != _barn) return;
                Rebuild();
            }

            /// <summary>Re-injects for this window/barn, or tears down if the
            /// window has moved on to another building.</summary>
            public void Rebuild()
            {
                if (!IsWindowStillShowingOurBarn())
                {
                    UnityEngine.Object.Destroy(gameObject);
                    return;
                }
                InjectButtons(_window, _barn);
            }

            private bool IsWindowStillShowingOurBarn()
            {
                if (_window == null || _barn == null) return false;
                try { return ResolveBuilding(_window) == _barn; }
                catch { return false; }
            }

            private void OnDestroy()
            {
                Live.Remove(this);
                if (_handler != null) GoatBarnModeStore.OnModeChanged -= _handler;
                _handler = null;
                _window = null;
                _barn = null;
            }
        }
    }
}
