using System;
using System.IO;
using UnityEngine;
using UnityEditor;

// Headless AssetBundle builder for Live-Stock Market art (Unity 2022.3.62f3 — matches FF).
// Copied into unity/bundler/Assets/Editor/ by tools/build-bundle.sh, then:
//   Unity.exe -batchmode -quit -projectPath unity/bundler -executeMethod BuildAssetBundles.Build -logFile <log>
//
// The sheep is a skinned mesh bound to the goat's own skeleton (art/sheep/sheep_rig.blend):
// the bundle carries the FBX's prefab (SkinnedMeshRenderer + bone transforms), three
// Standard-shader materials with the sheep textures, and the textures. At runtime the mod
// takes the mesh and the bone NAMES from the prefab and rebinds them to a live goat's bones.
public static class BuildAssetBundles
{
    const string OutDir = "AssetBundlesOut";

    public static void Build()
    {
        try
        {
            BuildSheep();
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

        foreach (var t in texs)
        {
            var ti = AssetImporter.GetAtPath(t) as TextureImporter;
            if (ti == null) throw new Exception("no texture at " + t);
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.alphaSource = TextureImporterAlphaSource.None;      // Standard Specular reads albedo alpha as smoothness
            ti.alphaIsTransparency = false;
            ti.SaveAndReimport();
        }

        var shader = Shader.Find("Standard");
        if (shader == null) throw new Exception("Standard shader not found.");
        var mats = new Material[3];
        for (int i = 0; i < 3; i++)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texs[i]);
            var m = new Material(shader) { name = names[i] };
            if (tex != null) m.mainTexture = tex;
            m.SetFloat("_Glossiness", 0.05f);
            AssetDatabase.CreateAsset(m, dir + "/" + names[i] + ".mat");
            mats[i] = m;
        }
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
            + " root=" + (smr.rootBone != null ? smr.rootBone.name : "null")
            + " rendererLocalScale=" + smr.transform.localScale.ToString("F4")
            + " rootLocalScale=" + inst.transform.localScale.ToString("F4"));
        Debug.Log("SHEEP_BONES " + string.Join("|", Array.ConvertAll(smr.bones, t => t.name)));
        UnityEngine.Object.DestroyImmediate(inst);

        foreach (var p in new[] { prefabPath, fbx, dir + "/SheepBody.mat", dir + "/SheepHead.mat", dir + "/SheepEye.mat", texs[0], texs[1], texs[2] })
        {
            var imp = AssetImporter.GetAtPath(p);
            if (imp != null) imp.SetAssetBundleNameAndVariant(bundle, "");
        }
        Debug.Log("BUILT_MODEL Sheep01A -> bundle '" + bundle + "'");
    }
}
