// MaterialBuilder.cs
// Builds materials for Unity 6 HDRP from VRoid .xwear MToon properties.
//
// Strategy: always use HDRP/Lit (or Standard as last fallback). HDRP/Lit is
// always available in HDRP projects and always compiles.  MToon-specific
// toon shading (rim, outline, matcap, shade ramp) is approximated by mixing
// the shade color into the base color, so even without a custom shader the
// clothing looks closer to the intended toon style.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace XWearImporter
{
    public static class MaterialBuilder
    {
        public static Material BuildMToonForHDRP(JSONObject mtoonMat,
                                                 Dictionary<string, Texture2D> texturesByGuid,
                                                 string sourceAssetPath)
        {
            // Pick the safest shader that always compiles in HDRP.
            // Try a sequence of fallbacks: HDRP/Lit, URP/Lit, Standard.
            // (URP/Lit is included in case the user has HDRP but accidentally
            // removed the HDRP shader package.)
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogWarning("[XWear] No HDRP/URP/Standard shader available — material will be invalid.");
                shader = Shader.Find("Sprites/Default");   // absolute last resort
            }
            var mat = new Material(shader);

            if (mtoonMat == null || !mtoonMat.HasField("ShaderProperties"))
                return mat;

            // Helpers we accumulate while iterating properties.
            Color shadeColor = new Color(0.42f, 0.40f, 0.40f, 1f);
            bool hasShadeColor = false;
            float alphaCutoff  = 0.5f;
            float doubleSided  = 0f;
            float zWrite       = 0f;
            bool  hasAlphaTest = false;

            foreach (JSONObject sp in mtoonMat.GetField("ShaderProperties").list)
            {
                if (sp == null) continue;
                string name = sp.HasField("PropertyName") ? sp.GetField("PropertyName").str : null;
                string type = sp.HasField("$type") ? (sp.GetField("$type").str ?? "") : "";
                if (string.IsNullOrEmpty(name)) continue;

                try
                {
                    if (type.Contains("ShaderColorProperty"))
                    {
                        JSONObject c = sp.GetField("Color");
                        Color col = new Color(
                            (float)c.GetField("r").ff,
                            (float)c.GetField("g").ff,
                            (float)c.GetField("b").ff,
                            (float)c.GetField("a").ff);

                        if (name == "_Color")
                        {
                            mat.SetColor("_BaseColor", col);
                        }
                        else if (name == "_EmissionColor")
                        {
                            mat.SetColor("_EmissionColor", col);
                        }
                        else if (name == "_ShadeColor")
                        {
                            shadeColor = col;
                            hasShadeColor = true;
                        }
                        else if (mat.HasProperty(name))
                        {
                            mat.SetColor(name, col);
                        }
                    }
                    else if (type.Contains("ShaderFloatProperty"))
                    {
                        float v = (float)sp.GetField("Value").ff;

                        if (name == "_AlphaMode")
                        {
                            // MToon: 0=Opaque, 1=Cutout, 2=Transparent
                            if (v >= 1f) hasAlphaTest = true;
                        }
                        else if (name == "_Cutoff")
                        {
                            alphaCutoff = v;
                        }
                        else if (name == "_TransparentWithZWrite")
                        {
                            zWrite = v;
                        }
                        else if (name == "_DoubleSided")
                        {
                            doubleSided = v;
                        }
                        else if (name == "_BumpScale")
                        {
                            if (mat.HasProperty("_NormalScale")) mat.SetFloat("_NormalScale", v);
                        }
                        else if (mat.HasProperty(name))
                        {
                            mat.SetFloat(name, v);
                        }
                    }
                    else if (type.Contains("ShaderTextureProperty"))
                    {
                        string guid = sp.GetField("TextureGuid").str;
                        Texture2D tex = null;
                        if (texturesByGuid != null && guid != null)
                            texturesByGuid.TryGetValue(guid, out tex);
                        if (tex == null) continue;

                        if (name == "_MainTex" || name == "_ShadeTex")
                        {
                            // Fold shade texture into the base map so a flat
                            // PBR look emerges.
                            mat.SetTexture("_BaseMap", tex);
                        }
                        else if (name == "_BumpMap")
                        {
                            mat.SetTexture("_NormalMap", tex);
                        }
                        else if (name == "_EmissionMap")
                        {
                            mat.SetTexture("_EmissionMap", tex);
                        }
                        else if (mat.HasProperty(name))
                        {
                            mat.SetTexture(name, tex);
                        }
                    }
                    else if (type.Contains("ShaderIntProperty"))
                    {
                        int v = sp.GetField("Value").i;
                        if (mat.HasProperty(name)) mat.SetInt(name, v);
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[XWear] Skipped property " + name + ": " + ex.Message);
                }
            }

            // Soft cel-shading approximation without a custom shader.
            if (hasShadeColor && mat.HasProperty("_BaseColor"))
            {
                Color baseCol = mat.GetColor("_BaseColor");
                float shade = 0.5f;
                Color shaded = new Color(
                    baseCol.r * (1f - shade) + shadeColor.r * shade,
                    baseCol.g * (1f - shade) + shadeColor.g * shade,
                    baseCol.b * (1f - shade) + shadeColor.b * shade,
                    baseCol.a);
                mat.SetColor("_BaseColor", shaded);
            }

            // Configure HDRP/Lit for cutout transparency when needed.
            if (hasAlphaTest)
            {
                if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", alphaCutoff);
                if (mat.HasProperty("_SurfaceType"))  mat.SetFloat("_SurfaceType", 1f);   // 1 = Transparent
                if (mat.HasProperty("_BlendMode"))    mat.SetFloat("_BlendMode", 0f);     // 0 = Alpha
                if (mat.HasProperty("_ZWrite"))      mat.SetFloat("_ZWrite", zWrite);
                if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", doubleSided);
                mat.SetOverrideTag("RenderType", "TransparentCutout");
                mat.renderQueue = 2450;
                if (mat.HasProperty("_AlphaToCoverage")) mat.SetFloat("_AlphaToCoverage", 1f);
            }

            // Honor explicit render queue from the .xwear JSON if present.
            if (mtoonMat.HasField("RenderQueue"))
                mat.renderQueue = mtoonMat.GetField("RenderQueue").i;

            return mat;
        }
    }
}
