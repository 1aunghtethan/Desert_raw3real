Shader "Custom/MoonUnlit"
{
    Properties
    {
        _BaseColor ("Moon Color", Color) = (0.95, 0.94, 0.82, 1)
        _ShadowColor ("Moon Shadow Color", Color) = (0.12, 0.14, 0.18, 1)
        _GlowColor ("Rim Glow Color", Color) = (0.55, 0.67, 1, 1)
        _Phase ("Phase", Range(-1, 1)) = 0
        _Visibility ("Visibility", Range(0, 1)) = 1
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.5
        _SurfaceNoise ("Surface Noise", Range(0, 0.25)) = 0.08
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry+20"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _GlowColor;
                half _Phase;
                half _Visibility;
                half _RimPower;
                half _SurfaceNoise;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewDirWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
            };

            half Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionWS);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirWS = normalize(input.viewDirWS);

                half facing = saturate(dot(normalWS, viewDirWS));
                half rim = pow(saturate(1.0h - facing), _RimPower);

                half phaseOffset = _Phase * 0.55h;
                half litMask = smoothstep(-0.08h, 0.08h, input.positionOS.x + phaseOffset);
                half frontDisc = smoothstep(0.02h, 0.32h, facing);
                litMask *= frontDisc;

                half surfaceNoise = (Hash(input.positionOS.xy * 12.0h) - 0.5h) * _SurfaceNoise;
                half3 discColor = lerp(_ShadowColor.rgb, _BaseColor.rgb + surfaceNoise, litMask);
                half3 glow = _GlowColor.rgb * rim * 0.65h;
                half3 color = (discColor + glow) * max(_Visibility, 0.05h);

                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
