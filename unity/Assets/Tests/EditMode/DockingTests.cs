using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>ステーション配置とドッキング (Step 5)。</summary>
    public sealed class DockingTests
    {
        const double Dt = UniverseConstants.FixedDeltaSeconds;

        static readonly SolarSystemModel Model = SolarSystemModel.CreateOpposition();

        static SpaceStation EarthStation => Model.Stations[0];
        static SpaceStation MarsStation => Model.Stations[1];

        static double CruiseKmPerSec =>
            UniverseConstants.BetaToKmPerSec(UniverseConstants.DefaultCruiseBeta);

        // ---- 配置 ----

        [Test]
        public void ステーションは2基あり指定の距離にある()
        {
            Assert.That(Model.Stations.Count, Is.EqualTo(2));

            Assert.That(EarthStation.Name, Is.EqualTo("Earth Station"));
            Assert.That(EarthStation.DistanceFromCenterKm, Is.EqualTo(12000.0));
            Assert.That(MarsStation.Name, Is.EqualTo("Mars Station"));
            Assert.That(MarsStation.DistanceFromCenterKm, Is.EqualTo(8000.0));

            Debug.Log($"[Step5] 地球St 高度 {EarthStation.AltitudeKm:F1} units / " +
                      $"火星St 高度 {MarsStation.AltitudeKm:F1} units / " +
                      $"ステーション間 {Vec3d.Distance(EarthStation.AbsolutePosition, MarsStation.AbsolutePosition):E4} units");

            Assert.That(EarthStation.AltitudeKm, Is.GreaterThan(0.0), "地表より上にある");
            Assert.That(MarsStation.AltitudeKm, Is.GreaterThan(0.0));
        }

        [Test]
        public void ステーションは太陽惑星軸に垂直で同じ側にある()
        {
            Vec3d axis = (Model.Mars.AbsolutePosition - Model.Sun.AbsolutePosition).Normalized;

            double e = Vec3d.Dot(EarthStation.OffsetDirection, axis);
            double m = Vec3d.Dot(MarsStation.OffsetDirection, axis);
            double same = Vec3d.Dot(EarthStation.OffsetDirection, MarsStation.OffsetDirection);

            Debug.Log($"[Step5] オフセット・軸の内積: 地球St {e:E2} / 火星St {m:E2} / 両者の内積 {same:F4}");

            Assert.That(System.Math.Abs(e), Is.LessThan(1e-9), "軸に垂直");
            Assert.That(System.Math.Abs(m), Is.LessThan(1e-9));
            Assert.That(same, Is.EqualTo(1.0).Within(1e-12), "同じ側");
        }

        [Test]
        public void 航路が惑星を貫通しない()
        {
            Vec3d a = EarthStation.AbsolutePosition;
            Vec3d b = MarsStation.AbsolutePosition;

            foreach (CelestialBody body in Model.Bodies)
            {
                double d = DistancePointToSegment(body.AbsolutePosition, a, b);
                Debug.Log($"[Step5] 航路から {body.Name,-6} 中心まで {d:E6} units / 半径 {body.RadiusKm:E3} / 余裕 {d - body.RadiusKm:E6}");
                Assert.That(d, Is.GreaterThan(body.RadiusKm),
                    $"航路が {body.Name} を貫通している");
            }
        }

        [Test]
        public void ステーションから母天体を見ると位相角が90度()
        {
            // 明暗境界線が視界の中央に来る配置かどうか。
            foreach (SpaceStation station in Model.Stations)
            {
                Vec3d toBody = (station.Host.AbsolutePosition - station.AbsolutePosition).Normalized;
                Vec3d sunToBody = (station.Host.AbsolutePosition - Model.Sun.AbsolutePosition).Normalized;

                // 位相角 = 太陽 -> 天体 -> 観測者 のなす角。90 度なら半月。
                double phase = System.Math.Acos(System.Math.Max(-1.0, System.Math.Min(1.0,
                    Vec3d.Dot(sunToBody, toBody)))) * 180.0 / System.Math.PI;

                Debug.Log($"[Step5] {station.Name} から {station.Host.Name} の位相角 = {phase:F2} 度");
                Assert.That(phase, Is.EqualTo(90.0).Within(0.01), "半月にならない = 境界線が中央に来ない");
            }
        }

        static double DistancePointToSegment(Vec3d p, Vec3d a, Vec3d b)
        {
            Vec3d ab = b - a;
            double denom = ab.SqrMagnitude;
            double t = denom > 0.0 ? Vec3d.Dot(p - a, ab) / denom : 0.0;
            t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
            return Vec3d.Distance(a + ab * t, p);
        }

        // ---- 状態遷移 ----

        [Test]
        public void 遠くにいる間はFree()
        {
            var d = new DockingSolver();
            d.Step(1000.0, 0.0, 0.0, false, false, Dt);
            Assert.That(d.State, Is.EqualTo(DockingState.Free));
        }

        [Test]
        public void 到着圏に入るとApproachingになる()
        {
            var d = new DockingSolver();

            // 距離は満たすが速い -> まだ Free
            d.Step(10.0, 100.0, 0.0, false, false, Dt);
            Assert.That(d.State, Is.EqualTo(DockingState.Free), "速すぎるうちは入らない");

            d.Step(10.0, 0.5, 0.0, false, false, Dt);
            Assert.That(d.State, Is.EqualTo(DockingState.Approaching));
        }

        [Test]
        public void Approachingはヒステリシスで振動しない()
        {
            var d = new DockingSolver();
            d.Step(10.0, 0.5, 0.0, false, false, Dt);
            Assert.That(d.State, Is.EqualTo(DockingState.Approaching));

            // 20 と 24 の間を往復しても Approaching のまま。
            for (int i = 0; i < 100; i++)
            {
                d.Step(i % 2 == 0 ? 21.0 : 23.0, 0.5, 0.0, false, false, Dt);
                Assert.That(d.State, Is.EqualTo(DockingState.Approaching));
            }

            d.Step(25.0, 0.5, 0.0, false, false, Dt);
            Assert.That(d.State, Is.EqualTo(DockingState.Free), "24 を超えたら抜ける");
        }

        [Test]
        public void 姿勢が合わないとDockingへ進まない()
        {
            var d = new DockingSolver();
            d.Step(10.0, 0.5, 0.0, false, false, Dt);
            d.Step(10.0, 0.5, 0.0, true, false, Dt);
            Assert.That(d.State, Is.EqualTo(DockingState.DockRequested));

            // 許容角 30 度を超えている間は待つ。
            for (int i = 0; i < 100; i++)
            {
                d.Step(10.0, 0.5, 45.0, false, false, Dt);
            }

            Assert.That(d.State, Is.EqualTo(DockingState.DockRequested));

            d.Step(10.0, 0.5, DockingSolver.AttitudeToleranceDegrees, false, false, Dt);
            Assert.That(d.State, Is.EqualTo(DockingState.Docking));
        }

        [Test]
        public void 補間は5秒でDockedになる()
        {
            var d = new DockingSolver();
            d.Step(10.0, 0.5, 0.0, false, false, Dt);
            d.Step(10.0, 0.5, 0.0, true, false, Dt);
            d.Step(10.0, 0.5, 0.0, false, false, Dt);
            Assert.That(d.State, Is.EqualTo(DockingState.Docking));

            int steps = 0;
            while (d.State == DockingState.Docking && steps < 10000)
            {
                d.Step(10.0, 0.0, 0.0, false, false, Dt);
                steps++;
            }

            Debug.Log($"[Step5] Docking -> Docked に {steps * Dt:F3} 秒 ({steps} ステップ)");
            Assert.That(d.State, Is.EqualTo(DockingState.Docked));
            Assert.That(steps * Dt, Is.EqualTo(DockingSolver.DockSeconds).Within(0.05));
        }

        [Test]
        public void 状態は飛ばずに順に進む()
        {
            var d = new DockingSolver();
            var seen = new System.Collections.Generic.List<DockingState> { d.State };

            void Advance(double distance, double speed, double angle, bool req, bool und)
            {
                DockingState before = d.State;
                d.Step(distance, speed, angle, req, und, Dt);
                if (d.State != before)
                {
                    seen.Add(d.State);
                }
            }

            Advance(10.0, 0.5, 0.0, false, false); // Free -> Approaching
            Advance(10.0, 0.5, 0.0, true, false);  // -> DockRequested
            Advance(10.0, 0.5, 0.0, false, false); // -> Docking
            for (int i = 0; i < 400; i++)
            {
                Advance(10.0, 0.0, 0.0, false, false);
            }

            Advance(0.0, 0.0, 0.0, false, true);   // Docked -> Undocking
            // 離脱中は実際に距離が開く。開き切ってから Free に戻る。
            for (int i = 0; i < 400; i++)
            {
                double distance = System.Math.Min(30.0, i * 0.1);
                Advance(distance, 0.0, 0.0, false, false);
            }

            Debug.Log("[Step5] ドッキング状態遷移: " + string.Join(" -> ", seen));
            Assert.That(seen, Is.EqualTo(new[]
            {
                DockingState.Free, DockingState.Approaching, DockingState.DockRequested,
                DockingState.Docking, DockingState.Docked, DockingState.Undocking, DockingState.Free,
            }));
        }

        [Test]
        public void 補間は端で速度が0になる()
        {
            var from = new Vec3d(0.0, 0.0, 0.0);
            var to = new Vec3d(10.0, 0.0, 0.0);

            Assert.That(DockingSolver.Interpolate(from, to, 0.0).X, Is.EqualTo(0.0));
            Assert.That(DockingSolver.Interpolate(from, to, 0.5).X, Is.EqualTo(5.0).Within(1e-9));
            Assert.That(DockingSolver.Interpolate(from, to, 1.0).X, Is.EqualTo(10.0).Within(1e-9));

            double first = DockingSolver.Interpolate(from, to, 0.02).X;
            double middle = DockingSolver.Interpolate(from, to, 0.52).X
                            - DockingSolver.Interpolate(from, to, 0.50).X;
            Debug.Log($"[Step5] 補間の刻み: 端 {first:E3} / 中央 {middle:E3}");
            Assert.That(first, Is.LessThan(middle), "端のほうがゆっくり");
        }

        // ---- 通し航行 ----

        /// <summary>ステーションへ向けて飛ぶ。到着した状態と経過秒を返す。</summary>
        static (AutopilotSolver ap, AbsoluteMotion ship, double seconds)
            FlyTo(Vec3d start, SpaceStation target, int maxSteps = 400000)
        {
            var ap = new AutopilotSolver();
            var ship = new AbsoluteMotion();
            ship.SetPosition(start);
            ap.Engage(ship.Position, target.PortPosition, CruiseKmPerSec);

            int steps = 0;
            while (ap.State != AutopilotState.Arrived && steps < maxSteps)
            {
                ap.Step(ship.Position, 0.0, Dt);
                Vec3d dir = (ap.TargetPosition - ship.Position).Normalized;
                ship.SetVelocity(dir * ap.CommandedSpeedKmPerSec);
                ship.Step(Dt);
                steps++;
            }

            return (ap, ship, steps * Dt);
        }

        [Test]
        public void 地球ステーションから火星ステーションへ到着する()
        {
            (AutopilotSolver ap, AbsoluteMotion ship, double seconds) =
                FlyTo(EarthStation.PortPosition, MarsStation);

            double finalDistance = MarsStation.DistanceFrom(ship.Position);

            Debug.Log($"[Step5] 地球St -> 火星St: {seconds:F2} 秒 ({seconds / 60:F2} 分) / " +
                      $"最終距離 {finalDistance:F4} units / 最終速度 {ship.SpeedKmPerSec:F4} km/s");

            Assert.That(ap.State, Is.EqualTo(AutopilotState.Arrived));
            Assert.That(finalDistance, Is.LessThanOrEqualTo(UniverseConstants.ArrivalRadiusUnits));
            Assert.That(ship.SpeedKmPerSec, Is.LessThanOrEqualTo(UniverseConstants.ArrivalMaxSpeedKmPerSec));
        }

        [Test]
        public void 火星ステーションから地球ステーションへ戻れる()
        {
            (AutopilotSolver ap, AbsoluteMotion ship, double seconds) =
                FlyTo(MarsStation.PortPosition, EarthStation);

            double finalDistance = EarthStation.DistanceFrom(ship.Position);

            Debug.Log($"[Step5] 火星St -> 地球St: {seconds:F2} 秒 ({seconds / 60:F2} 分) / " +
                      $"最終距離 {finalDistance:F4} units / 最終速度 {ship.SpeedKmPerSec:F4} km/s");

            Assert.That(ap.State, Is.EqualTo(AutopilotState.Arrived));
            Assert.That(finalDistance, Is.LessThanOrEqualTo(UniverseConstants.ArrivalRadiusUnits));
        }

        // ---- カメラ 4 段 ----

        [Test]
        public void カメラ4段の範囲がステーションと天体を漏れなく覆う()
        {
            Debug.Log($"[Step5] Deep [{CameraStackController.DeepNearClip:E2}, {CameraStackController.DeepFarClip:E2}] / " +
                      $"Near [{CameraStackController.NearNearClip:E2}, {CameraStackController.NearFarClip:E2}] / " +
                      $"Nearfield [{CameraStackController.NearfieldNearClip:E2}, {CameraStackController.NearfieldFarClip:E2}] / " +
                      $"Cockpit [{CameraStackController.CockpitNearClip:E2}, {CameraStackController.CockpitFarClip:E2}]");

            // ステーション (半径 0.25) はドッキング時 0.3 units 先。Nearfield 段に入る。
            double dockDistance = MarsStation.PortStandoffKm;
            Assert.That(dockDistance, Is.GreaterThan(CameraStackController.NearfieldNearClip));
            Assert.That(UniverseConstants.ArrivalRadiusUnits,
                Is.LessThan(CameraStackController.NearfieldFarClip),
                "到着圏 20 units の全域で Nearfield 段に入っている");

            // ステーションから見た母天体は Near 段に入る。
            double toMars = MarsStation.DistanceFromCenterKm - SolarSystemModel.MarsRadiusKm;
            double toMarsFar = MarsStation.DistanceFromCenterKm + SolarSystemModel.MarsRadiusKm;
            Debug.Log($"[Step5] 火星St から火星: 手前 {toMars:F1} / 奥 {toMarsFar:F1} units");
            Assert.That(toMars, Is.GreaterThan(CameraStackController.NearNearClip));
            Assert.That(toMarsFar, Is.LessThan(CameraStackController.NearFarClip));

            // 地球ステーションからも同様。
            double toEarth = EarthStation.DistanceFromCenterKm - SolarSystemModel.EarthRadiusKm;
            Assert.That(toEarth, Is.GreaterThan(CameraStackController.NearNearClip));

            // 段が距離で重ならず、かつ隙間もない。
            Assert.That(CameraStackController.NearfieldFarClip,
                Is.EqualTo(CameraStackController.NearNearClip),
                "Nearfield の far と Near の near が一致 = 隙間も重なりも無い");
        }

        [Test]
        public void 実スケール引き渡しはNear段の内側で完結する()
        {
            // 引き渡し帯 5e4 -> 3e4 units は Near [100, 1.5e5] の内側。
            Assert.That(RealScaleHandoff.FadeStartDistance, Is.LessThan(CameraStackController.NearFarClip));
            Assert.That(RealScaleHandoff.FadeEndDistance, Is.GreaterThan(CameraStackController.NearNearClip));
        }
    }
}
