using System.Collections;
using System.IO;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SolarSystem.Tests.PlayMode
{
    /// <summary>
    /// bloom が実行時に効いていること (Step 9-4)。
    ///
    /// **Step 6 の事故の再発防止がこのファイルの主目的。**
    /// 当時は Apply を Editor からしか呼ばず、アセットへ保存もしなかったので、
    /// シーンをロードすると Bloom の既定値 (intensity 0 = 消灯) で動いていた。
    /// </summary>
    public sealed class BloomPlayModeTests
    {
        const double Dt = UniverseConstants.FixedDeltaSeconds;
        const int W = 512;
        const int H = 512;

        UniverseRoot _root;
        ShipRig _rig;
        CameraStackController _stack;
        ScenarioRunner _runner;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath = Path.Combine(Path.GetTempPath(), "solar-system-explorer-bloom.save.json");
            SaveFile.Delete();
            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _root = Object.FindAnyObjectByType<UniverseRoot>();
            _rig = Object.FindAnyObjectByType<ShipRig>();
            _stack = Object.FindAnyObjectByType<CameraStackController>();
            _runner = Object.FindAnyObjectByType<ScenarioRunner>();
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        static VolumeProfile Profile()
        {
            var preset = Object.FindAnyObjectByType<PostProcessPreset>();
            if (preset == null || preset.Volume == null)
            {
                return null;
            }

            return preset.Volume.sharedProfile != null
                ? preset.Volume.sharedProfile : preset.Volume.profile;
        }

        [UnityTest]
        public IEnumerator シーンをロードしただけでbloomが有効になっている()
        {
            yield return null;

            VolumeProfile profile = Profile();
            Assert.That(profile, Is.Not.Null, "Volume のプロファイルが無い");
            Assert.That(profile.TryGet(out Bloom bloom), Is.True, "Bloom が Volume に無い");

            Debug.Log(string.Format(
                "  [Step9-4] 実行時の Bloom active={0} intensity={1:F3} threshold={2:F3} scatter={3:F3}",
                bloom.active, bloom.intensity.value, bloom.threshold.value, bloom.scatter.value));

            // **ここが本丸。** 0 だと bloom は一切効かない (Step 6 の状態)。
            Assert.That(bloom.active, Is.True, "Bloom が無効");
            Assert.That(bloom.intensity.value, Is.GreaterThan(0f),
                        "bloom が消灯している。PostProcessPreset.Awake が走っていない可能性");
            Assert.That(bloom.intensity.value,
                        Is.EqualTo((float)PlanetAppearance.BloomIntensity).Within(1e-3f),
                        "Core の定数と食い違っている");
            Assert.That(bloom.threshold.value,
                        Is.EqualTo((float)PlanetAppearance.BloomThreshold).Within(1e-3f));
        }

        [UnityTest]
        public IEnumerator 四段すべてでポストプロセスが有効になっている()
        {
            yield return null;

            foreach (Camera cam in new[] { _stack.Deep, _stack.Near, _stack.Nearfield, _stack.Cockpit })
            {
                Assert.That(cam, Is.Not.Null);
                var data = cam.GetUniversalAdditionalCameraData();
                Assert.That(data.renderPostProcessing, Is.True, cam.name + " でポストプロセスが無効");
            }

            // **URP はスタックの最後のカメラの設定で一度だけ適用する。**
            // Overlay 3 段の値は実効的に効かないが、有害でもないので触っていない
            // (計画書 9-4 に記録)。
            var baseData = _stack.Deep.GetUniversalAdditionalCameraData();
            Assert.That(baseData.renderType, Is.EqualTo(CameraRenderType.Base));
            Assert.That(baseData.cameraStack.Count, Is.EqualTo(3));
            Assert.That(baseData.cameraStack[2], Is.EqualTo(_stack.Cockpit),
                        "スタックの最後が Cockpit でない。ここの設定が実効値になる");
        }

        // **bloom の「滲みが増えたか」は PlayMode では測っていない。**
        //
        // Camera.Render() -> RenderTexture の経路では、強度 0.00 と 0.80 で
        // 太陽まわりの明部が 52 画素と 52 画素、差 0 だった（実測）。
        // トーンマップ（ACES）は同じ経路で効いているので、ポストプロセス自体は
        // 走っている。**bloom だけがこの経路に出ない理由は特定していない。**
        //
        // 滲みの確認は exe のスクショで行う（CLAUDE.md 0-B）。
        // ここで縛るのは「実行時に bloom が有効で、値が Core の定数と一致する」
        // ことまで。Step 6 の事故（intensity 0 のまま気づかない）はこれで捕まる。
    }
}
