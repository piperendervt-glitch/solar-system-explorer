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
    /// **暗い場面で内装が潰れていないかを画素で測る (Step 11-4)。**
    ///
    /// ■ 内装のマスクの作り方
    /// 同じ場面を 2 枚撮る。1 枚は通常、もう 1 枚は**コックピット段の culling mask
    /// だけを空にした絵**（CLAUDE.md §0-B の道具）。差が出た画素が内装。
    /// **固定の矩形で測らない。** 目の位置や機体が変われば内装の位置も変わる。
    ///
    /// **カメラごと無効にしてはいけない。** URP はスタックの最後のカメラで
    /// ポストプロセスを適用するので、段を消すとトーンマップの掛かり方まで変わり、
    /// 測っている絵が実機と別物になる。
    ///
    /// ■ 漏れの検査
    /// 補助光を**過大な強度**にして撮り、**内装以外**の画素が動くかを見る。
    /// 動けば他の段へ漏れている（`Light.cullingMask` が効いていない）。
    /// </summary>
    public static class CockpitLightingReport
    {
        const int Width = 1920;
        const int Height = 1080;

        /// <summary>漏れの検査に使う過大な強度。**普段使う値ではない。**</summary>
        const float LeakProbeIntensity = 50f;

        static readonly string[] Scenarios =
        {
            "cockpit-view", "earth-close-terminator", "earth-close-night", "sun-hidden",
        };

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);

            var root = Object.FindAnyObjectByType<UniverseRoot>();
            var stack = Object.FindAnyObjectByType<CameraStackController>();
            var lights = Object.FindAnyObjectByType<CockpitLights>();
            var runner = Object.FindAnyObjectByType<ScenarioRunner>();
            var rig = Object.FindAnyObjectByType<ShipRig>();
            var overlay = Object.FindAnyObjectByType<DebugOverlay>();

            if (root == null || stack == null || lights == null || runner == null)
            {
                Debug.LogWarning("[CockpitLighting] 場面を組み立てられない");
                return;
            }

            // **EditMode では Awake / Start が走らない。** 撮影経路と同じ順で
            // 初期化してから場面を選ぶ（ScenarioCapture と同じ）。
            root.Initialize();
            stack.Configure();

            var screens = Object.FindAnyObjectByType<CockpitScreens>();
            if (screens != null)
            {
                screens.ApplyMaterials();
            }

            lights.Apply();

            string outDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "..", "verify", "lighting"));
            Directory.CreateDirectory(outDir);

            var report = new StringBuilder();
            report.AppendLine("測定条件: 1920x1080 / 画角 60 度 / 内装マスク = "
                              + "コックピット段の culling mask を空にした絵との差分");
            report.AppendLine();
            report.AppendLine("場面\t補助光\t内装の画素\t平均輝度\t中央値\t真っ黒(0-2)\t最小\t最大");

            foreach (string scenario in Scenarios)
            {
                if (!runner.Select(root.Model, scenario))
                {
                    Debug.LogWarning($"[CockpitLighting] 場面が無い: {scenario}");
                    continue;
                }

                runner.Apply(root, rig, stack, overlay);
                for (int i = 0; i < 10; i++)
                {
                    root.Tick(UniverseConstants.FixedDeltaSeconds);
                }

                // 内装のマスクは**補助光を消した状態**で作る（光の有無で形は変わらない）。
                bool wasOn = lights.FillEnabled;
                lights.SetFillEnabled(false);
                Color32[] withCockpit = Shot(stack, root);
                Color32[] withoutCockpit = ShotWithoutCockpit(stack, root);
                bool[] mask = Mask(withCockpit, withoutCockpit);

                int inside = 0;
                foreach (bool m in mask)
                {
                    if (m) { inside++; }
                }

                report.AppendLine(Row(scenario, "OFF", withCockpit, mask));

                lights.SetFillEnabled(true);
                lights.SetFillIntensity((float)CockpitDefinition.DefaultFillLightIntensity);
                Color32[] lit = Shot(stack, root);
                report.AppendLine(Row(scenario, "ON (既定)", lit, mask));

                Save(outDir, scenario + "_fill-off", withCockpit);
                Save(outDir, scenario + "_fill-on", lit);

                // **既定の強度で、内装の外がどれだけ動くか。** これが実際に
                // 出荷する条件での答え。内装の中は動いてよい（動かなければ
                // 補助光が効いていない）。
                int outsideDefault = 0;
                int insideDefault = 0;
                int worstOutside = 0;
                for (int i = 0; i < lit.Length; i++)
                {
                    int d = Mathf.Max(Mathf.Abs(lit[i].r - withCockpit[i].r),
                                      Mathf.Max(Mathf.Abs(lit[i].g - withCockpit[i].g),
                                                Mathf.Abs(lit[i].b - withCockpit[i].b)));
                    if (d <= 2)
                    {
                        continue;
                    }

                    if (mask[i])
                    {
                        insideDefault++;
                    }
                    else
                    {
                        outsideDefault++;
                        worstOutside = Mathf.Max(worstOutside, d);
                    }
                }

                report.AppendLine($"{scenario}\t既定の強度で変わった画素\t"
                                  + $"内装 {insideDefault}\t"
                                  + $"**内装の外 {outsideDefault} (最大差 {worstOutside})**\t-\t-\t-\t-");

                // ---- 他の段への漏れ ----
                // **内装を描かない状態で比べる。** 内装を写したまま強度を上げると、
                // 明るくなった内装が bloom とトーンマップを通じて画面全体を変える
                // ので、「光が漏れた」のか「絵が明るくなった」のか分けられない
                // （最初にそれで 644,291 画素という数字を出してしまった）。
                // コックピット段を描かなければ、内装由来の寄与は 0 になる。
                lights.SetFillEnabled(false);
                Color32[] outsideOff = ShotWithoutCockpit(stack, root);

                lights.SetFillEnabled(true);
                lights.SetFillIntensity(LeakProbeIntensity);
                Color32[] outsideOver = ShotWithoutCockpit(stack, root);
                lights.SetFillIntensity((float)CockpitDefinition.DefaultFillLightIntensity);

                int leaked = 0;
                int worst = 0;
                for (int i = 0; i < outsideOver.Length; i++)
                {
                    int d = Mathf.Max(Mathf.Abs(outsideOver[i].r - outsideOff[i].r),
                                      Mathf.Max(Mathf.Abs(outsideOver[i].g - outsideOff[i].g),
                                                Mathf.Abs(outsideOver[i].b - outsideOff[i].b)));
                    if (d > 2)
                    {
                        leaked++;
                    }

                    worst = Mathf.Max(worst, d);
                }

                report.AppendLine($"{scenario}	漏れ検査 (強度 {LeakProbeIntensity:F0} / "
                                  + $"内装を描かずに比較)	-	-	-	"
                                  + $"**変化した画素 {leaked} / 最大差 {worst}**	-	-");

                // **範囲も外して確かめる。** 範囲 3 m のままだと「近くに他の段の物が
                // 無いから変わらなかった」だけかもしれず、culling mask が効いている
                // 証拠にならない。範囲を 1e6 にすれば太陽系のほとんどが射程に入る。
                float previousRange = lights.Fill.range;
                lights.Fill.range = 1.0e6f;
                lights.SetFillIntensity(LeakProbeIntensity);
                Color32[] outsideFar = ShotWithoutCockpit(stack, root);
                lights.Fill.range = previousRange;
                lights.SetFillIntensity((float)CockpitDefinition.DefaultFillLightIntensity);

                int leakedFar = 0;
                int worstFar = 0;
                for (int i = 0; i < outsideFar.Length; i++)
                {
                    int d = Mathf.Max(Mathf.Abs(outsideFar[i].r - outsideOff[i].r),
                                      Mathf.Max(Mathf.Abs(outsideFar[i].g - outsideOff[i].g),
                                                Mathf.Abs(outsideFar[i].b - outsideOff[i].b)));
                    if (d > 2)
                    {
                        leakedFar++;
                    }

                    worstFar = Mathf.Max(worstFar, d);
                }

                report.AppendLine($"{scenario}	漏れ検査 (強度 {LeakProbeIntensity:F0} / "
                                  + $"範囲 1e6 / 内装を描かずに比較)	-	-	-	"
                                  + $"**変化した画素 {leakedFar} / 最大差 {worstFar}**	-	-");

                // **対照: culling mask を外して同じ条件で撮る。**
                // これで画素が動かないなら、上の「0 画素」は mask のおかげではなく
                // **単に距離減衰で届いていないだけ**で、検査に効き目が無い。
                // 動くなら、止めているのは mask だと言える。
                int previousMask = lights.Fill.cullingMask;
                lights.Fill.cullingMask = ~0;
                lights.Fill.range = 1.0e6f;
                lights.SetFillIntensity(LeakProbeIntensity);
                Color32[] outsideAll = ShotWithoutCockpit(stack, root);
                lights.Fill.cullingMask = previousMask;
                lights.Fill.range = previousRange;
                lights.SetFillIntensity((float)CockpitDefinition.DefaultFillLightIntensity);

                int leakedAll = 0;
                int worstAll = 0;
                for (int i = 0; i < outsideAll.Length; i++)
                {
                    int d = Mathf.Max(Mathf.Abs(outsideAll[i].r - outsideOff[i].r),
                                      Mathf.Max(Mathf.Abs(outsideAll[i].g - outsideOff[i].g),
                                                Mathf.Abs(outsideAll[i].b - outsideOff[i].b)));
                    if (d > 2)
                    {
                        leakedAll++;
                    }

                    worstAll = Mathf.Max(worstAll, d);
                }

                report.AppendLine($"{scenario}	対照 (mask を外す / 強度 {LeakProbeIntensity:F0}"
                                  + $" / 範囲 1e6)	-	-	-	"
                                  + $"**変化した画素 {leakedAll} / 最大差 {worstAll}**	-	-");

                lights.SetFillEnabled(wasOn);
            }

            string path = Path.Combine(outDir, "cockpit-lighting.txt");
            File.WriteAllText(path, report.ToString());
            Debug.Log("[CockpitLighting]\n" + report);
        }

        static string Row(string scenario, string label, Color32[] image, bool[] mask)
        {
            var values = new List<double>();
            for (int i = 0; i < image.Length; i++)
            {
                if (mask[i])
                {
                    values.Add(CockpitLighting.Luminance(image[i].r, image[i].g, image[i].b));
                }
            }

            CockpitLighting.Result r = CockpitLighting.Measure(values);
            return $"{scenario}\t{label}\t{r.Count}\t{r.Mean:F2}\t{r.Median:F2}\t"
                   + $"{r.BlackRatio * 100.0:F1}%\t{r.Min:F0}\t{r.Max:F0}";
        }

        static bool[] Mask(Color32[] withCockpit, Color32[] withoutCockpit)
        {
            var mask = new bool[withCockpit.Length];
            for (int i = 0; i < mask.Length; i++)
            {
                int d = Mathf.Max(Mathf.Abs(withCockpit[i].r - withoutCockpit[i].r),
                                  Mathf.Max(Mathf.Abs(withCockpit[i].g - withoutCockpit[i].g),
                                            Mathf.Abs(withCockpit[i].b - withoutCockpit[i].b)));
                mask[i] = d > 2;
            }

            return mask;
        }

        static Color32[] ShotWithoutCockpit(CameraStackController stack, UniverseRoot root)
        {
            int previous = stack.Cockpit.cullingMask;
            try
            {
                // **カメラは回したまま mask だけ空にする。** 段ごと消すと
                // ポストプロセスの掛かり方が変わる（CLAUDE.md §0-B）。
                stack.Cockpit.cullingMask = 0;
                return Shot(stack, root);
            }
            finally
            {
                stack.Cockpit.cullingMask = previous;
            }
        }

        static Color32[] Shot(CameraStackController stack, UniverseRoot root)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            RenderTexture prevTarget = stack.Deep.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                stack.Deep.targetTexture = rt;
                stack.Deep.Render();

                RenderTexture.active = rt;
                var shot = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                shot.Apply();

                Color32[] pixels = shot.GetPixels32();
                Object.DestroyImmediate(shot);
                return pixels;
            }
            finally
            {
                stack.Deep.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static void Save(string outDir, string name, Color32[] pixels)
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }
    }
}
