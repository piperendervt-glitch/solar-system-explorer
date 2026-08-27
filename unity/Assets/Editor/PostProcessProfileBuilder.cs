using SolarSystem.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SolarSystem.Editor
{
    /// <summary>
    /// ポストプロセスのプロファイルをコードから作る (Step 6 / 9-4)。
    ///
    /// **値の出所は SolarSystem.Core.PlanetAppearance が唯一。**
    /// このアセット (Assets/Materials/PostProcess.asset) はそこから
    /// 生成されるものであって、**アセット側の値を正としない。**
    /// 実行時は PostProcessPreset.Awake が同じ定数から上書きするので、
    /// ここで書く値は「Editor でプロファイルを開いたときに実行時と同じに
    /// 見える」ためのもの。
    ///
    /// **Step 6 の事故:** ここが Bloom の値を書かず (既定 intensity 0)、
    /// SceneBuilder が呼ぶ Apply もアセットへ保存していなかったため、
    /// bloom が実行時に一度も効いていなかった。
    /// **Editor スクリプトで Volume の値を変えたら SetDirty / SaveAssets が要る。**
    /// </summary>
    public static class PostProcessProfileBuilder
    {
        const string Path = "Assets/Materials/PostProcess.asset";

        public static VolumeProfile GetOrCreate()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(Path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, Path);
            }

            // 控えめに 3 つだけ。強度は PostProcessPreset が上書きする。
            Bloom bloom = Ensure<Bloom>(profile);
            bloom.active = true;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = (float)SolarSystem.Core.PlanetAppearance.BloomIntensity;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = (float)SolarSystem.Core.PlanetAppearance.BloomThreshold;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = (float)SolarSystem.Core.PlanetAppearance.BloomScatter;

            Tonemapping tonemapping = Ensure<Tonemapping>(profile);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;

            Vignette vignette = Ensure<Vignette>(profile);
            vignette.intensity.overrideState = true;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.5f;

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// VolumeComponent を取り出す。無ければ足す。
        ///
        /// **AddObjectToAsset が要る。** これを忘れると VolumeComponent が
        /// .asset に保存されず、再読み込み後は TryGet が false を返す。
        /// (3 段階のスクショが同一になった原因がこれだった)
        /// </summary>
        static T Ensure<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T existing) && existing != null)
            {
                return existing;
            }

            T component = profile.Add<T>(overrides: true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }
    }

    /// <summary>
    /// 太陽のレンズフレアをコードから作る (Step 6 / 9-3b)。**外部素材は使わない。**
    ///
    /// 要素の並びは固定で、実行時に索引で引く (FlareRuntimeData)。
    /// **順番を変えるときは PlanetAppearance の索引も直すこと。**
    ///   0            : 中心のグレア
    ///   1 .. 6       : 光条 (最大 6 要素 = 画面上 12 本)
    ///   7            : 水平方向の縞
    ///   8            : ゴースト (5 個をまとめた 1 要素)
    /// </summary>
    public static class LensFlareBuilder
    {
        const string Path = "Assets/Materials/SunFlare.asset";

        public const int GlareIndex = 0;
        public const int SpikeFirstIndex = 1;
        public const int StripeIndex = SpikeFirstIndex + SolarSystem.Core.PlanetAppearance.FlareSpikeElementMax;
        public const int GhostIndex = StripeIndex + 1;
        public const int ElementCount = GhostIndex + 1;

        /// <summary>ゴーストの個数。計画書 9-3 の「4〜6 個」。</summary>
        public const int GhostCount = 5;

        public static LensFlareDataSRP GetOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(Path);
            LensFlareDataSRP data = existing != null
                ? existing
                : ScriptableObject.CreateInstance<LensFlareDataSRP>();

            // **毎回組み直す。** 早期 return にすると Step 6 の円 3 枚が残る。
            Texture streak = FlareTextureBuilder.GetOrCreate();
            var elements = new LensFlareDataElementSRP[ElementCount];

            elements[GlareIndex] = Circle(0.35f, 0f, new Color(1.0f, 0.95f, 0.85f, 1f), 1f);

            // ---- 光条 ----
            // 筋は中心を通る両側の帯なので、要素 1 個で 2 本に見える。
            // N 要素を 180/N 度おきに置くと、画面上には 2N 本が等間隔に並ぶ。
            int max = SolarSystem.Core.PlanetAppearance.FlareSpikeElementMax;

            // **既定の本数に合わせて visible を決める。** 全部 visible で作ると、
            // パネルを開くまで既定 6 本ではなく最大 12 本で描かれてしまう。
            int active = FlareRuntimeData.ElementsForSpikeCount(
                SolarSystem.Core.PlanetAppearance.FlareSpikeCount);

            for (int i = 0; i < max; i++)
            {
                LensFlareDataElementSRP spike = Streak(
                    streak,
                    length: (float)SolarSystem.Core.PlanetAppearance.FlareSpikeLength,
                    thickness: (float)SolarSystem.Core.PlanetAppearance.FlareSpikeThickness,
                    // **有効な本数で角度を割る。** max で割ると、本数を減らしたとき
                    // 片側へ寄る。FlareRuntimeData.Apply と同じ式にすること。
                    rotation: active > 0 ? i * 180f / active : 0f,
                    tint: new Color(1.0f, 0.93f, 0.80f, 1f));

                spike.visible = i < active;
                elements[SpikeFirstIndex + i] = spike;
            }

            // ---- 水平方向の縞 ----
            // 同じ筋を 1 個、極端に長く細く。
            elements[StripeIndex] = Streak(
                streak, length: 4.0f, thickness: 0.03f, rotation: 0f,
                tint: new Color(0.75f, 0.88f, 1.0f, 1f));

            // ---- ゴースト (レンズ内反射) ----
            // 太陽から視線をずらすと反対側へ並ぶ。sun-offaxis で確認する。
            elements[GhostIndex] = Ghosts(new Color(0.85f, 0.9f, 1.0f, 1f));

            data.elements = elements;

            if (existing == null)
            {
                AssetDatabase.CreateAsset(data, Path);
            }

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return data;
        }

        static LensFlareDataElementSRP Circle(float size, float position, Color tint, float intensity)
        {
            return new LensFlareDataElementSRP
            {
                visible = true,
                flareType = SRPLensFlareType.Circle,
                localIntensity = intensity,
                sizeXY = new Vector2(1f, 1f),
                uniformScale = size,
                position = position,
                tint = tint,
                blendMode = SRPLensFlareBlendMode.Additive,
                modulateByLightColor = true,
                inverseSDF = false,
                fallOff = 1f,
                edgeOffset = 0.1f,
            };
        }

        /// <summary>筋 1 本。**preserveAspectRatio を切らないと sizeXY で伸ばせない。**</summary>
        static LensFlareDataElementSRP Streak(Texture texture, float length, float thickness,
                                              float rotation, Color tint)
        {
            return new LensFlareDataElementSRP
            {
                visible = true,
                flareType = SRPLensFlareType.Image,
                lensFlareTexture = texture,
                localIntensity = 1f,
                uniformScale = 1f,
                sizeXY = new Vector2(length, thickness),
                preserveAspectRatio = false,
                position = 0f,
                rotation = rotation,
                autoRotate = false,
                tint = tint,
                blendMode = SRPLensFlareBlendMode.Additive,
                modulateByLightColor = true,
            };
        }

        static LensFlareDataElementSRP Ghosts(Color tint)
        {
            return new LensFlareDataElementSRP
            {
                visible = true,
                flareType = SRPLensFlareType.Circle,
                localIntensity = (float)SolarSystem.Core.PlanetAppearance.FlareGhostIntensity,
                sizeXY = new Vector2(1f, 1f),
                uniformScale = 0.07f,

                // **内部で 2 倍される。** LensFlareCommonSRP:1465 が
                //   float position = 2.0f * element.position;
                // としたうえで screenPos * (1 - position) に描く。つまり
                //   0.0 = 太陽 / 0.5 = 画面中心 / 1.0 = 太陽の対称点
                //
                // 1.7 だと -2.4 倍になり画面外へ飛ぶ。sun-offaxis で
                // 非黒画素が 412 のまま増えなかったのはこれが理由。
                //
                // 0.5 〜 1.0 に散らして、画面中心から対称点までに並べる。
                position = 0.75f,
                tint = tint,
                blendMode = SRPLensFlareBlendMode.Additive,
                modulateByLightColor = true,
                inverseSDF = false,
                fallOff = 1f,
                edgeOffset = 0.1f,

                // 太陽の反対側へ 5 個を等間隔に並べる。
                allowMultipleElement = true,
                count = GhostCount,
                distribution = SRPLensFlareDistribution.Uniform,
                lengthSpread = 0.5f, // 0.5 〜 1.0 (画面中心 〜 対称点)
            };
        }
    }

    /// <summary>エンジン音のループを AudioClip アセットとして作る (Step 6)。</summary>
    public static class EngineAudioClipBuilder
    {
        const string Path = "Assets/Materials/EngineRumble.asset";

        public static AudioClip GetOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<AudioClip>(Path);
            if (existing != null)
            {
                return existing;
            }

            AudioClip clip = EngineAudio.CreateRumbleClip();
            AssetDatabase.CreateAsset(clip, Path);
            EditorUtility.SetDirty(clip);
            AssetDatabase.SaveAssets();
            return clip;
        }
    }
}
