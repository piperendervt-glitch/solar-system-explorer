using System.Collections.Generic;

namespace SolarSystem.Core
{
    /// <summary>
    /// 天体の集合 (docs/01-architecture.md §3-2)。
    ///
    /// 配置は「衝」— 太陽・地球・火星がほぼ一直線 (§0-5)。
    /// 太陽を絶対座標の原点に置き、+X 方向へ地球・火星を並べる。
    ///   太陽 -> 地球  1.495978707e8 units
    ///   地球 -> 火星  7.8e7 units
    ///   太陽 -> 火星  2.275978707e8 units (実際の衝の距離 2.279e8 と 0.13% 差)
    /// </summary>
    public sealed class SolarSystemModel
    {
        public const double SunRadiusKm = 696000.0;
        public const double EarthRadiusKm = 6371.0;
        public const double MarsRadiusKm = 3389.5;

        public const double SunToEarthKm = UniverseConstants.AstronomicalUnitKm;
        public const double EarthToMarsKm = 7.8e7;
        public const double SunToMarsKm = SunToEarthKm + EarthToMarsKm;

        readonly List<CelestialBody> _bodies = new List<CelestialBody>();
        readonly List<SpaceStation> _stations = new List<SpaceStation>();

        SolarSystemModel() { }

        public IReadOnlyList<CelestialBody> Bodies => _bodies;

        /// <summary>ステーションの一覧 (Step 5)。N 基のリストとして持つ。</summary>
        public IReadOnlyList<SpaceStation> Stations => _stations;

        /// <summary>ステーションの母天体からの距離 [units]。</summary>
        public const double EarthStationDistanceKm = 12000.0;
        public const double MarsStationDistanceKm = 8000.0;

        /// <summary>ステーションの外形半径 [units] = 500 m。</summary>
        public const double StationRadiusKm = 0.25;

        /// <summary>自転周期 [時間] (Step 8-4)。恒星日。</summary>
        public const double EarthRotationPeriodHours = 23.93;
        public const double MarsRotationPeriodHours = 24.62;

        /// <summary>太陽は自転させない (見た目に寄与しないため)。</summary>
        public const double SunRotationPeriodHours = 0.0;

        public CelestialBody Sun { get; private set; }
        public CelestialBody Earth { get; private set; }
        public CelestialBody Mars { get; private set; }

        /// <summary>衝の配置を作る。テクスチャは使わない (決定 D-22) ので色は単色。</summary>
        public static SolarSystemModel CreateOpposition()
        {
            var model = new SolarSystemModel();

            model.Sun = new CelestialBody(
                "Sun", CelestialBodyKind.Star, SunRadiusKm,
                Vec3d.Zero,
                new Rgb(1.0, 0.95, 0.80), SunRotationPeriodHours);

            model.Earth = new CelestialBody(
                "Earth", CelestialBodyKind.Planet, EarthRadiusKm,
                new Vec3d(SunToEarthKm, 0.0, 0.0),
                new Rgb(0.20, 0.42, 0.72), EarthRotationPeriodHours);

            model.Mars = new CelestialBody(
                "Mars", CelestialBodyKind.Planet, MarsRadiusKm,
                new Vec3d(SunToMarsKm, 0.0, 0.0),
                new Rgb(0.76, 0.36, 0.22), MarsRotationPeriodHours);

            model._bodies.Add(model.Sun);
            model._bodies.Add(model.Earth);
            model._bodies.Add(model.Mars);

            // ステーションは**太陽-惑星軸 (+X) に垂直な +Y 方向、全基とも同じ側**。
            // ここから母天体を見ると位相角がちょうど 90 度になり、
            // 明暗境界線が視界の中央に来る (実測 90.00 度)。
            // 航路 (地球St -> 火星St) が惑星を貫通しないことは
            // DockingTests で検算している。
            var offset = new Vec3d(0.0, 1.0, 0.0);

            model._stations.Add(new SpaceStation(
                "Earth Station", model.Earth, offset, EarthStationDistanceKm, StationRadiusKm));
            model._stations.Add(new SpaceStation(
                "Mars Station", model.Mars, offset, MarsStationDistanceKm, StationRadiusKm));

            return model;
        }

        /// <summary>
        /// 観測者の位置での太陽光の向き (太陽 -> 観測者の単位ベクトル)。
        /// Directional Light の forward に入れる (docs/01-architecture.md §3-5)。
        /// 差分を double で取ってから正規化するので、浮動原点のシフトに影響されない。
        /// </summary>
        public Vec3d SunlightDirectionAt(Vec3d observer)
        {
            Vec3d d = observer - Sun.AbsolutePosition;
            return d.SqrMagnitude > 0.0 ? d.Normalized : new Vec3d(0.0, 0.0, 1.0);
        }

        /// <summary>
        /// 日射の相対強度。地球の位置を 1.0 とした逆二乗則。
        /// 火星では 0.431 になる (docs/01-architecture.md §3-5)。
        /// </summary>
        public double RelativeIrradianceAt(Vec3d observer)
        {
            double d = Sun.DistanceFrom(observer);
            if (d <= 0.0)
            {
                return 1.0;
            }

            double ratio = SunToEarthKm / d;
            return ratio * ratio;
        }
    }
}
