namespace SolarSystem.Core
{
    /// <summary>
    /// 自転の角度を時刻から求める (Step 8-4)。
    ///
    /// **等倍時間 (SpeedMultiplier = 1.0)。** 誇張しない。
    /// UniverseClock は ETA 表示・航行時間・自転で共有されている。
    /// 誇張すると、0.9c で 4.8 分の航行中に地球が何日も自転することになり、
    /// 計器の ETA と画面が矛盾する。
    ///
    /// 画面上の移動量は「円盤の半径 [px] × 回転角 [rad]」。実測:
    ///   earth-close (円盤 356 px) で 5 分に 7.8 px / 1 秒に 0.026 px
    /// 動きとしては知覚できないので、**時刻を進めた静止画を並べて確認する**
    /// (シナリオ earth-spin-t0 / earth-spin-t6h)。
    /// </summary>
    public static class BodyRotation
    {
        /// <summary>自転の倍率。**1.0 = 等倍。** 変えるならここ 1 箇所。</summary>
        public const double SpeedMultiplier = 1.0;

        /// <summary>地球の雲の周期 [時間]。地表 (23.93 h) より速く流れる。</summary>
        public const double EarthCloudPeriodHours = 20.0;

        public const double SecondsPerHour = 3600.0;

        /// <summary>
        /// 経過時刻に対する Y 軸まわりの角度 [度]。
        /// 周期が 0 以下なら 0 を返す (自転しない天体)。
        /// </summary>
        public static double AngleDegrees(double elapsedSeconds, double periodHours)
            => AngleDegrees(elapsedSeconds, periodHours, SpeedMultiplier);

        public static double AngleDegrees(double elapsedSeconds, double periodHours, double multiplier)
        {
            if (periodHours <= 0.0)
            {
                return 0.0;
            }

            double periodSeconds = periodHours * SecondsPerHour;
            return 360.0 * (elapsedSeconds * multiplier) / periodSeconds;
        }

        /// <summary>角速度 [度/秒]。テストが速さを比べるのに使う。</summary>
        public static double DegreesPerSecond(double periodHours)
            => AngleDegrees(1.0, periodHours);
    }
}
