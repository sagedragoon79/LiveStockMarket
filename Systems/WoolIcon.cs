using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>The wool item icon (art/wool_icon.png, embedded as LSM.wool_icon.png). See ModIcons.</summary>
    internal static class WoolIcon
    {
        private const string ResourceName  = "LSM.wool_icon.png";
        private const string LooseFileName = "wool_icon.png";

        public static Sprite Sprite => ModIcons.Get(ResourceName, LooseFileName);
    }
}
