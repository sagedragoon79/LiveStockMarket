using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using LiveStockMarket.Patches;

namespace LiveStockMarket.Systems
{
    /// <summary>
    /// Per-animal record of the goat's animation and rendering state, so a switch
    /// back is exact. Holds the live pig body while the animal is a pig.
    /// </summary>
    internal sealed class PigLook : MonoBehaviour
    {
        public Animator GoatAnimator;
        public bool GoatAnimatorWasEnabled;
        public List<Renderer> GoatRenderers = new List<Renderer>();
        public List<bool> GoatRenderersEnabled = new List<bool>();
        public GameObject PigInstance;
        public Animator PigAnimator;
        public Renderer[] PigRenderers;
        public string GoatDisplayName;
        public string GoatDescTag;
        public bool GoatNameNeedsTranslation;
        public Sprite GoatIcon;
        public Sprite GoatImg;
        public bool IsPig;
        /// <summary>Voice sources under the animal root (PigSounds): grunts, and the breathing loop.</summary>
        public AudioSource Voice;
        public AudioSource Breath;
    }

    /// <summary>
    /// Raises the game's animation begin/end events for the pig's tagged states.
    /// The goat controller carries CEAnimationStateBehaviour scripts on its eating,
    /// sleeping and fidget states; a bundle-built controller cannot reference that
    /// class, so this watches the pig animator's state tags instead and calls the
    /// same public handlers (HaltMovementForAnimation / ResumeMovementAfterAnimation
    /// hang off them). It also reports the fastest agent speed seen, to tune the
    /// walk/run blend thresholds.
    /// </summary>
    internal sealed class PigAnimDriver : MonoBehaviour
    {
        public Animator PigAnimator;
        public CEAnimationState Eating;
        public CEAnimationState Sleeping;
        public AICrateNavMeshAgent Agent;
        private bool _eating, _sleeping;
        private float _maxVelocity, _reportAt = -1f;
        private static bool _velocityReported;

        private void LateUpdate()
        {
            if (PigAnimator == null || !PigAnimator.isActiveAndEnabled) return;
            try
            {
                var cur = PigAnimator.GetCurrentAnimatorStateInfo(0);
                bool eating = cur.IsTag("eating"), sleeping = cur.IsTag("sleeping");
                if (PigAnimator.IsInTransition(0))
                {
                    var next = PigAnimator.GetNextAnimatorStateInfo(0);
                    eating |= next.IsTag("eating");
                    sleeping |= next.IsTag("sleeping");
                }
                Edge(ref _eating, eating, Eating);
                Edge(ref _sleeping, sleeping, Sleeping);

                if (!_velocityReported && Agent != null)
                {
                    _maxVelocity = Mathf.Max(_maxVelocity, Agent.velocityMagnitude);
                    if (_reportAt < 0f) _reportAt = Time.time + 40f;
                    else if (Time.time > _reportAt)
                    {
                        _velocityReported = true;
                        LiveStockMarketMod.Log.Msg($"{LiveStockMarketMod.LogTag} PigVisuals: fastest agent speed seen on a pig in 40 s = {_maxVelocity:F2} (blend thresholds walk 2.0 / run 6.0).");
                    }
                }
            }
            catch { }
        }

        private static void Edge(ref bool was, bool now, CEAnimationState state)
        {
            if (state == null || was == now) return;
            was = now;
            if (now) state.OnAnimationBegin(); else state.OnAnimationEnd();
        }
    }

    /// <summary>
    /// Pigs mode look: a self-animated pig body under the goat's root.
    ///
    /// The goat stays the game object the game knows (AI, herd, colliders, blackboard).
    /// Its renderers are hidden and a pig prefab (own skeleton, skinned mesh, Animator
    /// with a controller built to the game's parameter names) is parented under it.
    /// The game's CEAnimationController is then pointed at the pig's Animator — its
    /// private animator field, every registered state's animator, then Initialize()
    /// to re-read the parameters — so velocityFloat, eatingBool, sleepingBool and
    /// deathBool drive the pig exactly as they drove the goat.
    /// </summary>
    internal static class PigVisuals
    {
        private static string Tag => LiveStockMarketMod.LogTag;

