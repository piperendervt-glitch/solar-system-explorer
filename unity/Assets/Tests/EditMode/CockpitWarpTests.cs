using System;
using System.Collections.Generic;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Editor;
using SolarSystem.Unity;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// 逆歪ませを**実際のメッシュ越しに**縛る (Step 11-3c)。
    ///
    /// `ScreenWarpTests` は数学だけを見る。こちらは
    /// **中身の点 -> 逆歪ませ -> 面の UV -> 面上の位置 -> 目から見た方向**
    /// の全経路を、ベンダーのメッシュと組み上がった目の位置で通す。
    /// 行列の向き・順序・UV の張り方を取り違えていれば、ここで落ちる。
    /// </summary>
    public sealed class CockpitWarpTests
    {
        Transform _ship;

        [SetUp]
        public void SetUp() => _ship = new GameObject("TestShip").transform;

        [TearDown]
        public void TearDown()
        {
            if (_ship != null)
            {
                UnityEngine.Object.DestroyImmediate(_ship.gameObject);
            }
        }

        static void RequireImported()
        {
            if (!CockpitCatalog.IsAvailable(CockpitDefinition.HiRezSample))
            {
                Assert.Inconclusive("コックピットのアセットが取り込まれていない");
            }
        }

        [Test]
        public void 五面すべてで行列が出る()
        {
            RequireImported();

            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            Assert.That(hirez.Screens.Screens.Count, Is.EqualTo(5));

            foreach (CockpitScreens.Screen screen in hirez.Screens.Screens)
            {
                Assert.That(screen.Warped, Is.Not.Null, $"{screen.RendererName}: 出力先が無い");
                Assert.That(screen.WarpMaterial, Is.Not.Null,
                            $"{screen.RendererName}: blit のマテリアルが無い");
                Assert.That(screen.Warp, Is.Not.EqualTo(Matrix4x4.identity),
                            $"{screen.RendererName}: 行列が恒等のまま（解けていない）");

                // **圧縮される軸に積んである。** 縮めることはしない。
                Assert.That(screen.Warped.width, Is.GreaterThanOrEqualTo(screen.Texture.width - 4),
                            $"{screen.RendererName}: 歪ませたほうの幅が足りない");
                Assert.That(screen.Warped.height, Is.GreaterThanOrEqualTo(screen.Texture.height - 4),
                            $"{screen.RendererName}: 歪ませたほうの高さが足りない");
            }
        }

        [Test]
        public void 箱でも通る()
        {
            // 箱には画面が無い。**例外にならず、画面 0 枚で通ること。**
            // （11-6 でアセットを差し替えたクローンが箱へ落ちても止まらないように）
            CockpitBuilder.Result box = CockpitBuilder.Build(_ship, 9, CockpitDefinition.Box);
            Assert.That(box.Screens.Screens.Count, Is.EqualTo(0));
        }

        [Test]
        public void 曲面は例外で止まる()
        {
            // **11-6 で曲面の画面が来たときに黙って崩れないように。**
            var go = new GameObject("CurvedScreen");
            go.transform.SetParent(_ship, false);

            var mesh = new Mesh { name = "Curved" };
            mesh.vertices = new[]
            {
                new Vector3(-0.2f, -0.1f, 0f), new Vector3(0.2f, -0.1f, 0f),
                new Vector3(0.2f, 0.1f, 0f), new Vector3(-0.2f, 0.1f, 0.05f),
            };

            mesh.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
            };

            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            Renderer renderer = go.AddComponent<MeshRenderer>();

            var eye = new GameObject("Eye").transform;
            eye.SetParent(_ship, false);
            eye.localPosition = new Vector3(0f, 0f, -1f);

            Assert.That(() => CockpitWarp.Solve(renderer, eye, 1.88f),
                        Throws.InstanceOf<InvalidOperationException>());
        }

        [Test]
        public void 逆歪ませた円が画面上で真円になる()
        {
            RequireImported();

            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            foreach (CockpitScreens.Screen screen in hirez.Screens.Screens)
            {
                double before = Circularity(screen, hirez.CockpitCamera.transform, false);
                double after = Circularity(screen, hirez.CockpitCamera.transform, true);

                Assert.That(after, Is.GreaterThan(0.98),
                            $"{screen.RendererName}: 逆歪ませても円がつぶれている"
                            + $" (円形度 {after:F4} / 面に貼ると {before:F4})");
            }
        }

        [Test]
        public void 逆歪ませた格子が画面上で等間隔になる()
        {
            RequireImported();

            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            foreach (CockpitScreens.Screen screen in hirez.Screens.Screens)
            {
                // **台形が残っていれば、縦線の間隔が片側へ単調に詰まる。**
                double evenness = GridEvenness(screen, hirez.CockpitCamera.transform, true);
                Assert.That(evenness, Is.GreaterThan(0.98),
                            $"{screen.RendererName}: 格子が等間隔でない (最小/最大 {evenness:F4})");
            }
        }

        [Test]
        public void 面に貼ると円はつぶれ格子は詰まる()
        {
            RequireImported();

            // **測り方に効き目があることの担保。** 同じ経路で、逆歪ませを掛けない
            // ときは 5 面のうち少なくとも 1 面が明らかに崩れていること。
            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            double worstCircle = 1.0;
            double worstGrid = 1.0;
            foreach (CockpitScreens.Screen screen in hirez.Screens.Screens)
            {
                worstCircle = Math.Min(worstCircle,
                                       Circularity(screen, hirez.CockpitCamera.transform, false));
                worstGrid = Math.Min(worstGrid,
                                     GridEvenness(screen, hirez.CockpitCamera.transform, false));
            }

            Assert.That(worstCircle, Is.LessThan(0.95),
                        $"面に貼っても円のままなら、この測り方は歪みを見ていない ({worstCircle:F4})");
            Assert.That(worstGrid, Is.LessThan(0.99),
                        $"面に貼っても等間隔なら、この測り方は台形を見ていない ({worstGrid:F4})");
        }

        /// <summary>
        /// 中身の円を、**実際のメッシュ越しに**画面（視線方向）へ写したときの円形度。
        /// warped = false なら逆歪ませを通さない（面にそのまま貼った場合）。
        /// </summary>
        static double Circularity(CockpitScreens.Screen screen, Transform eye, bool warped)
        {
            double aspect = screen.Texture.width / (double)screen.Texture.height;
            var points = new List<Vector2>();
            const int steps = 64;

            for (int i = 0; i < steps; i++)
            {
                double a = (i / (double)steps) * Math.PI * 2.0;
                var q = new Vector2((float)(0.5 + (0.2 * Math.Cos(a))),
                                    (float)(0.5 + (0.2 * Math.Sin(a) * aspect)));
                points.Add(ToScreen(screen, eye, warped ? ToOutput(screen, q) : q));
            }

            Vector2 center = Vector2.zero;
            foreach (Vector2 p in points)
            {
                center += p;
            }

            center /= points.Count;

            double min = double.MaxValue, max = double.MinValue;
            foreach (Vector2 p in points)
            {
                double r = (p - center).magnitude;
                min = Math.Min(min, r); max = Math.Max(max, r);
            }

            return min / max;
        }

        /// <summary>縦線 8 本の間隔の、最小 / 最大。1.00 なら等間隔。</summary>
        static double GridEvenness(CockpitScreens.Screen screen, Transform eye, bool warped)
        {
            var xs = new List<float>();
            for (int i = 0; i <= 8; i++)
            {
                var q = new Vector2(i / 8f, 0.5f);
                xs.Add(ToScreen(screen, eye, warped ? ToOutput(screen, q) : q).x);
            }

            double min = double.MaxValue, max = double.MinValue;
            for (int i = 1; i < xs.Count; i++)
            {
                double gap = Math.Abs(xs[i] - xs[i - 1]);
                min = Math.Min(min, gap); max = Math.Max(max, gap);
            }

            return min / max;
        }

        /// <summary>中身の (s,t) が、出力 RT のどこに描かれるか（Warp の逆）。</summary>
        static Vector2 ToOutput(CockpitScreens.Screen screen, Vector2 source)
        {
            Homography toOutput = HomographyFrom(screen.Warp).Inverse();
            Vec2d p = toOutput.Apply(new Vec2d(source.x, source.y));
            return new Vector2((float)p.X, (float)p.Y);
        }

        /// <summary>
        /// 出力 RT の (s,t) が、**面の上のどこに貼られ、目からどの方向に見えるか。**
        /// 返すのは視線方向 (x/z, y/z)。画角と解像度に依存しない。
        /// </summary>
        static Vector2 ToScreen(CockpitScreens.Screen screen, Transform eye, Vector2 st)
        {
            Mesh mesh = screen.Target.GetComponent<MeshFilter>().sharedMesh;
            CockpitWarp.UvBasis(mesh, out Vector3 p0, out Vector2 uv0,
                                out Vector3 dpdu, out Vector3 dpdv,
                                out Vector2 uvMin, out Vector2 uvMax);

            var uv = new Vector2(Mathf.Lerp(uvMin.x, uvMax.x, st.x),
                                 Mathf.Lerp(uvMin.y, uvMax.y, st.y));
            Vector3 local = p0 + (dpdu * (uv.x - uv0.x)) + (dpdv * (uv.y - uv0.y));
            Vector3 view = eye.InverseTransformPoint(
                screen.Target.transform.TransformPoint(local));

            Assert.That(view.z, Is.GreaterThan(0f), $"{screen.RendererName}: 面が目の後ろにある");
            return new Vector2(view.x / view.z, view.y / view.z);
        }

        /// <summary>4x4 の左上 3x3 からホモグラフィを作る（単位正方形の像を経由）。</summary>
        static Homography HomographyFrom(Matrix4x4 m)
        {
            var corners = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f),
            };

            var mapped = new Vec2d[4];
            for (int i = 0; i < 4; i++)
            {
                float x = corners[i].x, y = corners[i].y;
                float w = (m.m20 * x) + (m.m21 * y) + m.m22;
                mapped[i] = new Vec2d(((m.m00 * x) + (m.m01 * y) + m.m02) / w,
                                      ((m.m10 * x) + (m.m11 * y) + m.m12) / w);
            }

            return Homography.FromUnitSquare(mapped);
        }
    }
}
