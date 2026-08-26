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
        // ---- 整列の表示と拒否理由 ----

        [Test]
        public void 許容角以内ならALIGNEDが付く()
        {
            Assert.That(InstrumentPanel.FormatAlignment(0.0), Does.Contain("ALIGNED"));
            Assert.That(InstrumentPanel.FormatAlignment(29.9), Does.Contain("ALIGNED"));
            Assert.That(InstrumentPanel.FormatAlignment(30.0), Does.Contain("ALIGNED"));
            Assert.That(InstrumentPanel.FormatAlignment(30.1), Does.Not.Contain("ALIGNED"));

            Debug.Log($"[Step6] 整列表示: 0 度 -> '{InstrumentPanel.FormatAlignment(0.0)}' / " +
                      $"45 度 -> '{InstrumentPanel.FormatAlignment(45.0)}'");
        }

        [Test]
        public void 要求が通らない理由は距離速度角度の順に出る()
        {
            // 全部だめなら距離を先に言う。遠ければ速度も角度も意味がないため。
            string far = DockingSolver.RejectionReason(100.0, 50.0, 90.0);
            string fast = DockingSolver.RejectionReason(10.0, 50.0, 90.0);
            string angle = DockingSolver.RejectionReason(10.0, 0.5, 90.0);
            string ok = DockingSolver.RejectionReason(10.0, 0.5, 10.0);

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
            d.Step(10.0, 0.5, 0.0, false, false, 1.0 / 60.0); // Free -> Approaching
            d.Step(10.0, 0.5, 0.0, true, false, 1.0 / 60.0);  // 要求が通る

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

        // ---- エンジン音 ----

        [Test]
        public void エンジン音のループが継ぎ目なく作れる()
        {
            AudioClip clip = EngineAudio.CreateRumbleClip();
            try
            {
                Assert.That(clip.frequency, Is.EqualTo(EngineAudio.SampleRate));
                Assert.That(clip.channels, Is.EqualTo(1));
                Assert.That(clip.samples, Is.EqualTo(EngineAudio.SampleRate * EngineAudio.LoopSeconds));

                var samples = new float[clip.samples];
                clip.GetData(samples, 0);

                float peak = 0f;
                double sum = 0.0;
                for (int i = 0; i < samples.Length; i++)
                {
                    peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
                    sum += samples[i] * samples[i];
                }

                double rms = System.Math.Sqrt(sum / samples.Length);

                // ループの継ぎ目: 末尾と先頭の差が、隣接サンプルの平均差と同程度なら滑らか。
                double seam = System.Math.Abs(samples[0] - samples[samples.Length - 1]);
                double meanStep = 0.0;
                for (int i = 1; i < 1000; i++)
                {
                    meanStep += System.Math.Abs(samples[i] - samples[i - 1]);
                }

                meanStep /= 999.0;

                Debug.Log($"[Step6] エンジン音: {clip.length:F1} 秒 / ピーク {peak:F3} / RMS {rms:F3} / " +
                          $"継ぎ目の段差 {seam:E3} (隣接サンプルの平均 {meanStep:E3})");

                Assert.That(peak, Is.EqualTo(0.9f).Within(0.01f), "正規化されている");
                Assert.That(rms, Is.GreaterThan(0.05), "無音ではない");
                Assert.That(seam, Is.LessThan(meanStep * 20.0), "継ぎ目でパチっと鳴る段差がある");
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void エンジン音はスラストで音量と音色が変わる()
        {
            var go = new GameObject("audio");
            try
            {
                var audio = go.AddComponent<EngineAudio>();

                audio.Apply(0f);
                float idleVolume = audio.LastVolume;
                float idleCutoff = audio.LastCutoff;

                audio.Apply(1f);
                float fullVolume = audio.LastVolume;
                float fullCutoff = audio.LastCutoff;

                Debug.Log($"[Step6] スラスト 0 -> 音量 {idleVolume:F3} / カットオフ {idleCutoff:F0} Hz\n" +
                          $"[Step6] スラスト 1 -> 音量 {fullVolume:F3} / カットオフ {fullCutoff:F0} Hz");

                Assert.That(fullVolume, Is.GreaterThan(idleVolume));
                Assert.That(fullCutoff, Is.GreaterThan(idleCutoff));

                // 逆スラストでも同じだけ鳴る (絶対値で見る)。
                audio.Apply(-1f);
                Assert.That(audio.LastVolume, Is.EqualTo(fullVolume).Within(1e-6f));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
