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
    }
}
