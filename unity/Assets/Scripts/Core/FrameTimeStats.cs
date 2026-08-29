using System;
using System.Collections.Generic;
using System.Globalization;

namespace SolarSystem.Core
{
    /// <summary>
    /// **フレーム時間の集計 (Step 13-0b)。UnityEngine 非依存の純関数。**
    ///
    /// ■ 埋まっていない標本を 0 として混ぜない
    /// `FrameTimingManager` は最初の数フレーム値を返さない。0 を平均に入れると
    /// **待った分だけ平均が下がる。** 標本として渡す前に呼び出し側が捨てること。
    /// ここは「渡された標本だけ」を集計する（空なら例外）。
    ///
    /// ■ p95 の定義
    /// **nearest-rank**（昇順に並べて `ceil(0.95 * n)` 番目、1 始まり）。
    /// 補間しない。標本数が少ないときに補間すると、実在しない値が出る。
    /// n=20 なら 19 番目、n=300 なら 285 番目。
    /// </summary>
    public static class FrameTimeStats
    {
        /// <summary>集計 1 件ぶん。**単位は ms。**</summary>
        public sealed class Summary
        {
            public string Label = string.Empty;
            public int Count;
            public double Mean;
            public double P95;
            public double Min;
            public double Max;

            public string Row()
                => string.Join("\t", new[]
                {
                    Label,
                    Count.ToString(CultureInfo.InvariantCulture),
                    Mean.ToString("F4", CultureInfo.InvariantCulture),
                    P95.ToString("F4", CultureInfo.InvariantCulture),
                    Min.ToString("F4", CultureInfo.InvariantCulture),
                    Max.ToString("F4", CultureInfo.InvariantCulture),
                });

            public override string ToString()
                => $"{Label}: n={Count} / 平均 {Mean:F4} ms / p95 {P95:F4} ms"
                   + $" / 最小 {Min:F4} / 最大 {Max:F4}";
        }

        /// <summary>見出し行。`Row()` と対にする。</summary>
        public const string Header = "対象\t標本数\t平均 [ms]\tp95 [ms]\t最小 [ms]\t最大 [ms]";

        /// <summary>
        /// 集計する。**標本が 1 つも無ければ例外。**
        /// 0 件を「平均 0 ms」として通すと、測れていないことが数字に化ける。
        /// </summary>
        public static Summary Summarize(string label, IReadOnlyList<double> samples)
        {
            if (samples == null || samples.Count == 0)
            {
                throw new ArgumentException(
                    "標本が無い: " + label
                    + "（0 件を平均 0 ms として通さない。測れていないことを数字に化けさせない）",
                    nameof(samples));
            }

            var sorted = new double[samples.Count];
            double sum = 0.0;
            for (int i = 0; i < samples.Count; i++)
            {
                double v = samples[i];
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    throw new ArgumentException("標本に NaN / 無限大がある: " + label, nameof(samples));
                }

                sorted[i] = v;
                sum += v;
            }

            Array.Sort(sorted);

            return new Summary
            {
                Label = label,
                Count = sorted.Length,
                Mean = sum / sorted.Length,
                P95 = Percentile(sorted, 0.95),
                Min = sorted[0],
                Max = sorted[sorted.Length - 1],
            };
        }

        /// <summary>
        /// **nearest-rank のパーセンタイル。** 昇順の配列を受け取る。
        /// rank = ceil(q * n)、1 始まり。補間しない。
        /// </summary>
        public static double Percentile(IReadOnlyList<double> sortedAscending, double q)
        {
            if (sortedAscending == null || sortedAscending.Count == 0)
            {
                throw new ArgumentException("標本が無い", nameof(sortedAscending));
            }

            if (q <= 0.0 || q > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(q), q, "q は 0 より大きく 1 以下");
            }

            int rank = (int)Math.Ceiling(q * sortedAscending.Count);
            if (rank < 1)
            {
                rank = 1;
            }

            if (rank > sortedAscending.Count)
            {
                rank = sortedAscending.Count;
            }

            return sortedAscending[rank - 1];
        }
    }
}
