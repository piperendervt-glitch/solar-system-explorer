namespace SolarSystem.Core
{
    /// <summary>
    /// 単位系と物理定数。1 Unity unit = 1 km (docs/00-requirements.md §3)。
    /// 速度はすべて km/s。c 表記への変換は表示側でだけ行う。
    /// </summary>
    public static class UniverseConstants
    {
        /// <summary>光速 [km/s] = [units/s]。</summary>
        public const double SpeedOfLightKmPerSec = 299792.458;

        /// <summary>1 天文単位 [km] = [units]。</summary>
        public const double AstronomicalUnitKm = 1.495978707e8;

        /// <summary>既定の巡航速度 [c] (決定 D-8)。</summary>
        public const double DefaultCruiseBeta = 0.9;

        /// <summary>最大の巡航速度 [c] (決定 D-14)。1.0c だと γ が発散して相対論拡張が不能になる。</summary>
        public const double MaxCruiseBeta = 0.99;

        /// <summary>手動操作の速度上限 [km/s] (決定 D-11)。c 表記は使わない。</summary>
        public const double ManualMaxSpeedKmPerSec = 1.0;

        /// <summary>固定タイムステップ [s] (決定 D-5)。60 Hz。</summary>
        public const double FixedDeltaSeconds = 1.0 / 60.0;

        public static double BetaToKmPerSec(double beta) => beta * SpeedOfLightKmPerSec;

        public static double KmPerSecToBeta(double kmPerSec) => kmPerSec / SpeedOfLightKmPerSec;
    }
}
