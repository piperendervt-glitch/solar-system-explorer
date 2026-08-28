using System;
using System.Collections.Generic;
using System.Globalization;

namespace SolarSystem.Core
{
    /// <summary>カメラ 4 段。**画素の分類と測定はこの順で並べる。**</summary>
    public enum XrLayer
    {
        Deep = 0,
        Near = 1,
        Nearfield = 2,
        Cockpit = 3,
    }

    /// <summary>
    /// XR 診断の画素勘定 (Step 12 の準備 / 平面版)。**UnityEngine 非依存。**
    ///
    /// ■ 何のためにあるか
    /// XR の成否は「4 層が両眼で正しく重なるか」で決まるが、その判定基準を先に
    /// 数値で決めることはできない。**先に平面で層ごとの見え方を出し、人が目で
    /// 承認した状態の数値を基準線として取る**ための道具。
    ///
    /// **ここでは絵の良し悪しを判断しない。数えるだけ。**
    /// </summary>
    public static class XrDiagnosticsModel
    {
        /// <summary>
        /// 層ごとのプローブ色 (0..1)。**色相を 90 度ずつ離してある。**
        ///
        /// - 最大成分は 0.85。bloom のしきい値 0.90 の下なので、滲んで白へ
        ///   寄る前に止まる（ACES を通ると彩度は落ちるが色相は残る）
        /// - 90 度間隔にしたのは、**最初に 4 色を近い色相で置いたら
        ///   分類が隣の色へ寄った**ため（許容 30 度に対して間隔 37 度しか
        ///   無い組があった）
        ///
        /// **色だけでは場面と区別できない。** 空の青や地球の縁は彩度も明度も
        /// 高く、色相だけで拾うと数万画素の誤検出になる（実測: 内装の青を
        /// Cockpit プローブとして 29,968 px 数えていた）。
        /// **プローブの有無で差分を取った画素だけを分類すること。**
        /// </summary>
        public static readonly double[][] ProbeColor =
        {
            new[] { 0.85, 0.00, 0.00 }, // Deep      = 赤     (色相 0)
            new[] { 0.42, 0.85, 0.00 }, // Near      = 黄緑   (色相 90)
            new[] { 0.00, 0.85, 0.85 }, // Nearfield = シアン (色相 180)
            new[] { 0.42, 0.00, 0.85 }, // Cockpit   = 紫     (色相 270)
        };

        public static readonly string[] LayerNames = { "Deep", "Near", "Nearfield", "Cockpit" };

        /// <summary>分類に必要な明るさ (0..1)。暗い画素は「どの層でもない」。</summary>
        public const double MinValue = 0.12;

        /// <summary>分類に必要な彩度。**灰色は必ず弾く**（計器の白文字を拾わないため）。</summary>
        public const double MinSaturation = 0.25;

        /// <summary>色相の許容 [度]。参照色からこれ以上離れていれば「どの層でもない」。</summary>
        public const double HueTolerance = 30.0;

        /// <summary>
        /// **盤の輪郭**からこの画素数までを「縁の帯」として内訳を分ける。
        /// 盤と窓の境目では、アンチエイリアスと 1 px のずれで漏れが必ず少し出る。
        /// それと**本当に盤の内側へ漏れているもの**を混ぜない。
        /// </summary>
        public const int WindowEdgeBandPixels = 4;

        public sealed class ProbeHit
        {
            public XrLayer Layer;
            public int Count;

            /// <summary>重心 [px]。**Count = 0 のときは -1**（0,0 と紛らわしいので）。</summary>
            public double CenterX = -1.0;
            public double CenterY = -1.0;

            public override string ToString()
                => Count == 0
                    ? LayerNames[(int)Layer] + ": 0 px"
                    : LayerNames[(int)Layer] + ": " + Count + " px @ ("
                      + CenterX.ToString("F1", CultureInfo.InvariantCulture) + ", "
                      + CenterY.ToString("F1", CultureInfo.InvariantCulture) + ")";
        }

        public sealed class LeakResult
        {
            /// <summary>窓の領域の中で Deep が見えている画素。**多いほど眺めが開けている。**</summary>
            public int WindowDeep;

