using SolarSystem.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SolarSystem.Editor
{
    /// <summary>ポストプロセスのプロファイルをコードから作る (Step 6)。</summary>
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
            bloom.intensity.overrideState = true;
            bloom.threshold.overrideState = true;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.6f;

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

    /// <summary>太陽のレンズフレアをコードから作る (Step 6)。外部素材は使わない。</summary>
    public static class LensFlareBuilder
    {
        const string Path = "Assets/Materials/SunFlare.asset";

        public static LensFlareDataSRP GetOrCreate()
        {
            var existing = AssetDatabase.LoadAssetAtPath<LensFlareDataSRP>(Path);
            if (existing != null)
            {
                return existing;
            }

            var data = ScriptableObject.CreateInstance<LensFlareDataSRP>();

            // 手続き生成の円 3 枚だけ。テクスチャは使わない。
            data.elements = new[]
            {
                Circle(0.35f, 1.0f, new Color(1.0f, 0.95f, 0.85f, 1f)),
                Circle(0.10f, 0.35f, new Color(0.8f, 0.9f, 1.0f, 1f)),
                Circle(0.06f, -0.25f, new Color(1.0f, 0.8f, 0.6f, 1f)),
            };

            AssetDatabase.CreateAsset(data, Path);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return data;
        }

        static LensFlareDataElementSRP Circle(float size, float position, Color tint)
        {
            return new LensFlareDataElementSRP
            {
                visible = true,
                flareType = SRPLensFlareType.Circle,
                localIntensity = 1f,
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
