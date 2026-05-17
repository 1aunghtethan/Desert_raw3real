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
        _MainTex ("Moon Texture", 2D) = "white" {}
        _BaseMap ("Moon Texture", 2D) = "white" {}
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
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _BaseColor;
                half4 _GlowColor;
                half _GlowIntensity;
                half _Phase;
                half _Visibility;
                float4 _MainTex_ST;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceNormalizeViewDir(vertexInput.positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normal = normalize(input.normalWS);
                half3 viewDir = normalize(input.viewDirWS);
                half4 moonTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // Simple fresnel rim for a soft moon glow
                half rim = 1.0 - saturate(dot(viewDir, normal));
                half rimGlow = pow(rim, 2.0) * _GlowIntensity;

                half3 baseCol = moonTex.rgb * _BaseColor.rgb;
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

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 normalW : TEXCOORD0; float3 viewW : TEXCOORD1; float2 uv : TEXCOORD2; };

            fixed4 _Color;
            fixed4 _BaseColor;
            fixed4 _GlowColor;
            half _GlowIntensity;
            half _Phase;
            half _Visibility;
            sampler2D _MainTex;
            float4 _MainTex_ST;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.normalW = UnityObjectToWorldNormal(v.normal);
                o.viewW = normalize(WorldSpaceViewDir(v.vertex));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 n = normalize(i.normalW);
                float3 v = normalize(i.viewW);
                fixed4 moonTex = tex2D(_MainTex, i.uv);
                half rim = 1.0 - saturate(dot(v, n));
                half rimGlow = pow(rim, 2.0) * _GlowIntensity;
                half3 baseCol = moonTex.rgb * _BaseColor.rgb;
                half3 glow = _GlowColor.rgb * rimGlow * 0.5;
                half3 finalColor = (baseCol + glow) * _Visibility;
                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}
