using System.IO;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// RenderTexture へ描いて verify/shots/ に PNG を保存する。
    ///
    /// batchmode で走らせるので `-nographics` を付けてはいけない
    /// (CLAUDE.md §5 / docs/01-architecture.md)。
    ///
    /// 観測者を各地点へ置いて 4 枚撮る。船を動かす実装 (Step 3) ではなく、
    /// UniverseRoot.PlaceObserver で座標を差し替えるだけ。
    /// </summary>
    public static class VerifyCapture
    {
        public const int Width = 1920;
        public const int Height = 1080;

        static string ShotDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../verify/shots"));

        public static void Run()
        {
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);

            UniverseRoot root = Object.FindAnyObjectByType<UniverseRoot>();
            if (root == null)
            {
                throw new System.InvalidOperationException("UniverseRoot が見つからない。先に SolarSetup.Run を回すこと。");
            }

            root.Initialize();

            CameraStackController stack = Object.FindAnyObjectByType<CameraStackController>();
            if (stack == null)
            {
                throw new System.InvalidOperationException("CameraStackController が見つからない。");
            }

            stack.Configure();

            SolarSystemModel model = root.Model;
            Vec3d earth = model.Earth.AbsolutePosition;
            Vec3d mars = model.Mars.AbsolutePosition;
            Vec3d towardMars = (mars - earth).Normalized;

            Directory.CreateDirectory(ShotDirectory);

            Capture(root, stack, "01_from_earth", earth + towardMars * 2.0e4, mars,
                    "地球近傍 (地球から 2e4 units) から火星方向");

            Capture(root, stack, "02_midpoint", earth + towardMars * (SolarSystemModel.EarthToMarsKm * 0.5), mars,
                    "地球-火星の中間点");

            Capture(root, stack, "03_mars_5e4", mars - towardMars * 5.0e4, mars,
                    "火星まで 5e4 units");

            Capture(root, stack, "04_mars_2e4", mars - towardMars * 2.0e4, mars,
                    "火星まで 2e4 units (メッシュ切替後)");

            Debug.Log($"[VerifyCapture] OK: {ShotDirectory}");
        }

        static void Capture(
            UniverseRoot root,
            CameraStackController stack,
            string name,
            Vec3d observer,
            Vec3d lookAtAbsolute,
            string description)
        {
            root.PlaceObserver(observer);

            Vec3d dir = (lookAtAbsolute - observer).Normalized;
            var forward = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            stack.Deep.transform.rotation = rotation;
            stack.Near.transform.rotation = rotation;

            // 観測者を動かしたのでビューを更新し直す。
            root.PlaceObserver(observer);

            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
            };
            rt.Create();

            RenderTexture previousDeep = stack.Deep.targetTexture;
            RenderTexture previousNear = stack.Near.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            try
            {
                // Overlay カメラは Base の targetTexture へ描かれる。Base だけに設定する。
                stack.Deep.targetTexture = rt;
                stack.Near.targetTexture = null;
                stack.Deep.Render();

                RenderTexture.active = rt;
                var png = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                png.Apply();

                string path = Path.Combine(ShotDirectory, $"{name}.png");
                File.WriteAllBytes(path, png.EncodeToPNG());

                LogStats(root, name, description, png, observer, lookAtAbsolute);
                Object.DestroyImmediate(png);
            }
            finally
            {
                stack.Deep.targetTexture = previousDeep;
                stack.Near.targetTexture = previousNear;
                RenderTexture.active = previousActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        static void LogStats(
            UniverseRoot root,
            string name,
            string description,
            Texture2D image,
            Vec3d observer,
            Vec3d lookAtAbsolute)
        {
            Color32[] pixels = image.GetPixels32();
            int lit = 0;
            int brightest = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                int v = Mathf.Max(pixels[i].r, Mathf.Max(pixels[i].g, pixels[i].b));
                if (v > 8)
                {
                    lit++;
                }

                if (v > brightest)
                {
                    brightest = v;
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[VerifyCapture] {name}.png — {description}");
            sb.AppendLine($"[VerifyCapture]   非黒画素 {lit} / {pixels.Length} ({100.0 * lit / pixels.Length:F4}%) / 最大輝度 {brightest}");

            foreach (CelestialBodyView view in root.SolarSystem.Views)
            {
                sb.AppendLine(
                    $"[VerifyCapture]   {view.Body.Name,-6} 距離 {view.LastDistance:E4} units / " +
                    $"角直径 {view.LastAngularPixels:F3} px / " +
                    $"blend {view.Lod.Blend:F3} / point {(view.Lod.PointActive ? "ON " : "off")} / " +
                    $"mesh {(view.Lod.MeshActive ? "ON " : "off")}");
            }

            sb.Append($"[VerifyCapture]   太陽光の向き {root.SunLight.LastDirection} / 相対日射 {root.SunLight.LastRelativeIrradiance:F4}");
            Debug.Log(sb.ToString());
        }
    }
}
