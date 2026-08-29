using System;
using NUnit.Framework;
using SolarSystem.Core;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// **StationDefinition の数表 (Step 13-1a)。**
    ///
    /// この回の成立条件は**絵と挙動を一切変えないこと。**
    /// 定数を定義側へ移しただけで、値が 1 つでも動いたら移設が失敗している。
    /// **既定値で現行の数値と厳密一致することをここで縛る。**
    /// </summary>
    public sealed class StationDefinitionTests
    {
        static StationDefinition Box() => StationCatalog.Box();

        static SolarSystemModel Model() => SolarSystemModel.CreateOpposition();

        // ---- 定義の値が現行と厳密一致 ----

        [Test]
        public void 箱の定義の数表()
        {
            //   項目            値      出所（移設前）
            //   Scale           1.0     プリミティブなので倍率なし
            //   PortStandoff    0.3     SpaceStation.PortStandoffKm = RadiusKm * 1.2 = 0.25 * 1.2
            //   RequestRange   20.0     UniverseConstants.ArrivalRadiusUnits（決定 D-10）
            StationDefinition box = Box();

            Assert.That(box.Id, Is.EqualTo(StationDefinition.BoxId));
            Assert.That(box.NeedsPrefab, Is.False, "箱はプレハブを持たない");
            Assert.That(box.Scale, Is.EqualTo(1.0));
            Assert.That(box.PortStandoff, Is.EqualTo(0.3).Within(1e-15));
            Assert.That(box.RequestRange, Is.EqualTo(20.0));

            // **移設前の式と厳密一致すること。** 片方だけ直したら落ちる。
            Assert.That(box.PortStandoff,
                        Is.EqualTo(SolarSystemModel.StationRadiusKm
                                   * StationCatalog.BoxStandoffMultiplier));
            Assert.That(box.RequestRange, Is.EqualTo(UniverseConstants.ArrivalRadiusUnits));
        }

        [Test]
        public void 地球と火星は同じ定義を共有する()
        {
            SolarSystemModel model = Model();

            // **同一インスタンス。** 差別化（灯の色・周期）は 13-4 の定数で行う。
            Assert.That(model.Stations[0].Definition,
                        Is.SameAs(model.Stations[1].Definition));

            foreach (SpaceStation station in model.Stations)
            {
                Assert.That(station.PortStandoffKm, Is.EqualTo(0.3).Within(1e-15), station.Name);
                Assert.That(station.RequestRangeUnits, Is.EqualTo(20.0), station.Name);
            }
        }

        [Test]
        public void ポート位置が移設前と一致する()
        {
            //   ステーション    中心 [units]                    ポート面 [units]
            //   Earth Station   (1.495978707e8, 12000, 0)       (1.495978707e8, 12000.3, 0)
            //   Mars Station    (2.275978707e8,  8000, 0)       (2.275978707e8,  8000.3, 0)
            SolarSystemModel model = Model();

            SpaceStation earth = model.Stations[0];
            Assert.That(earth.AbsolutePosition.Y, Is.EqualTo(12000.0).Within(1e-9));
            Assert.That(earth.PortPosition.Y, Is.EqualTo(12000.3).Within(1e-9));
            Assert.That(earth.PortPosition.X, Is.EqualTo(149597870.7).Within(1e-6));

            SpaceStation mars = model.Stations[1];
            Assert.That(mars.AbsolutePosition.Y, Is.EqualTo(8000.0).Within(1e-9));
            Assert.That(mars.PortPosition.Y, Is.EqualTo(8000.3).Within(1e-9));
            Assert.That(mars.PortPosition.X, Is.EqualTo(227597870.7).Within(1e-6));
        }

        // ---- 解析判定の境界（移設前と厳密一致）----

        [Test]
        public void ドッキング判定の境界の数表()
        {
            //   距離    速さ    角度    受理されるか   理由
            //   20.0    0.5      0.0    通る            境界ちょうどは通る (<=)
            //   20.001  0.5      0.0    距離           要求可能距離の外
            //   10.0    1.0      0.0    通る            速度の境界ちょうど
            //   10.0    1.001    0.0    速度
            //   10.0    0.5     30.0    通る            角度の境界ちょうど
            //   10.0    0.5     30.001  角度
            double range = Box().RequestRange;

            Assert.That(DockingSolver.RejectionReason(20.0, 0.5, 0.0, range), Is.Empty);
            Assert.That(DockingSolver.RejectionReason(20.001, 0.5, 0.0, range),
                        Does.StartWith("距離が遠い"));

            Assert.That(DockingSolver.RejectionReason(10.0, 1.0, 0.0, range), Is.Empty);
            Assert.That(DockingSolver.RejectionReason(10.0, 1.001, 0.0, range),
                        Does.StartWith("速すぎる").Or.Not.Empty);

            Assert.That(DockingSolver.RejectionReason(
                            10.0, 0.5, DockingSolver.AttitudeToleranceDegrees, range), Is.Empty);
            Assert.That(DockingSolver.RejectionReason(
                            10.0, 0.5, DockingSolver.AttitudeToleranceDegrees + 0.001, range),
                        Does.StartWith("ポート正面から"));
        }

        [Test]
        public void ヒステリシスと出港距離の数表()
        {
            //   要求可能距離 20 に対して
            //     入る    20.0 以下
            //     抜ける  24.0 より大きい   (20 * LeaveMultiplier 1.2)
            //     出港    24.0             （UndockDistance）
            double range = Box().RequestRange;

            Assert.That(DockingSolver.LeaveMultiplier, Is.EqualTo(1.2));
            Assert.That(DockingSolver.UndockDistance(range), Is.EqualTo(24.0).Within(1e-12));

            var solver = new DockingSolver();
            solver.Step(20.0, 0.5, 0.0, false, false, 1.0 / 60.0, range);
            Assert.That(solver.State, Is.EqualTo(DockingState.Approaching), "20.0 で入る");

            solver.Step(24.0, 0.5, 0.0, false, false, 1.0 / 60.0, range);
            Assert.That(solver.State, Is.EqualTo(DockingState.Approaching), "24.0 では抜けない");

            solver.Step(24.001, 0.5, 0.0, false, false, 1.0 / 60.0, range);
            Assert.That(solver.State, Is.EqualTo(DockingState.Free), "24.0 を超えたら抜ける");
        }

        [Test]
        public void 補間の軌跡の数表()
        {
            //   progress   smoothstep = t*t*(3-2t)
            //   0.00       0.00000
            //   0.25       0.15625
            //   0.50       0.50000
            //   0.75       0.84375
            //   1.00       1.00000
            var from = new Vec3d(0.0, 0.0, 0.0);
            var to = new Vec3d(100.0, 0.0, 0.0);

            Assert.That(DockingSolver.Interpolate(from, to, 0.00).X, Is.EqualTo(0.0).Within(1e-12));
            Assert.That(DockingSolver.Interpolate(from, to, 0.25).X, Is.EqualTo(15.625).Within(1e-12));
            Assert.That(DockingSolver.Interpolate(from, to, 0.50).X, Is.EqualTo(50.0).Within(1e-12));
            Assert.That(DockingSolver.Interpolate(from, to, 0.75).X, Is.EqualTo(84.375).Within(1e-12));
            Assert.That(DockingSolver.Interpolate(from, to, 1.00).X, Is.EqualTo(100.0).Within(1e-12));
        }

        // ---- PortStandoff の下限 ----

        [Test]
        public void PortStandoffの下限の数表()
        {
            // **ドッキング後のカメラ〜構造物の距離が Nearfield の near clip を
            // 下回ってはいけない。** 下回ると構造物が near の内側に入って消える。
            //
            //   Nearfield の near clip   0.01 units = 10 m
            //   ドッキング後の位置       ポート面（= 中心から PortStandoff）
            //   構造物の外形             中心から半径 RadiusKm
            //   カメラ〜構造物           PortStandoff - RadiusKm
            //
            //   半径     Standoff   カメラ〜構造物   下限を満たすか
            //   0.25     0.30       0.05             満たす（現行）
            //   0.25     0.26       0.01             ちょうど下限
            //   0.25     0.255      0.005            **下回る**
            //   0.0545   0.0645     0.01             ちょうど下限（ISS 109m 級）
            const double nearfieldNearClip = 0.01;

            Assert.That(MinStandoff(0.25, nearfieldNearClip), Is.EqualTo(0.26).Within(1e-12));
            Assert.That(MinStandoff(0.0545, nearfieldNearClip), Is.EqualTo(0.0645).Within(1e-12));

            // 現行の箱は下限を満たす。
            double clearance = Box().PortStandoff - SolarSystemModel.StationRadiusKm;
            Assert.That(clearance, Is.EqualTo(0.05).Within(1e-15));
            Assert.That(clearance, Is.GreaterThanOrEqualTo(nearfieldNearClip),
                        "**ドッキング後に構造物が near clip の内側に入る**");
        }

        /// <summary>半径 r の構造物で near clip を満たす最小の Standoff。</summary>
        static double MinStandoff(double radius, double nearClip) => radius + nearClip;

        // ---- 未使用の項目 ----

        [Test]
        public void NavLightsとWindowEmissivesはまだ空()
        {
            // **13-4 で使う。** 型と読む経路だけ用意してあることの記録。
            // 中身が入ったらこのテストが落ちて、使い始めたことに気づける。
            Assert.That(Box().NavLights, Is.Empty, "NavLights はまだ使っていない");
            Assert.That(Box().WindowEmissives, Is.Empty, "WindowEmissives はまだ使っていない");
        }

        // ---- 書き忘れの塞ぎ ----

        [Test]
        public void 未設定の数値は読んだ時点で例外になる()
        {
            // **`default` が 0 や既定値として静かに成立しないこと。** これが対策の本体。
            var unset = default(RequiredDouble);
            Assert.That(unset.IsSet, Is.False);
            Assert.Throws<InvalidOperationException>(() => { var _ = unset.Value; });

            // 定義に書き忘れても、**読んだ時点で**落ちる。
            StationDefinition broken = StationDefinition.Create(
                "broken", null, default,
                Vec3d.Zero, new Vec3d(0, 1, 0), new Vec3d(0, 0, 1),
                default, default, null, null, null);

            Assert.Throws<InvalidOperationException>(() => { var _ = broken.Scale; });
            Assert.Throws<InvalidOperationException>(() => { var _ = broken.PortStandoff; });
            Assert.Throws<InvalidOperationException>(() => { var _ = broken.RequestRange; });
        }

        [Test]
        public void 正の数以外は作れない()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RequiredDouble.Positive(0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RequiredDouble.Positive(-1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RequiredDouble.Positive(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RequiredDouble.Positive(double.PositiveInfinity));
        }

        [Test]
        public void 定義が無いステーションは作れない()
        {
            // **読む経路が無ければ塞ぎにならない。** 定義そのものを必須にする。
            Assert.Throws<ArgumentNullException>(() => new SpaceStation(
                "no definition", SolarSystemModel.CreateOpposition().Earth,
                new Vec3d(0, 1, 0), 12000.0, 0.25, null));
        }

        // ---- 移設していない値（範囲の記録）----

        [Test]
        public void オートパイロットの到着半径は移設していない()
        {
            // **計画書 13-1a の対象は「解析判定・補間・セーブ」。**
            // オートパイロットの到着プロファイルはそこに含まれないので、
            // `UniverseConstants.ArrivalRadiusUnits` のまま残してある。
            // **箱の定義の値と同値なので、現行の挙動は変わらない。**
            // 定義ごとに変えたくなったらここが食い違い、このテストが落ちる。
            Assert.That(UniverseConstants.ArrivalRadiusUnits,
                        Is.EqualTo(StationCatalog.Box().RequestRange),
                        "AP の到着半径と箱の要求可能距離が食い違っている");
        }
    }
}
