Shader "Custom/MoonUnlit"
{
    Properties
    {
        _Color ("Color", Color) = (0.95, 0.94, 0.82, 1)
        _BaseColor ("Base Color", Color) = (0.95, 0.94, 0.82, 1)
        _GlowColor ("Glow Color", Color) = (0.85, 0.88, 1, 1)
        _GlowIntensity ("Glow Intensity", Range(0, 5)) = 1.5
        _Phase ("Phase", Range(-1, 1)) = 0
        _Visibility ("Visibility", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+501" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            Name "MoonUnlit"
            Tags { "LightMode"="UniversalForward" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _BaseColor;
                half4 _GlowColor;
                half _GlowIntensity;
                half _Phase;
                half _Visibility;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                half3 viewDir = normalize(input.viewDirWS);

                // Simple fresnel rim for a soft moon glow
                half rim = 1.0 - saturate(dot(viewDir, normal));
                half rimGlow = pow(rim, 2.0) * _GlowIntensity;

                // Phase-based shading (simulate crescent)
                half phaseMask = saturate(dot(normal, float3(_Phase, 0, sqrt(1 - _Phase * _Phase))));

                half3 baseCol = _BaseColor.rgb * phaseMask;
                half3 glow = _GlowColor.rgb * rimGlow * 0.5;
                half3 finalColor = (baseCol + glow) * _Visibility;

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }

        // Depth-only pass for shadow/depth
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 frag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    // Fallback for non-URP
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+501" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 normalW : TEXCOORD0; float3 viewW : TEXCOORD1; };

            fixed4 _Color;
            fixed4 _BaseColor;
            fixed4 _GlowColor;
            half _GlowIntensity;
            half _Phase;
            half _Visibility;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normalW = UnityObjectToWorldNormal(v.normal);
                o.viewW = normalize(WorldSpaceViewDir(v.vertex));
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normalW);
                float3 v = normalize(i.viewW);
                half rim = 1.0 - saturate(dot(v, n));
                half rimGlow = pow(rim, 2.0) * _GlowIntensity;
                half phaseMask = saturate(dot(n, float3(_Phase, 0, sqrt(1 - _Phase * _Phase))));
                half3 baseCol = _BaseColor.rgb * phaseMask;
                half3 glow = _GlowColor.rgb * rimGlow * 0.5;
                half3 finalColor = (baseCol + glow) * _Visibility;
                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}