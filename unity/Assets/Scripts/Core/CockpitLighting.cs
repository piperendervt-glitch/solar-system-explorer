using System;
using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// 内装が「潰れていないか」を数える (Step 11-4)。**UnityEngine 非依存。**
    ///
    /// ■ 何を測るか
    /// 内装の画素だけを集めて、**平均輝度・中央値・真っ黒の割合**を出す。
    /// 「ほんのり明るい」の合否は人が目で決めるが、**決めた値がどういう数字
    /// だったかを残す**ための物差し（CLAUDE.md の測定条件の原則）。
    ///
    /// ■ 内装の画素の集め方は呼ぶ側の仕事
    /// コックピット段を消した絵との差分でマスクを作る（`CockpitLightingReport`）。
    /// ここは配列を受け取って数えるだけにして、EditMode で境界値を縛れるようにする。
    /// </summary>
    public static class CockpitLighting
    {
        /// <summary>これ以下を「真っ黒」と数える（0..255）。</summary>
        public const double BlackThreshold = 2.0;

        /// <summary>
        /// **回帰を捕まえるための線。合否ではない。**
        ///
        /// 「ほんのり明るいか」は人が目で決める。ここが縛るのは
        /// **補助光が効いている状態でしか通らないこと**だけ。
        ///
        /// 実測 (11-4 / 1920x1080 / 内装マスク):
        ///   補助光 OFF        真っ黒 8.1 〜 8.9 %
        ///   補助光 ON (0.35)  真っ黒 5.7 〜 6.3 %
        ///
        /// 7 % はその間。**強さを実機で決め直したら、この線も測り直す。**
        /// </summary>
        public const double MaxBlackRatio = 0.07;

        public readonly struct Result
        {
            public Result(int count, double mean, double median, double blackRatio,
                          double min, double max)
            {
                Count = count;
                Mean = mean;
                Median = median;
                BlackRatio = blackRatio;
                Min = min;
                Max = max;
            }

            /// <summary>数えた画素の数。**0 ならマスクが作れていない。**</summary>
            public int Count { get; }

            public double Mean { get; }

            public double Median { get; }

            /// <summary>真っ黒 (輝度 <= 2) の割合。0..1。</summary>
            public double BlackRatio { get; }

            public double Min { get; }

            public double Max { get; }

            public override string ToString()
                => $"画素 {Count} / 平均 {Mean:F2} / 中央値 {Median:F2} / "
                   + $"真っ黒 {BlackRatio * 100.0:F1}% / 最小 {Min:F1} / 最大 {Max:F1}";
        }

        /// <summary>
        /// 輝度の配列 (0..255) を数える。
        ///
        /// **空の配列は例外。** 「マスクが 0 画素なので潰れていない」という
        /// 通り方をさせない（設定漏れが成功に見える形を作らない / 11-3c の反省）。
        /// </summary>
        public static Result Measure(IReadOnlyList<double> luminance)
        {
            if (luminance == null || luminance.Count == 0)
            {
                throw new ArgumentException(
                    "内装の画素が 1 つも無い。マスクの作り方が壊れている");
            }

            var sorted = new double[luminance.Count];
            double sum = 0.0;
            int black = 0;
            double min = double.MaxValue;
            double max = double.MinValue;

            for (int i = 0; i < luminance.Count; i++)
            {
                double v = luminance[i];
                sorted[i] = v;
                sum += v;
                if (v <= BlackThreshold)
                {
                    black++;
                }

                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }

            Array.Sort(sorted);
            double median = sorted.Length % 2 == 1
                ? sorted[sorted.Length / 2]
                : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) * 0.5;

            return new Result(luminance.Count, sum / luminance.Count, median,
                              black / (double)luminance.Count, min, max);
        }

        /// <summary>ITU-R BT.709 の輝度 (0..255)。</summary>
        public static double Luminance(double r, double g, double b)
            => (0.2126 * r) + (0.7152 * g) + (0.0722 * b);

        /// <summary>回帰を捕まえるための線。**合否は人が目で決める。**</summary>
        public static bool WithinBudget(Result result) => result.BlackRatio <= MaxBlackRatio;
    }
}
