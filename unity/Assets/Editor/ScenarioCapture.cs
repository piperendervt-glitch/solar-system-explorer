using System.IO;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEngine;

namespace SolarSystem.Editor
{
    /// <summary>
    /// シナリオの初期状態を batchmode で撮る (Step 8-0)。
    ///
    /// 定義は Core の ScenarioLibrary。実行時 (ScenarioRunner) と同じものを読む。
    ///
    /// **出力先は既定で verify/shots/（gitignore の内側・フル解像度 PNG）。**
    /// -hero を付けたときだけ docs/screenshots/demo2/ へ 1280x720 の JPEG を出す。
    /// リポジトリは PUBLIC なので、追跡する画像は代表カットだけに絞る。
    ///
    /// **OnGUI はこの経路 (Camera.Render -> RenderTexture) に写らない。**
    /// デバッグ HUD と確認項目テキストは出力画像に出ない (CLAUDE.md 0-B)。
    /// F1 オン/オフの対比は exe で撮ること。
    /// </summary>
    public static class ScenarioCapture
    {
        public const string ScenarioArg = "-scenario";

        /// <summary>どの画面割り当てで撮るか (Step 11-3a の一時的な口)。</summary>
        public const string HeroArg = "-hero";

        /// <summary>段ごとに切り分けたスクショも出す。何が写っているか分からないときに使う。</summary>
        public const string DiagArg = "-diag";

        public const int Width = 1920;
        public const int Height = 1080;

        public const int HeroWidth = 1280;
        public const int HeroHeight = 720;
        public const int HeroJpegQuality = 85;

        /// <summary>撮影前に回すフレーム数。計器の 10 Hz 更新を 2 回以上跨ぐ長さ。</summary>
        public const int SettleTicks = 20;

        /// <summary>画素検証用。フル解像度・gitignore の内側。</summary>
        public static string ShotDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../verify/shots"));

        /// <summary>代表カットの行き先を選ぶ起動引数 (Step 11-5)。既定は demo2。</summary>
        public const string HeroDirArg = "-heroDir";

        /// <summary>
        /// 代表カット。**追跡対象なので Demo ごと最大 3 枚**に抑える。
        /// `-heroDir demo3` のように行き先を選ぶ（既定は demo2 のまま）。
        /// </summary>
        public static string HeroDirectory
        {
            get
            {
                string name = StandaloneCapture.ArgValue(HeroDirArg);
                if (string.IsNullOrEmpty(name))
                {
                    name = "demo2";
                }

                return Path.GetFullPath(Path.Combine(
                    Application.dataPath, "../../docs/screenshots", name));
            }
        }

        public static void Run()
        {
            bool hero = HasArg(HeroArg);
            string only = StandaloneCapture.ArgValue(ScenarioArg);

            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);

            var root = Object.FindAnyObjectByType<UniverseRoot>();
            if (root == null)
            {
                throw new System.InvalidOperationException(
                    "UniverseRoot が見つからない。先に SolarSetup.Run を回すこと。");
            }

            root.Initialize();

            var stack = Object.FindAnyObjectByType<CameraStackController>();
            if (stack == null)
            {
                throw new System.InvalidOperationException("CameraStackController が見つからない。");
            }

            stack.Configure();

            var rig = Object.FindAnyObjectByType<ShipRig>();
            var runner = Object.FindAnyObjectByType<ScenarioRunner>();
            if (runner == null)
            {
                throw new System.InvalidOperationException(
                    "ScenarioRunner が見つからない。先に SolarSetup.Run を回すこと。");
            }

            var overlay = Object.FindAnyObjectByType<DebugOverlay>();

            // **計器の画面は MPB で貼っている (Step 11-3)。**
            // MPB は実行時の状態で、EditMode では Start() が走らないので当たっていない。
            // ここで当てないと、撮った絵にベンダーの画面絵が写る。
            var screens = Object.FindAnyObjectByType<CockpitScreens>();
            if (screens != null)
            {

                // **テスト柄 (Step 11-3b の切り分け道具)。**
                if (StandaloneCapture.HasArg("-screenPattern"))
                {
                    screens.SetPattern(true);
                    Debug.Log("[ScenarioCapture] テスト柄を有効化");
                }

                // **計器の向き (Step 11-3c)。** 面に貼る / 正対 / 逆歪ませ。
                string wantedMode = StandaloneCapture.ArgValue("-screenMode");
                if (!string.IsNullOrEmpty(wantedMode))
                {
                    var mode = (ScreenMode)DebugPanel.ModeIndex(wantedMode);
                    screens.SetMode(mode);
                    Debug.Log($"[ScenarioCapture] 計器の向き: {mode}");
                }

                screens.ApplyMaterials();
            }

            string[] names = ScenarioNames(root.Model, only);
            if (names.Length == 0)
            {
                throw new System.InvalidOperationException(
                    string.IsNullOrEmpty(only)
                        ? "シナリオが 1 件も定義されていない。"
                        : $"シナリオ '{only}' が無い。");
            }

