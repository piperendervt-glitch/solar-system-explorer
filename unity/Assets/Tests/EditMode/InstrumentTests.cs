using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>計器の書式とカメラ段の整合 (Step 4)。</summary>
    public sealed class InstrumentTests
    {
        // ---- 速度表記 (決定 D-12 の 4 項目) ----

        [Test]
        public void 速度は1km毎秒未満ならkm毎秒表記()
        {
            Assert.That(InstrumentPanel.FormatSpeed(0.0), Is.EqualTo("0.000 km/s"));
            Assert.That(InstrumentPanel.FormatSpeed(0.5), Is.EqualTo("0.500 km/s"));
            Assert.That(InstrumentPanel.FormatSpeed(0.999), Is.EqualTo("0.999 km/s"));
        }

        [Test]
        public void 速度は1km毎秒以上ならc表記()
        {
            // 1 km/s = 3.3e-6 c。c 表記だと 0.000 c にしかならないが、
            // ここが表記の切り替え点 (決定 D-11 と同じ 1 km/s)。
            Assert.That(InstrumentPanel.FormatSpeed(1.0), Does.EndWith(" c"));

            double cruise = UniverseConstants.BetaToKmPerSec(UniverseConstants.DefaultCruiseBeta);
            Debug.Log($"[Step4] 0.9c = {InstrumentPanel.FormatSpeed(cruise)} / " +
                      $"1 km/s = {InstrumentPanel.FormatSpeed(1.0)} / " +
                      $"0.5 km/s = {InstrumentPanel.FormatSpeed(0.5)}");
            Assert.That(InstrumentPanel.FormatSpeed(cruise), Is.EqualTo("0.900 c"));
        }

        // ---- ETA (決定 D-13) ----

        [Test]
        public void 接近していないときのETAはダッシュ()
        {
            Assert.That(InstrumentPanel.FormatEta(double.PositiveInfinity), Is.EqualTo("--:--:--"));
            Assert.That(InstrumentPanel.FormatEta(double.NaN), Is.EqualTo("--:--:--"));
            Assert.That(InstrumentPanel.FormatEta(-1.0), Is.EqualTo("--:--:--"));
            Assert.That(InstrumentPanel.FormatEta(100.0 * 3600.0), Is.EqualTo("--:--:--"), "100 時間以上も表示しない");
        }

        [Test]
        public void ETAは時分秒で出る()
        {
            Assert.That(InstrumentPanel.FormatEta(0.0), Is.EqualTo("00:00:00"));
            Assert.That(InstrumentPanel.FormatEta(294.07), Is.EqualTo("00:04:54"));
            Assert.That(InstrumentPanel.FormatEta(2601.8), Is.EqualTo("00:43:21"));
        }

        [Test]
        public void 距離は1000units未満ならkm表記()
        {
            Assert.That(InstrumentPanel.FormatDistance(19.8), Is.EqualTo("19.8 km"));
            Assert.That(InstrumentPanel.FormatDistance(7.8e7), Is.EqualTo("7.800e+7 km"));
        }

        // ---- 更新頻度 ----

        [Test]
        public void 計器は10Hzでしか更新しない()
        {
            var go = new GameObject("panel");
            try
            {
                var panel = go.AddComponent<InstrumentPanel>();
                panel.ResetForTest();

                // 60 Hz で 1 秒ぶん回す。値は毎ステップ変える。
                for (int i = 0; i < 60; i++)
                {
                    panel.Tick(i / 60.0, i * 1.0, i * 100.0, i * 1.0, "Mars");
                }

                Debug.Log($"[Step4] 60 フレームで SetText を呼んだ回数 = {panel.RebuildCount} " +
                          $"(毎フレームなら 240 回)");

                // 10 Hz なら 10 回ぶん x 4 項目 = 40 回程度。240 回にはならない。
                Assert.That(panel.RebuildCount, Is.LessThanOrEqualTo(44));
                Assert.That(panel.RebuildCount, Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void 値が変わらなければSetTextを呼ばない()
        {
            var go = new GameObject("panel");
            try
            {
                var panel = go.AddComponent<InstrumentPanel>();
                panel.ResetForTest();

                for (int i = 0; i < 60; i++)
                {
                    panel.Tick(i / 60.0, 0.0, 100.0, double.PositiveInfinity, "Mars");
                }

                Debug.Log($"[Step4] 値が一定なら SetText 呼び出しは {panel.RebuildCount} 回");
                Assert.That(panel.RebuildCount, Is.EqualTo(4), "初回の 4 項目だけ");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---- カメラ 3 段の境界 ----

        [Test]
        public void カメラ3段の範囲が距離で連続していて隙間がない()
        {
            // Deep はプロキシ殻専用なので Near と重なってよい (レイヤーが排他)。
            // 問題になるのは「どの距離のオブジェクトも必ずどれかの段に入るか」。
            Debug.Log($"[Step4] Cockpit [{CameraStackController.CockpitNearClip:E2}, {CameraStackController.CockpitFarClip:E2}] " +
                      $"(描画倍率 {CameraStackController.CockpitRenderScale:E0}) / " +
                      $"Near [{CameraStackController.NearNearClip:E2}, {CameraStackController.NearFarClip:E2}] / " +
                      $"Deep [{CameraStackController.DeepNearClip:E2}, {CameraStackController.DeepFarClip:E2}]");

            // **Unity は nearClipPlane を 0.01 で下限クランプする。**
            // 実寸 2 m = 0.002 units のままでは丸ごと near の内側に入って消える (実測)。
            // コックピットだけ CockpitRenderScale 倍の描画空間で組む。
            Assert.That(CameraStackController.CockpitNearClip, Is.GreaterThanOrEqualTo(0.01f),
                "Unity のクランプ下限 0.01 を下回る値は指定できない");

            const float realCockpitSize = 0.002f; // 2 m
            float scaled = realCockpitSize * CameraStackController.CockpitRenderScale;
            Debug.Log($"[Step4] コックピット: 実寸 {realCockpitSize} units -> 描画空間 {scaled} units");

            Assert.That(scaled, Is.GreaterThan(CameraStackController.CockpitNearClip));
            Assert.That(scaled * 2f, Is.LessThan(CameraStackController.CockpitFarClip));

            // プロキシ殻 (1000〜10000) は Deep 段に完全に収まる。
            Assert.That(DeepProxyProjection.MinShellRadius, Is.GreaterThan(CameraStackController.DeepNearClip));
            Assert.That(DeepProxyProjection.MaxShellRadius, Is.LessThan(CameraStackController.DeepFarClip));

            // 実スケール天体 (最遠 5e4 + 半径) は Near 段に完全に収まる。
            double farthest = RealScaleHandoff.FadeStartDistance + SolarSystemModel.MarsRadiusKm;
            Assert.That(farthest, Is.LessThan(CameraStackController.NearFarClip));
            Assert.That(RealScaleHandoff.FadeEndDistance - SolarSystemModel.MarsRadiusKm,
                Is.GreaterThan(CameraStackController.NearNearClip),
                "渡し切りの距離でも天体の手前側が Near の near clip の外にある");
        }

        [Test]
        public void 各天体の表現のアルファの合計が常に1()
        {
            // 光点 / プロキシメッシュ / 実スケールメッシュ のどれか 1 つ以上が
            // 必ず見えていて、合計が 1 を超えない = 消えも二重描画もしない。
            double rpp = AngularSizeSolver.RadiansPerPixel(
                UniverseConstants.ReferenceVerticalFovDegrees, UniverseConstants.ReferencePixelHeight);

            var lod = new BodyLodSolver();
            double worstLow = 1.0;
            double worstHigh = 1.0;

            // 1e8 -> 5e3 units まで対数的に近づく。
            const int steps = 5000;
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double d = System.Math.Pow(10.0, 8.0 + t * (System.Math.Log10(5.0e3) - 8.0));

                lod.Update(AngularSizeSolver.AngularDiameterPixels(SolarSystemModel.MarsRadiusKm, d, rpp));
                double real = RealScaleHandoff.Blend(d);
                double proxyShare = 1.0 - real;

                double total = lod.Blend * proxyShare + (1.0 - lod.Blend) * proxyShare + real;
                worstLow = System.Math.Min(worstLow, total);
                worstHigh = System.Math.Max(worstHigh, total);
            }

            Debug.Log($"[Step4] アルファ合計の範囲: {worstLow:F9} 〜 {worstHigh:F9} ({steps + 1} 点)");
            Assert.That(worstLow, Is.EqualTo(1.0).Within(1e-9), "どこかで薄くなる = 消える");
            Assert.That(worstHigh, Is.EqualTo(1.0).Within(1e-9), "どこかで濃くなる = 二重描画");
        }
    }
}
