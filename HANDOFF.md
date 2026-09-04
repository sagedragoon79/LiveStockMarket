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
3. Shearing = mimic vanilla milking (worker visits animals, product to storage). See `game-systems/livestock-system.md`.
4. Sheep visuals: goats keep their look for now; later a sheep model through the Blender pipeline, mesh-swapped per barn mode.
5. Weaver end of the chain (wool → product). New clothing with warmth is the hard case (three hardcoded clothing seek slots) — prefer a trade good or a table-driven clothing patch.
