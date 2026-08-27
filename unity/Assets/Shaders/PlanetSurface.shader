// 惑星の表面 (Step 8-2)。
//
// **Shader Graph ではなく手書き。** .shader はテキストなので SolarSetup.Run の前に
// 置くだけでよく、GUI を挟まず CLI で完結する。
//
// **単一 UniversalForward パス。** UniversalFragmentPBR は呼ばない。
//   - 惑星は Transparent / ZWrite off なので ShadowCaster / DepthOnly /
//     DepthNormals は元々意味を持たない
//   - 光源は Directional Light 1 灯のみ。追加ライト・シャドウのバリアントが要らない
//   - SRP Batcher は CelestialBodyView が MaterialPropertyBlock を使うため
//     元々効いていない
//
// **守るべき契約 (既存コードが依存している):**
//   - プロパティ名は厳密に _BaseColor
//   - _BaseColor のアルファを出力アルファに掛ける (LOD 切替と実スケール引き渡しの
//     クロスフェードがこれで動く)
//   - Transparent / ZWrite off を維持する
//   - **_BaseColor の RGB は MaterialPropertyBlock が毎フレーム上書きする。**
//     このシェーダは RGB を**使わない**。掛けるとアルベドが天体色で染まる
//     (地球なら (0.20, 0.42, 0.72) が乗って青く沈む)。
//     強度や色の作り込みを焼いてはいけない。強度は _EmissionIntensity に持たせる
//
// **テクスチャの既定値を明示すること。** 書かないと Unity は "white" に解決するので、
// 火星のように未接続の天体が全面鏡面かつ全面街灯りになる。
Shader "SolarSystem/PlanetSurface"
{
    Properties
    {
        _BaseColor("Base Color (アルファのみ使う。MPB が毎フレーム上書き)", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [Normal] _NormalMap("Normal Map", 2D) = "bump" {}
        _SpecularMask("Specular Mask (白 = 海)", 2D) = "black" {}
        _NightMap("Night Lights", 2D) = "black" {}

        _SmoothnessLand("Smoothness (陸)", Range(0, 1)) = 0.1
        _SmoothnessOcean("Smoothness (海)", Range(0, 1)) = 0.85
        _SpecularStrength("Specular Strength", Float) = 1.0

        _NightIntensity("Night Intensity", Float) = 1.0

        _AtmosphereColor("Atmosphere Color", Color) = (0.35, 0.55, 1.0, 1.0)
        _AtmosphereStrength("Atmosphere Strength", Float) = 1.0
        _AtmospherePower("Atmosphere Power", Float) = 3.5

        _EmissionIntensity("Emission Intensity (HDR 強度)", Float) = 1.0
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
                float4 _AtmosphereColor;
                float _SmoothnessLand;
                float _SmoothnessOcean;
                float _SpecularStrength;
                float _NightIntensity;
                float _AtmosphereStrength;
                float _AtmospherePower;
                float _EmissionIntensity;
            CBUFFER_END

            TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap);    SAMPLER(sampler_NormalMap);
            TEXTURE2D(_SpecularMask); SAMPLER(sampler_SpecularMask);
            TEXTURE2D(_NightMap);     SAMPLER(sampler_NightMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 tangentWS  : TEXCOORD3;
                float3 bitangentWS: TEXCOORD4;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = nrm.normalWS;
                output.tangentWS = nrm.tangentWS;
                output.bitangentWS = nrm.bitangentWS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // **_BaseColor の RGB は使わない。** MaterialPropertyBlock が毎フレーム
                // Body.Color (Step 2 の単色プレースホルダ。地球なら (0.20, 0.42, 0.72))
                // を書き込むので、掛けるとアルベドが青く沈む。
                // 契約はアルファだけ (クロスフェード)。色はアルベドマップが持つ。
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;

                // **法線は必ず normalize する。** 4k 縮小の箱型フィルタでベクトルが
                // 非正規化されるため (docs/02-demo2-plan.md 8-1 の決定)。
                half3 normalTS = normalize(UnpackNormal(
                    SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv)));

                float3x3 tangentToWorld = float3x3(
                    normalize(input.tangentWS),
                    normalize(input.bitangentWS),
                    normalize(input.normalWS));

                float3 N = normalize(mul(normalTS, tangentToWorld));
                float3 V = normalize(GetWorldSpaceViewDir(input.positionWS));

                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float NdotL = dot(N, L);

                // ---- 拡散 ----
                half3 color = albedo * saturate(NdotL) * mainLight.color;

                // ---- 海の鏡面 ----
                half ocean = SAMPLE_TEXTURE2D(_SpecularMask, sampler_SpecularMask, uv).r;
                half smoothness = lerp(_SmoothnessLand, _SmoothnessOcean, ocean);
                // **SafeNormalize を使う。** 夜側正面 (L と V が正反対) では L + V が
                // 零ベクトルになり、normalize(0) が NaN を返す。saturate(NdotL) が 0 でも
                // 0 * NaN = NaN なので救えず、円盤の中央に輝点として焼き付く。
                float3 H = SafeNormalize(L + V);
                half power = exp2(smoothness * 10.0h) + 1.0h;
                half spec = pow(saturate(dot(N, H)), power) * saturate(NdotL);
                color += mainLight.color * spec * ocean * _SpecularStrength;

                // ---- 夜側の街灯り ----
                // 明暗境界線をまたいで滑らかに立ち上がることが肝。
                half nightMask = smoothstep(0.1h, -0.15h, (half)NdotL);
                half3 night = SAMPLE_TEXTURE2D(_NightMap, sampler_NightMap, uv).rgb;
                color += night * _NightIntensity * nightMask;

                // ---- 大気の縁 (フレネル) ----
                // 昼側だけ光らせるため wrap を掛ける。
                // **pow の底を 0 にしない。** 円盤の中央では N・V = 1 なので底が
                // ちょうど 0 になり、pow(0, 3.5) が NaN を返す GPU がある。
                // 実測: 画面中央に 1044 px の灰色の塊として焼き付いた。
                half rim = max(1.0h - saturate(dot(N, V)), 1e-4h);
                half fresnel = pow(rim, _AtmospherePower);
                half wrap = saturate(NdotL * 0.5 + 0.5);
                color += _AtmosphereColor.rgb * fresnel * wrap * _AtmosphereStrength;

                color *= _EmissionIntensity;

                // **アルファは _BaseColor 由来。** クロスフェードがここに依存している。
                return half4(color, _BaseColor.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
