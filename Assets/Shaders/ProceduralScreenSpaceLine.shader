Shader "Custom/ProceduralScreenSpaceLine"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 0.8)
        _LinePixelWidth ("Line Pixel Width", Float) = 3.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _LinePixelWidth;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 previousOS : NORMAL;
                float4 nextOSAndSide : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                float4 centerCS = TransformObjectToHClip(input.positionOS);
                float4 previousCS = TransformObjectToHClip(input.previousOS);
                float4 nextCS = TransformObjectToHClip(input.nextOSAndSide.xyz);

                float2 previousNdc = previousCS.xy / max(abs(previousCS.w), 1e-5);
                float2 nextNdc = nextCS.xy / max(abs(nextCS.w), 1e-5);

                float2 direction = nextNdc - previousNdc;
                if (dot(direction, direction) < 1e-8)
                    direction = float2(1, 0);
                direction = normalize(direction);

                float2 perpendicular = float2(-direction.y, direction.x);
                float2 pixelToNdc = 2.0 / _ScreenParams.xy;
                float side = input.nextOSAndSide.w;
                centerCS.xy += perpendicular * side * (_LinePixelWidth * 0.5) * pixelToNdc * centerCS.w;

                output.positionCS = centerCS;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }
}
