# Live-Stock Market

A wool economy and a pig pen for Farthest Frontier (v1.1.x, MelonLoader 0.7). Turn a goat barn into a sheep barn or a pig barn: shear wool every year and craft it into warmer clothing, or let pigs root up mushrooms in the woods and butcher them for extra meat and tallow. By SageDragoon.

## What it adds

**Sheep barns.** Every goat barn, both tiers, has Goats / Sheep / Pigs buttons on its portrait. Switch to Sheep and the barn becomes a Sheep Barn: its animals turn into sheep, newborns and goats bought from traders arrive as sheep, and the herders shear wool in season instead of milking. Switch back and everything returns to goats. The choice is saved with the barn.

**Wool.** A new item. It is stored wherever hides are, counted under Produced Materials in the Settlement Items window, and traded: the agricultural trader and the hunter-and-herder trader may carry it, and the Trading Center buys and sells it.

**Shearing.** Sheep grow a fleece over the year (240 days by default) and are shorn during the shearing season (days 78 to 200 by default, the goat milking window), once per year. A fully grown fleece yields 4 wool per sheep; a younger fleece yields proportionally less. Fleece growth banks across a switch to Goats and back.

**Wool garments.** Three trade goods that double as warmer clothing. Each producer gets the garment as an extra recipe beside its vanilla one, so you set the mix with the usual sliders.

| Garment | Made at | Recipe (default) | Replaces |
|---|---|---|---|
| Winter Boots | Cobbler Shop | the shoes' leather x2 + 5 wool | Shoes |
| Winter Cloak | Tannery | the hide coat's leather x2 + 5 wool | Hide Coat |
| Woolen Clothes | Weaver | the linen clothes' flax x2 + 5 wool | Linen Clothes |

Each is 25% warmer than the item it replaces and priced 25% higher. Villagers take a garment when one is in stock and fall back to the vanilla item otherwise; whatever they wear stays on until it wears out.

**Pig barns.** Switch a barn to Pigs and it becomes a Pig Barn. Its animals turn into pigs, with their own body and animations, and the herders stop milking: pigs are not harvested. Pigs eat the same fodder as goats, breed at twice the goat rate, produce one and a half times the waste for the compost yard, and butcher for twice the meat, twice the tallow and one and a half times the hide of a goat. All five numbers are settings.

**Truffle pigs.** Pigs forage. Every day from spring through autumn, each grown pig turns up mushrooms in proportion to how wooded the barn's grazing area is: 12 or more trees inside it give the full rate of 0.05 mushrooms per pig per day, fewer trees give less, and no grazing area gives none. The mushrooms collect in the barn (100 by default) and haulers carry them out. No herder time is spent.

**Pig sounds.** Pigs grunt now and then, breathe quietly when you are close, and squeal when they are butchered. Click a pig, or a Pig Barn, and it answers with a grunt instead of the goat's bell and bleat. The sounds run through the game's own volume sliders. Volume, interval, range and the click grunt are settings.

**Tallow candles.** The Candle Shop gets a second candle recipe beside the vanilla one: tallow instead of wax, twice the wax count by default. Set the mix with the usual sliders.

## Installation

1. Install MelonLoader 0.7 for Farthest Frontier (Mono).
2. Put `LiveStockMarket.dll` in the game's `Mods` folder. That is the whole mod: the sheep and pig models, the pig sounds and the icons are inside the DLL.
3. Optional: with Keep Clarity installed, the settings appear in its F10 panel under Live-Stock Market. Without it, edit `UserData/MelonPreferences.cfg`.

Safe to add to an existing save. Read "Removing the mod" before taking it out of one.

## Settings

All in the `LiveStockMarket` section of `MelonPreferences.cfg`. "Live" means the change applies without a restart.

