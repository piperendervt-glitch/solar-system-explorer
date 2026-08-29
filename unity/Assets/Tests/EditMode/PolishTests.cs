using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>見た目の仕上げ (Step 6)。</summary>
    public sealed class PolishTests
    {
        /// <summary>箱の定義の要求可能距離 (Step 13-1a)。**グローバル定数ではなく定義から読む。**</summary>
        static readonly double BoxRange = StationCatalog.Box().RequestRange;

        // ---- 整列の表示と拒否理由 ----

        [Test]
        public void 許容角以内ならALIGNEDが付く()
        {
            // **11-3b で "ALIGNED" の文字は外した。** 許容内かどうかは色で出ており、
            // 文字と重複していた。外したぶん字を大きくできる（HUD で実測 2.04 倍）。
            // 判定そのものは AlignmentInTolerance に残っている。
            Assert.That(InstrumentPanel.FormatAlignment(0.0), Does.Not.Contain("ALIGNED"));
            Assert.That(InstrumentPanel.FormatAlignment(30.1), Does.Not.Contain("ALIGNED"));
            Assert.That(InstrumentPanel.FormatAlignment(12.3), Is.EqualTo("12.3 deg"));

            Debug.Log($"[Step6] 整列表示: 0 度 -> '{InstrumentPanel.FormatAlignment(0.0)}' / " +
                      $"45 度 -> '{InstrumentPanel.FormatAlignment(45.0)}'");
        }

        [Test]
        public void 要求が通らない理由は距離速度角度の順に出る()
        {
            // 全部だめなら距離を先に言う。遠ければ速度も角度も意味がないため。
            string far = DockingSolver.RejectionReason(BoxRange * 5.0, 50.0, 90.0, BoxRange);
            string fast = DockingSolver.RejectionReason(BoxRange * 0.5, 50.0, 90.0, BoxRange);
            string angle = DockingSolver.RejectionReason(BoxRange * 0.5, 0.5, 90.0, BoxRange);
            string ok = DockingSolver.RejectionReason(BoxRange * 0.5, 0.5, 10.0, BoxRange);

            Debug.Log($"[Step6] 拒否理由:\n  遠い: {far}\n  速い: {fast}\n  角度: {angle}\n  OK: '{ok}'");

            Assert.That(far, Does.Contain("距離"));
            Assert.That(fast, Does.Contain("速度"));
            Assert.That(angle, Does.Contain("ポート正面"));
            Assert.That(ok, Is.Empty, "全部満たしていれば理由なし");
        }

        [Test]
        public void 要求が通ったときは理由が残らない()
        {
            var d = new DockingSolver();
            d.Step(BoxRange * 0.5, 0.5, 0.0, false, false, 1.0 / 60.0, BoxRange); // Free -> Approaching
            d.Step(BoxRange * 0.5, 0.5, 0.0, true, false, 1.0 / 60.0, BoxRange);  // 要求が通る

            Assert.That(d.State, Is.EqualTo(DockingState.DockRequested));
            Assert.That(d.LastRejection, Is.Empty);
        }

        // ---- アセット ----

        [Test]
        public void テクスチャが4件とも取り込まれている()
        {
            Assert.That(SolarSystem.Editor.TextureSetup.IsImported, Is.True,
                "SolarSetup.ImportTextures を先に実行すること");

            foreach (string file in new[]
                     {
                         SolarSystem.Editor.TextureSetup.StarMap, SolarSystem.Editor.TextureSetup.EarthAlbedo,
                         SolarSystem.Editor.TextureSetup.MarsAlbedo, SolarSystem.Editor.TextureSetup.SunAlbedo,
                     })
            {
                string path = SolarSystem.Editor.TextureSetup.AssetPath(file);
                var texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
                Assert.That(texture, Is.Not.Null, $"{path} が読めない");
                Debug.Log($"[Step6] {file}: {texture.width} x {texture.height} ({texture.GetType().Name})");
            }
        }

        [Test]
        public void 星図はCubemapとして読める()
        {
            var cubemap = AssetDatabase.LoadAssetAtPath<Cubemap>(
                SolarSystem.Editor.TextureSetup.AssetPath(SolarSystem.Editor.TextureSetup.StarMap));
            Assert.That(cubemap, Is.Not.Null, "Cubemap に設定されていない");
            Debug.Log($"[Step6] 星図 Cubemap: {cubemap.width} px/面");
        }

        [Test]
        public void 惑星のマテリアルにテクスチャが割り当たっている()
        {
            SolarSystemModel model = SolarSystemModel.CreateOpposition();
            foreach (CelestialBody body in model.Bodies)
            {
                Texture2D albedo = SolarSystem.Editor.MaterialLibrary.AlbedoFor(body);
                Assert.That(albedo, Is.Not.Null, $"{body.Name} のテクスチャが無い");
                Debug.Log($"[Step6] {body.Name} -> {albedo.name} ({albedo.width} x {albedo.height})");
            }
        }

        // ---- ポストプロセスの 3 段階 ----

        [Test]
        public void ポストプロセスの3段階は強度が単調に上がる()
        {
            var subtle = PostProcessPreset.Values(PostProcessStrength.Subtle);
            var medium = PostProcessPreset.Values(PostProcessStrength.Medium);
            var strong = PostProcessPreset.Values(PostProcessStrength.Strong);

            Debug.Log($"[Step6] Bloom 強度 {subtle.bloomIntensity} / {medium.bloomIntensity} / {strong.bloomIntensity}\n" +
                      $"[Step6] Bloom しきい値 {subtle.bloomThreshold} / {medium.bloomThreshold} / {strong.bloomThreshold}\n" +
                      $"[Step6] Vignette {subtle.vignette} / {medium.vignette} / {strong.vignette}");

            Assert.That(medium.bloomIntensity, Is.GreaterThan(subtle.bloomIntensity));
            Assert.That(strong.bloomIntensity, Is.GreaterThan(medium.bloomIntensity));

            // しきい値は下がるほど光る範囲が広い。
            Assert.That(medium.bloomThreshold, Is.LessThan(subtle.bloomThreshold));
            Assert.That(strong.bloomThreshold, Is.LessThan(medium.bloomThreshold));

            Assert.That(medium.vignette, Is.GreaterThan(subtle.vignette));
            Assert.That(strong.vignette, Is.GreaterThan(medium.vignette));
        }

    }
}
