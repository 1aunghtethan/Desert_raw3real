Shader "Custom/SandBlend"
{
    Properties
    {
        _MainTex ("Desert Sand (RGB)", 2D) = "white" {}
        _Color ("Desert Sand Color", Color) = (0.82, 0.55, 0.25, 1)
        _SecondaryTex ("Apron Sand (RGB)", 2D) = "white" {}
        _SecondaryColor ("Apron Sand Color", Color) = (0.6, 0.45, 0.3, 1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.1
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Tiling ("Main Tiling", Float) = 0.5
        _SecondaryTiling ("Apron Tiling", Float) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _SecondaryTex;
        float4 _Color;
        float4 _SecondaryColor;
        float _Glossiness;
        float _Metallic;
        float _Tiling;
        float _SecondaryTiling;

        struct Input
        {
            float3 worldPos;
            float3 worldNormal;
            float4 vertColor;
        };

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vertColor = v.color;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // World-space UVs for seamless tiling across chunks
            float2 mainUV = IN.worldPos.xz * _Tiling;
            float2 secUV  = IN.worldPos.xz * _SecondaryTiling;

            // Sample both textures
            float4 desertTex = tex2D(_MainTex, mainUV) * _Color;
            float4 apronTex  = tex2D(_SecondaryTex, secUV) * _SecondaryColor;

            // Vertex color alpha drives the blend:
            //   alpha = 0  → pure desert sand (far from mountains)
            //   alpha = 1  → pure apron sand (close to mountain base)
            float blend = smoothstep(0.2, 0.8, IN.vertColor.a);

            // Smooth hermite interpolation for natural transition
            float4 finalColor = lerp(desertTex, apronTex, blend);

            o.Albedo = finalColor.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = lerp(_Glossiness, _Glossiness * 0.5, blend); // Apron is rougher
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
