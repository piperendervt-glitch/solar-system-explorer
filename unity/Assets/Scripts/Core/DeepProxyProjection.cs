using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// 遠方天体をプロキシ殻へ射影する (docs/01-architecture.md §3-3 / 決定 D-15)。
    ///
    /// 地球-火星は 7.8e7 units。コックピットの near clip は 1e-5 units なので、
    /// 必要なダイナミックレンジは 7.8e12。1 台のカメラでは描けない。
    ///
    /// そこで**方向は真の方向のまま**、配置半径だけを対数圧縮して
    /// 半径 1,000〜10,000 units の殻に載せる:
    ///
    ///   r_proxy(d) = 1000 + 1800 * (log10(d) - 4)      [d は 1e4..1e9 units]
    ///
    /// 単調増加なので天体同士の前後関係が保存される (掩蔽が正しく描ける)。
    /// スケール係数 s = r_proxy / d を掛けると角直径が厳密に一致する。
    /// </summary>
    public static class DeepProxyProjection
    {
        public const double MinShellRadius = 1000.0;
        public const double MaxShellRadius = 10000.0;

        /// <summary>殻の内側に対応する真の距離 [km]。</summary>
        public const double MinDistanceKm = 1e4;

        /// <summary>殻の外側に対応する真の距離 [km]。</summary>
        public const double MaxDistanceKm = 1e9;

        const double Slope = (MaxShellRadius - MinShellRadius) / 5.0; // log10 で 5 桁ぶん = 1800

        /// <summary>真の距離 → プロキシ殻の配置半径 [units]。範囲外はクランプする。</summary>
        public static double ShellRadius(double distanceKm)
        {
            if (distanceKm <= MinDistanceKm)
            {
                return MinShellRadius;
            }

            if (distanceKm >= MaxDistanceKm)
            {
                return MaxShellRadius;
            }

            return MinShellRadius + Slope * (Math.Log10(distanceKm) - 4.0);
        }

        /// <summary>
        /// オブジェクトに掛けるスケール係数 s = r_proxy / d。
        /// これを掛けると、殻の上に置いても角直径が真の値と一致する。
        /// </summary>
        public static double ScaleFactor(double distanceKm)
        {
            if (distanceKm <= 0.0)
            {
                return 1.0;
            }

            return ShellRadius(distanceKm) / distanceKm;
        }
    }
}
