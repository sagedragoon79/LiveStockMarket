using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// ItemWool — the mod's first genuinely new item (step 2). Definition lives in
    /// the shared ModItems registry; this class keeps the wool constants and the
    /// forwarding helpers the wool-specific code uses.
    /// </summary>
    internal static class WoolItem
    {
        public const string ItemName          = "ItemWool";
        public const string TemplateItemName  = "ItemHide";   // numbers, category, storage placement
        public const string CarryPropItemName = "ItemFlax";   // carried mesh: a cream bundle

        public static readonly ItemID ItemId = (ItemID)((int)ItemID.MAX + 1);

        public const string DisplayName = "Wool";
        public const string DetailedDescription = "Raw wool shorn from sheep. Spun and woven into cloth.";

        internal static readonly ModItemDef Def = new ModItemDef
        {
            Name = ItemName,
            Id = ItemId,
            TemplateName = TemplateItemName,
            TemplateId = ItemID.Hide,
            CarryPropName = CarryPropItemName,
            DisplayName = DisplayName,
            Description = DetailedDescription,
            IconResource = "LSM.wool_icon.png",
            IconLooseFile = "wool_icon.png",
            LifetimeMonths = 0,   // wool doesn't spoil
        };

        public static bool IsRegistered => ModItems.IsRegistered;

        public static bool IsWool(Item item) => item != null && item.itemID == ItemId;

        /// <summary>A fresh Item instance for wool (needed = false). Requires registration.</summary>
        public static Item NewItem() => new Item(ItemName);

        public static bool EnsureRegistered() => ModItems.EnsureRegistered();

        public static void RaiseItemStorageLimit() => ModItems.RaiseItemStorageLimit();
    }
}
