using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// ポストプロセスの値の出所 (Step 9-4)。
    ///
    /// **Step 6 の事故の再発防止。** 当時は Apply を Editor からしか呼ばず、
    /// アセットへ保存もしなかったので、実行時は Bloom の既定値
    /// (intensity 0 = 消灯) で動いていた。
    /// </summary>
    public sealed class PostProcessTests
    {
        static (PostProcessPreset preset, VolumeProfile profile, GameObject go) Make()
        {
            var go = new GameObject("PostProcessTest");
            var volume = go.AddComponent<Volume>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<Bloom>();
            profile.Add<Tonemapping>();
            profile.Add<Vignette>();
            volume.sharedProfile = profile;

            var preset = go.AddComponent<PostProcessPreset>();
            preset.Bind(volume);
            return (preset, profile, go);
        }

        [Test]
        public void MediumはCoreの定数と一致する()
        {
            (float intensity, float threshold, float _) =
                PostProcessPreset.Values(PostProcessStrength.Medium);

            Assert.That(intensity, Is.EqualTo((float)PlanetAppearance.BloomIntensity).Within(1e-4f),
                        "強度が Core の定数と食い違っている（二重管理）");
            Assert.That(threshold, Is.EqualTo((float)PlanetAppearance.BloomThreshold).Within(1e-4f),
                        "しきい値が Core の定数と食い違っている（二重管理）");
        }

        [Test]
        public void 強度は消灯していない()
        {
            (float intensity, float _, float __) =
                PostProcessPreset.Values(PostProcessStrength.Medium);

            // **0 だと bloom は一切効かない。** これが Step 6 の状態だった。
            Assert.That(intensity, Is.GreaterThan(0f), "bloom が消灯している");
        }

        [Test]
        public void しきい値は太陽の出力より低い()
        {
            (float _, float threshold, float __) =
                PostProcessPreset.Values(PostProcessStrength.Medium);

            // 太陽のトーンマップ前の出力は 9.055 (Step 9-1 の実測)。
            Assert.That(threshold, Is.LessThan(9.055f), "しきい値が太陽の出力を超えている");
        }

        [Test]
        public void Applyでプロファイルに値が入る()
        {
            (PostProcessPreset preset, VolumeProfile profile, GameObject go) = Make();
            try
            {
                preset.Apply(PostProcessStrength.Medium);

                Assert.That(profile.TryGet(out Bloom bloom), Is.True);
                Assert.That(bloom.active, Is.True);
                Assert.That(bloom.intensity.value,
                            Is.EqualTo((float)PlanetAppearance.BloomIntensity).Within(1e-4f));
                Assert.That(bloom.threshold.value,
                            Is.EqualTo((float)PlanetAppearance.BloomThreshold).Within(1e-4f));
                Assert.That(bloom.scatter.value,
                            Is.EqualTo((float)PlanetAppearance.BloomScatter).Within(1e-4f));

                Assert.That(profile.TryGet(out Tonemapping tm), Is.True);
                Assert.That(tm.mode.value, Is.EqualTo(TonemappingMode.ACES));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void 三段階は強度の順に並ぶ()
        {
            (float subtle, float subtleT, float _) = PostProcessPreset.Values(PostProcessStrength.Subtle);
            (float medium, float mediumT, float __) = PostProcessPreset.Values(PostProcessStrength.Medium);
            (float strong, float strongT, float ___) = PostProcessPreset.Values(PostProcessStrength.Strong);

            Assert.That(subtle, Is.LessThan(medium));
            Assert.That(medium, Is.LessThan(strong));

            // 強いほどしきい値は低い（より多くの画素が滲む）。
            Assert.That(subtleT, Is.GreaterThan(mediumT));
            Assert.That(mediumT, Is.GreaterThan(strongT));
        }
    }
}
