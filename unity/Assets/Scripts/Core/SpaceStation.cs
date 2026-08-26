namespace SolarSystem.Core
{
    /// <summary>
    /// ステーション 1 基の不変データ (Step 5)。
    ///
    /// 位置は「どの天体の、中心からどれだけ離れた、どの方向か」で持つ。
    /// 絶対座標を直接書くと桁を間違えるし、天体を動かしたときに追従しない。
    ///
    /// 配置は**太陽-惑星軸に垂直な方向、全基とも同じ側**。
    /// こうすると、ステーションから母天体を見たときに位相角が 90 度になり、
    /// 明暗境界線が視界の中央に来る (実測 90.00 度)。
    /// </summary>
    public sealed class SpaceStation
    {
        public SpaceStation(string name, CelestialBody host, Vec3d offsetDirection,
                            double distanceFromCenterKm, double radiusKm)
        {
            Name = name;
            Host = host;
            OffsetDirection = offsetDirection.Normalized;
            DistanceFromCenterKm = distanceFromCenterKm;
            RadiusKm = radiusKm;
        }

        public string Name { get; }

        public CelestialBody Host { get; }

        /// <summary>母天体の中心から見たオフセット方向 (単位ベクトル)。</summary>
        public Vec3d OffsetDirection { get; }

        public double DistanceFromCenterKm { get; }

        /// <summary>ステーションの外形半径 [units]。</summary>
        public double RadiusKm { get; }

        /// <summary>母天体の地表からの高度 [units]。</summary>
        public double AltitudeKm => DistanceFromCenterKm - Host.RadiusKm;

        public Vec3d AbsolutePosition => Host.AbsolutePosition + OffsetDirection * DistanceFromCenterKm;

        /// <summary>
        /// ドッキングポートの向き。母天体と反対側 = 深宇宙側を向く。
        /// 船はこちら側から寄る。
        /// </summary>
        public Vec3d PortDirection => OffsetDirection;

        /// <summary>ポート面の位置。ドッキング完了時に船が座る場所。</summary>
        public Vec3d PortPosition => AbsolutePosition + PortDirection * PortStandoffKm;

        /// <summary>ポート面までの距離 [units]。船体半分ぶんの余裕。</summary>
        public double PortStandoffKm => RadiusKm * 1.2;

        public double DistanceFrom(Vec3d observer) => Vec3d.Distance(observer, AbsolutePosition);
    }
}
