using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// 投影された点列が画面のどれだけを占めるかを出す (Step 11-2b)。**UnityEngine 非依存。**
    ///
    /// ■ **凸包で近似する。**
    /// 窓（キャノピーのガラス）のシルエットが凹んでいる場合、凸包は実際より
    /// **大きく**出る。厳密なラスタ被覆率は計算量が増えるわりに、F4 で目の位置を
    /// 決めるときの判断材料としては差が出ないと見て採らなかった。
    /// **ログには「凸包近似」と明記する。**
    ///
    /// ■ 画面外は切り取らない
    /// 窓が画面からはみ出しているときは比が 1.0 を超えうる。**それを丸めない。**
    /// 「はみ出している」ことが分かるほうが、目の位置を決めるうえで役に立つ。
    /// </summary>
    public static class ProjectedAreaSolver
    {
        /// <summary>凸包の面積 [px^2]。点が 3 個未満、または一直線なら 0。</summary>
        public static double ConvexHullArea(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
        {
            List<int> hull = ConvexHull(xs, ys);
            if (hull.Count < 3)
            {
                return 0.0;
            }

            // 靴ひも公式。
            double twice = 0.0;
            for (int i = 0; i < hull.Count; i++)
            {
                int a = hull[i];
                int b = hull[(i + 1) % hull.Count];
                twice += (xs[a] * ys[b]) - (xs[b] * ys[a]);
            }

            return Math.Abs(twice) * 0.5;
        }

        /// <summary>
        /// 画面に占める割合。**画面の面積が 0 なら 0**（0 除算で NaN を返さない）。
        /// </summary>
        public static double ScreenRatio(double areaPixels, double screenWidth, double screenHeight)
        {
            double screen = screenWidth * screenHeight;
            return screen > 0.0 ? areaPixels / screen : 0.0;
        }

        /// <summary>
        /// 凸包の頂点の添字（反時計回り）。**Andrew の monotone chain。**
        /// 同じ点が混じっていても落ちない。
        /// </summary>
        public static List<int> ConvexHull(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
        {
            if (xs == null || ys == null)
            {
                throw new ArgumentNullException(nameof(xs));
            }

            if (xs.Count != ys.Count)
            {
                throw new ArgumentException("x と y の個数が違う");
            }

            int n = xs.Count;
            var result = new List<int>();
            if (n < 3)
            {
                return result;
            }

            var order = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                order.Add(i);
            }

            order.Sort((a, b) => xs[a] != xs[b] ? xs[a].CompareTo(xs[b]) : ys[a].CompareTo(ys[b]));

            var lower = new List<int>();
            foreach (int i in order)
            {
                while (lower.Count >= 2
                       && Cross(xs, ys, lower[lower.Count - 2], lower[lower.Count - 1], i) <= 0.0)
                {
                    lower.RemoveAt(lower.Count - 1);
                }

                lower.Add(i);
            }

            var upper = new List<int>();
            for (int k = order.Count - 1; k >= 0; k--)
            {
                int i = order[k];
                while (upper.Count >= 2
                       && Cross(xs, ys, upper[upper.Count - 2], upper[upper.Count - 1], i) <= 0.0)
                {
                    upper.RemoveAt(upper.Count - 1);
                }

                upper.Add(i);
            }

            // 端点が重複するので 1 個ずつ落とす。
            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);

            result.AddRange(lower);
            result.AddRange(upper);

            // 一直線のときは面積 0。頂点 2 個以下として返す。
            return result.Count >= 3 ? result : new List<int>();
        }

        static double Cross(IReadOnlyList<double> xs, IReadOnlyList<double> ys, int o, int a, int b)
            => ((xs[a] - xs[o]) * (ys[b] - ys[o])) - ((ys[a] - ys[o]) * (xs[b] - xs[o]));
    }
}
