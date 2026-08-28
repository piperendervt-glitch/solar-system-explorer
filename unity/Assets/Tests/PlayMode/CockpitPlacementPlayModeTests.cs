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
    /// 組まれたシーンでコックピットが差し替わっていること (Step 11-2)。
    ///
    /// ■ **アセットを持たないクローンでは Inconclusive にする。**
    /// `Main.unity` は追跡ファイルで、**プレハブのリンク（GUID）だけ**が載っている。
    /// アセットを持たないマシンではその参照が解決できず、レンダラーが 0 になる。
    /// それは「壊れている」のではなく「まだ取り込んでいない」なので、
    /// **落とさずに「確かめられなかった」と出す。**
    /// `run_unity.ps1 -Method SolarSetup.Run` でシーンを組み直すと箱に落ちる。
    /// </summary>
    public sealed class CockpitPlacementPlayModeTests
    {
        CockpitIdentity _identity;
        CockpitMetrics _metrics;

        /// <summary>箱の見込みレンダラー数（枠 4 + 背面 + 床 + 計器面 = 7）。</summary>
        const int BoxRendererCount = 7;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath = Path.Combine(Path.GetTempPath(),
                                                 "solar-system-explorer-placement.save.json");
            SaveFile.Delete();
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _identity = Object.FindAnyObjectByType<CockpitIdentity>();
            _metrics = Object.FindAnyObjectByType<CockpitMetrics>();
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        int RendererCount() =>
            _identity == null ? 0 : _identity.GetComponentsInChildren<Renderer>(true).Length;

        /// <summary>hirez で組まれているのに実体が無いなら、取り込んでいないだけ。</summary>
        void RequireResolvedPrefab()
        {
            Assert.That(_identity, Is.Not.Null, "CockpitIdentity がシーンに無い");

            if (_identity.DefinitionId == CockpitDefinition.HiRezSampleId
                && RendererCount() <= BoxRendererCount)
            {
                Assert.Inconclusive(
                    "シーンは hirez-sample で組まれているが、このマシンにアセットが無いため"
                    + "プレハブが解決できていない。SolarSetup.Run で組み直すと箱に落ちる。");
            }
        }

        [UnityTest]
        public IEnumerator 実アセットで組まれレンダラーが箱より多い()
        {
            yield return null;
            RequireResolvedPrefab();

            if (_identity.DefinitionId == CockpitDefinition.BoxId)
            {
                Assert.Inconclusive("箱で組まれている（取り込みが無い環境では正常）");
            }

            int renderers = RendererCount();
            Assert.That(renderers, Is.GreaterThan(BoxRendererCount),
                        "箱より部品が少ない。差し替わっていない");

            Debug.Log($"  [Step11-2a] コックピット {_identity.DefinitionId} "
                      + $"/ レンダラー {renderers} 個（箱は {BoxRendererCount} 個）");
        }

        [UnityTest]
        public IEnumerator 窓の投影面積比がログに出る()
        {
            yield return null;
            RequireResolvedPrefab();

            Assert.That(_metrics, Is.Not.Null, "CockpitMetrics がシーンに無い");

            // **観測経路そのものを確かめる。** batchmode では絵を見られないので、
            // 「測った値がログに出ること」が唯一の確認手段になる。
            string line = _metrics.Describe();
            Assert.That(line, Does.Contain("窓の投影面積比"));
            Debug.Log("  [Step11-2b] " + line);
        }

        [UnityTest]
        public IEnumerator 窓の投影面積比が正の値になる()
        {
            yield return null;
            RequireResolvedPrefab();

            if (_identity.DefinitionId == CockpitDefinition.BoxId)
            {
                Assert.Inconclusive("箱には窓が無い");
            }

            double ratio = _metrics.Measure(out int sampled, out int renderers, out int behind);

            Assert.That(ratio, Is.GreaterThan(0.0),
                        "窓が測れていない（カメラの後ろにある / レンダラーが無い）");
            Assert.That(renderers, Is.GreaterThan(0));
            Assert.That(sampled, Is.GreaterThan(2), "投影した頂点が少なすぎる");

            // **後方の点があること自体は異常ではない。** キャノピーは目より後ろへ
            // 回り込む。ログに出しているので、数が極端なら目の位置を疑える。
            Debug.Log($"  [Step11-2b] 面積比 {ratio * 100.0:F1} % / 頂点 {sampled} / 後方除外 {behind}");
        }

        [UnityTest]
        public IEnumerator 面積比は画面のアスペクトに依存しない()
        {
            yield return null;
            RequireResolvedPrefab();

            if (_identity.DefinitionId == CockpitDefinition.BoxId)
            {
                Assert.Inconclusive("箱には窓が無い");
            }

            // **実機で踏んだ食い違いの再発防止。**
            // 同じ目の位置で 640x480 では 10.4 %、1920x1080 では 7.8 % と出ていた。
            // 測定条件を 1920x1080 に固定したので、カメラのアスペクトを変えても
            // 答えは動かないはず。
            Camera cockpit = Object.FindAnyObjectByType<CameraStackController>().Cockpit;
            float previous = cockpit.aspect;

            try
            {
                cockpit.aspect = 4f / 3f;
                double fourThree = _metrics.Measure(out int _, out int _, out int _);

                cockpit.aspect = 21f / 9f;
                double ultraWide = _metrics.Measure(out int _, out int _, out int _);

                Assert.That(fourThree, Is.EqualTo(ultraWide).Within(1e-9),
                            "画面の形で面積比が変わっている（測定条件が固定できていない）");

                Debug.Log($"  [Step11-2b] 面積比 {fourThree * 100.0:F1} % "
                          + $"(4:3 と 21:9 で一致 / 基準 {CockpitMetrics.ReferenceWidth}x"
                          + $"{CockpitMetrics.ReferenceHeight})");
            }
            finally
            {
                cockpit.ResetAspect();
                cockpit.aspect = previous;
            }
        }

        [UnityTest]
        public IEnumerator 測定条件がログに併記される()
        {
            yield return null;
            RequireResolvedPrefab();

            // **数字だけでは比べられない。** 11-5 で無料と有料を比べるときに、
            // どの条件で測った値なのかがログから読めること。
            string line = _metrics.Describe();
            Assert.That(line, Does.Contain($"基準 {CockpitMetrics.ReferenceWidth}x"
                                           + $"{CockpitMetrics.ReferenceHeight}"));
            Assert.That(line, Does.Contain("画角"));
        }

        [UnityTest]
        public IEnumerator コックピット段が他の段を隠していない()
        {
            yield return null;
            RequireResolvedPrefab();

            var stack = Object.FindAnyObjectByType<CameraStackController>();
            Assert.That(stack, Is.Not.Null);

            // **深度クリアの Overlay なので、背景を塗ってはいけない。**
            // 塗ると外の景色が消える (11-2d)。
            Assert.That(stack.Cockpit.clearFlags, Is.Not.EqualTo(CameraClearFlags.Skybox));
            Assert.That(stack.Cockpit.clearFlags, Is.Not.EqualTo(CameraClearFlags.SolidColor));
        }
    }
}
