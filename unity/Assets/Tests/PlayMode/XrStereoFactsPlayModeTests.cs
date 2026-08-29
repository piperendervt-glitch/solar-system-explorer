using System.Collections;
using NUnit.Framework;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR;

namespace SolarSystem.Tests.PlayMode
{
    /// <summary>
    /// **「設定した」ではなく「経路が通っている」ことを縛る (Step 12-0d)。**
    ///
    /// 12-0c で、設定アセットの `renderMode: 1` を見る EditMode テストが緑のまま
    /// **実行時は MultiPass だった。** アセットの値を見るテストはこの取り違えを
    /// 一切捕まえない。ここでは `XRSettings` の実測値だけを見る。
    ///
    /// ■ **フレームを回してから読む。**
    /// `eyeTextureDesc` は表示サブシステムが描き始めるまで埋まらず、初期化直後は
    /// XR を使っていないときと同じ既定値 (Tex2D 256x256 / volumeDepth 1) が読める。
    ///
    /// ■ **後片付けを必ず行う。**
    /// XR を立ち上げたまま次のテストへ渡すと、`XrBootPlayModeTests` の
    /// 「無指定なら動いていない」が巻き添えで落ちる。
    /// </summary>
    public sealed class XrStereoFactsPlayModeTests
    {
        /// <summary>読めるようになるまで待つフレーム数の上限。</summary>
        const int MaxFrames = 30;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            XrBoot.Shutdown();
            yield return null;

            Assert.That(XRSettings.enabled, Is.False,
                        "**XR を立ち上げたまま次のテストへ渡している**");
        }

        [UnityTest]
        public IEnumerator MockHMDがSinglePassInstancedで走る()
        {
            // **XR を立ち上げる前の値を対照に取る。**
            // 目のテクスチャは描き始めるまで作られず、そのあいだ「XR を使って
            // いないときの既定値」がそのまま読める。**その値と同じなら、
            // 読めているのは MultiPass ではなく「まだ何も無い」。**
            XrBoot.StereoFacts before = XrBoot.ReadStereoFacts();

            XrBoot.Result result = XrBoot.Initialize(XrBoot.Mode.Mock);
            if (!result.Initialized)
            {
                Assert.Inconclusive("MockHMD を初期化できなかった: " + result.Message);
            }

            XrBoot.StereoFacts facts = null;
            for (int i = 0; i < MaxFrames; i++)
            {
                yield return null;
                facts = XrBoot.ReadStereoFacts();
                if (facts.EyeTextureVolumeDepth == 2)
                {
                    break;
                }
            }

            Debug.Log($"[XrStereoFacts] 前={before} / 後={facts}");

            // **batchmode の Editor には目のテクスチャを描く先が無い (Step 12-0d)。**
            // XRSettings.enabled も device も立つが、`eyeTextureDesc` は起動前と
            // 同じまま。**「測れなかった」のであって「MultiPass だった」ではない。**
            // SPI で走っていることは exe でしか読めない（CLAUDE.md §0-B）。
            if (facts.EyeTextureWidth == before.EyeTextureWidth
                && facts.EyeTextureHeight == before.EyeTextureHeight
                && facts.EyeTextureDimension == before.EyeTextureDimension)
            {
                Assert.Inconclusive(
                    "目のテクスチャが作られていない（起動前と同じ値）。"
                    + " batchmode の Editor では読めない: " + facts);
            }

            // **SPI の 3 点セット。** どれか 1 つでも欠けたら SPI の経路ではない。
            Assert.That(facts.StereoRenderingMode, Is.EqualTo("SinglePassInstanced"),
                        "**MultiPass で走っている**（xr.sdk.mock-hmd.settings の登録漏れ）");
            Assert.That(facts.EyeTextureDimension, Is.EqualTo("Tex2DArray"),
                        "目のテクスチャが配列になっていない");
            Assert.That(facts.EyeTextureVolumeDepth, Is.EqualTo(2),
                        "目のテクスチャの層が 2 枚ない");
        }

        [UnityTest]
        public IEnumerator 初期化した直後の値は当てにならない()
        {
            // **12-0c で踏んだ落とし穴を回帰として残す。**
            // 立ち上げた直後に読むと、XR を使っていないときと同じ既定値が読める。
            // 「初期化できた」だけを見て SPI を語れない、という記録。
            XrBoot.StereoFacts before = XrBoot.ReadStereoFacts();
            Assert.That(before.XrSettingsEnabled, Is.False, "先に XR が動いている");

            XrBoot.Result result = XrBoot.Initialize(XrBoot.Mode.Mock);
            if (!result.Initialized)
            {
                Assert.Inconclusive("MockHMD を初期化できなかった: " + result.Message);
            }

            Assert.That(result.Facts, Is.Not.Null);
            Assert.That(result.Facts.EyeTextureDimension, Is.EqualTo(before.EyeTextureDimension),
                        "**初期化直後に正しい値が読めるようになった。**"
                        + " 読む時機の注意書き（XrFactsLogger）を見直すこと");

            yield return null;
        }
    }
}
