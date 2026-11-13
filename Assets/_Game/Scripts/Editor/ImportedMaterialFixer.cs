using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPS.EditorTools
{
    /// <summary>
    /// Imported Asset Store packs ship Built-in "Standard" / "Standard (Specular setup)" materials, which render
    /// magenta under URP. This converts them to URP/Lit, keeping textures, colors, smoothness, alpha mode and emission.
    /// </summary>
    public static class ImportedMaterialFixer
    {
        static readonly string[] Folders = { "Assets/genAssault", "Assets/Weapons_ChamferZone", "Assets/Police_ru" };

        [MenuItem("Tools/FPS/Fix Imported Materials (URP)")]
        public static void FixAll()
        {
            int fixedCount = 0, total = 0;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (!lit) { Debug.LogError("URP/Lit shader not found"); return; }
            var folders = new List<string>();
            foreach (var f in Folders) if (AssetDatabase.IsValidFolder(f)) folders.Add(f);
            if (folders.Count == 0) return;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (var guid in AssetDatabase.FindAssets("t:Material", folders.ToArray()))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!m) continue;
                    total++;
                    if (Convert(m, lit)) { fixedCount++; EditorUtility.SetDirty(m); }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            foreach (var guid in AssetDatabase.FindAssets("t:Material", folders.ToArray()))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m && FixGlowingReticle(m)) EditorUtility.SetDirty(m);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ImportedMaterialFixer] converted {fixedCount} of {total} materials to URP/Lit");
            RemapModelMaterials(folders.ToArray());
        }

        /// <summary>
        /// The weapon pack ships its textured materials as separate .mat files but the FBX files still use their
        /// embedded (untextured, ID-colored) materials. Point every embedded material at the .mat of the same name,
        /// searching the model's folder first, then the whole pack.
        /// </summary>
        static void RemapModelMaterials(string[] folders)
        {
            var byName = new Dictionary<string, List<string>>();
            foreach (var g in AssetDatabase.FindAssets("t:Material", folders))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (!p.EndsWith(".mat")) continue;
                string n = System.IO.Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (!byName.TryGetValue(n, out var l)) byName[n] = l = new List<string>();
                l.Add(p);
            }
            int remapped = 0;
            var missing = new List<string>();
            foreach (var g in AssetDatabase.FindAssets("t:Model", folders))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (!(AssetImporter.GetAtPath(path) is ModelImporter imp)) continue;
                string root = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
                var existing = imp.GetExternalObjectMap();
                bool changed = false;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(o is Material m)) continue;
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name);
                    if (existing.ContainsKey(id)) continue;
                    if (!byName.TryGetValue(m.name.ToLowerInvariant(), out var candidates))
                    {
                        // M16 scope: the FBX wants "WPNM_Optic_Scope_reticle", the pack ships it as "WPNT_Reticles.mat";
                        // the embedded fallback is an opaque quad with a white background over the whole eyepiece
                        candidates = null;
                        if (m.name.ToLowerInvariant().Contains("reticle"))
                            foreach (var kv in byName)
                                if (kv.Key.Contains("reticle") && kv.Value.Exists(c => c.StartsWith(root + "/")))
                                { candidates = kv.Value; break; }
                        if (candidates == null) { missing.Add(path + ":" + m.name); continue; }
                    }
                    string best = candidates.Find(c => c.StartsWith(root + "/")) ?? candidates[0];
                    imp.AddRemap(id, AssetDatabase.LoadAssetAtPath<Material>(best));
                    changed = true;
                    remapped++;
                }
                if (changed) imp.SaveAndReimport();
            }
            Debug.Log($"[ImportedMaterialFixer] remapped {remapped} embedded model materials" + (missing.Count > 0 ? "; no .mat for: " + string.Join(", ", missing) : ""));
        }

        /// <summary>
        /// Holo / red-dot reticles: a transparent quad whose glow comes from emission. In URP/Lit emission ignores
        /// alpha, so the whole quad glowed as a red square. Unlit alpha-blended, tinted by the old emission color.
        /// </summary>
        static bool FixGlowingReticle(Material m)
        {
            if (!m.name.ToLowerInvariant().Contains("reticle") || !m.shader || m.shader.name != "Universal Render Pipeline/Lit") return false;
            if (!m.IsKeywordEnabled("_EMISSION")) return false;
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (!unlit) return false;
            var tex = m.GetTexture("_BaseMap");
            Color glow = m.GetColor("_EmissionColor");
            if (glow.maxColorComponent < 0.05f) glow = Color.red;
            m.shader = unlit;
            m.shaderKeywords = new string[0];
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(glow.r * 1.5f, glow.g * 1.5f, glow.b * 1.5f, 1f));
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent + 1;
            m.SetOverrideTag("RenderType", "Transparent");
            return true;
        }

        static bool IsBuiltinStandard(Material m)
        {
            if (!m.shader) return true;
            string n = m.shader.name;
            return n == "Standard" || n == "Standard (Specular setup)" || n == "Hidden/InternalErrorShader"
                   || n.StartsWith("Legacy Shaders/") || n == "Autodesk Interactive";
        }

        static Texture Tex(Material m, string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
        static Color Col(Material m, string p, Color d) => m.HasProperty(p) ? m.GetColor(p) : d;
        static float Flt(Material m, string p, float d) => m.HasProperty(p) ? m.GetFloat(p) : d;

        public static bool Convert(Material m, Shader lit)
        {
            if (!IsBuiltinStandard(m)) return false;
            bool specular = m.shader && m.shader.name.Contains("Specular");

            // ---- read everything while the old shader is still assigned
            var mainTex = Tex(m, "_MainTex");
            Vector2 scale = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            var color = Col(m, "_Color", Color.white);
            var bump = Tex(m, "_BumpMap");
            float bumpScale = Flt(m, "_BumpScale", 1f);
            var metalMap = Tex(m, "_MetallicGlossMap");
            float metallic = Flt(m, "_Metallic", 0f);
            var specMap = Tex(m, "_SpecGlossMap");
            var specColor = Col(m, "_SpecColor", new Color(0.2f, 0.2f, 0.2f));
            float gloss = Flt(m, "_Glossiness", 0.5f);
            float glossScale = Flt(m, "_GlossMapScale", 1f);
            float smoothSrc = Flt(m, "_SmoothnessTextureChannel", 0f);
            var occ = Tex(m, "_OcclusionMap");
            float occStr = Flt(m, "_OcclusionStrength", 1f);
            var emisMap = Tex(m, "_EmissionMap");
            var emisCol = Col(m, "_EmissionColor", Color.black);
            bool emission = m.IsKeywordEnabled("_EMISSION") && (emisMap || emisCol.maxColorComponent > 0.01f);
            float mode = Flt(m, "_Mode", 0f);   // 0 opaque, 1 cutout, 2 fade, 3 transparent
            float cutoff = Flt(m, "_Cutoff", 0.5f);
            var detail = Tex(m, "_DetailAlbedoMap");

            m.shader = lit;
            m.shaderKeywords = new string[0];

            m.SetTexture("_BaseMap", mainTex);
            m.SetTextureScale("_BaseMap", scale);
            m.SetTextureOffset("_BaseMap", offset);
            m.SetColor("_BaseColor", color);

            if (bump) { m.SetTexture("_BumpMap", bump); m.SetFloat("_BumpScale", bumpScale); m.EnableKeyword("_NORMALMAP"); }

            bool hasGlossMap;
            if (specular)
            {
                m.SetFloat("_WorkflowMode", 0f);
                m.EnableKeyword("_SPECULAR_SETUP");
                m.SetColor("_SpecColor", specColor);
                m.SetTexture("_SpecGlossMap", specMap);
                hasGlossMap = specMap;
            }
            else
            {
                m.SetFloat("_WorkflowMode", 1f);
                m.SetFloat("_Metallic", metallic);
                m.SetTexture("_MetallicGlossMap", metalMap);
                hasGlossMap = metalMap;
            }
            if (hasGlossMap) m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_Smoothness", hasGlossMap ? glossScale : gloss);
            m.SetFloat("_SmoothnessTextureChannel", smoothSrc);
            if (smoothSrc > 0.5f) m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");

            if (occ) { m.SetTexture("_OcclusionMap", occ); m.SetFloat("_OcclusionStrength", occStr); m.EnableKeyword("_OCCLUSIONMAP"); }

            if (emission)
            {
                m.SetTexture("_EmissionMap", emisMap);
                m.SetColor("_EmissionColor", emisCol);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            else m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            if (detail) { m.SetTexture("_DetailAlbedoMap", detail); }

            // ---- surface type
            bool transparent = mode >= 1.5f;
            bool cutout = mode > 0.5f && mode < 1.5f;
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", cutoff);
            m.SetFloat("_Blend", 0f);
            if (cutout) m.EnableKeyword("_ALPHATEST_ON");
            if (transparent)
            {
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                bool premul = mode > 2.5f;
                if (premul) m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.SetFloat("_SrcBlend", (float)(premul ? BlendMode.One : BlendMode.SrcAlpha));
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.renderQueue = (int)RenderQueue.Transparent;
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetShaderPassEnabled("DepthOnly", false);
                m.SetShaderPassEnabled("ShadowCaster", false);
            }
            else
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.Zero);
                m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
                m.SetFloat("_DstBlendAlpha", (float)BlendMode.Zero);
                m.SetFloat("_ZWrite", 1f);
                m.renderQueue = cutout ? (int)RenderQueue.AlphaTest : -1;
                m.SetOverrideTag("RenderType", cutout ? "TransparentCutout" : "Opaque");
                m.SetShaderPassEnabled("DepthOnly", true);
                m.SetShaderPassEnabled("ShadowCaster", true);
            }
            return true;
        }
    }
}
