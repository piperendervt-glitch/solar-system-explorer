using System.Collections;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using SolarSystem.Core;
using SolarSystem.Unity;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SolarSystem.Tests.PlayMode
{
    /// <summary>太陽の HDR 化 (Step 9-1) の PlayMode 検証。</summary>
    public sealed class SunPlayModeTests
    {
        const double Dt = UniverseConstants.FixedDeltaSeconds;

        UniverseRoot _root;
        ShipRig _rig;
        CameraStackController _stack;
        ScenarioRunner _runner;
        DebugPanel _panel;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SaveFile.OverridePath = Path.Combine(Path.GetTempPath(), "solar-system-explorer-sun.save.json");
            SaveFile.Delete();

            SceneManager.LoadScene("Main", LoadSceneMode.Single);
            yield return null;
            yield return null;

            _root = Object.FindAnyObjectByType<UniverseRoot>();
            _rig = Object.FindAnyObjectByType<ShipRig>();
            _stack = Object.FindAnyObjectByType<CameraStackController>();
            _runner = Object.FindAnyObjectByType<ScenarioRunner>();
            _panel = Object.FindAnyObjectByType<DebugPanel>();
            Assert.That(_runner, Is.Not.Null);
            Assert.That(_panel, Is.Not.Null);
        }

        [TearDown]
        public void TearDown()
        {
            SaveFile.Delete();
            SaveFile.OverridePath = null;
        }

        void ApplyScenario(string name)
        {
            Assert.That(_runner.Select(_root.Model, name), Is.True, name + " が無い");
            _runner.Apply(_root, _rig, _stack, Object.FindAnyObjectByType<SolarSystem.Unity.DebugOverlay>());
            for (int i = 0; i < 20; i++)
            {
                _root.Tick(Dt);
            }
        }

        static string[] RowFor(DebugPanel panel, string bodyName)
        {
            foreach (string[] row in panel.BuildBodyRows())
            {
                if (row[0] == bodyName)
                {
                    return row;
                }
            }

            return null;
        }

        static void SplitBbox(string text, out double w, out double h)
        {
            string[] parts = text.Split(new[] { "x" }, System.StringSplitOptions.None);
            w = double.Parse(parts[0], CultureInfo.InvariantCulture);
            h = double.Parse(parts[1], CultureInfo.InvariantCulture);
        }

        [UnityTest]
        public IEnumerator sun_faceで太陽のbboxが計算値と一致する()
        {
            ApplyScenario(ScenarioLibrary.SunFaceName);
            yield return null;

            string[] row = RowFor(_panel, "Sun");
            Assert.That(row, Is.Not.Null, "太陽の行が無い");
            Assert.That(row[5], Does.Contain("殻"), "太陽が殻で描かれていない: " + row[5]);
            Assert.That(row[3], Is.Not.EqualTo("---"), "bbox が測れていない");

            double computed = double.Parse(row[2], CultureInfo.InvariantCulture);
            SplitBbox(row[3], out double w, out double h);

            // **頂点を 256 点に間引いているので数 % 小さく出る。**
            // 小さい円盤では 1px の丸めも効くので絶対値の余裕も要る。
            double tolerance = System.Math.Max(1.5, computed * 0.05);
            Debug.Log(string.Format(
                "  [Step9-1] 画面 {0}x{1} / 太陽 計算 {2:F2} px / 実測 {3} / 許容 +-{4:F2}",
                Screen.width, Screen.height, computed, row[3], tolerance));

            Assert.That(w, Is.EqualTo(computed).Within(tolerance), "bbox の幅が計算と合わない");
            Assert.That(h, Is.EqualTo(computed).Within(tolerance), "bbox の高さが計算と合わない");
        }

        [UnityTest]
        public IEnumerator earth_close_dayの見た目が変わっていない()
        {
            ApplyScenario(ScenarioLibrary.EarthDayName);
            yield return null;

            string[] earth = RowFor(_panel, "Earth");
            Assert.That(earth, Is.Not.Null);
            Assert.That(earth[5], Does.Contain("実"), "地球が実スケールで描かれていない: " + earth[5]);

            double computed = double.Parse(earth[2], CultureInfo.InvariantCulture);
            SplitBbox(earth[3], out double w, out double _);

            Debug.Log(string.Format(
                "  [Step9-1] 回帰 earth-close-day 地球 計算 {0:F2} px / 実測 {1} / 引き渡し {2}",
                computed, earth[3], earth[4]));

            Assert.That(w, Is.EqualTo(computed).Within(System.Math.Max(1.5, computed * 0.05)),
                        "地球の見え方が変わっている");
            Assert.That(double.Parse(earth[4], CultureInfo.InvariantCulture),
                        Is.EqualTo(1.0).Within(1e-3), "引き渡し率が変わっている");

            // 地表マテリアルが太陽の変更に巻き込まれていないこと。
            Material surface = null;
            foreach (CelestialBodyView v in _root.SolarSystem.Views)
            {
                if (v.Body != null && v.Body.Name == "Earth" && v.RealMeshRenderer != null)
                {
                    surface = v.RealMeshRenderer.sharedMaterial;
                }
            }

            Assert.That(surface, Is.Not.Null);
            Assert.That(surface.shader.name, Is.EqualTo(MaterialLibraryNames.PlanetShader),
                        "地球のシェーダが差し替わっている");
            Assert.That(surface.GetFloat("_EmissionIntensity"), Is.EqualTo(1.0f).Within(1e-4f),
                        "惑星の発光強度が太陽の変更に巻き込まれている");
        }

        [UnityTest]
        public IEnumerator 太陽の出力がbloomしきい値を超える()
        {
            ApplyScenario(ScenarioLibrary.SunFaceName);
            yield return null;

            // **カメラの renderPostProcessing を切る。ここが肝。**
            // Volume.enabled = false だけでは ACES が残り、2.4 も 9.6 も
            // 0.59 / 0.63 に潰れて「強度を変えても絵が変わらない」ように見える。
            // bloom が見ているのはトーンマップ**前**の値なので、そこを測る。
            foreach (UnityEngine.Rendering.Volume vol in Object.FindObjectsByType<
                         UnityEngine.Rendering.Volume>(FindObjectsInactive.Include,
                                                       FindObjectsSortMode.None))
            {
                vol.enabled = false;
            }

            foreach (Camera cam in new[] { _stack.Deep, _stack.Near, _stack.Nearfield, _stack.Cockpit })
            {
                if (cam == null) { continue; }
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null) { data.renderPostProcessing = false; }
            }

            // 太陽だけを残す。非黒の画素が太陽であることを測定の前提にしない。
            if (_stack.Cockpit != null) { _stack.Cockpit.enabled = false; }
            if (_stack.Nearfield != null) { _stack.Nearfield.enabled = false; }
            _stack.Deep.clearFlags = CameraClearFlags.SolidColor;
            _stack.Deep.backgroundColor = Color.black;

            var flare = Object.FindAnyObjectByType<SunFlareController>();
            if (flare != null)
            {
                var lf = flare.GetComponentInChildren<
                    UnityEngine.Rendering.LensFlareComponentSRP>(true);
                if (lf != null) { lf.enabled = false; }
            }

            foreach (CelestialBodyView v in _root.SolarSystem.Views)
            {
                if (v.Body == null || v.Body.Name == "Sun") { continue; }
                foreach (Renderer r in v.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = false;
                }
            }

            yield return null;

            const int W = 512;
            const int H = 512;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGBHalf);
            rt.Create();
            RenderTexture prevTarget = _stack.Deep.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            float peak = 0f;

            try
            {
                _stack.Deep.targetTexture = rt;
                _stack.Deep.Render();
                RenderTexture.active = rt;

                var shot = new Texture2D(W, H, TextureFormat.RGBAHalf, false);
                shot.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                shot.Apply();
                foreach (Color c in shot.GetPixels())
                {
                    float lum = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    if (lum > peak) { peak = lum; }
                }

                Object.DestroyImmediate(shot);
            }
            finally
            {
                _stack.Deep.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            float threshold = PostProcessPreset.Values(PostProcessStrength.Medium).bloomThreshold;

            Debug.Log(string.Format(
                "  [Step9-1] 太陽のトーンマップ前の最大輝度 {0:F3} / bloom しきい値 {1:F2}",
                peak, threshold));

            Assert.That(peak, Is.GreaterThan(threshold),
                        "太陽の出力が bloom しきい値を超えていない");
        }
    }

    /// <summary>Editor アセンブリを参照せずにシェーダ名を照合するための定数。</summary>
    static class MaterialLibraryNames
    {
        public const string PlanetShader = "SolarSystem/PlanetSurface";
        public const string SunShader = "SolarSystem/SunSurface";
    }
}
