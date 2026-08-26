using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SolarSystem.Unity
{
    /// <summary>ポストプロセスの強度。3 段階を比較して人間が選ぶ (Step 6)。</summary>
    public enum PostProcessStrength
    {
        Subtle,
        Medium,
        Strong,
    }

    /// <summary>
    /// ポストプロセスの設定 (Step 6)。
    ///
    /// **強度は確定させない。** 3 段階をスクショで並べて人間が 1 つ選ぶ。
    /// 選ばれるまでは Subtle を既定にしておく (要件 §1「静けさを優先」)。
    ///
    /// Bloom (太陽と星) / Tonemapping / わずかな Vignette の 3 つだけ。
    /// Update() を持たない。切り替えは Apply を呼ぶ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PostProcessPreset : MonoBehaviour
    {
        [SerializeField] Volume _volume;
        [SerializeField] PostProcessStrength _strength = PostProcessStrength.Subtle;

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

        /// <summary>各段階のパラメータ。ここだけ見れば強度の違いが分かる。</summary>
        public static (float bloomIntensity, float bloomThreshold, float vignette) Values(
            PostProcessStrength strength)
        {
            switch (strength)
            {
                case PostProcessStrength.Subtle:
                    return (0.35f, 1.30f, 0.12f);
                case PostProcessStrength.Medium:
                    return (0.80f, 1.05f, 0.22f);
                case PostProcessStrength.Strong:
                    return (1.60f, 0.85f, 0.34f);
                default:
                    return (0.35f, 1.30f, 0.12f);
            }
        }

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
