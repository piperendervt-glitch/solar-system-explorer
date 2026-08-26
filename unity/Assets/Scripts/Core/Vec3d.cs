using System;

namespace SolarSystem.Core
{
    /// <summary>
    /// double 精度の 3 次元ベクトル。
    /// 絶対座標と速度は必ずこの型で持つ。UnityEngine.Vector3 (float32) には
    /// 絶対座標を入れない。1 unit = 1 km。
    ///
    /// 根拠 (docs/01-architecture.md §2-1):
    ///   地球-火星 7.8e7 units のとき float32 の刻みは 8 units = 8 km、
    ///   double なら 1.49e-8 units = 0.0149 mm。
    /// </summary>
    public readonly struct Vec3d : IEquatable<Vec3d>
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public Vec3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3d Zero => new Vec3d(0.0, 0.0, 0.0);

        public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3d operator -(Vec3d v)          => new Vec3d(-v.X, -v.Y, -v.Z);
        public static Vec3d operator *(Vec3d v, double s) => new Vec3d(v.X * s, v.Y * s, v.Z * s);
        public static Vec3d operator *(double s, Vec3d v) => v * s;
        public static Vec3d operator /(Vec3d v, double s) => new Vec3d(v.X / s, v.Y / s, v.Z / s);

        public double SqrMagnitude => X * X + Y * Y + Z * Z;

        public double Magnitude => Math.Sqrt(SqrMagnitude);

        /// <summary>長さ 0 のときは Zero を返す (例外を投げない)。</summary>
        public Vec3d Normalized
        {
            get
            {
                double m = Magnitude;
                return m > 0.0 ? this / m : Zero;
            }
        }

        public static double Dot(Vec3d a, Vec3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static double Distance(Vec3d a, Vec3d b) => (a - b).Magnitude;

        public bool Equals(Vec3d other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

        public override bool Equals(object obj) => obj is Vec3d other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = X.GetHashCode();
                h = (h * 397) ^ Y.GetHashCode();
                h = (h * 397) ^ Z.GetHashCode();
                return h;
            }
        }

        public override string ToString() => $"({X:R}, {Y:R}, {Z:R})";
    }
}
