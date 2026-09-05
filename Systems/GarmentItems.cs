using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// The three wool garments (step 5, pulled ahead of the sheep visuals). Each
    /// is a mod item cloned from the vanilla clothing item it replaces, priced at
    /// that item's price times the garment price multiplier, same weight and wear.
    /// Wearable behavior (warmth, seeking) lives in GarmentPatches; recipes in
    /// GarmentRecipes.
    /// </summary>
    internal static class GarmentItems
    {
        public static readonly ModItemDef WinterBoots = new ModItemDef
        {
            Name = "ItemWinterBoots",
            Id = (ItemID)((int)ItemID.MAX + 2),
            TemplateName = "ItemShoes",
            TemplateId = ItemID.Shoes,
            DisplayName = "Winter Boots",
            Description = "Wool-lined leather boots. Warmer than shoes.",
            IconResource = "LSM.winter_boots.png",
            IconLooseFile = "winter_boots.png",
            PriceMultiplier = () => PriceMultiplier,
        };

        public static readonly ModItemDef WinterCloak = new ModItemDef
        {
            Name = "ItemWinterCloak",
            Id = (ItemID)((int)ItemID.MAX + 3),
            TemplateName = "ItemHideCoat",
            TemplateId = ItemID.HideCoat,
            DisplayName = "Winter Cloak",
            Description = "A wool-lined leather cloak. Warmer than a hide coat.",
            IconResource = "LSM.winter_cloak.png",
            IconLooseFile = "winter_cloak.png",
            PriceMultiplier = () => PriceMultiplier,
        };

        public static readonly ModItemDef WoolenClothes = new ModItemDef
        {
            Name = "ItemWoolenClothes",
            Id = (ItemID)((int)ItemID.MAX + 4),
            TemplateName = "ItemLinenClothes",
            TemplateId = ItemID.LinenClothes,
            DisplayName = "Woolen Clothes",
            Description = "Clothes woven from wool. Warmer than linen.",
            IconResource = "LSM.woolen_clothes.png",
            IconLooseFile = "woolen_clothes.png",
            PriceMultiplier = () => PriceMultiplier,
        };

        public static readonly ModItemDef[] All = { WinterBoots, WinterCloak, WoolenClothes };

        internal static float PriceMultiplier =>
            Mathf.Clamp(LiveStockMarketMod.cfgGarmentPriceMultiplier?.Value ?? 1.25f, 0.5f, 5f);

        /// <summary>Warmth: a garment counts as its vanilla item at this effectiveness.</summary>
        internal static float WarmthMultiplier =>
            Mathf.Clamp(LiveStockMarketMod.cfgGarmentWarmthMultiplier?.Value ?? 1.25f, 1f, 3f);

        public static void DefineAll()
        {
            foreach (var g in All) ModItems.Define(g);
        }

        /// <summary>The garment that replaces a vanilla clothing item, or null.</summary>
        public static ModItemDef ForVanilla(ItemID vanillaId)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].TemplateId == vanillaId) return All[i];
            return null;
        }

        public static bool IsVanillaClothing(ItemID id)
            => id == ItemID.Shoes || id == ItemID.HideCoat || id == ItemID.LinenClothes;
    }
}
