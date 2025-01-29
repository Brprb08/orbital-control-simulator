Shader "Custom/Atmosphere"
{
    Properties
    {
        _Color ("Atmosphere Color", Color) = (0.3, 0.5, 1, 1)
        _FresnelPower ("Fresnel Power", Range(0.1, 5)) = 2.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" }
        ZWrite Off // Disable depth writing for proper transparency
        Blend SrcAlpha OneMinusSrcAlpha // Ensure correct blending

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 normal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            float4 _Color;
            float _FresnelPower;

            v2f vert (appdata_t v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normal = normalize(UnityObjectToWorldNormal(v.normal));
                o.viewDir = normalize(WorldSpaceViewDir(v.vertex));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float fresnel = pow(dot(i.normal, i.viewDir), _FresnelPower);
                fresnel = saturate(fresnel * 1 - 0.3); // Minor tweak for better fade

                float alpha = fresnel * 0.4; // Maintain transparency strength
                return float4(_Color.rgb * fresnel, alpha);
            }
            ENDCG
        }
    }
}