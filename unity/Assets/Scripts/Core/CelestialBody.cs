namespace SolarSystem.Core
{
    /// <summary>UnityEngine.Color に依存しない色 (0..1)。Core は UnityEngine を参照しない。</summary>
    public readonly struct Rgb
    {
        public readonly double R;
        public readonly double G;
        public readonly double B;

        public Rgb(double r, double g, double b)
        {
            R = r;
            G = g;
            B = b;
        }
    }

    public enum CelestialBodyKind
    {
        /// <summary>自ら光る。Directional Light の向きの基準になる。</summary>
        Star,
        Planet,
    }

    /// <summary>
    /// 天体 1 個の不変データ (docs/01-architecture.md §1-3)。
    /// 位置は固定 (要件 §3「惑星位置: 固定・公転なし」)。1 unit = 1 km。
    /// </summary>
    public sealed class CelestialBody
    {
        public CelestialBody(string name, CelestialBodyKind kind, double radiusKm, Vec3d absolutePosition, Rgb color)
        {
            Name = name;
            Kind = kind;
            RadiusKm = radiusKm;
            AbsolutePosition = absolutePosition;
            Color = color;
        }

        public string Name { get; }
        public CelestialBodyKind Kind { get; }
        public double RadiusKm { get; }
        public Vec3d AbsolutePosition { get; }
        public Rgb Color { get; }

        public double DistanceFrom(Vec3d observer) => Vec3d.Distance(observer, AbsolutePosition);

        /// <summary>観測者から見た方向 (単位ベクトル)。</summary>
        public Vec3d DirectionFrom(Vec3d observer) => (AbsolutePosition - observer).Normalized;
    }
}
