using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Editor;
using UnityEditor;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>素材の取り込みとインポート設定 (Step 10-1)。</summary>
    public sealed class AudioImportTests
    {
        static AudioClip Load(AudioAsset asset)
            => AssetDatabase.LoadAssetAtPath<AudioClip>(AudioImportSetup.AssetPath(asset.Output));

        [Test]
        public void 採用素材は7件()
        {
            Assert.That(AudioImportSetup.Assets.Length, Is.EqualTo(7));
            Assert.That(AudioImportSetup.Assets.Count(a => a.Use == AudioUse.Loop), Is.EqualTo(2));
            Assert.That(AudioImportSetup.Assets.Count(a => a.Use == AudioUse.OneShot), Is.EqualTo(5));
        }

        [Test]
        public void 全ての素材が取り込まれている()
        {
            foreach (AudioAsset asset in AudioImportSetup.Assets)
            {
                Assert.That(File.Exists(AudioImportSetup.AssetPath(asset.Output)), Is.True,
                            asset.Output + " が Assets/Audio に無い");
                Assert.That(Load(asset), Is.Not.Null, asset.Output + " が AudioClip として読めない");
            }
        }

        [Test]
        public void インポート設定が用途どおり()
        {
            foreach (AudioAsset asset in AudioImportSetup.Assets)
            {
                string path = AudioImportSetup.AssetPath(asset.Output);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                Assert.That(importer, Is.Not.Null, path);

                AudioImporterSampleSettings s = importer.defaultSampleSettings;

                if (asset.Use == AudioUse.Loop)
                {
                    Assert.That(s.loadType, Is.EqualTo(AudioClipLoadType.CompressedInMemory),
                                asset.Output + " は常時鳴るので常駐させる");
                    Assert.That(s.compressionFormat, Is.EqualTo(AudioCompressionFormat.Vorbis),
                                asset.Output + " は長さがあるので圧縮する");
                }
                else
                {
                    Assert.That(s.loadType, Is.EqualTo(AudioClipLoadType.DecompressOnLoad),
                                asset.Output + " は単発なので発音遅延を作らない");
                    Assert.That(s.compressionFormat, Is.EqualTo(AudioCompressionFormat.PCM),
                                asset.Output + " は短いので PCM");
                }

                Assert.That(s.preloadAudioData, Is.True, asset.Output + " の preload が OFF");
                Assert.That(importer.forceToMono, Is.EqualTo(asset.ForceMono), asset.Output);
            }
        }

        [Test]
        public void ループは1chになっている()
        {
            foreach (AudioAsset asset in AudioImportSetup.Assets.Where(a => a.Use == AudioUse.Loop))
            {
                Assert.That(Load(asset).channels, Is.EqualTo(1), asset.Output);
            }
        }

        // ---- 加工結果 ----

        [Test]
        public void エンジンのループは5秒より少し短い()
        {
            AudioClip clip = Load(AudioImportSetup.Assets[0]);
            // 5.000 秒からクロスフェード分だけ縮む。
            double expected = 5.0 - AudioLoopBuilder.EngineCrossfadeSeconds;
            Assert.That(clip.length, Is.EqualTo(expected).Within(0.02));
        }

        [Test]
        public void コックピットのループは8秒()
        {
            AudioClip clip = Load(AudioImportSetup.Assets[1]);
            Assert.That(clip.length, Is.EqualTo(AudioLoopBuilder.CockpitLengthSeconds).Within(0.02),
                        "0.954 秒のままでは 1 秒周期の反復に気付く");
        }

        [Test]
        public void 加工後のループ端の段差比が閾値以下()
        {
            foreach (AudioAsset asset in AudioImportSetup.Assets.Where(a => a.Use == AudioUse.Loop))
            {
                // **本番は Compressed In Memory なので AudioClip.GetData は通らない。**
                // コミットされる .wav そのものを読む。
                float[] samples = AudioLoopBuilder.DecodeWav16(
                    File.ReadAllBytes(AudioImportSetup.AssetPath(asset.Output)),
                    out int rate, out int _);

                double ratio = AudioAnalysis.SeamRatio(samples);
                Debug.Log($"  [Step10-1] {asset.Output} 段差比 {ratio:F3} " +
                          $"/ ピーク {AudioAnalysis.Peak(samples):F4} " +
                          $"/ 長さ {samples.Length / (double)rate:F3} s");

                Assert.That(ratio, Is.LessThanOrEqualTo(AudioLoopBuilder.SeamRatioLimit),
                            asset.Output + " のループ端でクリックが鳴る");
            }
        }

        [Test]
        public void 加工後もクリップしていない()
        {
            foreach (AudioAsset asset in AudioImportSetup.Assets.Where(a => a.Use == AudioUse.Loop))
            {
                float[] samples = AudioLoopBuilder.DecodeWav16(
                    File.ReadAllBytes(AudioImportSetup.AssetPath(asset.Output)),
                    out int _, out int __);

                Assert.That(AudioAnalysis.Peak(samples), Is.LessThanOrEqualTo(1.0),
                            asset.Output + " がフルスケールを超えている");
            }
        }

        // ---- 取り込みの除外ルール (8-1 と同じ) ----

        [TestCase("spaceEngineLow_003.ogg", true)]
        [TestCase("engine_loop.wav", true)]
        [TestCase("desktop.ini", false)]
        [TestCase("Thumbs.db", false)]
        [TestCase("License.txt", false)]
        [TestCase("Preview.ogg", false)]
        [TestCase(".DS_Store", false)]
        [TestCase("_scratch.ogg", false)]
        public void 非素材ファイルを取り込まない(string name, bool expected)
        {
            Assert.That(AudioImportSetup.IsAdoptable(name), Is.EqualTo(expected), name);
        }

        // ---- Step 6 の撤去 ----

        [Test]
        public void 旧EngineAudio系が残っていない()
        {
            Assembly unity = typeof(SolarSystem.Unity.AudioRouting).Assembly;
            Assert.That(unity.GetType("SolarSystem.Unity.EngineAudio"), Is.Null,
                        "EngineAudio が残っている（置換なので消す）");

            Assembly editor = typeof(AudioImportSetup).Assembly;
            Assert.That(editor.GetType("SolarSystem.Editor.EngineAudioClipBuilder"), Is.Null,
                        "EngineAudioClipBuilder が残っている");

            Assert.That(File.Exists("Assets/Materials/EngineRumble.asset"), Is.False,
                        "EngineRumble.asset が残っている");
        }

        [Test]
        public void ローパスを持つのはAudioRoutingだけ()
        {
            // **二重掛けを構造で防ぐ。** Step 6 は AudioSource 側と Mixer 側に
            // 分かれる予定になっていた。
            Assembly unity = typeof(SolarSystem.Unity.AudioRouting).Assembly;
            var holders = unity.GetTypes()
                .Where(t => t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic
                                        | BindingFlags.Public)
                             .Any(f => f.FieldType == typeof(AudioLowPassFilter)))
                .Select(t => t.Name)
                .ToArray();

            Assert.That(holders, Is.EquivalentTo(new[] { "AudioRouting" }),
                        "ローパスを持つ型が AudioRouting 以外にある: " + string.Join(", ", holders));
        }
    }
}
