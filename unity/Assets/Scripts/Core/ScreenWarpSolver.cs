using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// 画面の逆歪ませ (Step 11-3c)。**UnityEngine 非依存。**
    ///
    /// 面に貼った RT が斜めから見て歪むぶんを、**RT の中身をあらかじめ逆に歪ませて**
    /// 打ち消す。プロジェクタの台形補正と同じ問題で、面が平面なら厳密に解ける。
    ///
    /// ■ 手順
    ///   1. RT の 4 隅（UV 単位正方形の 4 隅）を面上の点として求める
    ///   2. 目から見た方向 (x/z, y/z) へ投影する
    ///   3. 4 点 DLT で「RT の (s,t) → 視線方向」のホモグラフィ H を得る
    ///   4. 「こう見えてほしい」軸沿いの矩形 R を決める（中身の縦横比を保つ）
    ///   5. **M = R⁻¹ ∘ H** を blit のシェーダへ渡す。
    ///      出力の (s,t) は、入力の M(s,t) を読む
    ///
    /// ■ 前提は**平面であること**。曲面では H が定義できないので例外で止める
    ///   （11-6 で曲面の画面が来たときに黙って崩れないように）。
    /// </summary>
    public static class ScreenWarpSolver
    {
        /// <summary>平面と見なす許容。面の最大寸法に対する比。</summary>
        public const double PlanarToleranceRatio = 0.002;

        /// <summary>RT を積む倍率の上限。**これ以上は解像度で殴らない。**</summary>
        public const double MaxTextureScale = 2.0;

        public sealed class Result
        {
            /// <summary>出力 (s,t) → 入力 (s,t)。blit のシェーダへ渡す。</summary>
            public Homography ToSource { get; internal set; }

            /// <summary>視線方向での 4 隅（面に貼ったときの見え方）。</summary>
            public IReadOnlyList<Vec2d> ProjectedCorners { get; internal set; }

            /// <summary>「こう見えてほしい」矩形（中心 x, y と 幅, 高さ）。</summary>
            public Vec2d TargetCenter { get; internal set; }

            public Vec2d TargetSize { get; internal set; }

            /// <summary>RT をこの倍率で積む。**圧縮される軸だけが 1 を超える。**</summary>
            public Vec2d TextureScale { get; internal set; }
        }

        /// <summary>
        /// 面の 4 隅（目のローカル座標、メートル）と中身の縦横比から解く。
        ///
        /// 隅の並びは RT の (0,0) → (1,0) → (1,1) → (0,1)。
        /// </summary>
        public static Result Solve(IReadOnlyList<Vec3d> cornersInEyeSpace, double contentAspect)
        {
            if (cornersInEyeSpace == null || cornersInEyeSpace.Count != 4)
            {
                throw new ArgumentException("4 隅が要る");
            }

            if (contentAspect <= 0.0)
            {
                throw new ArgumentException("中身の縦横比が 0 以下");
            }

            var projected = new Vec2d[4];
            for (int i = 0; i < 4; i++)
            {
                Vec3d c = cornersInEyeSpace[i];
                if (c.Z <= 1e-6)
                {
                    throw new InvalidOperationException(
                        $"面の隅が目の前に無い (z = {c.Z:F4})。逆歪ませは使えない");
                }

                projected[i] = new Vec2d(c.X / c.Z, c.Y / c.Z);
            }

            Homography h = Homography.FromUnitSquare(projected);

            // 見えてほしい矩形は、**投影された 4 点の外接矩形へ内接**させる。
            // 外接のまま伸ばすと中身が潰れたままになる（正対クアッドで実測済み）。
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;
            foreach (Vec2d p in projected)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }

            double width = maxX - minX;
            double height = maxY - minY;
            if (width / height > contentAspect)
            {
                width = height * contentAspect;
            }
            else
            {
                height = width / contentAspect;
            }

            double cx = (minX + maxX) * 0.5;
            double cy = (minY + maxY) * 0.5;
            Homography rect = Homography.Rectangle(cx, cy, width, height);

            // M = R⁻¹ ∘ H。出力 (s,t) を H で視線方向へ写し、R⁻¹ で入力の (s,t) へ戻す。
            Homography toSource = h.Then(rect.Inverse());

            return new Result
            {
                ToSource = toSource,
                ProjectedCorners = projected,
                TargetCenter = new Vec2d(cx, cy),
                TargetSize = new Vec2d(width, height),
                TextureScale = ScaleFor(toSource),
            };
        }

        /// <summary>
        /// **圧縮される軸を測る。** 入力の全面 (0..1)^2 が出力のどれだけの範囲に
        /// 収まるかを見る。狭いほど中身が詰め込まれるので、そのぶん RT を積む。
        /// </summary>
        static Vec2d ScaleFor(Homography toSource)
        {
            Homography toOutput = toSource.Inverse();
            double minX = double.MaxValue, maxX = double.MinValue;
            double minY = double.MaxValue, maxY = double.MinValue;

            var corners = new[]
            {
                new Vec2d(0.0, 0.0), new Vec2d(1.0, 0.0),
                new Vec2d(1.0, 1.0), new Vec2d(0.0, 1.0),
            };

            foreach (Vec2d c in corners)
            {
                Vec2d p = toOutput.Apply(c);
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }

            double sx = Clamp(1.0 / Math.Max(1e-6, maxX - minX));
            double sy = Clamp(1.0 / Math.Max(1e-6, maxY - minY));
            return new Vec2d(sx, sy);
        }

        static double Clamp(double v) => Math.Min(MaxTextureScale, Math.Max(1.0, v));

        /// <summary>
        /// 平面からのずれ（最大距離）。**面の最大寸法に対する比で返す。**
        /// 曲面をそのまま逆歪ませると黙って崩れるので、呼ぶ側で必ず見る。
        /// </summary>
        public static double PlanarDeviation(IReadOnlyList<Vec3d> points)
        {
            if (points == null || points.Count < 3)
            {
                throw new ArgumentException("3 点以上が要る");
            }

            // 最も離れた 2 点と、その直線から最も離れた 1 点で法線を作る。
            Vec3d a = points[0], b = points[0];
            double best = -1.0;
            foreach (Vec3d p in points)
            {
                foreach (Vec3d q in points)
                {
                    double d = (p - q).SqrMagnitude;
                    if (d > best)
                    {
                        best = d; a = p; b = q;
                    }
                }
            }

            double span = Math.Sqrt(Math.Max(best, 1e-12));
            Vec3d axis = (b - a) / span;

            Vec3d normal = Vec3d.Zero;
            double bestArea = 0.0;
            foreach (Vec3d p in points)
            {
                Vec3d n = Vec3d.Cross(axis, p - a);
                if (n.Magnitude > bestArea)
                {
                    bestArea = n.Magnitude;
                    normal = n;
                }
            }

            if (bestArea <= 1e-12)
            {
                return 0.0; // 全点が一直線 = 平面ではあるが面として使えない
            }

            normal = normal.Normalized;
            double maxDeviation = 0.0;
            foreach (Vec3d p in points)
            {
                maxDeviation = Math.Max(maxDeviation, Math.Abs(Vec3d.Dot(p - a, normal)));
            }

            return maxDeviation / span;
        }
    }
}
