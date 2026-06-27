// XWearImporter.cs (v11 — corrected XWear axis conversion)
// Unity 6 (6000.x) Editor importer for VRoid .xwear clothing/accessory files.

using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace XWearImporter
{
    [ScriptedImporter(version: 11, ext: "xwear")]
    public class XWearImporter : ScriptedImporter
    {
        public bool importMesh      = true;
        public bool importSkeleton  = true;
        public bool importMaterials = true;
        public bool importTextures  = true;
        public float importScale    = 1.0f;

        public enum AxisConversion
        {
            [Tooltip("Use XWear coordinates as-is. This is the correct mode for VRoid Studio .xwear in Unity when clothing appears backwards only after the old Flip Z importer path.")]
            None,

            [Tooltip("Legacy mode from older importer versions: mirror Z and reverse triangle winding. Use only for already validated assets that require it.")]
            MirrorZ,

            [Tooltip("Rotate imported mesh/skeleton by 180 degrees around Y. Usually NOT recommended for clothing because it swaps world left/right.")]
            RotateY180,

            [Tooltip("Mirror Z, then rotate 180 around Y. Experimental fallback.")]
            MirrorZThenRotateY180
        }

        [Header("Coordinate Conversion")]
        [Tooltip("Axis conversion baked during import. For your VRoid/Mixamo setup use None, not a runtime 180-degree Transform rotation. If old imports show clothing front on the character's back, reimport with None.")]
        public AxisConversion axisConversion = AxisConversion.None;

        [HideInInspector]
        public bool flipZ = false; // Legacy serialized field from v10. Kept so old .meta files do not break, but no longer used.

        public override void OnImportAsset(AssetImportContext ctx)
        {
            Debug.Log($"[XWear v11] Importing {ctx.assetPath} | axisConversion={axisConversion}");

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
            }

            ctx.AddObjectToAsset("XWearMetadata", asset);
        }

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

            try { asset.xItemJson   = JSONObject.Parse(Encoding.UTF8.GetString(GetEntry(asset, "Body/XItem.json/XItem.json"))); }
            catch (Exception ex) { Debug.LogWarning($"[XWear] XItem.json failed: {ex.Message}"); asset.xItemJson = new JSONObject(); }

            try { asset.xResources  = JSONObject.Parse(Encoding.UTF8.GetString(GetEntryByPrefix(asset, "Body/XResources/"))); }
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

        void BuildPrefab(AssetImportContext ctx, XWearAsset asset)
        {
            var root = new GameObject(Path.GetFileNameWithoutExtension(ctx.assetPath));

            var texturesByGuid = new Dictionary<string, Texture2D>();
            try { LoadTextures(ctx, asset, texturesByGuid); }
            catch (Exception ex) { Debug.LogWarning($"[XWear] Texture loading failed: {ex.Message}"); }

            XWearMeshData meshData = null;
            try { meshData = XWearBinaryReader.Read(asset.meshBytes, asset.xResources); }
            catch (Exception ex) { Debug.LogWarning($"[XWear] Mesh parse failed: {ex.Message}\n{ex.StackTrace}"); meshData = null; }

            Transform[] bonesByGuid = null;
            Transform skeletonRoot = null;
            var boneNameByGuid = new Dictionary<string, string>();
            var boneTfByGuid  = new Dictionary<string, Transform>();
            try
            {
                if (importSkeleton && asset.xResources != null && asset.xResources.HasField("RootGameObject"))
                {
                    bonesByGuid = BuildSkeleton(asset.xResources, meshData, out skeletonRoot,
                        out boneNameByGuid, out boneTfByGuid);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[XWear] Skeleton build failed: {ex.Message}\n{ex.StackTrace}");
                bonesByGuid = null;
            }
            int boneLimit = bonesByGuid != null && bonesByGuid.Length > 0 ? bonesByGuid.Length : 73;

            if (skeletonRoot != null)
            {
                skeletonRoot.SetParent(root.transform, false);
            }

            Mesh mesh = null;
            if (importMesh && meshData != null)
            {
                try { mesh = BuildMeshAsset(ctx, meshData, boneLimit); }
                catch (Exception ex) { Debug.LogWarning($"[XWear] Mesh asset build failed: {ex.Message}\n{ex.StackTrace}"); }
            }

            if (mesh != null && bonesByGuid != null)
            {
                try
                {
                    var bindposes = new Matrix4x4[bonesByGuid.Length];
                    for (int i = 0; i < bonesByGuid.Length; i++)
                    {
                        if (bonesByGuid[i] != null)
                            bindposes[i] = bonesByGuid[i].worldToLocalMatrix * root.transform.localToWorldMatrix;
                        else
                            bindposes[i] = Matrix4x4.identity;
                    }
                    mesh.bindposes = bindposes;
                }
                catch (Exception ex) { Debug.LogWarning($"[XWear] Bindpose failed: {ex.Message}"); }
            }

            var materialsByGuid = new Dictionary<string, Material>();
            try { BuildMaterials(ctx, asset, texturesByGuid, materialsByGuid); }
            catch (Exception ex) { Debug.LogWarning($"[XWear] Material build failed: {ex.Message}"); }

            try
            {
                if (mesh != null)
                {
                    var meshGO = new GameObject("MeshRenderer");
                    meshGO.transform.SetParent(root.transform, false);
                    var smr = meshGO.AddComponent<SkinnedMeshRenderer>();

                    smr.sharedMesh = mesh;
                    if (bonesByGuid != null) smr.bones = bonesByGuid;
                    smr.rootBone = bonesByGuid != null && bonesByGuid.Length > 0
                        ? bonesByGuid[0] : null;
                    smr.localBounds = mesh.bounds;

                    var mats = new Material[mesh.subMeshCount];
                    if (meshData.refMaterialGuids != null && meshData.refMaterialGuids.Length > 0)
                    {
                        for (int i = 0; i < mesh.subMeshCount && i < meshData.refMaterialGuids.Length; i++)
                        {
                            mats[i] = materialsByGuid.TryGetValue(meshData.refMaterialGuids[i], out var mm)
                                      ? mm : null;
                        }
                    }
                    else if (asset.xItemJson != null && asset.xItemJson.HasField("XResourceMaterials"))
                    {
                        int idx = 0;
                        foreach (JSONObject m in asset.xItemJson.GetField("XResourceMaterials").list)
                        {
                            if (idx < mesh.subMeshCount)
                            {
                                mats[idx++] = materialsByGuid.TryGetValue(m.GetField("Guid").str, out var mm)
                                              ? mm : null;
                            }
                        }
                    }
                    smr.sharedMaterials = mats;
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[XWear] SkinnedMeshRenderer attach failed: {ex.Message}"); }

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

            ctx.AddObjectToAsset("XWearPrefab", root);
            ctx.SetMainObject(root);
        }

        Vector3 ConvertPoint(Vector3 v)
        {
            switch (axisConversion)
            {
                case AxisConversion.MirrorZ:
                    return new Vector3(v.x, v.y, -v.z);
                case AxisConversion.RotateY180:
                    return new Vector3(-v.x, v.y, -v.z);
                case AxisConversion.MirrorZThenRotateY180:
                    // Mirror Z -> (x,y,-z), then rotate Y 180 -> (-x,y,z)
                    return new Vector3(-v.x, v.y, v.z);
                case AxisConversion.None:
                default:
                    return v;
            }
        }

        Vector3 ConvertDirection(Vector3 v)
        {
            // Directions use the same linear part as points, without translation.
            return ConvertPoint(v);
        }

        bool ShouldReverseTriangleWinding()
        {
            // Mirroring changes handedness, rotations do not. Reverse winding only for
            // conversions with an odd number of mirrored axes.
            return axisConversion == AxisConversion.MirrorZ ||
                   axisConversion == AxisConversion.MirrorZThenRotateY180;
        }

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

                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                tex.name = (t.HasField("Name") ? t.GetField("Name").str : null) ?? guid;
                if (!tex.LoadImage(pngBytes, true))
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

        Mesh BuildMeshAsset(AssetImportContext ctx, XWearMeshData meshData, int boneLimit)
        {
            Mesh mesh = new Mesh
            {
                name = Path.GetFileNameWithoutExtension(ctx.assetPath) ?? "XWearMesh",
            };

            int vCount = meshData.vertexCount;
            var pos  = new Vector3[vCount];
            var norm = new Vector3[vCount];
            var tang = new Vector4[vCount];
            var uvs  = new Vector2[vCount];

            for (int i = 0; i < vCount; i++)
            {
                var p = meshData.positions[i] * importScale;
                pos[i] = ConvertPoint(p);

                var n = meshData.normals[i];
                norm[i] = ConvertDirection(n);

                var t = meshData.tangents[i];
                Vector3 tangentDirection = ConvertDirection(new Vector3(t.x, t.y, t.z));
                tang[i] = new Vector4(tangentDirection.x, tangentDirection.y, tangentDirection.z, t.w);

                uvs[i] = meshData.uvs[i];
            }

            mesh.vertices = pos;
            mesh.normals  = norm;
            mesh.tangents = tang;
            mesh.uv       = uvs;

            if (meshData.boneIndices != null && meshData.boneWeights != null && meshData.boneIndices.GetLength(0) >= vCount)
            {
                var weights = new BoneWeight[vCount];
                for (int i = 0; i < vCount; i++)
                {
                    weights[i].boneIndex0 = Mathf.Clamp(meshData.boneIndices[i, 0], 0, boneLimit - 1);
                    weights[i].boneIndex1 = Mathf.Clamp(meshData.boneIndices[i, 1], 0, boneLimit - 1);
                    weights[i].boneIndex2 = Mathf.Clamp(meshData.boneIndices[i, 2], 0, boneLimit - 1);
                    weights[i].boneIndex3 = Mathf.Clamp(meshData.boneIndices[i, 3], 0, boneLimit - 1);

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
                    }
                }
                mesh.boneWeights = weights;
            }

            if (meshData.submeshes != null && meshData.submeshes.Count > 0)
            {
                mesh.subMeshCount = meshData.submeshes.Count;
                for (int s = 0; s < meshData.submeshes.Count; s++)
                {
                    var sm = meshData.submeshes[s];
                    if (sm.indices == null) continue;

                    int triCount = sm.indices.Length / 3;
                    int[] indices = new int[sm.indices.Length];
                    for (int t = 0; t < triCount; t++)
                    {
                        if (ShouldReverseTriangleWinding())
                        {
                            indices[t * 3 + 0] = sm.indices[t * 3 + 0];
                            indices[t * 3 + 1] = sm.indices[t * 3 + 2];
                            indices[t * 3 + 2] = sm.indices[t * 3 + 1];
                        }
                        else
                        {
                            indices[t * 3 + 0] = sm.indices[t * 3 + 0];
                            indices[t * 3 + 1] = sm.indices[t * 3 + 1];
                            indices[t * 3 + 2] = sm.indices[t * 3 + 2];
                        }
                    }
                    mesh.SetIndices(indices, MeshTopology.Triangles, s, true);
                }
            }

            mesh.RecalculateBounds();
            mesh.RecalculateUVDistributionMetrics(0);
            ctx.AddObjectToAsset("mesh", mesh);
            return mesh;
        }

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
                Material mat = MaterialBuilder.BuildMToonFallback(m, texturesByGuid, ctx.assetPath);
                mat.name = (m.HasField("Name") ? m.GetField("Name").str : null)
                           ?? (m.HasField("Guid") ? m.GetField("Guid").str : null)
                           ?? $"Material_{submeshIdx}";
                ctx.AddObjectToAsset($"mat_{m.GetField("Guid").str ?? submeshIdx.ToString()}", mat);
                materialsByGuid[m.GetField("Guid").str] = mat;
                submeshIdx++;
            }
        }

        Transform[] BuildSkeleton(JSONObject xResources, XWearMeshData meshData, out Transform skeletonRoot,
                                   out Dictionary<string, string> nameByGuid,
                                   out Dictionary<string, Transform> boneTfByGuid)
        {
            var boneByGuid = new Dictionary<string, Transform>();
            nameByGuid    = new Dictionary<string, string>();
            boneTfByGuid  = boneByGuid;
            skeletonRoot  = null;

            if (xResources == null || !xResources.HasField("RootGameObject"))
                return new Transform[0];

            JSONObject rootGO = xResources.GetField("RootGameObject");
            if (rootGO == null)
                return new Transform[0];

            try 
            { 
                skeletonRoot = BuildBoneRecursive(rootGO, null, boneByGuid, nameByGuid); 
            }
            catch (Exception ex) { Debug.LogError($"[XWear] Bone recursion failed: {ex.Message}"); }

            var ordered = new List<Transform>();
            if (meshData?.boneGuidsInOrder != null && meshData.boneGuidsInOrder.Length > 0)
            {
                for (int i = 0; i < meshData.boneGuidsInOrder.Length; i++)
                {
                    string bg = meshData.boneGuidsInOrder[i];
                    if (boneByGuid.TryGetValue(bg, out var t))
                    {
                        if (meshData.bindposes != null && i < meshData.bindposes.Length)
                        {
                            var M_world = meshData.bindposes[i].inverse;
                            var worldPos = M_world.GetColumn(3) * importScale;
                            t.position = ConvertPoint(worldPos);
                        }
                        ordered.Add(t);
                    }
                    else
                    {
                        ordered.Add(null);
                    }
                }
            }
            else
            {
                foreach (var t in boneByGuid.Values) ordered.Add(t);
            }
            return ordered.ToArray();
        }

        Transform BuildBoneRecursive(JSONObject node, Transform parent,
                                  Dictionary<string, Transform> boneByGuid,
                                  Dictionary<string, string> nameByGuid)
        {
            if (node == null) return null;

            string name = node.HasField("Name") ? node.GetField("Name").str : null;
            if (string.IsNullOrEmpty(name)) return null;

            string guid = node.HasField("Guid") ? node.GetField("Guid").str : "";
            if (string.IsNullOrEmpty(guid)) guid = name;

            var go = new GameObject(name);
            if (parent != null)
                go.transform.SetParent(parent, false);

            try
            {
                var t  = node.GetField("Transform");
                var lp = t.GetField("LocalPosition");
                var ls = t.GetField("LocalScale");

                if (parent == null)
                {
                    go.transform.localPosition = Vector3.zero;
                }
                else
                {
                    var pos = new Vector3((float)lp.GetField("x").ff, (float)lp.GetField("y").ff, (float)lp.GetField("z").ff) * importScale;
                    go.transform.localPosition = ConvertPoint(pos);
                }

                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = new Vector3((float)ls.GetField("x").ff, (float)ls.GetField("y").ff, (float)ls.GetField("z").ff);
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

            return go.transform;
        }
    }
}
