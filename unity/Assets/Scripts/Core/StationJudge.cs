using System;

namespace SolarSystem.Core
{
    /// <summary>接続面の候補 (Step 13-3b)。**どれを採るかは人間が決める。**</summary>
    public enum PortFaceCandidate
    {
        /// <summary>中央の金色の円。**絵の測定**（13-3a / 13-3_hatch_posZ.png）。</summary>
        GoldDisc = 0,

        /// <summary>突き出した円盤。幾何（z = 24.7182 の前面）。</summary>
        ProtrudingPlate = 1,

        /// <summary>module1 の胴。幾何（z ≈ 24.34 の断面）。</summary>
        ModuleBody = 2,
    }

    /// <summary>判定ビューの視点 (Step 13-3b)。</summary>
    public enum JudgeViewpoint
    {
        /// <summary>ポート面の正面、標準停止距離から。船と接続面の関係を見る。</summary>
        Docking = 0,

        /// <summary>構造全体の全長が画面に収まる距離から。大きさを見る。</summary>
        Overview = 1,
    }

    /// <summary>
    /// **接続面と Scale を人間が決めるための数値 (Step 13-3b)。**
    ///
    /// ■ ここは道具であって決定ではない
    /// 値（どの候補を接続面と見るか / Scale をいくつにするか）は**人間が絵を見て決める。**
    /// このクラスは「いま何を見ているか」を数で出すためだけにある。
    /// **`StationDefinition` には何も書かない。**
    ///
    /// ■ 寸法の出所はすべて 13-3a の実測
    /// [verify/station-port.txt](verify/station-port.txt) と
    /// [verify/station-renderers.txt](verify/station-renderers.txt)。
    ///
    /// ■ 単位
    /// **プレハブ単位 = メートル**（13-3a で確定）。`Scale` はプレハブ単位 -> units の倍率で、
    /// 1 unit = 1 km なので **実寸 [m] = プレハブ単位 x Scale x 1000**。
    /// </summary>
    public static class StationJudge
    {
        // ---- 起動引数 ----

        /// <summary>判定ビューを出す起動引数。**無指定なら何も起きない。**</summary>
        public const string Arg = "-stationJudge";

        // ---- 13-3a の実測（プレハブ単位 = メートル）----

        /// <summary>中央の金色の円の直径。**絵の測定**（1 px = 0.0046 m / 水平弦 114 px）。</summary>
        public const double GoldDiscMeters = 0.5241;

        /// <summary>突き出した円盤の X 幅。幾何（z = 24.7182 の前面 19 頂点）。</summary>
        public const double ProtrudingPlateMeters = 0.9456;

        /// <summary>突き出した円盤の Y 幅。**X とは違う**（0.9456 x 0.8208）。</summary>
        public const double ProtrudingPlateMetersY = 0.8208;

        /// <summary>module1 の胴の幅。幾何（z 24.318〜24.368 の 78 頂点）。</summary>
        public const double ModuleBodyMeters = 1.6888;

        /// <summary>構造全体の全長（bbox の Z）。</summary>
        public const double StationLengthMeters = 62.5408;

        /// <summary>構造全体の幅（bbox の X）。太陽電池アレイで決まる。</summary>
        public const double StationWidthMeters = 41.9387;

        /// <summary>ピボット基準の外接球の半径。**MinStandoff はここから出る。**</summary>
        public const double StationPivotRadiusMeters = 45.0777;

        /// <summary>ポート面の中心（プレハブ座標）。**候補であって確定ではない。**</summary>
        public static Vec3d PortFaceLocal => new Vec3d(0.0300, 0.2400, 24.7182);

        // ---- 船（Demo 3 の実測）----

        /// <summary>船の全幅。`Cockpit3_WithInterior` の bbox の X（13-3a）。</summary>
        public const double ShipWidthMeters = 1.6075;

        /// <summary>船の全高。同 bbox の Y。</summary>
        public const double ShipHeightMeters = 1.6312;

        /// <summary>物差しの下限（計画書 13-3）。開口が船の全幅の何倍か。</summary>
        public const double RatioMin = 1.5;

        /// <summary>物差しの上限。</summary>
        public const double RatioMax = 3.0;

        // ---- Scale の目盛 ----

        public const double ScaleMin = 0.001;
        public const double ScaleMax = 0.008;

        /// <summary>**連続で振るための刻み。** 段の切り替えにしない（境目が分かるように）。</summary>
        public const double ScaleStep = 0.00025;

        /// <summary>目盛の初期位置。**推奨値ではない。** 目盛の真ん中でもない、ただの出発点。</summary>
        public const double ScaleInitial = 0.002;

        // ---- 距離 ----

        /// <summary>
        /// **判定ビューの標準停止距離 [units]。暫定値。**
        ///
        /// Nearfield の near clip は 0.01 units（10 m）で、そこを切ると構造物が消える。
        /// **0.015 units（15 m）は「near clip の内側に入らない」ことだけを満たす仮の値**で、
        /// 実際の `PortStandoff` は 13-3 の次のコミットで導出する。
        /// **この値を根拠に何かを決めないこと。**
        /// </summary>
        public const double ProvisionalStandoffUnits = 0.015;

