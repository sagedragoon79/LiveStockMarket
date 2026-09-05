# Live-Stock Market

A wool economy for Farthest Frontier (v1.1.x, MelonLoader 0.7). Turn a goat barn into a sheep barn, shear wool every year, and craft it into warmer clothing or sell it. By SageDragoon.

## What it adds

**Sheep barns.** Every goat barn, both tiers, has Goats / Sheep buttons on its portrait. Switch to Sheep and the barn becomes a Sheep Barn: its animals turn into sheep, newborns and goats bought from traders arrive as sheep, and the herders shear wool in season instead of milking. Switch back and everything returns to goats. The choice is saved with the barn.

**Wool.** A new item. It is stored wherever hides are, counted under Produced Materials in the Settlement Items window, and traded: the agricultural trader and the hunter-and-herder trader may carry it, and the Trading Center buys and sells it.

**Shearing.** Sheep grow a fleece over the year (240 days by default) and are shorn during the shearing season (days 78 to 200 by default, the goat milking window), once per year. A fully grown fleece yields 4 wool per sheep; a younger fleece yields proportionally less. Fleece growth banks across a switch to Goats and back.

**Wool garments.** Three trade goods that double as warmer clothing. Each producer gets the garment as an extra recipe beside its vanilla one, so you set the mix with the usual sliders.

| Garment | Made at | Recipe (default) | Replaces |
|---|---|---|---|
| Winter Boots | Cobbler Shop | the shoes' leather x2 + 5 wool | Shoes |
| Winter Cloak | Tannery | the hide coat's leather x2 + 5 wool | Hide Coat |
| Woolen Clothes | Weaver | the linen clothes' flax x2 + 5 wool | Linen Clothes |

Each is 25% warmer than the item it replaces and priced 25% higher. Villagers take a garment when one is in stock and fall back to the vanilla item otherwise; whatever they wear stays on until it wears out.

## Installation

1. Install MelonLoader 0.7 for Farthest Frontier (Mono).
2. Put `LiveStockMarket.dll` in the game's `Mods` folder. That is the whole mod: the sheep model and the icons are inside the DLL.
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

## Known limitations

- A relocated Sheep barn comes back as a goat barn. Switch it again.
- On a save from before the mod, or from before the garments existed, tick wool and the garments once in the Trading Center's filter. Storehouses, depots, stockyards, granaries, root cellars, treasuries and markets tick them automatically.
- Slaughtering a sheep yields the goat's meat, hide and tallow.
- The sheep's fleece shades a little unevenly at some angles, and the barn's grazing, herd-size and divide buttons keep their goat artwork.

## Removing the mod

Farthest Frontier has no graceful path for items it does not know, and a save that ever changed the quota or production limit of a mod item keeps a record the game looks up by name with no null check. Before removing the mod from a save:

1. Switch every Sheep barn back to Goats.
2. Set the quota and production limits of wool and the three garments back to automatic.
3. Use up or sell all wool and garments, on villagers included (they wear out).
4. Save. Then remove the DLL.

A save loaded without the mod while it still holds wool or garments logs errors for every stack and may misbehave.

## Building from source

```bash
dotnet msbuild -restore -p:Configuration=Release LiveStockMarket.csproj
```

The build copies the DLL into the game's `Mods` folder (`CopyToMods` target; the game can be running). Paths to the game and its assemblies are at the top of `LiveStockMarket.csproj`.

The sheep model ships as an embedded Unity AssetBundle. Rebuild it only after changing `art/sheep/` (Unity 2022.3.62f3 through Unity Hub required):

```bash
./tools/build-bundle.sh
```

Development history, mechanics notes and the in-game checklists that guided each step are in [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md); the plan of record is [HANDOFF.md](HANDOFF.md). The Steam Workshop text is in [docs/steam-description.md](docs/steam-description.md) and change notes in [docs/change-notes.md](docs/change-notes.md).

## Credits

- Sheep model: a CGTrader asset, decimated and skinned to the game's goat skeleton.
- Icons and the rest: SageDragoon.
