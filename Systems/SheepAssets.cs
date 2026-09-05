using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// The sheep model. Ships as a Unity AssetBundle ("sheep", built by
    /// tools/build-bundle.sh with FF's Unity 2022.3.62f3 from art/sheep) embedded in
    /// the DLL as LSM.sheep; a loose Mods/LiveStockMarket/sheep overrides it for
    /// artist hot-swaps (BoatKeys' BoatAssets pattern).
    ///
    /// The bundle's prefab is the sheep skinned to the GOAT's skeleton (same bone
    /// names, art/sheep/sheep_rig.blend). We only keep what the runtime rebind
    /// needs: the mesh, the bone names in the mesh's bone order, and the textures.
    /// The skeleton itself is never instantiated — each animal keeps its own goat
    /// bones and Animator; see SheepVisuals.
    /// </summary>
    internal static class SheepAssets
    {
        private const string BundleFile   = "sheep";        // loose override file name (no extension)
        private const string ResourceName = "LSM.sheep";    // embedded copy
        private const string PrefabName   = "Sheep01A";
        private const string LooseFolder  = "LiveStockMarket";

        private static bool _tried;
        private static AssetBundle _bundle;

        public static Mesh      Mesh      { get; private set; }
        public static string[]  BoneNames { get; private set; }   // index = mesh bone index
        public static string    RootBone  { get; private set; }
        public static Texture2D[] Textures { get; private set; } // per submesh: body, head, eye
        public static Material[]  Materials { get; private set; } // bundle materials (Standard), fallback only
        private static readonly Dictionary<string, Texture2D> _texturesByName = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        /// <summary>A bundle texture by asset name (SheepBody / SheepHead / SheepEye), loaded straight
        /// from the bundle — the bundle's materials cannot be trusted to carry their textures.</summary>
        public static Texture2D GetTexture(string name)
        {
            EnsureLoaded();
            return name != null && _texturesByName.TryGetValue(name, out var t) ? t : null;
        }

        private static string Tag => LiveStockMarketMod.LogTag;

        public static bool Available
        {
            get { EnsureLoaded(); return Mesh != null && BoneNames != null && BoneNames.Length > 0; }
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
                    LiveStockMarketMod.Log.Warning($"{Tag} SheepAssets: bundle '{BundleFile}' unavailable — sheep keep the goat look.");
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
                    LiveStockMarketMod.Log.Warning($"{Tag} SheepAssets: no prefab in the bundle ({string.Join(", ", _bundle.GetAllAssetNames())}).");
                    return;
                }
                var smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr == null || smr.sharedMesh == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} SheepAssets: prefab '{prefab.name}' has no skinned mesh.");
                    return;
                }
                Mesh = smr.sharedMesh;
                Mesh.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                var bones = smr.bones ?? new Transform[0];
                BoneNames = new string[bones.Length];
                for (int i = 0; i < bones.Length; i++) BoneNames[i] = bones[i] != null ? bones[i].name : null;
                RootBone = smr.rootBone != null ? smr.rootBone.name : null;

                // textures as their own assets, by name (the reliable path)
                _texturesByName.Clear();
                var names = new List<string>();
                foreach (var t in _bundle.LoadAllAssets<Texture2D>())
                {
                    if (t == null) continue;
                    t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    _texturesByName[t.name] = t;
                    names.Add($"{t.name} {t.width}x{t.height}");
                }
                LiveStockMarketMod.Log.Msg($"{Tag} SheepAssets: bundle textures: {(names.Count > 0 ? string.Join(", ", names.ToArray()) : "NONE")}.");

                var mats = smr.sharedMaterials ?? new Material[0];
                Materials = mats;
                var texs = new List<Texture2D>();
                foreach (var m in mats)
                {
                    if (m == null) { texs.Add(null); continue; }
                    m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    var live = m.shader != null ? Shader.Find(m.shader.name) : null;   // bundle shader → the game's copy
                    if (live != null && live != m.shader) m.shader = live;
                    var t = m.mainTexture as Texture2D;
                    if (t != null) t.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    texs.Add(t);
                }
                Textures = texs.ToArray();

                var b = Mesh.bounds;
                LiveStockMarketMod.Log.Msg($"{Tag} SheepAssets: '{prefab.name}' mesh {Mesh.vertexCount} verts, {Mesh.subMeshCount} submeshes, " +
                    $"bounds {b.size.x:F4}×{b.size.y:F4}×{b.size.z:F4}, {BoneNames.Length} bones (root {RootBone ?? "none"}), " +
                    $"{Textures.Length} textures.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepAssets: load failed: {ex}");
                Mesh = null;
            }
        }

        private static AssetBundle LoadBundle()
        {
            try
            {
                string modsDir = Path.GetDirectoryName(typeof(SheepAssets).Assembly.Location);
                if (!string.IsNullOrEmpty(modsDir))
                {
                    string loose = Path.Combine(Path.Combine(modsDir, LooseFolder), BundleFile);
                    if (File.Exists(loose))
                    {
                        var fromFile = AssetBundle.LoadFromFile(loose);
                        if (fromFile != null)
                        {
                            LiveStockMarketMod.Log.Msg($"{Tag} SheepAssets: bundle loaded from override file {loose}.");
                            return fromFile;
                        }
                        LiveStockMarketMod.Log.Warning($"{Tag} SheepAssets: LoadFromFile failed for {loose} — trying the embedded copy.");
                    }
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepAssets: loose bundle check failed: {ex.Message}");
            }

            byte[] bytes;
            using (var stream = typeof(SheepAssets).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    LiveStockMarketMod.Log.Warning($"{Tag} SheepAssets: embedded resource '{ResourceName}' missing — broken build?");
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
            if (fromMem == null) LiveStockMarketMod.Log.Warning($"{Tag} SheepAssets: LoadFromMemory failed for '{ResourceName}' ({bytes.Length} bytes).");
            else LiveStockMarketMod.Log.Msg($"{Tag} SheepAssets: embedded bundle loaded ({bytes.Length / 1024} KB).");
            return fromMem;
        }
    }
}
