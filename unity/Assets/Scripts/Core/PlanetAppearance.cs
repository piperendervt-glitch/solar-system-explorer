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
        ///
        /// **bloom 無しの状態で決めた値。9-4 後に要再確認。**
        /// 決めた当時 bloom は実行時に効いていなかった (intensity 0)。
        /// 滲みが乗ると見え方が変わるので、9-4 で bloom を有効にしたあと
        /// 人が見直すこと。
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
        ///
        /// **bloom 無しの状態で決めた値。9-4 後に要再確認。**
        /// 決めた当時 bloom は実行時に効いていなかった (intensity 0)。
        /// 滲みが乗ると見え方が変わるので、9-4 で bloom を有効にしたあと
        /// 人が見直すこと。
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
    
        /// <summary>
        /// コロナの半径倍率 (Step 9-2)。太陽本体の何倍の Quad を出すか。
        ///
        /// **暫定値。実機で決める。** F4 の数値項目「コロナの大きさ」で振れる
        /// (1.0〜6.0 / 刻み 0.25)。
        ///
        /// 減衰は (1 - r)^3 で、r は Quad の中心からの正規化距離。
        /// 2.5 倍のとき太陽の縁は r = 1/2.5 = 0.40 に来るので、
        /// そこでの寄与は 0.216。強度 6.0 との積は 1.296 で
        /// bloom のしきい値 1.05 を超える。縁 (r = 1.0) では厳密に 0 になる。
        /// </summary>
        public const double CoronaRadiusScale = 2.5;
    
        /// <summary>
        /// 光条の要素の最大数 (Step 9-3b)。**アセットに作る Image 要素の個数。**
        /// 筋は中心を通る両側の帯なので、要素 1 個で光条は 2 本に見える。
        /// 画面上の最大本数は この 2 倍 = 12 本。
        /// </summary>
        public const int FlareSpikeElementMax = 6;

        /// <summary>
        /// 光条の本数 (Step 9-3b)。**画面上で数えられる本数。**
        /// 目で数える値なので要素数ではなくこちらを項目の意味にした。
        /// 要素数 = 本数 / 2 なので偶数のみ (パネルの刻みが 2)。
        ///
        /// **暫定値。実機で決める。** 既定 6 本 = 3 要素で 60 度おき。
        /// </summary>
        public const double FlareSpikeCount = 6.0;

        /// <summary>
        /// 光条の長さ (Step 9-3b)。要素の sizeXY.x に入る。
        /// **暫定値。実機で決める。**
        /// </summary>
        public const double FlareSpikeLength = 1.5;

        /// <summary>
        /// ゴースト (レンズ内反射) の強さ (Step 9-3b)。
        /// 太陽から視線をずらしたとき反対側に並ぶ小さな円。
        /// **暫定値。実機で決める。** 確認は sun-offaxis シナリオ。
        /// </summary>
        public const double FlareGhostIntensity = 0.4;
    
        // ---- ポストプロセス (Step 9-4) ----
        //
        // **ここが唯一の出所。** VolumeProfile アセット
        // (Assets/Materials/PostProcess.asset) はこの定数から生成されるもので、
        // アセット側の値を正としない。
        //
        // **Step 6 の「Medium で確定」は実行時には一度も効いていなかった。**
        // Apply を呼ぶのが SceneBuilder (Editor) だけで、実行時に呼ぶ口が無く、
        // かつアセットへ保存もしていなかったため、実行時は Bloom の既定値
        // (intensity 0 = 消灯) で動いていた。

        /// <summary>
        /// Bloom の強度。**暫定値。実機で決める (9-4)。**
        /// Step 6 の値を引き継いでいるが、あれは bloom が効いていない状態で
        /// 決めたものなので、根拠としては弱い。
        /// </summary>
        public const double BloomIntensity = 0.80;

        /// <summary>
        /// Bloom のしきい値。**暫定値。実機で決める (9-4)。**
        /// 太陽のトーンマップ前の出力は 9.055 なので、これを超えていれば滲む。
        /// </summary>
        public const double BloomThreshold = 1.05;

        /// <summary>Bloom の拡散。**暫定値。実機で決める (9-4)。**</summary>
        public const double BloomScatter = 0.60;

        /// <summary>
        /// コロナの減衰の指数 (Step 9-4 でシェーダ側へ移した)。
        /// **テクスチャには焼かない。** 焼くと実機で振れない。
        /// テクスチャは (1 - r) だけを持ち、シェーダが pow(値, 指数) を計算する。
        /// 大きいほど中心に集まり、小さいほどハローが広がる。
        /// **暫定値。実機で決める。**
        /// </summary>
        public const double CoronaFalloff = 3.0;

        /// <summary>
        /// 光条の太さ。要素の sizeXY.y に入る (Step 9-4)。
        /// **暫定値。実機で決める。**
        /// </summary>
        public const double FlareSpikeThickness = 0.10;
    }
}
