using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Editor;
using SolarSystem.Unity;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// 計器を映す 5 面 (Step 11-3a / 11-3b)。
    ///
    /// **A と B は実機で見比べるための一時的な足場。** 決まったら片方を削除する。
    /// </summary>
    public sealed class CockpitScreenTests
    {
        Transform _ship;

        [SetUp]
        public void SetUp() => _ship = new GameObject("TestShip").transform;

        [TearDown]
        public void TearDown()
        {
            if (_ship != null)
            {
                Object.DestroyImmediate(_ship.gameObject);
            }
        }

        static void RequireImported()
        {
            string path = AssetDatabase.GUIDToAssetPath(CockpitDefinition.HiRezSample.PrefabGuid);
            if (string.IsNullOrEmpty(path))
            {
                Assert.Inconclusive("まだ取り込まれていない（clone 直後は正常）。");
            }
        }

        static IReadOnlyList<CockpitScreen> ScreensOf(ScreenLayout layout)
            => CockpitDefinition.HiRezSample.Screens(layout);

        // ---- 割り当て (11-3a) ----

        [Test]
        public void 三案とも同じ5面を覆う()
        {
            string[] a = ScreensOf(ScreenLayout.A).Select(s => s.RendererName)
                .OrderBy(n => n).ToArray();

            Assert.That(a.Length, Is.EqualTo(5));

            foreach (ScreenLayout other in new[] { ScreenLayout.B, ScreenLayout.C })
            {
                string[] names = ScreensOf(other).Select(s => s.RendererName)
                    .OrderBy(n => n).ToArray();

                Assert.That(names, Is.EqualTo(a), $"案 {other} で対象の面が違う");
            }
        }

        [Test]
        public void 案Cの大画面は2行まで()
        {
            // **案 C の要点。** 大画面の字は行の高さで決まるので、3 行を 2 行に
            // 減らすことでしか大きくできない（実測から）。
            foreach (CockpitScreen screen in ScreensOf(ScreenLayout.C))
            {
                if (!screen.RendererName.Contains("Screen-"))
                {
                    continue;
                }

                Assert.That(screen.Role == ScreenRole.FlightShort
                            || screen.Role == ScreenRole.Target, Is.True,
                            $"{screen.RendererName} に 3 行の役割を割り当てている ({screen.Role})");
            }
        }

        [Test]
        public void 同じ面に2つの役割を割り当てていない()
        {
            foreach (ScreenLayout layout in new[] { ScreenLayout.A, ScreenLayout.B, ScreenLayout.C })
            {
                string[] duplicated = ScreensOf(layout)
                    .GroupBy(s => s.RendererName)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key)
                    .ToArray();

                Assert.That(duplicated, Is.Empty, $"{layout}: " + string.Join(", ", duplicated));
            }
        }

        [Test]
        public void 割り当てのレンダラー名が実在する()
        {
            RequireImported();

            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            string[] names = hirez.Identity.GetComponentsInChildren<Renderer>(true)
                .Select(r => r.name)
                .ToArray();

            foreach (CockpitScreen screen in ScreensOf(ScreenLayout.A))
            {
                Assert.That(names, Does.Contain(screen.RendererName),
                            "**プレハブに無いレンダラー名を指している: " + screen.RendererName);
            }
        }

        [Test]
        public void 箱には画面が無い()
        {
            Assert.That(CockpitDefinition.Box.Screens(ScreenLayout.A), Is.Empty);

            // **画面が無くても組み立ては通る。**
            CockpitBuilder.Result box = CockpitBuilder.Build(_ship, 9, CockpitDefinition.Box);
            Assert.That(box.Screens, Is.Not.Null);
            Assert.That(box.Screens.Screens, Is.Empty);
        }

        // ---- RT の解像度 (11-3b) ----

        /// <summary>投影サイズの実測 (基準 1920x1080 / 画角 60 度 / 目 (0, 0.43, -1.44))。</summary>
        static readonly Dictionary<string, Vector2Int> ProjectedPixels = new Dictionary<string, Vector2Int>
        {
            { "CockpitEquipments_Screen-1", new Vector2Int(351, 119) },
            { "CockpitEquipments_Screen-2", new Vector2Int(351, 119) },
            { "CockpitEquipments_HUD-2", new Vector2Int(188, 188) },
            { "CockpitEquipments_Gauge2-Screen", new Vector2Int(178, 139) },
            { "CockpitEquipments_Gauge1-Screen", new Vector2Int(98, 90) },
        };

        [Test]
        public void RTの長辺が投影サイズの2倍以上()
        {
            foreach (ScreenLayout layout in new[] { ScreenLayout.A, ScreenLayout.B, ScreenLayout.C })
            {
                foreach (CockpitScreen screen in ScreensOf(layout))
                {
                    Vector2Int projected = ProjectedPixels[screen.RendererName];
                    int longest = Mathf.Max(projected.x, projected.y);

                    Assert.That(screen.TextureLongSide, Is.GreaterThanOrEqualTo(longest * 2),
                                $"{screen.RendererName} の RT が足りない");
                }
            }
        }

        [Test]
        public void RTの縦横比が面の実寸に合っている()
        {
            RequireImported();

            // **投影サイズから決めてはいけない (11-3b の実測)。**
            // 実寸 345.4x183.5 mm の面に 2.667 の RT を貼って、文字が 0.706 倍に
            // つぶれていた。RT の比は面の実寸の比と一致していること。
            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            foreach (CockpitScreens.Screen screen in hirez.Screens.Screens)
            {
                Mesh mesh = screen.Target.GetComponent<MeshFilter>().sharedMesh;
                Vector2 uvSpan = UvSpan(mesh);
                Vector2 meters = MetersPerUv(mesh);

                float physical = (meters.x * uvSpan.x) / (meters.y * uvSpan.y);
                float texture = screen.Texture.width / (float)screen.Texture.height;

                Assert.That(texture, Is.EqualTo(physical).Within(0.02f * physical),
                            $"{screen.RendererName}: RT の比 {texture:F3} が"
                            + $" 面の実寸の比 {physical:F3} と合っていない");
            }
        }

        static Vector2 UvSpan(Mesh mesh)
        {
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            foreach (Vector2 t in mesh.uv)
            {
                u0 = Mathf.Min(u0, t.x); u1 = Mathf.Max(u1, t.x);
                v0 = Mathf.Min(v0, t.y); v1 = Mathf.Max(v1, t.y);
            }

            return new Vector2(u1 - u0, v1 - v0);
        }

        /// <summary>uv 1 単位あたり面上で何メートル進むか。**UV が線形なのは実測済み。**</summary>
        static Vector2 MetersPerUv(Mesh mesh)
        {
            Vector3[] p = mesh.vertices;
            Vector2[] uv = mesh.uv;
            int i1 = -1, i2 = -1;

            for (int i = 1; i < uv.Length && i2 < 0; i++)
            {
                Vector2 d = uv[i] - uv[0];
                if (d.sqrMagnitude < 1e-10f) { continue; }
                if (i1 < 0) { i1 = i; continue; }
                Vector2 a = uv[i1] - uv[0];
                if (Mathf.Abs((a.x * d.y) - (a.y * d.x)) > 1e-6f) { i2 = i; }
            }

            Vector2 e1 = uv[i1] - uv[0], e2 = uv[i2] - uv[0];
            Vector3 f1 = p[i1] - p[0], f2 = p[i2] - p[0];
            float det = (e1.x * e2.y) - (e1.y * e2.x);

            return new Vector2((((f1 * e2.y) - (f2 * e1.y)) / det).magnitude,
                               (((f2 * e1.x) - (f1 * e2.x)) / det).magnitude);
        }

        // ---- UV の写し取り (11-3b) ----

        [Test]
        public void UV矩形を0から1へ写す()
        {
            // 実測値で 3 件。u[0.004..0.500] / v[0.684..0.978] / u[0.810..0.926]
            UvRemap.ToUnit(0.004, 0.500, out double s1, out double o1);
            Assert.That(0.004 * s1 + o1, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(0.500 * s1 + o1, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(s1, Is.EqualTo(2.0161290322).Within(1e-6));

            UvRemap.ToUnit(0.684, 0.978, out double s2, out double o2);
            Assert.That(0.684 * s2 + o2, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(0.978 * s2 + o2, Is.EqualTo(1.0).Within(1e-9));

            UvRemap.ToUnit(0.810, 0.926, out double s3, out double o3);
            Assert.That(0.810 * s3 + o3, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(0.926 * s3 + o3, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void 幅が0のUVでも壊れない()
        {
            // **0 除算で NaN を撒かない。** 等倍で返す。
            UvRemap.ToUnit(0.5, 0.5, out double scale, out double offset);
            Assert.That(scale, Is.EqualTo(1.0));
            Assert.That(offset, Is.EqualTo(0.0));
        }

        // ---- 組み立て (11-3b) ----

        [Test]
        public void 面ごとに別のRTと別のSTを持つ()
        {
            RequireImported();

            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            IReadOnlyList<CockpitScreens.Screen> screens = hirez.Screens.Screens;
            Assert.That(screens.Count, Is.EqualTo(5));

            // **マテリアルを複製していないことの担保。**
            // 共有マテリアルのまま、面ごとに別の RT と別の ST を指していること。
            Assert.That(screens.Select(s => s.Texture).Distinct().Count(), Is.EqualTo(5),
                        "RT が使い回されている");

            Assert.That(screens.Select(s => s.BaseMapSt).Distinct().Count(), Is.EqualTo(5),
                        "ST が面ごとに違わない（UV の取り違え）");

            foreach (CockpitScreens.Screen screen in screens)
            {
                Assert.That(screen.Target, Is.Not.Null, screen.RendererName + " のレンダラーが無い");
                Assert.That(screen.CameraA, Is.Not.Null, "案 A の描画元が無い");
                Assert.That(screen.CameraB, Is.Not.Null, "案 B の描画元が無い");
                Assert.That(screen.CameraC, Is.Not.Null, "案 C の描画元が無い");

                // **常時有効にしない。** 描くのは CockpitScreens が 10 Hz で呼ぶ。
                Assert.That(screen.CameraA.enabled, Is.False, "案 A のカメラが有効のまま");
                Assert.That(screen.CameraB.enabled, Is.False, "案 B のカメラが有効のまま");
                Assert.That(screen.CameraC.enabled, Is.False, "案 C のカメラが有効のまま");
            }
        }

        /// <summary>
        /// **実際に出しうる文字列を全部並べる。** 想定最長だけでは足りない
        /// （11-3b で `113.4 deg` が枠から出た）。
        /// </summary>
        static IEnumerable<string> AllSamples(InstrumentPanel.Field field)
        {
            switch (field)
            {
                case InstrumentPanel.Field.Speed:
                    foreach (double v in new[] { 0.0, 0.999, 1.0, 100.0, 1e4,
                                                 UniverseConstants.BetaToKmPerSec(0.9) })
                    {
                        yield return InstrumentPanel.FormatSpeed(v);
                    }

                    break;

                case InstrumentPanel.Field.Distance:
                    foreach (double v in new[] { 0.0, 999.9, 1000.0, 1.234e4, 1.234e7, 1.234e10 })
                    {
                        yield return InstrumentPanel.FormatDistance(v);
                    }

                    break;

                case InstrumentPanel.Field.Eta:
                    foreach (double v in new[] { -1.0, 0.0, 59.0, 3599.0, 359999.0 })
                    {
                        yield return InstrumentPanel.FormatEta(v);
                    }

                    break;

                case InstrumentPanel.Field.Target:
                    yield return "EARTH STATION";
                    yield return "MARS STATION";
                    yield return "---";
                    break;

                case InstrumentPanel.Field.Alignment:
                    foreach (double v in new[] { 0.0, 8.2, 90.0, 113.4, 180.0 })
                    {
                        yield return InstrumentPanel.FormatAlignment(v);
                    }

                    break;

                case InstrumentPanel.Field.Docking:
                    foreach (DockingState state in System.Enum.GetValues(typeof(DockingState)))
                    {
                        yield return InstrumentPanel.FormatDocking(state);
                    }

                    break;

                case InstrumentPanel.Field.Dial:
                    for (int i = 0; i < SpeedDial.Steps.Count; i++)
                    {
                        yield return $"{i}/{SpeedDial.Steps.Count - 1}";
                    }

                    break;

                case InstrumentPanel.Field.Autopilot:
                    yield return "ON";
                    yield return "OFF";
                    break;

                case InstrumentPanel.Field.Warning:
                    yield return InstrumentPanel.WarningOnText;
                    yield return InstrumentPanel.WarningOffText;
                    break;
            }

            yield return "---";
        }

        [Test]
        public void 出しうる全文字列が枠に収まる()
        {
            RequireImported();

            // **「ちょうど入る」設計をやめた (11-3b)。**
            // 実機で `113.4 deg` が HUD の枠から出て切れた。想定最長で決めた大きさに
            // 余白が無かったため。ここでは**実際に出しうる文字列を全部**当てて確かめる。
            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            var overflowed = new List<string>();

            foreach (CockpitScreens.Screen screen in hirez.Screens.Screens)
            {
                foreach (ScreenLayout layout in
                         new[] { ScreenLayout.A, ScreenLayout.B, ScreenLayout.C })
                {
                    Camera source = screen.CameraFor(layout);
                    if (source == null)
                    {
                        continue;
                    }

                    foreach (TMPro.TextMeshProUGUI text in
                             source.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                    {
                        if (!System.Enum.TryParse(text.name, out InstrumentPanel.Field field))
                        {
                            continue;
                        }

                        var rect = text.rectTransform;
                        var canvas = rect.parent as RectTransform;
                        float width = canvas.sizeDelta.x * (rect.anchorMax.x - rect.anchorMin.x);

                        foreach (string sample in AllSamples(field))
                        {
                            float w = text.GetPreferredValues(sample, 0f, 0f).x;
                            if (w > width)
                            {
                                overflowed.Add($"{screen.RendererName}/{layout}/{field}"
                                               + $" \"{sample}\" {w:F0}px > 枠 {width:F0}px");
                            }
                        }
                    }
                }
            }

            Assert.That(overflowed, Is.Empty,
                        "枠から出る文字列がある: " + string.Join(" / ", overflowed));
        }

        [Test]
        public void 帯はまだ残っている()
        {
            RequireImported();

            // **撤去は 11-3c で別コミット。** 先に消すと戻すのが面倒なので、
            // 新しい 5 面と並存させて見比べる。
            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            bool found = hirez.Identity.GetComponentsInChildren<Transform>(true)
                .Any(t => t.name == "InstrumentSurface");

            Assert.That(found, Is.True, "帯が消えている（撤去は 11-3c のはず）");
        }
        [Test]
        public void 正対クアッドは目の側から見える()
        {
            RequireImported();

            // **巻き順を取り違えると「有効なのに 1 画素も出ない」形で失敗する。**
            // 実測でそうなった（5 枚とも enabled=True・位置も正しいのに、
            // 面に貼った絵と 1 画素も違わなかった）。両面であることを縛る。
            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            foreach (CockpitScreens.Screen screen in hirez.Screens.Screens)
            {
                Renderer facing = screen.Facing;
                Assert.That(facing, Is.Not.Null, $"{screen.RendererName}: 正対クアッドが無い");

                Mesh mesh = facing.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.triangles.Length, Is.EqualTo(12),
                            $"{screen.RendererName}: 正対クアッドが片面になっている");

                // 目より前にあること（カメラのローカルで z > 0）。
                Transform eye = hirez.CockpitCamera.transform;
                Vector3 view = eye.InverseTransformPoint(facing.transform.position);
                Assert.That(view.z, Is.GreaterThan(0f),
                            $"{screen.RendererName}: 正対クアッドが目の後ろにある");

                // **RT の縦横比のまま内接していること (Step 11-3b)。**
                // 外接のまま引き伸ばすと、正対にしても中身が潰れたままになる。
                Bounds b = mesh.bounds;
                float quad = b.size.x / b.size.y;
                float texture = screen.Texture.width / (float)screen.Texture.height;
                Assert.That(quad, Is.EqualTo(texture).Within(0.02f * texture),
                            $"{screen.RendererName}: クアッドの比 {quad:F3} が"
                            + $" RT の比 {texture:F3} と合っていない");
            }
        }
        [Test]
        public void 計器の描画元は原点の近くに置く()
        {
            RequireImported();

            // **遠くに置くと、船を回しただけで絵が変わる (Step 11-3c で実測)。**
            //
            // Canvas は船の子なので、船が回ると世界座標が動く。float の刻みは
            // 原点からの距離で決まるので、遠いほど丸めが粗くなり、**中身を
            // 何も変えていないのに描かれる絵が変わる。**
            // 以前は原点から 141,528 unit の位置に置いており、刻み 0.0156 unit
            // = Canvas の 8.5 画素だった。船を 0.5 度回すだけで RT の
            // 48,620 画素が変わり、円が 32 px 動いた。
            CockpitBuilder.Result hirez =
                CockpitBuilder.Build(_ship, 9, CockpitDefinition.HiRezSample);

            foreach (CockpitScreens.Screen screen in hirez.Screens.Screens)
            {
                foreach (Camera cam in new[]
                         { screen.CameraA, screen.CameraB, screen.CameraC,
                           screen.CameraPattern })
                {
                    if (cam == null)
                    {
                        continue;
                    }

                    float distance = cam.transform.position.magnitude;

                    // その距離での float の刻み（仮数 23 bit）。
                    float ulp = Mathf.Pow(2f, Mathf.Floor(Mathf.Log(distance, 2f)) - 23f);

                    // Canvas の 1 画素 [unit]。カメラは正射影で高さ 1 unit。
                    float canvasPixel = 1f / screen.Texture.height;

                    Assert.That(ulp, Is.LessThan(canvasPixel * 0.1f),
                                $"{screen.RendererName} の {cam.name} が遠すぎる"
                                + $" ({distance:F0} unit / 刻み {ulp:E2} unit ="
                                + $" Canvas の {ulp / canvasPixel:F2} 画素)");
                }
            }
        }
    }
}