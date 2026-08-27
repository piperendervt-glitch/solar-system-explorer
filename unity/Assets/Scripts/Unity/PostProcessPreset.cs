using SolarSystem.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SolarSystem.Unity
{
    /// <summary>
    /// ポストプロセスの強度。Step 6 で 3 段階を比較し、
    /// **Step 7 で Medium に確定した**（人間が選択）。
    /// 残り 2 段階は比較用に残してある。
    /// </summary>
    public enum PostProcessStrength
    {
        Subtle,
        Medium,
        Strong,
    }

    /// <summary>
    /// ポストプロセスの設定 (Step 6)。
    ///
    /// 強度は Step 6 で 3 段階を並べ、**Step 7 で Medium に確定**した。
    ///
    /// Bloom (太陽と星) / Tonemapping / わずかな Vignette の 3 つだけ。
    /// Update() を持たない。切り替えは Apply を呼ぶ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PostProcessPreset : MonoBehaviour
    {
        [SerializeField] Volume _volume;
        [SerializeField] PostProcessStrength _strength = PostProcessStrength.Medium;

        public PostProcessStrength Strength => _strength;

        /// <summary>
        /// Volume は同じ GameObject に付いている。参照が外れていても自力で拾い直す。
        /// (シリアライズ経由の参照が null になり、Apply が無言で return して
        ///  3 段階のスクショが同一になる不具合があったため)
        /// </summary>
        public Volume Volume
        {
            get
            {
                if (_volume == null)
                {
                    _volume = GetComponent<Volume>();
                }

                return _volume;
            }
        }

        public void Bind(Volume volume) => _volume = volume;

        /// <summary>
        /// 各段階のパラメータ。ここだけ見れば強度の違いが分かる。
        ///
        /// **Medium は Core の定数から取る。ここに数値を二重定義しない。**
        /// Subtle / Strong は Medium からの比で出す。
        ///
        /// **値の出所は SolarSystem.Core.PlanetAppearance が唯一。**
        /// VolumeProfile アセット (Assets/Materials/PostProcess.asset) は
        /// そこから生成されるものであって、アセット側の値を正としない。
        /// この二重管理が Step 6 以来 bloom が消灯していた原因 (下記 Awake 参照)。
        ///
        /// **これらは暫定値。** Step 6 で 3 案を比較して Medium に決めたが、
        /// bloom が実行時に効いていない状態での比較だった。9-4 で決め直す。
        /// </summary>
        public static (float bloomIntensity, float bloomThreshold, float vignette) Values(
            PostProcessStrength strength)
        {
            var mediumIntensity = (float)PlanetAppearance.BloomIntensity;
            var mediumThreshold = (float)PlanetAppearance.BloomThreshold;
            const float mediumVignette = 0.22f;

            switch (strength)
            {
                case PostProcessStrength.Subtle:
                    return (mediumIntensity * 0.44f, mediumThreshold * 1.24f, mediumVignette * 0.55f);
                case PostProcessStrength.Strong:
                    return (mediumIntensity * 2.0f, mediumThreshold * 0.81f, mediumVignette * 1.55f);
                default:
                    return (mediumIntensity, mediumThreshold, mediumVignette); // 既定 = Medium
            }
        }

        /// <summary>
        /// **実行時に必ず適用する (Step 9-4)。**
        ///
        /// Step 6 では SceneBuilder (Editor) から Apply を呼ぶだけで、
        /// 実行時に呼ぶ口が無かった。しかも Editor 側の Apply は
        /// VolumeProfile アセットへ SetDirty / SaveAssets をしていなかったので、
        /// アセットには Bloom の既定値 (intensity 0 = 消灯) が残っていた。
        /// **結果、Step 6 以来 bloom は一度も効いていなかった。**
        /// </summary>
        void Awake() => Apply(_strength);

        public void Apply(PostProcessStrength strength)
        {
            _strength = strength;

            Volume volume = Volume;
            VolumeProfile profile = volume != null && volume.sharedProfile != null
                ? volume.sharedProfile
                : volume != null ? volume.profile : null;
            if (volume == null || profile == null)
            {
                Debug.LogWarning($"[PostProcessPreset] Volume が無いので {strength} を適用できない");
                return;
            }

            (float bloomIntensity, float bloomThreshold, float vignette) = Values(strength);

            if (profile.TryGet(out Bloom bloom))
            {
                bloom.active = true;
                bloom.scatter.overrideState = true;
                bloom.scatter.value = (float)PlanetAppearance.BloomScatter;
                bloom.intensity.overrideState = true;
                bloom.intensity.value = bloomIntensity;
                bloom.threshold.overrideState = true;
                bloom.threshold.value = bloomThreshold;
            }

            if (profile.TryGet(out Vignette v))
            {
                v.active = true;
                v.intensity.overrideState = true;
                v.intensity.value = vignette;
            }

            if (profile.TryGet(out Tonemapping tonemapping))
            {
                tonemapping.active = true;
                tonemapping.mode.overrideState = true;
                tonemapping.mode.value = TonemappingMode.ACES;
            }
        }
    }
}
