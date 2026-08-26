using NUnit.Framework;
using SolarSystem.Core;

namespace SolarSystem.Tests.EditMode
{
    public sealed class Vec3dTests
    {
        [Test]
        public void 加減算は成分ごとに行われる()
        {
            var a = new Vec3d(1.0, 2.0, 3.0);
            var b = new Vec3d(0.5, -1.0, 10.0);

            Assert.That((a + b).X, Is.EqualTo(1.5));
            Assert.That((a + b).Y, Is.EqualTo(1.0));
            Assert.That((a + b).Z, Is.EqualTo(13.0));
            Assert.That((a - b).Z, Is.EqualTo(-7.0));
        }

        [Test]
        public void 太陽系スケールでも差分がdouble精度で取れる()
        {
            // 地球-火星 7.8e7 units。float32 なら刻みは 8 units (= 8 km)。
            var earth = new Vec3d(UniverseConstants.AstronomicalUnitKm, 0.0, 0.0);
            var mars = new Vec3d(UniverseConstants.AstronomicalUnitKm + 7.8e7, 0.0, 0.0);

            double d = Vec3d.Distance(earth, mars);

            // 1e-6 units = 1 mm 以内で一致すること。
            Assert.That(d, Is.EqualTo(7.8e7).Within(1e-6));
        }

        [Test]
        public void 長さ0のベクトルを正規化しても例外にならない()
        {
            Assert.That(Vec3d.Zero.Normalized, Is.EqualTo(Vec3d.Zero));
        }

        [Test]
        public void 正規化したベクトルの長さは1()
        {
            var v = new Vec3d(3.0, -4.0, 12.0); // 長さ 13
            Assert.That(v.Magnitude, Is.EqualTo(13.0).Within(1e-12));
            Assert.That(v.Normalized.Magnitude, Is.EqualTo(1.0).Within(1e-15));
        }
    }
}
