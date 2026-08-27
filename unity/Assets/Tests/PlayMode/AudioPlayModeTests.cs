using System.Collections;
using System.IO;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SolarSystem.Tests.PlayMode
{
    /// <summary>
    /// 音の実行時の値 (Step 10-1 / 10-2 / 10-5)。
    ///
    /// **batchmode では音は鳴らない**（CLAUDE.md 0-B）。
    /// volume / pitch / cutoff / Play() 回数という数値だけを縛る。
    /// 「聴こえるか」は exe で人が確かめる。
    /// </summary>
    public sealed class AudioPlayModeTests
    {
        const double Dt = UniverseConstants.FixedDeltaSeconds;

        UniverseRoot _root;
        ShipRig _rig;
        AudioRouting _audio;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath = Path.Combine(Path.GetTempPath(), "solar-system-explorer-audio.save.json");
            SaveFile.Delete();
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _root = Object.FindAnyObjectByType<UniverseRoot>();
            _rig = Object.FindAnyObjectByType<ShipRig>();
            _audio = Object.FindAnyObjectByType<AudioRouting>();
            Assert.That(_audio, Is.Not.Null, "AudioRouting がシーンに無い");
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        [UnityTest]
        public IEnumerator ループ2本が再生されている()
        {
            yield return null;

            foreach (AudioSource source in new[] { _audio.Engine, _audio.Cockpit })
            {
                Assert.That(source, Is.Not.Null);
                Assert.That(source.clip, Is.Not.Null, source.name + " にクリップが無い");
                Assert.That(source.loop, Is.True, "ループになっていない");
                Assert.That(source.isPlaying, Is.True, "再生されていない");

                // **すべて船内音なので 2D。** 距離減衰も定位も無い。
                Assert.That(source.spatialBlend, Is.EqualTo(0f).Within(1e-4f), "2D になっていない");
            }

            Assert.That(_audio.Sfx, Is.Not.Null);
            Assert.That(_audio.Sfx.loop, Is.False, "単発用が loop になっている");
        }

        [UnityTest]
        public IEnumerator 音量がグループの積になっている()
        {
            yield return null;
            _root.Tick(Dt);

            double expectedEngine = AudioMix.MasterVolume * AudioMix.EngineVolume
                                    * AudioMix.EngineScale(_audio.Docked01);
            double expectedCockpit = AudioMix.MasterVolume * AudioMix.CockpitVolume;

            Debug.Log(string.Format(
                "  [Step10-2] engine {0:F4} (期待 {1:F4}) / cockpit {2:F4} (期待 {3:F4}) / cutoff {4:F0} Hz",
                _audio.Engine.volume, expectedEngine,
                _audio.Cockpit.volume, expectedCockpit, _audio.LastCutoffHz));

            Assert.That(_audio.Engine.volume, Is.EqualTo((float)expectedEngine).Within(1e-3f));
            Assert.That(_audio.Cockpit.volume, Is.EqualTo((float)expectedCockpit).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator Master音量を0にすると全部無音になる()
        {
            yield return null;
            _audio.SetVolume(AudioGroup.Master, 0.0);
            _root.Tick(Dt);

            Assert.That(_audio.Engine.volume, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(_audio.Cockpit.volume, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(_audio.Sfx.volume, Is.EqualTo(0f).Within(1e-6f));
        }

        [UnityTest]
        public IEnumerator ローパスはエンジンに1個だけ()
        {
            yield return null;

            AudioLowPassFilter[] all = Object.FindObjectsByType<AudioLowPassFilter>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            // **二重掛けを残さない。** Step 6 は AudioSource 側と Mixer 側に
            // 分かれる予定になっていた。
            Assert.That(all.Length, Is.EqualTo(1), "ローパスが 1 個ではない");
            Assert.That(all[0], Is.EqualTo(_audio.EngineLowPass));
        }

        [UnityTest]
        public IEnumerator ドッキングでこもり離脱で戻る()
        {
            yield return null;

            // 飛行中。
            _audio.SnapDocked(false);
            float flying = _audio.LastCutoffHz;
            float flyingEngine = _audio.LastEngineVolume;

            // ドッキング。
            _audio.SnapDocked(true);
            float docked = _audio.LastCutoffHz;
            float dockedEngine = _audio.LastEngineVolume;

            Debug.Log(string.Format(
                "  [Step10-5] cutoff 飛行 {0:F0} Hz -> ドック {1:F0} Hz / エンジン音量 {2:F4} -> {3:F4}",
                flying, docked, flyingEngine, dockedEngine));

            Assert.That(docked, Is.LessThan(flying), "ドッキングでこもっていない");
            Assert.That(dockedEngine, Is.LessThan(flyingEngine), "エンジンが絞られていない");

            Assert.That(flying, Is.EqualTo((float)AudioMix.FlyingCutoffHz).Within(1f));
            Assert.That(docked, Is.EqualTo((float)AudioMix.DockedCutoffHz).Within(1f));
        }

        [UnityTest]
        public IEnumerator 遷移は一瞬では終わらない()
        {
            yield return null;
            _audio.SnapDocked(false);

            // 1 フレームぶんだけドッキング側へ進める。
            _audio.Tick(docked: true, deltaSeconds: 1.0 / 60.0);

            Assert.That(_audio.Docked01, Is.GreaterThan(0.0), "進んでいない");
            Assert.That(_audio.Docked01, Is.LessThan(1.0), "1 フレームで振り切れている");
        }

        [UnityTest]
        public IEnumerator 単発は鳴らすたびに再生される()
        {
            yield return null;

            AudioClip clip = _audio.Engine.clip; // 中身は問わない
            AudioSource sfx = _audio.Sfx;
            sfx.Stop();

            // PlayOneShot は isPlaying で数えられるので、鳴っているかだけを見る。
            Assert.That(sfx.isPlaying, Is.False);
            _audio.PlayOneShot(clip);
            Assert.That(sfx.isPlaying, Is.True, "単発が鳴っていない");

            // null を渡しても落ちないこと。
            Assert.DoesNotThrow(() => _audio.PlayOneShot(null));
        }
    }
}
