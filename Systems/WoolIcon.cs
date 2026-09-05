using System;
using System.IO;
using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// The wool item icon (art/wool_icon.png, embedded as LSM.wool_icon.png).
    /// A loose Mods/LiveStockMarket/wool_icon.png overrides the embedded copy for
    /// artist hot-swaps, BoatKeys style. Built once, kept alive for the session.
    /// </summary>
    internal static class WoolIcon
    {
        private const string ResourceName  = "LSM.wool_icon.png";
        private const string LooseFolder   = "LiveStockMarket";
        private const string LooseFileName = "wool_icon.png";

        private static Sprite _sprite;
        private static bool _tried;

        private static string Tag => LiveStockMarketMod.LogTag;

        public static Sprite Sprite
        {
            get
            {
                if (!_tried) Load();
                return _sprite;
            }
        }

        private static void Load()
        {
            _tried = true;
            try
            {
                byte[] png = null;
                string source = "embedded";

                try
                {
                    string modsDir = Path.GetDirectoryName(typeof(WoolIcon).Assembly.Location);
                    if (!string.IsNullOrEmpty(modsDir))
                    {
                        string loose = Path.Combine(Path.Combine(modsDir, LooseFolder), LooseFileName);
                        if (File.Exists(loose))
                        {
                            png = File.ReadAllBytes(loose);
                            source = loose;
                        }
                    }
                }
                catch { /* loose override is optional */ }

                if (png == null)
                {
                    using (var stream = typeof(WoolIcon).Assembly.GetManifestResourceStream(ResourceName))
                    {
                        if (stream == null)
                        {
                            LiveStockMarketMod.Log.Warning($"{Tag} WoolIcon: embedded resource '{ResourceName}' missing — wool shows without an icon.");
                            return;
                        }
                        png = new byte[stream.Length];
                        int read = 0;
                        while (read < png.Length)
                        {
                            int n = stream.Read(png, read, png.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }
                    }
                }

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(png))
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} WoolIcon: PNG decode failed ({source}).");
                    return;
                }
                tex.name = "LSM_WoolIcon";
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

                _sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                _sprite.name = "LSM_WoolIcon";
                _sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;

                LiveStockMarketMod.Log.Msg($"{Tag} WoolIcon: loaded {tex.width}x{tex.height} from {source}.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} WoolIcon: {ex.Message}");
            }
        }
    }
}
