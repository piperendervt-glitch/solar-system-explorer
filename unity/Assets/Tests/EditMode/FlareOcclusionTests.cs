using NUnit.Framework;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>レンズフレアの遮蔽判定 (Step 9-3a)。純関数なので素の値で叩く。</summary>
    public sealed class FlareOcclusionTests
    {
        // 観測者を原点に置き、太陽を +X の遠方、遮蔽物をその手前に置く。
        static readonly Vec3d Observer = Vec3d.Zero;
        const double SunDistance = 1.0e8;
        const double SunRadius = 696000.0;
        const double BodyDistance = 1.0e4;
        const double BodyRadius = 6371.0;

        static Vec3d Sun => new Vec3d(SunDistance, 0.0, 0.0);

        /// <summary>太陽方向から角度 theta [rad] だけ離れた位置に遮蔽物を置く。</summary>
        static Vec3d BodyAt(double theta) => new Vec3d(
            BodyDistance * System.Math.Cos(theta),
            BodyDistance * System.Math.Sin(theta),
            0.0);

        static double Occ(double theta) =>
            FlareOcclusion.OcclusionBy(Observer, Sun, SunRadius, BodyAt(theta), BodyRadius);

        static double SunAngle => FlareOcclusion.AngularRadius(SunRadius, SunDistance);
        static double BodyAngle => FlareOcclusion.AngularRadius(BodyRadius, BodyDistance);

        [Test]
        public void 遮蔽なしの配置で0になる()
        {
            double outer = SunAngle + BodyAngle;
            double o = Occ(outer * 1.2);
            Debug.Log($"[Step9-3a] 外接の外 (d = {outer * 1.2 * Mathf.Rad2Deg:F3} 度): 遮蔽率 {o:F4}");
            Assert.That(o, Is.EqualTo(0.0));
        }

        [Test]
        public void 完全に隠れる配置で1になる()
        {
            double o = Occ(0.0);
            Debug.Log($"[Step9-3a] 中心が重なる: 遮蔽率 {o:F4} " +
                      $"(天体 {BodyAngle * Mathf.Rad2Deg:F3} 度 / 太陽 {SunAngle * Mathf.Rad2Deg:F3} 度)");
            Assert.That(o, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void 縁にかかる配置で0と1の間になる()
        {
            // 太陽の中心が天体の縁に乗る位置 = 遮蔽率 0.5。
            double o = Occ(BodyAngle);
            Debug.Log($"[Step9-3a] 太陽の中心が縁に乗る: 遮蔽率 {o:F4}");
            Assert.That(o, Is.GreaterThan(0.0));
            Assert.That(o, Is.LessThan(1.0));
            Assert.That(o, Is.EqualTo(0.5).Within(1e-6), "縁に乗ったら半分のはず");
        }

        [Test]
        public void 角距離に対して単調に減る()
        {
            double outer = SunAngle + BodyAngle;
            double previous = double.MaxValue;
            for (int i = 0; i <= 20; i++)
            {
                double theta = outer * i / 20.0;
                double o = Occ(theta);
                Assert.That(o, Is.LessThanOrEqualTo(previous + 1e-12),
                    $"角距離 {theta * Mathf.Rad2Deg:F4} 度で増えた");
                previous = o;
            }

            Debug.Log($"[Step9-3a] 単調: d=0 で {Occ(0.0):F4} / d=外接で {Occ(outer):F4}");
        }

        [Test]
        public void 太陽より遠い天体は影響しない()
        {
            // 太陽の 2 倍の距離に、同じ見かけの大きさの天体を置く。
            var far = new Vec3d(SunDistance * 2.0, 0.0, 0.0);
            double o = FlareOcclusion.OcclusionBy(Observer, Sun, SunRadius, far, BodyRadius * 2.0);
            Debug.Log($"[Step9-3a] 太陽より遠い天体: 遮蔽率 {o:F4}");
            Assert.That(o, Is.EqualTo(0.0));
        }

        [Test]
        public void 視線と無関係な方向なら0になる()
        {
            double o = Occ(Mathf.PI * 0.5); // 真横
            Debug.Log($"[Step9-3a] 90 度離れた天体: 遮蔽率 {o:F4}");
            Assert.That(o, Is.EqualTo(0.0));
        }

        [Test]
        public void 天体が太陽より小さく見えるときは面積比で頭打ちになる()
        {
            // 小さくて近い天体。ab < as になるよう半径を絞る。
            const double tinyRadius = 20.0;
            var tiny = new Vec3d(BodyDistance, 0.0, 0.0);
            double ab = FlareOcclusion.AngularRadius(tinyRadius, BodyDistance);
            double asun = SunAngle;
            Assert.That(ab, Is.LessThan(asun), "前提: 天体のほうが小さく見える");

            double o = FlareOcclusion.OcclusionBy(Observer, Sun, SunRadius, tiny, tinyRadius);
            double expected = (ab / asun) * (ab / asun);
            Debug.Log($"[Step9-3a] 小さい天体が中心に重なる: 遮蔽率 {o:F4} / 面積比 {expected:F4}");
            Assert.That(o, Is.EqualTo(expected).Within(1e-9));
            Assert.That(o, Is.LessThan(1.0), "小さい天体で完全遮蔽にしてはいけない");
        }

        [Test]
        public void 観測者が天体の内部にいてもNaNにならない()
        {
            // D <= R。asin(R/D) は定義域の外。
            double inside = FlareOcclusion.AngularRadius(BodyRadius, BodyRadius * 0.5);
            Debug.Log($"[Step9-3a] 内部にいるときの角半径 {inside:F4} rad (π/2 = {System.Math.PI * 0.5:F4})");
            Assert.That(double.IsNaN(inside), Is.False, "NaN が出た");
            Assert.That(inside, Is.EqualTo(System.Math.PI * 0.5).Within(1e-9));

            var near = new Vec3d(BodyRadius * 0.5, 0.0, 0.0);
            double o = FlareOcclusion.OcclusionBy(Observer, Sun, SunRadius, near, BodyRadius);
            Debug.Log($"[Step9-3a] 天体の内部から見た遮蔽率 {o:F4}");
            Assert.That(double.IsNaN(o), Is.False, "遮蔽率が NaN");
            Assert.That(o, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void 太陽自身は遮蔽物に数えない()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();

            // 太陽のすぐ手前。太陽自身を数えると 1.0 になってしまう位置。
            Vec3d toward = (model.Sun.AbsolutePosition - model.Earth.AbsolutePosition).Normalized;
            Vec3d observer = model.Sun.AbsolutePosition - toward * (SolarSystemModel.SunRadiusKm * 5.0);

            double o = FlareOcclusion.Occlusion(observer, model);
            Debug.Log($"[Step9-3a] 太陽の手前 5 半径から: 遮蔽率 {o:F4}");
            Assert.That(o, Is.EqualTo(0.0), "太陽自身を遮蔽物に数えている");
        }
    }
}
