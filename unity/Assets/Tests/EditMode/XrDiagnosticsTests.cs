using System;
using System.Collections.Generic;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Editor;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// XR 診断の道具 (Step 12 の準備 / 平面版)。
    ///
    /// **絵の良し悪しは判断しない。** 縛るのは「数えられること」と
    /// 「壊したら数字が動くこと」だけ。
    /// </summary>
    public sealed class XrDiagnosticsTests
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

        // ---- 分類 ----

        [Test]
        public void 参照色は色相が離れている()
        {
            // **近い色相で置くと分類が隣へ寄る。** 最初 37 度差の組があって、
            // 許容 30 度との差が 7 度しか無かった。
            Assert.That(XrDiagnosticsModel.MinimumHueSeparation(),
                        Is.GreaterThan(XrDiagnosticsModel.HueTolerance * 2.0),
                        "参照色どうしが近すぎる");
        }

        [Test]
        public void 参照色はそれぞれ自分の層に分類される()
        {
            for (int i = 0; i < XrDiagnosticsModel.ProbeColor.Length; i++)
            {
                double[] c = XrDiagnosticsModel.ProbeColor[i];
                XrLayer? layer = XrDiagnosticsModel.Classify(c[0], c[1], c[2]);

                Assert.That(layer, Is.EqualTo((XrLayer)i),
                            XrDiagnosticsModel.LayerNames[i] + " が自分の層に分類されない");
            }
        }

        [Test]
        public void 暗い画素と灰色はどの層でもない()
        {
            Assert.That(XrDiagnosticsModel.Classify(0.02, 0.0, 0.02), Is.Null, "暗い画素を拾っている");
            Assert.That(XrDiagnosticsModel.Classify(0.8, 0.8, 0.8), Is.Null, "白を拾っている");
            Assert.That(XrDiagnosticsModel.Classify(0.5, 0.48, 0.46), Is.Null, "灰色を拾っている");
        }

        [Test]
        public void 彩度が落ちても色相が残れば分類できる()
        {
            // ACES を通ると彩度は落ちる。**色相さえ残れば拾える**ことを縛る。
            for (int i = 0; i < XrDiagnosticsModel.ProbeColor.Length; i++)
            {
                double[] c = XrDiagnosticsModel.ProbeColor[i];
                double gray = 0.35;

                // 参照色と灰色を混ぜて彩度だけ落とす。
                double r = (c[0] * 0.6) + gray;
                double g = (c[1] * 0.6) + gray;
                double b = (c[2] * 0.6) + gray;

                Assert.That(XrDiagnosticsModel.Classify(r, g, b), Is.EqualTo((XrLayer)i),
                            XrDiagnosticsModel.LayerNames[i] + " が彩度低下で分類できない");
            }
        }

        [Test]
        public void プローブは差分の画素だけを数える()
        {
            // 3x1 の画面。まん中だけがプローブ、両端は「場面の色」。
            double[] deep = XrDiagnosticsModel.ProbeColor[(int)XrLayer.Deep];
            var rgb = new byte[]
            {
                (byte)(deep[0] * 255), (byte)(deep[1] * 255), (byte)(deep[2] * 255),
                (byte)(deep[0] * 255), (byte)(deep[1] * 255), (byte)(deep[2] * 255),
                (byte)(deep[0] * 255), (byte)(deep[1] * 255), (byte)(deep[2] * 255),
            };

            List<XrDiagnosticsModel.ProbeHit> all = XrDiagnosticsModel.MeasureProbes(rgb, 3, 1);
            Assert.That(all[(int)XrLayer.Deep].Count, Is.EqualTo(3), "全部を数えていない");

            var only = new[] { false, true, false };
            List<XrDiagnosticsModel.ProbeHit> masked =
                XrDiagnosticsModel.MeasureProbes(rgb, 3, 1, only);

            Assert.That(masked[(int)XrLayer.Deep].Count, Is.EqualTo(1),
                        "**場面の色まで数えている**（プローブの差分だけを見ること）");
            Assert.That(masked[(int)XrLayer.Deep].CenterX, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void 見つからない層の重心は0ではなく負()
        {
            var rgb = new byte[3];
            List<XrDiagnosticsModel.ProbeHit> hits = XrDiagnosticsModel.MeasureProbes(rgb, 1, 1);

            foreach (XrDiagnosticsModel.ProbeHit hit in hits)
            {
                Assert.That(hit.Count, Is.EqualTo(0));
                Assert.That(hit.CenterX, Is.LessThan(0.0), "0,0 と見分けが付かない");
            }
        }

        [Test]
        public void 画素が足りなければ例外()
        {
            Assert.That(() => XrDiagnosticsModel.MeasureProbes(new byte[3], 4, 4),
                        Throws.InstanceOf<ArgumentException>());
            Assert.That(() => XrDiagnosticsModel.MeasureProbes(null, 1, 1),
                        Throws.InstanceOf<ArgumentException>());
        }

        // ---- 領域の勘定 ----

        [Test]
        public void 漏れは縁の帯と内側を分けて数える()
        {
            // 4x1: [窓][盤の縁][盤の内側][盤の内側]
            var deep = new[] { true, true, true, false };
            var window = new[] { true, false, false, false };
            var panel = new[] { false, true, true, true };
            var band = new[] { false, true, false, false };
            var outside = new[] { true, true, true, false };

            XrDiagnosticsModel.LeakResult r =
                XrDiagnosticsModel.MeasureLeak(deep, window, panel, band, outside);

            Assert.That(r.PanelDeepEdgeBand, Is.EqualTo(1), "縁の帯を分けていない");
            Assert.That(r.PanelDeepInterior, Is.EqualTo(1), "内側の漏れが合わない");
            Assert.That(r.PanelOutsideEdgeBand, Is.EqualTo(1));
            Assert.That(r.PanelOutsideInterior, Is.EqualTo(1));

            // 窓 = 盤の外。ガラスのメッシュでは定義しない（実測でそう変えた）。
            Assert.That(r.WindowOutside, Is.EqualTo(1));
            Assert.That(r.WindowDeep, Is.EqualTo(1));
        }

        [Test]
        public void マスクの長さが違えば例外()
        {
            Assert.That(() => XrDiagnosticsModel.MeasureLeak(
                            new bool[4], new bool[4], new bool[3], new bool[4]),
                        Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void 縁と膨張()
        {
            // 4x4 の中央 2x2 を塗る。
            var mask = new bool[16];
            mask[5] = mask[6] = mask[9] = mask[10] = true;

            bool[] edge = XrDiagnosticsModel.Boundary(mask, 4, 4);
            Assert.That(CountTrue(edge), Is.EqualTo(4), "2x2 は全部が縁のはず");

            bool[] grown = XrDiagnosticsModel.Dilate(mask, 4, 4, 1);
            Assert.That(CountTrue(grown), Is.EqualTo(16), "1 画素太らせれば 4x4 を覆う");
        }

        [Test]
        public void 多角形を塗る()
        {
            var square = new List<Vec2d>
            {
                new Vec2d(1.0, 1.0), new Vec2d(3.0, 1.0),
                new Vec2d(3.0, 3.0), new Vec2d(1.0, 3.0),
            };

            bool[] mask = XrDiagnosticsModel.FillPolygon(square, 4, 4);
            Assert.That(CountTrue(mask), Is.EqualTo(4), "2x2 の矩形にならない");
            Assert.That(XrDiagnosticsModel.FillPolygon(null, 4, 4).Length, Is.EqualTo(16));
        }

        static int CountTrue(bool[] mask)
        {
            int n = 0;
            foreach (bool b in mask)
            {
                if (b) { n++; }
            }

            return n;
        }

        // ---- 左右比 (Step 12 の本番用) ----

        [Test]
        public void 同じ絵を2枚渡すと左右比は1になる()
        {
            // **Q1 が問うのは絶対量ではなく比。** 平面では左右が無いので、
            // 同じ絵を 2 枚渡す。ここが 1.0 でなければ数え方が壊れている。
            var rgb = new byte[3 * 4];
            for (int i = 0; i < 4; i++)
            {
                double[] c = XrDiagnosticsModel.ProbeColor[i];
                rgb[(i * 3) + 0] = (byte)(c[0] * 255);
                rgb[(i * 3) + 1] = (byte)(c[1] * 255);
                rgb[(i * 3) + 2] = (byte)(c[2] * 255);
            }

            List<XrDiagnosticsModel.StereoProbeHit> stereo =
                XrDiagnosticsModel.MeasureStereo(rgb, rgb, 4, 1);

            Assert.That(stereo.Count, Is.EqualTo(4));
            foreach (XrDiagnosticsModel.StereoProbeHit hit in stereo)
            {
                Assert.That(hit.Left, Is.EqualTo(hit.Right));
                Assert.That(hit.Ratio, Is.EqualTo(1.0).Within(0.0),
                            "同じ絵なのに左右比が 1.0 でない");
                Assert.That(hit.OneEyeOnly, Is.False);
            }
        }

        [Test]
        public void 片目落ちは比が0になる()
        {
            double[] c = XrDiagnosticsModel.ProbeColor[(int)XrLayer.Cockpit];
            var left = new[] { (byte)(c[0] * 255), (byte)(c[1] * 255), (byte)(c[2] * 255) };
            var right = new byte[3];

            List<XrDiagnosticsModel.StereoProbeHit> stereo =
                XrDiagnosticsModel.MeasureStereo(left, right, 1, 1);

            XrDiagnosticsModel.StereoProbeHit hit = stereo[(int)XrLayer.Cockpit];
            Assert.That(hit.Left, Is.EqualTo(1));
            Assert.That(hit.Right, Is.EqualTo(0));
            Assert.That(hit.Ratio, Is.EqualTo(0.0).Within(1e-12),
                        "**片目落ちの signature** が比に出ていない");
            Assert.That(hit.OneEyeOnly, Is.True);
        }

        [Test]
        public void どちらの目にも出ていなければ比は1()
        {
            // 「両目とも 0」は差が無い状態。**0 除算にしない。**
            List<XrDiagnosticsModel.StereoProbeHit> stereo =
                XrDiagnosticsModel.MeasureStereo(new byte[3], new byte[3], 1, 1);

            foreach (XrDiagnosticsModel.StereoProbeHit hit in stereo)
            {
                Assert.That(hit.Ratio, Is.EqualTo(1.0).Within(0.0));
                Assert.That(hit.OneEyeOnly, Is.False);
            }
        }

        // ---- 計器盤の不変条件 ----

        [Test]
        public void 計器盤の画素数が範囲を外れたら失敗する()
        {
            // **「窓 = 計器盤の外」の定義には副作用がある。**
            // 内装の描画が丸ごと失敗すると窓が画面全体に広がり、
            // 漏れが 0 になって「正常」に見えてしまう。盤の面積そのものを縛る。
            const int total = 230400;

            Assert.That(XrDiagnosticsModel.PanelWithinBudget(15269, total), Is.True,
                        "実測値 (cockpit-view / 6.6 %) が範囲外");

            Assert.That(XrDiagnosticsModel.PanelWithinBudget(0, total), Is.False,
                        "**内装が消えても通ってしまう**");
            Assert.That(XrDiagnosticsModel.PanelWithinBudget(total, total), Is.False,
                        "**盤が画面全体でも通ってしまう**");
            Assert.That(XrDiagnosticsModel.PanelWithinBudget(100, 0), Is.False);
        }

        // ---- 片目の故意破壊（口だけ / 平面では no-op）----

        [Test]
        public void 片目の故意破壊は平面では何もしない()
        {
            CameraStackController stack = BuildStack();
            var go = new GameObject("XrDiagnostics");
            go.transform.SetParent(_ship, false);
            var diagnostics = go.AddComponent<XrDiagnostics>();
            diagnostics.Bind(stack, null, null, Array.Empty<XrDiagnostics.Probe>());

            int deepMask = stack.Deep.cullingMask;
            int cockpitMask = stack.Cockpit.cullingMask;
            CameraClearFlags clear = stack.Deep.clearFlags;

            foreach (XrDiagnostics.Fault fault in new[]
                     {
                         XrDiagnostics.Fault.DropLayerInOneEye,
                         XrDiagnostics.Fault.SkipDepthClearInOneEye,
                     })
            {
                diagnostics.SetFault(fault, XrLayer.Cockpit, XrDiagnostics.Eye.Left);

                // **平面では効かない。** 動かそうとしないこと（口だけ先に切ってある）。
                Assert.That(diagnostics.PerEyeFaultApplied, Is.False,
                            "平面なのに片目の故意破壊が効いている");
                Assert.That(stack.Deep.cullingMask, Is.EqualTo(deepMask));
                Assert.That(stack.Cockpit.cullingMask, Is.EqualTo(cockpitMask));
                Assert.That(stack.Deep.clearFlags, Is.EqualTo(clear));
                Assert.That(stack.Deep.enabled, Is.True);

                diagnostics.ClearFault();
            }
        }

        [Test]
        public void Deepを壊す口が3種類ある()
        {
            // **culling mask を空にしても星空は消えない (実測 / 0b)。**
            // スカイボックスは clearFlags で描かれるので mask の対象外。
            // Deep 段を壊すには別の口が要る。
            CameraStackController stack = BuildStack();
            var go = new GameObject("XrDiagnostics");
            go.transform.SetParent(_ship, false);
            var diagnostics = go.AddComponent<XrDiagnostics>();
            diagnostics.Bind(stack, null, null, Array.Empty<XrDiagnostics.Probe>());

            CameraClearFlags clear = stack.Deep.clearFlags;

            diagnostics.SetFault(XrDiagnostics.Fault.SkyboxOff, XrLayer.Deep);
            Assert.That(stack.Deep.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            diagnostics.ClearFault();
            Assert.That(stack.Deep.clearFlags, Is.EqualTo(clear), "戻せていない");

            diagnostics.SetFault(XrDiagnostics.Fault.BaseCameraOff, XrLayer.Deep);
            Assert.That(stack.Deep.enabled, Is.False);
            diagnostics.ClearFault();
            Assert.That(stack.Deep.enabled, Is.True, "戻せていない");

            Assert.That(XrDiagnostics.ParseFault("skyboxoff"),
                        Is.EqualTo(XrDiagnostics.Fault.SkyboxOff));
            Assert.That(XrDiagnostics.ParseFault("basecameraoff"),
                        Is.EqualTo(XrDiagnostics.Fault.BaseCameraOff));
        }

        // ---- プローブの組み立て ----

        [Test]
        public void プローブは層ごとに1つずつ既定で非表示()
        {
            CameraStackController stack = BuildStack();

            List<XrDiagnostics.Probe> probes = CockpitBuilder.BuildProbes(stack, 8, 0, 10, 9);

            Assert.That(probes.Count, Is.EqualTo(4), "層の数と合わない");

            var seen = new HashSet<XrLayer>();
            foreach (XrDiagnostics.Probe probe in probes)
            {
                Assert.That(seen.Add(probe.Layer), Is.True, "同じ層に 2 つある");
                Assert.That(probe.Renderer, Is.Not.Null);
                Assert.That(probe.Renderer.enabled, Is.False, "**既定は非表示**のはず");

                double[] c = XrDiagnosticsModel.ProbeColor[(int)probe.Layer];
                Color color = probe.Renderer.sharedMaterial.GetColor("_BaseColor");

                Assert.That(color.r, Is.EqualTo((float)c[0]).Within(1e-3f),
                            "マテリアルの色が定数と違う（古いアセットが残っている）");
                Assert.That(color.maxColorComponent, Is.LessThan(0.90f),
                            "bloom のしきい値 0.90 を超えると白へ寄って分類できない");
            }
        }

        [Test]
        public void プローブはそれぞれの段のカメラの子にある()
        {
            CameraStackController stack = BuildStack();
            List<XrDiagnostics.Probe> probes = CockpitBuilder.BuildProbes(stack, 8, 0, 10, 9);

            foreach (XrDiagnostics.Probe probe in probes)
            {
                Camera expected = probe.Layer == XrLayer.Deep ? stack.Deep
                    : probe.Layer == XrLayer.Near ? stack.Near
                    : probe.Layer == XrLayer.Nearfield ? stack.Nearfield
                    : stack.Cockpit;

                Assert.That(probe.Renderer.transform.parent, Is.EqualTo(expected.transform),
                            XrDiagnosticsModel.LayerNames[(int)probe.Layer]
                            + " が自分の段のカメラの子でない");

                // その段のクリップ範囲の内側にあること。
                float distance = probe.Renderer.transform.localPosition.z;
                Assert.That(distance, Is.GreaterThan(expected.nearClipPlane));
                Assert.That(distance, Is.LessThan(expected.farClipPlane));
            }
        }

        // ---- 故意破壊 ----

        [Test]
        public void 層アイソレーションはマスクだけを触り元に戻せる()
        {
            CameraStackController stack = BuildStack();
            var go = new GameObject("XrDiagnostics");
            go.transform.SetParent(_ship, false);
            var diagnostics = go.AddComponent<XrDiagnostics>();
            diagnostics.Bind(stack, null, null, Array.Empty<XrDiagnostics.Probe>());

            int deepMask = stack.Deep.cullingMask;
            int cockpitMask = stack.Cockpit.cullingMask;

            diagnostics.SetIsolation(1); // Deep だけ
            Assert.That(stack.Deep.cullingMask, Is.EqualTo(deepMask));
            Assert.That(stack.Cockpit.cullingMask, Is.EqualTo(0));

            // **カメラは止めない。** 段ごと止めるとポストの掛かり方が変わる。
            Assert.That(stack.Cockpit.enabled, Is.True, "カメラを止めている");

            diagnostics.SetIsolation(-1); // Deep だけ隠す
            Assert.That(stack.Deep.cullingMask, Is.EqualTo(0));
            Assert.That(stack.Cockpit.cullingMask, Is.EqualTo(cockpitMask));

            diagnostics.SetIsolation(0);
            Assert.That(stack.Deep.cullingMask, Is.EqualTo(deepMask));
            Assert.That(stack.Cockpit.cullingMask, Is.EqualTo(cockpitMask));
        }

        [Test]
        public void 故意破壊は既定でOFFで戻せる()
        {
            CameraStackController stack = BuildStack();
            var go = new GameObject("XrDiagnostics");
            go.transform.SetParent(_ship, false);
            var diagnostics = go.AddComponent<XrDiagnostics>();
            diagnostics.Bind(stack, null, null, Array.Empty<XrDiagnostics.Probe>());

            Assert.That(diagnostics.ActiveFault, Is.EqualTo(XrDiagnostics.Fault.None),
                        "**既定は OFF**のはず");

            int cockpitMask = stack.Cockpit.cullingMask;
            diagnostics.SetFault(XrDiagnostics.Fault.EmptyCullingMask, XrLayer.Cockpit);
            Assert.That(stack.Cockpit.cullingMask, Is.EqualTo(0));

            diagnostics.ClearFault();
            Assert.That(stack.Cockpit.cullingMask, Is.EqualTo(cockpitMask), "元に戻せていない");
            Assert.That(diagnostics.ActiveFault, Is.EqualTo(XrDiagnostics.Fault.None));
        }

        [Test]
        public void 描画順の入れ替えは元に戻せる()
        {
            CameraStackController stack = BuildStack();
            var go = new GameObject("XrDiagnostics");
            go.transform.SetParent(_ship, false);
            var diagnostics = go.AddComponent<XrDiagnostics>();
            diagnostics.Bind(stack, null, null, Array.Empty<XrDiagnostics.Probe>());

            List<Camera> order = stack.Deep
                .GetUniversalAdditionalCameraData().cameraStack;
            var before = new List<Camera>(order);

            diagnostics.SetFault(XrDiagnostics.Fault.SwapOverlayOrder, XrLayer.Cockpit);
            Assert.That(order[0], Is.Not.EqualTo(before[0]), "入れ替わっていない");

            diagnostics.ClearFault();
            Assert.That(order, Is.EqualTo(before), "元に戻せていない");
        }

        [Test]
        public void 深度クリアを外す口が使える()
        {
            // **URP 17 では clearDepth が読み取り専用。** 故意破壊のために
            // private フィールドを書き換えている。フィールド名が変わったら
            // false が返る（**そのときは黙って通さない**）。
            CameraStackController stack = BuildStack();
            var go = new GameObject("XrDiagnostics");
            go.transform.SetParent(_ship, false);
            var diagnostics = go.AddComponent<XrDiagnostics>();
            diagnostics.Bind(stack, null, null, Array.Empty<XrDiagnostics.Probe>());

            Assert.That(diagnostics.SetClearDepth(false), Is.True,
                        "clearDepth を書き換える口が無くなっている");

            Assert.That(stack.Cockpit.GetUniversalAdditionalCameraData().clearDepth, Is.False);
            diagnostics.SetClearDepth(true);
            Assert.That(stack.Cockpit.GetUniversalAdditionalCameraData().clearDepth, Is.True);
        }

        [Test]
        public void 起動引数の綴りを読む()
        {
            Assert.That(XrDiagnostics.ParseFault("nodepthclear"),
                        Is.EqualTo(XrDiagnostics.Fault.NoDepthClear));
            Assert.That(XrDiagnostics.ParseFault("swaporder"),
                        Is.EqualTo(XrDiagnostics.Fault.SwapOverlayOrder));
            Assert.That(XrDiagnostics.ParseFault("emptymask"),
                        Is.EqualTo(XrDiagnostics.Fault.EmptyCullingMask));
            Assert.That(XrDiagnostics.ParseFault("なにか"),
                        Is.EqualTo(XrDiagnostics.Fault.None));
        }

        CameraStackController BuildStack()
        {
            var go = new GameObject("CameraStack");
            go.transform.SetParent(_ship, false);

            Camera deep = NewCamera("Deep", 1 << 8);
            Camera near = NewCamera("Near", 1 << 0);
            Camera nearfield = NewCamera("Nearfield", 1 << 10);
            Camera cockpit = NewCamera("Cockpit", 1 << 9);

            var stack = go.AddComponent<CameraStackController>();
            stack.Bind(deep, near, nearfield, cockpit);
            stack.Configure();
            return stack;
        }

        Camera NewCamera(string name, int mask)
        {
            var go = new GameObject("Cam_" + name);
            go.transform.SetParent(_ship, false);
            Camera cam = go.AddComponent<Camera>();
            cam.cullingMask = mask;
            return cam;
        }
    }
}
