using System;
using System.Collections.Generic;
using UnityEngine;
using LiveStockMarket.Patches;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// Per-animal record of the goat look, so a switch back to Goats is exact.
    /// </summary>
    internal sealed class SheepLook : MonoBehaviour
    {
        public SkinnedMeshRenderer Renderer;
        public Mesh GoatMesh;
        public Material[] GoatMaterials;
        public Transform[] GoatBones;
        public Transform GoatRootBone;
        public string GoatDisplayName;
        public string GoatDescTag;
        public bool GoatNameNeedsTranslation;
        public Sprite GoatIcon;
        public Sprite GoatImg;
        public bool IsSheep;

        // the sheep state this look should hold, re-checked once a second (the game's own
        // material / property-block writes would otherwise silently put the goat coat back)
        public Mesh SheepMesh;
        public Material[] SheepMaterials;
        public float NextCheck;
        public float DiagAt;
        public bool Diagnosed;

        private void LateUpdate()
        {
            if (!IsSheep || Renderer == null) return;
            if (Time.unscaledTime < NextCheck) return;
            NextCheck = Time.unscaledTime + 1f;
            SheepVisuals.Enforce(this);
        }
    }

    /// <summary>
    /// Step 4: animals in a Sheep barn wear the sheep model, name and icon.
    ///
    /// No new prefab, no second skeleton: each goat keeps its own bones, Animator
    /// and colliders, and only its SkinnedMeshRenderer changes — mesh, bone array
    /// (the goat's bones matched by name) and materials. The sheep mesh was fitted
    /// to the goat mesh in Blender and skinned to the goat's skeleton
    /// (art/sheep/sheep_rig.blend), so the goat's own bindposes are the right ones:
    /// a bone's skinning matrix is its delta from rest in renderer-root space,
    /// whatever axis convention the bone frames use. Only the unit scale can differ
    /// between the two FBX pipelines; BuildMesh measures it from the bounds.
    ///
    /// Applied from SheepShearing.ApplyMode (toggle, load, tier upgrade, herd
    /// adoption) and from Herd.AddAnimalToHerd (births, purchases, transfers).
    /// </summary>
    internal static class SheepVisuals
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        public static bool Enabled       => LiveStockMarketMod.cfgSheepVisuals?.Value ?? true;
        public static bool UseGameShader => LiveStockMarketMod.cfgSheepUseGameShader?.Value ?? true;

        public static Sprite Icon  => ModIcons.Get("LSM.sheep_icon.png",  "sheep_icon.png");
        public static Sprite Image => ModIcons.Get("LSM.sheep_image.png", "sheep_image.png");
        /// <summary>Optional 192×256 portrait for the animal's window (vanilla hiRezImg size); the single icon stands in without it.</summary>
        public static Sprite Portrait => ModIcons.Get("LSM.sheep_portrait.png", "sheep_portrait.png", optional: true);

        // One rebound sheep mesh per goat mesh: female, male and young prefabs each carry their own bindposes.
        private static readonly Dictionary<Mesh, Mesh> _meshByGoatMesh = new Dictionary<Mesh, Mesh>();
        private static Material[] _materials;
        private static bool _loggedScale, _loggedMissingRenderer;

        public static void Register()
        {
            LocalizationPatches.Register("LSM_Sheep_Name", "Sheep");
            LocalizationPatches.Register("LSM_Sheep_Description",
                "A sheep. It grazes like a goat and grows a fleece that the herders shear for wool in the shearing season.");
            LocalizationPatches.Register("LSM_VillagerState_ShearingSheep", "Shearing Sheep");
        }

        public static bool WantSheep(GoatBarn barn) => Enabled && barn != null && GoatBarnModeStore.IsSheep(barn);

        /// <summary>Every animal of the barn's herd gets the look its mode calls for.</summary>
        public static void ApplyToBarn(GoatBarn barn)
        {
            try
            {
                if (barn == null || barn.herd == null || barn.herd.animalsInHerdRO == null) return;
                bool sheep = WantSheep(barn);
                var animals = new List<LivestockAnimal>(barn.herd.animalsInHerdRO);
                int changed = 0;
                foreach (var a in animals) if (Apply(a, sheep)) changed++;
                if (changed > 0)
                    LiveStockMarketMod.Log.Msg($"{Tag} SheepVisuals: '{barn.displayName}' → {changed} animal(s) now look like {(sheep ? "sheep" : "goats")}.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepVisuals.ApplyToBarn: {ex.Message}");
            }
        }

        /// <summary>Re-evaluates every goat barn on the map (pref changes).</summary>
        public static void ApplyAll()
        {
            try
            {
                _materials = null;   // shader/material choice may have changed
                foreach (var look in UnityEngine.Object.FindObjectsOfType<SheepLook>())
                    if (look != null && look.IsSheep) { Revert(look); look.IsSheep = false; }
                foreach (var barn in UnityEngine.Object.FindObjectsOfType<GoatBarn>()) ApplyToBarn(barn);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepVisuals.ApplyAll: {ex.Message}");
            }
        }

        /// <summary>Returns true when the animal's look actually changed.</summary>
        public static bool Apply(LivestockAnimal animal, bool sheep)
        {
            try
            {
                if (animal == null || !(animal is Goat)) return false;
                var look = animal.GetComponent<SheepLook>();
                if (sheep)
                {
                    if (!SheepAssets.Available) return false;
                    if (look == null)
                    {
                        look = animal.gameObject.AddComponent<SheepLook>();
                        if (!Capture(animal, look)) { UnityEngine.Object.Destroy(look); return false; }
                    }
                    bool changed = false;
                    if (!look.IsSheep && Rebind(look)) { look.IsSheep = true; changed = true; }
                    if (look.IsSheep) SetBlackboard(animal, look, true);
                    return changed;
                }
                if (look == null || !look.IsSheep) return false;
                Revert(look);
                look.IsSheep = false;
                SetBlackboard(animal, look, false);
                return true;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepVisuals.Apply: {ex}");
                return false;
            }
        }

        // ── goat side ────────────────────────────────────────────────────────
        private static bool Capture(LivestockAnimal animal, SheepLook look)
        {
            SkinnedMeshRenderer smr = null;
            foreach (var r in animal.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r == null || r.sharedMesh == null) continue;
                if (smr == null || r.sharedMesh.name.IndexOf("Goat", StringComparison.OrdinalIgnoreCase) >= 0) smr = r;
            }
            if (smr == null || smr.bones == null || smr.bones.Length == 0)
            {
                if (!_loggedMissingRenderer)
                {
                    _loggedMissingRenderer = true;
                    LiveStockMarketMod.Log.Warning($"{Tag} SheepVisuals: no skinned mesh on '{animal.name}' — sheep keep the goat look.");
                }
                return false;
            }
            look.Renderer      = smr;
            look.GoatMesh      = smr.sharedMesh;
            look.GoatMaterials = smr.sharedMaterials;
            look.GoatBones     = smr.bones;
            look.GoatRootBone  = smr.rootBone;
            var bb = animal.widgetBlackboard;
            if (bb != null)
            {
                look.GoatDisplayName          = bb.displayName;
                look.GoatDescTag              = bb.descLocTag;
                look.GoatNameNeedsTranslation = bb.nameNeedsTranslation;
                look.GoatIcon                 = bb.icon;
                look.GoatImg                  = bb.hiRezImg;
            }
            return true;
        }

        private static void Revert(SheepLook look)
        {
            var smr = look.Renderer;
            if (smr == null) return;
            smr.sharedMesh      = look.GoatMesh;
            smr.bones           = look.GoatBones;
            smr.rootBone        = look.GoatRootBone;
            smr.sharedMaterials = look.GoatMaterials;
            try
            {
                int n = look.SheepMaterials != null ? look.SheepMaterials.Length : 0;
                for (int i = 0; i < n; i++) smr.SetPropertyBlock(null, i);
                smr.SetPropertyBlock(null);
            }
            catch { }
            look.SheepMesh = null; look.SheepMaterials = null;
        }

        // ── sheep side ───────────────────────────────────────────────────────
        private static bool Rebind(SheepLook look)
        {
            var smr = look.Renderer;
            if (smr == null || look.GoatMesh == null) return false;
            if (!_meshByGoatMesh.TryGetValue(look.GoatMesh, out var mesh) || mesh == null)
            {
                mesh = BuildMesh(look);
                if (mesh == null) return false;
                _meshByGoatMesh[look.GoatMesh] = mesh;
            }
            var bones = MapBones(look);
            if (bones == null) return false;
            smr.sharedMesh      = mesh;
            smr.bones           = bones;              // rootBone and localBounds stay the goat's: same envelope
            smr.sharedMaterials = Materials(look.GoatMaterials, mesh);
            look.SheepMesh      = mesh;
            look.SheepMaterials = smr.sharedMaterials;
            look.NextCheck      = Time.unscaledTime + 1f;
            look.DiagAt         = Time.unscaledTime + 3f;
            look.Diagnosed      = false;
            PushTextures(look);
            return true;
        }

        /// <summary>
        /// A per-renderer MaterialPropertyBlock overrides the material's own textures. The
        /// animal shader is a "Highlighter" variant and the game drives it through blocks,
        /// so write the sheep texture into the block of every material slot as well.
        /// </summary>
        private static void PushTextures(SheepLook look)
        {
            var smr = look.Renderer; var mats = look.SheepMaterials;
            if (smr == null || mats == null) return;
            try
            {
                var block = new MaterialPropertyBlock();
                for (int i = 0; i < mats.Length; i++)
                {
                    var tex = mats[i] != null ? mats[i].mainTexture : null;
                    if (tex == null) continue;
                    block.Clear();
                    smr.GetPropertyBlock(block, i);
                    block.SetTexture("_MainTex", tex);
                    smr.SetPropertyBlock(block, i);
                }
                block.Clear();
                smr.GetPropertyBlock(block);
                if (block.GetTexture("_MainTex") != null && mats.Length > 0 && mats[0] != null && mats[0].mainTexture != null)
                {
                    block.SetTexture("_MainTex", mats[0].mainTexture);   // renderer-wide block held a texture too
                    smr.SetPropertyBlock(block);
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepVisuals.PushTextures: {ex.Message}");
            }
        }

        private static bool _loggedEnforce;

        /// <summary>Called by SheepLook once a second: puts the sheep state back if the game changed it, and logs what it found the first time.</summary>
        internal static void Enforce(SheepLook look)
        {
            try
            {
                var smr = look.Renderer;
                if (smr == null || look.SheepMesh == null || look.SheepMaterials == null) return;
                var mats = smr.sharedMaterials;
                bool meshOk = smr.sharedMesh == look.SheepMesh;
                bool matsOk = mats != null && mats.Length == look.SheepMaterials.Length;
                if (matsOk) for (int i = 0; i < mats.Length; i++) if (mats[i] != look.SheepMaterials[i]) { matsOk = false; break; }
                bool texOk = true;
                string blockTex = "none";
                try
                {
                    var block = new MaterialPropertyBlock();
                    smr.GetPropertyBlock(block, 0);
                    var t = block.GetTexture("_MainTex");
                    blockTex = t != null ? t.name : "none";
                    if (t != null && look.SheepMaterials.Length > 0 && look.SheepMaterials[0] != null && t != look.SheepMaterials[0].mainTexture) texOk = false;
                }
                catch { }
                if (!look.Diagnosed && Time.unscaledTime >= look.DiagAt)
                {
                    look.Diagnosed = true;
                    if (!_loggedEnforce)
                    {
                        _loggedEnforce = true;
                        string m0 = mats != null && mats.Length > 0 && mats[0] != null ? $"{mats[0].name} tex={(mats[0].mainTexture != null ? mats[0].mainTexture.name : "null")}" : "null";
                        LiveStockMarketMod.Log.Msg($"{Tag} SheepVisuals: 3 s after the swap on '{look.name}': mesh={(smr.sharedMesh != null ? smr.sharedMesh.name : "null")} (ours={meshOk}), " +
                            $"material[0]={m0} (ours={matsOk}), block[0]._MainTex={blockTex} (ours={texOk}), renderer enabled={smr.enabled}.");
                    }
                }
                if (meshOk && matsOk && texOk) return;
                if (!meshOk) smr.sharedMesh = look.SheepMesh;
                if (!matsOk) smr.sharedMaterials = look.SheepMaterials;
                PushTextures(look);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepVisuals.Enforce: {ex.Message}");
            }
        }

        private static Mesh BuildMesh(SheepLook look)
        {
            var src = SheepAssets.Mesh;
            var goatMesh = look.GoatMesh;
            if (src == null) return null;
            var mesh = UnityEngine.Object.Instantiate(src);
            mesh.name = "Animal_Sheep01A(" + goatMesh.name + ")";
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;

            // Unit check: the sheep was fitted inside the goat's envelope, so the largest
            // dimensions agree to within ~20% when the units match (goat 1.98 vs sheep 1.63).
            float goatMax = MaxDim(goatMesh.bounds.size), sheepMax = MaxDim(src.bounds.size);
            float factor = 1f;
            if (goatMax > 0f && sheepMax > 0f)
            {
                float p = Mathf.Round(Mathf.Log10((goatMax / sheepMax) / 1.2f));
                if (p != 0f) factor = Mathf.Pow(10f, p);
            }
            if (factor != 1f)
            {
                var v = mesh.vertices;
                for (int i = 0; i < v.Length; i++) v[i] *= factor;
                mesh.vertices = v;
                mesh.RecalculateBounds();
            }
            if (!_loggedScale)
            {
                _loggedScale = true;
                var g = goatMesh.bounds.size; var s = src.bounds.size;
                LiveStockMarketMod.Log.Msg($"{Tag} SheepVisuals: goat mesh '{goatMesh.name}' {g.x:F4}×{g.y:F4}×{g.z:F4}, " +
                    $"sheep {s.x:F4}×{s.y:F4}×{s.z:F4} → unit factor ×{factor}; goat bones {look.GoatBones.Length}, bindposes {goatMesh.bindposes.Length}.");
            }

            // Bindposes: the goat's, in the sheep mesh's bone order (matched by name).
            var goatBind  = goatMesh.bindposes;
            var index     = IndexByName(look.GoatBones);
            var names     = SheepAssets.BoneNames;
            int fallback  = FallbackIndex(index, look);
            var bind      = new Matrix4x4[names.Length];
            var missing   = new List<string>();
            for (int k = 0; k < names.Length; k++)
            {
                if (names[k] != null && index.TryGetValue(names[k], out int i) && i < goatBind.Length) bind[k] = goatBind[i];
                else
                {
                    bind[k] = fallback >= 0 && fallback < goatBind.Length ? goatBind[fallback] : Matrix4x4.identity;
                    missing.Add(names[k] ?? "<null>");
                }
            }
            mesh.bindposes = bind;
            if (missing.Count > 0)
                LiveStockMarketMod.Log.Warning($"{Tag} SheepVisuals: {missing.Count} sheep bone(s) have no goat bone of that name ({string.Join(", ", missing.ToArray())}); bound to the root.");
            return mesh;
        }

        private static Transform[] MapBones(SheepLook look)
        {
            var goatBones = look.GoatBones;
            var index = IndexByName(goatBones);
            var names = SheepAssets.BoneNames;
            int fallback = FallbackIndex(index, look);
            var bones = new Transform[names.Length];
            for (int k = 0; k < names.Length; k++)
            {
                if (names[k] != null && index.TryGetValue(names[k], out int i)) bones[k] = goatBones[i];
                else bones[k] = fallback >= 0 ? goatBones[fallback] : (look.GoatRootBone != null ? look.GoatRootBone : goatBones[0]);
                if (bones[k] == null) return null;
            }
            return bones;
        }

        private static Dictionary<string, int> IndexByName(Transform[] bones)
        {
            var d = new Dictionary<string, int>();
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null && !d.ContainsKey(bones[i].name)) d[bones[i].name] = i;
            return d;
        }

        private static int FallbackIndex(Dictionary<string, int> index, SheepLook look)
        {
            if (index.TryGetValue("BN_Root", out int r)) return r;
            if (look.GoatRootBone != null && index.TryGetValue(look.GoatRootBone.name, out int rb)) return rb;
            return index.Count > 0 ? 0 : -1;
        }

        private static float MaxDim(Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));

        // ── materials ────────────────────────────────────────────────────────
        // Default: clones of the goat's own material (the game's animal shader, fog of war,
        // highlight and season tinting included) with the sheep textures, its normal and
        // specular maps dropped and the specular damped (a bare albedo on Standard Specular
        // otherwise reads the texture alpha as smoothness and mirrors the sky).
        // Fallback / pref: the bundle's Standard materials.
        //
        // Submeshes are matched to textures by SIZE, not index: body is the biggest, eye the
        // smallest, so the order the FBX importer chose does not matter.
        private static readonly string[] Roles = { "SheepBody", "SheepHead", "SheepEye" };
        private static readonly string[] MapProps = { "_BumpMap", "_SpecGlossMap", "_MetallicGlossMap", "_SpecularMap", "_NormalMap", "_EmissionMap", "_OcclusionMap", "_DetailNormalMap", "_DetailAlbedoMap", "_ParallaxMap" };
        private static readonly string[] MapKeywords = { "_NORMALMAP", "_SPECGLOSSMAP", "_METALLICGLOSSMAP", "_EMISSION", "_DETAIL_MULX2", "_PARALLAXMAP", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A" };
        private static bool _loggedMaterialProps;

        private static Material[] Materials(Material[] goatMats, Mesh mesh)
        {
            if (_materials == null || _materials.Length != Roles.Length || _materials[0] == null)
            {
                var baseMat = UseGameShader && goatMats != null && goatMats.Length > 0 ? goatMats[0] : null;
                var arr = new Material[Roles.Length];
                for (int r = 0; r < Roles.Length; r++)
                {
                    var tex = TextureForRole(r);
                    Material m;
                    if (baseMat != null)
                    {
                        m = new Material(baseMat) { name = "LSM_" + Roles[r] };
                        if (!_loggedMaterialProps)
                        {
                            _loggedMaterialProps = true;
                            var names = new List<string>();
                            foreach (var p in m.GetTexturePropertyNames())
                            {
                                var t = m.GetTexture(p);
                                names.Add(p + "=" + (t != null ? t.name : "null"));
                            }
                            LiveStockMarketMod.Log.Msg($"{Tag} SheepVisuals: goat material '{baseMat.name}' texture slots: {string.Join(", ", names.ToArray())}; " +
                                $"keywords={string.Join(" ", m.shaderKeywords)}.");
                        }
                        // the colour texture: whatever holds the goat's diffuse, plus the declared main texture
                        foreach (var p in m.GetTexturePropertyNames())
                        {
                            var t = m.GetTexture(p);
                            string tn = t != null ? t.name : "";
                            if (p == "_MainTex" || tn.IndexOf("DIF", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (tex != null) m.SetTexture(p, tex);
                            }
                            else if (t != null && (tn.IndexOf("NML", StringComparison.OrdinalIgnoreCase) >= 0 || tn.IndexOf("SPC", StringComparison.OrdinalIgnoreCase) >= 0
                                  || tn.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0 || tn.IndexOf("Spec", StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                m.SetTexture(p, null);   // the goat's maps do not fit the sheep's UVs
                            }
                        }
                        if (tex != null) m.mainTexture = tex;
                        foreach (var p in MapProps) if (m.HasProperty(p)) m.SetTexture(p, null);
                        foreach (var k in MapKeywords) m.DisableKeyword(k);
                        if (m.HasProperty("_SpecColor"))     m.SetColor("_SpecColor", new Color(0.06f, 0.06f, 0.06f, 1f));
                        if (m.HasProperty("_Glossiness"))    m.SetFloat("_Glossiness", 0.12f);
                        if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", 0.12f);
                        if (m.HasProperty("_Metallic"))      m.SetFloat("_Metallic", 0f);
                        if (m.HasProperty("_SmoothnessTextureChannel")) m.SetFloat("_SmoothnessTextureChannel", 0f);
                        if (m.HasProperty("_EmissionColor"))  m.SetColor("_EmissionColor", Color.black);
                        if (tex == null) LiveStockMarketMod.Log.Warning($"{Tag} SheepVisuals: {Roles[r]} has no texture; its clone keeps the goat diffuse.");
                    }
                    else
                    {
                        m = BundleMaterialForRole(r);
                        if (m != null && tex != null && m.mainTexture == null) m.mainTexture = tex;
                    }
                    if (m != null) m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                    arr[r] = m;
                }
                _materials = arr;
                LiveStockMarketMod.Log.Msg($"{Tag} SheepVisuals: sheep materials " +
                    (baseMat != null ? $"cloned from goat material '{baseMat.name}' (shader '{baseMat.shader?.name}')." : "taken from the bundle (Standard shader)."));
            }

            // rank submeshes by triangle count: biggest → body, then head, smallest → eye
            int n = mesh != null ? mesh.subMeshCount : 1;
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            if (mesh != null) order.Sort((a, b) => mesh.GetSubMesh(b).indexCount.CompareTo(mesh.GetSubMesh(a).indexCount));
            var result = new Material[n];
            for (int rank = 0; rank < order.Count; rank++)
                result[order[rank]] = _materials[Mathf.Min(rank, _materials.Length - 1)];
            return result;
        }

        /// <summary>The bundle material named after the role (SheepBody / SheepHead / SheepEye), else by index.</summary>
        private static Material BundleMaterialForRole(int role)
        {
            var mats = SheepAssets.Materials ?? new Material[0];
            foreach (var m in mats) if (m != null && m.name.StartsWith(Roles[role], StringComparison.OrdinalIgnoreCase)) return m;
            return mats.Length > 0 ? mats[Mathf.Min(role, mats.Length - 1)] : null;
        }

        private static bool _loggedMissingTexture;

        private static Texture2D TextureForRole(int role)
        {
            var t = SheepAssets.GetTexture(Roles[role]);
            if (t != null) return t;
            var m = BundleMaterialForRole(role);
            t = m != null ? m.mainTexture as Texture2D : null;
            if (t != null) return t;
            var texs = SheepAssets.Textures ?? new Texture2D[0];
            t = texs.Length > 0 ? texs[Mathf.Min(role, texs.Length - 1)] : null;
            if (t == null && !_loggedMissingTexture)
            {
                _loggedMissingTexture = true;
                LiveStockMarketMod.Log.Warning($"{Tag} SheepVisuals: no texture for {Roles[role]} in the bundle — the sheep keeps the goat coat.");
            }
            return t;
        }

        // ── name, description, icons ─────────────────────────────────────────
        private static void SetBlackboard(LivestockAnimal animal, SheepLook look, bool sheep)
        {
            try
            {
                var bb = animal.widgetBlackboard;
                if (bb == null) return;
                if (sheep)
                {
                    bb.nameNeedsTranslation = true;
                    bb.displayName = "LSM_Sheep_Name";
                    bb.descLocTag  = "LSM_Sheep_Description";
                    var icon = Icon;
                    if (icon != null) { bb.icon = icon; bb.hiRezImg = Portrait ?? icon; }   // single sheep on the animal (a 192×256 portrait when drawn); the double is the barn's herd icon
                }
                else
                {
                    bb.nameNeedsTranslation = look.GoatNameNeedsTranslation;
                    bb.displayName = look.GoatDisplayName;
                    bb.descLocTag  = look.GoatDescTag;
                    bb.icon        = look.GoatIcon;
                    bb.hiRezImg    = look.GoatImg;
                }
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} SheepVisuals.SetBlackboard: {ex.Message}");
            }
        }
    }
}
