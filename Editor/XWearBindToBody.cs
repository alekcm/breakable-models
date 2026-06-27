#if UNITY_EDITOR
// XWearBindToBody.cs
// Re-binds a clothing .xwear SkinnedMeshRenderer onto the BODY's bones.
//
// WHY: the Animator only animates the body. Clothing (.xwear) has its own
// skeleton, and copying transforms every frame (Skeleton Binder) breaks when
// the body has a non-unit scale (e.g. scale=100 from Mixamo) -> clothing
// collapses into a ball.
//
// FIX: replace each clothing bone with the matching BODY bone and recompute
// bindposes. After this, one Animator drives BOTH the body and the clothing.
// No per-frame script, immune to scale, mathematically exact.
//
// HOW TO USE:
//   1. Put your BODY (the one with the Animator) into T-pose in the scene:
//        - select it, disable the Animator, or
//        - open the FBX Rig tab -> Configure so you can see the T-pose,
//          then come back to the scene with the Animator OFF.
//      The body must visually be in its bind/rest pose when you press Bind.
//   2. Select, in the Hierarchy, the CLOTHING GameObject(s) you want to bind
//      (the .xwear roots, each containing its own Armature + SkinnedMeshRenderer).
//   3. Menu: Tools / XWear / Bind Clothing To Body.
//   4. Drag the BODY root into "Body Root", click "Bind Selected Clothing".
//   5. Re-enable the Animator. Both body and clothing now animate together.
//
// NOTES:
//   - A copy of each clothing mesh (with recomputed bindposes) is saved as an
//     asset next to the clothing, so the original .xwear import is untouched.
//   - Bones with a humanoid match go to the body bones.
//     Bones WITHOUT a match (physics/skirt/hair/ribbon bones that have no
//     equivalent in the body) are attached to their nearest matched ancestor
//     on the body, so they still move (but without secondary physics for now).
//   - Canonical name matching is identical to XWearSkeletonBinder, so clothing
//     bones (j_bip_*) match body bones whether the body is mixamorig:* or
//     already renamed (Hips, Spine, ...).

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace XWearImporter
{
    public class XWearBindToBody : EditorWindow
    {
        const string MENU = "Tools/XWear/Bind Clothing To Body";

        SerializedProperty bodyRootProp;
        Transform bodyRoot;

        [MenuItem(MENU)]
        static void Open()
        {
            var w = GetWindow<XWearBindToBody>("Bind Clothing");
            w.minSize = new Vector2(340, 140);
        }

        void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Bind .xwear clothing onto the BODY's bones", EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(4);

            bodyRoot = (Transform)EditorGUILayout.ObjectField(
                "Body Root (with Animator)", bodyRoot, typeof(Transform), true);

            EditorGUILayout.Space(8);

            bool ok = bodyRoot != null && Selection.gameObjects.Length > 0;
            using (new EditorGUI.DisabledScope(!ok))
            {
                if (GUILayout.Button("Bind Selected Clothing", GUILayout.Height(34)))
                    BindAll();
            }

            EditorGUILayout.Space(4);
            if (Selection.gameObjects.Length == 0)
                EditorGUILayout.HelpBox("Select the clothing GameObject(s) in the Hierarchy first.", MessageType.Info);
            else
                EditorGUILayout.LabelField($"Selected: {Selection.gameObjects.Length} object(s)", EditorStyles.miniLabel);

            EditorGUILayout.HelpBox(
                "IMPORTANT: the Body must be in T-pose when you click Bind (Animator OFF). " +
                "Bones without a humanoid match (skirt/hair physics) are tied to the nearest body bone for now.",
                MessageType.Warning);
        }

        void BindAll()
        {
            // 1) Build canonical bone map of the body
            var bodyMap = new Dictionary<string, Transform>();
            CollectBonesCanonically(bodyRoot, bodyMap);
            if (bodyMap.Count == 0)
            {
                EditorUtility.DisplayDialog("Bind Clothing",
                    "No canonical humanoid bones found under Body Root.\n" +
                    "Make sure Body Root is the transform that contains the armature (Hips/Spine/...).", "OK");
                return;
            }

            // 2) Try to force the body into bind pose
            TryForceTPose(bodyRoot);

            int bound = 0;
            foreach (var go in Selection.gameObjects)
            {
                var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (smrs.Length == 0)
                {
                    Debug.LogWarning($"[XWearBind] {go.name}: no SkinnedMeshRenderer found, skipped.");
                    continue;
                }

                foreach (var smr in smrs)
                {
                    BindOne(smr, go.transform, bodyMap);
                    bound++;
                }

                EditorUtility.SetDirty(go);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[XWearBind] Bound {bound} SkinnedMeshRenderer(s) to body '{bodyRoot.name}'. " +
                      "Re-enable the Animator and press Play.");
            EditorUtility.DisplayDialog("Bind Clothing",
                $"Bound {bound} renderer(s) to the body.\nRe-enable the Animator and test.", "OK");
        }

        void BindOne(SkinnedMeshRenderer smr, Transform clothingRoot, Dictionary<string, Transform> bodyMap)
        {
            if (smr.sharedMesh == null) return;

            var oldBones = smr.bones;
            if (oldBones == null || oldBones.Length == 0) return;

            var newBones = new Transform[oldBones.Length];
            for (int i = 0; i < oldBones.Length; i++)
                newBones[i] = ResolveBodyBone(oldBones[i], clothingRoot, bodyMap);

            Transform newRoot = ResolveBodyBone(smr.rootBone, clothingRoot, bodyMap);
            if (newRoot == null)
            {
                foreach (var nb in newBones)
                    if (nb != null) { newRoot = nb; break; }
            }
            if (newRoot == null)
            {
                Debug.LogWarning($"[XWearBind] {smr.name}: could not resolve any body bone, skipped.");
                return;
            }

            // Clone the mesh and recompute bindposes for the body bones.
            // bindpose[i] = bone.worldToLocalMatrix * smr.localToWorldMatrix  (Unity standard)
            // sampled in T-pose -> scale (even 100) is baked consistently and cancels out.
            Mesh src = smr.sharedMesh;
            Mesh dst = Instantiate(src);
            dst.name = src.name + "_bound";

            var bp = new Matrix4x4[newBones.Length];
            Matrix4x4 smrLTW = smr.transform.localToWorldMatrix;
            for (int i = 0; i < newBones.Length; i++)
                bp[i] = newBones[i] != null
                    ? newBones[i].worldToLocalMatrix * smrLTW
                    : Matrix4x4.identity;
            dst.bindposes = bp;

            smr.bones = newBones;
            smr.rootBone = newRoot;
            smr.sharedMesh = dst;

            // Save the cloned mesh next to the clothing asset so it persists
            SaveMeshAsset(dst, smr, clothingRoot);
        }

        Transform ResolveBodyBone(Transform clothBone, Transform clothingRoot, Dictionary<string, Transform> bodyMap)
        {
            if (clothBone == null) return null;

            // Direct canonical match
            string canon = GetCanonicalBoneName(clothBone.name);
            if (!string.IsNullOrEmpty(canon) && bodyMap.TryGetValue(canon, out var direct))
                return direct;

            // No direct match (physics bone etc.) -> climb to nearest matched ancestor
            Transform p = clothBone.parent;
            while (p != null)
            {
                string pc = GetCanonicalBoneName(p.name);
                if (!string.IsNullOrEmpty(pc) && bodyMap.TryGetValue(pc, out var pb))
                    return pb;
                if (p == clothingRoot) break;
                p = p.parent;
            }
            return null;
        }

        static void TryForceTPose(Transform root)
        {
            var anim = root.GetComponentInParent<Animator>();
            if (anim == null)
            {
                anim = root.GetComponentInChildren<Animator>();
            }
            if (anim != null)
            {
                bool wasEnabled = anim.enabled;
                anim.enabled = false;
                anim.Rebind();
                anim.WriteDefaultValues();
                anim.enabled = wasEnabled;
            }
        }

        static void SaveMeshAsset(Mesh mesh, SkinnedMeshRenderer smr, Transform clothingRoot)
        {
            string dir = "Assets/XWearBound";
            if (!AssetDatabase.IsValidFolder(dir))
                AssetDatabase.CreateFolder("Assets", "XWearBound");

            string baseName = string.IsNullOrEmpty(clothingRoot.name) ? "clothing" : clothingRoot.name;
            string smrName = string.IsNullOrEmpty(smr.name) ? "smr" : smr.name;
            string path = AssetDatabase.GenerateUniqueAssetPath(
                $"{dir}/{baseName}_{smrName}_{mesh.name}.asset");

            AssetDatabase.CreateAsset(mesh, path);
            Debug.Log($"[XWearBind] Saved bound mesh -> {path}");
        }

        // ---- canonical bone matching (identical to XWearSkeletonBinder) ----

        static void CollectBonesCanonically(Transform current, Dictionary<string, Transform> dict)
        {
            if (current == null) return;

            string canon = GetCanonicalBoneName(current.name);
            if (!string.IsNullOrEmpty(canon) && !dict.ContainsKey(canon))
                dict[canon] = current;

            for (int i = 0; i < current.childCount; i++)
                CollectBonesCanonically(current.GetChild(i), dict);
        }

        static string GetCanonicalBoneName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return "";

            string n = rawName.ToLowerInvariant();

            n = n.Replace("mixamorig:", "")
                 .Replace("j_bip_c_", "")
                 .Replace("j_bip_l_", "l_")
                 .Replace("j_bip_r_", "r_")
                 .Replace("j_bip_", "")
                 .Replace("character_", "");

            bool isLeft = n.StartsWith("l_") || n.EndsWith("_l") || n.Contains("left");
            bool isRight = n.StartsWith("r_") || n.EndsWith("_r") || n.Contains("right");

            string role = "";
            if (n.Contains("hips") || n.Contains("pelvis")) role = "hips";
            else if (n.Contains("upperchest") || n.Contains("spine2")) role = "upperchest";
            else if (n.Contains("chest") || n.Contains("spine1")) role = "chest";
            else if (n.Contains("spine")) role = "spine";
            else if (n.Contains("neck")) role = "neck";
            else if (n.Contains("head")) role = "head";

            else if (n.Contains("shoulder")) role = "shoulder";
            else if (n.Contains("forearm") || n.Contains("lowerarm")) role = "lowerarm";
            else if (n.Contains("upperarm") || n.Contains("arm")) role = "upperarm";
            else if (n.Contains("hand")) role = "hand";

            else if (n.Contains("upleg") || n.Contains("upperleg")) role = "upperleg";
            else if (n.Contains("lowerleg") || n.Contains("calf") || (n.Contains("leg") && !n.Contains("upleg"))) role = "lowerleg";
            else if (n.Contains("foot")) role = "foot";
            else if (n.Contains("toebase") || n.Contains("toes") || n.Contains("toe")) role = "toes";

            if (string.IsNullOrEmpty(role)) return n;

            string side = isLeft ? "l_" : (isRight ? "r_" : "c_");
            return side + role;
        }
    }
}
#endif
