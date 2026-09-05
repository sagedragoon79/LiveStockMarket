# Live-Stock Market — Steam Workshop Description

**Copy everything below the line into the Steam Workshop description field.**

---

[h1]Live-Stock Market[/h1]
A wool economy for Farthest Frontier v1.1.x — sheep barns, shearing, and wool clothing.

[h2]The Problem[/h2]
Goats give milk and meat, and that is the end of the animal-fiber story. There is no wool, nothing warmer than a hide coat, and no reason to keep a second barn once the cheese is flowing.

[b]Live-Stock Market[/b] lets any goat barn keep sheep instead, adds wool as a real item that is stored, counted and traded, and gives the Cobbler, Tannery and Weaver something to make with it.

[h2]Sheep Barns[/h2]
Every goat barn, both tiers, gets [b]Goats / Sheep[/b] buttons on its portrait.
[list]
[*]Switch to Sheep: the barn becomes a Sheep Barn, its animals turn into sheep, newborns and goats bought from traders arrive as sheep, and the herders shear wool in season instead of milking.
[*]Switch back: everything returns to goats. The choice is saved with the barn.
[*]The barn window, the animal's name and icon, and the herder's "Shearing Sheep" task all follow the mode.
[/list]

[h2]Wool[/h2]
[list]
[*]A new item, stored wherever hides go and listed under Produced Materials in the Settlement Items window.
[*]The agricultural trader and the hunter-and-herder trader may carry it; the Trading Center buys and sells it.
[/list]

[h2]Shearing[/h2]
[list]
[*]Sheep grow a fleece over the year (240 days by default) and are shorn during the shearing season, days 78 to 200 by default, once per year.
[*]A fully grown fleece yields 4 wool per sheep; a younger fleece yields proportionally less.
[*]Fleece growth banks across a switch to Goats and back, so flipping the barn does not cheat the calendar.
[/list]

[h2]Wool Garments[/h2]
Three trade goods that double as warmer clothing. Each producer gets the garment as an extra recipe beside its vanilla one, so you set the mix with the usual sliders.
[list]
[*][b]Winter Boots[/b] — Cobbler Shop: the shoes' leather x2 plus 5 wool. Replaces shoes.
[*][b]Winter Cloak[/b] — Tannery: the hide coat's leather x2 plus 5 wool. Replaces the hide coat.
[*][b]Woolen Clothes[/b] — Weaver: the linen clothes' flax x2 plus 5 wool. Replaces linen clothes.
[/list]
Each is 25% warmer than the item it replaces and priced 25% higher. Villagers take a garment when one is in stock and fall back to the vanilla item otherwise; whatever they wear stays on until it wears out.

[h2]Configuration[/h2]
With [b]Keep Clarity[/b] installed, everything is in its F10 panel under Live-Stock Market; otherwise edit UserData/MelonPreferences.cfg.
[list]
[*][b]Garments[/b] — warmth multiplier, wool per garment, input multiplier, price multiplier.
[*][b]Shearing[/b] — season start and end, cooldown, wool per sheep, fleece growth days, worker seconds per wool, barn wool capacity.
[*][b]Visuals[/b] — sheep model on or off, game shader on the sheep.
[/list]
All but the price multiplier and the master switch apply live.

[h2]Compatibility[/h2]
[list]
[*]Farthest Frontier v1.1.x (Mono), MelonLoader 0.7.
[*]Safe to add to an existing save. Storehouses accept wool and the garments automatically; on an older save, tick them once in the Trading Center's filter.
[*]Touches only goat barns, the three clothing producers, storage filters, merchants and the villager clothing checks. Mods that replace those systems outright may conflict; Keep Clarity is optional and supported.
[/list]

[h2]Installation[/h2]
[list]
[*]Install MelonLoader 0.7 for Farthest Frontier (Mono).
[*]Drop LiveStockMarket.dll into the game's Mods folder. That is the whole mod: the sheep model and icons are inside the DLL.
[/list]

[h2]Removing the mod[/h2]
Farthest Frontier has no graceful path for items it does not know. Before removing the mod from a save: switch every Sheep barn back to Goats, set the quota and production limits of wool and the three garments back to automatic, use up or sell all wool and garments, then save. A save that still holds them, or that ever had their quota changed, will not load cleanly without the mod.

[h2]Known Limitations[/h2]
[list]
[*]A relocated Sheep barn comes back as a goat barn; switch it again.
[*]Slaughtering a sheep yields the goat's meat, hide and tallow.
[*]The sheep's fleece shades a little unevenly at some angles, and the barn's grazing, herd-size and divide buttons keep their goat artwork.
[/list]
