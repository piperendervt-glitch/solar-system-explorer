using System.Collections;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SolarSystem.Tests.PlayMode
{
    /// <summary>
    /// **セッション D の道具 (片目の故意破壊)。**
    ///
    /// ■ `Category("XR")` を付けてある
    /// XR の実態は batchmode の Editor では測れない（CLAUDE.md §0-B）。
    /// **ここで縛れるのは「平面では no-op であること」と「元に戻ること」だけ。**
    /// 実際に片目が落ちるかどうかは exe の数値表と画像で見る。
    ///
    ///   .\tools\run_tests.ps1 -TestPlatform PlayMode -Filter 'XrSessionD'
    /// </summary>
    [Category("XR")]
    public sealed class XrSessionDPlayModeTests
    {
        XrDiagnostics _diagnostics;
        CameraStackController _stack;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _diagnostics = Object.FindAnyObjectByType<XrDiagnostics>();
            _stack = Object.FindAnyObjectByType<CameraStackController>();
            Assert.That(_diagnostics, Is.Not.Null, "XrDiagnostics が無い");
            Assert.That(_stack, Is.Not.Null, "カメラスタックが無い");
        }

        [TearDown]
        public void TearDown() => _diagnostics?.ClearFault();

        [Test]
        public void 片目落ちは平面ではno_opになる()
        {
            // **平面では目が 1 つしかないので壊しようがない。**
            // `stereoEnabled` が false のあいだは `stereoTargetEye` を触らない。
            StereoTargetEyeMask before = _stack.Cockpit.stereoTargetEye;

            _diagnostics.SetFault(XrDiagnostics.Fault.DropLayerInOneEye,
                                  XrLayer.Cockpit, XrDiagnostics.Eye.Left);

            Assert.That(_stack.Cockpit.stereoEnabled, Is.False, "平面のはずが XR が動いている");
            Assert.That(_diagnostics.PerEyeFaultApplied, Is.False,
                        "**平面で片目の故意破壊が適用されている**");
            Assert.That(_stack.Cockpit.stereoTargetEye, Is.EqualTo(before),
                        "平面なのに stereoTargetEye が変わっている");
        }

        [Test]
        public void 片目落ちは元に戻せる()
        {
            // **元に戻せることまでが道具の責任。**
            // 戻せないと、以降の測定が全部その状態で回ってしまう。
            StereoTargetEyeMask before = _stack.Near.stereoTargetEye;

            _diagnostics.SetFault(XrDiagnostics.Fault.DropLayerInOneEye,
                                  XrLayer.Near, XrDiagnostics.Eye.Right);
            _diagnostics.ClearFault();

            Assert.That(_stack.Near.stereoTargetEye, Is.EqualTo(before));
            Assert.That(_diagnostics.ActiveFault, Is.EqualTo(XrDiagnostics.Fault.None));
            Assert.That(_diagnostics.PerEyeFaultApplied, Is.False);
        }

        [Test]
        public void 片目だけ深度クリアを飛ばすは実装されていない()
        {
            // **盲点として明示的に縛る (セッション D)。**
            // 深度クリアはカメラ単位で、目ごとに分ける口が Unity/URP に無い。
            // `stereoTargetEye` は描画先の目を選ぶだけで、クリアを分けられない。
            //
            // **将来これが実装できたら、このテストが落ちて気づける。**
            _diagnostics.SetFault(XrDiagnostics.Fault.SkipDepthClearInOneEye,
                                  XrLayer.Cockpit, XrDiagnostics.Eye.Left);

            Assert.That(_diagnostics.PerEyeFaultApplied, Is.False,
                        "片目だけの深度クリアが実装された？ 盲点の記述を見直すこと");
        }

        [Test]
        public void 層アイソレーションは4段すべてを取り出せる()
        {
            // セッション D の Q1 はこの口の上に立っている。
            // **カメラは無効にせず culling mask だけを空にする**（→ CLAUDE.md §0-B）。
            for (int layer = 1; layer <= 4; layer++)
            {
                _diagnostics.SetIsolation(layer);

                Camera[] cameras = { _stack.Deep, _stack.Near, _stack.Nearfield, _stack.Cockpit };
                for (int i = 0; i < cameras.Length; i++)
                {
                    bool shouldDraw = layer - 1 == i;
                    Assert.That(cameras[i].cullingMask != 0, Is.EqualTo(shouldDraw),
                                $"isolation={layer} のとき {XrDiagnosticsModel.LayerNames[i]}");
                    Assert.That(cameras[i].enabled, Is.True,
                                "**カメラを無効にしている**（ポストプロセスの掛かり方が変わる）");
                }
            }

            _diagnostics.SetIsolation(0);
            foreach (Camera cam in new[] { _stack.Deep, _stack.Near, _stack.Nearfield, _stack.Cockpit })
            {
                Assert.That(cam.cullingMask, Is.Not.EqualTo(0), "戻っていない");
            }
        }
    }
}
