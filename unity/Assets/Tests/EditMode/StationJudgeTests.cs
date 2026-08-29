using System;
using NUnit.Framework;
using SolarSystem.Core;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// **判定ビューが出す数の数表 (Step 13-3b)。**
    ///
    /// ここは**道具の目盛**であって決定ではない。接続面をどれと見るか、Scale を
    /// いくつにするかは人間が絵を見て決める。**このテストは「HUD に出る数が
    /// 13-3a の実測から正しく導けているか」だけを縛る。**
    ///
    /// 角直径と被覆の式は、箱（半径 0.25 / D 0.5）で 53.13 度 / 33.13 % を
    /// 再現することで裏を取ってある（下の対照）。
    /// </summary>
    public sealed class StationJudgeTests
    {
        // ---- 13-3a の実測がそのまま入っているか ----

        [Test]
        public void 候補の寸法が13a3の実測と一致する()
        {
            //   候補                  出所              プレハブ単位 [m]
            //   (a) 中央の金色の円    絵の測定          0.5241
            //   (b) 突き出した円盤    幾何 z=24.7182    0.9456（Y は 0.8208）
            //   (c) module1 の胴      幾何 z≈24.34      1.6888
            Assert.That(StationJudge.CandidateMeters(PortFaceCandidate.GoldDisc),
                        Is.EqualTo(0.5241));
            Assert.That(StationJudge.CandidateMeters(PortFaceCandidate.ProtrudingPlate),
                        Is.EqualTo(0.9456));
            Assert.That(StationJudge.CandidateMeters(PortFaceCandidate.ModuleBody),
                        Is.EqualTo(1.6888));

            Assert.That(StationJudge.ProtrudingPlateMetersY, Is.EqualTo(0.8208),
                        "**円盤は X と Y で寸法が違う。** 片方だけを開口と呼ばない");

            Assert.That(StationJudge.PortFaceLocal.X, Is.EqualTo(0.0300));
            Assert.That(StationJudge.PortFaceLocal.Y, Is.EqualTo(0.2400));
            Assert.That(StationJudge.PortFaceLocal.Z, Is.EqualTo(24.7182));
        }

        [Test]
        public void 船の寸法がDemo3の実測と一致する()
        {
            Assert.That(StationJudge.ShipWidthMeters, Is.EqualTo(1.6075));
            Assert.That(StationJudge.ShipHeightMeters, Is.EqualTo(1.6312));
        }

        // ---- 換算 ----

        [Test]
        public void プレハブ単位からメートルへの換算()
        {
            // **実寸 [m] = プレハブ単位 x Scale x 1000**（1 unit = 1 km）。
            //   Scale 0.001 -> 等倍（1 プレハブ m = 1 m）
            //   Scale 0.002 -> 2 倍
            Assert.That(StationJudge.ToMeters(1.0, 0.001), Is.EqualTo(1.0).Within(1e-12));
            Assert.That(StationJudge.ToMeters(1.0, 0.002), Is.EqualTo(2.0).Within(1e-12));
            Assert.That(StationJudge.ToUnits(1000.0, 0.001), Is.EqualTo(1.0).Within(1e-12));
        }

        [Test]
        public void 候補の実寸と船幅比の数表()
        {
            //   Scale 0.002 のとき
            //   候補   実寸 [m]   船幅比    物差し 1.5〜3.0 に入るか
            //   (a)    1.0482     0.6521    入らない
            //   (b)    1.8912     1.1765    入らない
            //   (c)    3.3776     2.1012    **入る**
            const double s = 0.002;

            Assert.That(StationJudge.OpeningMeters(PortFaceCandidate.GoldDisc, s),
                        Is.EqualTo(1.0482).Within(1e-9));
            Assert.That(StationJudge.OpeningMeters(PortFaceCandidate.ProtrudingPlate, s),
                        Is.EqualTo(1.8912).Within(1e-9));
            Assert.That(StationJudge.OpeningMeters(PortFaceCandidate.ModuleBody, s),
                        Is.EqualTo(3.3776).Within(1e-9));

            Assert.That(StationJudge.RatioToShipWidth(PortFaceCandidate.GoldDisc, s),
                        Is.EqualTo(0.6521).Within(1e-4));
            Assert.That(StationJudge.RatioToShipWidth(PortFaceCandidate.ProtrudingPlate, s),
                        Is.EqualTo(1.1765).Within(1e-4));
            Assert.That(StationJudge.RatioToShipWidth(PortFaceCandidate.ModuleBody, s),
                        Is.EqualTo(2.1012).Within(1e-4));

            Assert.That(StationJudge.WithinRule(PortFaceCandidate.GoldDisc, s), Is.False);
            Assert.That(StationJudge.WithinRule(PortFaceCandidate.ProtrudingPlate, s), Is.False);
            Assert.That(StationJudge.WithinRule(PortFaceCandidate.ModuleBody, s), Is.True);
        }

        [Test]
        public void 物差しを満たすScaleの範囲の数表()
        {
            //   候補   Scale の下限   上限
            //   (a)    0.0046007      0.0092015
            //   (b)    0.0025500      0.0050999
            //   (c)    0.0014278      0.0028556
            StationJudge.ScaleRangeFor(PortFaceCandidate.GoldDisc, out double lo, out double hi);
            Assert.That(lo, Is.EqualTo(0.0046007).Within(1e-7));
            Assert.That(hi, Is.EqualTo(0.0092015).Within(1e-7));

            StationJudge.ScaleRangeFor(PortFaceCandidate.ProtrudingPlate, out lo, out hi);
            Assert.That(lo, Is.EqualTo(0.0025500).Within(1e-7));
            Assert.That(hi, Is.EqualTo(0.0050999).Within(1e-7));

            StationJudge.ScaleRangeFor(PortFaceCandidate.ModuleBody, out lo, out hi);
            Assert.That(lo, Is.EqualTo(0.0014278).Within(1e-7));
            Assert.That(hi, Is.EqualTo(0.0028556).Within(1e-7));

            // 範囲の端では物差しをちょうど満たす。
            Assert.That(StationJudge.RatioToShipWidth(PortFaceCandidate.ModuleBody, lo),
                        Is.EqualTo(StationJudge.RatioMin).Within(1e-9));
            Assert.That(StationJudge.RatioToShipWidth(PortFaceCandidate.ModuleBody, hi),
                        Is.EqualTo(StationJudge.RatioMax).Within(1e-9));
        }

        // ---- 半径と MinStandoff ----

        [Test]
        public void 半径とMinStandoffの数表()
        {
            //   Scale    半径 [units]   MinStandoff = 半径 + 0.01
            //   0.001    0.0450777      0.0550777
            //   0.002    0.0901554      0.1001554
            //   0.004    0.1803108      0.1903108
            //   0.008    0.3606216      0.3706216
            Assert.That(StationJudge.RadiusUnits(0.001), Is.EqualTo(0.0450777).Within(1e-12));
            Assert.That(StationJudge.RadiusUnits(0.002), Is.EqualTo(0.0901554).Within(1e-12));
            Assert.That(StationJudge.RadiusUnits(0.008), Is.EqualTo(0.3606216).Within(1e-12));

            Assert.That(StationJudge.MinStandoffUnits(0.002), Is.EqualTo(0.1001554).Within(1e-12));

            // **これは 13-1a から引き継いだ球の仮定の式。** 次のコミットで
            // 実際の幾何へ置き換える（§0 の宿題）。
            Assert.That(StationJudge.MinStandoffUnits(0.002)
                        - StationJudge.RadiusUnits(0.002),
                        Is.EqualTo(StationJudge.NearfieldNearClipUnits).Within(1e-12));
        }

        [Test]
        public void 暫定の停止距離はnearclipの外にある()
        {
            // **暫定値。** 実際の PortStandoff は次のコミットで導出する。
            Assert.That(StationJudge.ProvisionalStandoffUnits,
                        Is.GreaterThan(StationJudge.NearfieldNearClipUnits),
                        "**near clip の内側に入っている**");
            Assert.That(StationJudge.ProvisionalStandoffUnits, Is.EqualTo(0.015));
        }

        // ---- 角直径と被覆 ----

        [Test]
        public void 角直径と被覆の式が箱の値を再現する()
        {
            // **式の裏取り。** 箱（半径 0.25 units / D 0.5 units）は
            // 53.13 度 / 33.13 %（1920x1080 / 画角 60 度）。
            //   角直径 = 2 * atan(R / D)
            //   被覆   = pi * (f * tan(角直径/2))^2 / (W * H),  f = (H/2) / tan(FOV/2)
            //
            // 半径 0.25 になる Scale を逆算して同じ関数へ通す。
            double scaleForBox = 0.25 / StationJudge.StationPivotRadiusMeters;

            Assert.That(StationJudge.RadiusUnits(scaleForBox), Is.EqualTo(0.25).Within(1e-12));
            Assert.That(StationJudge.AngularDiameterDegrees(scaleForBox, 0.5),
                        Is.EqualTo(53.1301).Within(1e-3));
            Assert.That(StationJudge.CoveragePercent(scaleForBox, 0.5),
                        Is.EqualTo(33.1340).Within(1e-3));
        }

        [Test]
        public void station_closeでの角直径と被覆の数表()
        {
            // **D = 0.5 units / 1920x1080 / 画角 60 度。**
            // 条件を書かない数字は比較に使えない（§0 の運用メモ）。
            //   Scale    角直径 [度]   被覆 [%]
            //   0.001    10.3032        1.0773
            //   0.002    20.4424        4.3090
            //   0.004    39.6608       17.2360
            //   0.008    71.6015       68.9441
            const double d = 0.5;

            Assert.That(StationJudge.AngularDiameterDegrees(0.001, d),
                        Is.EqualTo(10.3032).Within(1e-3));
            Assert.That(StationJudge.AngularDiameterDegrees(0.002, d),
                        Is.EqualTo(20.4424).Within(1e-3));
            Assert.That(StationJudge.AngularDiameterDegrees(0.004, d),
                        Is.EqualTo(39.6608).Within(1e-3));
            Assert.That(StationJudge.AngularDiameterDegrees(0.008, d),
                        Is.EqualTo(71.6015).Within(1e-3));

            Assert.That(StationJudge.CoveragePercent(0.001, d), Is.EqualTo(1.0773).Within(1e-3));
            Assert.That(StationJudge.CoveragePercent(0.002, d), Is.EqualTo(4.3090).Within(1e-3));
            Assert.That(StationJudge.CoveragePercent(0.004, d), Is.EqualTo(17.2360).Within(1e-3));
            Assert.That(StationJudge.CoveragePercent(0.008, d), Is.EqualTo(68.9441).Within(1e-3));
        }

        [Test]
        public void 距離が0以下なら例外()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StationJudge.AngularDiameterDegrees(0.002, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StationJudge.AngularDiameterDegrees(0.002, -1.0));
        }

        // ---- 全景 ----

        [Test]
        public void 全景の距離の数表()
        {
            // **全長を縦に置いて縦画角で収める**（横で収めるとアスペクト比に依存する）。
            //   d = (全長 * Scale / 2) / tan(画角/2) * 1.15
            //   Scale    全長 [units]   距離 [units]
            //   0.001    0.062541       0.062286
            //   0.002    0.125082       0.124572
            //   0.008    0.500326       0.498290
            Assert.That(StationJudge.OverviewDistanceUnits(0.001, 60.0),
                        Is.EqualTo(0.062286).Within(1e-6));
            Assert.That(StationJudge.OverviewDistanceUnits(0.002, 60.0),
                        Is.EqualTo(0.124572).Within(1e-6));
            Assert.That(StationJudge.OverviewDistanceUnits(0.008, 60.0),
                        Is.EqualTo(0.498290).Within(1e-6));

            // **どの Scale でも near clip の外。**
            foreach (double s in new[] { 0.001, 0.002, 0.004, 0.008 })
            {
                Assert.That(StationJudge.OverviewDistanceUnits(s, 60.0),
                            Is.GreaterThan(StationJudge.NearfieldNearClipUnits), "Scale " + s);
            }
        }

        // ---- 目盛 ----

        [Test]
        public void Scaleの目盛は連続で振れる()
        {
            // **段の切り替えにしない。** 境目がどこかを目で見るため。
            //   範囲 0.001〜0.008 / 刻み 0.00025 -> 29 段
            Assert.That(StationJudge.ScaleMin, Is.EqualTo(0.001));
            Assert.That(StationJudge.ScaleMax, Is.EqualTo(0.008));
            Assert.That(StationJudge.ScaleStep, Is.EqualTo(0.00025));

            double span = StationJudge.ScaleMax - StationJudge.ScaleMin;
            Assert.That(span / StationJudge.ScaleStep, Is.EqualTo(28.0).Within(1e-9),
                        "刻みが範囲を割り切らない");

            // 出発点は目盛の上に乗っていること（押しても端数が残らない）。
            double steps = (StationJudge.ScaleInitial - StationJudge.ScaleMin)
                           / StationJudge.ScaleStep;
            Assert.That(steps, Is.EqualTo(Math.Round(steps)).Within(1e-9));
        }

        [Test]
        public void 目盛のうち物差しが成立する範囲()
        {
            // **目盛の全域で成立するわけではない。** 事実として記録する。
            //
            //   候補   Scale の範囲
            //   (c)    0.0014278 〜 0.0028556
            //   (b)    0.0025500 〜 0.0050999
            //   (a)    0.0046007 〜 0.0092015
            //   和     0.0014278 〜 0.0092015  （隣り合う範囲が重なるので連続）
            //
            // 目盛は 0.001〜0.008 なので、
            //   **0.001 〜 0.0014278 ではどの候補も物差しに入らない**
            //   0.0014278 〜 0.008    ではいずれかが入る
            // **目盛の下端を切っていないのは、境目を目で見るため。**
            Assert.That(AnyWithin(0.001), Is.False,
                        "目盛の下端でどれかが入るようになっている（記録と食い違う）");
            Assert.That(AnyWithin(0.00125), Is.False);

            Assert.That(AnyWithin(0.0015), Is.True);
            Assert.That(AnyWithin(0.003), Is.True);
            Assert.That(AnyWithin(0.005), Is.True);
            Assert.That(AnyWithin(0.008), Is.True);

            // 和が連続していること（間に穴が空いていない）。
            StationJudge.ScaleRangeFor(PortFaceCandidate.ModuleBody, out double _, out double bodyHi);
            StationJudge.ScaleRangeFor(PortFaceCandidate.ProtrudingPlate,
                                       out double plateLo, out double plateHi);
            StationJudge.ScaleRangeFor(PortFaceCandidate.GoldDisc, out double goldLo, out double _);

            Assert.That(plateLo, Is.LessThan(bodyHi), "(c) と (b) の間に穴がある");
            Assert.That(goldLo, Is.LessThan(plateHi), "(b) と (a) の間に穴がある");
        }

        static bool AnyWithin(double scale)
            => StationJudge.WithinRule(PortFaceCandidate.GoldDisc, scale)
               || StationJudge.WithinRule(PortFaceCandidate.ProtrudingPlate, scale)
               || StationJudge.WithinRule(PortFaceCandidate.ModuleBody, scale);
    }
}
