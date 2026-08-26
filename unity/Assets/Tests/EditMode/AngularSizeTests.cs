using NUnit.Framework;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// 角直径の計算が実際の天文値と一致するかの検算
    /// (docs/01-architecture.md §3-2 の表)。
    /// ここが合っていれば、スケール設計そのものが正しいことの裏付けになる。
    /// </summary>
    public sealed class AngularSizeTests
    {
        static readonly SolarSystemModel Model = SolarSystemModel.CreateOpposition();

        [Test]
        public void 太陽は地球から0533度に見える()
        {
            double d = Model.Sun.DistanceFrom(Model.Earth.AbsolutePosition);
            double deg = AngularSizeSolver.AngularDiameterDegrees(SolarSystemModel.SunRadiusKm, d);

            Debug.Log($"[Step2] 太陽 @地球: 距離 {d:E6} units / 角直径 {deg:F4} deg (実測値 0.53 deg)");
            Assert.That(deg, Is.EqualTo(0.533).Within(0.001));
        }

        [Test]
        public void 太陽は火星から0350度に見える()
        {
            double d = Model.Sun.DistanceFrom(Model.Mars.AbsolutePosition);
            double deg = AngularSizeSolver.AngularDiameterDegrees(SolarSystemModel.SunRadiusKm, d);

            Debug.Log($"[Step2] 太陽 @火星: 距離 {d:E6} units / 角直径 {deg:F4} deg (実測値 0.35 deg)");
            Assert.That(deg, Is.EqualTo(0.350).Within(0.001));
        }

        [Test]
        public void 火星は衝で179秒角に見える()
        {
            double d = Model.Mars.DistanceFrom(Model.Earth.AbsolutePosition);
            double arcsec = AngularSizeSolver.AngularDiameterArcseconds(SolarSystemModel.MarsRadiusKm, d);

            Debug.Log($"[Step2] 火星 @衝: 距離 {d:E6} units / 角直径 {arcsec:F2} arcsec (実測値 18-25 arcsec)");
            Assert.That(d, Is.EqualTo(SolarSystemModel.EarthToMarsKm).Within(1.0));
            Assert.That(arcsec, Is.EqualTo(17.9).Within(0.1));
        }

        [Test]
        public void 火星は地球から01px未満にしかならない()
        {
            double rpp = AngularSizeSolver.RadiansPerPixel(
                UniverseConstants.ReferenceVerticalFovDegrees, UniverseConstants.ReferencePixelHeight);
            double d = Model.Mars.DistanceFrom(Model.Earth.AbsolutePosition);
            double px = AngularSizeSolver.AngularDiameterPixels(SolarSystemModel.MarsRadiusKm, d, rpp);

            Debug.Log($"[Step2] 1 px = {rpp:E4} rad / 火星 @衝 = {px:F4} px");

            // 0.081 px。メッシュで描く意味がないことの数値的裏付け (§3-2)。
            Assert.That(px, Is.LessThan(0.1));
        }

        [Test]
        public void 切替距離が設計書の表と一致する()
        {
            double rpp = AngularSizeSolver.RadiansPerPixel(
                UniverseConstants.ReferenceVerticalFovDegrees, UniverseConstants.ReferencePixelHeight);

            double mars4 = AngularSizeSolver.DistanceForPixels(SolarSystemModel.MarsRadiusKm, 4.0, rpp);
            double mars8 = AngularSizeSolver.DistanceForPixels(SolarSystemModel.MarsRadiusKm, 8.0, rpp);
            double earth4 = AngularSizeSolver.DistanceForPixels(SolarSystemModel.EarthRadiusKm, 4.0, rpp);
            double sun8 = AngularSizeSolver.DistanceForPixels(SolarSystemModel.SunRadiusKm, 8.0, rpp);

            Debug.Log(
                $"[Step2] 切替距離 (units): 火星 4px={mars4:E4} 8px={mars8:E4} / " +
                $"地球 4px={earth4:E4} / 太陽 8px={sun8:E4}");

            Assert.That(mars4, Is.EqualTo(1.585e6).Within(1e4));
            Assert.That(mars8, Is.EqualTo(7.926e5).Within(1e4));
            Assert.That(earth4, Is.EqualTo(2.979e6).Within(1e4));

            // 太陽は地球から見ても 8px を超えるので、常にメッシュ側になる (§3-3)。
            Assert.That(SolarSystemModel.SunToEarthKm, Is.LessThan(sun8));
        }

        [Test]
        public void 描画サイズの予測がスクショの実測と一致する()
        {
            // verify/shots の実測値との照合。ここがずれたら投影の式が壊れている。
            double f = AngularSizeSolver.FocalLengthPixels(
                UniverseConstants.ReferenceVerticalFovDegrees, UniverseConstants.ReferencePixelHeight);

            (double distance, int measured)[] cases =
            {
                (5.0e4, 126), // 03_mars_5e4.png の bbox
                (2.0e4, 322), // 04_mars_2e4.png の bbox
            };

            foreach ((double distance, int measured) in cases)
            {
                // プロキシ殻に載せた後の見かけ (角直径は真の値と一致するので同じ)。
                double shell = DeepProxyProjection.ShellRadius(distance);
                double proxyRadius = SolarSystemModel.MarsRadiusKm * DeepProxyProjection.ScaleFactor(distance);
                double predicted = AngularSizeSolver.ProjectedDiameterPixels(proxyRadius, shell, f);

                Debug.Log($"[Step2] 火星まで {distance:E1} units: 予測 {predicted:F1} px / 実測 {measured} px");
                Assert.That(predicted, Is.EqualTo(measured).Within(2.0));
            }

            Assert.That(f, Is.EqualTo(935.31).Within(0.01));
        }

        [Test]
        public void 切替しきい値の付近では線形換算と厳密投影が一致する()
        {
            // 4〜8 px では両者の差が無視できるので、切替判定は線形換算で足りる。
            double f = AngularSizeSolver.FocalLengthPixels(
                UniverseConstants.ReferenceVerticalFovDegrees, UniverseConstants.ReferencePixelHeight);
            double rpp = AngularSizeSolver.RadiansPerPixel(
                UniverseConstants.ReferenceVerticalFovDegrees, UniverseConstants.ReferencePixelHeight);

            foreach (double px in new[] { 4.0, 8.0 })
            {
                double d = AngularSizeSolver.DistanceForPixels(SolarSystemModel.MarsRadiusKm, px, rpp);
                double linear = AngularSizeSolver.AngularDiameterPixels(SolarSystemModel.MarsRadiusKm, d, rpp);
                double exact = AngularSizeSolver.ProjectedDiameterPixels(SolarSystemModel.MarsRadiusKm, d, f);

                Debug.Log($"[Step2] {px} px 相当 (d={d:E4}): 線形 {linear:F6} px / 厳密 {exact:F6} px / 差 {exact - linear:E2} px");

                // 実測差は 8 px で 1.2e-4 px。切替の可否を変えうる大きさではない。
                Assert.That(exact, Is.EqualTo(linear).Within(1e-3));
            }
        }

        [Test]
        public void 火星の日射は地球の431パーセント()
        {
            double irr = Model.RelativeIrradianceAt(Model.Mars.AbsolutePosition);
            Debug.Log($"[Step2] 相対日射 火星/地球 = {irr:F4}");
            Assert.That(irr, Is.EqualTo(0.431).Within(0.002));
        }

        [Test]
        public void 太陽光の向きは浮動原点のシフトで変わらない()
        {
            var observer = new Vec3d(SolarSystemModel.SunToEarthKm, 0.0, 0.0);
            Vec3d before = Model.SunlightDirectionAt(observer);

            // 再基準化は「両方の絶対位置に同じオフセットを足す」操作。差分は不変。
            var origin = new FloatingOrigin();
            origin.Rebase(new Vec3d(1.2345e8, -6.78e7, 9.9e6));
            Vec3d shiftedObserver = origin.ToOriginRelative(observer);
            Vec3d shiftedSun = origin.ToOriginRelative(Model.Sun.AbsolutePosition);
            Vec3d after = (shiftedObserver - shiftedSun).Normalized;

            double angleError = System.Math.Acos(System.Math.Min(1.0, Vec3d.Dot(before, after)));
            Debug.Log($"[Step2] シフト前後の太陽光方向のずれ = {angleError:E3} rad");

            Assert.That(angleError, Is.LessThan(1e-12));
        }
    }
}
