// Thermal sight overlay for drone mode: characters glow white-hot where visible and (within range)
// shine through walls in a warmer tint. Drawn additively with Graphics.RenderMesh for the drone camera only.
Shader "Hidden/FPS/ThermalXRay"
{
    Properties
    {
        _Hot ("Visible core", Color) = (1.6, 1.6, 1.5, 1)
        _Edge ("Visible edge", Color) = (0.55, 0.55, 0.55, 1)
        _Hidden ("Through walls", Color) = (1.5, 1.5, 1.45, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+60" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Hot, _Edge, _Hidden;
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
        half Facing(Varyings i)
        {
            return saturate(abs(dot(normalize(i.normalWS), normalize(i.viewWS))));
        }
        ENDHLSL

        Pass
        {
            Name "ThermalVisible"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off ZTest LEqual Cull Back Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                half f = pow(Facing(i), 0.7);
                return half4(lerp(_Edge.rgb, _Hot.rgb, f), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ThermalHidden"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite Off ZTest Greater Cull Back Blend One One
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(Varyings i) : SV_Target
            {
                half f = Facing(i);
                return half4(_Hidden.rgb * (0.35 + 0.65 * f), 1);
            }
            ENDHLSL
        }
    }
}
