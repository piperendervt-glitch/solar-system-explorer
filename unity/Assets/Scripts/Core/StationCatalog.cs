namespace SolarSystem.Core
{
    /// <summary>
    /// **ステーションの定義の一覧 (Step 13-1a)。**
    ///
    /// いまは箱 1 件。13-1b で路線が決まったらここに足す。
    /// **選ばれなかった定義を残さない**（Demo 3 の案 B / C と同じ扱い）。
    /// </summary>
    public static class StationCatalog
    {
        /// <summary>
        /// **箱ステーション。現行の挙動と厳密に同じ値。**
        ///
        /// `PortStandoff` の 0.3 は、これまで `SpaceStation.PortStandoffKm` が
        /// `RadiusKm * 1.2` で出していた値（0.25 * 1.2）。
        /// `RequestRange` の 20 は `UniverseConstants.ArrivalRadiusUnits`（決定 D-10）。
        ///
        /// **実行時に効くのはこの定義のほう。** `UniverseConstants.ArrivalRadiusUnits` は
        /// ここの既定値の出所と、オートパイロットの到着プロファイルに残っている。
        /// </summary>
        public static StationDefinition Box() => StationDefinition.Create(
            StationDefinition.BoxId,
            prefabGuid: null,

            // 箱はプリミティブで組むので倍率は 1.0。
            // メートル単位のモデルを入れるときは 0.001（1 unit = 1 km）。
            scale: RequiredDouble.Positive(1.0),

            // 箱はプレハブを持たないので、ポートは原点＋母天体と反対側 (+Y)。
            portLocal: Vec3d.Zero,
            portForward: new Vec3d(0.0, 1.0, 0.0),
            portUp: new Vec3d(0.0, 0.0, 1.0),

            portStandoff: RequiredDouble.Positive(
                SolarSystemModel.StationRadiusKm * BoxStandoffMultiplier),
            requestRange: RequiredDouble.Positive(UniverseConstants.ArrivalRadiusUnits),

            // **13-4 で使う。今は空。** 型と読む経路だけ用意してある。
            navLights: new NavLight[0],
            windowEmissives: new WindowEmissive[0],

            fallbackId: null);

        /// <summary>
        /// 箱のポート面までの倍率。**船体半分ぶんの余裕**として半径に掛ける。
        /// これまで `SpaceStation.PortStandoffKm` に直書きされていた 1.2。
        /// </summary>
        public const double BoxStandoffMultiplier = 1.2;
    }
}
