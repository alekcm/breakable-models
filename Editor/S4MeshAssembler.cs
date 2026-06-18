// S4MeshAssembler.cs — Unity Editor tool for assembling split s4extract meshes into prefabs.
// Drop this file into Assets/Editor (or any Editor folder) in your Unity project.
//
// How to use:
//   1. Run s4extract with --split-mode islands|components (or subsets) to get many small FBX/OBJ files.
//   2. In Unity, open: Tools / s4extract / S4 Mesh Assembler
//   3. Click "Load folder" and select the folder with the exported parts.
//   4. Tick the parts you want to group together (e.g. the chair back).
//   5. Click "Create Prefab from Selected" — it makes one prefab with those parts as children.
//   6. Or "Create Separate Prefab for Each" — one prefab per selected part.
//   7. Save / Load assembly presets to quickly recreate the same grouping later.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class S4MeshAssembler : EditorWindow
{
    // Data for each loaded part
    private class PartInfo
    {
        public string assetPath;
        public string name;
        public GameObject prefabAsset;
        public Mesh mesh;
        public bool selected = false;
        public int verts;
        public int faces;
        public bool hasCollider;
        public bool isVisible = true;
    }

    private string folderPath = "";
    private List<PartInfo> parts = new List<PartInfo>();
    private Vector2 scroll;

    private bool addRigidbody = true;
    private bool staticRoot = false;
    private bool addColliders = true;
    private bool useConvexColliders = true;
    private bool keepAsChildren = true;
    private string prefabName = "Assembly_";

    private string searchFilter = "";
    private bool showOnlySelected = false;

    private const string PREFS_FOLDER = "S4MeshAssembler_Folder";

    [MenuItem("Tools/s4extract/S4 Mesh Assembler", false, 200)]
    static void ShowWindow()
    {
        var window = GetWindow<S4MeshAssembler>("S4 Assembler");
        window.minSize = new Vector2(520, 420);
    }

    private void OnEnable()
    {
        folderPath = EditorPrefs.GetString(PREFS_FOLDER, "");
        if (!string.IsNullOrEmpty(folderPath))
            LoadFolder(folderPath, false);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Source folder", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        folderPath = EditorGUILayout.TextField(folderPath);
        if (GUILayout.Button("Browse…", GUILayout.Width(80)))
        {
            string p = EditorUtility.OpenFolderPanel("Select folder with split meshes", folderPath, "");
            if (!string.IsNullOrEmpty(p))
            {
                // Convert absolute path to project-relative if possible
                if (p.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                    p = "Assets" + p.Substring(Application.dataPath.Length);
                folderPath = p;
                LoadFolder(folderPath, true);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField($"Loaded parts: {parts.Count}", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        searchFilter = EditorGUILayout.TextField("Search", searchFilter, GUILayout.Width(220));
        showOnlySelected = GUILayout.Toggle(showOnlySelected, "Only selected", GUILayout.Width(100));
        if (GUILayout.Button("Select all", GUILayout.Width(80)))
            SetSelection(true);
        if (GUILayout.Button("Select none", GUILayout.Width(80)))
            SetSelection(false);
        EditorGUILayout.EndHorizontal();

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(180));
        foreach (var p in parts)
        {
            if (!string.IsNullOrEmpty(searchFilter) && !p.name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (showOnlySelected && !p.selected)
                continue;

            EditorGUILayout.BeginHorizontal();
            p.selected = GUILayout.Toggle(p.selected, "", GUILayout.Width(18));
            EditorGUILayout.LabelField(p.name, GUILayout.Width(220));
            EditorGUILayout.LabelField($"{p.verts}v / {p.faces}f", GUILayout.Width(80));
            EditorGUILayout.LabelField(p.hasCollider ? "collider" : "no collider", GUILayout.Width(70));
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Output settings", EditorStyles.boldLabel);
        prefabName = EditorGUILayout.TextField("Prefab name", prefabName);
        addRigidbody = EditorGUILayout.Toggle("Root Rigidbody", addRigidbody);
        if (addRigidbody) staticRoot = EditorGUILayout.Toggle("  Static (isKinematic)", staticRoot);
        addColliders = EditorGUILayout.Toggle("Add MeshColliders", addColliders);
        if (addColliders) useConvexColliders = EditorGUILayout.Toggle("  Convex", useConvexColliders);
        keepAsChildren = EditorGUILayout.Toggle("Keep as child meshes", keepAsChildren);
        if (!keepAsChildren)
            EditorGUILayout.HelpBox("Baking to one mesh loses the ability to detach individual parts later. Use only for static scenery.", MessageType.Warning);

        EditorGUILayout.Space(10);
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = parts.Any(p => p.selected) && !string.IsNullOrWhiteSpace(prefabName);
        if (GUILayout.Button("Create Prefab from Selected", GUILayout.Height(36)))
            CreatePrefabFromSelected();
        GUI.enabled = parts.Any(p => p.selected);
        if (GUILayout.Button("Separate Prefab for Each", GUILayout.Height(36)))
            CreateSeparatePrefabs();
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save Assembly Preset"))
            SavePreset();
        if (GUILayout.Button("Load Assembly Preset"))
            LoadPreset();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Tip: use --split-mode components or islands in s4extract. In Unity, tick the parts that belong to one logical object (e.g. chair back) and create a prefab. Each part keeps its own MeshCollider, so the parent prefab behaves as a single object but parts can be detached.",
            MessageType.Info);
    }

    private void SetSelection(bool value)
    {
        foreach (var p in parts)
        {
            if (!string.IsNullOrEmpty(searchFilter) && !p.name.Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            if (showOnlySelected && !p.selected)
                continue;
            p.selected = value;
        }
        Repaint();
    }

    private void LoadFolder(string path, bool savePrefs)
    {
        parts.Clear();
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        string[] files = Directory.GetFiles(path, "*.fbx", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(path, "*.obj", SearchOption.TopDirectoryOnly))
            .OrderBy(f => f)
            .ToArray();

        foreach (string f in files)
        {
            string assetPath = f.Replace("\\", "/");
            if (assetPath.StartsWith(Application.dataPath, StringComparison.OrdinalIgnoreCase))
                assetPath = "Assets" + assetPath.Substring(Application.dataPath.Length);
            else if (!assetPath.StartsWith("Assets/"))
                continue; // skip files outside the project

            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (go == null) continue;

            Mesh mesh = GetFirstMesh(go);
            if (mesh == null) continue;

            parts.Add(new PartInfo
            {
                assetPath = assetPath,
                name = go.name,
                prefabAsset = go,
                mesh = mesh,
                verts = mesh.vertexCount,
                faces = mesh.triangles.Length / 3,
                hasCollider = go.GetComponentInChildren<Collider>(true) != null,
            });
        }

        if (savePrefs)
            EditorPrefs.SetString(PREFS_FOLDER, folderPath);
    }

    private Mesh GetFirstMesh(GameObject source)
    {
        MeshFilter mf = source.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
        foreach (MeshFilter child in source.GetComponentsInChildren<MeshFilter>(true))
        {
            if (child.sharedMesh != null) return child.sharedMesh;
        }
        return null;
    }

    private Material GetMaterialFromSource(GameObject source)
    {
        Renderer r = source.GetComponent<Renderer>();
        if (r != null && r.sharedMaterial != null) return r.sharedMaterial;
        foreach (Renderer child in source.GetComponentsInChildren<Renderer>(true))
        {
            if (child.sharedMaterial != null) return child.sharedMaterial;
        }
        return null;
    }

    private void CreatePrefabFromSelected()
    {
        List<PartInfo> selected = parts.Where(p => p.selected).ToList();
        if (selected.Count == 0) return;

        string folder = Path.GetDirectoryName(selected[0].assetPath).Replace("\\", "/");
        string prefabPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + prefabName + ".prefab");

        GameObject root = new GameObject(prefabName);
        Undo.RegisterCreatedObjectUndo(root, "Create S4 assembly prefab");

        if (addRigidbody)
        {
            Rigidbody rb = root.AddComponent<Rigidbody>();
            rb.mass = selected.Count * 1.5f;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.05f;
            rb.useGravity = true;
            rb.isKinematic = staticRoot;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        }

        if (keepAsChildren)
        {
            foreach (PartInfo p in selected)
            {
                AddPartAsChild(root, p);
            }
        }
        else
        {
            Mesh combined = BakeMeshes(selected.Select(p => p.mesh).ToArray(), root.name + "_mesh");
            if (combined != null)
            {
                string meshPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + root.name + "_mesh.asset");
                AssetDatabase.CreateAsset(combined, meshPath);
                GameObject visual = new GameObject("CombinedMesh");
                visual.transform.SetParent(root.transform, false);
                visual.AddComponent<MeshFilter>().sharedMesh = combined;
                Renderer mr = visual.AddComponent<MeshRenderer>();
                mr.sharedMaterial = GetSharedMaterial(selected);
                if (addColliders)
                {
                    MeshCollider mc = visual.AddComponent<MeshCollider>();
                    mc.sharedMesh = combined;
                    mc.convex = useConvexColliders;
                }
            }
        }

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        EditorGUIUtility.PingObject(Selection.activeObject);
        DestroyImmediate(root);
        Debug.Log($"s4extract: created assembly prefab at {prefabPath}");
    }

    private void CreateSeparatePrefabs()
    {
        List<PartInfo> selected = parts.Where(p => p.selected).ToList();
        if (selected.Count == 0) return;

        string folder = Path.GetDirectoryName(selected[0].assetPath).Replace("\\", "/");
        foreach (PartInfo p in selected)
        {
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + p.name + "_solo.prefab");
            GameObject root = new GameObject(p.name + "_solo");
            Undo.RegisterCreatedObjectUndo(root, "Create S4 solo prefab");

            if (addRigidbody)
            {
                Rigidbody rb = root.AddComponent<Rigidbody>();
                rb.mass = 1f;
                rb.useGravity = true;
                rb.isKinematic = staticRoot;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            AddPartAsChild(root, p);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            DestroyImmediate(root);
        }
        Debug.Log($"s4extract: created {selected.Count} solo prefab(s) in {folder}");
    }

    private void AddPartAsChild(GameObject root, PartInfo p)
    {
        GameObject child = new GameObject(p.name);
        child.transform.SetParent(root.transform, false);
        child.AddComponent<MeshFilter>().sharedMesh = p.mesh;
        Renderer mr = child.AddComponent<MeshRenderer>();
        Material mat = GetMaterialFromSource(p.prefabAsset);
        mr.sharedMaterial = mat;

        if (addColliders)
        {
            MeshCollider mc = child.AddComponent<MeshCollider>();
            mc.sharedMesh = p.mesh;
            mc.convex = useConvexColliders;
        }
    }

    private Material GetSharedMaterial(List<PartInfo> selected)
    {
        foreach (var p in selected)
        {
            Material m = GetMaterialFromSource(p.prefabAsset);
            if (m != null) return m;
        }
        return null;
    }

    private Mesh BakeMeshes(Mesh[] meshes, string meshName)
    {
        if (meshes.Length == 0) return null;
        if (meshes.Length == 1)
        {
            Mesh copy = Instantiate(meshes[0]);
            copy.name = meshName;
            return copy;
        }

        List<Vector3> verts = new List<Vector3>();
        List<Vector3> norms = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> tris = new List<int>();
        int baseIndex = 0;

        foreach (Mesh m in meshes)
        {
            Vector3[] mv = m.vertices;
            Vector3[] mn = m.normals;
            Vector2[] mu = m.uv;
            int[] mt = m.triangles;
            for (int i = 0; i < mv.Length; i++) verts.Add(mv[i]);
            if (mn != null && mn.Length == mv.Length) norms.AddRange(mn);
            else for (int i = 0; i < mv.Length; i++) norms.Add(Vector3.up);
            if (mu != null && mu.Length == mv.Length) uvs.AddRange(mu);
            else for (int i = 0; i < mv.Length; i++) uvs.Add(Vector2.zero);
            for (int i = 0; i < mt.Length; i++) tris.Add(mt[i] + baseIndex);
            baseIndex += mv.Length;
        }

        Mesh combined = new Mesh();
        combined.name = meshName;
        combined.vertices = verts.ToArray();
        combined.normals = norms.ToArray();
        combined.uv = uvs.ToArray();
        combined.triangles = tris.ToArray();
        combined.RecalculateBounds();
        combined.RecalculateNormals();
        return combined;
    }

    // --- Presets: simple JSON with selected part names ---
    [Serializable]
    private class AssemblyPreset
    {
        public string prefabName;
        public string folderPath;
        public List<string> selectedNames = new List<string>();
        public bool addRigidbody;
        public bool staticRoot;
        public bool addColliders;
        public bool useConvexColliders;
        public bool keepAsChildren;
    }

    private void SavePreset()
    {
        if (parts.Count == 0) return;
        string path = EditorUtility.SaveFilePanel("Save assembly preset", folderPath, prefabName + "_assembly", "json");
        if (string.IsNullOrEmpty(path)) return;

        AssemblyPreset preset = new AssemblyPreset
        {
            prefabName = prefabName,
            folderPath = folderPath,
            selectedNames = parts.Where(p => p.selected).Select(p => p.name).ToList(),
            addRigidbody = addRigidbody,
            staticRoot = staticRoot,
            addColliders = addColliders,
            useConvexColliders = useConvexColliders,
            keepAsChildren = keepAsChildren,
        };
        File.WriteAllText(path, JsonUtility.ToJson(preset, true));
        Debug.Log($"s4extract: assembly preset saved to {path}");
    }

    private void LoadPreset()
    {
        string path = EditorUtility.OpenFilePanel("Load assembly preset", folderPath, "json");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        AssemblyPreset preset = JsonUtility.FromJson<AssemblyPreset>(File.ReadAllText(path));
        if (preset == null) return;

        prefabName = preset.prefabName;
        folderPath = preset.folderPath;
        addRigidbody = preset.addRigidbody;
        staticRoot = preset.staticRoot;
        addColliders = preset.addColliders;
        useConvexColliders = preset.useConvexColliders;
        keepAsChildren = preset.keepAsChildren;

        LoadFolder(folderPath, true);
        HashSet<string> names = new HashSet<string>(preset.selectedNames);
        foreach (var p in parts)
            p.selected = names.Contains(p.name);
        Repaint();
        Debug.Log($"s4extract: assembly preset loaded from {path}");
    }
}
#endif
