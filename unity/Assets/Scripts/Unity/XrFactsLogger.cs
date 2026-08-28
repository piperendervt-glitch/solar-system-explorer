using System.Collections;
using UnityEngine;

namespace SolarSystem.Unity
{
    /// <summary>
    /// **立体視の実態をフレームが回ってから読む (Step 12-0d)。**
    ///
    /// `XRSettings.eyeTextureDesc` は表示サブシステムが描き始めるまで埋まらない。
    /// 初期化直後に読むと、**XR を使っていないときと同じ既定値**
    /// (Tex2D 256x256 / volumeDepth 1) が読めてしまい、
    /// 「SPI なのか MultiPass なのか」の判定材料にならない。
    ///
    /// **判定はしない。決めた回のフレームで値をログに落とすだけ。**
    /// `XrBoot.Initialize` が XR を立ち上げたときだけ生成される。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XrFactsLogger : MonoBehaviour
    {
        /// <summary>読み直すフレーム。**最初と、落ち着いた後の両方**を残す。</summary>
        public static readonly int[] Frames = { 1, 2, 10, 60, 120 };

        /// <summary>最後に読んだ値。テストと撮影から参照する。</summary>
        public static XrBoot.StereoFacts Latest { get; private set; }

        IEnumerator Start()
        {
            int previous = 0;
            foreach (int frame in Frames)
            {
                for (int i = previous; i < frame; i++)
                {
                    yield return null;
                }

                previous = frame;
                Latest = XrBoot.ReadStereoFacts();
                Debug.Log($"[XrFacts] frame={frame} / {Latest}");
            }
        }
    }
}