            string directory = hero ? HeroDirectory : ShotDirectory;
            Directory.CreateDirectory(directory);

            foreach (string name in names)
            {
                if (!runner.Select(root.Model, name))
                {
                    throw new System.InvalidOperationException($"シナリオ '{name}' を選べない。");
                }

                runner.Apply(root, rig, stack, overlay);

                // Awake が走らない EditMode なので、ビューを落ち着かせる。
                // **計器は 10 Hz 更新なので 0.1 秒ぶん以上回す。**
                // 4 tick (0.067 秒) では初期値のまま写り、位置も速度も嘘になる。
                for (int i = 0; i < SettleTicks; i++)
                {
                    root.Tick(UniverseConstants.FixedDeltaSeconds);
                }

                // EditMode にはゲームループが無いので、RenderTexture へ描くカメラ
                // (計器パネルの Cam_Instrument) を自分で回す。
                // これを忘れるとパネルが空白のまま写る。
                RenderOffscreenCameras(stack);

                Capture(stack, directory, name, hero);

                if (HasArg(DiagArg))
                {
                    LogVisibleRenderers(stack);
                    EliminateOneByOne(stack, directory, name);
                    CaptureTierDiagnostics(stack, directory, name);
                }
            }

            Debug.Log($"[ScenarioCapture] OK: {directory} ({names.Length} 件 / hero={hero})");
        }

        /// <summary>
        /// Near 段の描画対象を 1 つずつ無効化して撮る。
        /// **どれを消したときに消えるか**を実測で突き止めるため。推測で決めない。
        /// あわせて Light の SRP Lens Flare も単独で切る (Renderer ではないので
        /// 描画対象の列挙には出てこない)。
        /// </summary>
        static void EliminateOneByOne(CameraStackController stack, string directory, string name)
        {
            var targets = new System.Collections.Generic.List<Renderer>();
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if ((stack.Near.cullingMask & (1 << r.gameObject.layer)) != 0)
                {
                    targets.Add(r);
                }
            }

            int index = 0;
            foreach (Renderer r in targets)
            {
                index++;
                string label = r.transform.parent != null
                    ? r.transform.parent.name + "-" + r.gameObject.name
                    : r.gameObject.name;

                r.enabled = false;
                Capture(stack, directory, name + "_off" + index + "-" + label, false);
                r.enabled = true;
            }

