// 雲層 (Step 8-3)。地球のみ。
//
// **輝度 (.r) をアルファとして読む。** 素材 (earth_clouds) はアルファを持たない
// グレースケール JPEG で、RGB は完全に同一と実測済み。RGBA PNG を作り直すと
// 4k で 8MB 前後になり 8-1 の 30MB 枠を圧迫するため、テクスチャは作り替えない。
//   素材の最大値 227 (= 0.89) / 平均 70.8 (= 0.28)
//   -> _CloudOpacity で持ち上げられるようにする
//
// **renderQueue は地表より +1。** 雲球と地表球は同心なので、Transparent の
// 既定ソート (バウンディング中心のカメラ距離) が同値になり順序が不定になる。
// 深度では解決しない (ZWrite off が前提)。
//
// **_BaseColor のアルファ契約は地表と同じ。** CelestialBodyView が雲球の
// MaterialPropertyBlock も駆動する。これを通さないと、引き渡し帯で地表だけ
// クロスフェードして雲が浮く。
Shader "SolarSystem/PlanetClouds"
{
    Properties
    {
        _BaseColor("Base Color (アルファのみ使う。MPB が毎フレーム上書き)", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Clouds (輝度をアルファとして読む)", 2D) = "black" {}
        _CloudOpacity("Cloud Opacity", Float) = 1.15
        _NightDarkness("夜側の暗さ (0 で真っ黒)", Range(0, 1)) = 0.02
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+1"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float _CloudOpacity;
                float _NightDarkness;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // 輝度をアルファとして読む。RGB は同一なので .r で足りる。
                half luminance = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).r;
                half alpha = saturate(luminance * _CloudOpacity) * _BaseColor.a;

                float3 N = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half nDotL = saturate(dot(N, normalize(mainLight.direction)));

                // 夜側で光らないように主光源だけの Lambert を掛ける。
                half3 color = mainLight.color * max(nDotL, _NightDarkness);

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
