using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Editor;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// **接近円錐と MinStandoff の数表 (Step 13-3b)。**
    ///
    /// 13-1a で置いた `MinStandoff = 半径 + near clip` は
    /// **「構造物が半径 R の球である」という仮定**の式だった（§0 の宿題）。
    /// ここで**実際の頂点を数えて**置き換えたことを縛る。
    ///
    /// **プレハブが取り込まれていなければ Inconclusive。**
    /// clone 直後はアセットが無いのが正常なので Fail にしない。
    /// </summary>
    public sealed class StationApproachTests
    {
        static StationDefinition Cobble() => StationCatalog.Cobble();

        static GameObject RequirePrefab()
        {
            string path = AssetDatabase.GUIDToAssetPath(Cobble().PrefabGuid);
            if (string.IsNullOrEmpty(path))
            {
                Assert.Inconclusive(
                    "まだ取り込まれていない（clone 直後は正常）。"
                    + "run_unity.ps1 -Method SolarSetup.ImportStation で取り込む。");
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Assert.Inconclusive("GUID は解決できたがプレハブとして読めない: " + path);
            }

            return prefab;
        }

        // ---- 式の差し替え（取り込みが無くても通る）----

        [Test]
        public void MinStandoffの式の数表()
        {
            //   定義     HullAheadOfPort   Scale    はみ出し [units]  MinStandoff(0.01)
            //   箱       0.25（＝半径）    1.000    0.25              0.26   ← 移設前と同じ
            //   Cobble   0.0               0.008    0.0               0.01
            //
            // **箱は値が変わらない。** ポートが構造物の中心にあるので、
            // ポートより前方へ半径ぶん出ており、旧式と同じ結果になる。
            const double nearClip = 0.01;

            StationDefinition box = StationCatalog.Box();
            Assert.That(box.HullAheadOfPortLocal, Is.EqualTo(0.25));
            Assert.That(box.HullAheadOfPortUnits, Is.EqualTo(0.25));
            Assert.That(box.MinStandoff(nearClip), Is.EqualTo(0.26).Within(1e-12),
                        "**箱の下限が移設前から動いた**");

            StationDefinition c = Cobble();
            Assert.That(c.HullAheadOfPortLocal, Is.EqualTo(0.0));
            Assert.That(c.HullAheadOfPortUnits, Is.EqualTo(0.0));
            Assert.That(c.MinStandoff(nearClip), Is.EqualTo(0.01).Within(1e-12));

            // **旧式なら 0.37062 units = 370.6 m を要求していた。**
            // 直径 4.19 m の口の 371 m 手前で止まることになる。
            Assert.That(c.RadiusUnits + nearClip, Is.EqualTo(0.37062).Within(1e-5));
            Assert.That(c.MinStandoff(nearClip), Is.LessThan(c.RadiusUnits + nearClip));
        }

        [Test]
        public void PortStandoffは下限を満たす()
        {
            //   下限        0.010 units = 10 m   （はみ出し 0 + near clip 0.01）
            //   余裕        0.005 units =  5 m
            //   PortStandoff 0.015 units = 15 m
            //   ドッキング後の隙間 0.015 units = 15 m
            StationDefinition c = Cobble();

            Assert.That(StationCatalog.MinStandoffUnits, Is.EqualTo(0.01).Within(1e-12));
            Assert.That(StationCatalog.StandoffMarginUnits, Is.EqualTo(0.005));
            Assert.That(c.PortStandoff, Is.EqualTo(0.015).Within(1e-15));

            Assert.That(c.PortStandoff,
                        Is.GreaterThanOrEqualTo(c.MinStandoff(StationCatalog.NearfieldNearClipUnits)),
                        "**下限を割っている**");
            Assert.That(c.DockedClearance,
                        Is.GreaterThanOrEqualTo(StationCatalog.NearfieldNearClipUnits),
                        "**ドッキング後に構造物が near clip の内側に入る**");
        }

        [Test]
        public void 機首とポート面の隙間の数表()
        {
            // 船の目は機首から 4.3191 m 後ろ（13-3a の実測）。
            // 目がポート面から 15 m のとき、機首とポート面の間は 10.68 m。
            //
            //   near clip 10 m のほうが機首の 4.3191 m より遠いので、
            //   **機首とポート面の間には必ず 5.68 m 以上の隙間が残る。**
            //   この構成では船が視覚的にステーションへ接触する絵は出せない。
            const double nose = 4.3191;

            double eyeToPort = Cobble().PortStandoff * 1000.0;
            Assert.That(eyeToPort, Is.EqualTo(15.0).Within(1e-9));
            Assert.That(eyeToPort - nose, Is.EqualTo(10.6809).Within(1e-9));

            double minEyeToPort = StationCatalog.NearfieldNearClipUnits * 1000.0;
            Assert.That(minEyeToPort - nose, Is.EqualTo(5.6809).Within(1e-9));
            Assert.That(minEyeToPort, Is.GreaterThan(nose),
                        "**near clip より機首のほうが遠い = 接触の絵が出せてしまう**");
        }

        // ---- 実際の頂点を数える ----

        [Test]
        public void 接近円錐に構造物が入っていない()
        {
            GameObject prefab = RequirePrefab();
            StationDefinition c = Cobble();

            // 円錐はプレハブ座標で見るので、units の距離を Scale で割る。
            double coneLengthLocal = c.PortStandoff / c.Scale;

            int inside = StationApproachProbe.CountInsideCone(
                prefab, c, StationApproachProbe.DefaultHalfAngleDegrees, coneLengthLocal);

            Assert.That(inside, Is.Zero,
                        "**ポート正面の接近円錐に構造物が入っている。**"
                        + " 半頂角 " + StationApproachProbe.DefaultHalfAngleDegrees + " 度 / 長さ "
                        + (c.PortStandoff * 1000.0) + " m");
        }

        [Test]
        public void 円錐を伸ばしても構造物が入らない()
        {
            // **標準停止距離だけで見ると「たまたま届いていない」場合がある。**
            // 4 倍（60 m）まで伸ばしても 0 件であることを見る。
            GameObject prefab = RequirePrefab();
            StationDefinition c = Cobble();

            double coneLengthLocal = c.PortStandoff * 4.0 / c.Scale;

            int inside = StationApproachProbe.CountInsideCone(
                prefab, c, StationApproachProbe.DefaultHalfAngleDegrees, coneLengthLocal);

            Assert.That(inside, Is.Zero, "4 倍に伸ばすと構造物が入る");
        }

        [Test]
        public void はみ出し0が実際の頂点と合っている()
        {
            // **定義に書いた `HullAheadOfPort = 0` の裏付け。**
            // 半頂角 90 度（前方の半空間ぜんぶ）で、十分長い円錐にしても 0 件なら、
            // ポート面より前方に頂点は 1 つも無い。
            GameObject prefab = RequirePrefab();
            StationDefinition c = Cobble();

            int ahead = StationApproachProbe.CountInsideCone(prefab, c, 89.999, 1.0e6);

            Assert.That(ahead, Is.Zero,
                        "**ポート面より前方に頂点がある。** HullAheadOfPort = 0 が嘘になる");
            Assert.That(c.HullAheadOfPortLocal, Is.EqualTo(0.0));
        }

        // ---- AP の到着半径との関係 (S2 の宿題 / Step 13-3b-2) ----

        [Test]
        public void 要求の判定はポート位置から測る()
        {
            // **13-3b で原点を揃えた。**
            //
            //   前: AP は PortPosition から / 要求は **構造物の中心** から
            //       原点の差 = PortLocalUnits + PortStandoff
            //         箱     0        + 0.3   = 0.3
            //         Cobble 0.19775  + 0.015 = 0.21275
            //       AP 到着時の「中心からの距離」は最大 20.21275 units になりえて、
            //       RequestRange 20 に対して余裕は 0 だった。
            //
            //   後: **どちらも PortPosition から。** 下駄が外れたので
            //       RequestRange は「ポートから何 units で要求できるか」になった。
            SolarSystemModel model = SolarSystemModel.CreateOpposition(Cobble());
            SpaceStation earth = model.Stations[0];

            // ポート位置にいるとき: ポートからは 0、中心からは 0.21275。
            Assert.That(earth.DistanceFromPort(earth.PortPosition),
                        Is.EqualTo(0.0).Within(1e-9));
            Assert.That(earth.DistanceFrom(earth.PortPosition),
                        Is.EqualTo(0.21275).Within(1e-4));

            SolarSystemModel boxModel =
                SolarSystemModel.CreateOpposition(StationCatalog.Box());
            SpaceStation boxEarth = boxModel.Stations[0];

            Assert.That(boxEarth.DistanceFromPort(boxEarth.PortPosition),
                        Is.EqualTo(0.0).Within(1e-9));
            Assert.That(boxEarth.DistanceFrom(boxEarth.PortPosition),
                        Is.EqualTo(0.3).Within(1e-9));
        }

        [Test]
        public void 原点を揃える前と後の数表()
        {
            // **同じ地点でも、原点が違えば距離が違う。**
            // ポート位置から d units 手前（ポート方向側）にいる船で比べる。
            //
            //   定義     d       ポートから   中心から      要求可能 2.0 で
            //   Cobble   1.0     1.0          1.21275       ポート基準なら通る
            //   箱       1.0     1.0          1.3           同上
            //
            // **中心基準のままだと、同じ位置でも 0.21275 / 0.3 の下駄を履く。**
            foreach (StationDefinition def in new[] { Cobble(), StationCatalog.Box() })
            {
                SolarSystemModel model = SolarSystemModel.CreateOpposition(def);
                SpaceStation st = model.Stations[0];

                const double d = 1.0;
                Vec3d p = st.PortPosition + st.PortDirection * d;

                Assert.That(st.DistanceFromPort(p), Is.EqualTo(d).Within(1e-9), def.Id);

                // **スカラーの和は近似。** ポートのオフセットにはポート方向と
                // 直交する成分がある（Cobble で X 0.00024 / Y 0.00192 units）ので、
                // 中心からの距離はベクトル和になり、和より **8e-6 units** 小さい。
                double offset = def.PortLocalUnits.Magnitude + def.PortStandoff;
                Assert.That(st.DistanceFrom(p), Is.EqualTo(d + offset).Within(1e-4), def.Id);

                // **ポート基準なら 2.0 の圏内。**
                Assert.That(st.DistanceFromPort(p),
                            Is.LessThanOrEqualTo(st.RequestRangeUnits), def.Id);
            }
        }

        [Test]
        public void 要求可能距離は実行時に振れる()
        {
            // **F4 で振る。** 判定も HUD も `EffectiveRequestRange` を読む。
            StationDefinition c = Cobble();
            Assert.That(c.EffectiveRequestRange, Is.EqualTo(2.0));

            c.SetRuntimeRequestRange(5.0);
            Assert.That(c.EffectiveRequestRange, Is.EqualTo(5.0));
            Assert.That(c.RequestRange, Is.EqualTo(2.0), "定義の値は動かない");

            Assert.That(DockingSolver.UndockDistance(c.EffectiveRequestRange),
                        Is.EqualTo(6.0).Within(1e-12), "出港距離も一緒に動く");

            c.ResetRuntimeRequestRange();
            Assert.That(c.EffectiveRequestRange, Is.EqualTo(2.0));

            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => c.SetRuntimeRequestRange(0.0));
        }
        [Test]
        public void RequestRangeの候補の数表()
        {
            // **選んでいない。人間が指定する。** 数だけ残す。
            //   候補   [units]  実寸    半径 0.36062 の  全長 0.50033 の  出港 (x1.2)

            //   (a)     20.0    20 km   55.5 倍          40.0 倍          24.0  （原点を揃える前の値）
            //   (b)      2.0     2 km    5.5 倍           4.0 倍           2.4
            //   (c)      1.0     1 km    2.8 倍           2.0 倍           1.2
            //   (d)      0.5   500 m     1.4 倍           1.0 倍           0.6
            StationDefinition c = Cobble();

            Assert.That(c.RadiusUnits, Is.EqualTo(0.36062).Within(1e-5));
            Assert.That(c.RequestRange, Is.EqualTo(2.0), "既定は (b)");

            double length = 62.5408 * c.Scale;
            Assert.That(length, Is.EqualTo(0.50033).Within(1e-5));

            Assert.That(20.0 / c.RadiusUnits, Is.EqualTo(55.5).Within(0.1));
            Assert.That(20.0 / length, Is.EqualTo(40.0).Within(0.1));
            Assert.That(2.0 / length, Is.EqualTo(4.0).Within(0.01));
            Assert.That(1.0 / length, Is.EqualTo(2.0).Within(0.01));
            Assert.That(0.5 / length, Is.EqualTo(1.0).Within(0.01));

            Assert.That(DockingSolver.UndockDistance(20.0), Is.EqualTo(24.0).Within(1e-12));
            Assert.That(DockingSolver.UndockDistance(1.0), Is.EqualTo(1.2).Within(1e-12));
        }
    }
}