        /// <summary>Nearfield の near clip [units]。</summary>
        public const double NearfieldNearClipUnits = 0.01;

        /// <summary>全景の余白（1.0 = ぴったり）。</summary>
        public const double OverviewMargin = 1.15;

        // ---- 画面の条件（比を出すときは必ず併記する / §0 の運用メモ）----

        public const int ReferenceWidth = 1920;
        public const int ReferenceHeight = 1080;
        public const double ReferenceFovDegrees = 60.0;

        // ---- 導出 ----

        /// <summary>プレハブ単位の長さ -> 実寸 [m]。</summary>
        public static double ToMeters(double prefabUnits, double scale)
            => prefabUnits * scale * 1000.0;

        /// <summary>プレハブ単位の長さ -> 描画空間の長さ [units]。</summary>
        public static double ToUnits(double prefabUnits, double scale) => prefabUnits * scale;

        /// <summary>候補のプレハブ単位での寸法。</summary>
        public static double CandidateMeters(PortFaceCandidate candidate)
        {
            switch (candidate)
            {
                case PortFaceCandidate.GoldDisc: return GoldDiscMeters;
                case PortFaceCandidate.ProtrudingPlate: return ProtrudingPlateMeters;
                case PortFaceCandidate.ModuleBody: return ModuleBodyMeters;
                default: throw new ArgumentOutOfRangeException(nameof(candidate));
            }
        }

        /// <summary>候補の実寸 [m]。</summary>
        public static double OpeningMeters(PortFaceCandidate candidate, double scale)
            => ToMeters(CandidateMeters(candidate), scale);

        /// <summary>開口が船の全幅の何倍か。**物差しの本体。**</summary>
        public static double RatioToShipWidth(PortFaceCandidate candidate, double scale)
            => OpeningMeters(candidate, scale) / ShipWidthMeters;

        /// <summary>物差し（1.5〜3.0 倍）に入っているか。</summary>
        public static bool WithinRule(PortFaceCandidate candidate, double scale)
        {
            double r = RatioToShipWidth(candidate, scale);
            return r >= RatioMin && r <= RatioMax;
        }

        /// <summary>その候補が物差しを満たす Scale の範囲。</summary>
        public static void ScaleRangeFor(PortFaceCandidate candidate,
                                         out double lo, out double hi)
        {
            double size = CandidateMeters(candidate);
            lo = ShipWidthMeters * RatioMin / (size * 1000.0);
            hi = ShipWidthMeters * RatioMax / (size * 1000.0);
        }

        /// <summary>構造物の外接球の半径 [units]。ピボット基準。</summary>
        public static double RadiusUnits(double scale) => ToUnits(StationPivotRadiusMeters, scale);

        /// <summary>
        /// 半径 + near clip [units]。**13-1a から引き継いだ球の仮定の式。**
        /// 13-3 の次のコミットで実際の幾何へ置き換える（§0 の宿題）。
        /// </summary>
        public static double MinStandoffUnits(double scale)
            => RadiusUnits(scale) + NearfieldNearClipUnits;

        /// <summary>
        /// 距離 D [units] から見た角直径 [度]。**`2 * atan(R / D)`。**
        /// 箱（R = 0.25 / D = 0.5）で 53.13 度になる式。
        /// </summary>
        public static double AngularDiameterDegrees(double scale, double distanceUnits)
        {
            if (!(distanceUnits > 0.0))
            {
                throw new ArgumentOutOfRangeException(nameof(distanceUnits), distanceUnits,
                                                      "距離は正");
            }

            return 2.0 * Math.Atan(RadiusUnits(scale) / distanceUnits) * 180.0 / Math.PI;
        }

        /// <summary>
        /// 画面に占める割合 [%]。**1920x1080 / 画角 60 度の固定条件。**
        /// 解像度とアスペクト比で値が変わるので、条件を併記しない数字は比較に使えない
        /// （§0 の運用メモ）。
        /// </summary>
        public static double CoveragePercent(double scale, double distanceUnits)
        {
            double halfAngle = AngularDiameterDegrees(scale, distanceUnits) * 0.5 * Math.PI / 180.0;
            double f = (ReferenceHeight * 0.5) / Math.Tan(ReferenceFovDegrees * 0.5 * Math.PI / 180.0);
            double radiusPixels = f * Math.Tan(halfAngle);

            return Math.PI * radiusPixels * radiusPixels
                   / (ReferenceWidth * (double)ReferenceHeight) * 100.0;
        }

        /// <summary>
        /// 全景の距離 [units]。**全長を縦に置いて縦画角で収める**
        /// （横で収めるとアスペクト比に依存する）。
        /// </summary>
        public static double OverviewDistanceUnits(double scale, double fovDegrees)
        {
            double halfLength = ToUnits(StationLengthMeters, scale) * 0.5;
            double t = Math.Tan(fovDegrees * 0.5 * Math.PI / 180.0);
            return halfLength / t * OverviewMargin;
        }
    }
}
