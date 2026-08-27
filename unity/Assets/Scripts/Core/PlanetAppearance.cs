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
    }
}
