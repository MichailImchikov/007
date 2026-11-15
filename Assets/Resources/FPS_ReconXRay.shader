// Sniper recon: enemies show through walls as a red silhouette with a bright rim (only the hidden parts are drawn:
// where the body is in plain sight the normal shading stays). Drawn with Graphics.RenderMesh for the player camera.
Shader "Hidden/FPS/ReconXRay"
{
    Properties
    {
        _Color ("Through walls", Color) = (1.0, 0.16, 0.12, 0.55)
        _Rim ("Rim", Color) = (1.6, 0.45, 0.3, 1)
        _Pulse ("Pulse 0..1", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+70" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ReconHidden"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off ZTest Greater Cull Back Blend SrcAlpha One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _Rim;
                half _Pulse;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 viewWS : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.viewWS = GetWorldSpaceViewDir(ws);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                half facing = saturate(abs(dot(normalize(i.normalWS), normalize(i.viewWS))));
                half rim = pow(1.0 - facing, 2.0);
                half3 c = _Color.rgb * (0.6 + 0.4 * _Pulse) + _Rim.rgb * rim;
                return half4(c, saturate(_Color.a + rim * 0.6));
            }
            ENDHLSL
        }
    }
}
