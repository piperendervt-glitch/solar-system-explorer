using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Editor;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>コロナ (Step 9-2)。</summary>
    public sealed class SunCoronaTests
    {
        static CelestialBody Sun() => SolarSystemModel.CreateOpposition().Sun;

        [Test]
        public void 減衰は中心1で縁0()
        {
            Assert.That(CoronaTextureBuilder.Profile(0f), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(CoronaTextureBuilder.Profile(1f), Is.EqualTo(0f),
                        "縁で厳密に 0 にならないと、Quad の輪郭が四角く見える");
            Assert.That(CoronaTextureBuilder.Profile(1.5f), Is.EqualTo(0f));
        }

        [Test]
        public void 減衰は単調に減る()
        {
            float prev = float.MaxValue;
            for (int i = 0; i <= 100; i++)
            {
                float r = i / 100f;
                float p = CoronaTextureBuilder.Profile(r);
                Assert.That(p, Is.LessThanOrEqualTo(prev), $"r={r} で増えている");
                prev = p;
            }
        }

        [Test]
        public void 太陽の縁での寄与がbloomしきい値を超える()
        {
            // 2.5 倍のとき太陽の縁は r = 1/2.5 = 0.40 に来る。
            float r = 1f / (float)PlanetAppearance.CoronaRadiusScale;
            float contribution = CoronaTextureBuilder.Shaped(r, PlanetAppearance.CoronaFalloff)
                                 * (float)PlanetAppearance.SunEmissionIntensity;

            (float _, float threshold, float __) =
                SolarSystem.Unity.PostProcessPreset.Values(
                    SolarSystem.Unity.PostProcessStrength.Medium);

            Assert.That(CoronaTextureBuilder.Shaped(r, PlanetAppearance.CoronaFalloff),
                        Is.EqualTo(0.216f).Within(1e-3f));
            Assert.That(contribution, Is.GreaterThan(threshold),
                        $"太陽の縁でのコロナの寄与 {contribution} がしきい値 {threshold} 以下");
        }

        [Test]
        public void コロナは太陽だけに作られる()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();
            Assert.That(model.Sun.Kind, Is.EqualTo(CelestialBodyKind.Star));
            Assert.That(model.Earth.Kind, Is.Not.EqualTo(CelestialBodyKind.Star));
            Assert.That(model.Mars.Kind, Is.Not.EqualTo(CelestialBodyKind.Star));
        }

        [Test]
        public void コロナの強度が本体と一致する()
        {
            CelestialBody sun = Sun();
            float corona = MaterialLibrary.CoronaMaterial(sun).GetFloat("_EmissionIntensity");
            float mesh = MaterialLibrary.MeshMaterial(sun).GetFloat("_EmissionIntensity");

            Assert.That(corona, Is.EqualTo(mesh).Within(1e-4f),
                        "コロナだけ取り残されると縁で段差になる");
        }

        [Test]
        public void コロナはAdditiveでZWriteしない()
        {
            Material m = MaterialLibrary.CoronaMaterial(Sun());
            Assert.That(m.shader.name, Is.EqualTo(MaterialLibrary.CoronaShaderName));

            // ブレンドはパスに固定で書いてある。マテリアル側から上書きされていないこと。
            Assert.That(m.GetTag("RenderType", false), Is.EqualTo("Transparent"));
            Assert.That(m.renderQueue, Is.GreaterThan((int)UnityEngine.Rendering.RenderQueue.Transparent),
                        "地表・雲より後に描かないと、加算が下の絵を拾えない");
        }

        [Test]
        public void グラデーションのテクスチャが作られる()
        {
            Texture2D t = CoronaTextureBuilder.GetOrCreate();
            Assert.That(t, Is.Not.Null);
            Assert.That(t.width, Is.EqualTo(CoronaTextureBuilder.Size));
            Assert.That(t.height, Is.EqualTo(CoronaTextureBuilder.Size));
        }
    }
}
