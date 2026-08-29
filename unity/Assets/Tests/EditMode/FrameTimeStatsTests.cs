using System;
using NUnit.Framework;
using SolarSystem.Core;

namespace SolarSystem.Tests.EditMode
{
    /// <summary>
    /// フレーム時間の集計 (Step 13-0b)。
    ///
    /// **閾値は持たない。** ここで縛るのは集計の算術と、
    /// 「測れていないことを数字に化けさせない」ことだけ。
    /// </summary>
    public sealed class FrameTimeStatsTests
    {
        [Test]
        public void 集計の数表()
        {
            // 標本 [ms]        1, 2, 3, 4, 5, 6, 7, 8, 9, 10
            //   n     10
            //   平均  5.5      (55 / 10)
            //   p95   10       nearest-rank: ceil(0.95*10) = 10 番目
            //   最小  1 / 最大 10
            var samples = new[] { 1.0, 2.0, 3.0, 4.0, 5.0, 6.0, 7.0, 8.0, 9.0, 10.0 };
            FrameTimeStats.Summary s = FrameTimeStats.Summarize("CPU", samples);

            Assert.That(s.Label, Is.EqualTo("CPU"));
            Assert.That(s.Count, Is.EqualTo(10));
            Assert.That(s.Mean, Is.EqualTo(5.5).Within(1e-12));
            Assert.That(s.P95, Is.EqualTo(10.0));
            Assert.That(s.Min, Is.EqualTo(1.0));
            Assert.That(s.Max, Is.EqualTo(10.0));
        }

        [Test]
        public void 並び順に依らない()
        {
            var ascending = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
            var shuffled = new[] { 4.0, 1.0, 5.0, 3.0, 2.0 };

            FrameTimeStats.Summary a = FrameTimeStats.Summarize("a", ascending);
            FrameTimeStats.Summary b = FrameTimeStats.Summarize("b", shuffled);

            Assert.That(b.Mean, Is.EqualTo(a.Mean).Within(1e-12));
            Assert.That(b.P95, Is.EqualTo(a.P95));
            Assert.That(b.Min, Is.EqualTo(a.Min));
            Assert.That(b.Max, Is.EqualTo(a.Max));
        }

        [Test]
        public void p95はnearest_rankで補間しない()
        {
            // **補間しない。** 標本数が少ないときに補間すると、実在しない値が出る。
            //
            //   n     rank = ceil(0.95 * n)   返す値（1 始まり）
            //   1     1                       samples[0]
            //   20    19                      19 番目
            //   21    20                      20 番目
            //   300   285                     285 番目
            Assert.That(FrameTimeStats.Percentile(new[] { 7.0 }, 0.95), Is.EqualTo(7.0));

            var twenty = new double[20];
            for (int i = 0; i < 20; i++)
            {
                twenty[i] = i + 1;
            }

            Assert.That(FrameTimeStats.Percentile(twenty, 0.95), Is.EqualTo(19.0));

            var twentyOne = new double[21];
            for (int i = 0; i < 21; i++)
            {
                twentyOne[i] = i + 1;
            }

            Assert.That(FrameTimeStats.Percentile(twentyOne, 0.95), Is.EqualTo(20.0));

            var threeHundred = new double[300];
            for (int i = 0; i < 300; i++)
            {
                threeHundred[i] = i + 1;
            }

            Assert.That(FrameTimeStats.Percentile(threeHundred, 0.95), Is.EqualTo(285.0));

            // 実在する標本のどれかであること（補間して中間値を作らない）。
            var uneven = new[] { 1.0, 100.0 };
            Assert.That(FrameTimeStats.Percentile(uneven, 0.95), Is.EqualTo(100.0));
        }

        [Test]
        public void 標本が無ければ例外()
        {
            // **0 件を「平均 0 ms」として通さない。**
            // 埋まっていないフレームを 0 として混ぜると、待った分だけ平均が下がる。
            Assert.Throws<ArgumentException>(() => FrameTimeStats.Summarize("空", new double[0]));
            Assert.Throws<ArgumentException>(() => FrameTimeStats.Summarize("null", null));
            Assert.Throws<ArgumentException>(() => FrameTimeStats.Percentile(new double[0], 0.95));
        }

        [Test]
        public void NaNや範囲外は例外()
        {
            Assert.Throws<ArgumentException>(
                () => FrameTimeStats.Summarize("NaN", new[] { 1.0, double.NaN }));
            Assert.Throws<ArgumentException>(
                () => FrameTimeStats.Summarize("Inf", new[] { 1.0, double.PositiveInfinity }));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => FrameTimeStats.Percentile(new[] { 1.0 }, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => FrameTimeStats.Percentile(new[] { 1.0 }, 1.5));
        }
    }
}
