// XWearBinaryReader.cs
// Parses the binary "Mesh/<uuid>" blob inside a .xwear file.  Format
// (verified against .xwear samples produced by VRoid Studio):
//
//   uint32  magic      = 0
//   uint8   nameLen
//   char[]  name       (UTF-8, length = nameLen)
//   int32   vertexCount
//   int32   _padding   (often == vertexCount; ignored)
//   float32 positions  [vertexCount * 3]
//   float32 normals    [vertexCount * 3]
//   float32 tangents   [vertexCount * 4]
//   float32 uvs        [vertexCount * 2]
//   uint32  boneIdx    [vertexCount] (4 uint8 packed little-endian)
//   float32 boneWeight [vertexCount * 4]
//   ...    aux block   (proprietary; size varies)
//   uint32  indices    [vertexCount] at the very end of the file

using System;
using System.IO;
using UnityEngine;

namespace XWearImporter
{
    public class XWearMeshData
    {
        public string  name;
        public int     vertexCount;
        public Vector3[] positions;
        public Vector3[] normals;
        public Vector4[] tangents;
        public Vector2[] uvs;
        public byte[,] boneIndices;       // [vertexCount, 4]
        public float[,] boneWeights;      // [vertexCount, 4]
        public int[]   indices;
        public string[] boneGuidsInOrder; // populated from XResources.SkinnedMeshRenderer.Bones
        public string  rootBoneGuid;
    }

    public static class XWearBinaryReader
    {
        public static XWearMeshData Read(byte[] data, JSONObject xResources)
        {
            // Initialize all collections up front so callers can rely on
            // non-null defaults even when the SkinnedMeshRenderer component
            // is not found in the .xwear JSON.
            var mesh = new XWearMeshData
            {
                name            = "",
                vertexCount     = 0,
                positions       = new Vector3[0],
                normals         = new Vector3[0],
                tangents        = new Vector4[0],
                uvs             = new Vector2[0],
                boneIndices     = new byte[0, 0],
                boneWeights     = new float[0, 0],
                indices         = new int[0],
                boneGuidsInOrder = System.Array.Empty<string>(),
                rootBoneGuid    = "",
            };

            using var ms = new MemoryStream(data, writable: false);
            using var br = new BinaryReader(ms);

            uint magic = br.ReadUInt32();
            if (magic != 0)
                throw new InvalidDataException($"Unexpected XWear mesh magic: 0x{magic:X8}");

            int nameLen = br.ReadByte();
            string name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nameLen));

            int vertexCount = br.ReadInt32();
            br.ReadInt32(); // padding

            var positions = new Vector3[vertexCount];
            var normals   = new Vector3[vertexCount];
            var tangents  = new Vector4[vertexCount];
            var uvs       = new Vector2[vertexCount];
            var boneIdx   = new byte[vertexCount, 4];
            var boneW     = new float[vertexCount, 4];

            for (int i = 0; i < vertexCount; i++)
                positions[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

            for (int i = 0; i < vertexCount; i++)
                normals[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

            for (int i = 0; i < vertexCount; i++)
                tangents[i] = new Vector4(br.ReadSingle(), br.ReadSingle(),
                                          br.ReadSingle(), br.ReadSingle());

            for (int i = 0; i < vertexCount; i++)
                uvs[i] = new Vector2(br.ReadSingle(), 1.0f - br.ReadSingle());

            for (int i = 0; i < vertexCount; i++)
            {
                uint packed = br.ReadUInt32();
                boneIdx[i, 0] = (byte)(packed & 0xFF);
                boneIdx[i, 1] = (byte)((packed >> 8)  & 0xFF);
                boneIdx[i, 2] = (byte)((packed >> 16) & 0xFF);
                boneIdx[i, 3] = (byte)((packed >> 24) & 0xFF);
            }

            for (int i = 0; i < vertexCount; i++)
                boneW[i, 0] = br.ReadSingle();
            for (int i = 0; i < vertexCount; i++)
                boneW[i, 1] = br.ReadSingle();
            for (int i = 0; i < vertexCount; i++)
                boneW[i, 2] = br.ReadSingle();
            for (int i = 0; i < vertexCount; i++)
                boneW[i, 3] = br.ReadSingle();

            // ---- Indices: the LAST (vertexCount * 4) bytes, uint32 LE ----
            int[] indices = new int[vertexCount];
            int byteCount = vertexCount * 4;
            ms.Position = data.Length - byteCount;
            for (int i = 0; i < vertexCount; i++)
                indices[i] = (int)br.ReadUInt32();

            mesh.name           = name;
            mesh.vertexCount    = vertexCount;
            mesh.positions      = positions;
            mesh.normals        = normals;
            mesh.tangents       = tangents;
            mesh.uvs            = uvs;
            mesh.boneIndices    = boneIdx;
            mesh.boneWeights    = boneW;
            mesh.indices        = indices;

            // ---- SkinnedMeshRenderer metadata (bones in skinning order) --
            if (xResources != null)
            {
                var smr = FindComponent(xResources, "XResourceSkinnedMeshRenderer");
                if (smr != null)
                {
                    mesh.rootBoneGuid = smr.GetField("RootBoneGuid").str;
                    if (smr.HasField("Bones"))
                    {
                        var list = new System.Collections.Generic.List<string>();
                        foreach (var b in smr.GetField("Bones").list)
                            list.Add(b.GetField("BoneGuid").str);
                        mesh.boneGuidsInOrder = list.ToArray();
                    }
                }
            }

            return mesh;
        }

        static JSONObject FindComponent(JSONObject root, string typeSuffix)
        {
            // Walk recursively looking for a dict item whose "$type" field
            // ends with typeSuffix.  Returns the component itself (not its
            // parent), so the caller can access its fields directly.
            var stack = new System.Collections.Generic.Stack<JSONObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n == null) continue;

                // Recurse into "Components" arrays first
                if (n.HasField("Components"))
                {
                    foreach (var c in n.GetField("Components").list)
                    {
                        if (c == null || !c.HasField("$type")) continue;
                        string typeStr = c.GetField("$type").str ?? "";
                        if (typeStr.EndsWith(typeSuffix) || typeStr.Contains("." + typeSuffix))
                            return c;
                    }
                }
                // Then check the current node itself
                if (n.HasField("$type") && n.GetField("$type").str?.EndsWith(typeSuffix) == true)
                    return n;
                // And finally recurse into nested dicts/lists
                foreach (var kv in n.dict)
                {
                    if (kv.Value == null) continue;
                    stack.Push(kv.Value);
                }
            }
            return null;
        }
    }
}
