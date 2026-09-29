# Live-Stock Market — Steam Workshop Description

**Copy the whole fenced block below (without the fence lines) into the Steam Workshop description field.** It is Steam BBCode; a Markdown preview would otherwise hide the [*] markers. Steam caps a description at 8,000 characters; check the count after every edit.

---

```
[h1]Live-Stock Market[/h1]
Sheep, pigs and bigger herds for Farthest Frontier v1.1.x. Any goat barn can keep goats, sheep or pigs: shear wool and turn it into warmer clothing, or let pigs root up mushrooms in the woods and butcher them for more meat and tallow. Every livestock building can hold up to twice its vanilla herd.

[h2]The Problem[/h2]
Goats give milk and meat, and that is the end of the small-livestock story. There is no wool, nothing warmer than a hide coat, no animal that earns its keep in the woods, and no reason to build a second barn once the cheese is flowing.

[b]Live-Stock Market[/b] gives the goat barn two more herds to choose from, adds wool as a real item that is stored, counted and traded, gives the Cobbler, Tannery and Weaver something to make with it, and lets the Candle Shop burn the extra tallow.

[h2]One Barn, Three Herds[/h2]
Every goat barn, both tiers, gets [b]Goats / Sheep / Pigs[/b] buttons on its portrait.
[list]
[*]Switch the barn and its animals change with it. Newborns and goats bought from traders arrive as whatever the barn keeps.
[*]The barn's name, the barn window, and each animal's name, icon and portrait follow the choice: Sheep Barn, Pig Barn, Large Pig Barn.
[*]Switch back to Goats and everything returns to vanilla. The choice is saved with the barn.
[/list]

[h2]Bigger Herds[/h2]
[list]
[*]Each livestock building type has its own capacity setting, up to twice the vanilla maximum herd: cow barns, goat barns (goats, sheep and pigs), chicken coops, stables, dog kennels and cat kennels.
[*]New buildings start at the new maximum, and buildings set to the maximum follow the setting. Lowering it below a herd sends the extras to the butcher.
[/list]

[h1]Sheep and Wool[/h1]

[h2]Shearing[/h2]
[list]
[*]In a Sheep Barn the herders shear wool in season instead of milking, and their task reads "Shearing Sheep".
[*]Sheep grow a fleece over the year (240 days by default) and are shorn during the shearing season, days 78 to 200 by default, once per year.
[*]A fully grown fleece yields 4 wool per sheep; a younger fleece yields proportionally less.
[*]Fleece growth banks across a switch to Goats and back, so flipping the barn does not cheat the calendar.
[/list]

[h2]Wool[/h2]
[list]
[*]A new item, stored wherever hides go and listed under Produced Materials in the Settlement Items window.
[*]The agricultural trader and the hunter-and-herder trader may carry it; the Trading Center buys and sells it.
[/list]

[h2]Wool Garments[/h2]
Three trade goods that double as warmer clothing. Each producer gets the garment as an extra recipe beside its vanilla one, so you set the mix with the usual sliders.
[list]
[*][b]Winter Boots[/b] — Cobbler Shop: the shoes' leather x2 plus 5 wool. Replaces shoes.
[*][b]Winter Cloak[/b] — Tannery: the hide coat's leather x2 plus 5 wool. Replaces the hide coat.
[*][b]Woolen Clothes[/b] — Weaver: the linen clothes' flax x2 plus 5 wool. Replaces linen clothes.
[/list]
Each is 25% warmer than the item it replaces and priced 25% higher. Villagers take whichever of the garment or the vanilla item they reach first; whatever they wear stays on until it wears out. Fetching missing clothing is urgent for every villager, soldiers included.

[h1]Pigs[/h1]

[h2]Pig Barns[/h2]
[list]
[*]Pigs have their own body, their own walk, graze and death animations, and three coats.
[*]Pigs are not milked or shorn. The herders keep them fed and butchered; the pigs do the rest themselves.
[*]They eat the same fodder as goats, breed at twice the goat rate, and produce one and a half times the waste for your compost yard.
[/list]

[h2]Truffle Pigs[/h2]
[list]
[*]Every day from spring through autumn, each grown pig turns up mushrooms in proportion to how wooded the barn's grazing area is. Twelve or more trees inside it give the full rate (0.05 mushrooms per pig per day), fewer trees give less, and no grazing area gives none.
[*]The mushrooms collect in the barn (100 by default) and haulers carry them out. No herder time is spent.
[*]Place the grazing area over woodland and a pig barn feeds the town between butcherings.
[/list]

[h2]Butchering[/h2]
A pig gives twice the meat, twice the tallow and one and a half times the hide of a goat.

[h2]Pig Sounds[/h2]
[list]
[*]Pigs grunt now and then, breathe quietly when you zoom in close, and squeal at the butcher.
[*]Click a pig, or the Pig Barn itself, and it answers with a grunt instead of the goat's bell and bleat.
[*]Everything runs through the game's own sound sliders and goes quiet when you pause.
[/list]

[h1]And for the Tallow[/h1]

[h2]Tallow Candles[/h2]
The Candle Shop gets a second candle recipe beside the vanilla one: 4 tallow in place of the 2 wax, with the usual firewood. Set the mix with the usual sliders.

[h2]Configuration[/h2]
With [b]Keep Clarity[/b] installed, everything is in its F10 panel under Live-Stock Market; otherwise edit UserData/MelonPreferences.cfg.
[list]
[*][b]Garments[/b] — warmth multiplier, wool per garment, input multiplier, price multiplier.
[*][b]Shearing[/b] — season start and end, cooldown, wool per sheep, fleece growth days, worker seconds per wool, barn wool capacity.
[*][b]Pigs[/b] — meat, tallow, hide, breeding and waste multipliers, mushrooms per pig per day, trees for the full yield, mushroom capacity.
[*][b]Sounds[/b] — pig sounds on or off, grunt volume, breathing volume, grunt interval, range, click sound on or off and which grunt it plays.
[*][b]Capacity[/b] — cow barn, goat barn, chicken coop, stable, dog kennel and cat kennel, 1x to 2x.
[*][b]Recipes[/b] — tallow candles on or off, tallow per wax.
[*][b]Visuals[/b] — sheep model on or off, game shader on the sheep, pig model on or off, pig size.
[/list]
All but the price multiplier, the tallow candle switch and the master switch apply live.

[h2]Compatibility[/h2]
[list]
[*]Farthest Frontier v1.1.x (Mono), MelonLoader 0.7.
[*]Safe to add to an existing save. Storehouses accept wool and the garments automatically; on an older save, tick them once in the Trading Center's filter.
[*]Touches only goat barns, the maximum herd size of livestock buildings, the three clothing producers, the Candle Shop's recipe list, storage filters, merchants, the Trading Center's keep-in-stock list and the villager clothing checks. Mods that replace those systems outright may conflict; Keep Clarity is optional and supported.
[/list]

[h2]Installation[/h2]
[list]
[*]Install MelonLoader 0.7 for Farthest Frontier (Mono).
[*]Drop LiveStockMarket.dll into the game's Mods folder. That is the whole mod: the sheep and pig models, the pig sounds and the icons are inside the DLL.
[/list]

[h2]Removing the mod[/h2]
Farthest Frontier has no graceful path for items it does not know. Before removing the mod from a save: switch every Sheep and Pig barn back to Goats, set every capacity back to 1x and let the herds shrink, set the quota and production limits of wool and the three garments back to automatic, use up or sell all wool and garments, turn the tallow candle recipe off and let its work order finish, then save. Mushrooms are a vanilla item and need no cleanup. A save that still holds wool or garments, or that ever had their quota changed, will not load cleanly without the mod.

[h2]Known Limitations[/h2]
[list]
[*]A relocated Sheep or Pig barn comes back as a goat barn; switch it again.
[*]Slaughtering a sheep yields the goat's meat, hide and tallow.
[*]The sheep's fleece shades a little unevenly at some angles, and the barn's grazing, herd-size and divide buttons keep their goat artwork.
[*]A pig's coat is rolled again each time the save loads, and an idle pig stands still.
[*]A walking pig's legs move slower than the ground it covers: its walk has a shorter stride than the goat's, and pigs travel at the goat's speed.
[/list]
```
