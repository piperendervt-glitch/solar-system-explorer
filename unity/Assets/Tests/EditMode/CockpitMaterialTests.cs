using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using SolarSystem.Editor;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// URP 変換 (Step 11-1b) とマテリアルの棚卸し (Step 11-1c)。
    ///
    /// **期待値は棚卸しの実測から書いている**（logs/unity_20260827_225200.log）。
    /// 先に数字を決めて書くと、観測していない値を実測として置くことになるため。
    /// </summary>
    public sealed class CockpitMaterialTests
    {
        static bool Imported => AssetDatabase.IsValidFolder(CockpitPackage.DestinationRoot);

        static void RequireImported()
        {
            if (!Imported)
            {
                Assert.Inconclusive(
                    "まだ取り込まれていない（clone 直後は正常）。"
                    + "run_unity.ps1 -Method SolarSetup.ImportCockpit で取り込む。");
            }
        }

        // ---- 11-1b 変換 ----

        [Test]
        public void ThirdParty配下の全マテリアルがURPシェーダ()
        {
            RequireImported();

            string[] notUrp = CockpitInventory.Collect()
                .Where(e => !e.ShaderName.StartsWith(UrpConversion.UrpShaderPrefix,
                                                     StringComparison.Ordinal))
                .Select(e => $"{e.Name}: {e.ShaderName}")
                .ToArray();

            Assert.That(notUrp, Is.Empty,
                        "**Built-in のシェーダが残っている**（URP では桃色になる）:\n  "
                        + string.Join("\n  ", notUrp));
        }

        [Test]
        public void 変換の対象がThirdPartyに限られている()
        {
            // **一括変換 API はパスで絞れずプロジェクト全体に掛かる。**
            // `Main.unity` を毎回生成するこのプロジェクトでは、外を触られないことが要る。
            Assert.That(UrpConversion.AllowedRoot, Is.EqualTo("Assets/ThirdParty/"));

            string[] outside = UrpConversion.MaterialsInScope()
                .Where(p => !p.StartsWith(UrpConversion.AllowedRoot, StringComparison.Ordinal))
                .ToArray();

            Assert.That(outside, Is.Empty, "対象が ThirdParty の外へ広がっている:\n  "
                                           + string.Join("\n  ", outside));
        }

        [Test]
        public void 変換器が実際に効く()
        {
            // **陽性対照。**
            // Hi-Rez は既に URP なので変換は 0 件で終わる。それだけでは「呼んだ」と
            // 「効いた」の区別がつかない（bloom が Step 6 から 9 まで一度も効いて
            // いなかったのと同じ形）。Standard のマテリアルを 1 枚わざと作って、
            // URP/Lit に変わることと `_MainTex` -> `_BaseMap` が引き継がれることを見る。
            //
            // **常設にしてよい理由は速さ。** 変換そのものは実測 0.09 秒
            // （マテリアル 23 枚 / 変換表 63 件）。触るのは追跡除外の一時フォルダだけで、
            // 最後に消す。
            var watch = Stopwatch.StartNew();
            UrpConversion.RunPositiveControl();
            watch.Stop();

            Assert.That(AssetDatabase.IsValidFolder(UrpConversion.SelfTestFolder), Is.False,
                        "陽性対照の一時フォルダが残っている: " + UrpConversion.SelfTestFolder);

            Debug.Log($"  [Step11-1b] 陽性対照: {watch.Elapsed.TotalSeconds:F2} 秒");
        }

        // ---- 11-1c 棚卸し ----

        [Test]
        public void ガラスが透明で描かれる()
        {
            RequireImported();

            CockpitInventory.Entry[] glass = CockpitInventory.Collect()
                .Where(e => e.Name.Contains("Glass"))
                .ToArray();

            // 実測: Cockpit3Grey_Glass / Cockpit3Red_Glass の 2 枚。
            // **どちらも取り込んだ時点で Transparent だった**（張り替え不要）。
            Assert.That(glass.Length, Is.EqualTo(2), "ガラスの枚数が変わっている");

            foreach (CockpitInventory.Entry e in glass)
            {
                Assert.That(e.IsTransparent, Is.True,
                            $"**{e.Name} が不透明。窓の外が見えない**");
                Assert.That(e.RenderQueue, Is.GreaterThanOrEqualTo(3000),
                            $"{e.Name} の描画順が不透明側にある");
            }
        }

        [Test]
        public void 画面マテリアルが内壁と共有されていない()
        {
            RequireImported();

            // **名前で辞書にしない。** Engine1 / MainBody13 などは Grey と Red で
            // 同じ名前のマテリアルが 2 枚ある（キーが衝突する）。
            var all = CockpitInventory.Collect();

            // 実測: 画面は 4 枚のメッシュ（Gauge_01 / Gauge_02 / Screen_01 / Screen_04）が
            // **1 枚のマテリアルを共有**している。内壁（CockpitsEquipments、レンダラー 46）
            // とは別なので、**RT が内壁に映る心配は無い。**
            // ただし 4 枚が同じマテリアルなので、**役割ごとに別の RT を割り当てるには
            // 11-3 で複製して独立させる必要がある。**
            CockpitInventory.Entry screens = all.Single(e => e.Name == "CockpitEquipments_Screens");
            Assert.That(screens.RendererCount, Is.EqualTo(4));
            Assert.That(screens.MeshCount, Is.EqualTo(4));

            foreach (string renderer in screens.Renderers)
            {
                Assert.That(renderer.Contains("Screen") || renderer.Contains("Gauge"), Is.True,
                            "画面以外のメッシュが画面マテリアルを使っている: " + renderer);
            }

            // 実測: HUD のガラス面 1 枚だけ。こちらは Transparent。
            CockpitInventory.Entry target = all.Single(e => e.Name == "CockpitEquipments_TargetScreens");
            Assert.That(target.RendererCount, Is.EqualTo(1));
            Assert.That(target.Renderers[0], Does.Contain("HUD"));
            Assert.That(target.IsTransparent, Is.True, "HUD 面が不透明になっている");
        }

        [Test]
        public void 発光しているマテリアルが実測と一致する()
        {
            RequireImported();

            string[] emissive = CockpitInventory.Collect()
                .Where(e => e.IsEmissive)
                .Select(e => e.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            // 棚卸しの結果。**アセットが変わったら気づけるように縛る。**
            // 11-4 で確かめたところ、シーンに置いているのは Cockpit3(interior) だけで、
            // Cockpit3Red と Thrusters1 はレンダラー 0 件。Cockpit3Grey は見えるが
            // **発光の強さを振っても画素が変わらなかった**ので、補助光だけにした。
            Assert.That(emissive, Is.EqualTo(new[]
            {
                "Cockpit3Grey",
                "Cockpit3Red",
                "CockpitEquipments_Screens",
                "Thrusters1",
            }));
        }

        [Test]
        public void 棚卸しがレンダラーとメッシュを別々に数える()
        {
            RequireImported();

            // **同じメッシュが複数のレンダラーに載ることがある。**
            // 内壁は実測でレンダラー 46 / メッシュ 20。共有の判定には
            // レンダラー数だけでは足りないので、両方を数えている。
            CockpitInventory.Entry interior = CockpitInventory.Collect()
                .Single(e => e.Name == "CockpitsEquipments");

            Assert.That(interior.RendererCount, Is.EqualTo(46));
            Assert.That(interior.MeshCount, Is.EqualTo(20));
            Assert.That(interior.RendererCount, Is.GreaterThan(interior.MeshCount));
        }
    }
}
