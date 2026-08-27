// 太陽の表面 (Step 9-1)。
//
// **Shader Graph ではなく手書き。** PlanetSurface と同じ理由で、.shader は
// テキストなので置くだけでよく、GUI を挟まず CLI で完結する。
//
// **URP/Unlit では書けない。** 理由は 2 つ:
//   - 強度を持たせる空きプロパティが無い。_BaseColor は MPB が毎フレーム
//     上書きするので、HDR 値を焼いても初回フレームで消える
//   - 周辺減光 (limb darkening) が書けない
//
// **守るべき契約 (既存コードが依存している):**
//   - プロパティ名は厳密に _BaseColor
//   - _BaseColor のアルファを出力アルファに掛ける (LOD 切替のクロスフェードが
//     これで動く)
//   - Transparent / ZWrite off を維持する
//   - **責務の分離: MPB = 描画状態 (アルファ) / マテリアル = 見た目 (強度・色)**
//     _BaseColor の RGB は CelestialBodyView が Body.Color (1.0, 0.95, 0.80) を
//     毎フレーム書く。太陽の色味なのでこれは掛けてよい。
//     **強度だけは _EmissionIntensity へ逃がす。**
//
// **光点と殻の両方に同じマテリアル設定を掛ける。** 航行範囲 (地球 8.70px 〜
// 火星 5.71px) で太陽は LOD 帯 (4〜8px) を横切るので、殻だけ HDR にすると
// 火星側で光点が優勢になり太陽が暗くなる。
Shader "SolarSystem/SunSurface"
{
    Properties
    {
        _BaseColor("Base Color (RGB = 色味 / A = クロスフェード。MPB が毎フレーム上書き)", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}

        _EmissionIntensity("Emission Intensity (HDR 強度)", Float) = 1.0

        // 0 = 減光なし / 1 = 縁が LimbFloor まで落ちる。
        _LimbDarkening("Limb Darkening (周辺減光)", Range(0, 1)) = 1.0
        _LimbFloor("Limb Floor (縁の明るさ)", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float _EmissionIntensity;
                float _LimbDarkening;
                float _LimbFloor;
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
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS);

                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = nrm.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;

                // ---- 周辺減光 ----
                // 視線と法線のなす角。中心は N.V = 1、縁は N.V -> 0。
                // **フレネルの逆。** 中心 1.0 -> 縁 _LimbFloor へ落とす。
                float3 N = normalize(input.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));
                half ndotv = saturate(dot(N, V));

                // sqrt で中心付近を平らに、縁だけ急に落とす。
                // pow の底を 0 にすると NaN が出るのでガードする (Step 8-2 で踏んだ)。
                half limb = lerp(1.0h, _LimbFloor, 1.0h - sqrt(max(ndotv, 1e-4h)));
                limb = lerp(1.0h, limb, saturate(_LimbDarkening));

                half3 color = albedo * _BaseColor.rgb * _EmissionIntensity * limb;
                return half4(color, _BaseColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
