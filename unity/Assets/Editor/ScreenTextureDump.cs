using System.IO;
using System.Text;
using SolarSystem.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// **RT の中身そのものを画素で確かめる (Step 11-3c)。**
    ///
    /// 面に貼ったあとの絵ではなく、**貼る前の RenderTexture を CPU へ読み戻して**
    /// PNG に落とし、テスト柄の円の外接矩形を測る。
    /// 1024x544 の RT に真円が入っているなら**幅と高さが等しくなる**。
    ///
    /// 実機の「RT を直接表示」で大画面の円が横に伸びて見えた、という報告に対する
    /// 決着のための道具。**画面に出す経路（拡大・切り取り）を一切通さない。**
    /// </summary>
    public static class ScreenTextureDump
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);

            var screens = Object.FindAnyObjectByType<CockpitScreens>();
            if (screens == null || screens.Screens.Count == 0)
            {
                Debug.LogWarning("[ScreenDump] 画面が無い（箱のコックピット）");
                return;
            }

            screens.SetPattern(true);
            screens.ApplyMaterials();

            // **画面だけを回す。** `UniverseRoot.Tick` は船の入力まで通るので、
            // 場面を組み立てていない EditMode では落ちる（実測）。
            for (int i = 0; i < 5; i++)
            {
                screens.Tick(i * CockpitScreens.UpdateIntervalSeconds);
            }

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "verify", "rt"));
            Directory.CreateDirectory(outDir);
            var report = new StringBuilder();
            report.AppendLine("面\tRT\t円の外接(画素)\t比\t橙の画素数");

            foreach (CockpitScreens.Screen screen in screens.Screens)
            {
                Measure(outDir, screen.RendererName, screen.Texture, report);

                if (screen.Warped != null && screen.Warped.IsCreated())
                {
                    Measure(outDir, screen.RendererName + "_Warped", screen.Warped, report);
                }
            }

            Jitter(screens, outDir, report);

            File.WriteAllText(Path.Combine(outDir, "circle-measure.txt"), report.ToString());
            Debug.Log("[ScreenDump] \n" + report);
        }

        /// <summary>
        /// **船を回したときに RT の中身が変わるか (Step 11-3c)。**
        ///
        /// 「マウスを大きく動かすと RT が歪む」という報告の切り分け。
        /// 計器の Canvas は船の子なので、船が回ると**世界座標が動く**。
        /// 描いている中身は動いていないはずなので、**RT の画素は 1 つも
        /// 変わらないのが正しい。** 変わるならそれは座標の精度の問題。
        /// </summary>
        static void Jitter(CockpitScreens screens, string outDir, StringBuilder report)
        {
            CockpitScreens.Screen face = screens.Screens[0];
            Transform cockpit = screens.transform;
            Quaternion original = cockpit.localRotation;

            report.AppendLine();
            report.AppendLine("船を回したときの RT の変化 (" + face.RendererName + ")");
            report.AppendLine("回した角度\t中心からの距離\t変わった画素\t最大の差\t円の外接");

            Color32[] baseline = null;
            double elapsed = 100.0;

            foreach (float angle in new[] { 0f, 0.5f, 5f, 30f, 90f })
            {
                cockpit.localRotation = original * Quaternion.Euler(0f, angle, 0f);

                for (int i = 0; i < 3; i++)
                {
                    elapsed += CockpitScreens.UpdateIntervalSeconds;
                    screens.Tick(elapsed);
                }

                Color32[] pixels = Read(face.Texture, out int w, out int h);
                float distance = face.CameraPattern != null
                    ? face.CameraPattern.transform.position.magnitude
                    : 0f;

                int changed = 0;
                int worst = 0;
                if (baseline == null)
                {
                    baseline = pixels;
                }
                else
                {
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        int d = Mathf.Max(Mathf.Abs(pixels[i].r - baseline[i].r),
                                          Mathf.Max(Mathf.Abs(pixels[i].g - baseline[i].g),
                                                    Mathf.Abs(pixels[i].b - baseline[i].b)));
                        if (d > 8)
                        {
                            changed++;
                        }

                        worst = Mathf.Max(worst, d);
                    }
                }

                report.AppendLine($"{angle,6:F1} 度\t{distance,10:F0} unit\t{changed,8}"
                                  + $"\t{worst,6}\t{CircleBox(pixels, w, h)}");
            }

            cockpit.localRotation = original;
        }

        static Color32[] Read(RenderTexture rt, out int width, out int height)
        {
            width = rt.width;
            height = rt.height;

            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            Color32[] pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            return pixels;
        }

        static string CircleBox(Color32[] pixels, int width, int height)
        {
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 c = pixels[(y * width) + x];
                    if (c.r < 120 || c.r <= c.b + 40 || c.g >= c.r)
                    {
                        continue;
                    }

                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            }

            return minX > maxX
                ? "環が無い"
                : $"{maxX - minX + 1}x{maxY - minY + 1} @ ({minX},{minY})";
        }

        static void Measure(string outDir, string name, RenderTexture rt, StringBuilder report)
        {
            if (rt == null)
            {
                return;
            }

            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, true);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            texture.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;

            string path = Path.Combine(outDir, name + $"_{rt.width}x{rt.height}.png");
            File.WriteAllBytes(path, texture.EncodeToPNG());

            // テスト柄の環は橙 (240,180,100)。**RT は素の値**（トーンマップ前）なので
            // 閾値は固定でよい。
            Color32[] pixels = texture.GetPixels32();
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            int count = 0;

            for (int y = 0; y < rt.height; y++)
            {
                for (int x = 0; x < rt.width; x++)
                {
                    Color32 c = pixels[(y * rt.width) + x];
                    if (c.r < 120 || c.r <= c.b + 40 || c.g >= c.r)
                    {
                        continue;
                    }

                    count++;
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                }
            }

            Object.DestroyImmediate(texture);

            if (count == 0)
            {
                report.AppendLine($"{name}\t{rt.width}x{rt.height}\t環が無い\t-\t0");
                return;
            }

            int w = maxX - minX + 1;
            int h = maxY - minY + 1;
            report.AppendLine($"{name}\t{rt.width}x{rt.height}\t{w}x{h}\t"
                              + $"{w / (float)h:F3}\t{count}");
        }
    }
}
