using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// 角直径の計算 (docs/01-architecture.md §3-2 / §3-3)。
    ///
    /// 光点⇔メッシュの切替基準は距離ではなく**画面上の px 数**にする (決定 D-7)。
    /// そうすれば天体の大きさに依存しない 1 本のルールで済む。
    /// </summary>
    public static class AngularSizeSolver
    {
        /// <summary>角直径 [rad] = 2 * atan(半径 / 距離)。</summary>
        public static double AngularDiameterRadians(double radiusKm, double distanceKm)
        {
            if (distanceKm <= 0.0)
            {
                return Math.PI;
            }

            return 2.0 * Math.Atan(radiusKm / distanceKm);
        }

        public static double AngularDiameterDegrees(double radiusKm, double distanceKm)
            => AngularDiameterRadians(radiusKm, distanceKm) * 180.0 / Math.PI;

        public static double AngularDiameterArcseconds(double radiusKm, double distanceKm)
            => AngularDiameterDegrees(radiusKm, distanceKm) * 3600.0;

        /// <summary>
        /// 透視投影の焦点距離 [px]。f = (H/2) / tan(FOV/2)。
        /// 画面中心での「1 rad が何 px になるか」。
        /// </summary>
        public static double FocalLengthPixels(double verticalFovDegrees, int pixelHeight)
        {
            if (pixelHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelHeight));
            }

            return pixelHeight * 0.5 / Math.Tan(verticalFovDegrees * Math.PI / 360.0);
        }

        /// <summary>
        /// 画面中心での 1 px あたりの角度 [rad] = 1/f = 2*tan(FOV/2) / H。
        ///
        /// **FOV/H ではない。** FOV/H は画面全体を等角で割った平均値で、
        /// 透視投影では画面中心の画素のほうが広い角度を張る。
        /// 1080p / 縦FOV60度 では FOV/H = 9.6963e-4 に対し 1/f = 1.06917e-3 で 10.3% 違う。
        /// 実際に RenderTexture へ描いて測ったところ、FOV/H を使うと
        /// 描画サイズが一定して 9.3% 小さく出た (火星まで 5e4 units: 予測 139.6 px / 実測 126 px)。
        /// </summary>
        public static double RadiansPerPixel(double verticalFovDegrees, int pixelHeight)
            => 1.0 / FocalLengthPixels(verticalFovDegrees, pixelHeight);

        /// <summary>角直径を px に直す。</summary>
        public static double AngularDiameterPixels(double radiusKm, double distanceKm, double radiansPerPixel)
            => AngularDiameterRadians(radiusKm, distanceKm) / radiansPerPixel;

        /// <summary>角直径が指定 px になる距離 [km]。切替距離の逆算用。</summary>
        public static double DistanceForPixels(double radiusKm, double pixels, double radiansPerPixel)
        {
            double theta = pixels * radiansPerPixel;
            return radiusKm / Math.Tan(theta * 0.5);
        }

        /// <summary>
        /// 球の**シルエット**が画面上で占める直径 [px] を厳密に求める。
        ///
        /// AngularDiameterPixels は「角度 / 1px の角度」の線形換算で、
        /// 切替判定 (4〜8 px) の範囲では誤差 1e-5 px 未満なので実用上ちょうど一致する。
        /// 一方、天体が画面いっぱいになるような大きい角度では tan の非線形が効くので、
        /// 描画サイズを予測したいときはこちらを使う。
        ///
        /// 球の接線半角は atan ではなく asin(r/d)。投影半径は f * tan(その角)。
        /// </summary>
        public static double ProjectedDiameterPixels(
            double radiusKm, double distanceKm, double focalLengthPixels)
        {
            if (distanceKm <= radiusKm)
            {
                return double.PositiveInfinity;
            }

            double halfAngle = Math.Asin(radiusKm / distanceKm);
            return 2.0 * focalLengthPixels * Math.Tan(halfAngle);
        }
    }
}
