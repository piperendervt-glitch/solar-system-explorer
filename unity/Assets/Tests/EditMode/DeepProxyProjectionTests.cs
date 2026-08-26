using NUnit.Framework;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// プロキシ殻への対数圧縮 (docs/01-architecture.md §3-3 / 決定 D-15)。
    /// </summary>
    public sealed class DeepProxyProjectionTests
    {
        [Test]
        public void 境界で殻の内外径にちょうど一致する()
        {
            Assert.That(DeepProxyProjection.ShellRadius(DeepProxyProjection.MinDistanceKm),
                Is.EqualTo(DeepProxyProjection.MinShellRadius).Within(1e-9));
            Assert.That(DeepProxyProjection.ShellRadius(DeepProxyProjection.MaxDistanceKm),
                Is.EqualTo(DeepProxyProjection.MaxShellRadius).Within(1e-9));
        }

        [Test]
        public void 境界で連続でクランプ側と一致する()
        {
            const double eps = 1.0; // 1 unit ずらして両側を比べる

            double insideLow = DeepProxyProjection.ShellRadius(DeepProxyProjection.MinDistanceKm - eps);
            double atLow = DeepProxyProjection.ShellRadius(DeepProxyProjection.MinDistanceKm);
            double aboveLow = DeepProxyProjection.ShellRadius(DeepProxyProjection.MinDistanceKm + eps);

            double belowHigh = DeepProxyProjection.ShellRadius(DeepProxyProjection.MaxDistanceKm - 1e3);
            double atHigh = DeepProxyProjection.ShellRadius(DeepProxyProjection.MaxDistanceKm);
            double outsideHigh = DeepProxyProjection.ShellRadius(DeepProxyProjection.MaxDistanceKm + 1e6);

            Debug.Log(
                $"[Step2] 殻の境界: d=1e4-1 -> {insideLow:F6} / d=1e4 -> {atLow:F6} / d=1e4+1 -> {aboveLow:F6}\n" +
                $"[Step2]           d=1e9-1e3 -> {belowHigh:F6} / d=1e9 -> {atHigh:F6} / d=1e9+1e6 -> {outsideHigh:F6}");

            // クランプ側と式側が境界で一致 = 連続。
            Assert.That(insideLow, Is.EqualTo(atLow).Within(1e-9));
            Assert.That(aboveLow - atLow, Is.LessThan(0.1), "境界のすぐ外側で飛んでいない");
            Assert.That(outsideHigh, Is.EqualTo(atHigh).Within(1e-9));
            Assert.That(atHigh - belowHigh, Is.LessThan(1.0), "境界のすぐ内側で飛んでいない");
        }

        [Test]
        public void 距離に対して単調非減少()
        {
            double previous = -1.0;
            double maxJump = 0.0;

            // 1e3 から 1e10 まで、1 桁あたり 200 点。
            const int samplesPerDecade = 200;
            const int decades = 7;
            for (int i = 0; i <= samplesPerDecade * decades; i++)
            {
                double d = System.Math.Pow(10.0, 3.0 + (double)i / samplesPerDecade);
                double r = DeepProxyProjection.ShellRadius(d);

                Assert.That(r, Is.GreaterThanOrEqualTo(previous),
                    $"d={d:E4} で単調性が壊れた ({previous:F6} -> {r:F6})");

                if (previous >= 0.0)
                {
                    maxJump = System.Math.Max(maxJump, r - previous);
                }

                previous = r;
                Assert.That(r, Is.InRange(DeepProxyProjection.MinShellRadius, DeepProxyProjection.MaxShellRadius));
            }

            Debug.Log($"[Step2] 単調性チェック {samplesPerDecade * decades + 1} 点 / 隣接点の最大跳躍 = {maxJump:F6} units");
            Assert.That(maxJump, Is.LessThan(20.0), "対数圧縮なら隣接サンプル間の跳躍は小さいはず");
        }

        [Test]
        public void スケールを掛けると角直径が厳密に一致する()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();

            (string name, double radius, double distance)[] cases =
            {
                ("Mars<-Earth", SolarSystemModel.MarsRadiusKm, SolarSystemModel.EarthToMarsKm),
                ("Earth<-Mars", SolarSystemModel.EarthRadiusKm, SolarSystemModel.EarthToMarsKm),
                ("Sun<-Earth", SolarSystemModel.SunRadiusKm, SolarSystemModel.SunToEarthKm),
                ("Sun<-Mars", SolarSystemModel.SunRadiusKm, SolarSystemModel.SunToMarsKm),
                ("Mars<-2e4", SolarSystemModel.MarsRadiusKm, 2.0e4),
            };

            foreach ((string name, double radius, double distance) in cases)
            {
                double shell = DeepProxyProjection.ShellRadius(distance);
                double scale = DeepProxyProjection.ScaleFactor(distance);

                double trueAngle = AngularSizeSolver.AngularDiameterRadians(radius, distance);
                double proxyAngle = AngularSizeSolver.AngularDiameterRadians(radius * scale, shell);
                double error = System.Math.Abs(trueAngle - proxyAngle);

                Debug.Log(
                    $"[Step2] {name,-12} d={distance:E4} shell={shell:F1} s={scale:E4} " +
                    $"proxy半径={radius * scale:F4} 角直径誤差={error:E3} rad");

                Assert.That(error, Is.LessThan(1e-15), $"{name} で角直径がずれた");
            }

            Assert.That(model.Bodies.Count, Is.EqualTo(3));
        }

        [Test]
        public void 殻はDeepカメラのnearとfarの内側に収まる()
        {
            // Deep カメラは near 500 / far 12000 (決定 D-6)。
            Assert.That(DeepProxyProjection.MinShellRadius, Is.GreaterThan(500.0));
            Assert.That(DeepProxyProjection.MaxShellRadius, Is.LessThan(12000.0));
        }

        [Test]
        public void プロキシが近クリップを切らない距離の下限を記録する()
        {
            // 殻の手前側 = shell - 天体半径*scale。これが near clip 500 を下回ると
            // 天体の手前が欠ける。距離が近づくほど scale が上がるので、
            // 「ある距離より近づくと破綻する」という下限が出る。
            //
            // 設計 (§3-3) はここで実スケールのオブジェクトへ引き渡すことになっている。
            // その実装は Step 5 (ステーション接近) の作業。ここでは下限を記録するだけ。
            const float nearClip = 500f;

            (string name, double radius, double expectedLimit)[] cases =
            {
                ("Sun", SolarSystemModel.SunRadiusKm, 7.844e5),
                ("Earth", SolarSystemModel.EarthRadiusKm, 1.155e4),
                ("Mars", SolarSystemModel.MarsRadiusKm, 6.777e3),
            };

            foreach ((string name, double radius, double expectedLimit) in cases)
            {
                double limit = FindNearClipLimit(radius, nearClip);
                Debug.Log($"[Step2] {name,-6} プロキシが near={nearClip} を切る距離 = {limit:E4} units 未満");
                Assert.That(limit, Is.EqualTo(expectedLimit).Within(expectedLimit * 0.01));
            }

            // Step 2 のスクショは最短 2e4 units。火星の下限 6777 units より遠いので安全。
            double marsLimit = FindNearClipLimit(SolarSystemModel.MarsRadiusKm, nearClip);
            Assert.That(2.0e4, Is.GreaterThan(marsLimit));
        }

        static double FindNearClipLimit(double radiusKm, double nearClip)
        {
            // 近い側から二分探索。
            double lo = 1.0;
            double hi = 1e9;
            for (int i = 0; i < 200; i++)
            {
                double mid = System.Math.Sqrt(lo * hi);
                double shell = DeepProxyProjection.ShellRadius(mid);
                double front = shell - radiusKm * DeepProxyProjection.ScaleFactor(mid);
                if (front < nearClip)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            return hi;
        }
    }
}
