# Live-Stock Market — handoff (July 26, 2026)

Farthest Frontier mod (MelonLoader 0.7 + HarmonyLib, FF v1.1.x Mono). Author: SageDragoon.
Identity: display name **Live-Stock Market**; repo/namespace/DLL/MelonPreferences category `LiveStockMarket`.

## Goal
A wool production chain, built in steps. Step 1 is the only thing in scope right now.

## Step 1 (agreed, ready to build): Goats / Sheep mode toggle on the goat barn
- Target: vanilla `GoatBarn` and `GoatBarn_Tier2` (identifiers confirmed in the decompile). NOT a building clone — a mode toggle on the existing building.
- Deliverable: injected **Goats / Sheep** buttons in the barn's info window, mode persisted per barn, default Goats, no behavior change yet. First in-game test: toggle → save → reload.
- Mechanism = the author's own proven pattern (documented in
  `Knowledge/FF-Modding-Knowledge/mod-patterns/building-work-modes.md`):
  - position-keyed static mode store (copy `WardenOfTheWilds/Systems/GraveyardModeStore.cs`)
  - info-panel button injection (copy `WardenOfTheWilds/Patches/GraveyardModeButtonPatches.cs`; original technique in `Tended Wilds/TendedWilds.cs`)
  - persistence via Save/Load postfixes appending the mode to the building's ES2 stream (copy `WardenOfTheWilds/Patches/GraveyardSaveLoadPatches.cs`)
  - load-phase application (Manifest Delivery's `OnGameFinishedLoadingFinalize` pattern; see `WardenOfTheWilds/Patches/FishingShackLoadPatches.cs`)
- Scaffold model: `BoatKeys/` (csproj with game-DLL HintPaths, `KeepClarityIntegration.cs`, deploy-while-running watcher; build with `dotnet msbuild`, deploy blocks while FF runs → `until ! tasklist | grep -qi Farthest; do sleep 20; done; cp …`).

## Later steps (decided in principle, not yet designed)
2. `ItemWool` — a genuinely new item. Full integration map: `Knowledge/FF-Modding-Knowledge/game-systems/items-system.md` (name-string saves, dictionary lookups, WorkBucketManager injection, recipes with pre-set `_item`, `forcedItemNames` for traders). Uninstall caveat: no graceful missing-mod path for items → warn players.
   - Item icon is ready: `art/wool_icon.png` (raw fleece bundle, transparent, 103×104, from the user on September 4, 2026). Embed it as an `EmbeddedResource` (BoatKeys style, `GetManifestResourceStream` → `Texture2D.LoadImage` → `Sprite.Create`) and inject it under `"ItemWool"` into `GlobalAssets.uiAssetMap.itemNameToSpriteDict` / `itemNameToGraphicDict` at mod init. Vanilla UI icons are 64×64, so the size is fine.
3. Shearing = mimic vanilla milking (worker visits animals, product to storage). See `game-systems/livestock-system.md`.
4. Sheep visuals: goats keep their look for now; later a sheep model through the Blender pipeline, mesh-swapped per barn mode.
5. Weaver end of the chain (wool → product). New clothing with warmth is the hard case (three hardcoded clothing seek slots) — prefer a trade good or a table-driven clothing patch.

## Step 1 status (September 4, 2026): built, deployed, core behavior verified in-game

v0.1.0 built and deployed in the morning session; v0.1.1 (placement change) built and deployed in the evening. Nothing is committed yet.

In-game results (v0.1.0, user test):
- Clicking Sheep flips the highlight; the mode survives save → quit → reload. PASS.
- Upgrading the tier 1 barn to the Large Goat Barn (tier 2) kept Sheep mode — position keying carries across the upgrade as expected. PASS.
- First placement (layout row above the vanilla herd controls in the Misc. section) worked but was rejected: too chunky, and the user wants the pair on the barn portrait instead.

What was built (files in this repo):
- `Systems/GoatBarnModeStore.cs` — position-keyed static store, `GoatBarnMode { Goats = 0, Sheep = 1 }`, `OnModeChanged` event, cleared on Map scene load.
- `Patches/GoatBarnModeButtonPatches.cs` — postfix on `UIBuildingInfoWindow_New.SetTargetData`. v0.1.1: the pair is overlaid on the barn portrait (`portraitImage`, a public field) at a normalized point inside its rect; position and size come from live prefs (`ButtonPosX/Y`, `ButtonWidth/Height`), and a pref change rebuilds the open row via `MelonPreferences_Entry.OnEntryValueChanged`. Fallbacks: layout row above `UISubWidgetLivestockControls`, then WotW's fixed overlay. WotW palette, tooltips (now below the button), live-registry sweep on every selection.
- `Patches/GoatBarnSaveLoadPatches.cs` — postfixes on `LivestockBuilding.Save/Load` (GoatBarn has no override; the base is shared by every livestock building, so both postfixes filter `__instance is GoatBarn`). Payload after vanilla fields: `int 'LSM1'` marker + `int mode`. Legacy saves load as Goats.
- `Patches/GoatBarnLoadPatches.cs` — postfix on `LivestockBuilding.OnGameFinishedLoadingFinalize`; step 1 logs Sheep barns only. Later steps apply mode data here (or as a prefix if it must precede the herd setup).
- `LiveStockMarket.cs`, `KeepClarityIntegration.cs`, `Properties/AssemblyInfo.cs`, `LiveStockMarket.csproj` (BoatKeys-style legacy csproj, net472, `dotnet msbuild -restore`), `deploy.sh` (waits for FF to exit, then copies), `README.md` (checks + tuning table).

Decisions made while building (flag if any is wrong):
- Payload marker `'LSM1'` precedes the mode int. WotW writes the bare mode and relies on validation to reject garbage on legacy saves; the marker closes the small chance that garbage reads as `1` (Sheep) and gives later steps a payload version to bump.
- v0.1.1 placement defaults were read off the user's marked-up screenshot: pair centered horizontally on the portrait, vertical center about 40% up from the portrait's bottom edge, buttons 110×40. Sliders exist so the user can dial it in without rebuilds; fold the final values into the defaults and drop the sliders once settled.
- Prefs: `ModEnabled` (restart required) plus the four placement knobs. No sheep-specific settings until a step needs them.

Placement settled (v0.1.2): the user dialed X 0.56, Y 0.40, width 95, height 47 with the live sliders — the pair sits on the portrait just above the Upgrade button. Those numbers are now the defaults. The sliders are still registered (cheap, and step 4's sheep visuals may change the portrait); strip them before a release build.

Barn name follows the mode (v0.1.3, user asked "can the barn name switch"; decision: real rename everywhere, leave the blurb): a Sheep barn is "Sheep Barn" / "Large Sheep Barn" in the window title, map hover label, lists, and modals. Mechanics: `Resource.displayName` is never saved and is regenerated by `Building.SetBuildingDataRecordName` (load with `force: true`, fresh build, the tier upgrade on the NEW instance, language change); its setter pushes into the widget blackboard (map label). So `Systems/GoatBarnNaming.cs` overlays the name at three points — the toggle (`OnModeChanged`), the Load postfix (after the mode is read), and `Patches/GoatBarnNamePatches.cs` (postfix on `SetBuildingDataRecordName`) — and the button patch writes the title field directly so a click flips it instantly. English word swap "Goat" → "Sheep"; other languages get a " (Sheep)" suffix. The description blurb is untouched until step 2. Verified in-game September 4, 2026: rename works on click, and upgrading a Sheep barn shows "Large Sheep Barn".

**Step 1 is complete as of v0.1.3.** Everything in scope works in-game: toggle, persistence across save/reload, tier upgrade, portrait placement, and the mode-driven name. Open before a release build: strip the four placement sliders, decide on the relocation handoff (barn relocation still resets to Goats), and run the uninstall round-trip. Nothing is committed yet.

Known step 1 limitations: relocating a barn resets it to Goats (port WotW's graveyard `Relocate`/`ConstructionComplete` handoff when mode drives behavior); uninstall leaves 8 orphaned bytes per barn that vanilla never reads.

Deploy note: the v0.1.2 build's `CopyToMods` overwrote `Mods/LiveStockMarket.dll` while Farthest Frontier was running (September 4, 2026, MelonLoader 0.7) — the file was not held open, which matches `mod-patterns/melonloader-deploy-while-running.md` rather than this handoff's "deploy blocks while FF runs". `deploy.sh`'s wait loop stays as a fallback for the day a copy does fail. Either way the new code needs a game relaunch to take effect.
