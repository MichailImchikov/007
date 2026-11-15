// Subtle cel look over the opaque scene (URP Full Screen Pass, before transparents: glass, the optics' lenses, particles
// and comic effects are drawn after it and stay clean):
//  - the light is softly posterized into a few bands (on a compressed luminance, so HDR highlights keep their shape),
//    blended in only partly and never moving a pixel far from its own brightness: shading reads as cel, dark corners
//    are not crushed to black;
//  - thin ink lines on silhouettes (a jump in depth: the second difference of 1/z, zero on any plane however grazing,
//    so far floors and walls do not ink themselves) and creases (normals), fading out by _OutlineFar;
//  - a touch more saturation, more with the local player's kill streak (_FPS_ComicBoost, 0..1, set by ComicFX);
//  - lighter shadows first (_ShadowLift): the maps' shadows were near black;
//  - characters stay readable (_FPS_CharMask from ComicAvatarFeature: the bodies' eye depth, matched against the scene
//    depth): no crease lines over their gear (at a distance the dense lines turned soldiers into black shapes), a
//    lighter silhouette that fades out by 35 m, and their dark tones lifted a little more (_CharLift).
Shader "FPS/ToonPost"
{
    Properties
    {
        _CelStrength ("Cel strength", Range(0, 1)) = 0.35
        _Bands ("Bands", Range(2, 8)) = 4
        _OutlineStrength ("Outline strength", Range(0, 1)) = 0.55
        _OutlineWidth ("Outline width (px)", Range(0.5, 3)) = 1
        _DepthThreshold ("Depth threshold (second difference of 1/z)", Range(0.005, 0.5)) = 0.06
        _NormalThreshold ("Normal threshold", Range(0.05, 1)) = 0.35
        _OutlineFar ("Outline fade distance (m)", Float) = 40
        _ShadowLift ("Shadow lift (dark tones brighter, black stays black)", Range(0, 0.6)) = 0.27
        _Saturation ("Saturation", Range(0.5, 1.5)) = 1.08
        _StreakSaturation ("Extra saturation at a full streak", Range(0, 1.5)) = 0.55
        _InkColor ("Ink color", Color) = (0.05, 0.04, 0.06, 1)
        _CharLift ("Extra shadow lift on characters", Range(0, 0.5)) = 0.2
        _CharInk ("Silhouette ink on characters", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "ToonPost"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float _CelStrength, _Bands, _OutlineStrength, _OutlineWidth, _DepthThreshold, _NormalThreshold, _OutlineFar, _Saturation, _StreakSaturation, _ShadowLift;
            float4 _InkColor;
            float _FPS_ComicBoost;
            float _CharLift, _CharInk;
            TEXTURE2D(_FPS_CharMask);
            float _FPS_CharMaskOn;

            float EyeDepth(float2 uv) { return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }

            // 1 where the visible surface at uv (scene eye depth d) is a character's body
            float CharAt(float2 uv, float d)
            {
                if (_FPS_CharMaskOn < 0.5) return 0.0;
                float cd = SAMPLE_TEXTURE2D_LOD(_FPS_CharMask, sampler_PointClamp, uv, 0).r;
                return cd > 0.01 && abs(cd - d) < 0.05 + d * 0.01 ? 1.0 : 0.0;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                float d0 = EyeDepth(uv);
                float isChar = CharAt(uv, d0);

                // ---- lighter shadows (comic shadows are flat and readable, not black): the dark tones are lifted in
                // a gamma-like space, strongest in the low midtones; the hue stays, pure black stays black, and
                // the light half of the picture is untouched. Characters get a little more (dark uniforms and gear).
                float lum0 = dot(col, float3(0.2126, 0.7152, 0.0722));
                float g = pow(saturate(lum0), 1.0 / 2.2);
                float lift = _ShadowLift + isChar * _CharLift;
                float g2 = g + lift * pow(max(g, 1e-6), 0.3) * (1.0 - g) * (1.0 - g);
                float liftRatio = lum0 > 1e-5 && lum0 < 1.0 ? min(pow(max(g2, 1e-6), 2.2) / lum0, 4.0) : 1.0;
                col *= liftRatio;

                // ---- cel bands on the light: a pixel leans toward its band, never far from its own brightness
                float lum = dot(col, float3(0.2126, 0.7152, 0.0722));
                float lc = lum / (1.0 + lum);                              // compressed: HDR fits in 0..1
                float n = _Bands;
                float band = (floor(lc * n) + smoothstep(0.38, 0.62, frac(lc * n))) / n;
                float lq = band / max(1.0 - band, 1e-3);                    // back to linear
                float ratio = clamp(lq / max(lum, 1e-4), 0.85, 1.25);       // the lowest band no longer pulls dim light to black
                col = lerp(col, col * ratio, _CelStrength);

                // ---- saturation, more with the local player's streak
                float sat = _Saturation + saturate(_FPS_ComicBoost) * _StreakSaturation;
                float l2 = dot(col, float3(0.2126, 0.7152, 0.0722));
                col = max(0, lerp(l2.xxx, col, sat));

                // ---- ink lines: silhouettes and creases, near the eye only
                if (d0 < _OutlineFar)
                {
                    float2 px = _ScreenSize.zw * _OutlineWidth;
                    float dl = EyeDepth(uv - float2(px.x, 0)), dr = EyeDepth(uv + float2(px.x, 0));
                    float dd = EyeDepth(uv - float2(0, px.y)), du = EyeDepth(uv + float2(0, px.y));
                    // 1/z is linear across any plane: its second difference is ~0 on floors and walls at any angle and
                    // large where the neighbour lies on another surface far behind or in front
                    float dEdge = abs(d0 / max(dl, 1e-3) + d0 / max(dr, 1e-3) - 2.0) + abs(d0 / max(dd, 1e-3) + d0 / max(du, 1e-3) - 2.0);
                    float depthLine = smoothstep(_DepthThreshold, _DepthThreshold * 2.5, dEdge);
                    float3 nl = SampleSceneNormals(uv - float2(px.x, 0)), nr = SampleSceneNormals(uv + float2(px.x, 0));
                    float3 nd = SampleSceneNormals(uv - float2(0, px.y)), nu = SampleSceneNormals(uv + float2(0, px.y));
                    float nEdge = (1.0 - dot(nl, nr)) + (1.0 - dot(nd, nu));
                    float nT = _NormalThreshold * (1.0 + d0 / 12.0);         // fine normal detail far away is noise
                    float normalLine = smoothstep(nT, nT * 2.0, nEdge);
                    // characters: no crease lines on or right next to them, a lighter silhouette fading out by 35 m
                    float charNear = max(isChar, max(max(CharAt(uv - float2(px.x, 0), dl), CharAt(uv + float2(px.x, 0), dr)),
                                                     max(CharAt(uv - float2(0, px.y), dd), CharAt(uv + float2(0, px.y), du))));
                    normalLine *= 1.0 - charNear;
                    depthLine *= lerp(1.0, _CharInk * (1.0 - smoothstep(12.0, 35.0, d0)), charNear);
                    float fade = 1.0 - smoothstep(_OutlineFar * 0.3, _OutlineFar, d0);
                    float ink = saturate(max(depthLine, normalLine)) * fade * _OutlineStrength;
                    col = lerp(col, _InkColor.rgb, ink);
                }
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