            /// <summary>
            /// **盤の外**で外の景色（Deep + Near + Nearfield）が見えている画素。
            ///
            /// ■ 「窓」をガラスのメッシュで定義しないこと（実測でそう変えた）
            /// このコックピットは**枠だけで、視界の大半にガラスのメッシュが無い。**
            /// ガラスの投影は 18,024 px（測定 640x360）で、これは 11-2b の
            /// 「窓の投影面積比 7.8 %」と同じもの——**前面の小さな風防だけ**。
            /// 地球が見えている領域はそこに入らないので、ガラスで窓を定義すると
            /// 「窓の外景 0 px」という無意味な数字になる。
            ///
            /// そこで**計器盤の領域の外で外景が見えている画素**を窓と数える。
            /// ガラスの投影面積は `WindowPixels` に別途出す。
            /// </summary>
            ///
            /// **この場面で眺めを担っているのは Deep ではない。** 地球は約 2e4 units
            /// にあり、実スケールの天体は Near 段が描く。Deep 段が描くのは
            /// プロキシ殻（1,000〜10,000 units）だけなので、窓の外に Deep が
            /// 1 画素も出ない場面がある（実測でそうなった）。
            /// </summary>
            public int WindowOutside;

            /// <summary>計器盤の領域への**外の景色**の漏れ（縁の帯を含まない）。</summary>
            public int PanelOutsideInterior;

            /// <summary>同上。縁の帯のぶん。</summary>
            public int PanelOutsideEdgeBand;

            public int OutsideVisiblePixels;

            /// <summary>計器盤の領域に Deep が漏れている画素（縁の帯を含まない）。</summary>
            public int PanelDeepInterior;

            /// <summary>窓の輪郭から数 px の帯にある漏れ。**縁のアンチエイリアスはここ。**</summary>
            public int PanelDeepEdgeBand;

            public int PanelDeepTotal => PanelDeepInterior + PanelDeepEdgeBand;

            public int WindowPixels;
            public int PanelPixels;
            public int DeepVisiblePixels;
        }

        /// <summary>
        /// 画素を層へ分類する。**どの層でもなければ null。**
        /// 明るさと彩度で足切りしてから、色相の近さで決める。
        /// </summary>
        public static XrLayer? Classify(double r, double g, double b)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            if (max < MinValue)
            {
                return null;
            }

            double saturation = max <= 0.0 ? 0.0 : (max - min) / max;
            if (saturation < MinSaturation)
            {
                return null;
            }

            double hue = Hue(r, g, b);
            XrLayer? best = null;
            double bestDistance = double.MaxValue;

            for (int i = 0; i < ProbeColor.Length; i++)
            {
                double[] c = ProbeColor[i];
                double distance = HueDistance(hue, Hue(c[0], c[1], c[2]));
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (XrLayer)i;
                }
            }

