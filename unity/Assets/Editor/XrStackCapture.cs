using System.Collections.Generic;
using System.IO;
using System.Text;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **xr-stack シナリオ（平面版 / Step 12 の準備）。**
    ///
    /// 正常時の画像一式と測定値、そして**故意破壊を 1 つずつ入れたときの前後の数値**を
    /// 出す。**絵が正しいかどうかは判断しない。** 出すのは画像と数値だけで、
    /// 承認は人が行う。
    ///
    /// **ScenarioLibrary には足していない。** あちらへ足すとシナリオ数が変わり、
    /// 既存の F2/F3 の並びと PlayMode の検証ハーネスに影響する。ここは
    /// 「撮って測るだけの入口」に閉じてある。視点は `cockpit-view` を使う。
    /// </summary>
    public static class XrStackCapture
    {
        const int Width = 1280;
        const int Height = 720;

        /// <summary>
        /// 視点。**1 つでは足りない (セッション 0b)。**
        ///
        /// cockpit-view では Deep 段が絵に寄与しないので、**どんな測定でも
        /// Deep の故障を検出できない。** 段ごとに「その段が仕事をしている視点」
        /// を用意して、視点の組み合わせで全層を覆う。
        /// </summary>
        sealed class Viewpoint
        {
            public string Name;
            public string Note;
            public System.Action<UniverseRoot, ShipRig> Place;
        }

        static Viewpoint[] Viewpoints()
        {
            return new[]
            {
                new Viewpoint
                {
                    Name = "cockpit-view",
                    Note = "地球が窓を埋める（シナリオ）",
                    Place = null,
                },
                new Viewpoint
                {
                    Name = "stars",
                    Note = "地球と太陽の反対を向く（窓の大半が星空）",
                    Place = (root, rig) =>
                    {
                        SpaceStation earth = root.Model.Stations[0];
                        Vec3d at = earth.AbsolutePosition
                                   + (earth.PortDirection * UniverseConstants.ArrivalRadiusUnits);
                        root.PlaceObserver(at);

                        // 地球からも太陽からも離れる向き。
                        Vec3d away = (at - root.Model.Earth.AbsolutePosition).Normalized;
                        Aim(rig, away);
                    },
                },
                new Viewpoint
                {
                    Name = "proxy",
                    Note = "太陽まで 1.2e6 units（プロキシ殻が画面を占める）",
                    Place = (root, rig) =>
                    {
                        // **1e4 units ではプロキシ殻は出ない（実測 / 0b）。**
                        // 指示は Demo 1 の「1e4 units で 672 px」だったが、
                        // いまの引き渡しは 5e4 → 3e4 units で、**3e4 より近ければ
                        // 実スケールだけ**になる (`RealScaleHandoff`)。
                        // 1e4 units で写っていた火星は Near 段の実スケールで、
                        // Deep 段は 1 px しか寄与していなかった。
                        //
                        // プロキシ殻だけが描かれるのは `FadeStartDistance` より遠い側。
                        // **火星では画面を占めない。** 引き渡しが 5e4 → 3e4 units
                        // なので、プロキシ殻が使われるのは 3e4 units より遠い側だけ。
                        // 火星 (半径 3,389.5) を 6e4 units から見た角直径は 6.5 度で、
                        // 実測でも Deep の寄与は 176 px (0.08 %) にしかならなかった。
                        //
                        // **太陽なら大きいまま遠い。** 半径 696,000 units を
                        // 1.2e6 units から見れば角直径は約 60 度で、しかも引き渡しの
                        // 帯 (5e4) よりはるかに遠いのでプロキシ殻だけが描かれる。
                        Vec3d sun = root.Model.Sun.AbsolutePosition;
                        Vec3d toward = new Vec3d(0.0, 0.0, 1.0);
                        root.PlaceObserver(sun - (toward * 1.2e6));
                        Aim(rig, toward);
                    },
                },
            };
        }

        static void Aim(ShipRig rig, Vec3d direction)
        {
            if (rig == null || rig.ShipTransform == null)
            {
                return;
            }

            var f = new Vector3((float)direction.X, (float)direction.Y, (float)direction.Z);
            if (f.sqrMagnitude > 0f)
            {
                rig.ShipTransform.rotation = Quaternion.LookRotation(f, Vector3.up);
            }
        }

        static readonly (XrDiagnostics.Fault Fault, XrLayer Layer, string Name)[] Faults =
        {
            (XrDiagnostics.Fault.NoDepthClear, XrLayer.Cockpit, "深度クリアを外す"),
            (XrDiagnostics.Fault.SwapOverlayOrder, XrLayer.Cockpit, "描画順の入れ替え"),
            (XrDiagnostics.Fault.EmptyCullingMask, XrLayer.Cockpit, "Cockpit の mask を空に"),
            (XrDiagnostics.Fault.EmptyCullingMask, XrLayer.Deep, "プロキシ殻の破壊 (Deep mask)"),
            (XrDiagnostics.Fault.SkyboxOff, XrLayer.Deep, "星空を消す (clearFlags)"),
            (XrDiagnostics.Fault.BaseCameraOff, XrLayer.Deep, "Base カメラを止める"),
            (XrDiagnostics.Fault.DropLayerInOneEye, XrLayer.Cockpit, "片目だけ層を落とす (平面 no-op)"),
            (XrDiagnostics.Fault.SkipDepthClearInOneEye, XrLayer.Cockpit,
             "片目だけ深度クリアを飛ばす (平面 no-op)"),
        };

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);

            var root = Object.FindAnyObjectByType<UniverseRoot>();
            var stack = Object.FindAnyObjectByType<CameraStackController>();
            var runner = Object.FindAnyObjectByType<ScenarioRunner>();
            var rig = Object.FindAnyObjectByType<ShipRig>();
            var overlay = Object.FindAnyObjectByType<DebugOverlay>();
            var diagnostics = Object.FindAnyObjectByType<XrDiagnostics>();
            var screens = Object.FindAnyObjectByType<CockpitScreens>();

            if (root == null || stack == null || runner == null || diagnostics == null)
            {
                Debug.LogWarning("[XrStack] 場面を組み立てられない");
                return;
            }

            root.Initialize();
            stack.Configure();
            screens?.ApplyMaterials();

            string outDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "..", "verify", "xr"));
            Directory.CreateDirectory(outDir);

            var report = new StringBuilder();
            report.AppendLine("xr-stack（平面版 / セッション 0b） / 測定 "
                              + XrDiagnostics.MeasureWidth + "x" + XrDiagnostics.MeasureHeight
                              + " / 画像 " + Width + "x" + Height);
            report.AppendLine();

            diagnostics.SetProbesVisible(true);
            diagnostics.SetIsolation(0);
            diagnostics.ClearFault();

            var probeRows = new List<string>();
            var deepRows = new List<string>();
            var faultRows = new List<string>();
            var normals = new Dictionary<string, XrDiagnosticsResult>();

            foreach (Viewpoint viewpoint in Viewpoints())
            {
                Place(root, rig, stack, runner, overlay, viewpoint);

                XrDiagnosticsResult normal = diagnostics.Measure();
                normals[viewpoint.Name] = normal;

                Save(stack, outDir, viewpoint.Name + "_01_normal");
                SaveColorMap(outDir, viewpoint.Name + "_02_colormap", normal);
                SaveMask(outDir, viewpoint.Name + "_03_mask", normal);

                // ---- 4 プローブの可視画素 ----
                // 通常（4 段とも表示）と、**その層だけ表示**したときの両方を出す。
                // 通常で 0 でも、層を切り分ければ測れることがある（内装に隠れるため）。
                var probe = new StringBuilder(viewpoint.Name + "\t" + viewpoint.Note);
                foreach (XrDiagnosticsModel.ProbeHit hit in normal.Probes)
                {
                    probe.Append('\t').Append(hit.Count);
                }

                for (int layer = 1; layer <= 4; layer++)
                {
                    diagnostics.SetIsolation(layer);
                    XrDiagnosticsResult isolated = diagnostics.Measure();
                    probe.Append('\t').Append(isolated.Probes[layer - 1].Count);
                }

                diagnostics.SetIsolation(0);
                probeRows.Add(probe.ToString());

                // ---- Deep 段が画面をどれだけ占めるか ----
                // **絵は切り替えたまま保存する。** 先に戻すと通常の絵が
                // 「only Deep」として残る（0b で実際に踏んだ）。
                diagnostics.SetIsolation(1);
                XrDiagnosticsResult onlyDeep = diagnostics.Measure();
                Save(stack, outDir, viewpoint.Name + "_04_only-Deep");
                diagnostics.SetIsolation(0);

                deepRows.Add(viewpoint.Name
                             + "\t" + normal.Leak.DeepVisiblePixels
                             + "\t" + (100.0 * normal.Leak.DeepVisiblePixels
                                       / (normal.Width * normal.Height)).ToString("F2")
                             + "\t" + onlyDeep.Leak.OutsideVisiblePixels
                             + "\t" + normal.Leak.PanelPixels
                             + "\t" + (XrDiagnosticsModel.PanelWithinBudget(
                                            normal.Leak.PanelPixels,
                                            normal.Width * normal.Height)
                                        ? "OK" : "**範囲外**"));

                // ---- 視点 x 故意破壊 ----
                for (int i = 0; i < Faults.Length; i++)
                {
                    diagnostics.SetFault(Faults[i].Fault, Faults[i].Layer);
                    XrDiagnosticsResult broken = diagnostics.Measure();

                    faultRows.Add(viewpoint.Name + "\t" + Faults[i].Name
                                  + "\t" + Delta(normal, broken));

                    Save(stack, outDir, viewpoint.Name + $"_1{i + 1}_" + Faults[i].Fault);
                    diagnostics.ClearFault();
                }
            }

            report.AppendLine("■ 各視点の 4 プローブ可視画素（正常時 / プローブの位置は動かしていない）");
            report.AppendLine("視点\t内容\t通常 Deep\t通常 Near\t通常 Nearfield\t通常 Cockpit"
                              + "\t単独 Deep\t単独 Near\t単独 Nearfield\t単独 Cockpit");
            foreach (string row in probeRows)
            {
                report.AppendLine(row);
            }

            report.AppendLine();
            report.AppendLine("■ Deep 段の寄与と、計器盤の不変条件");
            report.AppendLine("視点\tDeep 可視 px\t画面比 %\tDeep だけ表示の可視 px\t盤 px\t盤の不変条件");
            foreach (string row in deepRows)
            {
                report.AppendLine(row);
            }

            report.AppendLine();
            report.AppendLine("■ 視点 x 故意破壊（正常との差 / 0 は盲点として残す）");
            report.AppendLine("視点\t故意破壊\tプローブ画素の差\t**絵の差分 px**\t窓の外景\t盤の漏れ(内側)\t"
                              + "盤の漏れ(縁)\t外景 可視\t層の持ち主が変わった画素");
            foreach (string row in faultRows)
            {
                report.AppendLine(row);
            }

            report.AppendLine();
            report.AppendLine(Separation(normals["cockpit-view"]));
            report.AppendLine(Stereo(normals["cockpit-view"]));

            File.WriteAllText(Path.Combine(outDir, "xr-stack.txt"), report.ToString());
            Debug.Log("[XrStack]\n" + report);
        }

        static void Place(UniverseRoot root, ShipRig rig, CameraStackController stack,
                          ScenarioRunner runner, DebugOverlay overlay, Viewpoint viewpoint)
        {
            if (viewpoint.Place == null)
            {
                if (!runner.Select(root.Model, viewpoint.Name))
                {
                    Debug.LogWarning("[XrStack] 視点の場面が無い: " + viewpoint.Name);
                    return;
                }

                runner.Apply(root, rig, stack, overlay);
            }
            else
            {
                viewpoint.Place(root, rig);
            }

            for (int i = 0; i < 12; i++)
            {
                root.Tick(UniverseConstants.FixedDeltaSeconds);
            }
        }

        /// <summary>
        /// 左右比の節。**平面では左右が無いので、同じ絵を 2 枚渡す。**
        /// 比が 1.0 でないなら、その時点で数え方が壊れている。
        /// </summary>
        static string Stereo(XrDiagnosticsResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("■ 左右比（平面なので同じ絵を 2 枚。**1.0 でなければ数え方が壊れている**）");
            sb.AppendLine("層\t左\t右\t比");

            List<XrDiagnosticsModel.StereoProbeHit> stereo = XrDiagnosticsModel.MeasureStereo(
                r.Frame, r.Frame, r.Width, r.Height, r.ProbeMask, r.ProbeMask);

            foreach (XrDiagnosticsModel.StereoProbeHit hit in stereo)
            {
                sb.AppendLine(XrDiagnosticsModel.LayerNames[(int)hit.Layer]
                              + "\t" + hit.Left + "\t" + hit.Right
                              + "\t" + hit.Ratio.ToString("F4"));
            }

            return sb.ToString();
        }
        static string Header()
            => "条件\tDeep px\tNear px\tNearfield px\tCockpit px\t"
               + "窓 px\t窓の外景\t盤 px\t盤の漏れ(内側)\t盤の漏れ(縁)\t外景 可視\tDeep 可視";

        static string Row(string label, XrDiagnosticsResult r)
        {
            var sb = new StringBuilder(label);
            foreach (XrDiagnosticsModel.ProbeHit hit in r.Probes)
            {
                sb.Append('\t').Append(hit.Count);
            }

            sb.Append('\t').Append(r.Leak.WindowPixels);
            sb.Append('\t').Append(r.Leak.WindowOutside);
            sb.Append('\t').Append(r.Leak.PanelPixels);
            sb.Append('\t').Append(r.Leak.PanelOutsideInterior);
            sb.Append('\t').Append(r.Leak.PanelOutsideEdgeBand);
            sb.Append('\t').Append(r.Leak.OutsideVisiblePixels);
            sb.Append('\t').Append(r.Leak.DeepVisiblePixels);
            return sb.ToString();
        }

        static string Delta(XrDiagnosticsResult a, XrDiagnosticsResult b)
        {
            int probeDelta = 0;
            for (int i = 0; i < a.Probes.Count; i++)
            {
                probeDelta += Mathf.Abs(a.Probes[i].Count - b.Probes[i].Count);
            }

            int ownerChanged = 0;
            for (int i = 0; i < a.Owner.Length; i++)
            {
                if (a.Owner[i] != b.Owner[i])
                {
                    ownerChanged++;
                }
            }

            // **絵そのものの差分。** 層の帰属が変わらない壊れ方（星空を消す等）は
            // これでしか捕まらない。スカイボックスは culling mask の対象外なので、
            // 層ごとのマスクの差分には出てこない（実測 / 0b）。
            int frameChanged = 0;
            if (a.Frame != null && b.Frame != null && a.Frame.Length == b.Frame.Length)
            {
                for (int i = 0; i < a.Frame.Length; i += 3)
                {
                    int d = Mathf.Max(Mathf.Abs(a.Frame[i] - b.Frame[i]),
                                      Mathf.Max(Mathf.Abs(a.Frame[i + 1] - b.Frame[i + 1]),
                                                Mathf.Abs(a.Frame[i + 2] - b.Frame[i + 2])));
                    if (d > 2)
                    {
                        frameChanged++;
                    }
                }
            }

            return probeDelta
                   + "\t" + frameChanged
                   + "\t" + (b.Leak.WindowOutside - a.Leak.WindowOutside)
                   + "\t" + (b.Leak.PanelOutsideInterior - a.Leak.PanelOutsideInterior)
                   + "\t" + (b.Leak.PanelOutsideEdgeBand - a.Leak.PanelOutsideEdgeBand)
                   + "\t" + (b.Leak.OutsideVisiblePixels - a.Leak.OutsideVisiblePixels)
                   + "\t" + ownerChanged;
        }

        /// <summary>
        /// プローブの色が **bloom とトーンマップを通った後でも**分離できているか。
        /// 参照色の色相の最小間隔と、実際に画面から拾った画素の色相を並べる。
        /// </summary>
        static string Separation(XrDiagnosticsResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("■ プローブ色の分離（通過後）");
            sb.AppendLine("参照色の色相の最小間隔: "
                          + XrDiagnosticsModel.MinimumHueSeparation().ToString("F1") + " 度"
                          + " / 許容 " + XrDiagnosticsModel.HueTolerance.ToString("F0") + " 度");


            sb.AppendLine("層\t拾えた画素\t重心 x\t重心 y");
            foreach (XrDiagnosticsModel.ProbeHit hit in r.Probes)
            {
                sb.AppendLine(XrDiagnosticsModel.LayerNames[(int)hit.Layer]
                              + "\t" + hit.Count
                              + "\t" + hit.CenterX.ToString("F1")
                              + "\t" + hit.CenterY.ToString("F1"));
            }

            return sb.ToString();
        }

        // ---------------------------------------------------------------- 画像

        static void Save(CameraStackController stack, string outDir, string name)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            RenderTexture previousTarget = stack.Deep.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                stack.Deep.targetTexture = rt;
                stack.Deep.Render();

                RenderTexture.active = rt;
                var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                shot.Apply();

                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), shot.EncodeToPNG());
                Object.DestroyImmediate(shot);
            }
            finally
            {
                stack.Deep.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static void SaveColorMap(string outDir, string name, XrDiagnosticsResult r)
        {
            var texture = new Texture2D(r.Width, r.Height, TextureFormat.RGBA32, false);
            var colors = new Color32[r.Width * r.Height];

            for (int i = 0; i < colors.Length; i++)
            {
                sbyte owner = r.Owner[i];
                if (owner < 0)
                {
                    colors[i] = new Color32(0, 0, 0, 255);
                    continue;
                }

                double[] c = XrDiagnosticsModel.ProbeColor[owner];
                colors[i] = new Color32(
                    (byte)(c[0] * 255), (byte)(c[1] * 255), (byte)(c[2] * 255), 255);
            }

            texture.SetPixels32(colors);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        static void SaveMask(string outDir, string name, XrDiagnosticsResult r)
        {
            var texture = new Texture2D(r.Width, r.Height, TextureFormat.RGBA32, false);
            var colors = new Color32[r.Width * r.Height];

            for (int i = 0; i < colors.Length; i++)
            {
                // 窓を通して Deep が見えている = 緑 / 計器盤へ漏れている = 赤。
                if (r.OutsideVisible[i] && r.PanelRegion[i])
                {
                    colors[i] = new Color32(220, 40, 40, 255);
                }
                else if (r.OutsideVisible[i])
                {
                    colors[i] = new Color32(40, 220, 60, 255);
                }
                else if (r.PanelRegion[i])
                {
                    colors[i] = new Color32(40, 40, 70, 255);
                }
                else if (r.WindowRegion[i])
                {
                    colors[i] = new Color32(25, 45, 25, 255);
                }
                else
                {
                    colors[i] = new Color32(0, 0, 0, 255);
                }
            }

            texture.SetPixels32(colors);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
    }
}
