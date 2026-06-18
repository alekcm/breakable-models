// XWearAsset.cs
// Runtime asset class so imported .xwear files have a stable reference
// inside the AssetDatabase. Also stores parsed PhysBone (secondary physics)
// chains so the importer can recreate them on the prefab.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace XWearImporter
{
    public class XWearPhysBoneData
    {
        public string gameObjectGuid;
        public string rootBoneGuid;
        public float pull;
        public float stiffness;
        public float spring;
        public float gravity;
        public float immobile;
        public float maxStretch;
        public float maxSquish;
        public int   integrationType;
        public int   allowCollision; // 1 if the chain should bounce off body colliders
    }

    public class XWearAsset : ScriptableObject
    {
        public string sourcePath;
        public string guid;
        public Dictionary<string, byte[]> entries = new();
        public JSONObject xItemJson;
        public JSONObject xResources;
        public byte[] meshBytes;
        public List<XWearPhysBoneData> physBones = new();

        /// <summary>
        /// Walk the XResources Components list and collect every
        /// XResourcePhysBoneComponent into <see cref="physBones"/>.
        /// </summary>
        public void ParsePhysBones()
        {
            physBones.Clear();
            if (xResources == null || !xResources.HasField("Components")) return;
            foreach (JSONObject comp in xResources.GetField("Components").list)
            {
                if (!comp.HasField("PhysBoneParam")) continue;
                JSONObject p = comp.GetField("PhysBoneParam");

                physBones.Add(new XWearPhysBoneData
                {
                    gameObjectGuid  = comp.GetField("GameObjectGuid").str ?? "",
                    rootBoneGuid    = p.GetField("rootTransformGuid").str ?? "",
                    pull            = (float)p.GetField("pull").f,
                    stiffness       = (float)p.GetField("stiffness").f,
                    spring          = (float)p.GetField("spring").f,
                    gravity         = (float)p.GetField("gravity").f,
                    immobile        = (float)p.GetField("immobile").f,
                    maxStretch      = (float)p.GetField("maxStretch").f,
                    maxSquish       = (float)p.GetField("maxSquish").f,
                    integrationType = (int)p.GetField("integrationType").i,
                    allowCollision  = (int)p.GetField("allowCollision").i,
                });
            }
        }
    }
}
