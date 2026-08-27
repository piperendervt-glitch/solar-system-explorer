using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Editor;
using SolarSystem.Unity;
using UnityEngine.Rendering;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>フレアの見た目 (Step 9-3b)。</summary>
    public sealed class FlareLookTests
    {
        static LensFlareDataSRP Data() => LensFlareBuilder.GetOrCreate();

        [Test]
        public void 要素の並びが索引と一致する()
        {
            LensFlareDataSRP data = Data();
            Assert.That(data.elements.Length, Is.EqualTo(LensFlareBuilder.ElementCount));

            // **実行時側の索引と一致していること。** ずれると別の要素を触る。
            Assert.That(FlareRuntimeData.GlareIndex, Is.EqualTo(LensFlareBuilder.GlareIndex));
            Assert.That(FlareRuntimeData.SpikeFirstIndex, Is.EqualTo(LensFlareBuilder.SpikeFirstIndex));
            Assert.That(FlareRuntimeData.StripeIndex, Is.EqualTo(LensFlareBuilder.StripeIndex));
            Assert.That(FlareRuntimeData.GhostIndex, Is.EqualTo(LensFlareBuilder.GhostIndex));
        }

        [Test]
        public void 中心のグレアは円()
        {
            LensFlareDataElementSRP e = Data().elements[LensFlareBuilder.GlareIndex];
            Assert.That(e.flareType, Is.EqualTo(SRPLensFlareType.Circle));
            Assert.That(e.position, Is.EqualTo(0f).Within(1e-4f), "グレアは太陽の位置に重なる");
        }

        [Test]
        public void 光条は筋のImageで等間隔に並ぶ()
        {
            LensFlareDataSRP data = Data();
            int max = PlanetAppearance.FlareSpikeElementMax;
            int active = FlareRuntimeData.ElementsForSpikeCount(PlanetAppearance.FlareSpikeCount);

            for (int i = 0; i < max; i++)
            {
                LensFlareDataElementSRP e = data.elements[LensFlareBuilder.SpikeFirstIndex + i];
                Assert.That(e.flareType, Is.EqualTo(SRPLensFlareType.Image), $"光条 {i} が Image でない");
                Assert.That(e.lensFlareTexture, Is.Not.Null, $"光条 {i} にテクスチャが無い");
                Assert.That(e.preserveAspectRatio, Is.False,
                            "preserveAspectRatio が立っていると sizeXY で伸ばせない");
                Assert.That(e.autoRotate, Is.False, "autoRotate だと角度が固定できない");
                Assert.That(e.rotation, Is.EqualTo(i * 180f / active).Within(1e-3f));
            }
        }

        [Test]
        public void アセットの既定が画面上の既定本数と一致する()
        {
            LensFlareDataSRP data = Data();
            int visible = 0;
            for (int i = 0; i < PlanetAppearance.FlareSpikeElementMax; i++)
            {
                if (data.elements[LensFlareBuilder.SpikeFirstIndex + i].visible) { visible++; }
            }

            // **パネルを開かなくても既定どおりに見えること。**
            // 全部 visible で作ると既定 6 本のつもりが 12 本で描かれる。
            Assert.That(visible * 2, Is.EqualTo((int)PlanetAppearance.FlareSpikeCount),
                        $"アセットは {visible * 2} 本ぶん有効だが、既定は {PlanetAppearance.FlareSpikeCount} 本");
        }

        [Test]
        public void 水平方向の縞は横に長い()
        {
            LensFlareDataElementSRP e = Data().elements[LensFlareBuilder.StripeIndex];
            Assert.That(e.flareType, Is.EqualTo(SRPLensFlareType.Image));
            Assert.That(e.rotation, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(e.sizeXY.x, Is.GreaterThan(e.sizeXY.y * 10f), "横に引き伸ばされていない");
        }

        [Test]
        public void ゴーストは複数個が太陽の反対側に並ぶ()
        {
            LensFlareDataElementSRP e = Data().elements[LensFlareBuilder.GhostIndex];
            Assert.That(e.allowMultipleElement, Is.True);
            Assert.That(e.count, Is.EqualTo(LensFlareBuilder.GhostCount));
            Assert.That(LensFlareBuilder.GhostCount, Is.InRange(4, 6), "計画書 9-3 の 4〜6 個");
            Assert.That(e.distribution, Is.EqualTo(SRPLensFlareDistribution.Uniform));
            // **内部で 2 倍される** (LensFlareCommonSRP:1465)。描画位置は
            // screenPos * (1 - 2*position) で、0.5 = 画面中心 / 1.0 = 対称点。
            // 反対側に出すには 0.5 を超えていること。大きすぎると画面外へ飛ぶ。
            Assert.That(e.position, Is.GreaterThan(0.5f), "0.5 以下では反対側に出ない");
            Assert.That(e.position, Is.LessThanOrEqualTo(1.0f), "1.0 を超えると画面外へ飛ぶ");
            Assert.That(e.localIntensity,
                        Is.EqualTo((float)PlanetAppearance.FlareGhostIntensity).Within(1e-4f));
        }

        // ---- 生成テクスチャの値域 ----

        [Test]
        public void 筋は中心が1で四隅が0()
        {
            Assert.That(FlareTextureBuilder.Profile(0f, 0f), Is.EqualTo(1f).Within(1e-6f));

            foreach (var (u, v) in new[] { (1f, 1f), (-1f, 1f), (1f, -1f), (-1f, -1f) })
            {
                Assert.That(FlareTextureBuilder.Profile(u, v), Is.EqualTo(0f), $"四隅 ({u},{v}) が 0 でない");
            }
        }

        [Test]
        public void 筋はx軸y軸それぞれで中心から単調に減る()
        {
            float prevX = float.MaxValue;
            float prevY = float.MaxValue;

            for (int i = 0; i <= 100; i++)
            {
                float t = i / 100f;

                float x = FlareTextureBuilder.Profile(t, 0f);
                Assert.That(x, Is.LessThanOrEqualTo(prevX), $"x 軸 u={t} で増えている");
                prevX = x;

                float y = FlareTextureBuilder.Profile(0f, t);
                Assert.That(y, Is.LessThanOrEqualTo(prevY), $"y 軸 v={t} で増えている");
                prevY = y;
            }
        }

        [Test]
        public void 筋は横に長く縦に細い()
        {
            // 同じ距離なら、長さ方向のほうが明るく残る。
            Assert.That(FlareTextureBuilder.Profile(0.5f, 0f),
                        Is.GreaterThan(FlareTextureBuilder.Profile(0f, 0.5f)),
                        "縦横の減衰が逆になっている");
        }

        // ---- 本数の換算 ----

        [TestCase(0.0, 0)]
        [TestCase(2.0, 1)]
        [TestCase(6.0, 3)]
        [TestCase(12.0, 6)]
        [TestCase(99.0, PlanetAppearance.FlareSpikeElementMax)]
        public void 画面上の本数から要素数を出す(double screenSpikes, int expected)
        {
            Assert.That(FlareRuntimeData.ElementsForSpikeCount(screenSpikes), Is.EqualTo(expected));
        }

        [Test]
        public void 既定の本数は要素の最大数に収まる()
        {
            int elements = FlareRuntimeData.ElementsForSpikeCount(PlanetAppearance.FlareSpikeCount);
            Assert.That(elements, Is.EqualTo(3), "既定 6 本 = 3 要素");
            Assert.That(elements, Is.LessThanOrEqualTo(PlanetAppearance.FlareSpikeElementMax));
        }
    }
}
