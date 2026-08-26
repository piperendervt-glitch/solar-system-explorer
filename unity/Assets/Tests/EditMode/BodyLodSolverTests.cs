using NUnit.Framework;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// 光点⇔メッシュ切替 (docs/01-architecture.md §3-3 / 決定 D-7)。
    /// </summary>
    public sealed class BodyLodSolverTests
    {
        [Test]
        public void 遠方では光点だけメッシュは出ない()
        {
            var lod = new BodyLodSolver();
            lod.Update(0.081); // 火星 @衝

            Assert.That(lod.PointActive, Is.True);
            Assert.That(lod.MeshActive, Is.False);
            Assert.That(lod.Blend, Is.EqualTo(0.0));
        }

        [Test]
        public void 角直径8pxを超えたらメッシュだけになる()
        {
            var lod = new BodyLodSolver();
            lod.Update(126.6); // 火星まで 5e4 units

            Assert.That(lod.MeshActive, Is.True);
            Assert.That(lod.PointActive, Is.False);
            Assert.That(lod.Blend, Is.EqualTo(1.0));
        }

        [Test]
        public void 帯の中ではブレンド率が線形に上がる()
        {
            var lod = new BodyLodSolver();

            lod.Update(4.0);
            Assert.That(lod.Blend, Is.EqualTo(0.0).Within(1e-12));

            lod.Update(6.0);
            Assert.That(lod.Blend, Is.EqualTo(0.5).Within(1e-12));

            lod.Update(8.0);
            Assert.That(lod.Blend, Is.EqualTo(1.0).Within(1e-12));
        }

        [Test]
        public void ブレンド率は経路に依存しない()
        {
            // 上がってきた場合と下がってきた場合で同じ px なら同じ値。
            // ここが状態に依存すると往復で見た目が飛ぶ。
            var up = new BodyLodSolver();
            for (double px = 0.1; px <= 12.0; px += 0.1)
            {
                up.Update(px);
            }

            up.Update(6.0);
            double fromAbove = up.Blend;

            var down = new BodyLodSolver();
            for (double px = 12.0; px >= 0.1; px -= 0.1)
            {
                down.Update(px);
            }

            down.Update(6.0);
            double fromBelow = down.Blend;

            Assert.That(fromAbove, Is.EqualTo(fromBelow).Within(1e-12));
            Assert.That(fromAbove, Is.EqualTo(0.5).Within(1e-12));
        }

        [Test]
        public void 境界4px付近を往復してもレンダラがちらつかない()
        {
            var lod = new BodyLodSolver();

            // まずメッシュを有効にする
            lod.Update(4.1);
            Assert.That(lod.MeshActive, Is.True);

            int togglesBefore = lod.ToggleCount;

            // 4 px をまたいで 200 回往復させる (3.9 <-> 4.1)。
            // ヒステリシス下限は 3.8 px なので、メッシュは有効のまま動かないはず。
            for (int i = 0; i < 200; i++)
            {
                lod.Update(i % 2 == 0 ? 3.9 : 4.1);
            }

            int toggles = lod.ToggleCount - togglesBefore;
            Debug.Log($"[Step2] 4px 付近を 200 回往復 -> レンダラの ON/OFF 切替 {toggles} 回");

            Assert.That(toggles, Is.EqualTo(0), "ヒステリシスが効いていればここは 0 回");
            Assert.That(lod.MeshActive, Is.True);
            Assert.That(lod.Blend, Is.LessThan(0.03), "この帯ではメッシュの alpha はほぼ 0 なので見た目は変わらない");
        }

        [Test]
        public void 境界8px付近を往復してもレンダラがちらつかない()
        {
            var lod = new BodyLodSolver();

            lod.Update(8.1); // 光点を消す
            Assert.That(lod.PointActive, Is.False);

            int togglesBefore = lod.ToggleCount;

            // ヒステリシス下限は 7.6 px。7.9 <-> 8.1 の往復では動かないはず。
            for (int i = 0; i < 200; i++)
            {
                lod.Update(i % 2 == 0 ? 7.9 : 8.1);
            }

            int toggles = lod.ToggleCount - togglesBefore;
            Debug.Log($"[Step2] 8px 付近を 200 回往復 -> レンダラの ON/OFF 切替 {toggles} 回");

            Assert.That(toggles, Is.EqualTo(0));
            Assert.That(lod.PointActive, Is.False);
            Assert.That(lod.Blend, Is.GreaterThan(0.97), "この帯では光点の alpha はほぼ 0");
        }

        [Test]
        public void ヒステリシスなしなら往復でちらつくことを確認する()
        {
            // 対照実験。ヒステリシス幅ぶん外側で往復させれば当然切り替わる。
            var lod = new BodyLodSolver();
            lod.Update(4.1);

            int togglesBefore = lod.ToggleCount;
            for (int i = 0; i < 10; i++)
            {
                lod.Update(i % 2 == 0 ? 3.5 : 4.1); // 3.5 は下限 3.8 を下回る
            }

            int toggles = lod.ToggleCount - togglesBefore;
            Debug.Log($"[Step2] 対照: 3.5 <-> 4.1 を 10 回往復 -> 切替 {toggles} 回 (ヒステリシス幅の外なので切り替わって正しい)");

            Assert.That(toggles, Is.GreaterThan(0), "ヒステリシス幅を超えたら切り替わるのが正しい");
        }

        [Test]
        public void 全行程を通しても切替は片道1回ずつ()
        {
            // 火星へ 7.8e7 -> 2e4 units まで滑らかに近づく想定。
            var lod = new BodyLodSolver();
            double rpp = AngularSizeSolver.RadiansPerPixel(
                UniverseConstants.ReferenceVerticalFovDegrees, UniverseConstants.ReferencePixelHeight);

            const int steps = 20000;
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                // 対数的に近づく
                double d = System.Math.Pow(10.0, 7.892 + t * (System.Math.Log10(2.0e4) - 7.892));
                lod.Update(AngularSizeSolver.AngularDiameterPixels(SolarSystemModel.MarsRadiusKm, d, rpp));
            }

            Debug.Log($"[Step2] 7.8e7 -> 2e4 units の全行程 {steps} ステップ -> 切替 {lod.ToggleCount} 回");

            // メッシュ ON と 光点 OFF の 2 回だけ。
            Assert.That(lod.ToggleCount, Is.EqualTo(2));
            Assert.That(lod.MeshActive, Is.True);
            Assert.That(lod.PointActive, Is.False);
        }
    }
}
