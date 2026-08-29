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
                            double distanceFromCenterKm,
                            StationDefinition definition)
        {
            Name = name;
            Host = host;
            OffsetDirection = offsetDirection.Normalized;
            DistanceFromCenterKm = distanceFromCenterKm;
            Definition = definition
                         ?? throw new System.ArgumentNullException(nameof(definition));
        }

        /// <summary>
        /// **このステーションの定義 (Step 13-1a)。**
        /// 地球と火星は同じ定義を共有する。位置と姿勢だけがここ（配置側）にある。
        /// </summary>
        public StationDefinition Definition { get; }

        public string Name { get; }

        public CelestialBody Host { get; }

        /// <summary>母天体の中心から見たオフセット方向 (単位ベクトル)。</summary>
        public Vec3d OffsetDirection { get; }

        public double DistanceFromCenterKm { get; }

        /// <summary>
        /// ステーションの外形半径 [units]。
        ///
        /// **値の出所は定義 (Step 13-3 コミット2)。** `ModelRadius * EffectiveScale`。
        /// **定数として持たない。** Scale を振ったら半径も追随する。
        /// </summary>
        public double RadiusKm => Definition.RadiusUnits;

        /// <summary>母天体の地表からの高度 [units]。</summary>
        public double AltitudeKm => DistanceFromCenterKm - Host.RadiusKm;

        public Vec3d AbsolutePosition => Host.AbsolutePosition + OffsetDirection * DistanceFromCenterKm;

        /// <summary>
        /// ドッキングポートの向き。母天体と反対側 = 深宇宙側を向く。
        /// 船はこちら側から寄る。
        /// </summary>
        public Vec3d PortDirection => OffsetDirection;

        /// <summary>
        /// ローカル軸をワールドへ写す基底 (Step 13-3 コミット2)。
        /// **描画側（`StationView`）も同じものを読む。**
        /// </summary>
        public StationBasis Basis => StationBasis.FromPortDirection(
            PortDirection,
            StationBasis.RotateAbout(SunDirection, PortDirection,
                                     Definition.ArrayRollDegrees));

        /// <summary>
        /// **ステーションから太陽への向き (Step 13-3b)。**
        ///
        /// **太陽は絶対座標の原点**（`SolarSystemModel.CreateOpposition` が
        /// `Vec3d.Zero` に置く）。ここはその前提に乗っている。
        ///
        /// 配置が位相角 90 度（惑星は +X、ステーションは +Y にオフセット）なので、
        /// この向きはほぼ -X の定数になる。**太陽電池アレイの法線（`PortUp`）を
        /// ここへ向ける。**
        /// </summary>
        public Vec3d SunDirection
        {
            get
            {
                Vec3d toSun = Vec3d.Zero - AbsolutePosition;
                return toSun.SqrMagnitude > 0.0 ? toSun.Normalized : new Vec3d(-1.0, 0.0, 0.0);
            }
        }

        /// <summary>
        /// ポート面の位置。ドッキング完了時に船が座る場所。
        ///
        /// **`PortLocal × Scale` から導く (Step 13-3 コミット2)。世界位置を別途持たない。**
        /// 構造物の原点からポートまでのオフセットを基底でワールドへ写し、
        /// そこからポート方向へ `PortStandoff` だけ離れた点。
        /// **箱は `PortLocal = 0` なので、移設前と厳密に同じ値になる。**
        /// </summary>
        public Vec3d PortPosition =>
            AbsolutePosition
            + Basis.ToWorld(Definition.PortLocalUnits)
            + PortDirection * PortStandoffKm;

        /// <summary>
        /// ポート面までの距離 [units]。船体半分ぶんの余裕。
        ///
        /// **値の出所は定義 (Step 13-1a)。** ここで半径から計算しない。
        /// モデルが変われば寸法も変わるので、グローバルな式ではなく定義側に持つ。
        /// **未設定の定義ならここで例外になる**（読む経路がこれ）。
        /// </summary>
        public double PortStandoffKm => Definition.PortStandoff;

        /// <summary>ドッキング要求ができる距離 [units]。**値の出所は定義。**</summary>
        public double RequestRangeUnits => Definition.RequestRange;

        public double DistanceFrom(Vec3d observer) => Vec3d.Distance(observer, AbsolutePosition);
    }
}
