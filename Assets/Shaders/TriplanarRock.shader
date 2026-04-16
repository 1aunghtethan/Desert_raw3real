Shader "Custom/TriplanarRock"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _Tiling ("Tiling", Float) = 0.1
        _Blend ("Blending", Range(0, 10)) = 2.0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _NormalMap;
        float4 _Color;
        float _Tiling;
        float _Blend;
        float _Glossiness;
        float _Metallic;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // Normalize the world normal for blending
            float3 n = normalize(IN.worldNormal);
            float3 blending = abs(n);
            
            // Sharpen the blend
            blending = pow(blending, _Blend);
            blending /= (blending.x + blending.y + blending.z);

            float3 worldPos = IN.worldPos * _Tiling;

            // Triplanar sampling
            float4 xAlbedo = tex2D(_MainTex, worldPos.zy);
            float4 yAlbedo = tex2D(_MainTex, worldPos.xz);
            float4 zAlbedo = tex2D(_MainTex, worldPos.xy);

            // Blending albedo
            float4 finalAlbedo = (xAlbedo * blending.x + yAlbedo * blending.y + zAlbedo * blending.z) * _Color;

            o.Albedo = finalAlbedo.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = _Glossiness;
            o.Alpha = finalAlbedo.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