            // Renderer ではない描画物: Directional Light の SRP Lens Flare (Step 6)。
            foreach (var flare in Object.FindObjectsByType<UnityEngine.Rendering.LensFlareComponentSRP>(
                         FindObjectsSortMode.None))
            {
                if (!flare.enabled)
                {
                    continue;
                }

                flare.enabled = false;
                Capture(stack, directory, name + "_noflare", false);
                flare.enabled = true;
            }
        }

        /// <summary>各段が描く物を、画面上の占有矩形つきで出す。</summary>
        static void LogVisibleRenderers(CameraStackController stack)
        {
            foreach ((string tier, Camera cam) in new[]
                     { ("Deep", stack.Deep), ("Near", stack.Near), ("Nearfield", stack.Nearfield), ("Cockpit", stack.Cockpit) })
            {
                foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    if ((cam.cullingMask & (1 << r.gameObject.layer)) == 0)
                    {
                        continue;
                    }

                    Bounds b = r.bounds;
                    Vector3 min = cam.WorldToScreenPoint(b.min);
                    Vector3 max = cam.WorldToScreenPoint(b.max);
                    if (min.z <= 0f && max.z <= 0f)
                    {
                        continue; // カメラの後ろ
                    }

                    string mat = r.sharedMaterial != null ? r.sharedMaterial.name : "(無)";
                    float x0 = Mathf.Min(min.x, max.x);
                    float x1 = Mathf.Max(min.x, max.x);
                    float y0 = Mathf.Min(min.y, max.y);
                    float y1 = Mathf.Max(min.y, max.y);
                    float dist = Vector3.Distance(cam.transform.position, b.center);
                    Debug.Log($"[診断] {tier}: {r.gameObject.name} 画面 x[{x0:F0},{x1:F0}] y[{y0:F0},{y1:F0}] 距離 {dist:F3} 材質 {mat}");
                }
            }
        }

        /// <summary>
        /// Overlay 段を 1 つずつ外して撮る。
        /// 画面に何が写っているのか分からないときの切り分け用。
        /// </summary>
        static void CaptureTierDiagnostics(CameraStackController stack, string directory, string name)
        {
            var data = stack.Deep.GetComponent<UniversalAdditionalCameraData>();
            var original = new System.Collections.Generic.List<Camera>(data.cameraStack);

            try
            {
                var tiers = new (string label, Camera cam)[]
                {
                    ("no-near", stack.Near),
                    ("no-nearfield", stack.Nearfield),
                    ("no-cockpit", stack.Cockpit),
                };

                foreach ((string label, Camera cam) in tiers)
                {
                    data.cameraStack.Clear();
                    foreach (Camera c in original)
                    {
                        if (c != cam)
                        {
                            data.cameraStack.Add(c);
                        }
                    }

                    Capture(stack, directory, name + "_" + label, false);
                }

                // Deep 段だけ
                data.cameraStack.Clear();
                Capture(stack, directory, name + "_deep-only", false);
            }
            finally
            {
                data.cameraStack.Clear();
                data.cameraStack.AddRange(original);
            }
        }

        /// <summary>RenderTexture を持つカメラを手で描く (EditMode 用)。</summary>
        static void RenderOffscreenCameras(CameraStackController stack)
        {
            foreach (Camera cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (cam == null || cam.targetTexture == null)
                {
                    continue;
                }

                if (cam == stack.Deep || cam == stack.Near
                    || cam == stack.Nearfield || cam == stack.Cockpit)
                {
                    continue;
                }

                // **選ばれていない割り当ての描画元は飛ばす (Step 11-3)。**
                // 計器の 5 面は案 A と案 B の Canvas を両方持っていて、同じ RT を
                // 共有している。両方描くと**最後に描いたほうが残り**、実機と
                // 違う絵が写る。止まっている Canvas のカメラは描かない。
                Canvas canvas = cam.GetComponentInChildren<Canvas>(true);
                if (canvas != null && !canvas.enabled)
                {
                    continue;
                }

                cam.Render();
            }
        }

        static void SetCanvases(Camera cam, bool enabled)
        {
            if (cam == null)
            {
                return;
            }

            foreach (Canvas canvas in cam.GetComponentsInChildren<Canvas>(true))
            {
                canvas.enabled = enabled;
            }
        }

        static string[] ScenarioNames(SolarSystemModel model, string only)
        {
            var all = ScenarioLibrary.Create(model);
            if (string.IsNullOrEmpty(only))
            {
                var names = new string[all.Count];
                for (int i = 0; i < all.Count; i++)
                {
                    names[i] = all[i].Name;
                }

                return names;
            }

            return ScenarioLibrary.Find(all, only) != null ? new[] { only } : new string[0];
        }

        static void Capture(CameraStackController stack, string directory, string name, bool hero)
        {
            int w = hero ? HeroWidth : Width;
            int h = hero ? HeroHeight : Height;

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();

            RenderTexture prevDeep = stack.Deep.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                stack.Deep.targetTexture = rt;
                stack.Near.targetTexture = null;
                stack.Deep.Render();

                RenderTexture.active = rt;
                var shot = new Texture2D(w, h, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                shot.Apply();

                LogStats(name, shot);

                string path = Path.Combine(directory, hero ? name + ".jpg" : name + ".png");
                byte[] bytes = hero ? shot.EncodeToJPG(HeroJpegQuality) : shot.EncodeToPNG();
                File.WriteAllBytes(path, bytes);
                Object.DestroyImmediate(shot);

                Debug.Log($"[ScenarioCapture] {path} ({w}x{h} / {bytes.Length / 1024} KB)");
            }
            finally
            {
                stack.Deep.targetTexture = prevDeep;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        /// <summary>
        /// 撮った絵の診断値を出す (旧 VerifyCapture から移設)。
        /// Step 2 の LOD 判定と Step 3b の引き渡し検証はこの数値に依っていた。
        /// </summary>
        static void LogStats(string name, Texture2D image)
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
            sb.AppendLine($"[ScenarioCapture] {name} — 非黒画素 {lit} / {pixels.Length} " +
                          $"({100.0 * lit / pixels.Length:F4}%) / 最大輝度 {brightest}");

            var root = Object.FindAnyObjectByType<UniverseRoot>();
            if (root != null && root.SolarSystem != null)
            {
                foreach (CelestialBodyView view in root.SolarSystem.Views)
                {
                    sb.AppendLine(
                        $"[ScenarioCapture]   {view.Body.Name,-6} 距離 {view.LastDistance:E4} units / " +
                        $"角直径 {view.LastAngularPixels:F3} px / blend {view.Lod.Blend:F3} / " +
                        $"point {(view.Lod.PointActive ? "ON " : "off")} / " +
                        $"mesh {(view.Lod.MeshActive ? "ON " : "off")} / " +
                        $"real {view.RealScaleBlend:F3}");
                }
            }

            if (root != null && root.SunFlare != null)
            {
                sb.AppendLine($"[ScenarioCapture]   フレア 遮蔽率 {root.SunFlare.LastOcclusion:F3} / " +
                              $"強度 {root.SunFlare.LastIntensity:F3} (基準 {root.SunFlare.Base:F2})");
            }

            if (root != null && root.SunLight != null)
            {
                sb.Append($"[ScenarioCapture]   太陽光の向き {root.SunLight.LastDirection} / " +
                          $"相対日射 {root.SunLight.LastRelativeIrradiance:F4}");
            }

            Debug.Log(sb.ToString());
        }

        static bool HasArg(string name)
        {
            foreach (string a in System.Environment.GetCommandLineArgs())
            {
                if (string.Equals(a, name, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
