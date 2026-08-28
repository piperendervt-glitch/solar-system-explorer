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
        const string Viewpoint = "cockpit-view";

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

            if (!runner.Select(root.Model, Viewpoint))
            {
                Debug.LogWarning("[XrStack] 視点の場面が無い: " + Viewpoint);
                return;
            }

            runner.Apply(root, rig, stack, overlay);
            for (int i = 0; i < 12; i++)
            {
                root.Tick(UniverseConstants.FixedDeltaSeconds);
            }

            string outDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "..", "verify", "xr"));
            Directory.CreateDirectory(outDir);

            var report = new StringBuilder();
            report.AppendLine("xr-stack（平面版） / 視点 " + Viewpoint
                              + " / 測定 " + XrDiagnostics.MeasureWidth
                              + "x" + XrDiagnostics.MeasureHeight
                              + " / 画像 " + Width + "x" + Height);
            report.AppendLine();

            diagnostics.SetProbesVisible(true);
            diagnostics.SetIsolation(0);
            diagnostics.ClearFault();

            // ---- 正常時 ----
            XrDiagnosticsResult normal = diagnostics.Measure();
            report.AppendLine("■ 正常時");
            report.AppendLine(Header());
            report.AppendLine(Row("正常", normal));
            report.AppendLine();

            Save(stack, outDir, "01_normal");
            SaveColorMap(outDir, "02_colormap", normal);
            SaveMask(outDir, "03_mask", normal);

            // ---- 層アイソレーション ----
            report.AppendLine("■ 層アイソレーション（その層だけ表示）");
            report.AppendLine(Header());
            for (int layer = 1; layer <= 4; layer++)
            {
                diagnostics.SetIsolation(layer);
                XrDiagnosticsResult only = diagnostics.Measure();
                report.AppendLine(Row("only " + XrDiagnosticsModel.LayerNames[layer - 1], only));
                Save(stack, outDir, $"1{layer}_only-{XrDiagnosticsModel.LayerNames[layer - 1]}");
            }

            report.AppendLine();
            report.AppendLine("■ 層アイソレーション（その層だけ非表示）");
            report.AppendLine(Header());
            for (int layer = 1; layer <= 4; layer++)
            {
                diagnostics.SetIsolation(-layer);
                XrDiagnosticsResult hidden = diagnostics.Measure();
                report.AppendLine(Row("hide " + XrDiagnosticsModel.LayerNames[layer - 1], hidden));
                Save(stack, outDir, $"2{layer}_hide-{XrDiagnosticsModel.LayerNames[layer - 1]}");
            }

            diagnostics.SetIsolation(0);
            report.AppendLine();

            // ---- 故意破壊 ----
            report.AppendLine("■ 故意破壊（正常時との差）");
            report.AppendLine(Header());
            report.AppendLine(Row("正常", normal));

            var faults = new (XrDiagnostics.Fault Fault, XrLayer Layer, string Name)[]
            {
                (XrDiagnostics.Fault.NoDepthClear, XrLayer.Cockpit, "深度クリアを外す"),
                (XrDiagnostics.Fault.SwapOverlayOrder, XrLayer.Cockpit, "描画順の入れ替え"),
                (XrDiagnostics.Fault.EmptyCullingMask, XrLayer.Cockpit, "Cockpit の mask を空に"),
                (XrDiagnostics.Fault.EmptyCullingMask, XrLayer.Deep, "Deep の mask を空に"),
            };

            var deltas = new List<string>();
            for (int i = 0; i < faults.Length; i++)
            {
                diagnostics.SetFault(faults[i].Fault, faults[i].Layer);
                XrDiagnosticsResult broken = diagnostics.Measure();
                report.AppendLine(Row(faults[i].Name, broken));
                deltas.Add(Delta(faults[i].Name, normal, broken));
                Save(stack, outDir, $"3{i + 1}_fault-{faults[i].Fault}-{faults[i].Layer}");
                diagnostics.ClearFault();
            }

            report.AppendLine();
            report.AppendLine("■ 正常との差（0 なら、その測定はその壊れを検出できていない）");
            report.AppendLine("故意破壊\tプローブ画素の差\t窓の外景\t盤の漏れ(内側)\t"
                              + "盤の漏れ(縁)\t外景 可視\t層の持ち主が変わった画素");
            foreach (string line in deltas)
            {
                report.AppendLine(line);
            }

            // ---- プローブの色が通過後も分離できているか ----
            report.AppendLine();
            report.AppendLine(Separation(normal));

            File.WriteAllText(Path.Combine(outDir, "xr-stack.txt"), report.ToString());
            Debug.Log("[XrStack]\n" + report);
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

        static string Delta(string label, XrDiagnosticsResult a, XrDiagnosticsResult b)
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

            return label
                   + "\t" + probeDelta
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