            return bestDistance <= HueTolerance ? best : null;
        }

        /// <summary>色相 [度]。灰色 (彩度 0) では 0 を返す。</summary>
        public static double Hue(double r, double g, double b)
        {
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double span = max - min;
            if (span <= 1.0e-9)
            {
                return 0.0;
            }

            double hue;
            if (max == r)
            {
                hue = 60.0 * (((g - b) / span) % 6.0);
            }
            else if (max == g)
            {
                hue = 60.0 * (((b - r) / span) + 2.0);
            }
            else
            {
                hue = 60.0 * (((r - g) / span) + 4.0);
            }

            return hue < 0.0 ? hue + 360.0 : hue;
        }

        /// <summary>色相の差 [度]。0〜180。</summary>
        public static double HueDistance(double a, double b)
        {
            double d = Math.Abs(a - b) % 360.0;
            return d > 180.0 ? 360.0 - d : d;
        }

        /// <summary>参照色どうしの色相の最小距離 [度]。**分離できているかの物差し。**</summary>
        public static double MinimumHueSeparation()
        {
            double min = double.MaxValue;
            for (int i = 0; i < ProbeColor.Length; i++)
            {
                for (int j = i + 1; j < ProbeColor.Length; j++)
                {
                    double[] a = ProbeColor[i];
                    double[] b = ProbeColor[j];
                    min = Math.Min(min, HueDistance(Hue(a[0], a[1], a[2]), Hue(b[0], b[1], b[2])));
                }
            }

            return min;
        }

        /// <summary>
        /// 画面からプローブを数える。rgb は 1 画素 3 バイト。
        ///
        /// `only` を渡すと**その画素だけ**を見る。プローブを消した絵との差分を
        /// 渡すことで、**場面の色を数えてしまうのを防ぐ**（上の注記）。
        /// </summary>
        public static List<ProbeHit> MeasureProbes(IReadOnlyList<byte> rgb, int width,
                                                   int height, bool[] only = null)
        {
            if (rgb == null || width <= 0 || height <= 0)
            {
                throw new ArgumentException("画素が無い");
            }

            if (rgb.Count < width * height * 3)
            {
                throw new ArgumentException(
                    "画素の数が足りない (" + rgb.Count + " < " + (width * height * 3) + ")");
            }

            var hits = new List<ProbeHit>();
            var sumX = new double[ProbeColor.Length];
            var sumY = new double[ProbeColor.Length];
            var count = new int[ProbeColor.Length];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int pixel = (y * width) + x;
                    if (only != null && !only[pixel])
                    {
                        continue;
                    }

                    int i = pixel * 3;
                    XrLayer? layer = Classify(rgb[i] / 255.0, rgb[i + 1] / 255.0, rgb[i + 2] / 255.0);
                    if (!layer.HasValue)
                    {
                        continue;
                    }

                    int k = (int)layer.Value;
                    count[k]++;
                    sumX[k] += x;
                    sumY[k] += y;
                }
            }

            for (int k = 0; k < ProbeColor.Length; k++)
            {
                hits.Add(new ProbeHit
                {
                    Layer = (XrLayer)k,
                    Count = count[k],
                    CenterX = count[k] == 0 ? -1.0 : sumX[k] / count[k],
                    CenterY = count[k] == 0 ? -1.0 : sumY[k] / count[k],
                });
            }

            return hits;
        }

        /// <summary>
        /// 窓と計器盤の勘定。マスクはすべて同じ長さ。
        ///
        /// **縁の帯を分けて数える。** 窓の輪郭では、アンチエイリアスや 1 px の
        /// ずれで「漏れ」が必ず少し出る。それと**本当に盤の内側へ漏れているもの**を
        /// 混ぜると、どちらが動いたのか分からなくなる。
        /// </summary>
        public static LeakResult MeasureLeak(bool[] deepVisible, bool[] windowRegion,
                                             bool[] panelRegion, bool[] windowEdgeBand,
                                             bool[] outsideVisible = null)
        {
            if (deepVisible == null || windowRegion == null
                || panelRegion == null || windowEdgeBand == null)
            {
                throw new ArgumentException("マスクが無い");
            }

            int n = deepVisible.Length;
            if (windowRegion.Length != n || panelRegion.Length != n || windowEdgeBand.Length != n)
            {
                throw new ArgumentException("マスクの長さが揃っていない");
            }

            var result = new LeakResult();
            for (int i = 0; i < n; i++)
            {
                if (deepVisible[i]) { result.DeepVisiblePixels++; }
                if (windowRegion[i]) { result.WindowPixels++; }
                if (panelRegion[i]) { result.PanelPixels++; }

                if (deepVisible[i] && !panelRegion[i])
                {
                    result.WindowDeep++;
                }

                if (deepVisible[i] && panelRegion[i])
                {
                    if (windowEdgeBand[i])
                    {
                        result.PanelDeepEdgeBand++;
                    }
                    else
                    {
                        result.PanelDeepInterior++;
                    }
                }

                if (outsideVisible == null || !outsideVisible[i])
                {
                    continue;
                }

                result.OutsideVisiblePixels++;
                if (!panelRegion[i])
                {
                    result.WindowOutside++;
                }

                if (panelRegion[i])
                {
                    if (windowEdgeBand[i])
                    {
                        result.PanelOutsideEdgeBand++;
                    }
                    else
                    {
                        result.PanelOutsideInterior++;
                    }
                }
            }

            return result;
        }

        /// <summary>左右 1 組の測定結果。**Q1 が問うのは絶対量ではなく比。**</summary>
        public sealed class StereoProbeHit
        {
            public XrLayer Layer;
            public int Left;
            public int Right;

            /// <summary>
            /// **左右比。** 小さいほう / 大きいほう で 0..1。両目 0 なら 1（差が無い）。
            ///
            /// Single Pass Instanced の片目落ちは「片方だけ 0」として出るので、
            /// **比が 0 に落ちる**。絶対量では視点によって桁が変わるが、比は変わらない。
            /// </summary>
            public double Ratio
            {
                get
                {
                    int max = Math.Max(Left, Right);
                    return max == 0 ? 1.0 : Math.Min(Left, Right) / (double)max;
                }
            }

            /// <summary>片目にしか出ていないか。**片目落ちの signature。**</summary>
            public bool OneEyeOnly => (Left == 0) != (Right == 0);

            public override string ToString()
                => LayerNames[(int)Layer] + ": L " + Left + " / R " + Right
                   + " / 比 " + Ratio.ToString("F4", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// **左右 2 枚から層ごとの画素数と左右比を出す (Step 12 の本番用)。**
        ///
        /// 平面では左右が無いので**同じ画像を 2 枚渡す**。そのとき比は厳密に 1.0。
        /// XR に入ったら、左右のビューをそれぞれ渡す。
        ///
        /// `onlyLeft` / `onlyRight` はプローブを消した絵との差分（場面の色を
        /// 数えないため / セッション 0 の実測）。
        /// </summary>
        public static List<StereoProbeHit> MeasureStereo(
            IReadOnlyList<byte> left, IReadOnlyList<byte> right, int width, int height,
            bool[] onlyLeft = null, bool[] onlyRight = null)
        {
            List<ProbeHit> l = MeasureProbes(left, width, height, onlyLeft);
            List<ProbeHit> r = MeasureProbes(right, width, height, onlyRight);

            var hits = new List<StereoProbeHit>();
            for (int i = 0; i < l.Count; i++)
            {
                hits.Add(new StereoProbeHit
                {
                    Layer = (XrLayer)i,
                    Left = l[i].Count,
                    Right = r[i].Count,
                });
            }

            return hits;
        }

        /// <summary>
        /// **計器盤の画素数が想定の範囲にあるか。**
        ///
        /// 「窓 = 計器盤の外」と定義したので、**内装の描画が丸ごと失敗すると
        /// 窓が画面いっぱいに広がり、漏れが 0 になって「正常」に見えてしまう。**
        /// 盤の面積そのものを不変条件として持ち、外れたら失敗させる。
        /// </summary>
        public static bool PanelWithinBudget(int panelPixels, int totalPixels)
        {
            if (totalPixels <= 0)
            {
                return false;
            }

            double share = panelPixels / (double)totalPixels;
            return share >= MinPanelShare && share <= MaxPanelShare;
        }

        /// <summary>
        /// 計器盤が画面に占める割合の下限・上限。
        /// 実測（測定 640x360 / cockpit-view）で 15,269 / 230,400 = 6.6 %。
        /// **内装が消えれば 0 %、窓の定義が壊れれば 100 % 近くになる。**
        /// </summary>
        public const double MinPanelShare = 0.02;
        public const double MaxPanelShare = 0.30;

        /// <summary>マスクの縁（外側に非マスクを持つ画素）。</summary>
        public static bool[] Boundary(bool[] mask, int width, int height)
        {
            var edge = new bool[mask.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width) + x;
                    if (!mask[i])
                    {
                        continue;
                    }

                    bool outside =
                        x == 0 || x == width - 1 || y == 0 || y == height - 1
                        || !mask[i - 1] || !mask[i + 1]
                        || !mask[i - width] || !mask[i + width];

                    edge[i] = outside;
                }
            }

            return edge;
        }

        /// <summary>マスクを radius 画素だけ太らせる。</summary>
        public static bool[] Dilate(bool[] mask, int width, int height, int radius)
        {
            var grown = new bool[mask.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (!mask[(y * width) + x])
                    {
                        continue;
                    }

                    int y0 = Math.Max(0, y - radius), y1 = Math.Min(height - 1, y + radius);
                    int x0 = Math.Max(0, x - radius), x1 = Math.Min(width - 1, x + radius);
                    for (int yy = y0; yy <= y1; yy++)
                    {
                        for (int xx = x0; xx <= x1; xx++)
                        {
                            grown[(yy * width) + xx] = true;
                        }
                    }
                }
            }

            return grown;
        }

        /// <summary>凸多角形を塗りつぶしてマスクにする（走査線）。</summary>
        public static bool[] FillPolygon(IReadOnlyList<Vec2d> polygon, int width, int height)
        {
            var mask = new bool[width * height];
            if (polygon == null || polygon.Count < 3)
            {
                return mask;
            }

            for (int y = 0; y < height; y++)
            {
                double scan = y + 0.5;
                double left = double.MaxValue;
                double right = double.MinValue;

                for (int i = 0; i < polygon.Count; i++)
                {
                    Vec2d a = polygon[i];
                    Vec2d b = polygon[(i + 1) % polygon.Count];
                    if ((a.Y <= scan && b.Y > scan) || (b.Y <= scan && a.Y > scan))
                    {
                        double t = (scan - a.Y) / (b.Y - a.Y);
                        double x = a.X + (t * (b.X - a.X));
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                    }
                }

                if (left > right)
                {
                    continue;
                }

                int x0 = Math.Max(0, (int)Math.Ceiling(left - 0.5));
                int x1 = Math.Min(width - 1, (int)Math.Floor(right - 0.5));
                for (int x = x0; x <= x1; x++)
                {
                    mask[(y * width) + x] = true;
                }
            }

            return mask;
        }
    }
}
