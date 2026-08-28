using System.Collections.Generic;
using NUnit.Framework;
using SolarSystem.Core;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// 投影面積の計算 (Step 11-2b)。**純粋な計算なので入出力を手で確かめられる。**
    /// </summary>
    public sealed class ProjectedAreaTests
    {
        static double Area(double[] xs, double[] ys)
            => ProjectedAreaSolver.ConvexHullArea(new List<double>(xs), new List<double>(ys));

        [Test]
        public void 単位正方形の面積は1()
        {
            Assert.That(Area(new[] { 0.0, 1.0, 1.0, 0.0 }, new[] { 0.0, 0.0, 1.0, 1.0 }),
                        Is.EqualTo(1.0).Within(1e-12));
        }

        [Test]
        public void 直角三角形の面積は底辺かける高さの半分()
        {
            // 底辺 4 / 高さ 3 -> 6.0
            Assert.That(Area(new[] { 0.0, 4.0, 0.0 }, new[] { 0.0, 0.0, 3.0 }),
                        Is.EqualTo(6.0).Within(1e-12));
        }

        [Test]
        public void 一直線に並んだ点の面積は0()
        {
            Assert.That(Area(new[] { 0.0, 1.0, 2.0, 3.0 }, new[] { 0.0, 1.0, 2.0, 3.0 }),
                        Is.EqualTo(0.0).Within(1e-12));
        }

        [Test]
        public void 点が3個未満なら0()
        {
            Assert.That(Area(new[] { 0.0, 1.0 }, new[] { 0.0, 1.0 }), Is.EqualTo(0.0));
            Assert.That(Area(new double[0], new double[0]), Is.EqualTo(0.0));
        }

        [Test]
        public void 同じ点が重なっていても落ちない()
        {
            Assert.That(Area(new[] { 0.0, 0.0, 1.0, 1.0, 1.0, 0.0 },
                             new[] { 0.0, 0.0, 0.0, 1.0, 1.0, 1.0 }),
                        Is.EqualTo(1.0).Within(1e-12));
        }

        [Test]
        public void 凹んだ点は凸包に含まれない()
        {
            // 単位正方形の内側に 1 点。**凸包なので面積は 1.0 のまま。**
            // これは近似の性質そのもの。窓のシルエットが凹んでいると
            // 実際より大きく出る（ログに「凸包近似」と書いている理由）。
            double[] xs = { 0.0, 1.0, 1.0, 0.0, 0.5 };
            double[] ys = { 0.0, 0.0, 1.0, 1.0, 0.5 };

            Assert.That(Area(xs, ys), Is.EqualTo(1.0).Within(1e-12));

            List<int> hull = ProjectedAreaSolver.ConvexHull(new List<double>(xs), new List<double>(ys));
            Assert.That(hull.Count, Is.EqualTo(4), "内側の点が凸包に入っている");
            Assert.That(hull.Contains(4), Is.False, "内側の点 (index 4) が凸包に入っている");
        }

        [Test]
        public void 画面に占める割合は面積を画面面積で割った値()
        {
            // 1920x1080 の画面に 960x540 の矩形 = 面積比 0.25。
            double area = 960.0 * 540.0;
            Assert.That(ProjectedAreaSolver.ScreenRatio(area, 1920, 1080),
                        Is.EqualTo(0.25).Within(1e-12));
        }

        [Test]
        public void 画面の面積が0なら0を返す()
        {
            // **NaN を返さない。** 「測れない」は呼び手が負の値で表す。
            Assert.That(ProjectedAreaSolver.ScreenRatio(100.0, 0, 0), Is.EqualTo(0.0));
        }

        [Test]
        public void 画面からはみ出した分は丸めない()
        {
            // はみ出していることが分かるほうが、目の位置を決めるうえで役に立つ。
            double area = 1920.0 * 1080.0 * 2.0;
            Assert.That(ProjectedAreaSolver.ScreenRatio(area, 1920, 1080),
                        Is.EqualTo(2.0).Within(1e-12));
        }
    }
}
