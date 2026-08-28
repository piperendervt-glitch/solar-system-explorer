using System;
using System.Collections.Generic;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// 面ごとの逆歪ませ行列を、**メッシュから自動で出す** (Step 11-3c)。
    ///
    /// 手で数値を入れる箇所は 1 つも無い。11-6 で有料アセットへ差し替えても、
    /// 面が平面で UV が線形なら同じ手順でそのまま出る。
    /// **平面でなければ例外で止める**（黙って崩れるほうが困る）。
    /// </summary>
    public static class CockpitWarp
    {
        /// <summary>平面かどうかを見るときに拾う頂点の上限。</summary>
        const int SampleLimit = 256;

        public sealed class Face
        {
            public Matrix4x4 Warp;
            public Vector2 TextureScale;
            public Vector2 TargetSize;
            public double PlanarDeviation;
        }

        /// <summary>
        /// 面のメッシュと目の姿勢から、blit へ渡す行列を作る。
        ///
        /// contentAspect は**中身（描いた RT）の縦横比**。面の実寸の比と同じ。
        /// </summary>
        public static Face Solve(Renderer renderer, Transform eye, float contentAspect)
        {
            if (renderer == null || eye == null)
            {
                throw new ArgumentException("面と目が要る");
            }

            var filter = renderer.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || mesh.uv.Length < 3)
            {
                throw new InvalidOperationException($"{renderer.name}: UV を持つメッシュが無い");
            }

            // ---- 平面かどうか ----
            double deviation = ScreenWarpSolver.PlanarDeviation(Sample(mesh, renderer.transform));
            if (deviation > ScreenWarpSolver.PlanarToleranceRatio)
            {
                throw new InvalidOperationException(
                    $"{renderer.name}: 面が平面ではない（ずれ {deviation:P2} > "
                    + $"{ScreenWarpSolver.PlanarToleranceRatio:P2}）。逆歪ませは平面にしか使えない");
            }

            // ---- UV 単位正方形の 4 隅 -> 目のローカル ----
            UvBasis(mesh, out Vector3 p0, out Vector2 uv0, out Vector3 dpdu, out Vector3 dpdv,
                    out Vector2 uvMin, out Vector2 uvMax);

            var corners = new Vec3d[4];
            var st = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
            };

            for (int i = 0; i < 4; i++)
            {
                var uv = new Vector2(Mathf.Lerp(uvMin.x, uvMax.x, st[i].x),
                                     Mathf.Lerp(uvMin.y, uvMax.y, st[i].y));
                Vector3 local = p0 + (dpdu * (uv.x - uv0.x)) + (dpdv * (uv.y - uv0.y));
                Vector3 view = eye.InverseTransformPoint(renderer.transform.TransformPoint(local));
                corners[i] = new Vec3d(view.x, view.y, view.z);
            }

            ScreenWarpSolver.Result result = ScreenWarpSolver.Solve(corners, contentAspect);

            return new Face
            {
                Warp = ToMatrix(result.ToSource),
                TextureScale = new Vector2((float)result.TextureScale.X,
                                           (float)result.TextureScale.Y),
                TargetSize = new Vector2((float)result.TargetSize.X, (float)result.TargetSize.Y),
                PlanarDeviation = deviation,
            };
        }

        public static Matrix4x4 ToMatrix(Homography h)
        {
            IReadOnlyList<double> v = h.Values;
            var m = Matrix4x4.identity;
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    m[r, c] = (float)v[(r * 3) + c];
                }
            }

            return m;
        }

        /// <summary>
        /// UV から面上の位置への線形写像 (Step 11-3b の `TextureSizeFor` と同じ根拠)。
        /// **UV が面上の位置に対して線形であることは実測済み。**
        /// </summary>
        public static void UvBasis(Mesh mesh, out Vector3 p0, out Vector2 uv0,
                                   out Vector3 dpdu, out Vector3 dpdv,
                                   out Vector2 uvMin, out Vector2 uvMax)
        {
            Vector3[] p = mesh.vertices;
            Vector2[] uv = mesh.uv;

            int i1 = -1, i2 = -1;
            for (int i = 1; i < uv.Length && i2 < 0; i++)
            {
                Vector2 d1 = uv[i] - uv[0];
                if (d1.sqrMagnitude < 1e-10f)
                {
                    continue;
                }

                if (i1 < 0)
                {
                    i1 = i;
                    continue;
                }

                Vector2 a1 = uv[i1] - uv[0];
                if (Mathf.Abs((a1.x * d1.y) - (a1.y * d1.x)) > 1e-6f)
                {
                    i2 = i;
                }
            }

            if (i1 < 0 || i2 < 0)
            {
                throw new InvalidOperationException("UV が退化している（一次独立な 3 点が無い）");
            }

            Vector2 e1 = uv[i1] - uv[0];
            Vector2 e2 = uv[i2] - uv[0];
            Vector3 f1 = p[i1] - p[0];
            Vector3 f2 = p[i2] - p[0];
            float det = (e1.x * e2.y) - (e1.y * e2.x);

            p0 = p[0];
            uv0 = uv[0];
            dpdu = ((f1 * e2.y) - (f2 * e1.y)) / det;
            dpdv = ((f2 * e1.x) - (f1 * e2.x)) / det;

            float u0 = float.MaxValue, u1 = float.MinValue;
            float v0 = float.MaxValue, v1 = float.MinValue;
            foreach (Vector2 t in uv)
            {
                u0 = Mathf.Min(u0, t.x); u1 = Mathf.Max(u1, t.x);
                v0 = Mathf.Min(v0, t.y); v1 = Mathf.Max(v1, t.y);
            }

            uvMin = new Vector2(u0, v0);
            uvMax = new Vector2(u1, v1);
        }

        /// <summary>平面判定に使う頂点。**多い面では間引く**（判定は O(n^2)）。</summary>
        static List<Vec3d> Sample(Mesh mesh, Transform owner)
        {
            Vector3[] verts = mesh.vertices;
            int stride = Mathf.Max(1, verts.Length / SampleLimit);
            var points = new List<Vec3d>();
            for (int i = 0; i < verts.Length; i += stride)
            {
                Vector3 w = owner.TransformPoint(verts[i]);
                points.Add(new Vec3d(w.x, w.y, w.z));
            }

            return points;
        }
    }
}
