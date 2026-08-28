using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// **exe + MockHMD から左右 2 枚の最終絵を取り出す (Step 12-C)。**
    ///
    /// ■ なぜ exe なのか
    /// `ScenarioCapture` の手動 `Camera.Render()` は **XR の経路を通らない。**
    /// batchmode の Editor は目のテクスチャを作らない（→ CLAUDE.md §0-B）。
    /// **SPI で実際に走るのはスタンドアロン exe だけ**なので、ここで撮る。
    ///
    /// ■ **SPI ゲートを先に通す。**
    /// 撮影が MultiPass に落ちたまま気づかず全部緑、という事故を防ぐ。
    /// `[XrFacts]` が SinglePassInstanced / Tex2DArray / volumeDepth 2 でなければ
    /// **撮らずに落ちる。** 初期化直後の値は当てにならない（12-0d）ので
    /// フレームが回ってから読む。
    ///
    /// ■ 撮った条件を必ず併記する
    /// 出力の `xr-stack.txt` の先頭に stereoRenderingMode / dimension /
    /// volumeDepth / 目テクスチャのサイズ / f（左右）/ 眼間を書く。
    /// **画像だけ見てどのモードで撮ったのか分からない状態を作らない。**
    ///
    ///   SolarSystemExplorer.exe -xrMock -xrCaptureDir verify\xr-stereo
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XrStereoCapture : MonoBehaviour
    {
        public const string DirArg = "-xrCaptureDir";
        public const string FramesArg = "-xrCaptureFrames";

        /// <summary>
        /// **対照用 (Step 12-C2)。** 自前シェーダ (`SolarSystem/*`) を使っている
        /// レンダラーを既定の URP/Lit に差し替えてから撮る。
        ///
        /// 自前シェーダは SPI のマクロ（`UNITY_VERTEX_INPUT_INSTANCE_ID` など）を
        /// 1 つも持っていない。**それが片目落ちの原因かどうかを、直さずに確かめる**
        /// ための一時的な差し替え。exe の実行中だけで、アセットには書き戻さない。
        /// </summary>
        public const string SwapShaderArg = "-xrSwapShader";

        /// <summary>既定の待ちフレーム。シーンとポストプロセスが落ち着くまで待つ。</summary>
        public const int DefaultFrames = 150;

        /// <summary>SPI ゲートが落ちたときの終了コード。**0 と区別できる値。**</summary>
        public const int GateFailedExitCode = 3;

        /// <summary>撮影の経路そのものが成立しなかったときの終了コード。</summary>
        public const int CaptureFailedExitCode = 4;

        IEnumerator Start()
        {
            string dir = StandaloneCapture.ArgValue(DirArg);
            if (string.IsNullOrEmpty(dir))
            {
                yield break;
            }

            int frames = DefaultFrames;
            string framesText = StandaloneCapture.ArgValue(FramesArg);
            if (!string.IsNullOrEmpty(framesText)
                && int.TryParse(framesText, out int parsed) && parsed > 0)
            {
                frames = parsed;
            }

            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }

            Directory.CreateDirectory(dir);

            // ---- 条件を採る（撮影の入口）----
            XrBoot.StereoFacts facts = XrBoot.ReadStereoFacts();
            Debug.Log("[XrFacts] 撮影の入口 / " + facts);

            var stack = FindAnyObjectByType<CameraStackController>();
            List<XrStereoOptics.Reading> optics = XrStereoOptics.ReadStack(stack);
            foreach (XrStereoOptics.Reading reading in optics)
            {
                Debug.Log("[XrOptics] 撮影の入口 / " + reading);
            }

            var report = new StringBuilder();
            WriteConditions(report, facts, optics);
            WriteCameraState(report, stack);

            if (StandaloneCapture.HasArg(SwapShaderArg))
            {
                SwapShaders(report);
            }

            // ---- SPI ゲート ----
            string gate = GateFailure(facts);
            if (gate != null)
            {
                report.AppendLine("== SPI ゲート ==");
                report.AppendLine("**落ちた**: " + gate);
                report.AppendLine("撮影は行っていない。");
                Save(dir, report);

                Debug.LogError("[XrStereoCapture] SPI ゲートが落ちた: " + gate);
                yield return null;
                Application.Quit(GateFailedExitCode);
                yield break;
            }

            report.AppendLine("== SPI ゲート ==");
            report.AppendLine("通過。");
            report.AppendLine();

            // ---- 1. 目ごとのスクリーンショット ----
            //
            // **左右は同じフレームで撮る。** フレームをまたぐと、船の微振動や
            // 天体の運動による差が混ざり、「左右が違う」のが視差なのか時間なのか
            // 区別できなくなる。
            //
            // **対照として同じフレームで左目をもう 1 枚撮る。** これが 0 画素なら、
            // 撮影そのものの雑音の下限は 0（＝左右の差は本物）。
            yield return new WaitForEndOfFrame();

            Texture2D left = TryCapture(ScreenCapture.StereoScreenCaptureMode.LeftEye,
                                        report, "左目");
            Texture2D right = TryCapture(ScreenCapture.StereoScreenCaptureMode.RightEye,
                                         report, "右目");
            Texture2D leftAgain = TryCapture(ScreenCapture.StereoScreenCaptureMode.LeftEye,
                                             report, "左目 (対照)");

            bool ok = left != null && right != null;
            if (ok)
            {
                if (leftAgain != null)
                {
                    report.AppendLine();
                    report.AppendLine("== 雑音の下限 (同じフレームで左目を 2 回) ==");
                    ReportDifference(report, left, leftAgain);
                    Destroy(leftAgain);
                }

                ok = Compare(dir, report, left, right);
            }

            if (!ok)
            {
                // ---- 2. ミラーウィンドウ（backbuffer）の読み戻し ----
                //
                // 目ごとに取れなかったときの症状を残すため、素の backbuffer も撮る。
                // **これ 1 枚では左右にならない**ので、成立の判定には使えない。
                report.AppendLine("== 経路 2: ミラーウィンドウ (backbuffer) ==");
                yield return new WaitForEndOfFrame();
                Texture2D mirror = TryCapture(null, report, "ミラー");
                if (mirror != null)
                {
                    WritePng(dir, "mirror.png", mirror);
                    report.AppendLine($"  保存した: mirror.png ({mirror.width}x{mirror.height})");
                    report.AppendLine("  **左右 2 枚ではないので、これでは成立しない。**");
                    Destroy(mirror);
                }
            }

            Destroy(left);
            Destroy(right);

            Save(dir, report);
            Debug.Log($"[XrStereoCapture] 結果: {(ok ? "左右 2 枚を保存した" : "**成立しなかった**")}");

            yield return null;
            Application.Quit(ok ? 0 : CaptureFailedExitCode);
        }

        /// <summary>SPI の 3 点セット。**満たさない理由を文字列で返す**（満たせば null）。</summary>
        public static string GateFailure(XrBoot.StereoFacts facts)
        {
            if (facts == null)
            {
                return "立体視の値が読めていない";
            }

            if (facts.StereoRenderingMode != "SinglePassInstanced")
            {
                return "stereoRenderingMode = " + facts.StereoRenderingMode
                       + "（SinglePassInstanced ではない）";
            }

            if (facts.EyeTextureDimension != "Tex2DArray")
            {
                return "eyeTextureDesc.dimension = " + facts.EyeTextureDimension
                       + "（Tex2DArray ではない）";
            }

            if (facts.EyeTextureVolumeDepth != 2)
            {
                return "volumeDepth = " + facts.EyeTextureVolumeDepth + "（2 ではない）";
            }

            return null;
        }

        static void WriteConditions(StringBuilder report, XrBoot.StereoFacts facts,
                                    List<XrStereoOptics.Reading> optics)
        {
            report.AppendLine("== 撮った条件 ==");
            report.AppendLine("stereoRenderingMode : " + facts.StereoRenderingMode);
            report.AppendLine("eyeTexture.dimension: " + facts.EyeTextureDimension);
            report.AppendLine("eyeTexture.size     : "
                              + facts.EyeTextureWidth + "x" + facts.EyeTextureHeight);
            report.AppendLine("volumeDepth         : " + facts.EyeTextureVolumeDepth);
            report.AppendLine("device              : " + facts.DeviceName);
            report.AppendLine("画面                : " + Screen.width + "x" + Screen.height);
            report.AppendLine();

            report.AppendLine("== f と眼間の実測 (段ごと) ==");
            report.AppendLine("段\ts\tf 左 [px]\tf 右 [px]\t眼間 [units]\t眼間 [m]");
            foreach (XrStereoOptics.Reading r in optics)
            {
                report.AppendLine(string.Join("\t", new[]
                {
                    r.CameraName,
                    F(r.StageScale),
                    F(r.FocalLengthPixelsLeft),
                    F(r.FocalLengthPixelsRight),
                    E(r.EyeSeparationUnits),
                    E(r.EyeSeparationMeters),
                }));
            }

            report.AppendLine();
            report.AppendLine("**眼間は段によって意味が違う。** コックピット段は 1 m = 1 unit、");
            report.AppendLine("外 3 段は 1 unit = 1 km。同じ数字でも別の量。");
            report.AppendLine();
        }

        /// <summary>
        /// 4 段のカメラの状態 (Step 12-C2)。**`stereoTargetEye` が Both 以外だと
        /// その段は片目にしか出ない。** 値を出すだけで判定はしない。
        /// </summary>
        static void WriteCameraState(StringBuilder report, CameraStackController stack)
        {
            report.AppendLine("== カメラ 4 段の状態 ==");
            report.AppendLine("段\tstereoTargetEye\tstereoEnabled\tdepth\tnear\tfar\tcullingMask\ttargetTexture");

            if (stack == null)
            {
                report.AppendLine("  カメラスタックが見つからない");
                report.AppendLine();
                return;
            }

            (string Name, Camera Camera)[] cameras =
            {
                ("Deep", stack.Deep),
                ("Near", stack.Near),
                ("Nearfield", stack.Nearfield),
                ("Cockpit", stack.Cockpit),
            };

            foreach ((string name, Camera camera) in cameras)
            {
                if (camera == null)
                {
                    report.AppendLine(name + "\t(無し)");
                    continue;
                }

                report.AppendLine(string.Join("\t", new[]
                {
                    name,
                    camera.stereoTargetEye.ToString(),
                    camera.stereoEnabled.ToString(),
                    camera.depth.ToString(CultureInfo.InvariantCulture),
                    camera.nearClipPlane.ToString(CultureInfo.InvariantCulture),
                    camera.farClipPlane.ToString(CultureInfo.InvariantCulture),
                    "0x" + camera.cullingMask.ToString("X"),
                    camera.targetTexture == null ? "(無し)" : camera.targetTexture.name,
                }));
            }

            report.AppendLine();
        }

        /// <summary>
        /// **対照 (Step 12-C2)。** 自前シェーダを既定の URP/Lit に差し替える。
        /// 差し替えた結果、右目に出るようになれば原因はシェーダ。出なければ別。
        /// </summary>
        static void SwapShaders(StringBuilder report)
        {
            report.AppendLine("== 対照: 自前シェーダを URP/Lit へ差し替え ==");

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                report.AppendLine("  **URP/Lit が見つからない。** 差し替えていない。");
                report.AppendLine();
                return;
            }

            var replacement = new Material(lit);
            int swapped = 0;
            var names = new List<string>();

            foreach (Renderer renderer in FindObjectsByType<Renderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Material material = renderer.sharedMaterial;
                if (material == null || material.shader == null
                    || !material.shader.name.StartsWith("SolarSystem/"))
                {
                    continue;
                }

                names.Add($"{renderer.name} ({material.shader.name})");
                renderer.sharedMaterial = replacement;
                swapped++;
            }

            report.AppendLine($"  差し替えた: {swapped} 件");
            foreach (string name in names)
            {
                report.AppendLine("    " + name);
            }

            report.AppendLine();
        }

        /// <summary>撮る。**失敗したら症状を残して null を返す**（例外で落とさない）。</summary>
        Texture2D TryCapture(ScreenCapture.StereoScreenCaptureMode? mode,
                             StringBuilder report, string label)
        {
            try
            {
                Texture2D texture = mode.HasValue
                    ? ScreenCapture.CaptureScreenshotAsTexture(mode.Value)
                    : ScreenCapture.CaptureScreenshotAsTexture();

                if (texture == null)
                {
                    report.AppendLine($"  {label}: **null が返った**");
                    return null;
                }

                report.AppendLine($"  {label}: {texture.width}x{texture.height}");
                return texture;
            }
            catch (System.Exception e)
            {
                report.AppendLine($"  {label}: **例外** {e.GetType().Name}: {e.Message}");
                Debug.LogWarning($"[XrStereoCapture] {label} で例外: {e}");
                return null;
            }
        }

        /// <summary>左右を保存して比べる。**中身の正しさは見ない。**</summary>
        bool Compare(string dir, StringBuilder report, Texture2D left, Texture2D right)
        {
            report.AppendLine("== 経路 1: ScreenCapture の LeftEye / RightEye ==");
            WritePng(dir, "left.png", left);
            WritePng(dir, "right.png", right);
            report.AppendLine("  保存した: left.png / right.png");

            if (left.width != right.width || left.height != right.height)
            {
                report.AppendLine("  **左右で大きさが違う。** 比較できない。");
                return false;
            }

            int width = left.width;
            int height = left.height;

            byte[] leftRgb = ToRgb(left);
            byte[] rightRgb = ToRgb(right);

            report.AppendLine();
            report.AppendLine("== 左右の差 (同じフレーム) ==");
            int differing = ReportDifference(report, left, right);
            report.AppendLine(differing == 0
                ? "  **同一。片目分を 2 回撮っている（成立しない）。**"
                : "  同一ではない。");

            // 目ごとにどれだけ描かれたか。**プローブに依らない指標。**
            report.AppendLine();
            report.AppendLine("== 目ごとの描画量 ==");
            report.AppendLine($"  左: {Brightness(leftRgb)}");
            report.AppendLine($"  右: {Brightness(rightRgb)}");

            // 層ごとの識別色。**正しく重なっているかは見ない。**
            //
            // **プローブが表示されていなければ、これは場面の色を色相で拾っているだけ。**
            // （12-C はここを取り違えた。`-xrProbes` を付けないとプローブは出ない）
            var diagnostics = FindAnyObjectByType<XrDiagnostics>();
            bool probesVisible = diagnostics != null && diagnostics.ProbesVisible;

            report.AppendLine();
            report.AppendLine("== 層ごとの識別色 (写っているかだけ) ==");
            report.AppendLine(probesVisible
                ? "  プローブ: **表示**（この数はプローブ＋同じ色相の場面の色）"
                : "  プローブ: **非表示**。**下の数はプローブではなく、"
                  + "同じ色相を持つ場面の色を数えているだけ**（-xrProbes を付けること）");

            foreach (XrDiagnosticsModel.StereoProbeHit hit in
                     XrDiagnosticsModel.MeasureStereo(leftRgb, rightRgb, width, height))
            {
                report.AppendLine("  " + hit);
            }

            return differing > 0;
        }

        /// <summary>2 枚の差を数える。**判定はしない。数を出すだけ。**</summary>
        static int ReportDifference(StringBuilder report, Texture2D a, Texture2D b)
        {
            if (a.width != b.width || a.height != b.height)
            {
                report.AppendLine("  **大きさが違う。** 比較できない。");
                return -1;
            }

            byte[] left = ToRgb(a);
            byte[] right = ToRgb(b);

            int differing = 0;
            int maxDiff = 0;
            for (int i = 0; i < left.Length; i += 3)
            {
                int d = 0;
                for (int c = 0; c < 3; c++)
                {
                    d = System.Math.Max(d, System.Math.Abs(left[i + c] - right[i + c]));
                }

                if (d > 0)
                {
                    differing++;
                    maxDiff = System.Math.Max(maxDiff, d);
                }
            }

            report.AppendLine($"  大きさ    : {a.width}x{a.height}");
            report.AppendLine($"  差のある画素: {differing} / {a.width * a.height}");
            report.AppendLine($"  最大差    : {maxDiff}");
            return differing;
        }

        /// <summary>平均の明るさと、黒でない画素の数。**判定はしない。**</summary>
        static string Brightness(byte[] rgb)
        {
            long sum = 0;
            int nonBlack = 0;
            for (int i = 0; i < rgb.Length; i += 3)
            {
                int max = System.Math.Max(rgb[i], System.Math.Max(rgb[i + 1], rgb[i + 2]));
                sum += rgb[i] + rgb[i + 1] + rgb[i + 2];
                if (max >= 8)
                {
                    nonBlack++;
                }
            }

            int pixels = rgb.Length / 3;
            return $"平均 {sum / (double)rgb.Length:F2} / 黒でない画素 {nonBlack} / {pixels}";
        }

        static byte[] ToRgb(Texture2D texture)
        {
            Color32[] pixels = texture.GetPixels32();
            var rgb = new byte[pixels.Length * 3];
            for (int i = 0; i < pixels.Length; i++)
            {
                rgb[(i * 3) + 0] = pixels[i].r;
                rgb[(i * 3) + 1] = pixels[i].g;
                rgb[(i * 3) + 2] = pixels[i].b;
            }

            return rgb;
        }

        static void WritePng(string dir, string name, Texture2D texture)
            => File.WriteAllBytes(Path.Combine(dir, name), texture.EncodeToPNG());

        static void Save(string dir, StringBuilder report)
        {
            string path = Path.Combine(dir, "xr-stack.txt");
            File.WriteAllText(path, report.ToString());
            Debug.Log("[XrStereoCapture] " + path);
        }

        static string F(double value)
            => value.ToString("F4", CultureInfo.InvariantCulture);

        static string E(double value)
            => value.ToString("E6", CultureInfo.InvariantCulture);
    }
}
