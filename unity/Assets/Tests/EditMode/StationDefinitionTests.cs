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

        /// <summary>
        /// **箱で組んだモデル。** 本番は Cobble になった (13-3b) ので、
        /// 箱の数表を見るテストは明示的に箱を渡す。
        /// </summary>
        static SolarSystemModel Model()
            => SolarSystemModel.CreateOpposition(StationCatalog.Box());

        /// <summary>**本番のモデル。** 既定は Cobble。</summary>
        static SolarSystemModel DefaultModel() => SolarSystemModel.CreateOpposition();

        // ---- 定義の値が現行と厳密一致 ----

        [Test]
        public void 箱の定義の数表()
        {
            //   項目            値      出所（移設前）
            //   Scale           1.0     プリミティブなので倍率なし
            //   PortStandoff    0.3     SpaceStation.PortStandoffKm = RadiusKm * 1.2 = 0.25 * 1.2
            //   RequestRange    2.0     StationCatalog.DefaultRequestRangeUnits
            //
            // **13-3b で 20.0 -> 2.0。** 要求の判定の原点を構造物の中心から
            // **ポート位置**へ揃えたので、ポートのオフセットぶんの下駄が外れた。
            // AP の到着半径 20 は据え置き（到着後に手動で寄る区間が残るのは意図）。
            StationDefinition box = Box();

            Assert.That(box.Id, Is.EqualTo(StationDefinition.BoxId));
            Assert.That(box.NeedsPrefab, Is.False, "箱はプレハブを持たない");
            Assert.That(box.Scale, Is.EqualTo(1.0));
            Assert.That(box.PortStandoff, Is.EqualTo(0.3).Within(1e-15));
            Assert.That(box.RequestRange, Is.EqualTo(2.0));

            // **移設前の式と厳密一致すること。** 片方だけ直したら落ちる。
            Assert.That(box.PortStandoff,
                        Is.EqualTo(SolarSystemModel.StationRadiusKm
                                   * StationCatalog.BoxStandoffMultiplier));
            Assert.That(box.RequestRange,
                        Is.EqualTo(StationCatalog.DefaultRequestRangeUnits));

            // **AP の到着半径とは別物になった (13-3b)。**
            Assert.That(box.RequestRange,
                        Is.Not.EqualTo(UniverseConstants.ArrivalRadiusUnits));
        }

        [Test]
        public void Cobbleの定義の数表()
        {
            // **13-3b で人間が実機で判定して確定した値。**
            //   項目             値            出所
            //   Scale            0.00800       判定（接続面 = 中央の金色の円）
            //   ModelRadius     45.0777        13-3a の実測（ピボット基準の外接球）
            //   HullAheadOfPort  0.0           13-3a の実測（z > 24.7182 の頂点 0 件）
            //   PortLocal       (0.0300, 0.2400, 24.7182)  13-3a の実測
            //   PortForward     +Z / PortUp +Y
            //   PortStandoff     0.015         MinStandoff 0.010 + 余裕 0.005
            //   RequestRange     2.0           箱と同じ。**F4 で振って決める出発点**
            StationDefinition c = StationCatalog.Cobble();

            Assert.That(c.Id, Is.EqualTo(StationCatalog.CobbleId));
            Assert.That(c.NeedsPrefab, Is.True);
            Assert.That(c.PrefabGuid, Is.EqualTo("0daf96c15d4c97b4e9e526f6acfce2f0"));
            Assert.That(c.FallbackId, Is.EqualTo(StationDefinition.BoxId));

            Assert.That(c.Scale, Is.EqualTo(0.008));
            Assert.That(c.ModelRadius, Is.EqualTo(45.0777));
            Assert.That(c.HullAheadOfPortLocal, Is.EqualTo(0.0));

            Assert.That(c.PortLocal.X, Is.EqualTo(0.0300));
            Assert.That(c.PortLocal.Y, Is.EqualTo(0.2400));
            Assert.That(c.PortLocal.Z, Is.EqualTo(24.7182));
            Assert.That(c.PortForward.Z, Is.EqualTo(1.0));
            Assert.That(c.PortUp.Y, Is.EqualTo(1.0));

            Assert.That(c.PortStandoff, Is.EqualTo(0.015).Within(1e-15));
            Assert.That(c.RequestRange, Is.EqualTo(2.0), "箱と同じ既定を使う");
        }

        [Test]
        public void Cobbleの実寸の数表()
        {
            // Scale 0.008 / 1 unit = 1 km なので 実寸 [m] = プレハブ単位 x 8。
            //   全長      62.5408 -> 500.33 m
            //   アレイ幅  41.9387 -> 335.51 m
            //   リング径  24.8580 -> 198.86 m
            //   金の円     0.5241 ->   4.193 m   船の全幅 1.6075 m の 2.61 倍
            //   半径      45.0777 ->   0.36062 units
            //   ポート     24.7182 ->  0.19775 units（中心から 197.7 m 前方）
            StationDefinition c = StationCatalog.Cobble();

            Assert.That(62.5408 * c.Scale * 1000.0, Is.EqualTo(500.33).Within(0.01));
            Assert.That(41.9387 * c.Scale * 1000.0, Is.EqualTo(335.51).Within(0.01));
            Assert.That(24.8580 * c.Scale * 1000.0, Is.EqualTo(198.86).Within(0.01));
            Assert.That(0.5241 * c.Scale * 1000.0, Is.EqualTo(4.193).Within(0.001));
            Assert.That(0.5241 * c.Scale * 1000.0 / 1.6075, Is.EqualTo(2.61).Within(0.01));

            Assert.That(c.RadiusUnits, Is.EqualTo(0.36062).Within(1e-5));
            Assert.That(c.PortLocalUnits.Z, Is.EqualTo(0.19775).Within(1e-5));
        }

        [Test]
        public void 地球と火星は同じ定義を共有する()
        {
            SolarSystemModel model = Model();

            // **同一インスタンス。** 差別化（灯の色・周期）は 13-4 の定数で行う。
            Assert.That(model.Stations[0].Definition,
                        Is.SameAs(model.Stations[1].Definition));

            // 本番（Cobble）でも共有される。
            SolarSystemModel cobble = DefaultModel();
            Assert.That(cobble.Stations[0].Definition,
                        Is.SameAs(cobble.Stations[1].Definition));
            Assert.That(cobble.Stations[0].Definition.Id,
                        Is.EqualTo(StationCatalog.CobbleId));

            foreach (SpaceStation station in model.Stations)
            {
                Assert.That(station.PortStandoffKm, Is.EqualTo(0.3).Within(1e-15), station.Name);
                Assert.That(station.RequestRangeUnits, Is.EqualTo(2.0), station.Name);
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

        [Test]
        public void Cobbleのポート位置の数表()
        {
            // ポート位置 = 中心 + 基底(PortLocal * Scale) + ポート方向 * PortStandoff
            //   PortLocal.Z 24.7182 * 0.008 = 0.1977456 units（ローカル +Z -> ワールド +Y）
            //   PortStandoff                 0.015 units
            //   合計                         0.2127456 units
            //   Earth Station  12000 -> 12000.2127456
            //   Mars  Station   8000 ->  8000.2127456
            SolarSystemModel model = DefaultModel();

            SpaceStation earth = model.Stations[0];
            Assert.That(earth.PortPosition.Y - earth.AbsolutePosition.Y,
                        Is.EqualTo(0.2127456).Within(1e-9));
            Assert.That(earth.PortPosition.Y, Is.EqualTo(12000.2127456).Within(1e-7));

            SpaceStation mars = model.Stations[1];
            Assert.That(mars.PortPosition.Y, Is.EqualTo(8000.2127456).Within(1e-7));

            // **ドッキング後の目から構造物までの隙間。**
            //   PortStandoff 0.015 - HullAheadOfPort 0 = 0.015 units = 15 m
            Assert.That(earth.Definition.DockedClearance, Is.EqualTo(0.015).Within(1e-15));
            Assert.That(earth.Definition.DockedClearance,
                        Is.GreaterThanOrEqualTo(StationCatalog.NearfieldNearClipUnits));
        }

        // ---- 解析判定の境界（移設前と厳密一致）----

        [Test]
        public void ドッキング判定の境界の数表()
        {
            // **距離は 13-3b で 20 -> 2.0 になった（原点もポート位置へ）。**
            //   距離    速さ    角度    受理されるか   理由
            //   2.0     0.5      0.0    通る            境界ちょうどは通る (<=)
            //   2.001   0.5      0.0    距離           要求可能距離の外
            //   1.0     1.0      0.0    通る            速度の境界ちょうど
            //   1.0     1.001    0.0    速度
            //   1.0     0.5     30.0    通る            角度の境界ちょうど
            //   1.0     0.5     30.001  角度
            double range = Box().RequestRange;

            Assert.That(DockingSolver.RejectionReason(2.0, 0.5, 0.0, range), Is.Empty);
            Assert.That(DockingSolver.RejectionReason(2.001, 0.5, 0.0, range),
                        Does.StartWith("距離が遠い"));

            Assert.That(DockingSolver.RejectionReason(1.0, 1.0, 0.0, range), Is.Empty);
            Assert.That(DockingSolver.RejectionReason(1.0, 1.001, 0.0, range),
                        Does.StartWith("速すぎる").Or.Not.Empty);

            Assert.That(DockingSolver.RejectionReason(
                            1.0, 0.5, DockingSolver.AttitudeToleranceDegrees, range), Is.Empty);
            Assert.That(DockingSolver.RejectionReason(
                            1.0, 0.5, DockingSolver.AttitudeToleranceDegrees + 0.001, range),
                        Does.StartWith("ポート正面から"));
        }

        [Test]
        public void ヒステリシスと出港距離の数表()
        {
            //   **要求可能距離 2.0 に対して (13-3b で 20 -> 2.0)**
            //     入る    2.0 以下
            //     抜ける  2.4 より大きい   (2.0 * LeaveMultiplier 1.2)
            //     出港    2.4             （UndockDistance）
            double range = Box().RequestRange;

            Assert.That(DockingSolver.LeaveMultiplier, Is.EqualTo(1.2));
            Assert.That(DockingSolver.UndockDistance(range), Is.EqualTo(2.4).Within(1e-12));

            var solver = new DockingSolver();
            solver.Step(2.0, 0.5, 0.0, false, false, 1.0 / 60.0, range);
            Assert.That(solver.State, Is.EqualTo(DockingState.Approaching), "2.0 で入る");

            solver.Step(2.4, 0.5, 0.0, false, false, 1.0 / 60.0, range);
            Assert.That(solver.State, Is.EqualTo(DockingState.Approaching), "2.4 では抜けない");

            solver.Step(2.401, 0.5, 0.0, false, false, 1.0 / 60.0, range);
            Assert.That(solver.State, Is.EqualTo(DockingState.Free), "2.4 を超えたら抜ける");
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
                "broken", null, default, default, default,
                Vec3d.Zero, new Vec3d(0, 1, 0), new Vec3d(0, 0, 1),
                default, default, null, null, null);

            Assert.Throws<InvalidOperationException>(() => { var _ = broken.Scale; });
            Assert.Throws<InvalidOperationException>(() => { var _ = broken.PortStandoff; });
            Assert.Throws<InvalidOperationException>(() => { var _ = broken.RequestRange; });

            // **13-3 コミット2 で足した項目も同じ塞ぎが効くこと。**
            Assert.Throws<InvalidOperationException>(() => { var _ = broken.ModelRadius; });
            Assert.Throws<InvalidOperationException>(() => { var _ = broken.RadiusUnits; });
            Assert.Throws<InvalidOperationException>(() => { var _ = broken.EffectiveScale; });
            Assert.Throws<InvalidOperationException>(() => { var _ = broken.PortLocalUnits; });
            Assert.Throws<InvalidOperationException>(() => { var _ = broken.MinStandoff(0.01); });
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
                new Vec3d(0, 1, 0), 12000.0, null));
        }

        // ---- 値を振ったら下流が動くか (Step 13-1a 追補) ----
        //
        // **箱 1 件だけでは「移設できたか」を確かめられない。**
        // 箱の値は移設前のグローバル定数と同じなので、**どこかが古い定数を読んだままでも
        // 値が一致してテストが通ってしまう。** 「未設定なら例外」は読む経路があることしか
        // 示しておらず、**その値が下流まで届いていること**は別（§0-A）。
        //
        // そこで箱と違う値の定義を作り、判定と派生値が追随することを見る。
        // **箱の値の整数倍を避ける**（偶然の一致で通らないように）。
        //   PortStandoff  0.47   (箱 0.30)
        //   RequestRange 37.00   (箱 20.00)
        //   Scale         2.50   (箱 1.00)

        const double TestStandoff = 0.47;
        const double TestRange = 37.0;
        const double TestScale = 2.5;

        /// <summary>
        /// 試験用の定義。**`StationCatalog` の一覧には入れない**
        /// （実行時のカタログを汚さないため、ここでだけ作る）。
        /// </summary>
        static StationDefinition Varied() => StationDefinition.Create(
            "test-varied", null,
            RequiredDouble.Positive(TestScale),
            RequiredDouble.Positive(SolarSystemModel.StationRadiusKm),
            RequiredDouble.Positive(SolarSystemModel.StationRadiusKm),
            Vec3d.Zero, new Vec3d(0.0, 1.0, 0.0), new Vec3d(0.0, 0.0, 1.0),
            RequiredDouble.Positive(TestStandoff),
            RequiredDouble.Positive(TestRange),
            new NavLight[0], new WindowEmissive[0], StationDefinition.BoxId);

        [Test]
        public void 値を振ると判定の境界が追随する()
        {
            //   定義          通る    拒否      出港距離
            //   箱   20.0     20.0    20.001    24.0    （既存の数表）
            //   試験 37.0     37.0    37.001    44.4
            double range = Varied().RequestRange;
            Assert.That(range, Is.EqualTo(TestRange), "定義から読めていない");

            Assert.That(DockingSolver.RejectionReason(37.0, 0.5, 0.0, range), Is.Empty);
            Assert.That(DockingSolver.RejectionReason(37.001, 0.5, 0.0, range),
                        Does.StartWith("距離が遠い"));

            // **箱の値なら通ってしまう距離が、ここでは通ること。**
            // 古い定数 (20) を読んでいたら 25.0 は拒否されて落ちる。
            Assert.That(DockingSolver.RejectionReason(25.0, 0.5, 0.0, range), Is.Empty,
                        "**25.0 が拒否された = どこかが箱の 20 を読んでいる**");

            // 拒否の文言にも定義の値が出ること。
            Assert.That(DockingSolver.RejectionReason(50.0, 0.5, 0.0, range),
                        Does.Contain("37"), "文言が古い値を出している");
        }

        [Test]
        public void 値を振ると派生値が追随する()
        {
            //   RequestRange   UndockDistance = range * 1.2
            //   20.0           24.0   （箱 / 既存の数表）
            //   37.0           44.4
            Assert.That(DockingSolver.UndockDistance(TestRange), Is.EqualTo(44.4).Within(1e-12));

            // 比が箱と同じであること（式ごと差し替わっていないこと）。
            Assert.That(DockingSolver.UndockDistance(TestRange) / TestRange,
                        Is.EqualTo(DockingSolver.UndockDistance(20.0) / 20.0).Within(1e-12));

            var solver = new DockingSolver();
            solver.Step(37.0, 0.5, 0.0, false, false, 1.0 / 60.0, TestRange);
            Assert.That(solver.State, Is.EqualTo(DockingState.Approaching), "37.0 で入る");

            solver.Step(44.4, 0.5, 0.0, false, false, 1.0 / 60.0, TestRange);
            Assert.That(solver.State, Is.EqualTo(DockingState.Approaching), "44.4 では抜けない");

            solver.Step(44.401, 0.5, 0.0, false, false, 1.0 / 60.0, TestRange);
            Assert.That(solver.State, Is.EqualTo(DockingState.Free), "44.4 を超えたら抜ける");
        }

        [Test]
        public void 値を振るとポート位置が追随する()
        {
            // ポート面 = 中心 + ポート方向 * PortStandoff
            //   箱   0.30 -> 中心 +Y に 0.30   （既存の数表: 12000.3）
            //   試験 0.47 -> 中心 +Y に 0.47
            SolarSystemModel model = Model();
            var station = new SpaceStation(
                "test", model.Earth, new Vec3d(0.0, 1.0, 0.0),
                SolarSystemModel.EarthStationDistanceKm, Varied());

            Assert.That(station.PortStandoffKm, Is.EqualTo(TestStandoff));
            Assert.That(station.PortPosition.Y - station.AbsolutePosition.Y,
                        Is.EqualTo(TestStandoff).Within(1e-9),
                        "**ポート面が定義に追随していない**");
            Assert.That(station.RequestRangeUnits, Is.EqualTo(TestRange));

            // 出港先の距離（ShipRig が組み立てる式と同じ形）。
            //   箱   0.30 + 24.0 = 24.30
            //   試験 0.47 + 44.4 = 44.87
            double away = station.PortStandoffKm
                          + DockingSolver.UndockDistance(station.RequestRangeUnits);
            Assert.That(away, Is.EqualTo(44.87).Within(1e-9));
        }

        // ---- Scale の追随 (Step 13-3 コミット2) ----
        //
        // 13-1a の「Scale にはまだ下流の読み手が無い」を置き換えたもの。
        // **Scale を振ったら半径・ポート位置・MinStandoff が追随することを数表で縛る。**

        [Test]
        public void Scaleを振ると半径が追随する()
        {
            //   定義   ModelRadius   Scale   RadiusUnits
            //   箱     0.25          1.00    0.25      （現行と厳密一致）
            //   試験   0.25          2.50    0.625
            Assert.That(Box().ModelRadius, Is.EqualTo(0.25));
            Assert.That(Box().RadiusUnits, Is.EqualTo(0.25),
                        "**箱の半径が移設前と違う**");

            Assert.That(Varied().ModelRadius, Is.EqualTo(0.25));
            Assert.That(Varied().RadiusUnits, Is.EqualTo(0.625).Within(1e-12),
                        "**Scale が半径に効いていない**");

            // **定数として焼かれていないこと。** 焼かれていたら箱と同じ 0.25 になる。
            Assert.That(Varied().RadiusUnits, Is.Not.EqualTo(Box().RadiusUnits));
        }

        [Test]
        public void Scaleを振るとMinStandoffが追随する()
        {
            //   半径は Scale を掛けた後の値から出す。
            //   定義   RadiusUnits   nearClip   MinStandoff
            //   箱     0.25          0.01       0.26      （既存の数表と一致）
            //   試験   0.625         0.01       0.635
            const double nearClip = 0.01;

            Assert.That(Box().MinStandoff(nearClip), Is.EqualTo(0.26).Within(1e-12));
            Assert.That(Varied().MinStandoff(nearClip), Is.EqualTo(0.635).Within(1e-12));

            // 既存の自由関数（半径 + nearClip）と一致すること。式が二重にならないこと。
            Assert.That(Box().MinStandoff(nearClip),
                        Is.EqualTo(MinStandoff(Box().RadiusUnits, nearClip)).Within(1e-15));
        }

        [Test]
        public void Scaleを振るとポート位置が追随する()
        {
            // ポート位置 = 中心 + 基底(PortLocal * Scale) + ポート方向 * PortStandoff
            //
            //   PortLocal        Scale   基底で写した後      ポート面までの寄与
            //   (0, 0, 0)        1.00    (0, 0, 0)          0        （箱＝現行）
            //   (0, 0, 2)        1.00    ポート方向 * 2     +2.0
            //   (0, 0, 2)        2.50    ポート方向 * 5     +5.0
            //
            // **ローカル +Z がポート方向**（`StationBasis` の規約）。
            SolarSystemModel model = Model();
            var portDirection = new Vec3d(0.0, 1.0, 0.0);

            StationDefinition offset1 = WithPortLocal(new Vec3d(0.0, 0.0, 2.0), 1.0);
            StationDefinition offset25 = WithPortLocal(new Vec3d(0.0, 0.0, 2.0), 2.5);

            Assert.That(offset1.PortLocalUnits.Z, Is.EqualTo(2.0).Within(1e-12));
            Assert.That(offset25.PortLocalUnits.Z, Is.EqualTo(5.0).Within(1e-12),
                        "**Scale がポート位置に効いていない**");

            var s1 = new SpaceStation("t1", model.Earth, portDirection,
                                      SolarSystemModel.EarthStationDistanceKm, offset1);
            var s25 = new SpaceStation("t25", model.Earth, portDirection,
                                       SolarSystemModel.EarthStationDistanceKm, offset25);

            // ローカル +Z はポート方向（+Y）へ写るので、Y に出る。
            Assert.That(s1.PortPosition.Y - s1.AbsolutePosition.Y,
                        Is.EqualTo(2.0 + TestStandoff).Within(1e-9));
            Assert.That(s25.PortPosition.Y - s25.AbsolutePosition.Y,
                        Is.EqualTo(5.0 + TestStandoff).Within(1e-9));
        }

        [Test]
        public void 箱はPortLocalが0なので移設前と厳密に同じ()
        {
            // **この回の成立条件。** ポート位置の式を変えたが、箱では値が動かない。
            SolarSystemModel model = Model();

            foreach (SpaceStation station in model.Stations)
            {
                Assert.That(station.Definition.PortLocal, Is.EqualTo(Vec3d.Zero));
                Assert.That(station.PortPosition.Y - station.AbsolutePosition.Y,
                            Is.EqualTo(0.3).Within(1e-9), station.Name);
                Assert.That(station.RadiusKm, Is.EqualTo(0.25), station.Name);
            }
        }

        [Test]
        public void F4の倍率は描画と判定の両方に効く()
        {
            // **F4 は `EffectiveScale` を書き換える。** 半径・ポート位置・MinStandoff は
            // すべてそこから出るので、1 箇所を振ると全部動く。
            //
            //   倍率   EffectiveScale   RadiusUnits   MinStandoff(0.01)
            //   1.00   1.00             0.25          0.26
            //   2.00   2.00             0.50          0.51
            //   0.50   0.50             0.125         0.135
            StationDefinition box = Box();
            Assert.That(box.HasRuntimeScale, Is.False, "最初から振られている");

            box.SetRuntimeScale(box.Scale * 2.0);
            Assert.That(box.EffectiveScale, Is.EqualTo(2.0));
            Assert.That(box.RadiusUnits, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(box.MinStandoff(0.01), Is.EqualTo(0.51).Within(1e-12));
            Assert.That(box.HasRuntimeScale, Is.True);

            box.SetRuntimeScale(box.Scale * 0.5);
            Assert.That(box.RadiusUnits, Is.EqualTo(0.125).Within(1e-12));

            box.ResetRuntimeScale();
            Assert.That(box.HasRuntimeScale, Is.False);
            Assert.That(box.EffectiveScale, Is.EqualTo(1.0), "**R で既定へ戻らない**");
            Assert.That(box.RadiusUnits, Is.EqualTo(0.25));
        }

        [Test]
        public void 実行時の倍率は0以下を受け付けない()
        {
            StationDefinition box = Box();
            Assert.Throws<ArgumentOutOfRangeException>(() => box.SetRuntimeScale(0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => box.SetRuntimeScale(-1.0));
            Assert.That(box.HasRuntimeScale, Is.False, "失敗した代入が残っている");
        }

        /// <summary>試験用: ポート位置と倍率だけを振った定義。</summary>
        static StationDefinition WithPortLocal(Vec3d portLocal, double scale)
            => StationDefinition.Create(
                "test-port", null,
                RequiredDouble.Positive(scale),
                RequiredDouble.Positive(SolarSystemModel.StationRadiusKm),
                RequiredDouble.Positive(SolarSystemModel.StationRadiusKm),
                portLocal, new Vec3d(0.0, 0.0, 1.0), new Vec3d(0.0, 1.0, 0.0),
                RequiredDouble.Positive(TestStandoff),
                RequiredDouble.Positive(TestRange),
                new NavLight[0], new WindowEmissive[0], StationDefinition.BoxId);

        [Test]
        public void 試験用の定義はカタログに入っていない()
        {
            // **実行時のカタログを汚さない。** 試験用はテストの中だけで作る。
            Assert.That(Box().Id, Is.EqualTo(StationDefinition.BoxId));
            Assert.That(Varied().Id, Is.Not.EqualTo(Box().Id));

            // **本番のモデルはカタログの定義しか使わない。**
            // 試験用（test-varied / test-port）が紛れ込んでいたらここで落ちる。
            foreach (SpaceStation station in DefaultModel().Stations)
            {
                Assert.That(station.Definition.Id,
                            Is.EqualTo(StationCatalog.CobbleId),
                            station.Name + " が本番の定義を使っていない");
            }

            foreach (SpaceStation station in Model().Stations)
            {
                Assert.That(station.Definition.Id, Is.EqualTo(StationDefinition.BoxId),
                            station.Name + " が箱以外の定義を使っている");
            }
        }

        // ---- 移設していない値（範囲の記録）----

        [Test]
        public void オートパイロットの到着半径は要求可能距離とは別物()
        {
            // **13-3b で切り離した。**
            // それまで AP の到着半径（20 / 決定 D-10）と箱の要求可能距離は
            // 同値で、要求可能距離は AP の値を流用していただけだった。
            //
            //   AP の到着半径    20.0 units   原点は **ポート位置**（据え置き）
            //   要求可能距離      2.0 units   原点は **ポート位置**（13-3b で揃えた）
            //
            // **AP が止まるのは要求可能距離の外。** 到着後に手動で寄る区間が
            // 残るのは意図した設計（計画書「到着後にひと呼吸ある形を残す」）。
            Assert.That(UniverseConstants.ArrivalRadiusUnits, Is.EqualTo(20.0));
            Assert.That(StationCatalog.DefaultRequestRangeUnits, Is.EqualTo(2.0));

            Assert.That(StationCatalog.Box().RequestRange,
                        Is.LessThan(UniverseConstants.ArrivalRadiusUnits),
                        "**AP の到着半径が要求可能距離の内側にある。**"
                        + " 手動で寄る区間が消えている");

            // 手動で詰める距離 = 到着半径 - 要求可能距離。
            Assert.That(UniverseConstants.ArrivalRadiusUnits
                        - StationCatalog.DefaultRequestRangeUnits,
                        Is.EqualTo(18.0).Within(1e-12));
        }
    }
}
