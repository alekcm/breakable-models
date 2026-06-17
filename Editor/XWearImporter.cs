// XWearImporter.cs (v5 — bulletproof)
// Unity 6 (6000.x) Editor importer for VRoid .xwear clothing/accessory files.
// Reverse-engineered from the proprietary .xwear ZIP container produced by
// VRoid Studio's dress-up feature.
//
// This revision wraps every potentially-failing step in try/catch with
// Debug.LogError reporting, so a malformed .xwear produces a readable
// diagnostic in the Console instead of a NullReferenceException.

using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.Rendering;

namespace XWearImporter
{
    // VERSION 6: bumped to force Unity to recompile & reimport.
    // Fixes: JSON parser null handling, ParseNumber safety, ParseObject colon check
    [ScriptedImporter(version: 6, ext: "xwear")]
    public class XWearImporter : ScriptedImporter
    {
        // -- Inspector knobs (visible on the asset) -------------------------
        public bool importMesh      = true;
        public bool importSkeleton  = true;
        public bool importMaterials = true;
        public bool importTextures  = true;
        public float importScale    = 1.0f;
        public bool flipZ           = true;

        // -- Importer entry point ------------------------------------------
        public override void OnImportAsset(AssetImportContext ctx)
        {
            Debug.Log($"[XWear v6] Importing {ctx.assetPath}");

            XWearAsset asset = new XWearAsset();
            asset.sourcePath = ctx.assetPath;
            asset.guid       = AssetDatabase.AssetPathToGUID(ctx.assetPath);

            try
            {
                ReadXWear(ctx.assetPath, asset);
                BuildPrefab(ctx, asset);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[XWear] Failed to import .xwear: {ex.Message}\n{ex.StackTrace}");
                // Still add the empty asset so Unity shows something.
            }

            ctx.AddObjectToAsset("XWearMetadata", asset);
        }

