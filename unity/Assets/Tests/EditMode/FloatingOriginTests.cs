using NUnit.Framework;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// 浮動原点の数値テスト (決定 D-25: Step 1 の判定は数値のみ)。
    /// docs/01-architecture.md §2-5。
    /// </summary>
    public sealed class FloatingOriginTests
    {
        /// <summary>0.9c を 10 分ぶん、60 Hz 固定ステップで回す。</summary>
        const int TenMinuteStepCount = 36000;

        static Vec3d CruiseVelocity()
        {
            double v = UniverseConstants.DefaultCruiseBeta * UniverseConstants.SpeedOfLightKmPerSec;
            return new Vec3d(0.0, 0.0, v);
        }

        [Test]
        public void 往復変換は元の値に戻る()
        {
            var origin = new FloatingOrigin();
            origin.Rebase(new Vec3d(1.234e8, -5.678e7, 9.012e6));

            var absolute = new Vec3d(1.495978707e8, 3.3e7, -1.1e7);
            Vec3d roundTrip = origin.ToAbsolute(origin.ToOriginRelative(absolute));

            // a - o + o は double で厳密に元に戻るとは限らないが、
            // 1e-6 units = 1 mm 以内であること。
            Assert.That(Vec3d.Distance(roundTrip, absolute), Is.LessThan(1e-6));
        }

        [Test]
        public void 再基準化しても2天体の相対位置は変わらない()
        {
            var origin = new FloatingOrigin();
            var earth = new Vec3d(UniverseConstants.AstronomicalUnitKm, 0.0, 0.0);
            var mars = new Vec3d(UniverseConstants.AstronomicalUnitKm + 7.8e7, 1.0e6, -2.0e6);

            Vec3d expected = mars - earth;

            var ship = new AbsoluteMotion();
            ship.SetPosition(earth);
            ship.SetVelocity(CruiseVelocity());

            double maxError = 0.0;
            for (int i = 0; i < TenMinuteStepCount; i++)
            {
                ship.Step(UniverseConstants.FixedDeltaSeconds);
                origin.Rebase(ship.Position);

                Vec3d localEarth = origin.ToOriginRelative(earth);
                Vec3d localMars = origin.ToOriginRelative(mars);
                double error = Vec3d.Distance(localMars - localEarth, expected);
                if (error > maxError)
                {
                    maxError = error;
                }
            }

            Debug.Log($"[Step1] 相対位置の最大誤差 = {maxError:R} units ({maxError * 1e6:F6} mm) / シフト {origin.ShiftCount} 回");

            // 1e-6 units = 1 mm 以内。
            Assert.That(maxError, Is.LessThan(1e-6));
        }

        [Test]
        public void 三万六千回シフトしても位置誤差が発散しない()
        {
            var origin = new FloatingOrigin();
            var ship = new AbsoluteMotion();
            Vec3d velocity = CruiseVelocity();
            ship.SetVelocity(velocity);

            double maxPositionError = 0.0;
            double maxShipLocalError = 0.0;

            for (int i = 1; i <= TenMinuteStepCount; i++)
            {
                ship.Step(UniverseConstants.FixedDeltaSeconds);
                origin.Rebase(ship.Position);

                // 解析解: v * (i * dt)。積分の累積誤差だけを見る。
                Vec3d analytic = velocity * (i * UniverseConstants.FixedDeltaSeconds);
                double positionError = Vec3d.Distance(ship.Position, analytic);
                if (positionError > maxPositionError)
                {
                    maxPositionError = positionError;
                }

                // 毎フレーム再基準化なので、船の原点相対座標は常に厳密に 0。
                double shipLocal = origin.ToOriginRelative(ship.Position).Magnitude;
                if (shipLocal > maxShipLocalError)
                {
                    maxShipLocalError = shipLocal;
                }
            }

            // 同じ積分を float32 で回したときの誤差。double を使う根拠の対照。
            float floatPos = 0f;
            var floatStep = (float)(velocity.Z * UniverseConstants.FixedDeltaSeconds);
            for (int i = 0; i < TenMinuteStepCount; i++)
            {
                floatPos += floatStep;
            }

            double travelled = ship.Position.Magnitude;
            double analyticFinal = velocity.Z * (TenMinuteStepCount * UniverseConstants.FixedDeltaSeconds);
            double floatError = System.Math.Abs(floatPos - analyticFinal);

            Debug.Log(
                $"[Step1] 0.9c x 600s ({TenMinuteStepCount} ステップ): 移動 {travelled:R} units\n" +
                $"[Step1]   double 積分の位置誤差 最大 = {maxPositionError:R} units = {maxPositionError * 1e6:F3} mm " +
                $"(相対 {maxPositionError / travelled:E3})\n" +
                $"[Step1]   float32 で同じ積分をした場合 = {floatError:R} units = {floatError / 1000.0:F3} km " +
                $"(相対 {floatError / travelled:E3})\n" +
                $"[Step1]   船の原点相対座標の最大 = {maxShipLocalError:R} units");

            // 逐次積分である以上、丸めは 1 ステップごとに乗る (ランダムウォークで sqrt(N) 倍)。
            // 「発散しない」の判定は絶対値ではなく相対誤差で行う。
            // double の刻みは 1.6189e8 units で 2.98e-8 units なので、
            // 36000 ステップぶんの相対誤差が 1e-11 に収まっていれば理論限界の範囲。
            Assert.That(maxPositionError / travelled, Is.LessThan(1e-11), "積分の相対誤差が double の理論限界を超えた");

            // 実用上の上限。1.6189e8 units 飛んで 1e-3 units (1 m) 以内。
            Assert.That(maxPositionError, Is.LessThan(1e-3), "積分の累積誤差が 1 m を超えた");

            Assert.That(maxShipLocalError, Is.EqualTo(0.0), "毎フレーム再基準化なら船は厳密に原点にいるはず");
        }

        [Test]
        public void 原点相対座標をfloatに落としてもジッターが1cm未満()
        {
            // ドッキング相当: 船から 20 m 先のマーカー。
            var origin = new FloatingOrigin();
            var ship = new AbsoluteMotion();
            ship.SetPosition(new Vec3d(UniverseConstants.AstronomicalUnitKm, 0.0, 0.0));
            ship.SetVelocity(new Vec3d(0.0, 0.0, UniverseConstants.ManualMaxSpeedKmPerSec));

            double maxJitterMeters = 0.0;
            for (int i = 0; i < 6000; i++) // 100 秒ぶん
            {
                ship.Step(UniverseConstants.FixedDeltaSeconds);
                origin.Rebase(ship.Position);

                Vec3d markerAbsolute = ship.Position + new Vec3d(0.02, 0.0, 0.0); // 20 m 先
                Vec3d local = origin.ToOriginRelative(markerAbsolute);

                var asFloat = new Vector3((float)local.X, (float)local.Y, (float)local.Z);
                double errorUnits = Vec3d.Distance(new Vec3d(asFloat.x, asFloat.y, asFloat.z), local);
                double errorMeters = errorUnits * 1000.0;
                if (errorMeters > maxJitterMeters)
                {
                    maxJitterMeters = errorMeters;
                }
            }

            Debug.Log($"[Step1] 20 m 先のマーカーの float32 量子化誤差の最大 = {maxJitterMeters:E3} m");

            Assert.That(maxJitterMeters, Is.LessThan(0.01), "20 m 先で 1 cm 以上ずれたら浮動原点が効いていない");
        }

        [Test]
        public void シフト量は前回原点との差になる()
        {
            var origin = new FloatingOrigin();
            origin.Rebase(new Vec3d(100.0, 0.0, 0.0));
            origin.Rebase(new Vec3d(150.0, -20.0, 0.0));

            Assert.That(origin.LastShift.X, Is.EqualTo(50.0).Within(1e-12));
            Assert.That(origin.LastShift.Y, Is.EqualTo(-20.0).Within(1e-12));
            Assert.That(origin.ShiftCount, Is.EqualTo(2));
        }
    }
}
