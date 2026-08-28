using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>2 次元の点。**UnityEngine 非依存**（Core の制約）。</summary>
    public readonly struct Vec2d
    {
        public Vec2d(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }

        public override string ToString() => $"({X:F4}, {Y:F4})";
    }

    /// <summary>
    /// 平面の射影変換 (Step 11-3c)。
    ///
    /// ■ **なぜこれで歪みが消えるのか**
    /// 計器の面は平面で、UV は面上の位置に対して線形（実測済み）。したがって
    /// 「RT の (s,t) → 目から見た方向」は
    ///   (s,t) →（アフィン）→ 面上の点 →（透視投影）→ 視線平面
    /// の合成で、**ホモグラフィ 1 枚**になる。中身をその逆で先に歪ませておけば、
    /// 画面の上では厳密に元へ戻る。**近似ではない。** 台形の歪みも縦横比の
    /// 潰れも、同じ 1 枚の行列に含まれている。
    ///
    /// ■ 座標系
    /// 目から見た方向は**正規化した (x/z, y/z)** で扱う。画角と解像度が決まれば
    /// 画素座標との間は**等方な拡大と平行移動だけ**なので、この空間で円なら
    /// 画面でも円になる。**解像度に依存しない**（CLAUDE.md の測定条件の原則）。
    /// </summary>
    public sealed class Homography
    {
        /// <summary>行優先の 3x3。</summary>
        readonly double[] _m;

        Homography(double[] m)
        {
            _m = m;
        }

        public IReadOnlyList<double> Values => _m;

        /// <summary>単位正方形 (0,0)-(1,0)-(1,1)-(0,1) を 4 点へ写す変換。</summary>
        public static Homography FromUnitSquare(IReadOnlyList<Vec2d> destination)
        {
            var square = new[]
            {
                new Vec2d(0.0, 0.0), new Vec2d(1.0, 0.0),
                new Vec2d(1.0, 1.0), new Vec2d(0.0, 1.0),
            };

            return FromCorrespondences(square, destination);
        }

        /// <summary>
        /// **4 点 DLT。** 8 つの未知数 (h33 = 1 に正規化) を 8 元 1 次で解く。
        /// 3 点以上が同一直線上にあるなど、解が定まらない配置では例外を投げる。
        /// </summary>
        public static Homography FromCorrespondences(
            IReadOnlyList<Vec2d> source, IReadOnlyList<Vec2d> destination)
        {
            if (source == null || destination == null
                || source.Count != 4 || destination.Count != 4)
            {
                throw new ArgumentException("4 点ずつの対応が要る");
            }

            var a = new double[8, 9];
            for (int i = 0; i < 4; i++)
            {
                double x = source[i].X, y = source[i].Y;
                double u = destination[i].X, v = destination[i].Y;

                a[i * 2, 0] = x; a[i * 2, 1] = y; a[i * 2, 2] = 1.0;
                a[i * 2, 6] = -u * x; a[i * 2, 7] = -u * y; a[i * 2, 8] = u;

                a[(i * 2) + 1, 3] = x; a[(i * 2) + 1, 4] = y; a[(i * 2) + 1, 5] = 1.0;
                a[(i * 2) + 1, 6] = -v * x; a[(i * 2) + 1, 7] = -v * y; a[(i * 2) + 1, 8] = v;
            }

            double[] h = SolveGauss(a);
            var result = new Homography(
                new[] { h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], 1.0 });

            // **解けても使えるとは限らない。** 3 点が一直線に並ぶ配置では 8 元 1 次は
            // 解けてしまうが、得られる 3x3 は特異で、逆向きに写せない（= 逆歪ませが
            // 作れない）。ここで弾いておかないと、崩れた絵が黙って出る。
            result.RequireInvertible();
            return result;
        }

        public Vec2d Apply(Vec2d p)
        {
            double w = (_m[6] * p.X) + (_m[7] * p.Y) + _m[8];
            if (Math.Abs(w) < 1e-12)
            {
                throw new InvalidOperationException("射影が破綻している (w = 0)");
            }

            return new Vec2d((((_m[0] * p.X) + (_m[1] * p.Y) + _m[2]) / w),
                             (((_m[3] * p.X) + (_m[4] * p.Y) + _m[5]) / w));
        }

        /// <summary>this のあとに other を掛ける（other ∘ this）。</summary>
        public Homography Then(Homography other)
        {
            var r = new double[9];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    double s = 0.0;
                    for (int k = 0; k < 3; k++)
                    {
                        s += other._m[(i * 3) + k] * _m[(k * 3) + j];
                    }

                    r[(i * 3) + j] = s;
                }
            }

            return new Homography(r);
        }

        /// <summary>特異なら例外。**逆向きに写せない変換は使わない。**</summary>
        public void RequireInvertible()
        {
            double scale = 0.0;
            foreach (double v in _m)
            {
                scale = Math.Max(scale, Math.Abs(v));
            }

            double[] m = _m;
            double det = (m[0] * ((m[4] * m[8]) - (m[5] * m[7])))
                         + (m[1] * ((m[5] * m[6]) - (m[3] * m[8])))
                         + (m[2] * ((m[3] * m[7]) - (m[4] * m[6])));

            if (Math.Abs(det) < 1e-9 * scale * scale * scale)
            {
                throw new InvalidOperationException(
                    "4 点の配置から変換を決められない（3 点が一直線上にある等）");
            }
        }

        public Homography Inverse()
        {
            double[] m = _m;
            double c00 = (m[4] * m[8]) - (m[5] * m[7]);
            double c01 = (m[5] * m[6]) - (m[3] * m[8]);
            double c02 = (m[3] * m[7]) - (m[4] * m[6]);
            double det = (m[0] * c00) + (m[1] * c01) + (m[2] * c02);
            if (Math.Abs(det) < 1e-15)
            {
                throw new InvalidOperationException("逆行列が無い (det = 0)");
            }

            var r = new double[9];
            r[0] = c00 / det;
            r[1] = ((m[2] * m[7]) - (m[1] * m[8])) / det;
            r[2] = ((m[1] * m[5]) - (m[2] * m[4])) / det;
            r[3] = c01 / det;
            r[4] = ((m[0] * m[8]) - (m[2] * m[6])) / det;
            r[5] = ((m[2] * m[3]) - (m[0] * m[5])) / det;
            r[6] = c02 / det;
            r[7] = ((m[1] * m[6]) - (m[0] * m[7])) / det;
            r[8] = ((m[0] * m[4]) - (m[1] * m[3])) / det;
            return new Homography(r);
        }

        /// <summary>軸に沿った矩形へ写すアフィン変換（ホモグラフィの特別な場合）。</summary>
        public static Homography Rectangle(double centerX, double centerY,
                                           double width, double height)
        {
            if (width <= 0.0 || height <= 0.0)
            {
                throw new ArgumentException("矩形の辺が 0 以下");
            }

            return new Homography(new[]
            {
                width, 0.0, centerX - (width * 0.5),
                0.0, height, centerY - (height * 0.5),
                0.0, 0.0, 1.0,
            });
        }

        static double[] SolveGauss(double[,] a)
        {
            const int n = 8;
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int row = col + 1; row < n; row++)
                {
                    if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col]))
                    {
                        pivot = row;
                    }
                }

                if (Math.Abs(a[pivot, col]) < 1e-12)
                {
                    throw new InvalidOperationException(
                        "4 点の配置から変換を決められない（3 点が一直線上にある等）");
                }

                if (pivot != col)
                {
                    for (int k = col; k <= n; k++)
                    {
                        double t = a[col, k];
                        a[col, k] = a[pivot, k];
                        a[pivot, k] = t;
                    }
                }

                for (int row = 0; row < n; row++)
                {
                    if (row == col)
                    {
                        continue;
                    }

                    double f = a[row, col] / a[col, col];
                    for (int k = col; k <= n; k++)
                    {
                        a[row, k] -= f * a[col, k];
                    }
                }
            }

            var x = new double[n];
            for (int i = 0; i < n; i++)
            {
                x[i] = a[i, n] / a[i, i];
            }

            return x;
        }
    }
}
