using NUnit.Framework;
using SolarSystem.Core;
using UnityEngine;

namespace SolarSystem.Tests.EditMode
{
    public sealed class UniverseClockTests
    {
        [Test]
        public void 固定ステップ未満では進まない()
        {
            var clock = new UniverseClock();
            Assert.That(clock.Advance(0.001), Is.EqualTo(0));
            Assert.That(clock.StepCount, Is.EqualTo(0));
        }

        [Test]
        public void 溜まった分だけステップが出る()
        {
            var clock = new UniverseClock();
            // 1/60 の 3.5 倍 -> 3 ステップ、残り 0.5 ステップぶん
            int steps = clock.Advance(UniverseConstants.FixedDeltaSeconds * 3.5);

            Assert.That(steps, Is.EqualTo(3));
            Assert.That(clock.InterpolationAlpha, Is.EqualTo(0.5).Within(1e-9));
        }

        [Test]
        public void 長すぎるdtは上限で打ち切られる()
        {
            var clock = new UniverseClock();
            int steps = clock.Advance(10.0); // 600 ステップぶん

            Assert.That(steps, Is.EqualTo(UniverseClock.MaxStepsPerAdvance));
            Assert.That(clock.InterpolationAlpha, Is.EqualTo(0.0), "打ち切ったら残りは捨てる");
        }

        [Test]
        public void 経過時間は積み上げないので誤差が乗らない()
        {
            var clock = new UniverseClock();
            const int steps = 36000; // 600 秒ぶん

            for (int i = 0; i < steps; i++)
            {
                clock.Advance(UniverseConstants.FixedDeltaSeconds);
            }

            Assert.That(clock.StepCount, Is.EqualTo(steps));

            // 比較対象: += で素朴に積み上げた場合
            double naive = 0.0;
            for (int i = 0; i < steps; i++)
            {
                naive += UniverseConstants.FixedDeltaSeconds;
            }

            double exact = steps * UniverseConstants.FixedDeltaSeconds;
            double naiveDrift = System.Math.Abs(naive - exact);
            double clockDrift = System.Math.Abs(clock.ElapsedSeconds - exact);

            Debug.Log(
                $"[Step1] 600 秒ぶんの時間積算: ElapsedSeconds={clock.ElapsedSeconds:R} / " +
                $"素朴な += の誤差 = {naiveDrift:E3} s / 本実装の誤差 = {clockDrift:E3} s");

            Assert.That(clockDrift, Is.EqualTo(0.0), "乗算で導いているので誤差は 0 のはず");
            Assert.That(clock.ElapsedSeconds, Is.EqualTo(600.0).Within(1e-9));
        }

        [Test]
        public void 負のdtは受け付けない()
        {
            var clock = new UniverseClock();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => clock.Advance(-1.0));
        }

        [Test]
        public void Resetで初期状態に戻る()
        {
            var clock = new UniverseClock();
            clock.Advance(1.0);
            clock.Reset();

            Assert.That(clock.StepCount, Is.EqualTo(0));
            Assert.That(clock.ElapsedSeconds, Is.EqualTo(0.0));
        }
    }
}
