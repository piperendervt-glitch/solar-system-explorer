using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SolarSystem.Core;
using UnityEngine;
using UnityEngine.Rendering.Universal;

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

        /// <summary>**対照 (Step 12-2c)。** 撮影時にアンチエイリアスを切る。</summary>
        public const string NoAaArg = "-xrCapNoAA";

        /// <summary>**対照 (Step 12-2c)。** 撮影時にポストプロセスを切る。</summary>
        public const string NoPostArg = "-xrCapNoPost";

        /// <summary>
        /// **試したが使えなかった経路 (Step 12-2c)。既定では使わない。**
        ///
        /// `CaptureScreenshotIntoRenderTexture` に目テクスチャと同じ記述子の
        /// RenderTexture を渡しても、**目テクスチャは得られない。**
        /// 実測では 1512x1680 の RT の**左下隅に、640x480 のミラーの中身が
        /// 上下反転で書かれ**（左右が横に並んだ絵）、残りは黒。スライス 1 は
        /// 丸ごと黒。**スライスの指定は効いていない。**
        ///
        /// 記録として残すが、既定はミラー経路。
        /// </summary>
        public const string EyeTextureArg = "-xrCapEyeTexture";

        /// <summary>
        /// **セッション D の測定を回す (量ベース)。**
        ///
        /// 正常 -> 層アイソレーション -> 故意破壊 を**1 回のプロセスの中で**行う。
        /// 12-2c の未解決の雑音（実行をまたぐと ±1 の揺れが 76,000 画素）は
        /// **実行内では出ない**（同じフレームで左目を 2 回撮ると 0 画素）ので、
        /// 比較を全部実行内に閉じれば迂回できる。
        ///
        /// **位置ベースの指標は出さない。** ミラーと目テクスチャの対応が未知で、
        /// 重心の差は 4 段とも同じ値になり視差を測れていない（→ CLAUDE.md §0-B）。
        /// </summary>
        public const string SessionDArg = "-xrSessionD";

        /// <summary>既定の待ちフレーム。シーンとポストプロセスが落ち着くまで待つ。</summary>
        public const int DefaultFrames = 150;

        /// <summary>SPI ゲートが落ちたときの終了コード。**0 と区別できる値。**</summary>
        public const int GateFailedExitCode = 3;

        /// <summary>撮影の経路そのものが成立しなかったときの終了コード。</summary>
        public const int CaptureFailedExitCode = 4;

        /// <summary>
        /// **XR が目のテクスチャを作るまで待つ上限 [秒] (Step 12-2d)。**
        ///
        /// フレーム数で待ってはいけない。12-2b で入れた `captureFramerate = 60` は
        /// **フレームを実時間より速く進める**ので、同じ 150 フレームでも実機では
        /// 値が埋まる前に撮影に入ってしまう（実機の撮影が 2 回とも
        /// MultiPass / Tex2D 256x256 / stereoEnabled=False で落ちた）。
        ///
        /// **条件で待ち、実時間で打ち切る。**
        /// </summary>
        public const float GateTimeoutSeconds = 30f;

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

            // ---- 1. XR が目のテクスチャを作るまで待つ (Step 12-2d) ----
            //
            // **フレーム数で待たない。** captureFramerate はフレームを実時間より
            // 速く進めるので、フレーム数で待つと実機では値が埋まる前に撮ってしまう。
            float waitStarted = Time.realtimeSinceStartup;
            int waitStartedFrame = Time.frameCount;
            XrBoot.StereoFacts facts = XrBoot.ReadStereoFacts();

            while (GateFailure(facts) != null
                   && Time.realtimeSinceStartup - waitStarted < GateTimeoutSeconds)
            {
                yield return null;
                facts = XrBoot.ReadStereoFacts();
            }

            float gateSeconds = Time.realtimeSinceStartup - waitStarted;
            int gateFrames = Time.frameCount - waitStartedFrame;
            Debug.Log($"[XrGate] 待った: {gateFrames} フレーム / {gateSeconds:F3} 秒 / {facts}");

            // ---- 2. 落ち着かせる ----
            //
            // **実行をまたいで同じ絵にする (Step 12-2b)。**
            // 既定では Time.deltaTime が実測時間なので、同じ設定で 2 回撮っても
            // 微振動の位相が違い、内装で 47,000 px 動く。
            // 固定の刻みで進めてから、撮影の直前に世界を止める。
            Time.captureFramerate = 60;

            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }

            // **ここから先は時間を進めない。** 撮り比べのあいだ絵が動かない。
            Time.timeScale = 0f;
            yield return null;

            Directory.CreateDirectory(dir);

            // ---- 条件を採る（撮影の入口）----
            facts = XrBoot.ReadStereoFacts();
            Debug.Log("[XrFacts] 撮影の入口 / " + facts);

            var stack = FindAnyObjectByType<CameraStackController>();
            List<XrStereoOptics.Reading> optics = XrStereoOptics.ReadStack(stack);
            foreach (XrStereoOptics.Reading reading in optics)
            {
                Debug.Log("[XrOptics] 撮影の入口 / " + reading);
            }

            var report = new StringBuilder();
            WriteConditions(report, facts, optics);

            report.AppendLine("== 目のテクスチャが埋まるまで ==");
            report.AppendLine($"  待った: {gateFrames} フレーム / {gateSeconds:F3} 秒"
                              + $" (上限 {GateTimeoutSeconds} 秒)");
            report.AppendLine();
            WriteCameraState(report, stack);

            WriteRenderState(report, stack);

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

            if (StandaloneCapture.HasArg(SessionDArg))
            {
                yield return RunSessionD(dir, report);
                Save(dir, report);
                Debug.Log("[XrSessionD] 終わった");
                yield return null;
                Application.Quit(0);
                yield break;
            }

            // ---- 1. 目ごとのスクリーンショット ----
            //
            // **左右は同じフレームで撮る。** フレームをまたぐと、船の微振動や
            // 天体の運動による差が混ざり、「左右が違う」のが視差なのか時間なのか
            // 区別できなくなる。
            //
            // **対照として同じフレームで左目をもう 1 枚撮る。** これが 0 画素なら、
            // 撮影そのものの雑音の下限は 0（＝左右の差は本物）。
            yield return new WaitForEndOfFrame();

            Texture2D left = CaptureEye(ScreenCapture.StereoScreenCaptureMode.LeftEye,
                                        report, "左目");
            Texture2D right = CaptureEye(ScreenCapture.StereoScreenCaptureMode.RightEye,
                                         report, "右目");
            Texture2D leftAgain = CaptureEye(ScreenCapture.StereoScreenCaptureMode.LeftEye,
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

                yield return MeasureProbesByDifference(dir, report, left, right);
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

        // ================= セッション D =================

        /// <summary>1 件ぶんの測定結果。**量だけ。位置は持たない。**</summary>
        sealed class Amounts
        {
            public string Case = string.Empty;
            public double MeanLeft;
            public double MeanRight;
            public int NonBlackLeft;
            public int NonBlackRight;
            public int Total;

            /// <summary>左右比。**小 / 大**。片目落ちは 0 に落ちる。</summary>
            public double Ratio
            {
                get
                {
                    int max = System.Math.Max(NonBlackLeft, NonBlackRight);
                    return max == 0 ? 1.0 : System.Math.Min(NonBlackLeft, NonBlackRight) / (double)max;
                }
            }

            /// <summary>片目にしか出ていないか。**片目落ちの signature。**</summary>
            public bool OneEyeOnly => (NonBlackLeft == 0) != (NonBlackRight == 0);

            public string Row()
                => string.Join("\t", new[]
                {
                    Case,
                    MeanLeft.ToString("F2", CultureInfo.InvariantCulture),
                    MeanRight.ToString("F2", CultureInfo.InvariantCulture),
                    NonBlackLeft.ToString(CultureInfo.InvariantCulture),
                    NonBlackRight.ToString(CultureInfo.InvariantCulture),
                    Ratio.ToString("F4", CultureInfo.InvariantCulture),
                    OneEyeOnly ? "**片目だけ**" : "-",
                });
        }

        /// <summary>左右を撮って量だけ出す。**画像を残すなら name を渡す。**</summary>
        Amounts MeasurePair(string dir, string caseName, string saveAs, StringBuilder report)
        {
            Texture2D left = CaptureEye(ScreenCapture.StereoScreenCaptureMode.LeftEye,
                                        report, caseName + " L");
            Texture2D right = CaptureEye(ScreenCapture.StereoScreenCaptureMode.RightEye,
                                         report, caseName + " R");

            var a = new Amounts { Case = caseName };
            if (left == null || right == null)
            {
                return a;
            }

            byte[] l = ToRgb(left);
            byte[] r = ToRgb(right);
            a.Total = l.Length / 3;
            Amount(l, out a.MeanLeft, out a.NonBlackLeft);
            Amount(r, out a.MeanRight, out a.NonBlackRight);

            if (!string.IsNullOrEmpty(saveAs))
            {
                WritePng(dir, saveAs + "_L.png", left);
                WritePng(dir, saveAs + "_R.png", right);
                WriteSideBySide(dir, saveAs + "_LR.png", left, right);
            }

            Destroy(left);
            Destroy(right);
            return a;
        }

        static void Amount(byte[] rgb, out double mean, out int nonBlack)
        {
            long sum = 0;
            nonBlack = 0;
            for (int i = 0; i < rgb.Length; i += 3)
            {
                sum += rgb[i] + rgb[i + 1] + rgb[i + 2];
                if (System.Math.Max(rgb[i], System.Math.Max(rgb[i + 1], rgb[i + 2])) >= 8)
                {
                    nonBlack++;
                }
            }

            mean = sum / (double)rgb.Length;
        }

        /// <summary>左右を横に並べた 1 枚。**人間が見るための絵。**</summary>
        static void WriteSideBySide(string dir, string name, Texture2D left, Texture2D right)
        {
            int w = left.width + right.width;
            int h = System.Math.Max(left.height, right.height);
            var joined = new Texture2D(w, h, TextureFormat.RGB24, false);

            var clear = new Color32[w * h];
            joined.SetPixels32(clear);
            joined.SetPixels32(0, 0, left.width, left.height, left.GetPixels32());
            joined.SetPixels32(left.width, 0, right.width, right.height, right.GetPixels32());
            joined.Apply();

            WritePng(dir, name, joined);
            Destroy(joined);
        }

        /// <summary>
        /// **セッション D の測定。量ベースのみ。**
        ///
        /// 正常 -> 層アイソレーション -> 故意破壊 を 1 プロセスの中で回す。
        /// **判定はしない。数値表と画像を出すだけ。**
        /// </summary>
        IEnumerator RunSessionD(string dir, StringBuilder report)
        {
            var diagnostics = FindAnyObjectByType<XrDiagnostics>();
            if (diagnostics == null)
            {
                report.AppendLine("== セッション D ==");
                report.AppendLine("  XrDiagnostics が見つからない。測れない。");
                yield break;
            }

            string view = StandaloneCapture.ArgValue("-scenario");
            if (string.IsNullOrEmpty(view))
            {
                view = "default";
            }

            report.AppendLine("== Q1 層ごとの描画量 (層アイソレーション) ==");
            report.AppendLine("  **段別プローブの画素数は使っていない。**"
                              + " 数十画素では偽の片目落ちが出る (12-2d の実測)。");
            report.AppendLine("  ケース\t左 平均\t右 平均\t左 黒でない\t右 黒でない\t左右比\t片目だけ");

            var rows = new List<Amounts>();

            diagnostics.SetIsolation(0);
            yield return null;
            yield return new WaitForEndOfFrame();
            Amounts normal = MeasurePair(dir, "normal", view + "_00_normal", report);
            rows.Add(normal);

            for (int i = 0; i < 4; i++)
            {
                diagnostics.SetIsolation(i + 1);
                yield return null;
                yield return new WaitForEndOfFrame();
                rows.Add(MeasurePair(dir, "only-" + XrDiagnosticsModel.LayerNames[i],
                                     view + "_01_only-" + XrDiagnosticsModel.LayerNames[i], report));
            }

            // 外景 3 段だけ / コックピット段だけ (Q2 で使う)。
            diagnostics.SetIsolation(-4);
            yield return null;
            yield return new WaitForEndOfFrame();
            Amounts outside = MeasurePair(dir, "outside(Deep+Near+Nearfield)", null, report);
            rows.Add(outside);

            diagnostics.SetIsolation(4);
            yield return null;
            yield return new WaitForEndOfFrame();
            Amounts panel = MeasurePair(dir, "cockpit-only", null, report);
            rows.Add(panel);

            diagnostics.SetIsolation(0);
            yield return null;

            foreach (Amounts a in rows)
            {
                report.AppendLine("  " + a.Row());
            }

            // ---- Q2 ----
            report.AppendLine();
            report.AppendLine("== Q2 窓の外景と計器盤の面積 (目ごと) ==");
            report.AppendLine("  量\t左 [px]\t右 [px]\t左 割合\t右 割合");
            report.AppendLine("  " + string.Join("\t", new[]
            {
                "窓の外景 (外 3 段)",
                outside.NonBlackLeft.ToString(CultureInfo.InvariantCulture),
                outside.NonBlackRight.ToString(CultureInfo.InvariantCulture),
                Share(outside.NonBlackLeft, outside.Total),
                Share(outside.NonBlackRight, outside.Total),
            }));
            report.AppendLine("  " + string.Join("\t", new[]
            {
                "計器盤 (Cockpit 段)",
                panel.NonBlackLeft.ToString(CultureInfo.InvariantCulture),
                panel.NonBlackRight.ToString(CultureInfo.InvariantCulture),
                Share(panel.NonBlackLeft, panel.Total),
                Share(panel.NonBlackRight, panel.Total),
            }));
            report.AppendLine();
            report.AppendLine("  PanelWithinBudget (2〜30 %): 左 "
                              + XrDiagnosticsModel.PanelWithinBudget(panel.NonBlackLeft, panel.Total)
                              + " / 右 "
                              + XrDiagnosticsModel.PanelWithinBudget(panel.NonBlackRight, panel.Total));

            // ---- 故意破壊 ----
            report.AppendLine();
            report.AppendLine("== 故意破壊 (1 プロセスの中で切り替え) ==");
            report.AppendLine("  ケース\t左 平均\t右 平均\t左 黒でない\t右 黒でない\t左右比\t片目だけ\t正常との差 [px]");

            yield return RunFaults(dir, view, diagnostics, normal, report);

            report.AppendLine();
            report.AppendLine("== 測れていないもの (盲点) ==");
            report.AppendLine("  片目だけ深度クリアを飛ばす: **実装できていない。**"
                              + " 深度クリアはカメラ単位で、目ごとに分ける口が Unity/URP に無い。");
            report.AppendLine("  SPI -> Multi-pass: **実行時に切り替える口が無い。**"
                              + " MockHMDBuildSettings はビルド時に焼かれる。"
                              + " 同等の対照は 12-0d でビルドを 2 通り作って取ってある。");
            report.AppendLine("  視差 (Q3): **測らない。** ミラーと目テクスチャの対応が未知"
                              + "（→ CLAUDE.md §0-B）。目視と 12-1 の理論値で判定する。");
        }

        static string Share(int count, int total)
            => total <= 0
                ? "---"
                : (100.0 * count / total).ToString("F2", CultureInfo.InvariantCulture) + " %";

        IEnumerator RunFaults(string dir, string view, XrDiagnostics diagnostics,
                              Amounts normal, StringBuilder report)
        {
            (XrDiagnostics.Fault Fault, XrLayer Layer, XrDiagnostics.Eye Eye, string Name, bool Save)[] cases =
            {
                (XrDiagnostics.Fault.NoDepthClear, XrLayer.Cockpit, XrDiagnostics.Eye.Left,
                 "深度クリアを外す", false),
                (XrDiagnostics.Fault.SwapOverlayOrder, XrLayer.Cockpit, XrDiagnostics.Eye.Left,
                 "描画順の入れ替え", false),
                (XrDiagnostics.Fault.EmptyCullingMask, XrLayer.Cockpit, XrDiagnostics.Eye.Left,
                 "Cockpit の mask を空に", false),
                (XrDiagnostics.Fault.EmptyCullingMask, XrLayer.Deep, XrDiagnostics.Eye.Left,
                 "プロキシ殻の破壊 (Deep mask)", false),
                (XrDiagnostics.Fault.SkyboxOff, XrLayer.Deep, XrDiagnostics.Eye.Left,
                 "星空を消す", false),
                (XrDiagnostics.Fault.BaseCameraOff, XrLayer.Deep, XrDiagnostics.Eye.Left,
                 "Base カメラを止める", false),

                (XrDiagnostics.Fault.DropLayerInOneEye, XrLayer.Deep, XrDiagnostics.Eye.Left,
                 "片目 Deep 落ち (左)", false),
                (XrDiagnostics.Fault.DropLayerInOneEye, XrLayer.Deep, XrDiagnostics.Eye.Right,
                 "片目 Deep 落ち (右)", false),
                (XrDiagnostics.Fault.DropLayerInOneEye, XrLayer.Near, XrDiagnostics.Eye.Left,
                 "片目 Near 落ち (左)", true),
                (XrDiagnostics.Fault.DropLayerInOneEye, XrLayer.Near, XrDiagnostics.Eye.Right,
                 "片目 Near 落ち (右)", false),
                (XrDiagnostics.Fault.DropLayerInOneEye, XrLayer.Nearfield, XrDiagnostics.Eye.Left,
                 "片目 Nearfield 落ち (左)", false),
                (XrDiagnostics.Fault.DropLayerInOneEye, XrLayer.Nearfield, XrDiagnostics.Eye.Right,
                 "片目 Nearfield 落ち (右)", false),
                (XrDiagnostics.Fault.DropLayerInOneEye, XrLayer.Cockpit, XrDiagnostics.Eye.Left,
                 "片目 Cockpit 落ち (左)", true),
                (XrDiagnostics.Fault.DropLayerInOneEye, XrLayer.Cockpit, XrDiagnostics.Eye.Right,
                 "片目 Cockpit 落ち (右)", false),

                (XrDiagnostics.Fault.SkipDepthClearInOneEye, XrLayer.Cockpit, XrDiagnostics.Eye.Left,
                 "片目だけ深度クリアを飛ばす", false),
            };

            int index = 0;
            foreach (var c in cases)
            {
                index++;
                diagnostics.SetFault(c.Fault, c.Layer, c.Eye);
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();

                string save = c.Save
                    ? view + "_1" + index.ToString(CultureInfo.InvariantCulture) + "_" + c.Fault
                    : null;
                Amounts a = MeasurePair(dir, c.Name, save, report);

                int moved = System.Math.Abs(a.NonBlackLeft - normal.NonBlackLeft)
                            + System.Math.Abs(a.NonBlackRight - normal.NonBlackRight);

                string note = c.Fault == XrDiagnostics.Fault.DropLayerInOneEye
                              || c.Fault == XrDiagnostics.Fault.SkipDepthClearInOneEye
                    ? (diagnostics.PerEyeFaultApplied ? "" : " **効いていない**")
                    : "";

                report.AppendLine("  " + a.Row() + "\t"
                                  + moved.ToString(CultureInfo.InvariantCulture) + note);

                diagnostics.ClearFault();
                yield return null;
            }
        }

        // ================= セッション D ここまで =================

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
            report.AppendLine("段\ts\tlossyScale\tf 左 [px]\tf 右 [px]\t眼間 [units]\t眼間 [m]");
            foreach (XrStereoOptics.Reading r in optics)
            {
                report.AppendLine(string.Join("\t", new[]
                {
                    r.CameraName,
                    F(r.StageScale),
                    E(r.LossyScale),
                    F(r.FocalLengthPixelsLeft),
                    F(r.FocalLengthPixelsRight),
                    E(r.EyeSeparationUnits),
                    E(r.EyeSeparationMeters),
                }));

                report.AppendLine($"  目L={r.EyeLeftWorld:F6} / 目R={r.EyeRightWorld:F6}");
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

        /// <summary>
        /// **描画の状態と、非決定要素の切り分け用スイッチ (Step 12-2c)。**
        ///
        /// 12-2b で、世界を止めても実行をまたぐと 76,000 画素動くことが分かった。
        /// 平面の 36 枚は bit-exact なので **XR の経路に固有**。
        /// 候補を 1 つずつ切って測れるように、状態を記録してから切る。
        /// </summary>
        static void WriteRenderState(StringBuilder report, CameraStackController stack)
        {
            report.AppendLine("== 描画の状態 ==");
            report.AppendLine("  フレーム数        : " + Time.frameCount);
            report.AppendLine("  captureFramerate  : " + Time.captureFramerate);
            report.AppendLine("  timeScale         : " + Time.timeScale);
            report.AppendLine("  renderViewportScale : "
                              + UnityEngine.XR.XRSettings.renderViewportScale);
            report.AppendLine("  eyeTextureResolutionScale : "
                              + UnityEngine.XR.XRSettings.eyeTextureResolutionScale);

            bool noAa = StandaloneCapture.HasArg(NoAaArg);
            bool noPost = StandaloneCapture.HasArg(NoPostArg);

            if (stack == null)
            {
                report.AppendLine("  カメラスタックが無い");
                report.AppendLine();
                return;
            }

            report.AppendLine("  段\tAA\tポスト\tMSAA\tDynamicRes");

            (string Name, Camera Camera)[] cameras =
            {
                ("Deep", stack.Deep), ("Near", stack.Near),
                ("Nearfield", stack.Nearfield), ("Cockpit", stack.Cockpit),
            };

            foreach ((string name, Camera camera) in cameras)
            {
                if (camera == null)
                {
                    continue;
                }

                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                report.AppendLine("  " + string.Join("\t", new[]
                {
                    name,
                    data.antialiasing.ToString(),
                    data.renderPostProcessing.ToString(),
                    camera.allowMSAA.ToString(),
                    camera.allowDynamicResolution.ToString(),
                }));

                if (noAa)
                {
                    data.antialiasing = AntialiasingMode.None;
                    camera.allowMSAA = false;
                }

                if (noPost)
                {
                    data.renderPostProcessing = false;
                }
            }

            if (noAa)
            {
                report.AppendLine("  **AA を切った** (" + NoAaArg + ")");
            }

            if (noPost)
            {
                report.AppendLine("  **ポストプロセスを切った** (" + NoPostArg + ")");
            }

            report.AppendLine();
        }

        /// <summary>
        /// **目テクスチャそのものから撮る (Step 12-2c)。**
        ///
        /// ミラーウィンドウ (640x480) は目テクスチャ (1512x1680) と解像度も
        /// アスペクトも違う。**f は目テクスチャ基準の値**なので、ミラーの画素と
        /// 直接比べられない（12-2b で 実測/理論 = 0.55、角度空間で -20 度）。
        ///
        /// `CaptureScreenshotIntoRenderTexture` に**目テクスチャと同じ記述子**の
        /// RenderTexture を渡し、スライス 0 / 1 を読み戻す。
        /// 取れなければミラーへ落ちる（`-xrCapMirror` で明示的にミラーにもできる）。
        /// </summary>
        Texture2D CaptureEye(ScreenCapture.StereoScreenCaptureMode mode,
                             StringBuilder report, string label)
        {
            // **既定はミラー。** 目テクスチャの読み戻しは成立しなかった（上の注記）。
            if (StandaloneCapture.HasArg(EyeTextureArg))
            {
                Texture2D fromEye = TryCaptureEyeTexture(mode, report, label);
                if (fromEye != null)
                {
                    return fromEye;
                }
            }

            return TryCapture(mode, report, label + " (ミラー)");
        }

        Texture2D TryCaptureEyeTexture(ScreenCapture.StereoScreenCaptureMode mode,
                                       StringBuilder report, string label)
        {
            RenderTextureDescriptor desc = UnityEngine.XR.XRSettings.eyeTextureDesc;
            if (desc.width <= 0 || desc.height <= 0 || desc.volumeDepth < 2)
            {
                report.AppendLine($"  {label}: 目テクスチャの記述子が使えない"
                                  + $" ({desc.width}x{desc.height} depth={desc.volumeDepth})");
                return null;
            }

            RenderTexture rt = null;
            var texture = new Texture2D(desc.width, desc.height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;

            try
            {
                rt = new RenderTexture(desc);
                if (!rt.Create())
                {
                    report.AppendLine($"  {label}: RenderTexture を作れない");
                    Destroy(texture);
                    return null;
                }

                ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);

                int slice = mode == ScreenCapture.StereoScreenCaptureMode.RightEye ? 1 : 0;
                Graphics.SetRenderTarget(rt, 0, CubemapFace.Unknown, slice);
                texture.ReadPixels(new Rect(0, 0, desc.width, desc.height), 0, 0);
                texture.Apply();

                report.AppendLine($"  {label}: {texture.width}x{texture.height} (目テクスチャ slice {slice})");
                return texture;
            }
            catch (System.Exception e)
            {
                report.AppendLine($"  {label}: 目テクスチャから撮れない"
                                  + $" **例外** {e.GetType().Name}: {e.Message}");
                Destroy(texture);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
            }
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

        /// <summary>
        /// **層別の数を「プローブを消した絵との差分」で採る (Step 12-2)。**
        ///
        /// ■ なぜ色相の分類だけでは駄目か
        /// 12-C / 12-C2 の実測で、色相だけで拾うと**場面の色を数えてしまう。**
        /// 地球の陸地は Near と同じ色相帯にあり、自前シェーダを差し替えて
        /// 白くしたら Near が 0/0 になった（＝あれはプローブではなかった）。
        /// このままセッション D で使うと**偽の片目落ちが出る。**
        ///
        /// ■ 差分にした理由（もう 1 つの案との比較）
        /// 「目ごとの描画量」のようなプローブに依らない指標も併記しているが、
        /// **段を切り分けられない。** どの段が落ちたかを言うには層別が要る。
        /// プローブの色を変える案は、平面 36 枚の基準値を全部作り直すことになる。
        /// **差分なら基準値を動かさずに、場面の色を確実に除ける。**
        ///
        /// ■ 差分を取るためにフレームを止める
        /// プローブを消すには 1 フレーム進める必要があり、そのままだと微振動と
        /// 天体の運動が混ざる。**`Time.timeScale = 0` で世界を止めてから**
        /// 撮り比べる。止まっていることは「プローブ以外の画素が 0」で確かめられる。
        /// </summary>
        IEnumerator MeasureProbesByDifference(string dir, StringBuilder report,
                                              Texture2D left, Texture2D right)
        {
            report.AppendLine();
            report.AppendLine("== 層ごとの識別色 (プローブを消した絵との差分) ==");

            var diagnostics = FindAnyObjectByType<XrDiagnostics>();
            if (diagnostics == null || !diagnostics.ProbesVisible)
            {
                report.AppendLine("  プローブが非表示なので測らない（-xrProbes を付けること）。");
                report.AppendLine("  **色相だけで数えると場面の色を拾う**ので、代わりに数えない。");
                yield break;
            }

            // 世界は撮影の入口で既に止めてある。
            diagnostics.SetProbesVisible(false);

            yield return null;
            yield return new WaitForEndOfFrame();

            Texture2D bareLeft = CaptureEye(ScreenCapture.StereoScreenCaptureMode.LeftEye,
                                            report, "左目 (プローブ無し)");
            Texture2D bareRight = CaptureEye(ScreenCapture.StereoScreenCaptureMode.RightEye,
                                             report, "右目 (プローブ無し)");

            diagnostics.SetProbesVisible(true);

            if (bareLeft == null || bareRight == null)
            {
                report.AppendLine("  プローブ無しの絵が撮れなかった。測らない。");
                yield break;
            }

            WritePng(dir, "left_no-probes.png", bareLeft);
            WritePng(dir, "right_no-probes.png", bareRight);

            int width = left.width;
            int height = left.height;

            byte[] leftRgb = ToRgb(left);
            byte[] rightRgb = ToRgb(right);
            bool[] maskLeft = Differs(leftRgb, ToRgb(bareLeft));
            bool[] maskRight = Differs(rightRgb, ToRgb(bareRight));

            report.AppendLine($"  差分の画素: 左 {Count(maskLeft)} / 右 {Count(maskRight)}");

            List<XrDiagnosticsModel.ProbeHit> hitsLeft =
                XrDiagnosticsModel.MeasureProbes(leftRgb, width, height, maskLeft);
            List<XrDiagnosticsModel.ProbeHit> hitsRight =
                XrDiagnosticsModel.MeasureProbes(rightRgb, width, height, maskRight);

            report.AppendLine("  段\t左 [px]\t右 [px]\t左 重心\t右 重心\t重心の差 x [px]");
            for (int i = 0; i < hitsLeft.Count; i++)
            {
                XrDiagnosticsModel.ProbeHit l = hitsLeft[i];
                XrDiagnosticsModel.ProbeHit r = hitsRight[i];
                string shift = l.Count == 0 || r.Count == 0
                    ? "---"
                    : F(r.CenterX - l.CenterX);

                report.AppendLine("  " + string.Join("\t", new[]
                {
                    XrDiagnosticsModel.LayerNames[i],
                    l.Count.ToString(CultureInfo.InvariantCulture),
                    r.Count.ToString(CultureInfo.InvariantCulture),
                    F(l.CenterX) + ", " + F(l.CenterY),
                    F(r.CenterX) + ", " + F(r.CenterY),
                    shift,
                }));
            }

            WriteAngleComparison(report, hitsLeft, hitsRight, width, height);

            Destroy(bareLeft);
            Destroy(bareRight);
        }

        /// <summary>
        /// **左右を角度空間で比べる (Step 12-2b)。**
        ///
        /// 画素座標のままだと、左右の投影の非対称（主点のずれ）が視差に化ける。
        /// 12-2 の実測では 4 段とも -42〜-72 px と、**距離に依らない値**が出た。
        /// 目ごとに**その目自身の投影行列**で方向へ戻せば、そのずれは消える。
        ///
        /// **注意: ここで使う画素はミラーウィンドウの解像度**（目テクスチャでは
        /// ない）。ミラーが目の絵をそのまま縮めているという前提が要る。
        /// その前提が崩れていれば角度は正しく出ない。**確かめていない。**
        /// </summary>
        void WriteAngleComparison(StringBuilder report,
                                  List<XrDiagnosticsModel.ProbeHit> hitsLeft,
                                  List<XrDiagnosticsModel.ProbeHit> hitsRight,
                                  int width, int height)
        {
            var stack = FindAnyObjectByType<CameraStackController>();
            List<XrStereoOptics.Reading> optics = XrStereoOptics.ReadStack(stack);

            report.AppendLine();
            report.AppendLine("== 左右の差 (角度空間) ==");
            report.AppendLine("  主点のずれ [px] = m02 * 幅/2。左右で違うぶんが画素座標に乗る。");
            report.AppendLine("  段\tm02 左\tm02 右\t主点 左 [px]\t主点 右 [px]\t主点の差 [px]");

            foreach (XrStereoOptics.Reading r in optics)
            {
                double pl = StereoGeometry.PrincipalOffsetPixels(r.M02Left, width);
                double pr = StereoGeometry.PrincipalOffsetPixels(r.M02Right, width);
                report.AppendLine("  " + string.Join("\t", new[]
                {
                    r.CameraName, F(r.M02Left), F(r.M02Right), F(pl), F(pr), F(pr - pl),
                }));
            }

            report.AppendLine();
            report.AppendLine("  段\ttanX 左\ttanX 右\t角度差 [mrad]\tf 換算 [px]");

            for (int i = 0; i < hitsLeft.Count && i < optics.Count; i++)
            {
                XrDiagnosticsModel.ProbeHit l = hitsLeft[i];
                XrDiagnosticsModel.ProbeHit r = hitsRight[i];
                XrStereoOptics.Reading o = optics[i];

                if (l.Count == 0 || r.Count == 0 || o.M00Left <= 0.0 || o.M00Right <= 0.0)
                {
                    report.AppendLine("  " + XrDiagnosticsModel.LayerNames[i] + "\t---");
                    continue;
                }

                double tanLeft = StereoGeometry.TangentFromPixel(
                    l.CenterX, width, o.M00Left, o.M02Left);
                double tanRight = StereoGeometry.TangentFromPixel(
                    r.CenterX, width, o.M00Right, o.M02Right);

                double radians = System.Math.Atan(tanRight) - System.Math.Atan(tanLeft);

                report.AppendLine("  " + string.Join("\t", new[]
                {
                    XrDiagnosticsModel.LayerNames[i],
                    tanLeft.ToString("F8", CultureInfo.InvariantCulture),
                    tanRight.ToString("F8", CultureInfo.InvariantCulture),
                    F(radians * 1000.0),
                    F(radians * o.FocalLengthPixelsLeft),
                }));
            }

            report.AppendLine();
        }

        static bool[] Differs(byte[] a, byte[] b)
        {
            var mask = new bool[a.Length / 3];
            for (int i = 0; i < mask.Length; i++)
            {
                int p = i * 3;
                mask[i] = a[p] != b[p] || a[p + 1] != b[p + 1] || a[p + 2] != b[p + 2];
            }

            return mask;
        }

        static int Count(bool[] mask)
        {
            int n = 0;
            foreach (bool v in mask)
            {
                if (v)
                {
                    n++;
                }
            }

            return n;
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
