using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS.EditorTools
{
    /// <summary>
    /// Flooded Grounds (Asset Store environment) ships Built-in shaders: Standard, its own surface shaders
    /// (PBR_TopBlend, PBR_Water, Triplanar, rotating skybox), Tree Creator and legacy particles. All of them
    /// render magenta in URP. This converts every material of the pack, including the tree prefabs' embedded
    /// optimized materials, to URP equivalents.
    /// </summary>
    public static class FloodedMaterials
    {
        public const string Folder = "Assets/Flooded_Grounds";

        [MenuItem("Tools/FPS/Fix Flooded Grounds Materials (URP)")]
        public static void FixAll()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) { Debug.LogWarning("[Flooded] pack not found"); return; }
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var mats = new HashSet<Material>();
            foreach (var g in AssetDatabase.FindAssets("t:Material", new[] { Folder }))
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                    if (o is Material m) mats.Add(m);
            // tree prefabs keep their generated "optimized" materials inside the prefab asset
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { Folder }))
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                    if (o is Material m) mats.Add(m);

            int n = 0;
            var log = new List<string>();
            foreach (var m in mats)
            {
                string before = m.shader ? m.shader.name : "null";
                if (Convert(m, lit)) { n++; EditorUtility.SetDirty(m); log.Add(m.name + ": " + before + " -> " + m.shader.name); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Flooded] converted {n} of {mats.Count} materials\n" + string.Join("\n", log));
        }

        static Texture Tex(Material m, string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
        static Color Col(Material m, string p, Color d) => m.HasProperty(p) ? m.GetColor(p) : d;
        static float Flt(Material m, string p, float d) => m.HasProperty(p) ? m.GetFloat(p) : d;

        static bool Convert(Material m, Shader lit)
        {
            string s = m.shader ? m.shader.name : "";
            if (s.StartsWith("Universal Render Pipeline/") || s.StartsWith("Skybox/")) return false;

            if (s == "Flooded_Grounds/Skybox_Rotating") return Sky(m);
            if (s.StartsWith("Legacy Shaders/Particles/")) return Particles(m, s.Contains("Additive"));
            if (s == "Flooded_Grounds/PBR_Water") return Water(m, lit);
            if (s == "Flooded_Grounds/PBR_TopBlend") return TopBlend(m, lit);
            if (s == "Flooded_Grounds/Triplanar_BumpSpec") return Triplanar(m, lit);
            if (s.Contains("Tree Creator") || s.Contains("TreeCreator")) return Tree(m, lit, s.Contains("Leaves"));
            if (s.StartsWith("Unlit/"))
            {
                var t = Tex(m, "_MainTex");
                Reset(m, Shader.Find("Universal Render Pipeline/Unlit"));
                m.SetTexture("_BaseMap", t);
                return true;
            }
            return ImportedMaterialFixer.Convert(m, lit);
        }

        static void Reset(Material m, Shader sh)
        {
            m.shader = sh;
            m.shaderKeywords = new string[0];
        }

        static void Opaque(Material m, bool cutout, float cutoff = 0.5f, bool twoSided = false)
        {
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", cutoff);
            if (cutout) m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)BlendMode.Zero);
            m.SetFloat("_ZWrite", 1f);
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.renderQueue = cutout ? (int)RenderQueue.AlphaTest : -1;
            m.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }

        static void Normal(Material m, Texture bump, float scale = 1f)
        {
            if (!bump) return;
            m.SetTexture("_BumpMap", bump);
            m.SetFloat("_BumpScale", scale);
            m.EnableKeyword("_NORMALMAP");
        }

        static bool Sky(Material m)
        {
            var cube = Tex(m, "_Tex");
            var tint = Col(m, "_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            float exp = Flt(m, "_Exposure", 1f), rot = Flt(m, "_Rotation", 0f);
            Reset(m, Shader.Find("Skybox/Cubemap"));
            m.SetTexture("_Tex", cube);
            m.SetColor("_Tint", tint);
            m.SetFloat("_Exposure", exp);
            m.SetFloat("_Rotation", rot);
            return true;
        }

        static bool Particles(Material m, bool additive)
        {
            var tex = Tex(m, "_MainTex");
            var tint = Col(m, "_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f)) * 2f;
            Reset(m, Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            return true;
        }

        static bool Water(Material m, Shader lit)
        {
            var col = Col(m, "_Color", new Color(0.2f, 0.25f, 0.25f, 0.8f));
            var main = Tex(m, "_MainTex");
            Vector2 tiling = m.HasProperty("_BumpMap") ? m.GetTextureScale("_BumpMap") : Vector2.one;
            var bump = Tex(m, "_BumpMap");
            float smooth = Flt(m, "_Smth", 0.9f);
            Reset(m, lit);
            m.SetTexture("_BaseMap", main);
            m.SetColor("_BaseColor", new Color(col.r, col.g, col.b, Mathf.Clamp(col.a, 0.75f, 0.92f)));
            m.SetFloat("_Smoothness", Mathf.Max(smooth, 0.9f));
            m.SetFloat("_Metallic", 0f);
            Normal(m, bump, 0.6f);
            m.SetTextureScale("_BaseMap", tiling);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("DepthOnly", false);
            return true;
        }

        /// <summary>Base layer only (URP/Lit has no world-up moss blend): albedo, normal, metal/smooth map, AO.</summary>
        static bool TopBlend(Material m, Shader lit)
        {
            var main = Tex(m, "_MainTex");
            Vector2 sc = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 of = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            var spc = Tex(m, "_Spc");
            var bump = Tex(m, "_BumpMap");
            var ao = Tex(m, "_AO");
            var detail = Tex(m, "_DetailBump");
            float detailTiling = Flt(m, "_DetailTiling", 2f), detailInt = Flt(m, "_DetailInt", 0.4f);
            Reset(m, lit);
            m.SetTexture("_BaseMap", main);
            m.SetTextureScale("_BaseMap", sc);
            m.SetTextureOffset("_BaseMap", of);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_WorkflowMode", 1f);
            Normal(m, bump);
            if (spc) { m.SetTexture("_MetallicGlossMap", spc); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", 1f); }
            else { m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.25f); }
            if (ao) { m.SetTexture("_OcclusionMap", ao); m.SetFloat("_OcclusionStrength", 1f); m.EnableKeyword("_OCCLUSIONMAP"); }
            if (detail)
            {
                m.SetTexture("_DetailNormalMap", detail);
                m.SetFloat("_DetailNormalMapScale", detailInt);
                m.SetTextureScale("_DetailAlbedoMap", Vector2.one * detailTiling);
                m.EnableKeyword("_DETAIL_MULX2");
            }
            Opaque(m, false);
            return true;
        }

        static bool Triplanar(Material m, Shader lit)
        {
            var main = Tex(m, "_MainTex");
            var bump = Tex(m, "_BumpMap1");
            float scale = Flt(m, "_TexScale", 1f);
            float cutoff = Flt(m, "_Cutoff", 0.5f);
            Reset(m, lit);
            m.SetTexture("_BaseMap", main);
            m.SetTextureScale("_BaseMap", Vector2.one * scale);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.2f);
            m.SetFloat("_Metallic", 0f);
            Normal(m, bump);
            // used by bushes: keep the alpha test
            bool cut = m.name.ToLowerInvariant().Contains("bush");
            Opaque(m, cut, cutoff, cut);
            return true;
        }

        static bool Tree(Material m, Shader lit, bool leaves)
        {
            var main = Tex(m, "_MainTex");
            var col = Col(m, "_Color", Color.white);
            var bump = Tex(m, "_BumpMap");
            float cutoff = Flt(m, "_Cutoff", 0.3f);
            Reset(m, lit);
            m.SetTexture("_BaseMap", main);
            m.SetColor("_BaseColor", new Color(col.r, col.g, col.b, 1f));
            m.SetFloat("_Smoothness", leaves ? 0.1f : 0.05f);
            m.SetFloat("_Metallic", 0f);
            Normal(m, bump);
            Opaque(m, leaves, Mathf.Clamp(cutoff, 0.2f, 0.6f), leaves);
            return true;
        }
    }
}
