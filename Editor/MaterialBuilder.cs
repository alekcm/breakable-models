// MaterialBuilder.cs
// Builds materials for Unity 6 (URP, HDRP, Built-in Standard) from VRoid .xwear MToon properties.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace XWearImporter
{
    public static class MaterialBuilder
    {
        public static Material BuildMToonFallback(JSONObject mtoonMat,
                                                  Dictionary<string, Texture2D> texturesByGuid,
                                                  string sourceAssetPath)
        {
            Shader shader = null;

            // 1. Try matching active render pipeline
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                string rpName = GraphicsSettings.currentRenderPipeline.GetType().Name;
                if (rpName.Contains("Universal")) shader = Shader.Find("Universal Render Pipeline/Lit");
                else if (rpName.Contains("HD"))   shader = Shader.Find("HDRP/Lit");
            }

            // 2. Fallbacks
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("HDRP/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogWarning("[XWear] No standard PBR shader available — using fallback.");
                shader = Shader.Find("Sprites/Default");
            }

            var mat = new Material(shader);
            if (mtoonMat == null || !mtoonMat.HasField("ShaderProperties"))
                return mat;

            Color shadeColor     = new Color(0.42f, 0.40f, 0.40f, 1f);
            bool  hasShadeColor  = false;
            float alphaCutoff    = 0.5f;
            float doubleSided    = 0f;
            float zWrite         = 1f;
            bool  hasAlphaTest   = false;
            bool  hasTransparent = false;

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
                            if (mat.HasProperty("_BaseColor"))    mat.SetColor("_BaseColor", col);
                            if (mat.HasProperty("_Color"))        mat.SetColor("_Color", col);
                        }
                        else if (name == "_EmissionColor")
                        {
                            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", col);
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
                            if (v == 1f) hasAlphaTest = true;
                            if (v == 2f) hasTransparent = true;
                        }
                        else if (name == "_Cutoff")
                        {
                            alphaCutoff = v;
                        }
                        else if (name == "_TransparentWithZWrite")
                        {
                            if (v > 0f) { hasTransparent = true; zWrite = 1f; }
                        }
                        else if (name == "_DoubleSided")
                        {
                            doubleSided = v;
                        }
                        else if (name == "_BumpScale")
                        {
                            if (mat.HasProperty("_NormalScale")) mat.SetFloat("_NormalScale", v);
                            if (mat.HasProperty("_BumpScale"))   mat.SetFloat("_BumpScale", v);
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
                            if (mat.HasProperty("_BaseMap"))      mat.SetTexture("_BaseMap", tex);
                            if (mat.HasProperty("_BaseColorMap")) mat.SetTexture("_BaseColorMap", tex);
                            if (mat.HasProperty("_MainTex"))      mat.SetTexture("_MainTex", tex);
                        }
                        else if (name == "_BumpMap")
                        {
                            if (mat.HasProperty("_NormalMap")) mat.SetTexture("_NormalMap", tex);
                            if (mat.HasProperty("_BumpMap"))   mat.SetTexture("_BumpMap", tex);
                        }
                        else if (name == "_EmissionMap")
                        {
                            if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", tex);
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
                catch (Exception ex)
                {
                    Debug.LogWarning("[XWear] Skipped property " + name + ": " + ex.Message);
                }
            }

            // Explicitly configure Tiling & Offset so textures match standard UV bounds
            if (mat.HasProperty("_BaseColorMap"))
            {
                mat.SetTextureScale("_BaseColorMap", new Vector2(1f, 1f));
                mat.SetTextureOffset("_BaseColorMap", new Vector2(0f, 0f));
            }
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTextureScale("_BaseMap", new Vector2(1f, 1f));
                mat.SetTextureOffset("_BaseMap", new Vector2(0f, 0f));
            }
            if (mat.HasProperty("_MainTex"))
            {
                mat.SetTextureScale("_MainTex", new Vector2(1f, 1f));
                mat.SetTextureOffset("_MainTex", new Vector2(0f, 0f));
            }

            // HDRP specific mapping settings
            if (mat.HasProperty("_UVBase")) mat.SetFloat("_UVBase", 0f); // 0 = UV0 mapping
            if (mat.HasProperty("_BaseColorMap_UVMappingMask"))
                mat.SetVector("_BaseColorMap_UVMappingMask", new Vector4(1f, 0f, 0f, 0f));
            if (mat.HasProperty("_UVMappingMask"))
                mat.SetVector("_UVMappingMask", new Vector4(1f, 0f, 0f, 0f));
            if (mat.HasProperty("_BaseColorMap_ST"))
                mat.SetVector("_BaseColorMap_ST", new Vector4(1f, 1f, 0f, 0f));

            // Cel-shading mixing
            if (hasShadeColor)
            {
                Color baseCol = Color.white;
                if (mat.HasProperty("_BaseColor")) baseCol = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("_Color")) baseCol = mat.GetColor("_Color");

                float mix = 0.35f;
                Color shaded = new Color(
                    Mathf.Lerp(baseCol.r, shadeColor.r, mix),
                    Mathf.Lerp(baseCol.g, shadeColor.g, mix),
                    Mathf.Lerp(baseCol.b, shadeColor.b, mix),
                    baseCol.a);

                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", shaded);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color", shaded);
            }

            // Setup Cutout / Transparency mode settings
            if (hasAlphaTest || hasTransparent)
            {
                if (mat.HasProperty("_AlphaCutoff")) mat.SetFloat("_AlphaCutoff", alphaCutoff);
                if (mat.HasProperty("_Cutoff"))      mat.SetFloat("_Cutoff", alphaCutoff);

                if (hasAlphaTest)
                {
                    // Full Cutout Setup (HDRP, URP, Built-in)
                    if (mat.HasProperty("_AlphaCutoffEnable")) mat.SetFloat("_AlphaCutoffEnable", 1f); // HDRP
                    if (mat.HasProperty("_AlphaClip"))         mat.SetFloat("_AlphaClip", 1f);         // URP / HDRP

                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHACLIP_ON");
                    mat.EnableKeyword("_ALPHA_CLIP");

                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.renderQueue = 2450;
                }
                else
                {
                    // Full Transparent Setup (HDRP, URP, Built-in)
                    if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f); // HDRP Transparent
                    if (mat.HasProperty("_Surface"))     mat.SetFloat("_Surface", 1f);     // URP Transparent

                    if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f); // Alpha blend
                    if (mat.HasProperty("_SrcBlend"))  mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    if (mat.HasProperty("_DstBlend"))  mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    if (mat.HasProperty("_ZWrite"))    mat.SetFloat("_ZWrite", zWrite);

                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.EnableKeyword("_SURFACE_TRANSPARENT");
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHACLIP_ON");

                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = 3000;
                }
            }

            // Double Sided setup
            if (doubleSided > 0f)
            {
                if (mat.HasProperty("_DoubleSidedEnable")) mat.SetFloat("_DoubleSidedEnable", 1f);
                if (mat.HasProperty("_DoubleSided"))       mat.SetFloat("_DoubleSided", 1f);
                if (mat.HasProperty("_Cull"))              mat.SetFloat("_Cull", 0f); // 0 = Off

                mat.EnableKeyword("_DOUBLESIDED_ON");
            }

            if (mtoonMat.HasField("RenderQueue"))
                mat.renderQueue = mtoonMat.GetField("RenderQueue").i;

            return mat;
        }
    }
}