        public static bool  Enabled => LiveStockMarketMod.cfgPigVisuals?.Value ?? true;
        public static float Scale   => Mathf.Clamp(LiveStockMarketMod.cfgPigScale?.Value ?? 1f, 0.2f, 5f);

        public static Sprite Icon  => ModIcons.Get("LSM.pig_icon.png",  "pig_icon.png");
        public static Sprite Image => ModIcons.Get("LSM.pig_image.png", "pig_image.png");
        /// <summary>Optional 192×256 portrait for the animal's window (vanilla hiRezImg size); the single icon stands in without it.</summary>
        public static Sprite Portrait => ModIcons.Get("LSM.pig_portrait.png", "pig_portrait.png", optional: true);

        private static readonly FieldInfo CtrlAnimator  = AccessTools.Field(typeof(CEAnimationController), "animator");
        private static readonly FieldInfo CtrlStates    = AccessTools.Field(typeof(CEAnimationController), "animStateDict");
        private static readonly FieldInfo StateAnimator = AccessTools.Field(typeof(CEAnimationState), "animator");
        private static readonly FieldInfo ActiveRenderers = AccessTools.Field(typeof(LandAnimal), "activeRenderers");

        private static readonly string[] Coats = { "PigBase", "PigSpotted", "PigBlack" };
        private static readonly Material[] _materials = new Material[3];
        private static bool _loggedSize, _loggedMissing;

        public static void Register()
        {
            LocalizationPatches.Register("LSM_Pig_Name", "Pig");
            LocalizationPatches.Register("LSM_Pig_Description",
                "A pig. It roots through its grazing ground for mushrooms when there are trees about, breeds quickly, and butchers into more meat and tallow than a goat.");
            if (CtrlAnimator == null || CtrlStates == null || StateAnimator == null)
                LiveStockMarketMod.Log.Warning($"{Tag} PigVisuals: CEAnimationController layout not as expected — pigs keep the goat look.");
        }

        public static bool WantPig(GoatBarn barn) => Enabled && barn != null && GoatBarnModeStore.IsPigs(barn);

        public static void ApplyToBarn(GoatBarn barn)
        {
            try
            {
                if (barn == null || barn.herd == null || barn.herd.animalsInHerdRO == null) return;
                bool pig = WantPig(barn);
                var animals = new List<LivestockAnimal>(barn.herd.animalsInHerdRO);
                int changed = 0;
                foreach (var a in animals) if (Apply(a, pig)) changed++;
                if (changed > 0)
                    LiveStockMarketMod.Log.Msg($"{Tag} PigVisuals: '{barn.displayName}' → {changed} animal(s) now look like {(pig ? "pigs" : "goats")}.");
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} PigVisuals.ApplyToBarn: {ex.Message}");
            }
        }

