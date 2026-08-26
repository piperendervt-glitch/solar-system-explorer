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

        /// <summary>到着圏内の半径 [units] (決定 D-10)。</summary>
        public const double ArrivalRadiusUnits = 20.0;

        /// <summary>到着圏内と認める速度の上限 [km/s] (決定 D-10)。手動上限と同値。</summary>
        public const double ArrivalMaxSpeedKmPerSec = 1.0;

        /// <summary>切替判定の基準となる縦 FOV [deg]。</summary>
        public const double ReferenceVerticalFovDegrees = 60.0;

        /// <summary>切替判定の基準となる縦解像度 [px]。</summary>
        public const int ReferencePixelHeight = 1080;

        /// <summary>
        /// 光点の最小表示サイズ [px]。
        /// 火星は地球から 0.090 px しかなく、そのままでは 1 px にも満たず描画されない。
        /// 実際の星と同じで、遠方天体は最小サイズで描いて明るさで距離を表す。
        /// クロスフェード帯 (4〜8 px) では実サイズのほうが大きいのでこのクランプは効かず、
        /// 切替時の角直径一致は保たれる。
        /// </summary>
        public const double MinPointPixels = 2.0;

        public static double BetaToKmPerSec(double beta) => beta * SpeedOfLightKmPerSec;

        public static double KmPerSecToBeta(double kmPerSec) => kmPerSec / SpeedOfLightKmPerSec;
    }
}
