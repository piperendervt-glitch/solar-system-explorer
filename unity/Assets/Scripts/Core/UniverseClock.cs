using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// 固定タイムステップの時計 (決定 D-5 / D-24)。
    ///
    /// 可変 dt で積分すると到着位置が実行ごとに変わり、EditMode テストで再現できない。
    /// そこで実 dt を溜め込み、固定幅のステップ数に変換して返す。
    ///
    /// 経過時間は「足し込まない」。StepCount * FixedDeltaSeconds で導出する。
    /// += で積み上げると 36000 ステップぶんの丸め誤差が乗るため
    /// (実測値は UniverseClockTests を参照)。
    /// </summary>
    public sealed class UniverseClock
    {
        /// <summary>1 回のフレームで消化するステップ数の上限。
        /// これが無いと、極端に長い dt が来たときに際限なくステップを回して固まる。</summary>
        public const int MaxStepsPerAdvance = 8;

        double _pendingSeconds;

        public UniverseClock(double fixedDeltaSeconds = UniverseConstants.FixedDeltaSeconds)
        {
            if (fixedDeltaSeconds <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds), "固定ステップ幅は正の値でなければならない。");
            }

            FixedDeltaSeconds = fixedDeltaSeconds;
        }

        public double FixedDeltaSeconds { get; }

        public long StepCount { get; private set; }

        /// <summary>積算した経過時間 [s]。加算ではなく乗算で導く。</summary>
        public double ElapsedSeconds => StepCount * FixedDeltaSeconds;

        /// <summary>
        /// 時刻を差し替える (シナリオの初期状態 / Step 8-0)。
        /// **ステップ数に丸めてから入れる。** 累積誤差を持ち込まないため、
        /// 経過時間は常に StepCount * FixedDeltaSeconds のままにする。
        /// </summary>
        public void SetElapsedSeconds(double seconds)
        {
            if (seconds < 0.0)
            {
                seconds = 0.0;
            }

            StepCount = (long)System.Math.Round(seconds / FixedDeltaSeconds);
        }

        /// <summary>次のステップまでの進捗 0..1。描画側の補間に使う。</summary>
        public double InterpolationAlpha => _pendingSeconds / FixedDeltaSeconds;

        /// <summary>
        /// 実時間を進め、消化すべき固定ステップ数を返す。
        /// 呼び出し側はこの回数だけシミュレーションを 1 ステップ進める。
        /// </summary>
        public int Advance(double realDeltaSeconds)
        {
            if (realDeltaSeconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(realDeltaSeconds), "負の dt は受け付けない。");
            }

            _pendingSeconds += realDeltaSeconds;

            int steps = 0;
            while (_pendingSeconds >= FixedDeltaSeconds && steps < MaxStepsPerAdvance)
            {
                _pendingSeconds -= FixedDeltaSeconds;
                steps++;
            }

            // 上限で打ち切った場合、溜まった残りは捨てる (次フレームへ持ち越すと雪だるまになる)。
            if (steps == MaxStepsPerAdvance && _pendingSeconds >= FixedDeltaSeconds)
            {
                _pendingSeconds = 0.0;
            }

            StepCount += steps;
            return steps;
        }

        public void Reset()
        {
            StepCount = 0;
            _pendingSeconds = 0.0;
        }
    }
}
