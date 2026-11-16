// The eyepiece of an optic (its rear lens). Everything is worked out from the direction the eye looks through each
// point of the glass, relative to the optic's axis (the barrel's sight line):
//  - 1x sights (red dot, holographic): clear glass with a collimated reticle at infinity. It sits where the axis is,
//    seen from wherever the eye is, so it marks the barrel and has no parallax; it shows only through the glass.
//  - magnified optics: the eyepiece shows a picture-in-picture camera looking along the axis, magnified, with the
//    reticle in it (second focal plane: same size at any distance), a field stop and the eyebox shadow when the eye
//    is off the axis.
// Sizes are angles; the reticle never gets thinner than about a pixel and a half (fwidth), so it does not shimmer.
Shader "FPS/OpticLens"
{
    Properties
    {
        _Tint ("Glass tint", Color) = (0.55, 0.72, 0.78, 1)
        _TintAlpha ("Glass opacity", Range(0, 1)) = 0.05
        [HDR] _ReticleColor ("Reticle color", Color) = (4, 0.15, 0.1, 1)
        _Style ("Reticle: 0 dot, 1 holo, 2 chevron, 3 duplex", Float) = 0
        _DotMoa ("Dot size (MOA)", Float) = 2
        _Mag ("Magnification", Float) = 1
        _PipTex ("Picture in picture", 2D) = "black" {}
        _PipOn ("Picture in picture rendered", Float) = 0
        _PipTanHalf ("Picture in picture tan(half fov)", Float) = 0.05
        _ApparentTan ("Eyepiece apparent radius (tan)", Float) = 0.15
        _Aim ("Aim", Float) = 0
        _LensCenter ("Lens center (world)", Vector) = (0, 0, 0, 0)
        _LensRadius ("Lens radius (m)", Float) = 0.01
        _Axis ("Axis (world)", Vector) = (0, 0, 1, 0)
        _AxisUp ("Axis up (world)", Vector) = (0, 1, 0, 0)
        _AxisRight ("Axis right (world)", Vector) = (1, 0, 0, 0)
        _EyeboxGain ("Eyebox shadow per lens radius of eye offset", Float) = 1
        // magnified eyepieces mark their pixels (stencil bit 8): the main view's recon x-ray is not drawn over the scope
        // picture (the eyepiece camera draws its own, magnified)
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass ("Stencil pass op", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "OpticLens"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Stencil
            {
                Ref 8
                WriteMask 8
                Comp Always
                Pass [_StencilPass]
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PipTex);
            SAMPLER(sampler_PipTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _TintAlpha;
                float4 _ReticleColor;
                float _Style;
                float _DotMoa;
                float _Mag;
                float4 _PipTex_ST;
                float _PipOn;
                float _PipTanHalf;
                float _ApparentTan;
                float _Aim;
                float4 _LensCenter;
                float _LensRadius;
                float4 _Axis;
                float4 _AxisUp;
                float4 _AxisRight;
                float _EyeboxGain;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // 0..1 coverage of a line of half width w at distance d, antialiased over aa
            float Line(float d, float w, float aa) { return 1.0 - smoothstep(w - aa, w + aa, d); }

            float Segment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / max(dot(ba, ba), 1e-8));
                return length(pa - ba * h);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 eye = GetCameraPositionWS();
                float3 d = normalize(i.positionWS - eye);
                float fz = max(dot(d, _Axis.xyz), 1e-3);
                // tangents of the view direction off the axis (right, up)
                float2 t = float2(dot(d, _AxisRight.xyz), dot(d, _AxisUp.xyz)) / fz;
                // where on the glass this is, in lens radii
                float3 lp = i.positionWS - _LensCenter.xyz;
                float2 q = float2(dot(lp, _AxisRight.xyz), dot(lp, _AxisUp.xyz)) / max(_LensRadius, 1e-4);
                float r = length(q);

                if (_Mag < 1.01)
                {
                    // ---- clear glass, a little darker at the rim
                    float rim = smoothstep(0.7, 1.0, r);
                    float a = _TintAlpha + rim * 0.18;
                    float3 col = _Tint.rgb * a;
                    // ---- collimated reticle, in minutes of angle around the axis
                    float moa = length(t) * 3437.75;
                    float px = max(fwidth(moa), 1e-4);          // one pixel in MOA here
                    float ret = 0;
                    float dotR = max(_DotMoa * 0.5, px * 1.6);
                    float core = Line(moa, dotR, px * 0.75);
                    float glow = exp(-moa / (dotR * 1.6)) * 0.1;
                    if (_Style < 0.5)
                    {
                        ret = core + glow;
                    }
                    else
                    {
                        // holographic: 65 MOA ring and a 1 MOA dot, the hologram's soft speckle halo
                        float ringW = max(0.75, px * 0.8);
                        float ring = Line(abs(moa - 32.5), ringW, px * 0.75);
                        float ringGlow = exp(-abs(moa - 32.5) / (ringW * 2.0)) * 0.08;
                        ret = max(ring, core) + ringGlow + glow;
                    }
                    col += _ReticleColor.rgb * ret;
                    return half4(col, a);
                }

                // ---- magnified: the scene along the axis through the eyepiece
                float2 tm = t / _Mag;                                    // the true angle shown here
                float2 uv = 0.5 + 0.5 * tm / max(_PipTanHalf, 1e-5);
                float ca = 0.006 * r * r;                                 // a touch of lateral color at the rim
                float3 scene;
                scene.r = SAMPLE_TEXTURE2D(_PipTex, sampler_PipTex, 0.5 + (uv - 0.5) * (1.0 + ca)).r;
                scene.g = SAMPLE_TEXTURE2D(_PipTex, sampler_PipTex, uv).g;
                scene.b = SAMPLE_TEXTURE2D(_PipTex, sampler_PipTex, 0.5 + (uv - 0.5) * (1.0 - ca)).b;
                scene *= float3(0.97, 1.0, 0.985);

                // eyebox: the eye off the axis sees the exit pupil slide away, a shadow sweeps in from the far side
                float3 eo = eye - _LensCenter.xyz;
                float2 eoff = float2(dot(eo, _AxisRight.xyz), dot(eo, _AxisUp.xyz)) / max(_LensRadius, 1e-4);
                // (a centred eye sees the whole field to the field stop; the shadow only comes in as the eye slides off.
                // Wide field: the picture reaches almost to the rim of the glass)
                float clear = 1.0 - smoothstep(0.9, 1.12, length(q + eoff * _EyeboxGain));
                float stop = 1.0 - smoothstep(0.95, 0.99, r);            // the field stop's sharp black edge
                float vis = clear * stop * saturate(_Aim * 1.5) * _PipOn;
                float3 col = scene * vis;

                // ---- reticle in the eyepiece's apparent field (-1..1 across it)
                float2 p = t / max(_ApparentTan, 1e-4);
                float aa = max(fwidth(p.x), 1e-4) * 0.8;
                float black = 0, lit = 0;
                if (_Style < 2.5)
                {
                    // chevron (combat prism): an illuminated ^ at the aim point, a bullet-drop post below with
                    // stadia ticks, fine horizontal hairlines out to the sides
                    float wv = max(0.006, aa * 1.6);
                    float chev = min(Segment(p, float2(0, 0), float2(-0.055, -0.07)), Segment(p, float2(0, 0), float2(0.055, -0.07)));
                    lit = Line(chev, wv, aa);
                    black = max(black, Line(chev, wv * 2.4, aa) * 0.85);   // dark rim: readable on any target
                    float post = abs(p.x);
                    black = max(black, Line(post, max(0.0035, aa * 1.2), aa) * step(p.y, -0.13) * step(-0.62, p.y));
                    for (int k = 1; k <= 4; k++)
                    {
                        float y = -0.13 - 0.11 * k;
                        float tickHalf = 0.07 - 0.012 * k;
                        black = max(black, Line(abs(p.y - y), max(0.003, aa * 1.1), aa) * step(abs(p.x), tickHalf));
                    }
                    black = max(black, Line(abs(p.y), max(0.0028, aa * 1.0), aa) * step(0.2, abs(p.x)));
                }
                else
                {
                    // duplex: thick posts from the edge, a fine cross in the middle with mil dots, a lit center dot
                    float thin = max(0.0028, aa * 1.0), thick = max(0.016, aa * 1.6);
                    float2 ap = abs(p);
                    float fine = max(Line(ap.x, thin, aa) * step(ap.y, 0.42), Line(ap.y, thin, aa) * step(ap.x, 0.42));
                    float posts = max(Line(ap.x, thick, aa) * step(0.42, ap.y), Line(ap.y, thick, aa) * step(0.42, ap.x));
                    black = max(fine * step(0.02, length(p)), posts);
                    for (int m = 1; m <= 4; m++)
                    {
                        float o = 0.09 * m;
                        black = max(black, Line(length(float2(ap.x, ap.y - o)), max(0.007, aa * 1.2), aa) * step(ap.x, 0.02));
                        black = max(black, Line(length(float2(ap.x - o, ap.y)), max(0.007, aa * 1.2), aa) * step(ap.y, 0.02));
                    }
                    lit = Line(length(p), max(0.008, aa * 1.4), aa);
                }
                col *= 1.0 - black * vis;
                col += _ReticleColor.rgb * lit * vis * 1.3;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
