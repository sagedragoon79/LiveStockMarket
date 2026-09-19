using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// The pig body: a self-contained animated prefab (skeleton, skinned mesh, Animator
    /// with a controller built to the game's livestock contract) in the "pig" bundle,
    /// embedded in the DLL as LSM.pig; a loose Mods/LiveStockMarket/pig overrides it.
    /// Textures are loaded by asset name — bundle materials cannot be trusted to carry
    /// them in the player (see SheepAssets).
    /// </summary>
    internal static class PigAssets
    {
        private const string BundleFile   = "pig";
        private const string ResourceName = "LSM.pig";
        private const string PrefabName   = "Pig01A";
        private const string LooseFolder  = "LiveStockMarket";

        private static bool _tried;
        private static AssetBundle _bundle;
        private static readonly Dictionary<string, Texture2D> _texturesByName = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        public static GameObject Prefab { get; private set; }
        public static RuntimeAnimatorController Controller { get; private set; }
        public static Avatar Avatar { get; private set; }
        public static Material[] Materials { get; private set; }
        public static List<AudioClip> Grunts  { get; } = new List<AudioClip>();
        public static List<AudioClip> Squeals { get; } = new List<AudioClip>();
        public static AudioClip Breathing { get; private set; }
        /// <summary>The hand-made grunt that answers a click on a pig (PigClick in the bundle); not part of the ambient pool.</summary>
        public static AudioClip Click { get; private set; }

        private static string Tag => LiveStockMarketMod.LogTag;

        public static bool Available
        {
            get { EnsureLoaded(); return Prefab != null && Controller != null; }
        }

        public static Texture2D GetTexture(string name)
        {
            EnsureLoaded();
            return name != null && _texturesByName.TryGetValue(name, out var t) ? t : null;
        }

        private static void EnsureLoaded()
        {
            if (_tried) return;
            _tried = true;
            try
            {
                _bundle = LoadBundle();
                if (_bundle == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} PigAssets: bundle '{BundleFile}' unavailable — pigs keep the goat look.");
                    return;
                }
                var prefab = _bundle.LoadAsset<GameObject>(PrefabName);
                if (prefab == null)
                    foreach (var n in _bundle.GetAllAssetNames())
                    {
                        prefab = _bundle.LoadAsset<GameObject>(n);
                        if (prefab != null) break;
                    }
                if (prefab == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} PigAssets: no prefab in the bundle ({string.Join(", ", _bundle.GetAllAssetNames())}).");
                    return;
                }
                var animator = prefab.GetComponent<Animator>() ?? prefab.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} PigAssets: prefab '{prefab.name}' has no Animator with a controller.");
                    return;
                }
                Prefab = prefab;
                Controller = animator.runtimeAnimatorController;
                Avatar = animator.avatar;
                prefab.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                Controller.hideFlags |= HideFlags.DontUnloadUnusedAsset;

                _texturesByName.Clear();
                var names = new List<string>();
                foreach (var t in _bundle.LoadAllAssets<Texture2D>())
                {
                    if (t == null) continue;
                    t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    _texturesByName[t.name] = t;
                    names.Add($"{t.name} {t.width}x{t.height}");
                }
                var smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Materials = smr != null ? smr.sharedMaterials : new Material[0];
                foreach (var m in Materials)
                {
                    if (m == null) continue;
                    m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    var live = m.shader != null ? Shader.Find(m.shader.name) : null;
                    if (live != null && live != m.shader) m.shader = live;
                }
                // voices: PigGruntNN, PigSquealNN, PigBreathing (mono Vorbis in the bundle)
                Grunts.Clear(); Squeals.Clear(); Breathing = null; Click = null;
                foreach (var c in _bundle.LoadAllAssets<AudioClip>())
                {
                    if (c == null) continue;
                    c.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    if (c.name.StartsWith("PigClick", StringComparison.OrdinalIgnoreCase)) Click = c;
                    else if (c.name.StartsWith("PigGrunt", StringComparison.OrdinalIgnoreCase)) Grunts.Add(c);
                    else if (c.name.StartsWith("PigSqueal", StringComparison.OrdinalIgnoreCase)) Squeals.Add(c);
                    else if (c.name.StartsWith("PigBreath", StringComparison.OrdinalIgnoreCase)) Breathing = c;
                }
                Grunts.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
                Squeals.Sort((x, y) => string.CompareOrdinal(x.name, y.name));

                var b = smr != null && smr.sharedMesh != null ? smr.sharedMesh.bounds.size : Vector3.zero;
                LiveStockMarketMod.Log.Msg($"{Tag} PigAssets: '{prefab.name}' mesh bounds {b.x:F3}×{b.y:F3}×{b.z:F3}, bones {(smr != null && smr.bones != null ? smr.bones.Length : 0)}, " +
                    $"controller '{Controller.name}' ({Controller.animationClips.Length} clips), avatar {(Avatar != null ? Avatar.name : "none")}, textures: {(names.Count > 0 ? string.Join(", ", names.ToArray()) : "NONE")}; " +
                    $"audio: {Grunts.Count} grunt(s), {Squeals.Count} squeal(s), breathing {(Breathing != null ? Breathing.length.ToString("F1") + " s" : "none")}, click {(Click != null ? Click.length.ToString("F2") + " s" : "none")}.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} PigAssets: load failed: {ex}");
                Prefab = null;
            }
        }

        private static AssetBundle LoadBundle()
        {
            try
            {
                string modsDir = Path.GetDirectoryName(typeof(PigAssets).Assembly.Location);
                if (!string.IsNullOrEmpty(modsDir))
                {
                    string loose = Path.Combine(Path.Combine(modsDir, LooseFolder), BundleFile);
                    if (File.Exists(loose))
                    {
                        var fromFile = AssetBundle.LoadFromFile(loose);
                        if (fromFile != null)
                        {
                            LiveStockMarketMod.Log.Msg($"{Tag} PigAssets: bundle loaded from override file {loose}.");
                            return fromFile;
                        }
                        LiveStockMarketMod.Log.Warning($"{Tag} PigAssets: LoadFromFile failed for {loose} — trying the embedded copy.");
                    }
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} PigAssets: loose bundle check failed: {ex.Message}");
            }

            byte[] bytes;
            using (var stream = typeof(PigAssets).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} PigAssets: embedded resource '{ResourceName}' missing — broken build?");
                    return null;
                }
                bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
            }
            var fromMem = AssetBundle.LoadFromMemory(bytes);
            if (fromMem == null) LiveStockMarketMod.Log.Warning($"{Tag} PigAssets: LoadFromMemory failed for '{ResourceName}' ({bytes.Length} bytes).");
            else LiveStockMarketMod.Log.Msg($"{Tag} PigAssets: embedded bundle loaded ({bytes.Length / 1024} KB).");
            return fromMem;
        }
    }
}