        // -- Read the .xwear ZIP container ----------------------------------
        static void ReadXWear(string path, XWearAsset asset)
        {
            asset.entries.Clear();
            try
            {
                using var zip = ZipFile.OpenRead(path);
                foreach (var entry in zip.Entries)
                {
                    try
                    {
                        string key = entry.FullName.Replace('\\', '/');
                        using var ms = new MemoryStream();
                        entry.Open().CopyTo(ms);
                        asset.entries[key] = ms.ToArray();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[XWear] Skipped entry {entry.FullName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[XWear] Failed to read ZIP: {ex.Message}");
                return;
            }

            try { asset.xItemJson   = ParseJson(GetEntry(asset, "Body/XItem.json/XItem.json")); }
            catch (Exception ex) { Debug.LogWarning($"[XWear] XItem.json failed: {ex.Message}"); asset.xItemJson = new JSONObject(); }

            try { asset.xResources  = ParseJson(GetEntryByPrefix(asset, "Body/XResources/")); }
            catch (Exception ex) { Debug.LogWarning($"[XWear] XResources failed: {ex.Message}"); asset.xResources = new JSONObject(); }

            try { asset.meshBytes   = GetEntryByPrefix(asset, "Mesh/"); }
            catch (Exception ex) { Debug.LogWarning($"[XWear] Mesh load failed: {ex.Message}"); asset.meshBytes = Array.Empty<byte>(); }

            try { asset.ParsePhysBones(); }
            catch (Exception ex) { Debug.LogWarning($"[XWear] PhysBones parse failed: {ex.Message}"); }
        }

        static byte[] GetEntry(XWearAsset a, string name) =>
            a.entries.TryGetValue(name, out var b) ? b : Array.Empty<byte>();

        static byte[] GetEntryByPrefix(XWearAsset a, string prefix)
        {
            foreach (var kv in a.entries)
                if (kv.Key.StartsWith(prefix)) return kv.Value;
            return Array.Empty<byte>();
        }

        static JSONObject ParseJson(byte[] data) =>
            data.Length == 0 ? new JSONObject() : JSONObject.Parse(Encoding.UTF8.GetString(data));

        // -- Build a Prefab GameObject -------------------------------------
        void BuildPrefab(AssetImportContext ctx, XWearAsset asset)
        {
            var root = new GameObject("XWearRoot");

            // ---- Load texture files into the asset database ----------------
            var texturesByGuid = new Dictionary<string, Texture2D>();
            try { LoadTextures(ctx, asset, texturesByGuid); }
            catch (Exception ex) { Debug.LogWarning($"[XWear] Texture loading failed: {ex.Message}"); }

            // ---- Parse binary mesh ----------------------------------------
            XWearMeshData meshData = null;
            try { meshData = XWearBinaryReader.Read(asset.meshBytes, asset.xResources); }
            catch (Exception ex) { Debug.LogWarning($"[XWear] Mesh parse failed: {ex.Message}"); meshData = null; }

            // ---- Build skeleton FIRST (need bone count before mesh) ----
            Transform[] bonesByGuid = null;
            var boneNameByGuid = new Dictionary<string, string>();
            var boneTfByGuid  = new Dictionary<string, Transform>();   // GUID → Transform
            try
            {
                if (importSkeleton
                    && asset.xResources != null
                    && asset.xResources.HasField("RootGameObject"))
                {
                    JSONObject rootGO = asset.xResources.GetField("RootGameObject");
                    if (rootGO != null)
                        bonesByGuid = BuildSkeleton(asset.xResources, meshData,
                            out boneNameByGuid, out boneTfByGuid);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[XWear] Skeleton build failed: {ex.Message}\n{ex.StackTrace}");
                bonesByGuid = null;
                boneNameByGuid.Clear();
                boneTfByGuid.Clear();
            }
            int boneLimit = bonesByGuid != null && bonesByGuid.Length > 0
                             ? bonesByGuid.Length : 73;

            // ---- Build mesh asset ------------------------------------------
            Mesh mesh = null;
            if (importMesh && meshData != null)
            {
                try { mesh = BuildMeshAsset(ctx, meshData, boneLimit); }
                catch (Exception ex) { Debug.LogWarning($"[XWear] Mesh asset build failed: {ex.Message}"); }
            }

// (skeleton already built above)

            // ---- Bind mesh to skeleton -----------------------------------
            if (mesh != null && bonesByGuid != null)
            {
                try
                {
                    mesh.bindposes = new Matrix4x4[bonesByGuid.Length];
                    for (int i = 0; i < bonesByGuid.Length; i++)
                        mesh.bindposes[i] = bonesByGuid[i].worldToLocalMatrix;
                }
                catch (Exception ex) { Debug.LogWarning($"[XWear] Bindpose failed: {ex.Message}"); }
            }

            // ---- Build materials -----------------------------------------
            var materialsByGuid = new Dictionary<string, Material>();
            try { BuildMaterials(ctx, asset, texturesByGuid, materialsByGuid); }
            catch (Exception ex) { Debug.LogWarning($"[XWear] Material build failed: {ex.Message}"); }

            // ---- Attach SkinnedMeshRenderer -------------------------------
            try
            {
                var smr = root.AddComponent<SkinnedMeshRenderer>();
                if (mesh != null)
                {
                    smr.sharedMesh = mesh;
                    if (bonesByGuid != null) smr.bones = bonesByGuid;
                    smr.rootBone = bonesByGuid != null && bonesByGuid.Length > 0
                        ? bonesByGuid[0] : null;
                    smr.localBounds = mesh.bounds;

                    var mats = new Material[mesh.subMeshCount];
                    if (asset.xItemJson != null && asset.xItemJson.HasField("XResourceMaterials"))
                    {
                        int idx = 0;
                        foreach (JSONObject m in asset.xItemJson.GetField("XResourceMaterials").list)
                            mats[idx++] = materialsByGuid.TryGetValue(m.GetField("Guid").str, out var mm)
                                          ? mm : null;
                    }
                    smr.sharedMaterials = mats;
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[XWear] SkinnedMeshRenderer attach failed: {ex.Message}"); }

            // ---- Add SpringBone physics components ----------------------
            try
            {
                if (bonesByGuid != null)
                {
                    var boneTfByName = new Dictionary<string, Transform>();
                    foreach (var t in bonesByGuid)
                        if (t != null && !boneTfByName.ContainsKey(t.name))
                            boneTfByName[t.name] = t;

                    foreach (var pb in asset.physBones)
                    {
                        if (string.IsNullOrEmpty(pb.rootBoneGuid)) continue;
                        if (!boneTfByName.TryGetValue(pb.rootBoneGuid, out var boneTf)) continue;

                        var spring = boneTf.gameObject.AddComponent<XWearSpringBone>();
                        spring.pull             = pb.pull;
                        spring.stiffness        = pb.stiffness;
                        spring.spring           = pb.spring;
                        spring.gravity          = pb.gravity;
                        spring.immobile         = pb.immobile;
                        spring.maxStretch       = pb.maxStretch;
                        spring.maxSquish        = pb.maxSquish;
                        spring.integrationType  = pb.integrationType;
                        spring.radius           = 0.01f;
                        spring.useCollisions    = pb.allowCollision != 0;
                        spring.Initialize(boneTf);
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[XWear] SpringBone attach failed: {ex.Message}"); }

            // ---- Embed everything as sub-assets --------------------------
            // CRITICAL: do NOT call PrefabUtility.SaveAsPrefabAsset inside
            // OnImportAsset — that triggers a re-import of the saved .prefab,
            // which re-enters this importer (infinite recursion -> NRE in
            // PackageImportTreeView).  Embed the assembled GameObject as a
            // sub-asset of the .xwear file itself.
            ctx.AddObjectToAsset("XWearPrefab", root);
            ctx.SetMainObject(root);
        }

        // -- Texture loader -------------------------------------------------
        void LoadTextures(AssetImportContext ctx, XWearAsset asset, Dictionary<string, Texture2D> texturesByGuid)
        {
            if (!importTextures || asset.xItemJson == null || !asset.xItemJson.HasField("XResourceTextures"))
                return;

            foreach (JSONObject t in asset.xItemJson.GetField("XResourceTextures").list)
            {
                if (t == null || !t.HasField("Guid")) continue;
                string guid    = t.GetField("Guid").str;
                if (string.IsNullOrEmpty(guid)) continue;
                string texKey = $"Textures/{guid}.png";
                if (!asset.entries.TryGetValue(texKey, out var pngBytes)) continue;

                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.name = (t.HasField("Name") ? t.GetField("Name").str : null) ?? guid;
                if (!tex.LoadImage(pngBytes, false))
                {
                    Debug.LogWarning($"[XWear] Failed to load {texKey}");
                    continue;
                }

                bool isNormal = t.HasField("TextureImportSettings") &&
                                t.GetField("TextureImportSettings").HasField("isNormal") &&
                                t.GetField("TextureImportSettings").GetField("isNormal").b;
                if (isNormal) tex.name += " (Normal)";

                ctx.AddObjectToAsset($"tex_{guid}", tex);
                texturesByGuid[guid] = tex;
            }
        }

        // -- Mesh asset builder --------------------------------------------
        Mesh BuildMeshAsset(AssetImportContext ctx, XWearMeshData meshData, int boneLimit)
        {
            Mesh mesh = new Mesh
            {
                name = meshData.name ?? "XWearMesh",
                indexFormat = (meshData.indices != null && meshData.indices.Length > 65535)
                    ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.vertices  = meshData.positions ?? new Vector3[0];
            mesh.normals   = meshData.normals   ?? new Vector3[0];
            mesh.tangents  = meshData.tangents  ?? new Vector4[0];
            mesh.uv        = meshData.uvs       ?? new Vector2[0];

            if (meshData.boneIndices != null && meshData.boneWeights != null)
            {
                // boneLimit is the array length passed in from the caller.
                var weights = new BoneWeight[meshData.positions.Length];
                for (int i = 0; i < weights.Length && i < meshData.boneIndices.GetLength(0); i++)
                {
                    // Clamp bone indices to a valid range.  VRoid's .xwear binary
                    // format isn't fully reverse-engineered, so the raw bytes may
                    // contain out-of-range indices.  Clamping prevents Unity's
                    // SkinnedMeshRenderer "Bone index is not within the number of bones"
                    // error without making the mesh completely unusable.
                    weights[i].boneIndex0 = Mathf.Clamp(meshData.boneIndices[i, 0], 0, boneLimit - 1);
                    weights[i].boneIndex1 = Mathf.Clamp(meshData.boneIndices[i, 1], 0, boneLimit - 1);
                    weights[i].boneIndex2 = Mathf.Clamp(meshData.boneIndices[i, 2], 0, boneLimit - 1);
                    weights[i].boneIndex3 = Mathf.Clamp(meshData.boneIndices[i, 3], 0, boneLimit - 1);

                    // Normalize weights so they sum to 1.0 — the VRoid binary
                    // weights are not always normalized, but Unity's SMR
                    // expects them to sum to ~1.0.
                    float w0 = meshData.boneWeights[i, 0];
                    float w1 = meshData.boneWeights[i, 1];
                    float w2 = meshData.boneWeights[i, 2];
                    float w3 = meshData.boneWeights[i, 3];
                    float sum = w0 + w1 + w2 + w3;
                    if (sum > 1e-4f)
                    {
                        weights[i].weight0 = w0 / sum;
                        weights[i].weight1 = w1 / sum;
                        weights[i].weight2 = w2 / sum;
                        weights[i].weight3 = w3 / sum;
                    }
                    else
                    {
                        weights[i].weight0 = 1f;
                        weights[i].weight1 = 0f;
                        weights[i].weight2 = 0f;
                        weights[i].weight3 = 0f;
                    }
                }
                mesh.boneWeights = weights;
            }

            if (meshData.indices != null)
            {
                // VRoid .xwear stores triangle indices in CCW order (glTF-like
                // convention). Unity expects CW so that front-faces are visible.
                // Swap index 1 and index 2 of each triangle.
                int triCount = meshData.indices.Length / 3;
                int[] flipped = new int[meshData.indices.Length];
                for (int t = 0; t < triCount; t++)
                {
                    int i0 = meshData.indices[t * 3 + 0];
                    int i1 = meshData.indices[t * 3 + 1];
                    int i2 = meshData.indices[t * 3 + 2];
                    flipped[t * 3 + 0] = i0;
                    flipped[t * 3 + 1] = i2;
                    flipped[t * 3 + 2] = i1;
                }
                mesh.SetIndices(flipped, MeshTopology.Triangles, 0, true);
            }

            mesh.RecalculateBounds();
            mesh.RecalculateUVDistributionMetrics(0);
            ctx.AddObjectToAsset("mesh", mesh);
            return mesh;
        }

        // -- Materials ------------------------------------------------------
        void BuildMaterials(AssetImportContext ctx, XWearAsset asset,
                             Dictionary<string, Texture2D> texturesByGuid,
                             Dictionary<string, Material> materialsByGuid)
        {
            if (!importMaterials || asset.xItemJson == null || !asset.xItemJson.HasField("XResourceMaterials"))
                return;
            int submeshIdx = 0;
            foreach (JSONObject m in asset.xItemJson.GetField("XResourceMaterials").list)
            {
                if (m == null) continue;
                Material mat = MaterialBuilder.BuildMToonForHDRP(m, texturesByGuid, ctx.assetPath);
                mat.name = (m.HasField("Name") ? m.GetField("Name").str : null)
                           ?? (m.HasField("Guid") ? m.GetField("Guid").str : null)
                           ?? $"Material_{submeshIdx}";
                ctx.AddObjectToAsset($"mat_{m.GetField("Guid").str ?? submeshIdx.ToString()}", mat);
                materialsByGuid[m.GetField("Guid").str] = mat;
                submeshIdx++;
            }
        }

        // -- Skeleton builder ------------------------------------------------
        Transform[] BuildSkeleton(JSONObject xResources, XWearMeshData meshData,
                                   out Dictionary<string, string> nameByGuid,
                                   out Dictionary<string, Transform> boneTfByGuid)
        {
            var boneByGuid = new Dictionary<string, Transform>();
            nameByGuid    = new Dictionary<string, string>();
            boneTfByGuid  = boneByGuid;

            if (xResources == null)
            {
                Debug.LogError("[XWear] xResources is null — XResources JSON was not parsed");
                return new Transform[0];
            }
            if (!xResources.HasField("RootGameObject"))
            {
                Debug.LogError($"[XWear] xResources has no RootGameObject. Keys present: " +
                    string.Join(",", xResources.dict.Keys));
                return new Transform[0];
            }
            JSONObject rootGO = xResources.GetField("RootGameObject");
            if (rootGO == null)
            {
                Debug.LogError("[XWear] RootGameObject is null");
                return new Transform[0];
            }
            Debug.Log($"[XWear] RootGameObject name='{(rootGO.HasField("Name") ? rootGO.GetField("Name").str : "?")}' " +
                $"children={(rootGO.HasField("Children") ? rootGO.GetField("Children").list.Count : 0)}");

            try { BuildBoneRecursive(rootGO, null, boneByGuid, nameByGuid); }
            catch (Exception ex) { Debug.LogError($"[XWear] Bone recursion failed: {ex.Message}"); }

            Debug.Log($"[XWear] boneByGuid.Count={boneByGuid.Count}, meshData.boneGuidsInOrder=" +
                $"{(meshData?.boneGuidsInOrder == null ? "null" : meshData.boneGuidsInOrder.Length.ToString())}");

            // Order bones according to meshData.boneGuidsInOrder (the
            // SkinnedMeshRenderer bone list).  When that list is null (SMR
            // not found in JSON), emit bones in insertion order.
            var ordered = new List<Transform>();
            if (meshData?.boneGuidsInOrder != null && meshData.boneGuidsInOrder.Length > 0)
            {
                foreach (var bg in meshData.boneGuidsInOrder)
                    if (boneByGuid.TryGetValue(bg, out var t))
                        ordered.Add(t);
            }
            else
            {
                foreach (var t in boneByGuid.Values) ordered.Add(t);
            }
            Debug.Log($"[XWear] Built {ordered.Count} bones (from {boneByGuid.Count} parsed)");
            return ordered.ToArray();
        }

        void BuildBoneRecursive(JSONObject node, Transform parent,
                                  Dictionary<string, Transform> boneByGuid,
                                  Dictionary<string, string> nameByGuid)
        {
            if (node == null) return;

            string name = node.HasField("Name") ? node.GetField("Name").str : null;
            if (string.IsNullOrEmpty(name)) return;

            string guid = node.HasField("Guid") ? node.GetField("Guid").str : "";
            if (string.IsNullOrEmpty(guid)) guid = name;

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            try
            {
                var t  = node.GetField("Transform");
                var lp = t.GetField("LocalPosition");
                var lr = t.GetField("LocalRotation");
                var ls = t.GetField("LocalScale");
                go.transform.localPosition = new Vector3(lp.GetField("x").ff, lp.GetField("y").ff,
                                                         flipZ ? -lp.GetField("z").ff : lp.GetField("z").ff);
                go.transform.localRotation = new Quaternion(lr.GetField("x").ff, lr.GetField("y").ff,
                                                            flipZ ? -lr.GetField("z").ff : lr.GetField("z").ff,
                                                            lr.GetField("w").ff);
                go.transform.localScale = new Vector3(ls.GetField("x").ff, ls.GetField("y").ff, ls.GetField("z").ff);
            }
            catch (Exception) { /* leave at identity */ }

            boneByGuid[guid] = go.transform;
            nameByGuid[guid] = name;

            if (node.HasField("Children"))
            {
                var children = node.GetField("Children").list;
                if (children != null)
                {
                    foreach (JSONObject child in children)
                        if (child != null)
                            BuildBoneRecursive(child, go.transform, boneByGuid, nameByGuid);
                }
            }
        }
    }

    // -- Lightweight JSON parser (Newtonsoft.Json isn't strictly required) --
    public class JSONObject
    {
        public Dictionary<string, JSONObject> dict = new();
        public List<JSONObject> list = new();
        public string str;
        public double n;
        public bool b;

        public bool isDict => dict.Count > 0;
        public bool isList => list.Count > 0;
        public double f => n;
        public float  ff => (float)n;
        public int i => (int)n;

        public bool HasField(string k) => dict.ContainsKey(k);
        public JSONObject GetField(string k) => dict.TryGetValue(k, out var v) ? v : new JSONObject();

        public static JSONObject Parse(string json)
        {
            var p = new JsonParser(json);
            p.SkipWs();
            var v = p.ParseValue();
            p.SkipWs();
            return v;
        }
    }

    class JsonParser
    {
        string s; int i;
        public JsonParser(string s) { this.s = s; this.i = 0; }

        public void SkipWs() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        public JSONObject ParseValue()
        {
            SkipWs();
            if (i >= s.Length) return new JSONObject();
            char c = s[i];
            if (c == '{') return ParseObject();
            if (c == '[') return ParseArray();
            if (c == '"') return ParseString();
            if (c == 't' || c == 'f') return ParseBool();
            if (c == 'n')  // JSON null
            {
                if (i + 4 <= s.Length && s.Substring(i, 4) == "null")
                {
                    i += 4;
                    return new JSONObject();   // empty obj == null
                }
            }
            return ParseNumber();
        }

        JSONObject ParseObject()
        {
            var o = new JSONObject();
            i++;   // skip '{'
            SkipWs();
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (true)
            {
                SkipWs();
                if (i >= s.Length || s[i] == '}') { if (i < s.Length) i++; break; }
                var key = ParseString().str;
                SkipWs();
                if (i < s.Length && s[i] == ':') i++;   // only skip if it's really ':'
                var val = ParseValue();
                if (key != null) o.dict[key] = val;
                SkipWs();
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                break;
            }
            return o;
        }

        JSONObject ParseArray()
        {
            var o = new JSONObject();
            i++;
            SkipWs();
            if (i < s.Length && s[i] == ']') { i++; return o; }
            while (true)
            {
                o.list.Add(ParseValue());
                SkipWs();
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                break;
            }
            return o;
        }

        JSONObject ParseString()
        {
            var o = new JSONObject();
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char n = s[i + 1];
                    sb.Append(n switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '"' => '"',
                        '\\' => '\\',
                        _ => n,
                    });
                    i += 2;
                }
                else
                {
                    sb.Append(s[i]);
                    i++;
                }
            }
            i++;
            o.str = sb.ToString();
            return o;
        }

        JSONObject ParseNumber()
        {
            var o = new JSONObject();
            int start = i;
            if (i < s.Length && s[i] == '-') i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-'))
                i++;
            if (i == start) i++;   // safety: always advance so we don't loop forever
            double.TryParse(s.Substring(start, i - start), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out o.n);
            return o;
        }

        JSONObject ParseBool()
        {
            var o = new JSONObject();
            if (s.Substring(i, 4) == "true")  { o.b = true;  i += 4; }
            else if (s.Substring(i, 5) == "false") { o.b = false; i += 5; }
            return o;
        }
    }
}
