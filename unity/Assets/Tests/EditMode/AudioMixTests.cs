using System;
using NUnit.Framework;
using SolarSystem.Core;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>グループ構成と、飛行 ⇔ ドッキングの補間 (Step 10-2 / 10-5)。</summary>
    public sealed class AudioMixTests
    {
        [Test]
        public void グループは4つ()
        {
            Assert.That(Enum.GetValues(typeof(AudioGroup)).Length, Is.EqualTo(AudioMix.GroupCount));
            Assert.That(Enum.IsDefined(typeof(AudioGroup), AudioGroup.Master), Is.True);
            Assert.That(Enum.IsDefined(typeof(AudioGroup), AudioGroup.Engine), Is.True);
            Assert.That(Enum.IsDefined(typeof(AudioGroup), AudioGroup.Cockpit), Is.True);
            Assert.That(Enum.IsDefined(typeof(AudioGroup), AudioGroup.Sfx), Is.True);
        }

        [Test]
        public void 既定音量はすべて範囲内で0でない()
        {
            foreach (AudioGroup g in Enum.GetValues(typeof(AudioGroup)))
            {
                double v = AudioMix.DefaultVolume(g);
                Assert.That(v, Is.GreaterThan(0.0), g + " が消音になっている");
                Assert.That(v, Is.InRange(AudioMix.MinVolume, AudioMix.MaxVolume), g.ToString());
            }
        }

        // ---- 遷移 (10-5) ----

        [Test]
        public void ドッキングへ向かうと1へ近づく()
        {
            double t = 0.0;
            for (int i = 0; i < 200; i++)
            {
                t = AudioMix.AdvanceDocked(t, docked: true, deltaSeconds: 1.0 / 60.0);
                Assert.That(t, Is.InRange(0.0, 1.0));
            }

            Assert.That(t, Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void 離脱すると0へ戻る()
        {
            double t = 1.0;
            for (int i = 0; i < 200; i++)
            {
                t = AudioMix.AdvanceDocked(t, docked: false, deltaSeconds: 1.0 / 60.0);
            }

            Assert.That(t, Is.EqualTo(0.0).Within(1e-9));
        }

        [Test]
        public void 遷移は指定した秒数でだいたい終わる()
        {
            double t = 0.0;
            const double dt = 1.0 / 60.0;
            int steps = 0;
            while (t < 1.0 && steps < 10000)
            {
                t = AudioMix.AdvanceDocked(t, docked: true, dt);
                steps++;
            }

            Assert.That(steps * dt, Is.EqualTo(AudioMix.TransitionSeconds).Within(dt * 1.5));
        }

        [Test]
        public void カットオフは飛行で高くドッキングで低い()
        {
            Assert.That(AudioMix.CutoffHz(0.0), Is.EqualTo(AudioMix.FlyingCutoffHz).Within(1.0));
            Assert.That(AudioMix.CutoffHz(1.0), Is.EqualTo(AudioMix.DockedCutoffHz).Within(1.0));
            Assert.That(AudioMix.DockedCutoffHz, Is.LessThan(AudioMix.FlyingCutoffHz));
        }

        [Test]
        public void カットオフは単調に下がる()
        {
            double prev = double.MaxValue;
            for (int i = 0; i <= 100; i++)
            {
                double v = AudioMix.CutoffHz(i / 100.0);
                Assert.That(v, Is.LessThanOrEqualTo(prev), $"t={i / 100.0} で上がっている");
                prev = v;
            }
        }

        [Test]
        public void カットオフは対数で補間される()
        {
            // **線形だと前半でほとんど変化が聴こえない。**
            // 中点が線形中点 (7000) より低ければ対数で効いている。
            double mid = AudioMix.CutoffHz(0.5);
            double linearMid = (AudioMix.FlyingCutoffHz + AudioMix.DockedCutoffHz) * 0.5;

            Assert.That(mid, Is.LessThan(linearMid),
                        $"中点 {mid:F0} Hz が線形中点 {linearMid:F0} Hz を下回っていない");

            // 対数中点 = sqrt(12000 * 2000) = 4899 Hz
            double logMid = Math.Sqrt(AudioMix.FlyingCutoffHz * AudioMix.DockedCutoffHz);
            Assert.That(mid, Is.EqualTo(logMid).Within(1.0));
        }

        [Test]
        public void エンジン音量はドッキングで下がる()
        {
            Assert.That(AudioMix.EngineScale(0.0), Is.EqualTo(1.0).Within(1e-9));
            Assert.That(AudioMix.EngineScale(1.0),
                        Is.EqualTo(AudioMix.DockedEngineScale).Within(1e-9));
            Assert.That(AudioMix.DockedEngineScale, Is.LessThan(1.0));
        }

        [Test]
        public void 補間は両端で速度0になる()
        {
            // 遷移の開始と終了を目立たなくするため。
            const double h = 1e-4;
            Assert.That((AudioMix.Smooth(h) - AudioMix.Smooth(0.0)) / h, Is.LessThan(0.01));
            Assert.That((AudioMix.Smooth(1.0) - AudioMix.Smooth(1.0 - h)) / h, Is.LessThan(0.01));
            Assert.That(AudioMix.Smooth(0.5), Is.EqualTo(0.5).Within(1e-9));
        }

        // ---- 加工の純関数 ----

        [Test]
        public void クロスフェードで段差比が下がる()
        {
            // のこぎり波。端で必ず跳ぶ。
            const int n = 4410;
            var saw = new float[n];
            for (int i = 0; i < n; i++)
            {
                saw[i] = i / (float)n;
            }

            double before = AudioAnalysis.SeamRatio(saw);
            float[] after = AudioAnalysis.CrossfadeLoop(saw, 441);

            Assert.That(before, Is.GreaterThan(100.0), "素材の段差が小さすぎて検証にならない");
            Assert.That(AudioAnalysis.SeamRatio(after), Is.LessThan(before));
        }

        [Test]
        public void クロスフェードは長さを縮める()
        {
            var s = new float[1000];
            Assert.That(AudioAnalysis.CrossfadeLoop(s, 100).Length, Is.EqualTo(900));
        }

        [Test]
        public void クロスフェード長が不正なら例外()
        {
            var s = new float[100];
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioAnalysis.CrossfadeLoop(s, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => AudioAnalysis.CrossfadeLoop(s, 50));
        }

        [Test]
        public void 素材は繰り返して読まれる()
        {
            var s = new float[] { 0f, 1f, 2f, 3f };
            Assert.That(AudioAnalysis.SampleLooped(s, 0.0), Is.EqualTo(0.0).Within(1e-6));
            Assert.That(AudioAnalysis.SampleLooped(s, 4.0), Is.EqualTo(0.0).Within(1e-6),
                        "末尾を超えたら先頭へ回る");
            Assert.That(AudioAnalysis.SampleLooped(s, 1.5), Is.EqualTo(1.5).Within(1e-6),
                        "線形補間になっていない");
        }

        [Test]
        public void 層を重ねても入力数で割るのでクリップしない()
        {
            const int rate = 44100;
            var src = new float[rate];
            for (int i = 0; i < src.Length; i++)
            {
                src[i] = 0.9f; // 全部ほぼフルスケール
            }

            float[] mixed = AudioAnalysis.BuildLayeredLoop(
                src, rate, new[] { 0.97, 1.0, 1.03 }, new[] { 0.0, 0.31, 0.62 }, 2.0, 0.2);

            Assert.That(AudioAnalysis.Peak(mixed), Is.LessThanOrEqualTo(1.0));
        }

        [Test]
        public void 層を重ねた結果は指定した長さになる()
        {
            const int rate = 8000;
            var src = new float[rate];
            float[] mixed = AudioAnalysis.BuildLayeredLoop(
                src, rate, new[] { 1.0 }, new[] { 0.0 }, 2.0, 0.2);

            Assert.That(mixed.Length, Is.EqualTo(rate * 2));
        }
    }
}
