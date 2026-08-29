using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// **頭姿勢の配布と、視差・方向変化の数表 (Step 12-1)。**
    ///
    /// 期待値は手で計算した数表として書いてある。**式を実測に合わせて変えないための錨。**
    /// 後のセッションで実測とずれたときは、この表ではなく描画側を疑うこと
    /// （ずれの事実は数値で報告する）。
    ///
    /// 数表の条件:
    ///   IPD 63 mm / 頭の移動 0 / 0.1 / 0.2 / 0.3 m
    ///   スケール s = 1.0（コックピット段）と s = 0.001（外 3 段）の両方
    ///   焦点距離 f = 935.3074360872 px（1080p / 縦画角 60 度）
    ///
    /// 距離は実際に使う値:
    ///   計器 0.5（コックピット段で 1 m = 1 unit なので 0.5 m 相当）
    ///   地球 1.2e4 units / 火星 8e3 units / 太陽 1 AU = 1.495978707e8 units
    /// </summary>
    public sealed class StereoGeometryTests
    {
        /// <summary>相対許容差。double の丸めだけを吸収する幅にする。</summary>
        const double RelativeTolerancePercent = 1e-9;

        /// <summary>
        /// **平面のときの IPD [m]。仮定であって XR の入力ではない (Step 12-1b)。**
        /// XR で実際に使われる値は `XrStereoOptics` が実行時に測る。
        /// </summary>
        const double Ipd = 0.063;

        /// <summary>
        /// **平面のときの焦点距離 [px]。1920x1080 / 縦画角 60 度。**
        /// 12-0d の実測で XR の目テクスチャは 1512x1680 だったので、
        /// **この値を XR に持ち込まない。** 数表は平面の値で凍結してある。
        /// </summary>
        static readonly double PlaneFocalLengthPixels =
            AngularSizeSolver.FocalLengthPixels(
                UniverseConstants.ReferenceVerticalFovDegrees,
                UniverseConstants.ReferencePixelHeight);
        const double Instrument = 0.5;
        const double Earth = 12000.0;
        const double Mars = 8000.0;
        const double Sun = 149597870.7;

        const double Cockpit = StereoGeometry.CockpitStageScale;  // 1.0
        const double World = StereoGeometry.WorldStageScale;      // 0.001

        static StereoGeometry.HeadPose Facing(Vec3d positionMeters)
            => new StereoGeometry.HeadPose(
                positionMeters, new Vec3d(0.0, 0.0, 1.0), new Vec3d(0.0, 1.0, 0.0));

        static void AssertClose(double actual, double expected, string label)
        {
            if (expected == 0.0)
            {
                Assert.That(actual, Is.EqualTo(0.0), label);
                return;
            }

            Assert.That(actual, Is.EqualTo(expected).Within(RelativeTolerancePercent).Percent, label);
        }

        // ---- 焦点距離 ----

        [Test]
        public void 焦点距離は既存の式から来ている()
        {
            // **f = (H/2)/tan(FOV/2)。FOV/H ではない**（AngularSizeSolver の注記）。
            //   f   = 540 / tan(30 度) = 540 / 0.5773502691896258 = 935.3074360872
            //   1/f = 1.0691671651659736e-3 rad/px
            AssertClose(PlaneFocalLengthPixels, 935.3074360872, "f");

            AssertClose(1.0 / PlaneFocalLengthPixels,
                        1.0691671651659736e-3, "RadiansPerPixel");

            // 既存の関数と同じ値であること（別式を持ち込んでいない）。
            AssertClose(1.0 / PlaneFocalLengthPixels,
                        AngularSizeSolver.RadiansPerPixel(
                            UniverseConstants.ReferenceVerticalFovDegrees,
                            UniverseConstants.ReferencePixelHeight),
                        "AngularSizeSolver と一致");
        }

        [Test]
        public void 段のスケールはカメラスタックの倍率と一致する()
        {
            // **コックピット段は 1000 倍の描画空間**（1 m = 1 unit）。
            // 外 3 段は 1 unit = 1 km なので 1 m = 0.001 units。
            // ここがずれると、頭を動かしたとき段どうしで見かけの角度が食い違う。
            Assert.That(StereoGeometry.CockpitStageScale, Is.EqualTo(1.0));
            Assert.That(StereoGeometry.WorldStageScale,
                        Is.EqualTo(1.0 / CameraStackController.CockpitRenderScale).Within(1e-15));
        }

        // ---- 1. 頭姿勢を段へ配る ----

        struct PoseRow
        {
            public double Meters;
            public double Scale;
            public double ExpectedUnits;

            public PoseRow(double meters, double scale, double expectedUnits)
            {
                Meters = meters;
                Scale = scale;
                ExpectedUnits = expectedUnits;
            }
        }

        static readonly PoseRow[] PoseRows =
        {
            //          頭の移動 [m]  s        段の中の位置 [units]
            new PoseRow(0.0,          Cockpit, 0.0),
            new PoseRow(0.1,          Cockpit, 0.1),
            new PoseRow(0.2,          Cockpit, 0.2),
            new PoseRow(0.3,          Cockpit, 0.3),
            new PoseRow(0.0,          World,   0.0),
            new PoseRow(0.1,          World,   0.0001),
            new PoseRow(0.2,          World,   0.0002),
            new PoseRow(0.3,          World,   0.0003),
        };

        [Test]
        public void 頭の位置は段のスケールで縮む()
        {
            foreach (PoseRow row in PoseRows)
            {
                StereoGeometry.StagePose pose =
                    StereoGeometry.CameraLocal(Facing(new Vec3d(row.Meters, 0.0, 0.0)), row.Scale);

                string label = $"{row.Meters} m / s={row.Scale}";
                AssertClose(pose.Position.X, row.ExpectedUnits, label);
                AssertClose(pose.Position.Y, 0.0, label);
                AssertClose(pose.Position.Z, 0.0, label);
            }
        }

        [Test]
        public void 三軸すべてが同じ倍率で縮む()
        {
            // 1 軸だけの表では、成分の取り違えを捕まえられない。
            var head = Facing(new Vec3d(0.1, -0.2, 0.3));

            StereoGeometry.StagePose world = StereoGeometry.CameraLocal(head, World);
            AssertClose(world.Position.X, 0.0001, "X");
            AssertClose(world.Position.Y, -0.0002, "Y");
            AssertClose(world.Position.Z, 0.0003, "Z");

            StereoGeometry.StagePose cockpit = StereoGeometry.CameraLocal(head, Cockpit);
            AssertClose(cockpit.Position.X, 0.1, "X");
            AssertClose(cockpit.Position.Y, -0.2, "Y");
            AssertClose(cockpit.Position.Z, 0.3, "Z");
        }

        [Test]
        public void 回転はスケールしない()
        {
            // **向きは向きのまま。** ここを s 倍すると軸の長さが変わり、
            // 段によって見ている方向が変わってしまう。
            var head = new StereoGeometry.HeadPose(
                new Vec3d(0.3, 0.0, 0.0), new Vec3d(1.0, 0.0, 0.0), new Vec3d(0.0, 1.0, 0.0));

            foreach (double scale in new[] { Cockpit, World })
            {
                StereoGeometry.StagePose pose = StereoGeometry.CameraLocal(head, scale);

                Assert.That(pose.Forward, Is.EqualTo(head.Forward), $"前方 / s={scale}");
                Assert.That(pose.Up, Is.EqualTo(head.Up), $"上方 / s={scale}");

                // 右 = 上 x 前（Unity と同じ左手系）。
                // 前が +X・上が +Y なら右は -Z。
                AssertClose(pose.Right.X, 0.0, "右.X");
                AssertClose(pose.Right.Y, 0.0, "右.Y");
                AssertClose(pose.Right.Z, -1.0, "右.Z");
            }
        }

        // ---- 2. 両眼の位置 ----

        [Test]
        public void IPDも段のスケールに従う()
        {
            //   s = 1.0    63 mm -> 0.063 units
            //   s = 0.001  63 mm -> 6.3e-5 units
            AssertClose(StereoGeometry.IpdInStageUnits(Ipd, Cockpit), 0.063, "コックピット段");
            AssertClose(StereoGeometry.IpdInStageUnits(Ipd, World), 6.3e-5, "外 3 段");
        }

        struct EyeRow
        {
            public double Meters;
            public double Scale;
            public double ExpectedLeftX;
            public double ExpectedRightX;

            public EyeRow(double meters, double scale, double left, double right)
            {
                Meters = meters;
                Scale = scale;
                ExpectedLeftX = left;
                ExpectedRightX = right;
            }
        }

        static readonly EyeRow[] EyeRows =
        {
            //         頭 [m]  s        左目 [units]      右目 [units]
            new EyeRow(0.0,     Cockpit, -0.0315,          0.0315),
            new EyeRow(0.1,     Cockpit,  0.0685,          0.1315),
            new EyeRow(0.2,     Cockpit,  0.1685,          0.2315),
            new EyeRow(0.3,     Cockpit,  0.2685,          0.3315),
            new EyeRow(0.0,     World,   -3.15e-5,         3.15e-5),
            new EyeRow(0.1,     World,    6.85e-5,         1.315e-4),
            new EyeRow(0.2,     World,    1.685e-4,        2.315e-4),
            new EyeRow(0.3,     World,    2.685e-4,        3.315e-4),
        };

        [Test]
        public void 両眼の位置の数表()
        {
            foreach (EyeRow row in EyeRows)
            {
                var head = Facing(new Vec3d(row.Meters, 0.0, 0.0));
                string label = $"{row.Meters} m / s={row.Scale}";

                StereoGeometry.StagePose left =
                    StereoGeometry.EyeLocal(head, StereoGeometry.Eye.Left, Ipd, row.Scale);
                StereoGeometry.StagePose right =
                    StereoGeometry.EyeLocal(head, StereoGeometry.Eye.Right, Ipd, row.Scale);

                AssertClose(left.Position.X, row.ExpectedLeftX, "左 " + label);
                AssertClose(right.Position.X, row.ExpectedRightX, "右 " + label);

                // 左右の差は常に IPD（頭の位置に依らない）。
                AssertClose(right.Position.X - left.Position.X,
                            StereoGeometry.IpdInStageUnits(Ipd, row.Scale), "差 " + label);

                AssertClose(left.Position.Y, 0.0, "左.Y " + label);
                AssertClose(left.Position.Z, 0.0, "左.Z " + label);
            }
        }

        [Test]
        public void 左右は頭の向きに付いてくる()
        {
            // 前が +X・上が +Y のとき右は -Z。**右目が -Z 側に出る。**
            // ここが逆になると、実機で左右が入れ替わって見える（見た目では
            // 気付きにくく、数値で縛るしかない）。
            var head = new StereoGeometry.HeadPose(
                Vec3d.Zero, new Vec3d(1.0, 0.0, 0.0), new Vec3d(0.0, 1.0, 0.0));

            Vec3d right = StereoGeometry.EyeOffsetMeters(head, StereoGeometry.Eye.Right, Ipd);
            Vec3d left = StereoGeometry.EyeOffsetMeters(head, StereoGeometry.Eye.Left, Ipd);

            AssertClose(right.Z, -0.0315, "右目 Z");
            AssertClose(left.Z, 0.0315, "左目 Z");
            AssertClose(right.X, 0.0, "右目 X");
            AssertClose(right.Y, 0.0, "右目 Y");
        }

        // ---- 3. 視差の数表 ----

        struct ParallaxRow
        {
            public string Body;
            public string Stage;
            public double Distance;
            public double Scale;
            public double ExpectedPixels;

            public ParallaxRow(string body, string stage, double distance, double scale, double px)
            {
                Body = body;
                Stage = stage;
                Distance = distance;
                Scale = scale;
                ExpectedPixels = px;
            }
        }

        /// <summary>視差 [px] = f * IPD * s / D。IPD 63 mm / f = 935.3074360872。</summary>
        static readonly ParallaxRow[] ParallaxRows =
        {
            //              対象          段         距離 [units]  s        視差 [px]
            new ParallaxRow("計器",       "コクピ",  Instrument,   Cockpit, 1.1784873694699e+02),
            new ParallaxRow("計器",       "外 3 段", Instrument,   World,   1.1784873694699e-01),
            new ParallaxRow("地球",       "コクピ",  Earth,        Cockpit, 4.9103640394578e-03),
            new ParallaxRow("地球",       "外 3 段", Earth,        World,   4.9103640394578e-06),
            new ParallaxRow("火星",       "コクピ",  Mars,         Cockpit, 7.3655460591867e-03),
            new ParallaxRow("火星",       "外 3 段", Mars,         World,   7.3655460591867e-06),
            new ParallaxRow("太陽",       "コクピ",  Sun,          Cockpit, 3.9388507468571e-07),
            new ParallaxRow("太陽",       "外 3 段", Sun,          World,   3.9388507468571e-10),
        };

        [Test]
        public void 視差の数表()
        {
            foreach (ParallaxRow row in ParallaxRows)
            {
                double actual = StereoGeometry.ParallaxPixels(
                    Ipd, row.Distance, row.Scale, PlaneFocalLengthPixels);
                AssertClose(actual, row.ExpectedPixels, $"{row.Body} / {row.Stage}");
            }
        }

        [Test]
        public void 外3段の視差は1pxに遠く届かない()
        {
            // **これは合否ではなく、後で実測と突き合わせるための事実。**
            // 外 3 段では最も近い地球でも 4.9e-6 px。1 px の 20 万分の 1 で、
            // **左右の絵は画素として同一になる。**
            // 立体に見える視差を持つのはコックピット段だけ（計器で 117.8 px）。
            foreach (ParallaxRow row in ParallaxRows)
            {
                // **計器の行は外 3 段には無い組み合わせ。** スケールの効き方を
                // 見せるために表へ入れてあるだけで、実際の配置ではないので外す。
                if (row.Scale != World || row.Distance == Instrument)
                {
                    continue;
                }

                Assert.That(row.ExpectedPixels, Is.LessThan(1e-4),
                            $"{row.Body}: 外 3 段で視差が出ている（前提が変わった）");
            }

            Assert.That(
                StereoGeometry.ParallaxPixels(Ipd, Instrument, Cockpit, PlaneFocalLengthPixels),
                Is.GreaterThan(100.0),
                "コックピット段の視差まで消えている");
        }

        // ---- 4. 方向変化の数表 ----

        struct DirectionRow
        {
            public string Body;
            public double Baseline;
            public double Distance;
            public double Scale;
            public double ExpectedRadians;
            public double ExpectedPixels;

            public DirectionRow(string body, double baseline, double distance, double scale,
                                double radians, double pixels)
            {
                Body = body;
                Baseline = baseline;
                Distance = distance;
                Scale = scale;
                ExpectedRadians = radians;
                ExpectedPixels = pixels;
            }
        }

        /// <summary>Δθ = baseline * s / D、px = Δθ * f。</summary>
        static readonly DirectionRow[] DirectionRows =
        {
            //               対象    baseline [m] 距離 [units] s        Δθ [rad]              [px]
            new DirectionRow("計器", 0.0,         Instrument,  Cockpit, 0.0,                  0.0),
            new DirectionRow("地球", 0.0,         Earth,       World,   0.0,                  0.0),

            new DirectionRow("計器", 0.1,         Instrument,  Cockpit, 2.0000000000000e-01,  1.8706148721744e+02),
            new DirectionRow("計器", 0.1,         Instrument,  World,   2.0000000000000e-04,  1.8706148721744e-01),
            new DirectionRow("地球", 0.1,         Earth,       Cockpit, 8.3333333333333e-06,  7.7942286340599e-03),
            new DirectionRow("地球", 0.1,         Earth,       World,   8.3333333333333e-09,  7.7942286340599e-06),
            new DirectionRow("火星", 0.1,         Mars,        Cockpit, 1.2500000000000e-05,  1.1691342951090e-02),
            new DirectionRow("火星", 0.1,         Mars,        World,   1.2500000000000e-08,  1.1691342951090e-05),
            new DirectionRow("太陽", 0.1,         Sun,         Cockpit, 6.6845871222684e-10,  6.2521440426304e-07),
            new DirectionRow("太陽", 0.1,         Sun,         World,   6.6845871222684e-13,  6.2521440426304e-10),

            new DirectionRow("計器", 0.2,         Instrument,  Cockpit, 4.0000000000000e-01,  3.7412297443488e+02),
            new DirectionRow("計器", 0.2,         Instrument,  World,   4.0000000000000e-04,  3.7412297443488e-01),
            new DirectionRow("地球", 0.2,         Earth,       Cockpit, 1.6666666666667e-05,  1.5588457268120e-02),
            new DirectionRow("地球", 0.2,         Earth,       World,   1.6666666666667e-08,  1.5588457268120e-05),
            new DirectionRow("火星", 0.2,         Mars,        Cockpit, 2.5000000000000e-05,  2.3382685902180e-02),
            new DirectionRow("火星", 0.2,         Mars,        World,   2.5000000000000e-08,  2.3382685902180e-05),
            new DirectionRow("太陽", 0.2,         Sun,         Cockpit, 1.3369174244537e-09,  1.2504288085261e-06),
            new DirectionRow("太陽", 0.2,         Sun,         World,   1.3369174244537e-12,  1.2504288085261e-09),

            new DirectionRow("計器", 0.3,         Instrument,  Cockpit, 6.0000000000000e-01,  5.6118446165232e+02),
            new DirectionRow("計器", 0.3,         Instrument,  World,   6.0000000000000e-04,  5.6118446165232e-01),
            new DirectionRow("地球", 0.3,         Earth,       Cockpit, 2.5000000000000e-05,  2.3382685902180e-02),
            new DirectionRow("地球", 0.3,         Earth,       World,   2.5000000000000e-08,  2.3382685902180e-05),
            new DirectionRow("火星", 0.3,         Mars,        Cockpit, 3.7500000000000e-05,  3.5074028853270e-02),
            new DirectionRow("火星", 0.3,         Mars,        World,   3.7500000000000e-08,  3.5074028853270e-05),
            new DirectionRow("太陽", 0.3,         Sun,         Cockpit, 2.0053761366805e-09,  1.8756432127891e-06),
            new DirectionRow("太陽", 0.3,         Sun,         World,   2.0053761366805e-12,  1.8756432127891e-09),
        };

        [Test]
        public void 方向変化の数表()
        {
            foreach (DirectionRow row in DirectionRows)
            {
                string label = $"{row.Body} / baseline {row.Baseline} m / s={row.Scale}";

                AssertClose(
                    StereoGeometry.DirectionChangeRadians(row.Baseline, row.Distance, row.Scale),
                    row.ExpectedRadians, label + " [rad]");

                AssertClose(
                    StereoGeometry.DirectionChangePixels(
                        row.Baseline, row.Distance, row.Scale, PlaneFocalLengthPixels),
                    row.ExpectedPixels, label + " [px]");
            }
        }

        [Test]
        public void 小角近似が外れる範囲を数値で残す()
        {
            // **Δθ = baseline / D は小角近似。** 指定された式はこれで、変えない。
            // ただし計器 0.5 m を 0.3 m 動くと比が 0.6 になり、厳密な atan と 10 % 違う。
            // **実機でずれたときに、この近似の分なのか描画の壊れなのかを
            // 切り分けられるように、乖離をここに残しておく。**
            //
            //   baseline  近似 [rad]  厳密 atan [rad]  厳密/近似
            //   0.1 m     0.2         0.19739556       0.986978
            //   0.2 m     0.4         0.38050638       0.951266
            //   0.3 m     0.6         0.54041950       0.900699
            var rows = new[]
            {
                new { Baseline = 0.1, Approx = 0.2, Exact = 1.9739555984988e-01, Ratio = 0.986978 },
                new { Baseline = 0.2, Approx = 0.4, Exact = 3.8050637711236e-01, Ratio = 0.951266 },
                new { Baseline = 0.3, Approx = 0.6, Exact = 5.4041950027058e-01, Ratio = 0.900699 },
            };

            foreach (var row in rows)
            {
                double approx =
                    StereoGeometry.DirectionChangeRadians(row.Baseline, Instrument, Cockpit);
                double exact =
                    StereoGeometry.DirectionChangeExactRadians(row.Baseline, Instrument, Cockpit);

                AssertClose(approx, row.Approx, $"近似 {row.Baseline} m");
                AssertClose(exact, row.Exact, $"厳密 {row.Baseline} m");
                Assert.That(exact / approx, Is.EqualTo(row.Ratio).Within(1e-6),
                            $"乖離 {row.Baseline} m");
            }

            // 外 3 段では比が 1e-8 以下なので、近似と厳密の差は double の丸め以下。
            double farApprox = StereoGeometry.DirectionChangeRadians(0.3, Earth, World);
            double farExact = StereoGeometry.DirectionChangeExactRadians(0.3, Earth, World);
            Assert.That(farExact, Is.EqualTo(farApprox).Within(1e-9).Percent,
                        "外 3 段では近似の差が出ないはず");
        }

        // ---- 前提の検査 ----

        [Test]
        public void 距離0やスケール0は例外にする()
        {
            // **黙って 0 や NaN を返さない。** 単位を取り違えたまま
            // 「小さい値が出た」で通ると、実測と突き合わせたときに原因が消える。
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StereoGeometry.ParallaxPixels(Ipd, 0.0, Cockpit, PlaneFocalLengthPixels));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StereoGeometry.ParallaxPixels(Ipd, -1.0, Cockpit, PlaneFocalLengthPixels));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StereoGeometry.ParallaxPixels(Ipd, Instrument, 0.0, PlaneFocalLengthPixels));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StereoGeometry.DirectionChangeRadians(0.1, 0.0, Cockpit));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StereoGeometry.CameraLocal(StereoGeometry.HeadPose.Identity, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StereoGeometry.EyeOffsetMeters(
                    StereoGeometry.HeadPose.Identity, StereoGeometry.Eye.Left, -0.063));
        }

        // ---- 投影行列から f を出す (Step 12-1b) ----

        struct FocalRow
        {
            public string Note;
            public double M11;
            public int Height;
            public double ExpectedPixels;

            public FocalRow(string note, double m11, int height, double px)
            {
                Note = note;
                M11 = m11;
                Height = height;
                ExpectedPixels = px;
            }
        }

        /// <summary>f [px] = (H/2) * m11。m11 = 1/tan(縦画角/2)。</summary>
        static readonly FocalRow[] FocalRows =
        {
            //           条件                      m11                 H     f [px]
            new FocalRow("平面 1080p / 60 度",      1.7320508075688774, 1080, 935.3074360871938),
            new FocalRow("XR 目テクスチャ高 1680",  1.0,                1680, 840.0),
            new FocalRow("同 / 画角 60 度相当",     1.7320508075688774, 1680, 1454.922678357857),
            new FocalRow("H 1512 / m11 = 2",       2.0,                1512, 1512.0),
        };

        [Test]
        public void 投影行列から焦点距離を出す数表()
        {
            foreach (FocalRow row in FocalRows)
            {
                AssertClose(
                    StereoGeometry.FocalLengthPixelsFromProjection(row.M11, row.Height),
                    row.ExpectedPixels, row.Note);
            }

            // **平面の経路と同じ値になること。** 別式を持ち込んでいない確認。
            AssertClose(
                StereoGeometry.FocalLengthPixelsFromProjection(
                    1.0 / Math.Tan(Math.PI / 6.0), UniverseConstants.ReferencePixelHeight),
                PlaneFocalLengthPixels, "平面と一致");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => StereoGeometry.FocalLengthPixelsFromProjection(0.0, 1080));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StereoGeometry.FocalLengthPixelsFromProjection(1.5, 0));
        }

        [Test]
        public void fとIPDに既定値を持たせない()
        {
            // **渡し忘れを黙って通さないことの構造的な保証 (Step 12-1b)。**
            // 既定値があると、平面の f = 935.31 px や IPD 63 mm で計算が通ってしまい、
            // 「実測 / 理論」が 1.0 から外れたときに描画の壊れなのか入力違いなのかを
            // 切り分けられなくなる。
            foreach (MethodInfo method in typeof(StereoGeometry)
                     .GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.HasDefaultValue, Is.False,
                                $"{method.Name} の {parameter.Name} に既定値がある");
                }
            }

            // px を返す関数は f を必ず受け取る（f 無しのオーバーロードを作らない）。
            foreach (string name in new[] { "ParallaxPixels", "DirectionChangePixels" })
            {
                MethodInfo[] overloads = typeof(StereoGeometry)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name == name).ToArray();

                Assert.That(overloads, Is.Not.Empty, name + " が無い");

                foreach (MethodInfo method in overloads)
                {
                    Assert.That(method.GetParameters().Select(p => p.Name),
                                Does.Contain("focalLengthPixels"),
                                $"{name} に f を受け取らない口がある");
                }
            }

            // 平面の値を持ち出せる公開メンバを置かない。
            foreach (MemberInfo member in typeof(StereoGeometry)
                     .GetMembers(BindingFlags.Public | BindingFlags.Static))
            {
                Assert.That(member.Name, Does.Not.Contain("Default"),
                            "既定値らしき公開メンバがある: " + member.Name);
                Assert.That(member.Name, Does.Not.Contain("Reference"),
                            "基準値らしき公開メンバがある: " + member.Name);
            }
        }
    }
}
