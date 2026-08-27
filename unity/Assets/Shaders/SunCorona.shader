// 太陽のコロナ (Step 9-2)。**ビルボードの Quad 1 枚。**
//
// 向きは CelestialBodyView が root に入れる LookRotation(dir) が担うので、
// このシェーダはビルボード処理をしない。
// **ただし Cull Off にする。** 光点を Quad で作ったとき「描画されなかった」
// 事例があり (SceneBuilder のコメント)、片面ポリゴンの裏表に依存させない。
//
// **アルファ契約が他のシェーダと違う。**
//   - 地表 / 雲 / 太陽本体: _BaseColor.a を出力アルファに掛ける (クロスフェード)
//   - コロナ: **Additive (Blend One One) なので出力アルファは効かない。**
//     減衰は RGB に入っている。_BaseColor.a は読まない
//
// **フェードの仕組みは持たない。** 太陽は引き渡し対象ではないので
// RealScaleBlend が常に 0、つまり「光点 α + 殻 α」は構造的に常に 1.0 で、
// 掛けても何も起きない。何もしない掛け算を残さない (Step 6 の
// EmissionIntensity = 4.0 のデッドコードと同じ轍を踏まないため)。
// **コロナは太陽が描かれている限り常に出る。**
Shader "SolarSystem/SunCorona"
{
    Properties
    {
        // **テクスチャは (1 - r) だけを持つ。** 減衰の指数はここで掛ける。
        // 焼き込むと実機で振れない (Step 9-4)。
        _BaseMap("Corona Distance (1 - r)", 2D) = "black" {}
        _CoronaColor("Corona Color", Color) = (1.0, 0.82, 0.55, 1.0)
        _Falloff("Falloff (減衰の指数)", Float) = 3.0
        _EmissionIntensity("Emission Intensity (HDR 強度)", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+2"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _CoronaColor;
                float _Falloff;
                float _EmissionIntensity;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = GetVertexPositionInputs(input.positionOS.xyz).positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                // テクスチャは中心 1 / 縁 0 の (1 - r)。
                half distance01 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).r;

                // pow の底を 0 にすると NaN が出るのでガードする (Step 8-2 で踏んだ)。
                half falloff = pow(max(distance01, 1e-4h), max(_Falloff, 1e-2h));

                // 縁で厳密に 0 にする。1e-4 のガードが残ると四角い輪郭が出る。
                falloff *= step(1e-4h, distance01);

                half3 corona = _CoronaColor.rgb * falloff * _EmissionIntensity;

                // **アルファは 0 を返す。** Additive では出力アルファが効かないので、
                // ここに減衰を入れると「効いているように見えて効かない」値になる。
                return half4(corona, 0.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
