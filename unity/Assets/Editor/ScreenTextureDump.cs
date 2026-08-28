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

            File.WriteAllText(Path.Combine(outDir, "circle-measure.txt"), report.ToString());
            Debug.Log("[ScreenDump] \n" + report);
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
