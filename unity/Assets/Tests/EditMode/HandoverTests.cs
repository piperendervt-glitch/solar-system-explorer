using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>引き渡し帯の整合 (Step 8-5)。</summary>
    public sealed class HandoverTests
    {
        [Test]
        public void 引き渡しシナリオが帯を挟んでいる()
        {
            var all = ScenarioLibrary.Create(SolarSystemModel.CreateOpposition());
            Vec3d earth = SolarSystemModel.CreateOpposition().Earth.AbsolutePosition;

            string[] names =
            {
                ScenarioLibrary.HandoverOutName,
                ScenarioLibrary.HandoverMidName,
                ScenarioLibrary.HandoverInName,
            };

            var distances = new System.Collections.Generic.List<double>();
            foreach (string name in names)
            {
                Scenario s = ScenarioLibrary.Find(all, name);
                Assert.That(s, Is.Not.Null, name + " が無い");
                double d = Vec3d.Distance(s.Start.Position, earth);
                distances.Add(d);
                Debug.Log($"[Step8-5] {name}: 距離 {d:E3} units / 引き渡し率 {RealScaleHandoff.Blend(d):F3}");
            }

            Assert.That(distances[0], Is.GreaterThan(RealScaleHandoff.FadeStartDistance), "帯の外側でない");
            Assert.That(distances[1], Is.LessThan(RealScaleHandoff.FadeStartDistance), "帯の中に無い");
            Assert.That(distances[1], Is.GreaterThan(RealScaleHandoff.FadeEndDistance), "帯の中に無い");
            Assert.That(distances[2], Is.LessThan(RealScaleHandoff.FadeEndDistance), "帯の内側でない");
        }

        [Test]
        public void 引き渡し率が0と0_5と1になる()
        {
            Assert.That(RealScaleHandoff.Blend(5.2e4), Is.EqualTo(0.0).Within(1e-9));
            Assert.That(RealScaleHandoff.Blend(4.0e4), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(RealScaleHandoff.Blend(2.8e4), Is.EqualTo(1.0).Within(1e-9));

            Debug.Log($"[Step8-5] 帯 {RealScaleHandoff.FadeStartDistance:E1} -> " +
                      $"{RealScaleHandoff.FadeEndDistance:E1} units");
        }

        [Test]
        public void 雲の半径倍率が殻と実スケールで同じ定数から来ている()
        {
            // 殻側は meshRadius * 2 * RadiusScale、実側は RadiusKm * 2 * RadiusScale。
            // どちらも「その空間での地表直径 x RadiusScale」なので比は同じ。
            Debug.Log($"[Step8-5] 雲の半径倍率 {CloudLayer.RadiusScale} (殻・実スケール共通の定数)");
            Assert.That(CloudLayer.RadiusScale, Is.EqualTo(1.006f).Within(1e-6f));
        }
    }
}
