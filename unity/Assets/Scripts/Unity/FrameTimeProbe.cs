using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using SolarSystem.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace SolarSystem.Unity
{
    /// <summary>
    /// **フレーム時間を測る (Step 13-0b)。Step 12 の宿題（Q4 の ms 未取得）の回収。**
    ///
    /// ■ 埋まるまで待つ。フレーム数で待たない
    /// `FrameTimingManager` は最初の数フレーム値を返さない。**固定フレームで読むと
    /// 0 を平均に混ぜる。** 値が入るまで条件待ちし、待ったフレーム数を残す
    /// （Step 12-2d で XR のゲートに対して同じことをした）。
    ///
    /// ■ dt を固定する
    /// `Time.captureFramerate = 60`。実時間で走らせるとフレームの長さが変わって
    /// **船の到達点が変わり、Demo 4 実装後に同じ場所を測れなくなる。**
    /// 固定が読み値に影響しないことは対照で確かめる（`-frameTimeFreeRun`）。
    ///
    /// ■ 系列も残す
    /// 平均だけだと、後で差が出たときに**どのフレームで増えたのかが追えない。**
    ///
    /// **閾値は持たない。判定もしない。** 数字と条件を出すだけ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FrameTimeProbe : MonoBehaviour
    {
        /// <summary>結果の出力先。**無ければ F1 の表示だけ行い、ファイルは書かない。**</summary>
        public const string DirArg = "-frameTimeDir";

        /// <summary>測る窓の長さ [フレーム]。場面ごとに変える。</summary>
        public const string FramesArg = "-frameTimeFrames";

        /// <summary>**対照。** `Time.captureFramerate` を設定しない（実時間で走らせる）。</summary>
        public const string FreeRunArg = "-frameTimeFreeRun";

        /// <summary>**対照。** 計測そのものを載せない（計測コストを見るため）。</summary>
        public const string OffArg = "-frameTimeOff";

        /// <summary>
        /// **対照 (Step 13-0b / 案 D)。** 固定ステップの自動実行を止める。
        ///
        /// `captureFramerate = 60` はゲーム内時計を毎フレーム 1/60 秒進めるので、
        /// **0.02 秒周期の固定ステップが 6 フレームに 5 回走る**（実測: 重いフレームの
        /// 間隔の最頻値が 6）。固定なしでは 1 フレーム約 1.35 ms しか進まないので
        /// 約 15 フレームに 1 回にしかならない。**この差が読み値の差の原因かを見る。**
        /// </summary>
        public const string NoFixedStepArg = "-frameTimeNoFixedStep";

        /// <summary>
        /// **対照 (Step 13-0b / 案 D)。** `Time.fixedDeltaTime` を窓より長くして、
        /// **固定ステップの回数を両条件で 0 に揃える。**
        ///
        /// `simulationMode = Script` は物理の中身を止めるだけで
        /// **FixedUpdate の回数は変えない**（実測: 250 / 19 のまま）。
        /// 回数そのものを揃えないと、回数が原因かどうかを切り分けられない。
        ///
        /// **プロセス内だけの変更**で、`ProjectSettings/TimeManager.asset` は触らない。
        /// </summary>
        public const string BigFixedStepArg = "-frameTimeBigFixedStep";

        /// <summary>既定の窓 [フレーム]。</summary>
        public const int DefaultFrames = 300;

        /// <summary>値が埋まるまで待つ上限 [フレーム]。**超えたら測れなかったとして落とす。**</summary>
        public const int MaxWaitFrames = 600;

        /// <summary>直近の値 [ms]。**F1 が読む。**</summary>
        public static double LatestCpuMs { get; private set; }
        public static double LatestGpuMs { get; private set; }

        /// <summary>直近の値が埋まっているか。**埋まっていないなら F1 は「---」を出す。**</summary>
        public static bool HasLatest { get; private set; }

        /// <summary>計測を載せているか。`-frameTimeOff` で false。</summary>
        public static bool Enabled { get; private set; }

        /// <summary>窓の中で FixedUpdate が走った回数。**直接数える。**</summary>
        int _fixedSteps;
        bool _countFixed;
        bool _bigFixedStep;

        void FixedUpdate()
        {
            if (_countFixed)
            {
                _fixedSteps++;
            }
        }

        readonly List<double> _cpu = new List<double>();
        readonly List<int> _frames = new List<int>();

        // **GPU は CPU と別に持つ (Step 13-0b の実測)。**
        // `gpuFrameTime` は CPU が埋まっているフレームでも 0 で返ることがある
        // （実測: 300 標本中 122〜128 件）。0 を平均に混ぜると GPU が過小に出る。
        // **標本数も別に出す**（CPU の n と同じだと思わせない）。
        readonly List<double> _gpu = new List<double>();
        readonly List<int> _gpuFrames = new List<int>();
        readonly FrameTiming[] _timings = new FrameTiming[1];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoSpawn()
        {
            HasLatest = false;
            LatestCpuMs = 0.0;
            LatestGpuMs = 0.0;

            if (StandaloneCapture.HasArg(OffArg))
            {
                // **対照。** 計測コストを見るために丸ごと載せない。
                Enabled = false;
                return;
            }

            Enabled = true;

            var go = new GameObject("FrameTimeProbe");
            go.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(go);
            go.AddComponent<FrameTimeProbe>();
        }

        /// <summary>1 フレーム読む。**埋まっていなければ false**（0 を返さない）。</summary>
        bool TryRead(out double cpuMs, out double gpuMs)
        {
            cpuMs = 0.0;
            gpuMs = 0.0;

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) == 0)
            {
                return false;
            }

            cpuMs = _timings[0].cpuFrameTime;
            gpuMs = _timings[0].gpuFrameTime;

            // **0 は「まだ埋まっていない」。** 平均に混ぜない。
            return cpuMs > 0.0;
        }

        IEnumerator Start()
        {
            string dir = StandaloneCapture.ArgValue(DirArg);

            int frames = DefaultFrames;
            string framesText = StandaloneCapture.ArgValue(FramesArg);
            if (!string.IsNullOrEmpty(framesText)
                && int.TryParse(framesText, out int parsed) && parsed > 0)
            {
                frames = parsed;
            }

            bool freeRun = StandaloneCapture.HasArg(FreeRunArg);
            if (!freeRun)
            {
                // **dt を固定する。** 実時間だと船の到達点が変わる。
                Time.captureFramerate = 60;
            }

            bool noFixedStep = StandaloneCapture.HasArg(NoFixedStepArg);
            if (noFixedStep)
            {
                // **対照。** 自動の物理ステップを止める（Script モードでは呼ばれない）。
                Physics.simulationMode = SimulationMode.Script;
                Physics2D.simulationMode = SimulationMode2D.Script;
            }

            _bigFixedStep = StandaloneCapture.HasArg(BigFixedStepArg);
            if (_bigFixedStep)
            {
                // **対照。** 窓より長くして固定ステップを 0 回にする。
                Time.fixedDeltaTime = 1000f;
            }

            // ---- 値が埋まるまで待つ（フレーム数で待たない）----
            int waited = 0;
            while (waited < MaxWaitFrames)
            {
                yield return new WaitForEndOfFrame();
                waited++;

                if (TryRead(out double cpu, out double gpu))
                {
                    LatestCpuMs = cpu;
                    LatestGpuMs = gpu;
                    HasLatest = true;
                    break;
                }
            }

            if (!HasLatest)
            {
                Debug.LogWarning($"[FrameTime] **値が埋まらなかった** ({MaxWaitFrames} フレーム待った)。"
                                 + " FrameTimingManager が無効か、この環境では取れない");
            }

            if (string.IsNullOrEmpty(dir))
            {
                // F1 の表示だけ続ける。
                while (true)
                {
                    yield return new WaitForEndOfFrame();
                    if (TryRead(out double cpu, out double gpu))
                    {
                        LatestCpuMs = cpu;
                        LatestGpuMs = gpu;
                        HasLatest = true;
                    }
                }
            }

            int waitedFrames = waited;
            int startFrame = Time.frameCount;
            _fixedSteps = 0;
            _countFixed = true;

            // ---- 窓 ----
            for (int i = 0; i < frames; i++)
            {
                if (TryRead(out double cpu, out double gpu))
                {
                    LatestCpuMs = cpu;
                    LatestGpuMs = gpu;
                    HasLatest = true;

                    _frames.Add(Time.frameCount - startFrame);
                    _cpu.Add(cpu);

                    if (gpu > 0.0)
                    {
                        _gpuFrames.Add(Time.frameCount - startFrame);
                        _gpu.Add(gpu);
                    }
                }

                yield return new WaitForEndOfFrame();
            }

            _countFixed = false;
            Write(dir, frames, waitedFrames, freeRun, noFixedStep);

            yield return null;
            Application.Quit(_cpu.Count > 0 ? 0 : 5);
        }

        void Write(string dir, int frames, int waitedFrames, bool freeRun, bool noFixedStep)
        {
            Directory.CreateDirectory(dir);

            string scenario = StandaloneCapture.ArgValue(ScenarioRunner.ScenarioArg) ?? "(無指定)";
            var report = new StringBuilder();

            report.AppendLine("== 測定条件 ==");
            report.AppendLine("  場面              : " + scenario);
            report.AppendLine("  解像度            : " + Screen.width + "x" + Screen.height);
            report.AppendLine("  環境              : " + (Application.isEditor ? "batchmode Editor" : "exe"));
            report.AppendLine("  窓 [フレーム]     : " + frames);
            report.AppendLine("  dt 固定           : "
                              + (freeRun ? "**していない** (-frameTimeFreeRun)" : "captureFramerate = 60"));
            report.AppendLine("  埋まるまで待った  : " + waitedFrames + " フレーム");
            report.AppendLine("  計測              : " + (Enabled ? "ON" : "OFF"));
            report.AppendLine("  Time.fixedDeltaTime : " + Time.fixedDeltaTime.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("  固定ステップ      : "
                              + (noFixedStep ? "**物理を止めた** (simulationMode = Script)" : "自動")
                              + (_bigFixedStep ? " / **fixedDeltaTime = 1000**" : ""));
            report.AppendLine("  窓の中の FixedUpdate 回数 : " + _fixedSteps
                              + " (窓 " + frames + " フレーム)");
            report.AppendLine("  FrameTimingManager: "
                              + (HasLatest ? "値が入った" : "**値が入らなかった**"));
            report.AppendLine();

            if (_cpu.Count == 0)
            {
                report.AppendLine("== 集計 ==");
                report.AppendLine("  **標本が 0 件。測れていない。**");
                File.WriteAllText(Path.Combine(dir, "frametime.txt"), report.ToString());
                Debug.LogError("[FrameTime] 標本が 0 件");
                return;
            }

            report.AppendLine("== 集計 ==");
            report.AppendLine(FrameTimeStats.Header);
            report.AppendLine(FrameTimeStats.Summarize("CPU", _cpu).Row());
            report.AppendLine(_gpu.Count > 0
                ? FrameTimeStats.Summarize("GPU", _gpu).Row()
                : "GPU	0	---	---	---	---");
            report.AppendLine();
            report.AppendLine("  GPU の標本数が CPU より少ないのは、`gpuFrameTime` が 0 で"
                              + "返るフレームを混ぜていないため（0 を平均に入れない）。");
            report.AppendLine();
            report.AppendLine("  p95 は nearest-rank（補間しない）。");
            report.AppendLine("  **閾値は持たない。判定もしない。**");

            File.WriteAllText(Path.Combine(dir, "frametime.txt"), report.ToString());

            // 系列。**平均だけだとどのフレームで増えたか追えない。**
            var series = new StringBuilder();
            series.AppendLine("frame,cpu_ms");
            for (int i = 0; i < _cpu.Count; i++)
            {
                series.AppendLine(_frames[i].ToString(CultureInfo.InvariantCulture)
                                  + "," + _cpu[i].ToString("F4", CultureInfo.InvariantCulture));
            }

            // **GPU は frame 番号が CPU と揃わない**ので別ファイルにする。
            var gpuSeries = new StringBuilder();
            gpuSeries.AppendLine("frame,gpu_ms");
            for (int i = 0; i < _gpu.Count; i++)
            {
                gpuSeries.AppendLine(_gpuFrames[i].ToString(CultureInfo.InvariantCulture)
                                     + "," + _gpu[i].ToString("F4", CultureInfo.InvariantCulture));
            }

            File.WriteAllText(Path.Combine(dir, "frametime-series-gpu.csv"), gpuSeries.ToString());

            File.WriteAllText(Path.Combine(dir, "frametime-series.csv"), series.ToString());

            Debug.Log("[FrameTime] " + FrameTimeStats.Summarize("CPU", _cpu)
                      + (_gpu.Count > 0 ? " / " + FrameTimeStats.Summarize("GPU", _gpu) : " / GPU 標本なし")
                      + " / 待ち " + waitedFrames + " フレーム");
        }
    }
}
