namespace SolarSystem.Core
{
    /// <summary>
    /// 惑星の見た目の数値 (Step 8-2 / 8-3)。
    ///
    /// **Editor ではなく Core に置く。** 値そのものはデータであって Editor の
    /// ロジックではない。実行時のデバッグパネル (Step 8-0b) が既定値として
    /// 読むので、実行時から届く場所に無いと二重定義になる。
    /// </summary>
    public static class PlanetAppearance
    {
        /// <summary>
        /// 地球の大気の強さ。**5.0 で確定** (earth-close-day を目視して決定)。
        ///
        /// **画素テストは相対成分 B/(R+G+B) で判定している。**
        /// 絶対値の B で比べると、中心が明るい海のとき大気の強さと無関係に
        /// 中心が勝ちうるため。5.0 での実測: 縁 0.5145 / 中心 0.4975 (差 +0.0170)。
        ///
        /// **注意: 以前 MaterialLibrary 側に書いていた「縁 B=132.0 / 中心 B=117.2
        /// (差 +14.8)」は誤った走査範囲で得た値だった。** PlayMode テストの画素走査が
        /// `y &lt; Height - PanelTop` となっており、計器パネルを除くつもりで
        /// 画面下 1/3 だけを見ていた (GetPixels32 は下から上に並ぶ)。
        /// 修正後の絶対値は 縁 132.4 / 中心 130.6 で、差は +1.8 しかない。
        /// </summary>
        public const double EarthAtmosphereStrength = 5.0;

        /// <summary>火星の大気は地球の 1/4 (薄い大気)。</summary>
        public const double MarsAtmosphereRatio = 0.25;

        /// <summary>
        /// 雲の不透明度のゲイン。**1.15 で確定** (earth-close-day を目視して決定)。
        ///
        /// 素材 (earth_clouds) の最大値は 227/255 = 0.89 しかない。
        /// これを 1.0 まで持ち上げる係数は 255/227 = 1.123 で、1.15 はその少し上。
        ///
        /// **EditMode テストは下限 1.12 しか縛っていない。**
        /// 「素材の最大 0.89 のままでは薄い」ことしか自動では言えないため。
        /// 濃さそのものは目視で決めた値なので、変えるならまた目で見ること。
        /// </summary>
        public const double CloudOpacity = 1.15;

        /// <summary>
        /// 太陽の HDR 発光強度 (Step 9-1)。**光点と殻の両方に同じ値を掛ける。**
        ///
        /// 航行範囲で太陽は LOD 帯 (4〜8px) を横切るため
        /// (地球近傍 8.70px / 火星近傍 5.71px)、殻だけ HDR にすると
        /// 火星側で光点が優勢になり太陽が暗くなっていく。
        ///
        /// **暫定値。** 計画書 9-1 の 4〜8 の中間を初期値にした。
        /// **9-4 で bloom と露出を再調整するときに決め直す。**
        /// 実機での目視による決定もそのときにまとめて行う (9-1 では行わない)。
        ///
        /// **実測: この値でトーンマップ前の最大輝度は 5.688。**
        /// bloom のしきい値 1.05 (Medium) を構造として超えている。
        /// 測り方は SunPlayModeTests。カメラの renderPostProcessing を
        /// 切らないと ACES が残って潰れる (CLAUDE.md 0-B)。
        /// </summary>
        public const double SunEmissionIntensity = 6.0;
    }
}
