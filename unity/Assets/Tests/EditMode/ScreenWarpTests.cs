using System;
using System.Collections.Generic;
using NUnit.Framework;
using SolarSystem.Core;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// 逆歪ませの数学 (Step 11-3c)。**UnityEngine を通さずに縛る。**
    ///
    /// 「画面の上でどう見えるか」は、目から見た方向 (x/z, y/z) で測る。
    /// 画角と解像度が決まれば画素座標との間は**等方な拡大と平行移動だけ**なので、
    /// この空間で真円なら画面でも真円になる。**解像度に依存しない測り方。**
    /// </summary>
    public sealed class ScreenWarpTests
    {
        /// <summary>斜めに置かれた面。**手前が右下、奥が左上**（実機の大画面に近い）。</summary>
        static IReadOnlyList<Vec3d> TiltedFace()
        {
            // 幅 0.35 m / 高さ 0.18 m の面を、下辺を手前に倒して置く。
            return new[]
            {
                new Vec3d(-0.175, -0.30, 0.62),
                new Vec3d(0.175, -0.30, 0.62),
                new Vec3d(0.175, -0.18, 0.80),
                new Vec3d(-0.175, -0.18, 0.80),
            };
        }

        [Test]
        public void 単位正方形をそのまま写すと恒等になる()
        {
            var square = new[]
            {
                new Vec2d(0.0, 0.0), new Vec2d(1.0, 0.0),
                new Vec2d(1.0, 1.0), new Vec2d(0.0, 1.0),
            };

            Homography h = Homography.FromUnitSquare(square);
            Vec2d p = h.Apply(new Vec2d(0.37, 0.81));

            Assert.That(p.X, Is.EqualTo(0.37).Within(1e-9));
            Assert.That(p.Y, Is.EqualTo(0.81).Within(1e-9));
        }

        [Test]
        public void 逆行列を掛けると元へ戻る()
        {
            var quad = new[]
            {
                new Vec2d(-0.20, -0.44), new Vec2d(0.31, -0.40),
                new Vec2d(0.22, -0.19), new Vec2d(-0.14, -0.21),
            };

            Homography h = Homography.FromUnitSquare(quad);
            Homography back = h.Inverse();

            Vec2d p = back.Apply(h.Apply(new Vec2d(0.63, 0.29)));
            Assert.That(p.X, Is.EqualTo(0.63).Within(1e-9));
            Assert.That(p.Y, Is.EqualTo(0.29).Within(1e-9));
        }

        [Test]
        public void 三点が一直線なら例外で止まる()
        {
            var degenerate = new[]
            {
                new Vec2d(0.0, 0.0), new Vec2d(1.0, 0.0),
                new Vec2d(2.0, 0.0), new Vec2d(0.0, 1.0),
            };

            Assert.That(() => Homography.FromUnitSquare(degenerate),
                        Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void 逆歪ませたあとの円が画面上で真円になる()
        {
            ScreenWarpSolver.Result warp = ScreenWarpSolver.Solve(TiltedFace(), 1024.0 / 544.0);

            // **中身の円**（RT の中心・半径 0.3）が、画面上でどんな形に見えるか。
            // 中身の点 q は、出力 (s,t) = M⁻¹(q) の位置に描かれ、
            // その (s,t) は H で視線方向へ写る。
            double[] radii = ScreenRadii(warp, 64);
            double min = double.MaxValue, max = double.MinValue;
            foreach (double r in radii)
            {
                min = Math.Min(min, r); max = Math.Max(max, r);
            }

            // 円形度 = 最短半径 / 最長半径。**1.00 が真円。**
            double circularity = min / max;
            Assert.That(circularity, Is.GreaterThan(0.99),
                        $"逆歪ませても円がつぶれている (円形度 {circularity:F4})");
        }

        [Test]
        public void 逆歪ませないと円はつぶれる()
        {
            // **測り方に効き目があることの担保。** 同じ測り方で、逆歪ませを
            // 掛けない（= 面にそのまま貼る）ときは円形度が明らかに 1 を割る。
            ScreenWarpSolver.Result warp = ScreenWarpSolver.Solve(TiltedFace(), 1024.0 / 544.0);
            Homography h = Homography.FromUnitSquare(Projected(TiltedFace()));

            double min = double.MaxValue, max = double.MinValue;
            Vec2d center = h.Apply(new Vec2d(0.5, 0.5));
            for (int i = 0; i < 64; i++)
            {
                double a = (i / 64.0) * Math.PI * 2.0;
                Vec2d p = h.Apply(new Vec2d(0.5 + (0.2 * Math.Cos(a)),
                                            0.5 + (0.2 * Math.Sin(a) * (1024.0 / 544.0))));
                double r = Math.Sqrt(((p.X - center.X) * (p.X - center.X))
                                     + ((p.Y - center.Y) * (p.Y - center.Y)));
                min = Math.Min(min, r); max = Math.Max(max, r);
            }

            Assert.That(min / max, Is.LessThan(0.9),
                        "面にそのまま貼っても円のままなら、この測り方は歪みを見ていない");
            Assert.That(warp.TextureScale.X, Is.GreaterThanOrEqualTo(1.0));
        }

        [Test]
        public void 逆歪ませたあとの格子が画面上で等間隔になる()
        {
            ScreenWarpSolver.Result warp = ScreenWarpSolver.Solve(TiltedFace(), 1024.0 / 544.0);
            Homography h = Homography.FromUnitSquare(Projected(TiltedFace()));
            Homography toOutput = warp.ToSource.Inverse();

            // 縦線 8 分割。**台形が残っていれば間隔が単調に詰まる。**
            var xs = new List<double>();
            for (int i = 0; i <= 8; i++)
            {
                Vec2d q = new Vec2d(i / 8.0, 0.5);
                xs.Add(h.Apply(toOutput.Apply(q)).X);
            }

            double minGap = double.MaxValue, maxGap = double.MinValue;
            for (int i = 1; i < xs.Count; i++)
            {
                double gap = Math.Abs(xs[i] - xs[i - 1]);
                minGap = Math.Min(minGap, gap); maxGap = Math.Max(maxGap, gap);
            }

            Assert.That(minGap / maxGap, Is.GreaterThan(0.99),
                        $"格子が等間隔でない (最小/最大 {minGap / maxGap:F4})");
        }

        [Test]
        public void 面が目の後ろにあれば例外で止まる()
        {
            var behind = new[]
            {
                new Vec3d(-0.1, -0.1, -0.5), new Vec3d(0.1, -0.1, -0.5),
                new Vec3d(0.1, 0.1, -0.5), new Vec3d(-0.1, 0.1, -0.5),
            };

            Assert.That(() => ScreenWarpSolver.Solve(behind, 1.0),
                        Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void 平面からのずれを比で返す()
        {
            var flat = new List<Vec3d>(TiltedFace());
            Assert.That(ScreenWarpSolver.PlanarDeviation(flat), Is.LessThan(1e-9));

            // 1 点だけ面から 10 mm 持ち上げる。面の差し渡しは約 0.39 m。
            flat.Add(new Vec3d(0.0, -0.24, 0.71 + 0.01));
            double deviation = ScreenWarpSolver.PlanarDeviation(flat);

            Assert.That(deviation, Is.GreaterThan(ScreenWarpSolver.PlanarToleranceRatio),
                        $"曲がりを見逃している (ずれ {deviation:P3})");
        }

        static IReadOnlyList<Vec2d> Projected(IReadOnlyList<Vec3d> corners)
        {
            var p = new Vec2d[corners.Count];
            for (int i = 0; i < corners.Count; i++)
            {
                p[i] = new Vec2d(corners[i].X / corners[i].Z, corners[i].Y / corners[i].Z);
            }

            return p;
        }

        /// <summary>中身の円を画面（視線方向）へ写したときの、中心からの距離。</summary>
        static double[] ScreenRadii(ScreenWarpSolver.Result warp, int steps)
        {
            Homography h = Homography.FromUnitSquare(Projected(TiltedFace()));
            Homography toOutput = warp.ToSource.Inverse();

            Vec2d center = h.Apply(toOutput.Apply(new Vec2d(0.5, 0.5)));
            var radii = new double[steps];
            for (int i = 0; i < steps; i++)
            {
                double a = (i / (double)steps) * Math.PI * 2.0;

                // **中身の円は、RT の画素比で真円になるように取る。**
                // 縦横比 1024:544 の RT では、u 方向の 0.3 は v 方向の 0.3 * (1024/544)。
                var q = new Vec2d(0.5 + (0.2 * Math.Cos(a)),
                                  0.5 + (0.2 * Math.Sin(a) * (1024.0 / 544.0)));
                Vec2d p = h.Apply(toOutput.Apply(q));
                radii[i] = Math.Sqrt(((p.X - center.X) * (p.X - center.X))
                                     + ((p.Y - center.Y) * (p.Y - center.Y)));
            }

            return radii;
        }
    }
}
