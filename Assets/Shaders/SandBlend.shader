Shader "Custom/SandBlend"
{
    Properties
    {
        _MainTex ("Desert Sand (RGB)", 2D) = "white" {}
        _Color ("Desert Sand Color", Color) = (0.82, 0.55, 0.25, 1)
        _SecondaryTex ("Apron Sand (RGB)", 2D) = "white" {}
        _SecondaryColor ("Apron Sand Color", Color) = (0.6, 0.45, 0.3, 1)
        _TreeZoneTexA ("Tree Zone Ground A (RGB)", 2D) = "white" {}
        _TreeZoneTexB ("Tree Zone Ground B (RGB)", 2D) = "white" {}
        _TreeZoneTexC ("Tree Zone Ground C (RGB)", 2D) = "white" {}
        _TreeZoneColor ("Tree Zone Tint", Color) = (1, 1, 1, 1)
        _Glossiness ("Smoothness", Range(0,1)) = 0.1
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _Tiling ("Main Tiling", Float) = 0.5
        _SecondaryTiling ("Apron Tiling", Float) = 0.3
        _TreeZoneTiling ("Tree Zone Tiling", Float) = 0.35
        _FarTextureFadeStart ("Far Texture Fade Start", Float) = 180
        _FarTextureFadeEnd ("Far Texture Fade End", Float) = 420
        _FarTextureFadeStrength ("Far Texture Fade Strength", Range(0,1)) = 0.45
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
        sampler2D _TreeZoneTexA;
        sampler2D _TreeZoneTexB;
        sampler2D _TreeZoneTexC;
        float4 _Color;
        float4 _SecondaryColor;
        float4 _TreeZoneColor;
        float _Glossiness;
        float _Metallic;
        float _Tiling;
        float _SecondaryTiling;
        float _TreeZoneTiling;
        float _FarTextureFadeStart;
        float _FarTextureFadeEnd;
        float _FarTextureFadeStrength;

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
            float2 treeUV = IN.worldPos.xz * _TreeZoneTiling;

            // Sample the base terrain textures.
            float4 desertTex = tex2D(_MainTex, mainUV) * _Color;
            float4 apronTex  = tex2D(_SecondaryTex, secUV) * _SecondaryColor;

            // Vertex color alpha drives the blend:
            //   alpha = 0  → pure desert sand (far from mountains)
            //   alpha = 1  → pure apron sand (close to mountain base)
            float blend = smoothstep(0.2, 0.8, IN.vertColor.a);

            // Smooth hermite interpolation for natural transition
            float4 finalColor = lerp(desertTex, apronTex, blend);

            // Vertex RGB stores tree-zone weights; alpha remains the apron blend.
            float3 treeWeights = saturate(IN.vertColor.rgb);
            float treeMask = saturate(treeWeights.r + treeWeights.g + treeWeights.b);
            if (treeMask > 0.001)
            {
                treeWeights /= max(0.001, treeMask);
                float4 treeTex =
                    tex2D(_TreeZoneTexA, treeUV) * treeWeights.r +
                    tex2D(_TreeZoneTexB, treeUV) * treeWeights.g +
                    tex2D(_TreeZoneTexC, treeUV) * treeWeights.b;
                finalColor = lerp(finalColor, treeTex * _TreeZoneColor, smoothstep(0.02, 0.95, treeMask));
            }

            float3 baseTint = lerp(_Color.rgb, _SecondaryColor.rgb, blend);
            baseTint = lerp(baseTint, _TreeZoneColor.rgb, treeMask);
            float farRange = max(0.001, _FarTextureFadeEnd - _FarTextureFadeStart);
            float farFade = saturate((distance(IN.worldPos, _WorldSpaceCameraPos) - _FarTextureFadeStart) / farRange) * _FarTextureFadeStrength;
            finalColor.rgb = lerp(finalColor.rgb, baseTint, farFade);

            o.Albedo = finalColor.rgb;
            o.Metallic = _Metallic;
            o.Smoothness = lerp(_Glossiness, _Glossiness * 0.5, blend); // Apron is rougher
            o.Alpha = 1.0;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