        public static void ApplyAll()
        {
            try
            {
                foreach (var look in UnityEngine.Object.FindObjectsOfType<PigLook>())
                    if (look != null && look.IsPig) { var a = look.GetComponent<LivestockAnimal>(); Revert(look); look.IsPig = false; if (a != null) SetBlackboard(a, look, false); }
                foreach (var barn in UnityEngine.Object.FindObjectsOfType<GoatBarn>()) ApplyToBarn(barn);
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} PigVisuals.ApplyAll: {ex.Message}");
            }
        }

        /// <summary>Returns true when the animal's look actually changed.</summary>
        public static bool Apply(LivestockAnimal animal, bool pig)
        {
            try
            {
                if (animal == null || !(animal is Goat)) return false;
                var look = animal.GetComponent<PigLook>();
                if (pig)
                {
                    if (!PigAssets.Available || CtrlAnimator == null) return false;
                    if (look == null) look = animal.gameObject.AddComponent<PigLook>();
                    bool changed = false;
                    if (!look.IsPig && Swap(animal, look)) { look.IsPig = true; changed = true; }
                    if (look.IsPig) SetBlackboard(animal, look, true);
                    return changed;
                }
                if (look == null || !look.IsPig) return false;
                Revert(look);
                look.IsPig = false;
                SetBlackboard(animal, look, false);
                return true;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} PigVisuals.Apply: {ex}");
                return false;
            }
        }

        // ── swap ─────────────────────────────────────────────────────────────
        private static bool Swap(LivestockAnimal animal, PigLook look)
        {
            var ctrl = animal.animationController;
            if (ctrl == null) { LogMissing("no CEAnimationController"); return false; }
            var goatAnimator = CtrlAnimator.GetValue(ctrl) as Animator;
            if (goatAnimator == null) { LogMissing("controller has no animator"); return false; }
            var states = CtrlStates.GetValue(ctrl) as Dictionary<CEAnimationState.State, CEAnimationState>;

            // the goat's renderers: hide, and remember what was enabled
            look.GoatRenderers.Clear(); look.GoatRenderersEnabled.Clear();
            foreach (var r in animal.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                look.GoatRenderers.Add(r); look.GoatRenderersEnabled.Add(r.enabled);
            }

            // the pig body
            var inst = UnityEngine.Object.Instantiate(PigAssets.Prefab, animal.transform, false);
            inst.name = "LSM_PigBody";
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            inst.transform.localScale = inst.transform.localScale * Scale;
            SetLayerRecursively(inst, animal.gameObject.layer);
            var pigAnimator = inst.GetComponent<Animator>() ?? inst.GetComponentInChildren<Animator>(true);
            if (pigAnimator == null) { UnityEngine.Object.Destroy(inst); LogMissing("pig prefab has no Animator"); return false; }
            pigAnimator.runtimeAnimatorController = PigAssets.Controller;
            if (PigAssets.Avatar != null) pigAnimator.avatar = PigAssets.Avatar;
            pigAnimator.applyRootMotion = false;
            pigAnimator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            pigAnimator.enabled = true;

            var pigRenderers = inst.GetComponentsInChildren<Renderer>(true);
            var mats = Materials(FirstGoatMaterial(look), CoatIndex(animal));
            foreach (var r in pigRenderers)
            {
                if (r == null) continue;
                var arr = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < arr.Length; i++) arr[i] = mats[Mathf.Min(i, mats.Length - 1)];
                r.sharedMaterials = arr;
            }

            // hand the game's animation controller the pig's animator
            look.GoatAnimator = goatAnimator;
            look.GoatAnimatorWasEnabled = goatAnimator.enabled;
            CtrlAnimator.SetValue(ctrl, pigAnimator);
            if (states != null) foreach (var s in states.Values) if (s != null) StateAnimator.SetValue(s, pigAnimator);
            ctrl.Initialize();
            goatAnimator.enabled = false;
            foreach (var r in look.GoatRenderers) if (r != null) r.enabled = false;

            // the fog / hide list keeps renderer components, so swap them there too
            var active = ActiveRenderers?.GetValue(animal) as List<Renderer>;
            if (active != null)
            {
                foreach (var r in look.GoatRenderers) active.Remove(r);
                foreach (var r in pigRenderers) if (r != null && !active.Contains(r)) active.Add(r);
            }

            var driver = animal.gameObject.GetComponent<PigAnimDriver>() ?? animal.gameObject.AddComponent<PigAnimDriver>();
            driver.PigAnimator = pigAnimator;
            driver.Eating = states != null && states.TryGetValue(CEAnimationState.State.EATING, out var eat) ? eat : null;
            driver.Sleeping = states != null && states.TryGetValue(CEAnimationState.State.SLEEPING, out var slp) ? slp : null;
            driver.Agent = animal.GetComponent<AICrateNavMeshAgent>();

            look.PigInstance = inst;
            look.PigAnimator = pigAnimator;
            look.PigRenderers = pigRenderers;

            if (!_loggedSize)
            {
                _loggedSize = true;
                var g = look.GoatRenderers.Count > 0 && look.GoatRenderers[0] != null ? look.GoatRenderers[0].bounds.size : Vector3.zero;
                var p = pigRenderers.Length > 0 && pigRenderers[0] != null ? pigRenderers[0].bounds.size : Vector3.zero;
                LiveStockMarketMod.Log.Msg($"{Tag} PigVisuals: first pig on '{animal.name}': goat renderer bounds {g.x:F2}×{g.y:F2}×{g.z:F2}, pig {p.x:F2}×{p.y:F2}×{p.z:F2} (scale pref {Scale:F2}; prefab root scale {PigAssets.Prefab.transform.localScale.x:F3}); " +
                    $"states registered: {(states != null ? states.Count : 0)}, eating/sleeping handlers {(driver.Eating != null)}/{(driver.Sleeping != null)}.");
            }
            PigSounds.Attach(look, animal);
            return true;
        }

        private static void Revert(PigLook look)
        {
            try
            {
                var animal = look.GetComponent<LivestockAnimal>();
                var ctrl = animal != null ? animal.animationController : null;
                if (ctrl != null && look.GoatAnimator != null)
                {
                    CtrlAnimator.SetValue(ctrl, look.GoatAnimator);
                    var states = CtrlStates.GetValue(ctrl) as Dictionary<CEAnimationState.State, CEAnimationState>;
                    if (states != null) foreach (var s in states.Values) if (s != null) StateAnimator.SetValue(s, look.GoatAnimator);
                    look.GoatAnimator.enabled = look.GoatAnimatorWasEnabled;
                    ctrl.Initialize();
                }
                for (int i = 0; i < look.GoatRenderers.Count; i++)
                    if (look.GoatRenderers[i] != null) look.GoatRenderers[i].enabled = i < look.GoatRenderersEnabled.Count ? look.GoatRenderersEnabled[i] : true;
                var active = animal != null ? ActiveRenderers?.GetValue(animal) as List<Renderer> : null;
                if (active != null)
                {
                    if (look.PigRenderers != null) foreach (var r in look.PigRenderers) active.Remove(r);
                    for (int i = 0; i < look.GoatRenderers.Count; i++)
                        if (look.GoatRenderers[i] != null && look.GoatRenderersEnabled[i] && !active.Contains(look.GoatRenderers[i])) active.Add(look.GoatRenderers[i]);
                }
                var driver = look.GetComponent<PigAnimDriver>();
                if (driver != null) UnityEngine.Object.Destroy(driver);
                PigSounds.Detach(look);
                if (look.PigInstance != null) UnityEngine.Object.Destroy(look.PigInstance);
                look.PigInstance = null; look.PigAnimator = null; look.PigRenderers = null;
            }
            catch (Exception ex)
            {
                LiveStockMarketMod.Log.Warning($"{Tag} PigVisuals.Revert: {ex.Message}");
            }
        }

        // ── materials ────────────────────────────────────────────────────────
        // Clones of the goat's material (game shader) with the pig's colour and normal maps;
        // three coats, picked per animal. Bundle materials are the fallback.
        private static readonly string[] SpecProps = { "_SpecGlossMap", "_MetallicGlossMap", "_SpecularMap", "_EmissionMap", "_OcclusionMap", "_DetailNormalMap", "_DetailAlbedoMap", "_ParallaxMap" };
        private static readonly string[] SpecKeywords = { "_SPECGLOSSMAP", "_METALLICGLOSSMAP", "_EMISSION", "_DETAIL_MULX2", "_PARALLAXMAP", "_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A" };

        private static Material[] Materials(Material goatMat, int coat)
        {
            coat = Mathf.Clamp(coat, 0, Coats.Length - 1);
            if (_materials[coat] == null)
            {
                var albedo = PigAssets.GetTexture(Coats[coat]) ?? PigAssets.GetTexture("PigBase");
                var normal = PigAssets.GetTexture("PigNormal");
                Material m = null;
                if (goatMat != null)
                {
                    m = new Material(goatMat) { name = "LSM_Pig_" + Coats[coat] };
                    if (albedo != null) { m.mainTexture = albedo; if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", albedo); }
                    if (m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", normal); if (normal != null) m.EnableKeyword("_NORMALMAP"); else m.DisableKeyword("_NORMALMAP"); }
                    foreach (var p in SpecProps) if (m.HasProperty(p)) m.SetTexture(p, null);
                    foreach (var k in SpecKeywords) m.DisableKeyword(k);
                    if (m.HasProperty("_SpecColor"))     m.SetColor("_SpecColor", new Color(0.08f, 0.08f, 0.08f, 1f));
                    if (m.HasProperty("_Glossiness"))    m.SetFloat("_Glossiness", 0.2f);
                    if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", 0.2f);
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
                }
                else
                {
                    var fb = PigAssets.Materials;
                    m = fb != null && fb.Length > 0 ? fb[0] : null;
                    if (m != null && albedo != null) m.mainTexture = albedo;
                }
                if (m != null) m.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                _materials[coat] = m;
                LiveStockMarketMod.Log.Msg($"{Tag} PigVisuals: pig material '{Coats[coat]}' " + (goatMat != null ? $"cloned from goat material '{goatMat.name}'" : "taken from the bundle") + $" (albedo {(albedo != null ? albedo.name : "none")}, normal {(normal != null ? normal.name : "none")}).");
            }
            return new[] { _materials[coat] };
        }

        private static Material FirstGoatMaterial(PigLook look)
        {
            foreach (var r in look.GoatRenderers)
                if (r != null && r.sharedMaterials != null)
                    foreach (var m in r.sharedMaterials) if (m != null) return m;
            return null;
        }

        private static int CoatIndex(LivestockAnimal animal)
        {
            // stable across a session; a reload may pick a different coat for the same pig
            int id = animal.GetInstanceID();
            return Mathf.Abs(id / 7) % Coats.Length;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursively(c.gameObject, layer);
        }

        private static void LogMissing(string what)
        {
            if (_loggedMissing) return;
            _loggedMissing = true;
            LiveStockMarketMod.Log.Warning($"{Tag} PigVisuals: {what} — pigs keep the goat look.");
        }

        // ── name, description, icons ─────────────────────────────────────────
        private static void SetBlackboard(LivestockAnimal animal, PigLook look, bool pig)
        {
            try
            {
                var bb = animal.widgetBlackboard;
                if (bb == null) return;
                if (pig)
                {
                    if (look.GoatDisplayName == null)
                    {
                        look.GoatDisplayName = bb.displayName; look.GoatDescTag = bb.descLocTag;
                        look.GoatNameNeedsTranslation = bb.nameNeedsTranslation; look.GoatIcon = bb.icon; look.GoatImg = bb.hiRezImg;
                    }
                    bb.nameNeedsTranslation = true;
                    bb.displayName = "LSM_Pig_Name";
                    bb.descLocTag  = "LSM_Pig_Description";
                    // single pig on the animal (a 192×256 portrait when drawn); the double is the barn's herd icon, as with sheep
                    var icon = Icon;  if (icon != null) bb.icon = icon;
                    var img  = Portrait ?? icon; if (img != null) bb.hiRezImg = img;
                }
                else if (look.GoatDisplayName != null)
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
                LiveStockMarketMod.Log.Warning($"{Tag} PigVisuals.SetBlackboard: {ex.Message}");
            }
        }
    }
}
