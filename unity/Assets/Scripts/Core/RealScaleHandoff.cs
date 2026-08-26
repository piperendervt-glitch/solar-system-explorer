namespace SolarSystem.Core
{
    /// <summary>
    /// プロキシ殻から実スケールメッシュへの引き渡し (Step 3b)。
    ///
    /// プロキシ殻は近づくほどスケール係数が上がり、殻の手前側が Deep カメラの
    /// near clip を切って壊れる (火星は 6777 units が下限 / docs/01-architecture.md §3-3)。
    /// その手前で、真の距離・真の大きさで置いた実スケールメッシュへ渡す。
    ///
    /// 帯は 5e4 → 3e4 units。下限 6777 units より十分手前で渡し切る。
    /// 角直径は両者で一致しているので、クロスフェードすればシルエットは飛ばない。
    /// </summary>
    public static class RealScaleHandoff
    {
        /// <summary>この距離より遠ければプロキシ殻だけ。</summary>
        public const double FadeStartDistance = 5.0e4;

        /// <summary>この距離より近ければ実スケールだけ。</summary>
        public const double FadeEndDistance = 3.0e4;

        /// <summary>0 = プロキシ殻のみ / 1 = 実スケールのみ。距離の連続関数。</summary>
        public static double Blend(double distanceKm)
        {
            if (distanceKm >= FadeStartDistance)
            {
                return 0.0;
            }

            if (distanceKm <= FadeEndDistance)
            {
                return 1.0;
            }

            return (FadeStartDistance - distanceKm) / (FadeStartDistance - FadeEndDistance);
        }

        /// <summary>この距離で引き渡しに関わっているか (どちらか一方でも描くか)。</summary>
        public static bool IsActive(double distanceKm) => distanceKm < FadeStartDistance;
    }
}