| Setting | Default | Meaning |
|---|---|---|
| `ModEnabled` | true | Master switch. Restart. |
| `GarmentWarmthMultiplier` | 1.25 | A garment counts as the item it replaces at this effectiveness. Live. |
| `GarmentWoolCost` | 5 | Wool per garment in the three recipes. Live. |
| `GarmentInputMultiplier` | 2.0 | Multiplier on the vanilla recipe's leather or flax. Live. |
| `GarmentPriceMultiplier` | 1.25 | Garment price relative to the replaced item. Restart. |
| `ShearSeasonStartDay` | 78 | First day of the year Sheep barns shear. Live. |
| `ShearSeasonEndDay` | 200 | Last day of the year Sheep barns shear. Live. |
| `ShearCooldownDays` | 300 | Days before a shorn sheep can be shorn again. Live. |
| `WoolPerSheep` | 4 | Wool from a fully grown fleece. Live. |
| `WoolSecondsPerUnit` | 10 | Herder time per unit of wool. Live. |
| `WoolGrowthDays` | 240 | Days in Sheep mode for a full fleece. Live. |
| `SheepBarnWoolCapacity` | 300 | Wool a Sheep barn holds before haulers take it out. Live. |
| `SheepVisuals` | true | Sheep model, name and icon in Sheep barns. Live. |
| `SheepUseGameShader` | true | The goat's own material with the sheep textures. Off = the plain bundled material. Live. |
| `PigMeatMultiplier` | 2.0 | Meat from a butchered pig relative to a goat. Live. |
| `PigTallowMultiplier` | 2.0 | Tallow from a butchered pig relative to a goat. Live. |
| `PigHideMultiplier` | 1.5 | Hide from a butchered pig relative to a goat. Live. |
| `PigBreedingMultiplier` | 2.0 | Breeding chance and minimum births relative to goats. Live. |
| `PigWasteMultiplier` | 1.5 | Waste (manure) a Pig barn produces relative to goats. Live. |
| `PigMushroomsPerPigPerDay` | 0.05 | Mushrooms per grown pig per day in a fully wooded grazing area, spring through autumn. Live. |
| `PigMushroomTreesForFullYield` | 12 | Trees inside the grazing area for the full mushroom rate. Live. |
| `PigMushroomCapacity` | 100 | Mushrooms a Pig barn holds before haulers take them out. Live. |
| `PigVisuals` | true | Pig body, name and icon in Pig barns. Live. |
| `PigScale` | 1.0 | Size of the pig body. 1 is about a meter long. Live. |
| `PigSounds` | true | Grunts, the breathing loop and the slaughter squeal. Live. |
| `PigGruntVolume` | 0.6 | Volume of grunts and squeals, 0 to 1, on top of the game's sliders. Live. |
| `PigBreathingVolume` | 0.25 | Volume of the breathing loop on every pig; 0 turns it off. Live. |
| `PigGruntIntervalMin` | 6 | Shortest wait in seconds between grunts across all pigs. Live. |
| `PigGruntIntervalMax` | 20 | Longest wait in seconds between grunts across all pigs. Live. |
| `PigSoundRange` | 40 | Distance in meters at which a grunt fades out. Breathing carries 40% of it, a squeal 150%. Live. |
| `PigClickSound` | true | A clicked pig, and a clicked Pig Barn, grunt instead of playing the goat's sounds. Live. |
| `PigClickGrunt` | 0 | Which grunt answers a click: 0 is the click grunt made for it, 1 to 17 picks one of the ambient grunts, -1 plays a random one each time. Changing it plays the grunt. Live. |
| `TallowCandleEnabled` | true | The tallow candle recipe at the Candle Shop. Restart. |
| `TallowCandleTallowMultiplier` | 2.0 | Tallow in that recipe relative to the vanilla wax count. Live. |

## Known limitations

- A relocated Sheep or Pig barn comes back as a goat barn. Switch it again.
- On a save from before the mod, or from before the garments existed, tick wool and the garments once in the Trading Center's filter. Storehouses, depots, stockyards, granaries, root cellars, treasuries and markets tick them automatically.
- Slaughtering a sheep yields the goat's meat, hide and tallow.
- The sheep's fleece shades a little unevenly at some angles, and the barn's grazing, herd-size and divide buttons keep their goat artwork.
- A pig's coat (pink, black or spotted) is rolled again each time the save loads, and an idle pig stands still: there is no fidget animation.
- A walking pig's legs move slower than the ground it covers. Its walk clip has a much shorter stride than the goat's, and pigs travel at the goat's speed.

## Removing the mod

Farthest Frontier has no graceful path for items it does not know, and a save that ever changed the quota or production limit of a mod item keeps a record the game looks up by name with no null check. Before removing the mod from a save:

1. Switch every Sheep barn and Pig barn back to Goats.
2. Set the quota and production limits of wool and the three garments back to automatic.
3. Use up or sell all wool and garments, on villagers included (they wear out).
4. Turn the Candle Shop's tallow candle recipe off and let its current work order finish.
5. Save. Then remove the DLL.

Mushrooms are a vanilla item, so a Pig barn's stock needs no cleanup. A save loaded without the mod while it still holds wool or garments logs errors for every stack and may misbehave.

## Building from source

```bash
dotnet msbuild -restore -p:Configuration=Release LiveStockMarket.csproj
```

The build copies the DLL into the game's `Mods` folder (`CopyToMods` target; the game can be running). Paths to the game and its assemblies are at the top of `LiveStockMarket.csproj`.

The sheep and pig models ship as embedded Unity AssetBundles. Rebuild them only after changing `art/sheep/` or `art/Pig/export/` (Unity 2022.3.62f3 through Unity Hub required):

```bash
./tools/build-bundle.sh
```

The pig's game-ready FBX comes from the Auto-Rig Pro source through the headless Blender scripts in [tools/blender/](tools/blender/README.md).

Development history, mechanics notes and the in-game checklists that guided each step are in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md); the plan of record is [HANDOFF.md](HANDOFF.md). The Steam Workshop text is in [docs/steam-description.md](docs/steam-description.md) and change notes in [docs/change-notes.md](docs/change-notes.md).

## Credits

- Sheep model: a CGTrader asset, decimated and skinned to the game's goat skeleton.
- Pig model: SageDragoon's own Auto-Rig Pro rig and animations on a Tripo-generated mesh, pruned and decimated for the game.
- Pig sounds: grunts and squeals by gsmsea and pig breathing by freesound_community, both on Pixabay, cut and resampled for the game. The click grunt is SageDragoon's own.
- Icons and the rest: SageDragoon.
