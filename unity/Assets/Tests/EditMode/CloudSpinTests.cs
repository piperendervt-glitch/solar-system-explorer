using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>雲層と自転 (Step 8-3 / 8-4)。</summary>
    public sealed class CloudSpinTests
    {
        static CelestialBody Earth() => SolarSystemModel.CreateOpposition().Earth;
        static CelestialBody Mars() => SolarSystemModel.CreateOpposition().Mars;

        // ---- 8-3 雲層 ----

        [Test]
        public void 雲球のrenderQueueが地表より1大きい()
        {
            Material surface = SolarSystem.Editor.MaterialLibrary.MeshMaterial(Earth());
            Material clouds = SolarSystem.Editor.MaterialLibrary.CloudMaterial(Earth());

            Debug.Log($"[Step8-3] 地表 queue {surface.renderQueue} / 雲 queue {clouds.renderQueue}");
            Assert.That(clouds.renderQueue, Is.EqualTo(surface.renderQueue + 1),
                "同心球なので距離ソートが同値になる。queue で順序を確定させること");
        }

        [Test]
        public void 雲球の半径が地表より大きい()
        {
            Debug.Log($"[Step8-3] 雲の半径倍率 {CloudLayer.RadiusScale}");
            Assert.That(CloudLayer.RadiusScale, Is.GreaterThan(1f), "雲が地表の内側にある");
            Assert.That(CloudLayer.RadiusScale, Is.LessThan(1.05f), "浮きすぎ");
        }

        [Test]
        public void 雲マテリアルにテクスチャと不透明度が入っている()
        {
            Material clouds = SolarSystem.Editor.MaterialLibrary.CloudMaterial(Earth());
            Texture t = clouds.GetTexture("_BaseMap");
            float gain = clouds.GetFloat("_CloudOpacity");

            Debug.Log($"[Step8-3] 雲テクスチャ {(t != null ? t.name : "(無し)")} / ゲイン {gain}");
            Assert.That(t, Is.Not.Null, "雲テクスチャが空");

            // 素材の最大値は 227/255 = 0.89。1.0 まで持ち上げる係数は 1.123。
            Assert.That(gain, Is.GreaterThan(1.12f), "最大 0.89 のままでは雲が薄い");
        }

        // ---- 8-4 自転 ----

        [Test]
        public void 自転周期が天体のフィールドに入っている()
        {
            Debug.Log($"[Step8-4] 地球 {Earth().RotationPeriodHours} 時間 / 火星 {Mars().RotationPeriodHours} 時間");
            Assert.That(Earth().RotationPeriodHours, Is.EqualTo(23.93).Within(1e-9));
            Assert.That(Mars().RotationPeriodHours, Is.EqualTo(24.62).Within(1e-9));
        }

        [Test]
        public void 雲の角速度が地表より大きい()
        {
            double surface = BodyRotation.DegreesPerSecond(Earth().RotationPeriodHours);
            double cloud = BodyRotation.DegreesPerSecond(BodyRotation.EarthCloudPeriodHours);

            Debug.Log($"[Step8-4] 角速度 地表 {surface:E4} 度/秒 / 雲 {cloud:E4} 度/秒 " +
                      $"(比 {cloud / surface:F3})");
            Assert.That(cloud, Is.GreaterThan(surface), "雲が地表より遅い");

            // 6 時間ぶんで比べる。実時間 5 分では差が 1.5 px 程度でレンダリング誤差に埋もれる。
            const double sixHours = 6.0 * 3600.0;
            double surfaceAngle = BodyRotation.AngleDegrees(sixHours, Earth().RotationPeriodHours);
            double cloudAngle = BodyRotation.AngleDegrees(sixHours, BodyRotation.EarthCloudPeriodHours);
            Debug.Log($"[Step8-4] 6 時間で 地表 {surfaceAngle:F1} 度 / 雲 {cloudAngle:F1} 度 " +
                      $"(差 {cloudAngle - surfaceAngle:F1} 度)");
            Assert.That(cloudAngle - surfaceAngle, Is.GreaterThan(10.0), "6 時間でも差が小さすぎる");
        }

        [Test]
        public void 自転角が時刻に比例し周期でちょうど1周する()
        {
            const double period = 24.0;
            Assert.That(BodyRotation.AngleDegrees(0.0, period), Is.EqualTo(0.0));
            Assert.That(BodyRotation.AngleDegrees(period * 3600.0, period), Is.EqualTo(360.0).Within(1e-9));
            Assert.That(BodyRotation.AngleDegrees(period * 1800.0, period), Is.EqualTo(180.0).Within(1e-9));

            // 周期 0 以下は自転しない (太陽)。
            Assert.That(BodyRotation.AngleDegrees(1.0e6, 0.0), Is.EqualTo(0.0));

            Debug.Log($"[Step8-4] 倍率 {BodyRotation.SpeedMultiplier} (1.0 = 等倍)");
            Assert.That(BodyRotation.SpeedMultiplier, Is.EqualTo(1.0),
                "誇張すると ETA と自転が同じ時計を共有している矛盾が画面に出る");
        }

        [Test]
        public void 自転シナリオが2件あり時刻が違う()
        {
            var all = ScenarioLibrary.Create(SolarSystemModel.CreateOpposition());
            Scenario t0 = ScenarioLibrary.Find(all, ScenarioLibrary.EarthSpinT0Name);
            Scenario t6 = ScenarioLibrary.Find(all, ScenarioLibrary.EarthSpinT6hName);

            Assert.That(t0, Is.Not.Null);
            Assert.That(t6, Is.Not.Null);
            Debug.Log($"[Step8-4] t0 {t0.Start.ElapsedSeconds} 秒 / t6h {t6.Start.ElapsedSeconds} 秒");
            Assert.That(t6.Start.ElapsedSeconds, Is.EqualTo(6.0 * 3600.0).Within(1e-6));
            Assert.That(t0.Start.Position, Is.EqualTo(t6.Start.Position), "視点は同じにすること");
        }
    }
}
