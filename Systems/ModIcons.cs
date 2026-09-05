using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// Embedded PNG → Sprite, with mipmaps so a 256×256 master renders clean at
    /// every UI size (storage filters at ~32 px up to detail views over 100 px).
    /// A loose Mods/LiveStockMarket/&lt;file&gt; overrides the embedded copy for
    /// artist hot-swaps, BoatKeys style. Sprites are built once and kept alive.
    /// </summary>
    internal static class ModIcons
    {
        private const string LooseFolder = "LiveStockMarket";

        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> _failed = new HashSet<string>();

        private static string Tag => LiveStockMarketMod.LogTag;

        /// <summary>Returns the sprite for an embedded resource (null if it can't be built).</summary>
        public static Sprite Get(string resourceName, string looseFileName)
        {
            if (_cache.TryGetValue(resourceName, out var cached) && cached != null) return cached;
            if (_failed.Contains(resourceName)) return null;

            var sprite = Load(resourceName, looseFileName);
            if (sprite != null) _cache[resourceName] = sprite;
            else _failed.Add(resourceName);
            return sprite;
        }

        private static Sprite Load(string resourceName, string looseFileName)
        {
            try
            {
                byte[] png = null;
                string source = "embedded";

                try
                {
                    string modsDir = Path.GetDirectoryName(typeof(ModIcons).Assembly.Location);
                    if (!string.IsNullOrEmpty(modsDir) && !string.IsNullOrEmpty(looseFileName))
                    {
                        string loose = Path.Combine(Path.Combine(modsDir, LooseFolder), looseFileName);
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
                    using (var stream = typeof(ModIcons).Assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                        {
                            LiveStockMarketMod.Log.Warning($"{Tag} ModIcons: embedded resource '{resourceName}' missing.");
                            return null;
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

                // Mipmaps: the UI draws these anywhere from ~32 px to >100 px.
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                if (!tex.LoadImage(png))
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} ModIcons: PNG decode failed for '{resourceName}' ({source}).");
                    return null;
                }
                tex.name = "LSM_" + resourceName;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Trilinear;
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;

                var sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = "LSM_" + resourceName;
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;

                LiveStockMarketMod.Log.Msg($"{Tag} ModIcons: loaded '{resourceName}' {tex.width}x{tex.height} from {source}.");
                return sprite;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} ModIcons: '{resourceName}': {ex.Message}");
                return null;
            }
        }
    }
}
