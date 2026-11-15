// The comic look of a character on a kill streak, drawn over the character's own skinned renderers by ComicAvatarFeature
// (no extra skinning), all scaled by _Level (0..1):
//   pass 0  ComicInk   thick ink outline: an inverted hull pushed out in screen space (constant pixel width)
//   pass 1  ComicTone  comic shading: a hard terminator from a viewer-relative key light, a lifted light side and a
//                      flat shade with halftone dots (2x multiply over the body as it was lit)
//   pass 2  ComicRim   a hard rim light in the aura color along the silhouette (additive)
//   pass 3  ComicAura  cel-shaded flames licking up off the silhouette: a larger hull, its back faces only, pushed
//                      back in depth so it never covers the character itself
Shader "FPS/ComicAvatar"
{
    Properties
    {
        _Level ("Level", Range(0, 1)) = 0
        _InkColor ("Ink", Color) = (0.02, 0.015, 0.03, 1)
        _InkPx ("Ink width (px at 1080p)", Float) = 3.4
        _ToneColor ("Halftone ink", Color) = (0.16, 0.1, 0.2, 1)
        [HDR] _AuraColor ("Aura", Color) = (2, 0.6, 0.1, 1)
        [HDR] _AuraCore ("Aura core", Color) = (3, 2.6, 1.2, 1)
        _AuraWidth ("Aura width (m)", Float) = 0.055
        _Base ("Character root (world)", Vector) = (0, 0, 0, 0)
        _Seed ("Noise seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Level;
            half4 _InkColor;
            float _InkPx;
            half4 _ToneColor;
            half4 _AuraColor;
            half4 _AuraCore;
            float _AuraWidth;
            float4 _Base;
            float _Seed;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };

        float Hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return frac((p.x + p.y) * p.z);
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = lerp(Hash13(i), Hash13(i + float3(1, 0, 0)), f.x);
            float b = lerp(Hash13(i + float3(0, 1, 0)), Hash13(i + float3(1, 1, 0)), f.x);
            float c = lerp(Hash13(i + float3(0, 0, 1)), Hash13(i + float3(1, 0, 1)), f.x);
            float d = lerp(Hash13(i + float3(0, 1, 1)), Hash13(i + float3(1, 1, 1)), f.x);
            return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
        }

        float Fbm(float3 p)
        {
            return ValueNoise(p) * 0.58 + ValueNoise(p * 2.13 + 7.31) * 0.29 + ValueNoise(p * 4.37 + 3.17) * 0.13;
        }

        struct VSurf { float4 pos : SV_POSITION; float3 posWS : TEXCOORD0; float3 nWS : TEXCOORD1; };

        VSurf VertSurface(Attributes v)
        {
            VSurf o;
            o.posWS = TransformObjectToWorld(v.positionOS.xyz);
            o.nWS = TransformObjectToWorldNormal(v.normalOS);
            o.pos = TransformObjectToHClip(v.positionOS.xyz);   // the same transform the body itself was drawn with
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ComicInk"
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            float4 vert(Attributes v) : SV_POSITION
            {
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = normalize(TransformObjectToWorldNormal(v.normalOS));
                float4 clip = TransformWorldToHClip(posWS);
                float2 dir = mul((float3x3)GetWorldToHClipMatrix(), nWS).xy * _ScreenParams.xy;
                float len = length(dir);
                dir = len > 1e-6 ? dir / len : float2(0, 0);
                // constant pixel width close up, thinning a little with distance so far figures stay readable
                float px = _InkPx * smoothstep(0.05, 0.7, _Level) * clamp(9.0 / max(clip.w, 0.1), 0.4, 1.0) * (_ScreenParams.y / 1080.0);
                clip.xy += dir * (px * 2.0 / _ScreenParams.xy) * clip.w;
                return clip;
            }

            half4 frag() : SV_Target { return half4(_InkColor.rgb, 1); }
            ENDHLSL
        }

        Pass
        {
            Name "ComicTone"
            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend DstColor SrcColor, Zero One   // 2x multiply: 0.5 leaves the body as it is, more lifts it, less darkens it

            HLSLPROGRAM
            #pragma vertex VertSurface
            #pragma fragment frag

            half4 frag(VSurf i) : SV_Target
            {
                float3 n = normalize(i.nWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.posWS);
                // a comic key light from the viewer's upper left, the same for whoever looks: a hard terminator splits
                // the form into a lifted light side and a flat shade with halftone dots
                float3 key = normalize(mul((float3x3)UNITY_MATRIX_I_V, float3(-0.5, 0.7, 0.5)));
                float ndl = dot(n, key);
                float ndv = saturate(dot(n, V));
                float aaL = max(fwidth(ndl), 1e-3);
                float lit = smoothstep(0.06 - aaL, 0.06 + aaL, ndl);
                lit *= 1.0 - smoothstep(0.32, 0.2, ndv);               // the turning edge falls into the shade
                float deep = saturate(-ndl * 1.3 + 0.3);
                // halftone dots on a 45° screen grid, bigger in the deep shade
                float scale = _ScreenParams.y / 1080.0;
                float2 sp = i.pos.xy / scale;
                const float cell = 6.0;
                float2 g = float2(sp.x + sp.y, sp.x - sp.y) * (0.70710678 / cell);
                float r = length(frac(g) - 0.5);
                float rad = lerp(0.28, 0.5, deep);
                float aa = max(fwidth(r), 1e-4);
                float dots = 1.0 - smoothstep(rad - aa, rad + aa, r);
                // fades out with distance: a far figure is a few pixels across, and dots on it only darkened it
                float k = smoothstep(0.08, 0.55, _Level) * (1.0 - smoothstep(10.0, 30.0, distance(i.posWS, _WorldSpaceCameraPos)));
                float shadeF = 0.85 * (1.0 - 0.5 * dots);
                float f = lerp(shadeF, 1.2, lit);
                f = lerp(1.0, f, k);
                return half4(f.xxx * 0.5, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ComicRim"
            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend One One, Zero One

            HLSLPROGRAM
            #pragma vertex VertSurface
            #pragma fragment frag

            half4 frag(VSurf i) : SV_Target
            {
                float3 n = normalize(i.nWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.posWS);
                float rim = 1.0 - saturate(dot(n, V));
                float f = Fbm(i.posWS * 7.0 + float3(0, -_Time.y * 2.6, 0) + _Seed);
                float x = rim + (f - 0.5) * 0.3;
                float aa = max(fwidth(x), 1e-3);
                float band = smoothstep(0.72 - aa, 0.72 + aa, x);
                float k = smoothstep(0.15, 0.65, _Level);
                return half4(_AuraColor.rgb * band * k * 0.45, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ComicAura"
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha, Zero One

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            struct VAura { float4 pos : SV_POSITION; float3 posWS : TEXCOORD0; float3 nWS : TEXCOORD1; float3 basePos : TEXCOORD2; };

            VAura vert(Attributes v)
            {
                VAura o;
                float3 p = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = normalize(TransformObjectToWorldNormal(v.normalOS));
                float t = _Time.y;
                float h = saturate((p.y - _Base.y) / 1.8);                   // 0 at the feet, 1 at the head
                float nz = ValueNoise(p * float3(5.0, 3.0, 5.0) + float3(0, -t * 2.6, 0) + _Seed);
                float up = saturate(n.y * 0.8 + 0.2);
                float w = _AuraWidth * smoothstep(0.1, 0.85, _Level) * lerp(0.45, 1.15, h);
                float3 q = p + n * w * (0.7 + 0.5 * nz);
                q.y += w * up * nz * nz * 1.5;                                // tongues licking up off the shoulders and head
                o.posWS = q;
                o.nWS = n;
                o.basePos = p;
                // pushed back along the view ray (same outline on screen, farther away): the shell shows against
                // the scene behind the character, never over the character's own nearer parts or its face
                float3 vs = TransformWorldToView(q);
                float d = max(-vs.z, 1e-3);
                vs *= (d + 0.3) / d;
                o.pos = TransformWViewToHClip(vs);
                return o;
            }

            half4 frag(VAura i) : SV_Target
            {
                float3 n = normalize(i.nWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.posWS);
                float edge = abs(dot(n, V));                                 // 0 at the shell's outline, more toward the body
                float f = Fbm(i.basePos * float3(8.0, 3.0, 8.0) + float3(0, -_Time.y * 3.4, 0) + _Seed);
                float heat = min(edge, 0.75) * 1.45 + (f - 0.5) * 1.25;
                float aa = max(fwidth(heat), 1e-3);
                float outer = smoothstep(0.45 - aa, 0.45 + aa, heat);       // hard-edged cel flames
                float inner = smoothstep(0.95 - aa, 0.95 + aa, heat);
                float k = smoothstep(0.1, 0.5, _Level);
                half3 col = lerp(_AuraColor.rgb, _AuraCore.rgb, inner);
                half a = outer * k * 0.85;
                return half4(col * a, a);
            }
            ENDHLSL
        }

        // 4: the character mask for FPS/ToonPost — the body's linear eye depth (the post compares it with the scene
        // depth to tell visible character pixels from the world). Drawn for every body, streak or not.
        Pass
        {
            Name "CharMask"
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex VertSurface
            #pragma fragment FragMask

            float4 FragMask(VSurf i) : SV_Target
            {
                return float4(-TransformWorldToView(i.posWS).z, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
