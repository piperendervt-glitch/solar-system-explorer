using NUnit.Framework;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// **Core の基底が Unity の `Quaternion.LookRotation` と一致すること (Step 13-3 コミット2)。**
    ///
    /// ■ なぜこれを縛るか
    /// ポート位置（判定）は `StationBasis` で、姿勢（描画）は Unity の Quaternion で作る。
    /// **2 つが食い違うと、絵と数字が別のものを見る。**
    /// Demo 3 と Step 12 で繰り返し踏んだ型なので、突き合わせを自動で見る。
    ///
    /// ■ 縮退のケースを含める
    /// このプロジェクトのステーションは**ワールド +Y に置かれている**
    /// （`SolarSystemModel` の `offset`）。`LookRotation` の既定の up も +Y なので、
    /// **本番の向きがちょうど縮退のケース。** Unity がそこで何を返すかは
    /// 推測せず、ここで実測して合わせている。
    /// </summary>
    public sealed class StationBasisTests
    {
        static readonly Vec3d[] Directions =
        {
            new Vec3d(0.0, 1.0, 0.0),    // **本番の向き（縮退）**
            new Vec3d(0.0, -1.0, 0.0),   // 反対側（縮退）
            new Vec3d(0.0, 0.0, 1.0),
            new Vec3d(1.0, 0.0, 0.0),
            new Vec3d(0.0, 0.0, -1.0),
            new Vec3d(-1.0, 0.0, 0.0),
            new Vec3d(0.3, 0.5, -0.8),
            new Vec3d(-0.7, 0.1, 0.2),
        };

        [Test]
        public void 基底がUnityのLookRotationと一致する()
        {
            foreach (Vec3d d in Directions)
            {
                StationBasis basis = StationBasis.FromPortDirection(d);
                Quaternion q = Quaternion.LookRotation(ToUnity(d));

                AssertClose(q * Vector3.right, basis.Right, d, "right");
                AssertClose(q * Vector3.up, basis.Up, d, "up");
                AssertClose(q * Vector3.forward, basis.Forward, d, "forward");
            }
        }

        [Test]
        public void 本番の向きでの基底の数表()
        {
            // **ポート方向 +Y（縮退のケース）。**
            //   ローカル   ワールド
            //   +X         (1, 0, 0)
            //   +Y         (0, 0, -1)
            //   +Z         (0, 1, 0)   = ポート方向
            StationBasis b = StationBasis.FromPortDirection(new Vec3d(0.0, 1.0, 0.0));

            AssertVec(b.Right, 1.0, 0.0, 0.0);
            AssertVec(b.Up, 0.0, 0.0, -1.0);
            AssertVec(b.Forward, 0.0, 1.0, 0.0);
        }

        [Test]
        public void ローカルZがポート方向へ写る()
        {
            // **`StationBasis` の規約。** 箱のポート（`SceneBuilder` の円柱）も
            // ローカル +Z にある。
            foreach (Vec3d d in Directions)
            {
                StationBasis b = StationBasis.FromPortDirection(d);
                Vec3d mapped = b.ToWorld(new Vec3d(0.0, 0.0, 1.0));
                Vec3d expected = d.Normalized;

                Assert.That(Vec3d.Distance(mapped, expected), Is.LessThan(1e-9),
                            "ローカル +Z がポート方向へ写っていない: " + d);
            }
        }

        [Test]
        public void 基底は正規直交()
        {
            foreach (Vec3d d in Directions)
            {
                StationBasis b = StationBasis.FromPortDirection(d);

                Assert.That(b.Right.Magnitude, Is.EqualTo(1.0).Within(1e-9), "right の長さ");
                Assert.That(b.Up.Magnitude, Is.EqualTo(1.0).Within(1e-9), "up の長さ");
                Assert.That(b.Forward.Magnitude, Is.EqualTo(1.0).Within(1e-9), "forward の長さ");

                Assert.That(Vec3d.Dot(b.Right, b.Up), Is.EqualTo(0.0).Within(1e-9));
                Assert.That(Vec3d.Dot(b.Up, b.Forward), Is.EqualTo(0.0).Within(1e-9));
                Assert.That(Vec3d.Dot(b.Forward, b.Right), Is.EqualTo(0.0).Within(1e-9));
            }
        }

        static Vector3 ToUnity(Vec3d v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);

        static void AssertClose(Vector3 unity, Vec3d core, Vec3d direction, string label)
        {
            // float の Quaternion と double の基底を比べるので、許容は float の精度。
            Assert.That(Mathf.Abs(unity.x - (float)core.X), Is.LessThan(1e-5f),
                        label + ".x が違う (" + direction + ")");
            Assert.That(Mathf.Abs(unity.y - (float)core.Y), Is.LessThan(1e-5f),
                        label + ".y が違う (" + direction + ")");
            Assert.That(Mathf.Abs(unity.z - (float)core.Z), Is.LessThan(1e-5f),
                        label + ".z が違う (" + direction + ")");
        }

        static void AssertVec(Vec3d v, double x, double y, double z)
        {
            Assert.That(v.X, Is.EqualTo(x).Within(1e-12));
            Assert.That(v.Y, Is.EqualTo(y).Within(1e-12));
            Assert.That(v.Z, Is.EqualTo(z).Within(1e-12));
        }
    }
}
