using System;
using NUnit.Framework;
using SolarSystem.Core;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>エンジン音のスラスト連動 (Step 10-3)。</summary>
    public sealed class EngineAudioModelTests
    {
        // ---- 入出力表 ----

        [TestCase(0.0, 0.36, 0.90)]
        [TestCase(0.5, 0.68, 1.05)]
        [TestCase(1.0, 1.00, 1.20)]
        public void スラストに対する音量係数とピッチ(double thrust, double volume, double pitch)
        {
            Assert.That(EngineAudioModel.TargetVolumeScale(thrust, braking: false),
                        Is.EqualTo(volume).Within(1e-9));
            Assert.That(EngineAudioModel.TargetPitch(thrust, braking: false),
                        Is.EqualTo(pitch).Within(1e-9));
        }

        [Test]
        public void 制動中はスラストによらず一定()
        {
            foreach (double thrust in new[] { 0.0, 0.5, 1.0 })
            {
                Assert.That(EngineAudioModel.TargetVolumeScale(thrust, braking: true),
                            Is.EqualTo(EngineAudioModel.BrakeVolumeScale).Within(1e-9));
                Assert.That(EngineAudioModel.TargetPitch(thrust, braking: true),
                            Is.EqualTo(EngineAudioModel.BrakePitch).Within(1e-9));
            }
        }

        [Test]
        public void 音量係数は全開で1に正規化されている()
        {
            // **10-2 で耳で決めた Engine = 0.10 が「全開時の音量」を意味するように。**
            // 計画書の 0.25 / 0.70 / 0.60 を 0.70 で割った値。
            Assert.That(EngineAudioModel.FullVolumeScale, Is.EqualTo(1.0).Within(1e-9));
            Assert.That(EngineAudioModel.IdleVolumeScale,
                        Is.EqualTo(0.25 / 0.70).Within(0.005), "アイドルの比率が崩れている");
            Assert.That(EngineAudioModel.BrakeVolumeScale,
                        Is.EqualTo(0.60 / 0.70).Within(0.005), "制動中の比率が崩れている");
        }

        [Test]
        public void ピッチは0対9から1対2に収まる()
        {
            for (int i = 0; i <= 100; i++)
            {
                foreach (bool braking in new[] { false, true })
                {
                    double p = EngineAudioModel.TargetPitch(i / 100.0, braking);
                    Assert.That(p, Is.InRange(0.9, 1.2), $"thrust={i / 100.0} braking={braking}");
                }
            }
        }

        [Test]
        public void 音量係数もピッチもスラストに対して単調に増える()
        {
            double prevVolume = double.MinValue;
            double prevPitch = double.MinValue;

            for (int i = 0; i <= 100; i++)
            {
                double t = i / 100.0;
                double v = EngineAudioModel.TargetVolumeScale(t, braking: false);
                double p = EngineAudioModel.TargetPitch(t, braking: false);

                Assert.That(v, Is.GreaterThanOrEqualTo(prevVolume), $"volume が t={t} で減った");
                Assert.That(p, Is.GreaterThanOrEqualTo(prevPitch), $"pitch が t={t} で減った");
                prevVolume = v;
                prevPitch = p;
            }
        }

        [Test]
        public void 範囲外のスラストは丸められる()
        {
            Assert.That(EngineAudioModel.TargetPitch(-1.0, false),
                        Is.EqualTo(EngineAudioModel.IdlePitch).Within(1e-9));
            Assert.That(EngineAudioModel.TargetPitch(2.0, false),
                        Is.EqualTo(EngineAudioModel.FullPitch).Within(1e-9));
        }

        // ---- 一次遅れ ----

        [Test]
        public void 時定数ぶん進むと目標の63パーセントまで動く()
        {
            const double tau = 0.5;
            double v = EngineAudioModel.FirstOrderLag(0.0, 1.0, tau, tau);
            Assert.That(v, Is.EqualTo(1.0 - Math.Exp(-1.0)).Within(1e-9));
            Assert.That(v, Is.EqualTo(0.632).Within(0.001));
        }

        [Test]
        public void 遅れは目標を行き過ぎない()
        {
            // **dt > tau でも行き過ぎない。** 線形近似だと発振する。
            double v = EngineAudioModel.FirstOrderLag(0.0, 1.0, deltaSeconds: 10.0, tauSeconds: 0.1);
            Assert.That(v, Is.LessThanOrEqualTo(1.0));
            Assert.That(v, Is.EqualTo(1.0).Within(1e-6));
        }

        [Test]
        public void 時定数が0なら即座に追従する()
        {
            Assert.That(EngineAudioModel.FirstOrderLag(0.0, 1.0, 0.016, 0.0),
                        Is.EqualTo(1.0).Within(1e-9));
        }

        [Test]
        public void 遅れを通すと目標へ近づく()
        {
            var model = new EngineAudioModel();
            model.Snap(0.0, braking: false);

            double start = model.Pitch;
            for (int i = 0; i < 300; i++)
            {
                model.Advance(1.0, braking: false, deltaSeconds: 1.0 / 60.0,
                              tauSeconds: AudioMix.EngineLagSeconds);
            }

            Assert.That(start, Is.EqualTo(EngineAudioModel.IdlePitch).Within(1e-9));
            Assert.That(model.Pitch, Is.EqualTo(EngineAudioModel.FullPitch).Within(1e-3));
            Assert.That(model.VolumeScale, Is.EqualTo(EngineAudioModel.FullVolumeScale).Within(1e-3));
        }

        [Test]
        public void 一フレームでは振り切れない()
        {
            var model = new EngineAudioModel();
            model.Snap(0.0, braking: false);
            model.Advance(1.0, braking: false, deltaSeconds: 1.0 / 60.0,
                          tauSeconds: AudioMix.EngineLagSeconds);

            Assert.That(model.Pitch, Is.GreaterThan(EngineAudioModel.IdlePitch), "進んでいない");
            Assert.That(model.Pitch, Is.LessThan(EngineAudioModel.FullPitch), "1 フレームで振り切れた");
        }

        [Test]
        public void Snapは遅れを飛ばす()
        {
            var model = new EngineAudioModel();
            model.Snap(1.0, braking: false);

            Assert.That(model.Pitch, Is.EqualTo(EngineAudioModel.FullPitch).Within(1e-9));
            Assert.That(model.VolumeScale, Is.EqualTo(EngineAudioModel.FullVolumeScale).Within(1e-9));
        }

        [Test]
        public void 時定数の既定はパネルの範囲内()
        {
            Assert.That(AudioMix.EngineLagSeconds,
                        Is.InRange(AudioMix.MinEngineLagSeconds, AudioMix.MaxEngineLagSeconds));
            Assert.That(AudioMix.EngineLagSeconds, Is.GreaterThan(0.0));
        }
    }
}
