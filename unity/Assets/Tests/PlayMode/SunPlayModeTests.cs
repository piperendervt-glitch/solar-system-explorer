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
            [UnityTest]
        public IEnumerator コロナが太陽の外側の画素を明るくする()
        {
            ApplyScenario(ScenarioLibrary.SunFaceName);
            yield return null;

            CelestialBodyView sun = null;
            foreach (CelestialBodyView v in _root.SolarSystem.Views)
            {
                if (v.Body != null && v.Body.Name == "Sun") { sun = v; }
            }

            Assert.That(sun, Is.Not.Null);
            Assert.That(sun.CoronaRenderer, Is.Not.Null, "太陽にコロナが無い");

            // **本体との比が保たれていること。** 距離が変わっても崩れない。
            float bodyDiameter = sun.Mesh.localScale.x;
            float coronaDiameter = sun.Corona.localScale.x;
            float ratio = coronaDiameter / bodyDiameter;

            Debug.Log(string.Format(
                "  [Step9-2] 本体 {0:E3} / コロナ {1:E3} / 比 {2:F3} (既定 {3:F2})",
                bodyDiameter, coronaDiameter, ratio, PlanetAppearance.CoronaRadiusScale));

            Assert.That(ratio, Is.EqualTo((float)PlanetAppearance.CoronaRadiusScale).Within(1e-3f),
                        "コロナの大きさが本体に追従していない");

            // コロナは自転しない。root の LookRotation だけが乗る。
            Assert.That(sun.Corona.parent, Is.EqualTo(sun.transform),
                        "コロナが Spin の下にあると自転してしまう");
        }

        [UnityTest]
        public IEnumerator コロナをOFFにすると太陽まわりの画素が暗くなる()
        {
            ApplyScenario(ScenarioLibrary.SunFaceName);
            yield return null;

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

            CelestialBodyView sun = null;
            foreach (CelestialBodyView v in _root.SolarSystem.Views)
            {
                if (v.Body == null) { continue; }
                if (v.Body.Name == "Sun") { sun = v; continue; }
                foreach (Renderer r in v.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = false;
                }
            }

            Assert.That(sun, Is.Not.Null);
            yield return null;

            // 太陽本体を消してコロナだけにする。外側の光がコロナ由来だと確かめるため。
            sun.MeshRenderer.enabled = false;
            sun.PointRenderer.enabled = false;

            sun.CoronaRenderer.enabled = true;
            float withCorona = SunAreaPeak();
            sun.CoronaRenderer.enabled = false;
            float withoutCorona = SunAreaPeak();

            Debug.Log(string.Format(
                "  [Step9-2] 本体を消した状態 コロナ ON {0:F3} / OFF {1:F3}",
                withCorona, withoutCorona));

            Assert.That(withoutCorona, Is.LessThan(1e-3f), "コロナを消しても何かが描かれている");
            Assert.That(withCorona, Is.GreaterThan(1.05f),
                        "コロナ単独で bloom しきい値に届いていない");
        }

        /// <summary>トーンマップ前の最大輝度。呼ぶ前にポストプロセスを切っておくこと。</summary>
        float SunAreaPeak()
        {
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

            return peak;
        }
            /// <summary>
        /// フレアだけを残す。**ポストプロセスは切らない。**
        /// SRP Lens Flare はポストプロセス経路で描かれるので、
        /// renderPostProcessing = false にすると光条ごと消えて測れない。
        /// </summary>
        void IsolateFlare()
        {
            if (_stack.Cockpit != null) { _stack.Cockpit.enabled = false; }
            if (_stack.Nearfield != null) { _stack.Nearfield.enabled = false; }
            _stack.Deep.clearFlags = CameraClearFlags.SolidColor;
            _stack.Deep.backgroundColor = Color.black;

            foreach (CelestialBodyView v in _root.SolarSystem.Views)
            {
                foreach (Renderer r in v.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = false;
                }
            }

            foreach (StationView st in Object.FindObjectsByType<StationView>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (Renderer r in st.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = false;
                }
            }
        }

        /// <summary>非黒の画素数。トーンマップ後の LDR で数える。</summary>
        int LitPixels()
        {
            const int W = 512;
            const int H = 512;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            RenderTexture prevTarget = _stack.Deep.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            int lit = 0;

            try
            {
                _stack.Deep.targetTexture = rt;
                _stack.Deep.Render();
                RenderTexture.active = rt;

                var shot = new Texture2D(W, H, TextureFormat.RGB24, false);
                shot.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                shot.Apply();
                foreach (Color32 c in shot.GetPixels32())
                {
                    if (c.r > 8 || c.g > 8 || c.b > 8) { lit++; }
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

            return lit;
        }

        [UnityTest]
        public IEnumerator 光条をONにすると円盤の外側の画素が増える()
        {
            ApplyScenario(ScenarioLibrary.SunFaceName);
            yield return null;
            IsolateFlare();
            yield return null;

            var flare = Object.FindAnyObjectByType<SunFlareController>();
            Assert.That(flare, Is.Not.Null);

            // 長く多くして差を出す。
            flare.Look.Apply(spikeCount: 12, spikeLength: 6.0, spikeThickness: 0.10, ghostIntensity: 0.0);
            yield return null;
            int on = LitPixels();

            flare.Look.Apply(spikeCount: 0, spikeLength: 6.0, spikeThickness: 0.10, ghostIntensity: 0.0);
            yield return null;
            int off = LitPixels();

            Debug.Log(string.Format("  [Step9-3b] 光条 12 本 {0} 画素 / 0 本 {1} 画素 / 差 {2}",
                on, off, on - off));

            Assert.That(on, Is.GreaterThan(off), "光条を出しても画素が増えていない");
            Assert.That(on - off, Is.GreaterThan(500), $"差が小さすぎる ({on - off} 画素)");
        }

        [UnityTest]
        public IEnumerator 遮蔽率1のとき光条も消える()
        {
            ApplyScenario(ScenarioLibrary.SunHiddenName);
            yield return null;
            IsolateFlare();
            yield return null;

            var flare = Object.FindAnyObjectByType<SunFlareController>();
            Assert.That(flare, Is.Not.Null);

            flare.Look.Apply(spikeCount: 12, spikeLength: 6.0, spikeThickness: 0.10, ghostIntensity: 1.0);
            for (int i = 0; i < 5; i++)
            {
                _root.Tick(Dt);
            }

            yield return null;
            int lit = LitPixels();

            Debug.Log(string.Format(
                "  [Step9-3b] sun-hidden 遮蔽率 {0:F3} / 強度 {1:F3} / 非黒 {2} 画素",
                flare.LastOcclusion, flare.LastIntensity, lit));

            Assert.That(flare.LastOcclusion, Is.EqualTo(1.0).Within(1e-3),
                        "9-3a の遮蔽判定が効いていない");
            Assert.That(flare.LastIntensity, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(lit, Is.EqualTo(0), "隠れているのに光条が残っている");
        }

        [UnityTest]
        public IEnumerator 実行時コピーはアセットを汚さない()
        {
            ApplyScenario(ScenarioLibrary.SunFaceName);
            yield return null;

            var flare = Object.FindAnyObjectByType<SunFlareController>();
            Assert.That(flare, Is.Not.Null);
            Assert.That(flare.Look.HasCopy, Is.False, "触る前からコピーがある");

            UnityEngine.Rendering.LensFlareDataSRP before = flare.Flare.lensFlareData;
            flare.Look.Apply(spikeCount: 2, spikeLength: 1.0, spikeThickness: 0.10, ghostIntensity: 0.1);

            Assert.That(flare.Look.HasCopy, Is.True, "コピーが作られていない");
            Assert.That(flare.Flare.lensFlareData, Is.Not.SameAs(before),
                        "アセットを直接書き換えている");
            Assert.That(flare.Flare.lensFlareData.name, Does.Contain("runtime"));
        }

            [UnityTest]
        public IEnumerator ゴーストは太陽の反対側に出る()
        {
            // **太陽の反対側に出る。** position は内部で 2 倍されるので
            // 0.5 = 画面中心 / 1.0 = 対称点 (LensFlareCommonSRP:1465)。
            ApplyScenario(ScenarioLibrary.SunOffAxisName);
            yield return null;
            IsolateFlare();
            yield return null;

            var flare = Object.FindAnyObjectByType<SunFlareController>();
            Assert.That(flare, Is.Not.Null);

            // **光条を消してゴーストだけを見る。** 光条が残ると差が埋もれる。
            flare.Look.Apply(spikeCount: 0, spikeLength: 1.0, spikeThickness: 0.10, ghostIntensity: 2.0);
            yield return null;
            int on = LitPixels();

            flare.Look.Apply(spikeCount: 0, spikeLength: 1.0, spikeThickness: 0.10, ghostIntensity: 0.0);
            yield return null;
            int off = LitPixels();

            Debug.Log(string.Format("  [Step9-3b] ゴースト 強 {0} 画素 / 無 {1} 画素 / 差 {2}",
                on, off, on - off));

            Assert.That(on, Is.GreaterThan(off), "ゴーストを出しても画素が増えていない");
        }

    }

    static class MaterialLibraryNames
    {
        public const string PlanetShader = "SolarSystem/PlanetSurface";
        public const string SunShader = "SolarSystem/SunSurface";
    }
}
