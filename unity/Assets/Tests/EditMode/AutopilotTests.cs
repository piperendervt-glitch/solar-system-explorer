using NUnit.Framework;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>オートパイロットと実スケール引き渡し (Step 3b)。</summary>
    public sealed class AutopilotTests
    {
        const double Dt = UniverseConstants.FixedDeltaSeconds;

        static readonly SolarSystemModel Model = SolarSystemModel.CreateOpposition();

        static double CruiseKmPerSec =>
            UniverseConstants.BetaToKmPerSec(UniverseConstants.DefaultCruiseBeta);

        /// <summary>火星から distance の地点。</summary>
        static Vec3d Start(double distance) => DebugJumpTable.PositionAt(Model, distance);

        /// <summary>目標へ向かって積分しながら走らせる。到達した状態と経過秒を返す。</summary>
        static (AutopilotSolver ap, AbsoluteMotion ship, double seconds, int steps)
            Fly(double startDistance, int maxSteps = 400000)
        {
            var ap = new AutopilotSolver();
            var ship = new AbsoluteMotion();
            ship.SetPosition(Start(startDistance));
            ap.Engage(ship.Position, Model.Mars.AbsolutePosition, CruiseKmPerSec);

            int steps = 0;
            while (ap.State != AutopilotState.Arrived && steps < maxSteps)
            {
                // 機首は常に合っている扱い (Align は Unity 側の回転が担当)。
                ap.Step(ship.Position, 0.0, Dt);
                Vec3d dir = (ap.TargetPosition - ship.Position).Normalized;
                ship.SetVelocity(dir * ap.CommandedSpeedKmPerSec);
                ship.Step(Dt);
                steps++;
            }

            return (ap, ship, steps * Dt, steps);
        }

        // ---- 状態遷移 ----

        [Test]
        public void 起動前はIdle()
        {
            var ap = new AutopilotSolver();
            Assert.That(ap.State, Is.EqualTo(AutopilotState.Idle));
            Assert.That(ap.IsEngaged, Is.False);
            Assert.That(ap.CommandedSpeedKmPerSec, Is.EqualTo(0.0));
        }

        [Test]
        public void 起動するとAlignから始まり機首が合うまで進まない()
        {
            var ap = new AutopilotSolver();
            Vec3d pos = Start(7.8e4);
            ap.Engage(pos, Model.Mars.AbsolutePosition, CruiseKmPerSec);

            Assert.That(ap.State, Is.EqualTo(AutopilotState.Align));

            // 30 度ずれている間は Align のまま、指令速度は 0。
            for (int i = 0; i < 100; i++)
            {
                ap.Step(pos, 30.0, Dt);
            }

            Assert.That(ap.State, Is.EqualTo(AutopilotState.Align));
            Assert.That(ap.CommandedSpeedKmPerSec, Is.EqualTo(0.0));

            ap.Step(pos, AutopilotSolver.AlignToleranceDegrees, Dt);
            Assert.That(ap.State, Is.EqualTo(AutopilotState.Cruise));
        }

        [Test]
        public void 状態は飛ばずに順に進む()
        {
            var ap = new AutopilotSolver();
            var ship = new AbsoluteMotion();
            ship.SetPosition(Start(7.8e4));
            ap.Engage(ship.Position, Model.Mars.AbsolutePosition, CruiseKmPerSec);

            var seen = new System.Collections.Generic.List<AutopilotState> { ap.State };
            AutopilotState previous = ap.State;

            for (int i = 0; i < 200000 && ap.State != AutopilotState.Arrived; i++)
            {
                ap.Step(ship.Position, 0.0, Dt);
                Vec3d dir = (ap.TargetPosition - ship.Position).Normalized;
                ship.SetVelocity(dir * ap.CommandedSpeedKmPerSec);
                ship.Step(Dt);

                if (ap.State != previous)
                {
                    seen.Add(ap.State);

                    // 隣接遷移だけを許す。Cruise から直接 Arrived にはならない。
                    int from = (int)previous;
                    int to = (int)ap.State;
                    Assert.That(to, Is.EqualTo(from + 1),
                        $"状態が飛んだ: {previous} -> {ap.State}");
                    previous = ap.State;
                }
            }

            Debug.Log("[Step3b] 状態遷移: " + string.Join(" -> ", seen));
            Assert.That(seen, Is.EqualTo(new[]
            {
                AutopilotState.Align, AutopilotState.Cruise,
                AutopilotState.Brake, AutopilotState.Arrived,
            }));
        }

        [Test]
        public void 解除するとIdleに戻り指令速度が0になる()
        {
            var ap = new AutopilotSolver();
            Vec3d pos = Start(7.8e4);
            ap.Engage(pos, Model.Mars.AbsolutePosition, CruiseKmPerSec);
            ap.Step(pos, 0.0, Dt);
            ap.Step(pos, 0.0, Dt);
            Assert.That(ap.CommandedSpeedKmPerSec, Is.GreaterThan(0.0));

            ap.Disengage();
            Assert.That(ap.State, Is.EqualTo(AutopilotState.Idle));
            Assert.That(ap.CommandedSpeedKmPerSec, Is.EqualTo(0.0));
        }

        // ---- 制動 ----

        [Test]
        public void 制動距離は制動時間一定の式どおり()
        {
            // 0.9c からの制動距離 = v * 10 / 2
            double v = CruiseKmPerSec;
            double expected = v * AutopilotSolver.BrakeSeconds * 0.5;
            Debug.Log($"[Step3b] 0.9c = {v:F2} km/s / 制動距離 = {expected:E4} units");
            Assert.That(expected, Is.EqualTo(1.349e6).Within(1e3));
        }

        [Test]
        public void 止まりきれない速度は出さない()
        {
            // 1/1000 スケールの仮目標 7.8e4 units (決定 D-8)。
            // 0.9c の制動距離 1.349e6 は残距離を大きく超えるので、頭打ちされる。
            var ap = new AutopilotSolver();
            ap.Engage(Start(7.8e4), Model.Mars.AbsolutePosition, CruiseKmPerSec);

            double cap = 2.0 * (7.8e4 - UniverseConstants.ArrivalRadiusUnits) / AutopilotSolver.BrakeSeconds;
            Debug.Log($"[Step3b] 要求 {ap.RequestedCruiseKmPerSec:E4} km/s -> 実効 {ap.EffectiveCruiseKmPerSec:E4} km/s " +
                      $"({UniverseConstants.KmPerSecToBeta(ap.EffectiveCruiseKmPerSec):F4} c) / 上限 {cap:E4}");

            Assert.That(ap.EffectiveCruiseKmPerSec, Is.EqualTo(cap).Within(1e-6));
            Assert.That(ap.EffectiveCruiseKmPerSec, Is.LessThan(ap.RequestedCruiseKmPerSec));

            // 実距離 7.8e7 units なら頭打ちは効かない。
            var far = new AutopilotSolver();
            far.Engage(Start(7.8e7), Model.Mars.AbsolutePosition, CruiseKmPerSec);
            Assert.That(far.EffectiveCruiseKmPerSec, Is.EqualTo(CruiseKmPerSec).Within(1e-6));
        }

        [Test]
        public void 仮目標へ放置すると到着する()
        {
            (AutopilotSolver ap, AbsoluteMotion ship, double seconds, int steps) = Fly(7.8e4);

            double finalDistance = Model.Mars.DistanceFrom(ship.Position);
            double finalSpeed = ship.SpeedKmPerSec;
            double overshoot = UniverseConstants.ArrivalRadiusUnits - finalDistance;

            Debug.Log(
                $"[Step3b] 仮目標 7.8e4 units への到着: {seconds:F3} 秒 ({steps} ステップ)\n" +
                $"[Step3b]   最終距離 {finalDistance:F6} units / 最終速度 {finalSpeed:F6} km/s\n" +
                $"[Step3b]   到着圏 20 units に対する行き過ぎ {overshoot:F6} units");

            Assert.That(ap.State, Is.EqualTo(AutopilotState.Arrived));
            Assert.That(finalDistance, Is.LessThanOrEqualTo(UniverseConstants.ArrivalRadiusUnits));
            Assert.That(finalDistance, Is.GreaterThan(0.0), "目標を通り過ぎていない");
            Assert.That(finalSpeed, Is.LessThanOrEqualTo(UniverseConstants.ArrivalMaxSpeedKmPerSec));
        }

        [Test]
        public void 実距離でも到着する()
        {
            (AutopilotSolver ap, AbsoluteMotion ship, double seconds, _) = Fly(7.8e7);
            double finalDistance = Model.Mars.DistanceFrom(ship.Position);

            Debug.Log($"[Step3b] 実距離 7.8e7 units への到着: {seconds:F2} 秒 " +
                      $"({seconds / 60:F2} 分) / 最終距離 {finalDistance:F6} units / " +
                      $"最終速度 {ship.SpeedKmPerSec:F6} km/s");

            Assert.That(ap.State, Is.EqualTo(AutopilotState.Arrived));
            Assert.That(finalDistance, Is.LessThanOrEqualTo(UniverseConstants.ArrivalRadiusUnits));

            // 巡航区間 (7.8e7 - 20 - 1.349e6) / 269813.2 = 284.1 秒 + 制動 10 秒 = 294.1 秒。
            Assert.That(seconds, Is.EqualTo(294.0).Within(3.0));
        }

        [Test]
        public void 到着圏の手前で制動が終わる()
        {
            (AutopilotSolver ap, AbsoluteMotion ship, _, _) = Fly(7.8e4);
            double finalDistance = Model.Mars.DistanceFrom(ship.Position);

            // 20 units 手前で止まる。離散化のぶんだけ内側に入る。
            Assert.That(finalDistance, Is.InRange(0.0, UniverseConstants.ArrivalRadiusUnits));
            Assert.That(ap.CommandedSpeedKmPerSec, Is.EqualTo(0.0));
        }

        // ---- ETA ----

        [Test]
        public void ETAが巡航中に正しく減る()
        {
            var ap = new AutopilotSolver();
            var ship = new AbsoluteMotion();
            ship.SetPosition(Start(7.8e7));
            ap.Engage(ship.Position, Model.Mars.AbsolutePosition, CruiseKmPerSec);
            ap.Step(ship.Position, 0.0, Dt); // Align -> Cruise

            double first = double.NaN;
            double last = double.NaN;
            for (int i = 0; i < 600; i++)
            {
                ap.Step(ship.Position, 0.0, Dt);
                Vec3d dir = (ap.TargetPosition - ship.Position).Normalized;
                ship.SetVelocity(dir * ap.CommandedSpeedKmPerSec);
                ship.Step(Dt);
                if (i == 0) first = ap.EtaSeconds;
                last = ap.EtaSeconds;
            }

            Debug.Log($"[Step3b] ETA: 開始 {first:F2} 秒 -> 10 秒後 {last:F2} 秒 (差 {first - last:F2})");

            Assert.That(first, Is.EqualTo(294.0).Within(3.0));
            Assert.That(first - last, Is.EqualTo(10.0).Within(0.2), "実時間ぶんだけ減る");
        }

        [Test]
        public void 起動していなければETAは無限大()
        {
            var ap = new AutopilotSolver();
            Assert.That(double.IsPositiveInfinity(ap.EtaSeconds), Is.True);
        }

        // ---- 実スケール引き渡し ----

        [Test]
        public void 引き渡し帯は5万から3万units()
        {
            Assert.That(RealScaleHandoff.Blend(6.0e4), Is.EqualTo(0.0));
            Assert.That(RealScaleHandoff.Blend(5.0e4), Is.EqualTo(0.0));
            Assert.That(RealScaleHandoff.Blend(4.0e4), Is.EqualTo(0.5).Within(1e-12));
            Assert.That(RealScaleHandoff.Blend(3.0e4), Is.EqualTo(1.0));
            Assert.That(RealScaleHandoff.Blend(5.0e3), Is.EqualTo(1.0));
        }

        [Test]
        public void 引き渡しはプロキシ殻の下限より十分手前で終わる()
        {
            // 火星のプロキシ下限は 6777 units。3e4 で渡し切っているので 4.4 倍の余裕。
            const double proxyLimit = 6777.0;
            Debug.Log($"[Step3b] 渡し切り {RealScaleHandoff.FadeEndDistance:E1} units / " +
                      $"プロキシ下限 {proxyLimit:E1} units / 余裕 {RealScaleHandoff.FadeEndDistance / proxyLimit:F1} 倍");
            Assert.That(RealScaleHandoff.FadeEndDistance, Is.GreaterThan(proxyLimit * 2.0));
        }

        [Test]
        public void 引き渡し帯はNearカメラのfarの内側()
        {
            // 実スケールの火星が far clip に収まること。
            double farthest = RealScaleHandoff.FadeStartDistance + SolarSystemModel.MarsRadiusKm;
            Debug.Log($"[Step3b] 実スケールの最遠 {farthest:E4} units / Near far = 1.5e5");
            Assert.That(farthest, Is.LessThan(150000.0));
        }

        [Test]
        public void 太陽と地球は引き渡し帯に入らない()
        {
            // このシナリオでは観測者は常に火星の近くにしか行かない。
            Vec3d atMars = Start(3.0e3);
            Assert.That(Model.Sun.DistanceFrom(atMars), Is.GreaterThan(RealScaleHandoff.FadeStartDistance));
            Assert.That(Model.Earth.DistanceFrom(atMars), Is.GreaterThan(RealScaleHandoff.FadeStartDistance));
        }
    }
}
