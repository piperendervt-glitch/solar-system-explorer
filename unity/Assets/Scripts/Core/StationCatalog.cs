namespace SolarSystem.Core
{
    /// <summary>
    /// **ステーションの定義の一覧 (Step 13-1a / 13-3b)。**
    ///
    /// **選ばれなかった定義を残さない**（Demo 3 の案 B / C と同じ扱い）。
    /// 箱はフォールバックなので残す（アセットを持たないクローンで組む先）。
    /// </summary>
    public static class StationCatalog
    {
        // ---- 箱（フォールバック）----

        /// <summary>
        /// **箱ステーション。Demo 2 までの挙動と厳密に同じ値。**
        ///
        /// `PortStandoff` の 0.3 は、これまで `SpaceStation.PortStandoffKm` が
        /// `RadiusKm * 1.2` で出していた値（0.25 * 1.2）。
        /// `RequestRange` は **13-3b で 20 -> 2.0**（`DefaultRequestRangeUnits`）。
        ///
        /// **実行時に効くのはこの定義のほう。** `UniverseConstants.ArrivalRadiusUnits` は
        /// ここの既定値の出所と、オートパイロットの到着プロファイルに残っている。
        /// </summary>
        public static StationDefinition Box() => StationDefinition.Create(
            StationDefinition.BoxId,
            prefabGuid: null,

            // 箱はプリミティブで組むので倍率は 1.0。
            scale: RequiredDouble.Positive(1.0),

            // **箱の外形半径 [プレハブ単位]。** 箱は Scale 1.0 なので units と同値。
            // `SceneBuilder` はこの値でプリミティブを組み、`StationView` が
            // `EffectiveScale` を transform に掛ける。**両方がここを読む。**
            modelRadius: RequiredDouble.Positive(SolarSystemModel.StationRadiusKm),

            // **箱はポートが構造物の中心にある**ので、ポートより前方へ半径ぶん出る。
            // これが `MinStandoff = 0.26` の出所（0.25 + near clip 0.01）。
            hullAheadOfPort: RequiredDouble.Positive(SolarSystemModel.StationRadiusKm),

            // 箱はプレハブを持たないので、ポートは原点＋母天体と反対側。
            // **ローカル +Z がポート方向**（`StationBasis` の規約 / `SceneBuilder` が
            // 置くポートの円柱もローカル +Z にある）。
            portLocal: Vec3d.Zero,
            portForward: new Vec3d(0.0, 0.0, 1.0),
            portUp: new Vec3d(0.0, 1.0, 0.0),

            portStandoff: RequiredDouble.Positive(
                SolarSystemModel.StationRadiusKm * BoxStandoffMultiplier),
            requestRange: RequiredDouble.Positive(DefaultRequestRangeUnits),

            // **空のままで確定 (Step 13 クローズ)。** 13-4 で埋める予定だったが
            // 13-4 は実施しない。型と読む経路だけ残す。空は `StationDefinitionTests` が縛る。
            navLights: new NavLight[0],
            windowEmissives: new WindowEmissive[0],

            fallbackId: null);

        /// <summary>
        /// 箱のポート面までの倍率。**船体半分ぶんの余裕**として半径に掛ける。
        /// これまで `SpaceStation.PortStandoffKm` に直書きされていた 1.2。
        /// </summary>
        public const double BoxStandoffMultiplier = 1.2;

        // ---- Cobble（本番 / Step 13-3b で確定）----

        public const string CobbleId = "cobble-station";

        /// <summary>プレハブの GUID（13-2 の実測）。</summary>
        public const string CobblePrefabGuid = "0daf96c15d4c97b4e9e526f6acfce2f0";

        /// <summary>
        /// **倍率 0.008。人間が実機で判定して確定 (Step 13-3b)。**
        ///
        /// 接続面は **(a) 中央の金色の円**（プレハブ単位 0.5241 m）。
        /// 0.5241 * 0.008 * 1000 = **4.193 m** で、船の全幅 1.6075 m の **2.61 倍**
        /// （物差しは 1.5〜3.0 倍）。
        /// </summary>
        public const double CobbleScale = 0.008;

        /// <summary>ピボット基準の外接球の半径 [プレハブ単位]（13-3a の実測）。</summary>
        public const double CobbleModelRadius = 45.0777;

        /// <summary>
        /// **ポート面の中心（プレハブ座標 / 13-3a の実測）。**
        /// `module1` の前面 z = 24.7182 の外接矩形の中心。
        /// **Y = 0.2400 は脊柱の軸上**（`module1` の bbox 中心 Y = 0.783 とは別）。
        /// </summary>
        public static Vec3d CobblePortLocal => new Vec3d(0.0300, 0.2400, 24.7182);

        /// <summary>
        /// **ポートより前方へのはみ出しは 0**（13-3a の実測）。
        /// z > 24.7182 に頂点を持つレンダラーは 0 件で、ポート面が構造全体の最前端。
        /// </summary>
        public const double CobbleHullAheadOfPort = 0.0;

        /// <summary>
        /// **ポート面から目までの距離 [units] = 15 m。**
        ///
        /// 下限は `MinStandoff(0.01) = HullAheadOfPort 0 + near clip 0.01 = 0.010`。
        /// そこへ余裕 `StandoffMarginUnits` を足した値。
        /// **人間が指定したのは接続面と Scale と PortLocal までで、この 1 つは導出値。**
        /// </summary>
        public static double CobblePortStandoff => MinStandoffUnits + StandoffMarginUnits;

        /// <summary>Nearfield 段の near clip [units] = 10 m。</summary>
        public const double NearfieldNearClipUnits = 0.01;

        /// <summary>Cobble の `MinStandoff`（= はみ出し 0 + near clip）。</summary>
        public const double MinStandoffUnits = CobbleHullAheadOfPort + NearfieldNearClipUnits;

        /// <summary>
        /// 下限に足す余裕 [units] = 5 m。
        /// **near clip ちょうどで停めない**（補間の行き過ぎで構造物が消えないように）。
        /// </summary>
        public const double StandoffMarginUnits = 0.005;

        /// <summary>
        /// **Cobble のステーション。13-3b で確定した値を焼いてある。**
        ///
        /// `RequestRange` は `DefaultRequestRangeUnits`（2.0）。箱と同じ値を使う。
        /// **出発点であって確定値ではない。** F4 で振って人間が決める。
        /// </summary>
        public static StationDefinition Cobble() => StationDefinition.Create(
            CobbleId,
            prefabGuid: CobblePrefabGuid,
            scale: RequiredDouble.Positive(CobbleScale),
            modelRadius: RequiredDouble.Positive(CobbleModelRadius),
            hullAheadOfPort: RequiredDouble.NonNegative(CobbleHullAheadOfPort),

            portLocal: CobblePortLocal,

            // **2 軸で持つ**（1 軸だと回転軸が一意に決まらず、前後と上下が
            // 同時に反転しうる / Step 11-2c で実機で踏んだ）。
            portForward: new Vec3d(0.0, 0.0, 1.0),
            portUp: new Vec3d(0.0, 1.0, 0.0),

            portStandoff: RequiredDouble.Positive(CobblePortStandoff),
            requestRange: RequiredDouble.Positive(DefaultRequestRangeUnits),

            navLights: new NavLight[0],
            windowEmissives: new WindowEmissive[0],

            fallbackId: StationDefinition.BoxId);

        // ---- 選択 ----

        // ---- 要求可能距離 (Step 13-3b) ----

        /// <summary>
        /// **ドッキング要求ができる距離 [units]。原点はポート位置。**
        ///
        /// ■ 20.0 から 2.0 へ変えた (13-3b)
        /// 20 は `UniverseConstants.ArrivalRadiusUnits`（決定 D-10）と同値で、
        /// **AP の到着半径をそのまま流用していた値。**
        /// 原点を中心からポートへ揃えたので、下駄（Cobble 0.19775 + 0.015）が外れ、
        /// **「ポートから何 units で要求できるか」という意味のある量になった。**
        ///
        /// ■ **2.0 で確定 (Step 13 クローズ)**
        /// 13-5 の誘導灯の長さと合わせて決める予定だったが、**13-5 は実施しない。**
        /// 誘導灯が無いので列の長さは判断に効かず、**2.0 のまま確定した。**
        /// 箱と Cobble で同じ値を使う。F4 の「要求可能距離」で振れるのはそのまま。
        ///
        /// ■ AP の到着半径は 20 のまま
        /// **到着後に手動で寄る区間が残るのは意図した設計**
        /// （計画書「到着後にひと呼吸ある形を残す」）。
        /// </summary>
        public const double DefaultRequestRangeUnits = 2.0;

        /// <summary>**本番の定義。** アセットが無ければ組む側が箱へ落とす。</summary>
        public static StationDefinition Default() => Cobble();

        /// <summary>Id から引く。**シーンに焼いた Id を実行時に解決する口。**</summary>
        public static StationDefinition ById(string id)
            => id == CobbleId ? Cobble() : Box();
    }
}
