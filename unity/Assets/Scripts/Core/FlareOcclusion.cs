using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// レンズフレアの遮蔽率を解析的に求める (Step 9-3a)。
    ///
    /// **深度バッファを使わない。** 惑星は Transparent / ZWrite off で深度を書かないため、
    /// SRP Lens Flare の occlusion 機能は原理的に効かない
    /// (docs/02-demo2-plan.md 9-3 の確定事項)。角半径と角距離だけで判定する。
    ///
    /// **単位について。** 位置は Vec3d の絶対座標で単位は units、半径は km。
    /// 本プロジェクトは 1 unit = 1 km なので数値としては同じだが、
    /// 引数名では区別している (CLAUDE.md 5「座標・単位」)。
    /// </summary>
    public static class FlareOcclusion
    {
        /// <summary>
        /// 観測者から見た角半径 [rad]。
        ///
        /// **観測者が天体の内部にいるとき (D &lt;= R) は π/2 に倒す。**
        /// asin(R/D) は R/D &gt; 1 で NaN を返し、そのまま強度まで伝播する。
        /// 内部にいるなら視界の半分以上が天体なので π/2 が妥当な上限。
        /// </summary>
        public static double AngularRadius(double radiusKm, double distanceUnits)
        {
            if (distanceUnits <= 0.0 || radiusKm <= 0.0)
            {
                return 0.0;
            }

            if (distanceUnits <= radiusKm)
            {
                return Math.PI * 0.5;
            }

            return Math.Asin(radiusKm / distanceUnits);
        }

        /// <summary>観測者から見た 2 点の角距離 [rad]。</summary>
        public static double AngularDistance(Vec3d observer, Vec3d a, Vec3d b)
        {
            Vec3d ua = a - observer;
            Vec3d ub = b - observer;
            double la = ua.Magnitude;
            double lb = ub.Magnitude;
            if (la <= 0.0 || lb <= 0.0)
            {
                return 0.0;
            }

            double cos = Vec3d.Dot(ua, ub) / (la * lb);
            cos = Math.Min(1.0, Math.Max(-1.0, cos));
            return Math.Acos(cos);
        }

        /// <summary>
        /// 天体 1 つによる遮蔽率 0.0〜1.0。
        ///
        ///   d &gt;= as + ab      → 0.0 (接触もしていない)
        ///   d &lt;= |ab - as|    → 上限 (ab &gt;= as なら 1.0 / そうでなければ面積比 (ab/as)^2)
        ///   その間            → smoothstep で補間
        ///
        /// **太陽より遠い天体は無視する。** 観測者と太陽の間にあるものだけが遮る。
        /// </summary>
        public static double OcclusionBy(
            Vec3d observer,
            Vec3d sunCenter, double sunRadiusKm,
            Vec3d bodyCenter, double bodyRadiusKm)
        {
            double sunDistance = Vec3d.Distance(observer, sunCenter);
            double bodyDistance = Vec3d.Distance(observer, bodyCenter);

            if (bodyDistance >= sunDistance || sunDistance <= 0.0)
            {
                return 0.0;
            }

            double sunAngle = AngularRadius(sunRadiusKm, sunDistance);
            double bodyAngle = AngularRadius(bodyRadiusKm, bodyDistance);
            if (sunAngle <= 0.0 || bodyAngle <= 0.0)
            {
                return 0.0;
            }

            double d = AngularDistance(observer, sunCenter, bodyCenter);

            double outer = sunAngle + bodyAngle;
            if (d >= outer)
            {
                return 0.0;
            }

            // 天体が太陽より小さく見えるときは、隠せるのは面積比まで。
            double cap = bodyAngle >= sunAngle
                ? 1.0
                : (bodyAngle / sunAngle) * (bodyAngle / sunAngle);

            double inner = Math.Abs(bodyAngle - sunAngle);
            if (d <= inner)
            {
                return cap;
            }

            double span = outer - inner;
            if (span <= 0.0)
            {
                return cap;
            }

            double t = (outer - d) / span;
            t = Math.Min(1.0, Math.Max(0.0, t));
            return SmoothStep(t) * cap;
        }

        /// <summary>
        /// 全天体のうち最大の遮蔽率。太陽自身は数えない。
        /// </summary>
        public static double Occlusion(Vec3d observer, SolarSystemModel model)
        {
            if (model == null || model.Sun == null)
            {
                return 0.0;
            }

            double sunRadius = SolarSystemModel.SunRadiusKm;
            Vec3d sunCenter = model.Sun.AbsolutePosition;

            double worst = 0.0;
            foreach (CelestialBody body in model.Bodies)
            {
                if (body == null || body.Kind == CelestialBodyKind.Star)
                {
                    continue; // 太陽自身は遮蔽物ではない
                }

                double o = OcclusionBy(observer, sunCenter, sunRadius,
                                       body.AbsolutePosition, body.RadiusKm);
                if (o > worst)
                {
                    worst = o;
                }
            }

            return worst;
        }

        /// <summary>端点で微分が 0 になる補間。遮蔽の開始・終了で折れ目が出ない。</summary>
        public static double SmoothStep(double t) => t * t * (3.0 - 2.0 * t);
    }
}
