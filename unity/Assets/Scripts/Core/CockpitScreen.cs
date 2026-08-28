namespace SolarSystem.Core
{
    /// <summary>
    /// 画面に出す役割 (Step 11-3a)。**UnityEngine 非依存。**
    ///
    /// **A と B の 2 案を実機で見比べるための一時的な足場。**
    /// 決まったら選ばれなかったほうの役割は削除する（見えないコードを残さない）。
    /// </summary>
    public enum ScreenRole
    {
        /// <summary>SPD / DST / ETA。</summary>
        Flight,

        /// <summary>TGT / ALN / ドッキング状態（案 A の右下）。</summary>
        TargetFull,

        /// <summary>TGT / ALN（案 B）。</summary>
        Target,

        /// <summary>ドッキング状態 + AP（案 B）。</summary>
        Docking,

        /// <summary>ALN の度数と可否だけ（案 A の HUD）。</summary>
        Alignment,

        /// <summary>速度ダイヤルの段。</summary>
        SpeedDial,

        /// <summary>AP の ON/OFF（案 A の小ゲージ）。</summary>
        Autopilot,

        /// <summary>警告灯（案 B の小ゲージ）。整列が許容外のときに点く。</summary>
        Warning,

        /// <summary>SPD / DST の 2 行（案 C の大画面）。</summary>
        FlightShort,

        /// <summary>ETA / ドッキング状態の 2 行（案 C の HUD）。</summary>
        EtaDocking,
    }

    /// <summary>
    /// 計器の見せ方 (Step 11-3c)。**実機で 3 つを見比べて 1 つに決める。**
    /// </summary>
    public enum ScreenMode
    {
        /// <summary>ベンダーの面にそのまま貼る。**既定。** 斜めから見るので歪む。</summary>
        OnFace,

        /// <summary>面の位置に、視線へ正対するクアッドを置いて貼る。</summary>
        Facing,

        /// <summary>
        /// 面に貼ったまま、**RT の中身をあらかじめ逆に歪ませる**。
        /// 台形も縦横比の潰れも 1 枚のホモグラフィで消える (`ScreenWarpSolver`)。
        /// </summary>
        Prewarp,
    }

    /// <summary>役割の割り当て案 (Step 11-3a)。**実機で見比べて 1 つに決める。**</summary>
    public enum ScreenLayout
    {
        /// <summary>大画面に文字情報、HUD は最小限。**既定。**</summary>
        A,

        /// <summary>中央の HUD を主役にする。</summary>
        B,

        /// <summary>
        /// **大画面は 2 行まで**にして、あふれた項目を HUD とゲージへ逃がす。
        ///
        /// 大画面の字は**行の高さで決まっている**（実測）ので、文字列を短くしても
        /// 大きくならない。行数を 3 -> 2 に減らすのが唯一の効く手。
        /// </summary>
        C,
    }

    /// <summary>
    /// 1 つの画面の割り当て (Step 11-3a)。
    ///
    /// **レンダラー名で引く。** プレハブの子の順番に依存すると、アセットを
    /// 差し替えたときに黙って壊れる。
    /// </summary>
    public sealed class CockpitScreen
    {
        public CockpitScreen(string rendererName, ScreenRole role, int textureLongSide)
        {
            RendererName = rendererName;
            Role = role;
            TextureLongSide = textureLongSide;
        }

        public string RendererName { get; }

        public ScreenRole Role { get; }

        /// <summary>
        /// この面の RenderTexture の**長辺**の解像度。
        ///
        /// ■ **もう一辺はメッシュの実寸から出す。決め打ちしない。**
        /// 最初は「画面上の投影サイズの 2 倍」で縦横とも決めていたが、**これは誤り**
        /// だった。投影サイズは目の位置と面の傾きで決まる見かけの大きさで、**面そのものの
        /// 縦横比とは別物。** 実測では大画面が実寸 345.4 x 183.5 mm（比 1.882）なのに
        /// RT を 1024x384（比 2.667）にしていたため、**文字が横に 0.706 倍つぶれていた。**
        ///
        /// いまは `CockpitBuilder` が UV の勾配から「u 方向・v 方向の実寸」を出し、
        /// その比で短辺を決める。**アセットを差し替えても自動で追従する。**
        ///
        /// **1 枚のアトラスにしない理由**は、ベンダーの UV 配置が画面上の必要解像度と
        /// 釣り合っておらず（小さいゲージほど目に近い）、アトラスだと 3072x2400 必要に
        /// なるため。
        /// </summary>
        public int TextureLongSide { get; }

        /// <summary>
        /// RenderTexture の異方性フィルタの段数 (Step 11-3b)。
        /// **面が傾いているので縮小率が方向で違う。** mipmap だけだと大きいほうの
        /// 縮小率で段が選ばれ、もう一方が必要以上にぼける。値は実測で決める。
        /// </summary>
        public const int TextureAniso = 4;

        public override string ToString() => $"{RendererName}:{Role}";
    }

    /// <summary>
    /// メッシュの UV 矩形を 0..1 へ写す変換 (Step 11-3b)。
    ///
    /// ■ なぜ要るか
    /// 5 つの画面は**共有マテリアルの中でそれぞれ別の UV 矩形**を使っている
    /// （実測: Screen-1 は u[0.004..0.500] v[0.684..0.978] など）。
    /// 面ごとに別の RenderTexture を貼るには、その矩形を RT の全面へ写す必要がある。
    /// **`_BaseMap_ST` を MaterialPropertyBlock で面ごとに渡せば、マテリアルを
    /// 複製せずに済む。**
    ///
    /// ■ URP Lit では ST は 1 つ
    /// `Shaders/LitInput.hlsl` の CBUFFER にあるのは `_BaseMap_ST` だけで、
    /// `SampleEmission` も同じ uv を使う（`_EmissionMap_ST` は無い）。
    /// **1 つの ST が `_BaseMap` と `_EmissionMap` の両方に効く。**
    /// </summary>
    public static class UvRemap
    {
        /// <summary>
        /// `uv * scale + offset` が [min, max] を [0, 1] に写すような (scale, offset)。
        /// **幅が 0 のときは等倍で返す**（0 除算で NaN を撒かない）。
        /// </summary>
        public static void ToUnit(double min, double max, out double scale, out double offset)
        {
            double span = max - min;
            if (span <= 1.0e-6)
            {
                scale = 1.0;
                offset = 0.0;
                return;
            }

            scale = 1.0 / span;
            offset = -min / span;
        }
    }
}
