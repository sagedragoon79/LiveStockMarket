using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

// Headless AssetBundle builder for Live-Stock Market art (Unity 2022.3.62f3 — matches FF).
// Copied into unity/bundler/Assets/Editor/ by tools/build-bundle.sh, then:
//   Unity.exe -batchmode -quit -projectPath unity/bundler -executeMethod BuildAssetBundles.Build -logFile <log>
//
// Bundles:
//   sheep — a skinned mesh bound to the goat's own skeleton (art/sheep/sheep_rig.blend). The
//           mod takes the mesh and the bone NAMES from the prefab and rebinds them to a live
//           goat's bones at runtime.
//   pig   — a self-contained animated body (art/Pig/export, Auto-Rig Pro export pruned to 40
//           bones) with its own AnimatorController built here to the game's livestock contract:
//           parameters velocityFloat / inCombatFloat / ridingFloat / interruptTrigger /
//           deathBool / eatingBool / sleepingBool / idleFidgetInt, and states tagged
//           "eating" / "sleeping" so the mod can raise the game's begin/end events.
public static class BuildAssetBundles
{
    const string OutDir = "AssetBundlesOut";

    public static void Build()
    {
        try
        {
            BuildSheep();
            BuildPig();
            Directory.CreateDirectory(OutDir);
            var manifest = BuildPipeline.BuildAssetBundles(
                OutDir, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
            if (manifest == null) throw new Exception("BuildAssetBundles returned null.");
            Debug.Log("BUNDLE_BUILD_OK " + Path.GetFullPath(OutDir));
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogError("BUNDLE_BUILD_FAIL " + e);
            EditorApplication.Exit(1);
        }
    }

    // ── shared helpers ────────────────────────────────────────────────────
    static void ImportTexture(string path, bool normalMap)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) throw new Exception("no texture at " + path);
        ti.mipmapEnabled = true;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.alphaSource = TextureImporterAlphaSource.None;      // Standard Specular reads albedo alpha as smoothness
        ti.alphaIsTransparency = false;
        ti.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.SaveAndReimport();
    }

    static Material StandardMaterial(string path, string name, Texture2D albedo)
    {
        var shader = Shader.Find("Standard");
        if (shader == null) throw new Exception("Standard shader not found.");
        var m = new Material(shader) { name = name };
        if (albedo != null) m.mainTexture = albedo;
        m.SetFloat("_Glossiness", 0.05f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void Mark(string bundle, params string[] paths)
    {
        foreach (var p in paths)
        {
            var imp = AssetImporter.GetAtPath(p);
            if (imp != null) imp.SetAssetBundleNameAndVariant(bundle, "");
        }
    }

    // ── sheep ─────────────────────────────────────────────────────────────
    static void BuildSheep()
    {
        const string dir = "Assets/Sheep";
        const string fbx = dir + "/Sheep01A.fbx";
        const string bundle = "sheep";
        string[] texs  = { dir + "/SheepBody.png", dir + "/SheepHead.png", dir + "/SheepEye.png" };
        string[] names = { "SheepBody", "SheepHead", "SheepEye" };

        if (AssetDatabase.LoadAssetAtPath<GameObject>(fbx) == null)
            throw new Exception("no fbx at " + fbx);

        var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (mi == null) throw new Exception("no ModelImporter for " + fbx);
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importNormals      = ModelImporterNormals.Calculate;   // smooth shading regardless of the FBX's normals
        mi.normalSmoothingAngle = 90f;
        mi.globalScale        = 1f;
        mi.useFileScale       = true;
        mi.animationType      = ModelImporterAnimationType.Generic;   // keeps the skin; the game's goat Animator drives it
        mi.importAnimation    = false;
        mi.avatarSetup        = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.optimizeGameObjects = false;                                // bone transforms must stay addressable by name
        mi.isReadable         = true;                                  // runtime copies the mesh and rewrites its bindposes
        mi.importBlendShapes  = false;
        mi.skinWeights        = ModelImporterSkinWeights.Standard;
        mi.SaveAndReimport();

        foreach (var t in texs) ImportTexture(t, false);

        var mats = new Material[3];
        for (int i = 0; i < 3; i++)
            mats[i] = StandardMaterial(dir + "/" + names[i] + ".mat", names[i], AssetDatabase.LoadAssetAtPath<Texture2D>(texs[i]));
        AssetDatabase.SaveAssets();

        var fbxGo = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbxGo);
        var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null) throw new Exception("no SkinnedMeshRenderer in " + fbx + " — the skin did not survive import.");
        int n = smr.sharedMesh.subMeshCount;
        var arr = new Material[n];
        for (int i = 0; i < n; i++) arr[i] = mats[Mathf.Min(i, 2)];
        smr.sharedMaterials = arr;
        inst.name = "Sheep01A";
        string prefabPath = dir + "/Sheep01A.prefab";
        PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);

        var b = smr.sharedMesh.bounds;
        Debug.Log("SHEEP_MESH verts=" + smr.sharedMesh.vertexCount + " tris=" + (smr.sharedMesh.triangles.Length / 3)
            + " submeshes=" + n + " bounds center=" + b.center.ToString("F3") + " size=" + b.size.ToString("F3")
            + " bindposes=" + smr.sharedMesh.bindposes.Length + " bones=" + smr.bones.Length
            + " root=" + (smr.rootBone != null ? smr.rootBone.name : "null"));
        UnityEngine.Object.DestroyImmediate(inst);

        Mark(bundle, prefabPath, fbx, dir + "/SheepBody.mat", dir + "/SheepHead.mat", dir + "/SheepEye.mat", texs[0], texs[1], texs[2]);
        Debug.Log("BUILT_MODEL Sheep01A -> bundle '" + bundle + "'");
    }

    // ── pig ───────────────────────────────────────────────────────────────
    // Blend thresholds in m/s of the agent's velocityMagnitude (the game writes it every
    // frame). Measured September 15, 2026: goats walk at 2.0 (LandAnimal.movementSpeedBase)
    // and flee at 6.0. The pig's walk clip is a short-stepped amble (0.77 cycles/s, stride
    // 0.17 body lengths), so its playback speed is set to match the goat's leg cadence:
    // the goat walks at 1.0 cycles/s and runs at 1.79 → 1.3x and 2.3x on the pig's clip.
    const float WalkSpeed = 2.0f;
    const float RunSpeed  = 6.0f;
    const float WalkTimeScale = 1.3f;
    const float RunTimeScale  = 2.3f;
    // Idle <-> Locomotion hysteresis on velocityFloat.
    const float IdleExitSpeed  = 0.3f;
    const float IdleEnterSpeed = 0.15f;

    static void BuildPig()
    {
        const string dir = "Assets/Pig";
        const string fbx = dir + "/Pig01A_game.fbx";
        const string bundle = "pig";
        string[] texs = { dir + "/PigBase.png", dir + "/PigBlack.png", dir + "/PigSpotted.png", dir + "/PigNormal.png" };

        if (AssetDatabase.LoadAssetAtPath<GameObject>(fbx) == null)
            throw new Exception("no fbx at " + fbx);

        var mi = AssetImporter.GetAtPath(fbx) as ModelImporter;
        if (mi == null) throw new Exception("no ModelImporter for " + fbx);
        mi.materialImportMode  = ModelImporterMaterialImportMode.None;
        mi.importNormals       = ModelImporterNormals.Calculate;
        mi.normalSmoothingAngle = 80f;
        mi.globalScale         = 1f;
        mi.useFileScale        = true;
        mi.animationType       = ModelImporterAnimationType.Generic;
        mi.importAnimation     = true;
        mi.avatarSetup         = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.optimizeGameObjects = false;
        mi.isReadable          = false;
        mi.importBlendShapes   = false;
        mi.skinWeights         = ModelImporterSkinWeights.Standard;
        mi.animationCompression = ModelImporterAnimationCompression.Optimal;
        mi.SaveAndReimport();

        // clips: one per take, looping where the motion cycles
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = c.takeName.Contains("|") ? c.takeName.Substring(c.takeName.LastIndexOf('|') + 1) : c.takeName;   // "root|Walk" -> "Walk"
            bool loop = c.name == "Walk" || c.name == "GrazeLoop" || c.name == "Graze";
            c.loopTime = loop;
            c.loopPose = false;
            c.lockRootRotation = true;
            c.lockRootHeightY = true;
            c.lockRootPositionXZ = true;
            c.keepOriginalOrientation = true;
            c.keepOriginalPositionY = true;
            c.keepOriginalPositionXZ = true;
        }
        mi.clipAnimations = clips;
        mi.SaveAndReimport();

        for (int i = 0; i < texs.Length; i++) ImportTexture(texs[i], texs[i].EndsWith("PigNormal.png"));

        // ── voices: mono 22.05 kHz WAVs from tools/blender/cut_pig_sounds.py, stored as Vorbis ──
        string audioDir = dir + "/Audio";
        var audioPaths = System.IO.Directory.Exists(audioDir)
            ? System.IO.Directory.GetFiles(audioDir, "*.wav").Select(p => p.Replace('\\', '/')).OrderBy(p => p).ToArray()
            : new string[0];
        // PigClick is the author's hand-made click grunt: imported as is (stereo, native rate, PCM).
        foreach (var a in audioPaths) ImportAudio(a, System.IO.Path.GetFileNameWithoutExtension(a).StartsWith("PigClick"));
        Debug.Log("PIG_AUDIO " + audioPaths.Length + " clip(s): " + string.Join(", ", audioPaths.Select(p => System.IO.Path.GetFileNameWithoutExtension(p)).ToArray()));

        var assets = AssetDatabase.LoadAllAssetsAtPath(fbx);
        AnimationClip Clip(string name)
        {
            var c = assets.OfType<AnimationClip>().FirstOrDefault(a => !a.name.StartsWith("__preview") && (a.name == name || a.name.EndsWith("|" + name)));
            if (c == null) throw new Exception("pig clip '" + name + "' not found; clips: " + string.Join(", ", assets.OfType<AnimationClip>().Select(a => a.name).ToArray()));
            return c;
        }
        var walk = Clip("Walk"); var grazeStart = Clip("GrazeStart"); var grazeLoop = Clip("GrazeLoop"); var grazeEnd = Clip("GrazeEnd"); var death = Clip("Death");
        var avatar = assets.OfType<Avatar>().FirstOrDefault();

        // ── animator controller to the game's livestock contract ──
        string ctrlPath = dir + "/PigAnimator.controller";
        AssetDatabase.DeleteAsset(ctrlPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ctrlPath);
        ctrl.AddParameter("velocityFloat",    AnimatorControllerParameterType.Float);
        ctrl.AddParameter("inCombatFloat",    AnimatorControllerParameterType.Float);
        ctrl.AddParameter("ridingFloat",      AnimatorControllerParameterType.Float);
        ctrl.AddParameter("interruptTrigger", AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("deathBool",        AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("eatingBool",       AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("sleepingBool",     AnimatorControllerParameterType.Bool);
        ctrl.AddParameter("idleFidgetInt",    AnimatorControllerParameterType.Int);
        var sm = ctrl.layers[0].stateMachine;

        BlendTree tree;
        var loco = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "velocityFloat";
        tree.useAutomaticThresholds = false;
        tree.AddChild(walk, WalkSpeed);
        tree.AddChild(walk, RunSpeed);
        var children = tree.children;
        children[0].timeScale = WalkTimeScale;
        children[1].timeScale = RunTimeScale;   // no run clip: the walk, faster
        tree.children = children;

        // Standing is its own state: the graze-start clip held on its first frame (all four
        // feet planted). It must not be a frozen child of the blend tree — Unity blends child
        // DURATIONS, so a child at speed ~0 stalls the walk at any velocity below the walk
        // threshold instead of only at zero.
        var idle = sm.AddState("Idle"); idle.motion = grazeStart; idle.speed = 0.0001f;
        sm.defaultState = idle;

        var eatStart = sm.AddState("EatingStart"); eatStart.motion = grazeStart; eatStart.tag = "eating";
        var eatLoop  = sm.AddState("EatingLoop");  eatLoop.motion  = grazeLoop;  eatLoop.tag  = "eating";
        var eatEnd   = sm.AddState("EatingEnd");   eatEnd.motion   = grazeEnd;   eatEnd.tag   = "eating";
        var sleep    = sm.AddState("Sleeping");    sleep.motion    = grazeLoop;  sleep.tag    = "sleeping"; sleep.speed = 0.2f;
        var dead     = sm.AddState("Death");       dead.motion     = death;      dead.tag     = "death";

        AnimatorStateTransition T(AnimatorState from, AnimatorState to, float duration, bool exit, float exitTime)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = exit; t.exitTime = exitTime; t.hasFixedDuration = true; t.duration = duration; t.canTransitionToSelf = false;
            return t;
        }
        T(idle, loco, 0.2f, false, 0f).AddCondition(AnimatorConditionMode.Greater, IdleExitSpeed, "velocityFloat");
        T(loco, idle, 0.3f, false, 0f).AddCondition(AnimatorConditionMode.Less, IdleEnterSpeed, "velocityFloat");
        T(idle, eatStart, 0.15f, false, 0f).AddCondition(AnimatorConditionMode.If, 0f, "eatingBool");
        T(idle, sleep, 0.3f, false, 0f).AddCondition(AnimatorConditionMode.If, 0f, "sleepingBool");
        T(loco, eatStart, 0.15f, false, 0f).AddCondition(AnimatorConditionMode.If, 0f, "eatingBool");
        T(eatStart, eatLoop, 0.1f, true, 0.95f);
        var stopEarly = T(eatStart, eatEnd, 0.1f, false, 0f); stopEarly.AddCondition(AnimatorConditionMode.IfNot, 0f, "eatingBool");
        T(eatLoop, eatEnd, 0.1f, false, 0f).AddCondition(AnimatorConditionMode.IfNot, 0f, "eatingBool");
        T(eatEnd, loco, 0.15f, true, 0.95f);
        T(loco, sleep, 0.3f, false, 0f).AddCondition(AnimatorConditionMode.If, 0f, "sleepingBool");
        T(sleep, loco, 0.3f, false, 0f).AddCondition(AnimatorConditionMode.IfNot, 0f, "sleepingBool");

        var anyDeath = sm.AddAnyStateTransition(dead);
        anyDeath.hasExitTime = false; anyDeath.duration = 0.1f; anyDeath.canTransitionToSelf = false;
        anyDeath.AddCondition(AnimatorConditionMode.If, 0f, "deathBool");
        var interrupt = sm.AddAnyStateTransition(loco);
        interrupt.hasExitTime = false; interrupt.duration = 0.1f; interrupt.canTransitionToSelf = false;
        interrupt.AddCondition(AnimatorConditionMode.If, 0f, "interruptTrigger");
        interrupt.AddCondition(AnimatorConditionMode.IfNot, 0f, "deathBool");
        AssetDatabase.SaveAssets();

        // ── materials (fallback only; the mod clones the goat material with these textures) ──
        var baseMat = StandardMaterial(dir + "/PigBase.mat", "PigBase", AssetDatabase.LoadAssetAtPath<Texture2D>(texs[0]));
        AssetDatabase.SaveAssets();

        // ── prefab ──
        var fbxGo = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbxGo);
        var animator = inst.GetComponent<Animator>();
        if (animator == null) animator = inst.AddComponent<Animator>();
        animator.runtimeAnimatorController = ctrl;
        if (avatar != null) animator.avatar = avatar;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr == null) throw new Exception("no SkinnedMeshRenderer in " + fbx);
        var mats = new Material[Mathf.Max(1, smr.sharedMesh.subMeshCount)];
        for (int i = 0; i < mats.Length; i++) mats[i] = baseMat;
        smr.sharedMaterials = mats;
        inst.name = "Pig01A";
        string prefabPath = dir + "/Pig01A.prefab";
        PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);

        var b = smr.sharedMesh.bounds;
        var rb = smr.bounds;
        Debug.Log("PIG_MESH verts=" + smr.sharedMesh.vertexCount + " tris=" + (smr.sharedMesh.triangles.Length / 3)
            + " submeshes=" + smr.sharedMesh.subMeshCount + " meshBounds=" + b.size.ToString("F3")
            + " rendererWorldBounds=" + rb.size.ToString("F3") + " bones=" + smr.bones.Length
            + " root=" + (smr.rootBone != null ? smr.rootBone.name : "null")
            + " rootScale=" + inst.transform.localScale.ToString("F4")
            + " smrScale=" + smr.transform.localScale.ToString("F4")
            + " avatar=" + (avatar != null ? avatar.name : "null"));
        Debug.Log("PIG_CLIPS " + string.Join(", ", assets.OfType<AnimationClip>().Select(c => c.name + "(" + c.length.ToString("F2") + "s" + (c.isLooping ? ",loop" : "") + ")").ToArray()));
        Debug.Log("PIG_HIERARCHY " + string.Join("|", inst.GetComponentsInChildren<Transform>().Take(12).Select(t => t.name).ToArray()) + " ...");
        UnityEngine.Object.DestroyImmediate(inst);

        Mark(bundle, new[] { prefabPath, fbx, ctrlPath, dir + "/PigBase.mat", texs[0], texs[1], texs[2], texs[3] }.Concat(audioPaths).ToArray());
        Debug.Log("BUILT_MODEL Pig01A -> bundle '" + bundle + "'");
    }

    /// <param name="asIs">Keep the clip untouched: channels, sample rate and samples (PCM). Used for the
    /// flat 2D click grunt. Everything else is a positional voice: mono, 22.05 kHz, Vorbis.</param>
    static void ImportAudio(string path, bool asIs)
    {
        var ai = AssetImporter.GetAtPath(path) as AudioImporter;
        if (ai == null) throw new Exception("no AudioImporter for " + path);
        ai.forceToMono = !asIs;
        ai.loadInBackground = false;
        ai.ambisonic = false;
        var s = ai.defaultSampleSettings;
        s.loadType = AudioClipLoadType.DecompressOnLoad;      // short clips; decoded once, cheap to play
        if (asIs)
        {
            s.compressionFormat = AudioCompressionFormat.PCM;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        }
        else
        {
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.5f;
            s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            s.sampleRateOverride = 22050;
        }
        ai.defaultSampleSettings = s;
        ai.SaveAndReimport();
    }
}
