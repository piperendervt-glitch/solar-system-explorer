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
    /// XR 診断を実際のシーンで回す (Step 12 の準備 / 平面版)。
    ///
    /// **絵の良し悪しは判断しない。** 縛るのは
    ///   - 層を切り分けるとその層のプローブが数えられること
    ///   - **故意に壊すと測定値が動くこと**（動かない測定は壊れを検出できていない）
    /// の 2 つだけ。
    /// </summary>
    public sealed class XrDiagnosticsPlayModeTests
    {
        UniverseRoot _root;
        XrDiagnostics _diagnostics;
        CockpitScreens _screens;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath =
                Path.Combine(Path.GetTempPath(), "solar-system-explorer-xr.save.json");
            SaveFile.Delete();

            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _root = Object.FindAnyObjectByType<UniverseRoot>();
            _diagnostics = Object.FindAnyObjectByType<XrDiagnostics>();
            _screens = Object.FindAnyObjectByType<CockpitScreens>();

            // **視点を cockpit-view に固定する。**
            // プローブが写るかどうかは視点で変わる（外の段のプローブは
            // 地球や内装に隠れる）。**隠れること自体は道具の壊れではない**ので、
            // 判定は Editor の xr-stack と同じ視点で行う。
            var runner = Object.FindAnyObjectByType<ScenarioRunner>();
            var rig = Object.FindAnyObjectByType<ShipRig>();
            var stack = Object.FindAnyObjectByType<CameraStackController>();
            var overlay = Object.FindAnyObjectByType<DebugOverlay>();
            if (runner != null && runner.Select(_root.Model, "cockpit-view"))
            {
                runner.Apply(_root, rig, stack, overlay);
                for (int i = 0; i < 12; i++)
                {
                    _root.Tick(UniverseConstants.FixedDeltaSeconds);
                }
            }

            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (_diagnostics != null)
            {
                _diagnostics.ClearFault();
                _diagnostics.SetIsolation(0);
                _diagnostics.SetProbesVisible(false);
            }

            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        void RequireDiagnostics()
        {
            if (_diagnostics == null)
            {
                Assert.Inconclusive("XrDiagnostics がシーンに無い");
            }
        }

        [UnityTest]
        public IEnumerator 既定ではすべてOFF()
        {
            yield return null;
            RequireDiagnostics();

            Assert.That(_diagnostics.IsOpen, Is.False, "F5 が既定で開いている");
            Assert.That(_diagnostics.ProbesVisible, Is.False, "プローブが既定で出ている");
            Assert.That(_diagnostics.ColorMapEnabled, Is.False);
            Assert.That(_diagnostics.MaskOverlayEnabled, Is.False);
            Assert.That(_diagnostics.Isolation, Is.EqualTo(0));
            Assert.That(_diagnostics.ActiveFault, Is.EqualTo(XrDiagnostics.Fault.None),
                        "**故意破壊が既定で入っている**");
        }

        [UnityTest]
        public IEnumerator 層を切り分けるとその層のプローブが数えられる()
        {
            yield return null;
            RequireDiagnostics();

            _diagnostics.SetProbesVisible(true);

            for (int layer = 0; layer < 4; layer++)
            {
                _diagnostics.SetIsolation(layer + 1);
                XrDiagnosticsResult result = _diagnostics.Measure();

                Assert.That(result, Is.Not.Null);
                Assert.That(result.Probes[layer].Count, Is.GreaterThan(0),
                            XrDiagnosticsModel.LayerNames[layer]
                            + " のプローブが 1 画素も写らない");

                Debug.Log($"  [Step12] only {XrDiagnosticsModel.LayerNames[layer]}: "
                          + result.Probes[layer]);
            }

            _diagnostics.SetIsolation(0);
        }

        [UnityTest]
        public IEnumerator 故意破壊で測定値が動く()
        {
            yield return null;
            RequireDiagnostics();

            _diagnostics.SetProbesVisible(true);
            XrDiagnosticsResult normal = Snapshot();

            var faults = new (XrDiagnostics.Fault Fault, XrLayer Layer)[]
            {
                (XrDiagnostics.Fault.NoDepthClear, XrLayer.Cockpit),
                (XrDiagnostics.Fault.SwapOverlayOrder, XrLayer.Cockpit),
                (XrDiagnostics.Fault.EmptyCullingMask, XrLayer.Cockpit),
            };

            foreach (var item in faults)
            {
                _diagnostics.SetFault(item.Fault, item.Layer);
                XrDiagnosticsResult broken = Snapshot();
                _diagnostics.ClearFault();

                int ownerChanged = 0;
                for (int i = 0; i < normal.Owner.Length; i++)
                {
                    if (normal.Owner[i] != broken.Owner[i])
                    {
                        ownerChanged++;
                    }
                }

                Debug.Log($"  [Step12] {item.Fault}: 層の持ち主が変わった画素 {ownerChanged}"
                          + $" / {normal.Owner.Length}");

                // **動かない測定は壊れを検出できていない。**
                // 下限は「画面の 0.04 % 以上」。実測では深度クリアを外したときが
                // いちばん小さく 1,120 px（230,400 画素中 0.49 %）だった。
                Assert.That(ownerChanged, Is.GreaterThan(normal.Owner.Length / 2500),
                            $"{item.Fault} を入れても絵が変わらない（測定が壊れを見ていない）");
            }
        }

        [UnityTest]
        public IEnumerator 計器の画面へ数値を出せる()
        {
            yield return null;
            RequireDiagnostics();

            if (_screens == null || _screens.Screens.Count == 0)
            {
                Assert.Inconclusive("計器の画面が無い（箱コックピット）");
            }

            _diagnostics.SetProbesVisible(true);
            _diagnostics.Measure();
            _diagnostics.SetScreenDiagnostics(true);

            Assert.That(_screens.DiagnosticsEnabled, Is.True);

            bool found = false;
            foreach (CockpitScreens.Screen screen in _screens.Screens)
            {
                if (screen.DiagnosticsText == null)
                {
                    continue;
                }

                found = true;
                Assert.That(screen.DiagnosticsText.text, Does.Contain("XR DIAG"),
                            "数値が差されていない");
            }

            Assert.That(found, Is.True, "診断を出せる面が 1 つも無い");

            _diagnostics.SetScreenDiagnostics(false);
            Assert.That(_screens.DiagnosticsEnabled, Is.False);
        }

        [UnityTest]
        public IEnumerator 計器盤の画素数が想定の範囲にある()
        {
            yield return null;
            RequireDiagnostics();

            // **窓を「盤の外」で定義しているので、内装が消えると窓が画面全体に
            // 広がり、漏れが 0 になって「正常」に見えてしまう。**
            // 盤の面積そのものを不変条件にする。
            XrDiagnosticsResult r = _diagnostics.Measure();
            int total = r.Width * r.Height;

            Debug.Log($"  [Step12] 盤 {r.Leak.PanelPixels} px / {total} px"
                      + $" = {100.0 * r.Leak.PanelPixels / total:F2} %");

            Assert.That(XrDiagnosticsModel.PanelWithinBudget(r.Leak.PanelPixels, total),
                        Is.True,
                        $"計器盤が想定の範囲外 ({r.Leak.PanelPixels} px / {total} px)");
        }

        [UnityTest]
        public IEnumerator 同じ絵の左右比は1になる()
        {
            yield return null;
            RequireDiagnostics();

            _diagnostics.SetProbesVisible(true);
            XrDiagnosticsResult r = _diagnostics.Measure();

            // 平面では左右が無いので同じ絵を 2 枚渡す。**厳密に 1.0。**
            System.Collections.Generic.List<XrDiagnosticsModel.StereoProbeHit> stereo =
                XrDiagnosticsModel.MeasureStereo(r.Frame, r.Frame, r.Width, r.Height,
                                                 r.ProbeMask, r.ProbeMask);

            foreach (XrDiagnosticsModel.StereoProbeHit hit in stereo)
            {
                Assert.That(hit.Ratio, Is.EqualTo(1.0).Within(0.0), hit.ToString());
            }
        }

        [UnityTest]
        public IEnumerator 片目の故意破壊は平面では絵を変えない()
        {
            yield return null;
            RequireDiagnostics();

            _diagnostics.SetProbesVisible(true);
            XrDiagnosticsResult normal = Snapshot();

            foreach (XrDiagnostics.Fault fault in new[]
                     {
                         XrDiagnostics.Fault.DropLayerInOneEye,
                         XrDiagnostics.Fault.SkipDepthClearInOneEye,
                     })
            {
                _diagnostics.SetFault(fault, XrLayer.Cockpit, XrDiagnostics.Eye.Left);
                XrDiagnosticsResult after = Snapshot();
                _diagnostics.ClearFault();

                Assert.That(_diagnostics.PerEyeFaultApplied, Is.False);

                for (int i = 0; i < normal.Probes.Count; i++)
                {
                    Assert.That(after.Probes[i].Count, Is.EqualTo(normal.Probes[i].Count),
                                $"{fault} が平面で効いてしまっている");
                }
            }
        }

        XrDiagnosticsResult Snapshot()
        {
            _root.Tick(UniverseConstants.FixedDeltaSeconds);
            return _diagnostics.Measure();
        }
    }
}
