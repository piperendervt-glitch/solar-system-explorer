using System.Collections;
using NUnit.Framework;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SolarSystem.Tests.PlayMode
{
    /// <summary>
    /// **batchmode の Editor でフレーム時間が測れるか (Step 13-0b)。**
    ///
    /// `-executeMethod` にはフレームループが無いので、batchmode で
    /// フレームが進む唯一の経路が PlayMode テスト。**ここで埋まらなければ
    /// batchmode 側の基準値は取れない。**
    ///
    /// 閾値は持たない。**埋まるかどうかと、埋まるまでのフレーム数だけ**を見る。
    /// </summary>
    [Category("FrameTime")]
    public sealed class FrameTimePlayModeTests
    {
        /// <summary>待つ上限。exe では 5 フレームで埋まった（実測）。</summary>
        const int MaxWaitFrames = 600;

        [UnityTest]
        public IEnumerator batchmodeでFrameTimingManagerが埋まるか()
        {
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            Assert.That(FrameTimeProbe.Enabled, Is.True,
                        "計測が載っていない（-frameTimeOff が付いている？）");

            int waited = 0;
            while (waited < MaxWaitFrames && !FrameTimeProbe.HasLatest)
            {
                yield return null;
                waited++;
            }

            if (!FrameTimeProbe.HasLatest)
            {
                Assert.Inconclusive(
                    $"**batchmode の Editor では FrameTimingManager が埋まらない**"
                    + $"（{MaxWaitFrames} フレーム待った）。batchmode 側の基準値は取れない");
            }

            Debug.Log($"[FrameTime] batchmode Editor: {waited} フレームで埋まった"
                      + $" / CPU {FrameTimeProbe.LatestCpuMs:F4} ms"
                      + $" / GPU {FrameTimeProbe.LatestGpuMs:F4} ms");

            Assert.That(FrameTimeProbe.LatestCpuMs, Is.GreaterThan(0.0));
        }
    }
}
