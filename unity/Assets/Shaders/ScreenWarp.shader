// 画面の逆歪ませ (Step 11-3c)。
//
// **計器カメラの projectionMatrix は触らない。** ScreenSpace-Camera の Canvas は
// カメラの orthographic / fieldOfView からレイアウトを決めるので、行列だけ
// 差し替えると UI 側が破綻する。代わりに、真っ直ぐ描いた RT を
// **この blit で 1 回だけ歪ませる**（10 Hz。毎フレームではない）。
//
// _Warp は 3x3 のホモグラフィを 4x4 の左上に入れたもの (Core/ScreenWarpSolver)。
// 出力の (s,t) を写した先が、入力を読む位置。**外は黒**（元の面の絵を残さない）。
Shader "SolarSystem/ScreenWarp"
{
    Properties
    {
        _MainTex("Source", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZTest Always Cull Off ZWrite Off

        Pass
        {
            Name "Warp"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // **CBUFFER の外に置く。** 行列のプロパティは Properties ブロックに
            // 書けず、UnityPerMaterial に入れると SRP Batcher の宣言と食い違う。
            float4x4 _Warp;

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

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 p = mul((float3x3)_Warp, float3(input.uv, 1.0));
                if (abs(p.z) < 1e-6)
                {
                    return half4(0, 0, 0, 0);
                }

                float2 src = p.xy / p.z;
                if (src.x < 0.0 || src.x > 1.0 || src.y < 0.0 || src.y > 1.0)
                {
                    return half4(0, 0, 0, 0);
                }

                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, src);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
